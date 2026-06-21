using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    using var upstream = await TryConnectUpstreamAsync(
        "CONNECT",
        host,
        port,
        clientStream,
        cancellationToken);
    if (upstream is null)
    {
        return;
    }

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

    using var upstream = await TryConnectUpstreamAsync(
        "HTTP",
        targetHost,
        port,
        clientStream,
        cancellationToken);
    if (upstream is null)
    {
        return;
    }

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
        502 => "Bad Gateway",
        403 => "Forbidden",
        _ => "Proxy Error"
    };
    return WriteRawAsync(
        stream,
        $"HTTP/1.1 {statusCode.ToString(CultureInfo.InvariantCulture)} {reason}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n",
        cancellationToken);
}

static async Task<TcpClient?> TryConnectUpstreamAsync(
    string method,
    string host,
    int port,
    Stream clientStream,
    CancellationToken cancellationToken)
{
    const int UpstreamConnectTimeoutSeconds = 10;
    using var timeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(UpstreamConnectTimeoutSeconds));
    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        timeout.Token);
    try
    {
        return await UpstreamConnector.ConnectAsync(host, port, linkedCancellation.Token);
    }
    catch (Exception exception)
        when (exception is SocketException
            or IOException
            or InvalidOperationException
            or HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
    {
        WriteUpstreamFailure(method, host, exception);
        await WriteProxyResponseAsync(clientStream, 502, cancellationToken);
        return null;
    }
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

    Console.Error.WriteLine(
        $"Astral.WebProxy denied {method}: {ProxyLogSanitizer.SanitizeHost(host)}");
}

