using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Astral.Core.WebProxy;

var options = ProxyOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: Astral.WebProxy --port <port> --allow <domain> [--allow <domain>...]");
    return 2;
}

var policy = new WebProxyAccessPolicy(options.AllowedPatterns);
var listener = new TcpListener(IPAddress.Loopback, options.Port);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    listener.Start();
    Console.WriteLine(
        $"Astral.WebProxy listening on 127.0.0.1:{options.Port.ToString(CultureInfo.InvariantCulture)}");
}
catch (SocketException exception)
    when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse)
{
    Console.Error.WriteLine(
        "Astral.WebProxy local port is already in use: " +
        options.Port.ToString(CultureInfo.InvariantCulture));
    return 72;
}

try
{
    while (!shutdown.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(shutdown.Token);
        _ = Task.Run(
            () => HandleClientAsync(client, policy, shutdown.Token),
            shutdown.Token);
    }
}
catch (OperationCanceledException)
{
}
finally
{
    listener.Stop();
}

return 0;

static async Task HandleClientAsync(
    TcpClient client,
    WebProxyAccessPolicy policy,
    CancellationToken cancellationToken)
{
    using (client)
    {
        client.NoDelay = true;
        await using var clientStream = client.GetStream();
        var headerBytes = await ReadHeaderAsync(clientStream, cancellationToken);
        if (headerBytes.Length == 0)
        {
            return;
        }

        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);
        var requestLine = lines.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            await WriteProxyResponseAsync(clientStream, 400, cancellationToken);
            return;
        }

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            await WriteProxyResponseAsync(clientStream, 400, cancellationToken);
            return;
        }

        var method = parts[0];
        if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            await HandleConnectAsync(
                parts[1],
                policy,
                clientStream,
                cancellationToken);
            return;
        }

        await HandleHttpAsync(
            parts[1],
            lines,
            headerBytes,
            policy,
            clientStream,
            cancellationToken);
    }
}

static async Task HandleConnectAsync(
    string target,
    WebProxyAccessPolicy policy,
    Stream clientStream,
    CancellationToken cancellationToken)
{
    if (!TryParseHostPort(target, defaultPort: 443, out var host, out var port)
        || !policy.IsAllowedHost(host))
    {
        WriteDeniedHost("CONNECT", host);
        await WriteProxyResponseAsync(clientStream, 403, cancellationToken);
        return;
    }

    using var upstream = new TcpClient();
    await upstream.ConnectAsync(host, port, cancellationToken);
    await using var upstreamStream = upstream.GetStream();
    await WriteRawAsync(
        clientStream,
        "HTTP/1.1 200 Connection Established\r\nProxy-Agent: Astral.WebProxy\r\n\r\n",
        cancellationToken);
    await RelayAsync(clientStream, upstreamStream, cancellationToken);
}

static async Task HandleHttpAsync(
    string requestTarget,
    IReadOnlyList<string> lines,
    byte[] headerBytes,
    WebProxyAccessPolicy policy,
    Stream clientStream,
    CancellationToken cancellationToken)
{
    var hostHeader = lines
        .FirstOrDefault(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
    var hostValue = hostHeader?["Host:".Length..].Trim();
    var targetHost = ResolveHttpHost(requestTarget, hostValue, out var port);
    if (targetHost is null || !policy.IsAllowedHost(targetHost))
    {
        WriteDeniedHost("HTTP", targetHost);
        await WriteProxyResponseAsync(clientStream, 403, cancellationToken);
        return;
    }

    using var upstream = new TcpClient();
    await upstream.ConnectAsync(targetHost, port, cancellationToken);
    await using var upstreamStream = upstream.GetStream();
    await upstreamStream.WriteAsync(headerBytes, cancellationToken);
    await RelayAsync(clientStream, upstreamStream, cancellationToken);
}

static async Task<byte[]> ReadHeaderAsync(
    Stream stream,
    CancellationToken cancellationToken)
{
    const int maxHeaderBytes = 32 * 1024;
    var buffer = new byte[1024];
    using var memory = new MemoryStream();
    while (memory.Length < maxHeaderBytes)
    {
        var read = await stream.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            break;
        }

        memory.Write(buffer, 0, read);
        var current = memory.ToArray();
        if (EndsHeader(current))
        {
            return current;
        }
    }

    return [];
}