static void WriteUpstreamFailure(string method, string host, Exception exception)
{
    Console.Error.WriteLine(
        $"Astral.WebProxy upstream failed {method}: {ProxyLogSanitizer.SanitizeHost(host)} ({exception.GetType().Name})");
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

file static class UpstreamConnector
{
    private const string AllowPublicDnsFallbackEnvironmentVariable =
        "ASTRAL_WEBPROXY_ALLOW_PUBLIC_DNS_FALLBACK";
    private const ushort DnsRecordTypeA = 1;
    private const ushort DnsRecordTypeAaaa = 28;
    private const ushort DnsClassInternet = 1;
    private static readonly IPAddress[] FallbackDnsServers =
    [
        IPAddress.Parse("1.1.1.1"),
        IPAddress.Parse("8.8.8.8")
    ];

    private static readonly HttpClient DnsClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false
        })
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    public static async Task<TcpClient> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new TcpClient
            {
                NoDelay = true
            };
            await client.ConnectAsync(host, port, cancellationToken);
            return client;
        }
        catch (SocketException exception) when (ShouldTryResolverFallback(exception))
        {
            if (!IsPublicDnsFallbackEnabled())
            {
                throw;
            }

            var addresses = await ResolveBypassSystemDnsAsync(host, cancellationToken);
            if (addresses.Count == 0)
            {
                throw;
            }

            return await ConnectResolvedAddressAsync(
                host,
                addresses,
                port,
                cancellationToken);
        }
    }

    private static bool IsPublicDnsFallbackEnabled()
    {
        var value = Environment.GetEnvironmentVariable(
            AllowPublicDnsFallbackEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TcpClient> ConnectResolvedAddressAsync(
        string host,
        IReadOnlyList<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            var client = new TcpClient
            {
                NoDelay = true
            };
            try
            {
                await client.ConnectAsync(address, port, cancellationToken);
                Console.Error.WriteLine(
                    $"Astral.WebProxy upstream DNS fallback resolved: {ProxyLogSanitizer.SanitizeHost(host)}");
                return client;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastFailure = exception;
                client.Dispose();
            }
        }

        throw new IOException(
            $"Astral.WebProxy upstream address connection failed for {ProxyLogSanitizer.SanitizeHost(host)}.",
            lastFailure);
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveBypassSystemDnsAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
        await TryAddDohAnswersAsync(host, "A", addresses, cancellationToken);
        await TryAddDohAnswersAsync(host, "AAAA", addresses, cancellationToken);
        if (addresses.Count > 0)
        {
            return addresses.Values.ToArray();
        }

        await AddUdpDnsAnswersAsync(host, DnsRecordTypeA, addresses, cancellationToken);
        await AddUdpDnsAnswersAsync(host, DnsRecordTypeAaaa, addresses, cancellationToken);
        return addresses.Values.ToArray();
    }

    private static async Task TryAddDohAnswersAsync(
        string host,
        string recordType,
        Dictionary<string, IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        try
        {
            await AddDohAnswersAsync(host, recordType, addresses, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableResolverFailure(exception, cancellationToken))
        {
        }
    }

    private static async Task AddDohAnswersAsync(
        string host,
        string recordType,
        Dictionary<string, IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://cloudflare-dns.com/dns-query?name=" +
            Uri.EscapeDataString(host.Trim().TrimEnd('.')) +
            "&type=" +
            recordType);
        request.Headers.Accept.ParseAdd("application/dns-json");

        using var response = await DnsClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("Status", out var status)
            && status.ValueKind == JsonValueKind.Number
            && status.GetInt32() != 0)
        {
            return;
        }

        if (!document.RootElement.TryGetProperty("Answer", out var answers)
            || answers.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var answer in answers.EnumerateArray())
        {
            if (!answer.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.Number
                || (type.GetInt32() is not 1 and not 28)
                || !answer.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var rawAddress = data.GetString();
            if (rawAddress is not null
                && IPAddress.TryParse(rawAddress, out var address))
            {
                addresses[address.ToString()] = address;
            }
        }
    }

    private static async Task AddUdpDnsAnswersAsync(
        string host,
        ushort recordType,
        Dictionary<string, IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        var queryName = TryConvertToDnsQueryName(host);
        if (queryName is null)
        {
            return;
        }

        foreach (var server in FallbackDnsServers)
        {
            try
            {
                using var udp = new UdpClient(server.AddressFamily);
                udp.Connect(server, 53);
                var query = BuildDnsQuery(queryName, recordType, out var queryId);
                await udp.SendAsync(query, query.Length);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var response = await udp.ReceiveAsync(timeout.Token);
                AddUdpDnsResponseAnswers(
                    response.Buffer,
                    queryId,
                    recordType,
                    addresses);
                if (addresses.Count > 0)
                {
                    return;
                }
            }
            catch (Exception exception) when (IsRecoverableResolverFailure(exception, cancellationToken))
            {
            }
        }
    }

    private static string? TryConvertToDnsQueryName(string host)
    {
        try
        {
            var ascii = new IdnMapping().GetAscii(host.Trim().TrimEnd('.'));
            if (ascii.Length is 0 or > 253)
            {
                return null;
            }

            var labels = ascii.Split('.');
            return labels.Any(label => label.Length is 0 or > 63)
                ? null
                : ascii;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static byte[] BuildDnsQuery(
        string queryName,
        ushort recordType,
        out ushort queryId)
    {
        queryId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
        Span<byte> buffer = stackalloc byte[512];
        var offset = 0;
        WriteUInt16(buffer, ref offset, queryId);
        WriteUInt16(buffer, ref offset, 0x0100);
        WriteUInt16(buffer, ref offset, 1);
        WriteUInt16(buffer, ref offset, 0);
        WriteUInt16(buffer, ref offset, 0);
        WriteUInt16(buffer, ref offset, 0);

        foreach (var label in queryName.Split('.'))
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            buffer[offset++] = (byte)labelBytes.Length;
            labelBytes.CopyTo(buffer[offset..]);
            offset += labelBytes.Length;
        }

        buffer[offset++] = 0;
        WriteUInt16(buffer, ref offset, recordType);
        WriteUInt16(buffer, ref offset, DnsClassInternet);
        return buffer[..offset].ToArray();
    }

    private static void AddUdpDnsResponseAnswers(
        byte[] response,
        ushort queryId,
        ushort recordType,
        Dictionary<string, IPAddress> addresses)
    {
        if (response.Length < 12
            || BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0, 2)) != queryId)
        {
            return;
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
        if ((flags & 0x000f) != 0)
        {
            return;
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6, 2));
        var offset = 12;
        for (var index = 0; index < questionCount; index++)
        {
            if (!TrySkipDnsName(response, ref offset)
                || offset + 4 > response.Length)
            {
                return;
            }

            offset += 4;
        }

        for (var index = 0; index < answerCount; index++)
        {
            if (!TrySkipDnsName(response, ref offset)
                || offset + 10 > response.Length)
            {
                return;
            }

            var answerType = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset, 2));
            offset += 2;
            var answerClass = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset, 2));
            offset += 2;
            offset += 4;
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset, 2));
            offset += 2;
            if (offset + dataLength > response.Length)
            {
                return;
            }

            if (answerClass == DnsClassInternet
                && answerType == recordType
                && dataLength == (recordType == DnsRecordTypeA ? 4 : 16))
            {
                var address = new IPAddress(response.AsSpan(offset, dataLength));
                addresses[address.ToString()] = address;
            }

            offset += dataLength;
        }
    }

    private static bool TrySkipDnsName(byte[] message, ref int offset)
    {
        for (var labels = 0; labels < 128; labels++)
        {
            if (offset >= message.Length)
            {
                return false;
            }

            var length = message[offset++];
            if (length == 0)
            {
                return true;
            }

            if ((length & 0xc0) == 0xc0)
            {
                if (offset >= message.Length)
                {
                    return false;
                }

                offset++;
                return true;
            }

            if ((length & 0xc0) != 0)
            {
                return false;
            }

            offset += length;
            if (offset > message.Length)
            {
                return false;
            }
        }

        return false;
    }

    private static void WriteUInt16(
        Span<byte> buffer,
        ref int offset,
        int value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(
            buffer.Slice(offset, 2),
            (ushort)value);
        offset += 2;
    }

    private static bool IsRecoverableResolverFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        return exception is HttpRequestException
            or IOException
            or JsonException
            or SocketException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private static bool ShouldTryResolverFallback(SocketException exception)
    {
        return IsNameResolutionFailure(exception)
            || exception.SocketErrorCode is SocketError.ConnectionRefused
                or SocketError.NetworkUnreachable
                or SocketError.HostUnreachable
                or SocketError.TimedOut
                or SocketError.AddressNotAvailable
                or SocketError.ConnectionReset
                or SocketError.ConnectionAborted;
    }

    private static bool IsNameResolutionFailure(SocketException exception)
    {
        return exception.SocketErrorCode is SocketError.HostNotFound
            or SocketError.NoData
            or SocketError.TryAgain
            or SocketError.NoRecovery;
    }
}

file static class ProxyLogSanitizer
{
    public static string SanitizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "invalid-host";
        }

        var safeHost = new string(host
            .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_')
            .Take(253)
            .ToArray());
        return string.IsNullOrWhiteSpace(safeHost)
            ? "invalid-host"
            : safeHost;
    }
}