static bool EndsHeader(byte[] value)
{
    return value.Length >= 4
        && value.AsSpan(value.Length - 4).SequenceEqual("\r\n\r\n"u8);
}

static async Task RelayAsync(
    Stream clientStream,
    Stream upstreamStream,
    CancellationToken cancellationToken)
{
    var clientToServer = clientStream.CopyToAsync(upstreamStream, cancellationToken);
    var serverToClient = upstreamStream.CopyToAsync(clientStream, cancellationToken);
    await Task.WhenAny(clientToServer, serverToClient);
}

static string? ResolveHttpHost(
    string requestTarget,
    string? hostHeader,
    out int port)
{
    port = 80;
    if (Uri.TryCreate(requestTarget, UriKind.Absolute, out var uri)
        && !string.IsNullOrWhiteSpace(uri.Host))
    {
        port = uri.Port > 0 ? uri.Port : 80;
        return uri.Host;
    }

    if (string.IsNullOrWhiteSpace(hostHeader))
    {
        return null;
    }

    return TryParseHostPort(hostHeader, defaultPort: 80, out var host, out port)
        ? host
        : null;
}

static bool TryParseHostPort(
    string value,
    int defaultPort,
    out string host,
    out int port)
{
    host = string.Empty;
    port = defaultPort;
    var candidate = value.Trim();
    var colonIndex = candidate.LastIndexOf(':');
    if (colonIndex > 0
        && candidate.IndexOf(':') == colonIndex
        && int.TryParse(
            candidate[(colonIndex + 1)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedPort))
    {
        host = candidate[..colonIndex];
        port = parsedPort;
    }
    else
    {
        host = candidate;
    }

    return !string.IsNullOrWhiteSpace(host)
        && port is > 0 and <= 65535;
}

static Task WriteProxyResponseAsync(
    Stream stream,
    int statusCode,
    CancellationToken cancellationToken)
{
    var reason = statusCode switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        _ => "Proxy Error"
    };
    return WriteRawAsync(
        stream,
        $"HTTP/1.1 {statusCode.ToString(CultureInfo.InvariantCulture)} {reason}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n",
        cancellationToken);
}

static Task WriteRawAsync(
    Stream stream,
    string value,
    CancellationToken cancellationToken)
{
    var bytes = Encoding.ASCII.GetBytes(value);
    return stream.WriteAsync(bytes, cancellationToken).AsTask();
}

static void WriteDeniedHost(string method, string? host)
{
    if (string.IsNullOrWhiteSpace(host))
    {
        Console.Error.WriteLine($"Astral.WebProxy denied {method}: invalid-host");
        return;
    }

    var safeHost = new string(host
        .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')
        .Take(253)
        .ToArray());
    if (string.IsNullOrWhiteSpace(safeHost))
    {
        safeHost = "invalid-host";
    }

    Console.Error.WriteLine($"Astral.WebProxy denied {method}: {safeHost}");
}

file sealed record ProxyOptions(
    int Port,
    IReadOnlyList<DomainPattern> AllowedPatterns)
{
    public static ProxyOptions? Parse(IReadOnlyList<string> args)
    {
        var port = 18088;
        var allowed = new List<DomainPattern>();
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Count
                && int.TryParse(
                    args[index + 1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPort))
            {
                port = parsedPort;
                index++;
                continue;
            }

            if (arg.Equals("--allow", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Count)
            {
                allowed.Add(DomainPattern.Parse(args[index + 1]));
                index++;
                continue;
            }

            return null;
        }

        if (port is <= 0 or > 65535 || allowed.Count == 0)
        {
            return null;
        }

        return new ProxyOptions(port, allowed);
    }
}
