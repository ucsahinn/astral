using Astral.Core.Configuration;
using Astral.Core.Diagnostics;
using Astral.Core.Infrastructure;
using Astral.Core.Targets;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Astral.Core.WebProxy;

public interface IScopedWebProxyService
{
    Task ApplyAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<ScopedWebProxyStatus> EnsureAppliedAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<ScopedWebProxyStatus> GetStatusAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken);

    Task<ScopedWebProxyProof> VerifyTargetAccessAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed record ScopedWebProxyStatus(
    bool Required,
    bool IsApplied,
    string Message,
    string? ExpectedAutoConfigUrl = null,
    string? CurrentAutoConfigUrl = null,
    bool PacFileExists = false,
    bool StateFileExists = false,
    bool ProxyProcessRunning = false,
    int? ProxyPort = null)
{
    public static ScopedWebProxyStatus NotRequired(string message = "Web proxy kapsamı gerekmiyor.") =>
        new(false, true, message);

    public IReadOnlyDictionary<string, string?> ToDiagnosticDetails(string prefix = "webProxyScope")
    {
        return new Dictionary<string, string?>
        {
            [$"{prefix}.required"] = Required.ToString(),
            [$"{prefix}.applied"] = IsApplied.ToString(),
            [$"{prefix}.message"] = Message,
            [$"{prefix}.expectedAutoConfigUrl"] = ExpectedAutoConfigUrl,
            [$"{prefix}.currentAutoConfigUrl"] = RedactAutoConfigUrl(CurrentAutoConfigUrl),
            [$"{prefix}.pacFileExists"] = PacFileExists.ToString(),
            [$"{prefix}.stateFileExists"] = StateFileExists.ToString(),
            [$"{prefix}.proxyProcessRunning"] = ProxyProcessRunning.ToString(),
            [$"{prefix}.proxyPort"] = ProxyPort?.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string? RedactAutoConfigUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            ? "file"
            : value;
    }
}

public sealed record ScopedWebProxyProof(
    bool Required,
    bool IsVerified,
    string Message,
    string? Host = null,
    int? ProxyPort = null,
    int? StatusCode = null,
    int? RequiredTargetCount = null,
    int? VerifiedTargetCount = null,
    string? FailedTargets = null,
    int? ElapsedMilliseconds = null)
{
    public static ScopedWebProxyProof NotRequired(
        string message = "Web proxy target proof is not required.") =>
        new(false, true, message);

    public static ScopedWebProxyProof Verified(
        string host,
        int proxyPort,
        string message = "Scoped web proxy target proof succeeded.") =>
        new(
            true,
            true,
            message,
            host,
            proxyPort,
            200,
            RequiredTargetCount: 1,
            VerifiedTargetCount: 1);

    public static ScopedWebProxyProof VerifiedAll(
        IReadOnlyList<string> hosts,
        int proxyPort,
        int requiredTargetCount) =>
        new(
            true,
            true,
            "Scoped web proxy target proof succeeded for all selected web targets.",
            string.Join(", ", hosts),
            proxyPort,
            200,
            requiredTargetCount,
            hosts.Count);

    public static ScopedWebProxyProof Failed(
        string? host,
        int? proxyPort,
        string message,
        int? statusCode = null,
        int? requiredTargetCount = null,
        int? verifiedTargetCount = null,
        string? failedTargets = null) =>
        new(
            true,
            false,
            message,
            host,
            proxyPort,
            statusCode,
            requiredTargetCount,
            verifiedTargetCount,
            failedTargets);

    public IReadOnlyDictionary<string, string?> ToDiagnosticDetails(
        string prefix = "webProxyProof")
    {
        return new Dictionary<string, string?>
        {
            [$"{prefix}.required"] = Required.ToString(),
            [$"{prefix}.verified"] = IsVerified.ToString(),
            [$"{prefix}.message"] = Message,
            [$"{prefix}.host"] = Host,
            [$"{prefix}.proxyPort"] = ProxyPort?.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.statusCode"] = StatusCode?.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.requiredTargetCount"] =
                RequiredTargetCount?.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.verifiedTargetCount"] =
                VerifiedTargetCount?.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.failedTargets"] = FailedTargets,
            [$"{prefix}.elapsedMs"] =
                ElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture)
        };
    }
}

public sealed class NullScopedWebProxyService : IScopedWebProxyService
{
    public static NullScopedWebProxyService Instance { get; } = new();

    private NullScopedWebProxyService()
    {
    }

    public Task ApplyAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<ScopedWebProxyStatus> EnsureAppliedAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(routingPlan.RequiresWebProxy
            ? new ScopedWebProxyStatus(
                Required: true,
                IsApplied: true,
                Message: "No-op web proxy servisi kapsamı uygulanmış kabul etti.")
            : ScopedWebProxyStatus.NotRequired());
    }

    public Task<ScopedWebProxyStatus> GetStatusAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(routingPlan.RequiresWebProxy
            ? new ScopedWebProxyStatus(
                Required: true,
                IsApplied: true,
                Message: "No-op web proxy servisi kapsamı uygulanmış kabul etti.")
            : ScopedWebProxyStatus.NotRequired());
    }

    public Task<ScopedWebProxyProof> VerifyTargetAccessAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(routingPlan.RequiresWebProxy
            ? ScopedWebProxyProof.VerifiedAll(
                routingPlan.SelectedTargets
                    .Where(target => target.HasWebScope)
                    .Select(target => target.Id)
                    .ToArray(),
                0,
                routingPlan.SelectedTargets.Count(target => target.HasWebScope))
            : ScopedWebProxyProof.NotRequired());
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class WindowsScopedWebProxyService : IScopedWebProxyService, IAsyncDisposable
{
    private const int DefaultProxyPort = 18088;
    private const int MaxProbeHostsPerTarget = 2;
    private const int MaxConcurrentProbeTargets = 6;
    private const int MaxTargetProofAttempts = 2;
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TargetProofTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TargetProofRetryDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ProcessStartupValidationDelay =
        TimeSpan.FromMilliseconds(450);
    private static readonly JsonSerializerOptions StatusJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly ICommandRunner _commandRunner;
    private readonly IProcessLauncher _processLauncher;
    private readonly string _webProxyExecutable;
    private readonly IAstralDiagnostics _diagnostics;
    private readonly string _powerShellPath;
    private readonly int _preferredProxyPort;
    private IManagedProcess? _proxyProcess;

    public WindowsScopedWebProxyService(
        AppPaths paths,
        ICommandRunner commandRunner,
        IProcessLauncher processLauncher,
        string webProxyExecutable,
        IAstralDiagnostics? diagnostics = null,
        string? powerShellPath = null,
        int preferredProxyPort = DefaultProxyPort)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _webProxyExecutable = string.IsNullOrWhiteSpace(webProxyExecutable)
            ? throw new ArgumentException("Web proxy yolu boş olamaz.", nameof(webProxyExecutable))
            : Path.GetFullPath(webProxyExecutable);
        _diagnostics = diagnostics ?? NullAstralDiagnostics.Instance;
        _powerShellPath = string.IsNullOrWhiteSpace(powerShellPath)
            ? GetDefaultPowerShellPath()
            : powerShellPath;
        if (preferredProxyPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredProxyPort),
                "Proxy portu 1-65535 aralığında olmalıdır.");
        }

        _preferredProxyPort = preferredProxyPort;
    }

    public async Task ApplyAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!routingPlan.RequiresWebProxy)
        {
            await ClearAsync(cancellationToken);
            return;
        }

        if (!File.Exists(_webProxyExecutable))
        {
            throw new FileNotFoundException(
                "Astral web proxy çalıştırılabilir dosyası bulunamadı.",
                _webProxyExecutable);
        }

        _paths.EnsureDirectories();
        await StopProxyProcessAsync(cancellationToken);
        await StopOwnedOrphanProxyProcessesAsync(cancellationToken);

        var proxyPort = SelectAvailableProxyPort(_preferredProxyPort);
        if (proxyPort != _preferredProxyPort)
        {
            _diagnostics.Warning(
                "webProxy.portFallback",
                "Varsayılan yerel proxy portu kullanımda; bu oturum için yedek port seçildi.",
                new Dictionary<string, string?>
                {
                    ["preferredPort"] = _preferredProxyPort.ToString(CultureInfo.InvariantCulture),
                    ["selectedPort"] = proxyPort.ToString(CultureInfo.InvariantCulture)
                });
        }

        var pacScript = ProxyPacScriptBuilder.Build(
            routingPlan.ProxyRules,
            "127.0.0.1",
            proxyPort);
        await File.WriteAllTextAsync(_paths.WebProxyPacFile, pacScript, cancellationToken);
        var pacUri = new Uri(_paths.WebProxyPacFile).AbsoluteUri;

        var arguments = new List<string>
        {
            "--port",
            proxyPort.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var rule in routingPlan.ProxyRules)
        {
            arguments.Add("--allow");
            arguments.Add(rule.Pattern);
        }

        _proxyProcess = _processLauncher.Start(
            _webProxyExecutable,
            arguments,
            Path.GetDirectoryName(_webProxyExecutable)!,
            _paths.TunnelLog);
        await EnsureProxyProcessStartedAsync(proxyPort, cancellationToken);
        progress?.Report("Seçili web hedefleri için yerel proxy hazır");

        await RunScriptAsync(
            BuildApplyPacScript(pacUri),
            cancellationToken);
        _diagnostics.Info(
            "webProxy.apply",
            "Seçili web hedefleri için PAC ayarı uygulandı.",
            new Dictionary<string, string?>
            {
                ["proxyRuleCount"] = routingPlan.ProxyRules.Count.ToString(CultureInfo.InvariantCulture),
                ["proxyPort"] = proxyPort.ToString(CultureInfo.InvariantCulture),
                ["pac"] = "file"
            });
    }

    public async Task<ScopedWebProxyStatus> EnsureAppliedAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        var status = await GetStatusAsync(routingPlan, cancellationToken);
        if (!routingPlan.RequiresWebProxy || status.IsApplied)
        {
            return status;
        }

        _diagnostics.Warning(
            "webProxy.ensure",
            "Scoped PAC ayarında sapma algılandı; seçili web hedefleri için yeniden uygulanıyor.",
            status.ToDiagnosticDetails());
        progress?.Report("Seçili web hedefleri için proxy kapsamı yenileniyor");

        await ApplyAsync(routingPlan, progress, cancellationToken);
        var refreshed = await GetStatusAsync(routingPlan, cancellationToken);
        if (refreshed.IsApplied)
        {
            _diagnostics.Info(
                "webProxy.ensure",
                "Scoped PAC ayarı yeniden doğrulandı.",
                refreshed.ToDiagnosticDetails());
            return refreshed;
        }

        throw new InvalidOperationException(
            "Seçili web hedefleri için scoped PAC ayarı doğrulanamadı: " +
            refreshed.Message);
    }

    public async Task<ScopedWebProxyStatus> GetStatusAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!routingPlan.RequiresWebProxy)
        {
            return ScopedWebProxyStatus.NotRequired();
        }

        var expectedPacUri = new Uri(_paths.WebProxyPacFile).AbsoluteUri;
        var proxyPort = TryReadProxyPortFromPac(_paths.WebProxyPacFile);
        var proxyProcessRunning = _proxyProcess is not null && !_proxyProcess.HasExited;
        var scriptResult = await RunStatusScriptAsync(expectedPacUri, cancellationToken);
        var pacFileExists = scriptResult.PacFileExists ?? File.Exists(_paths.WebProxyPacFile);
        var stateFileExists = scriptResult.StateFileExists ?? File.Exists(_paths.WebProxyStateFile);
        var currentAutoConfigUrl = scriptResult.CurrentAutoConfigURL;
        var stateOwnerValid = string.Equals(
            scriptResult.StateOwner,
            "Astral",
            StringComparison.Ordinal);
        var stateAppliedValid = string.Equals(
            scriptResult.StateAppliedAutoConfigURL,
            expectedPacUri,
            StringComparison.OrdinalIgnoreCase);
        var autoConfigValid = string.Equals(
            currentAutoConfigUrl,
            expectedPacUri,
            StringComparison.OrdinalIgnoreCase);
        var isApplied = pacFileExists
            && stateFileExists
            && stateOwnerValid
            && stateAppliedValid
            && autoConfigValid
            && proxyProcessRunning
            && proxyPort is not null;

        var message = isApplied
            ? "Scoped PAC aktif ve seçili web hedefleri yerel proxyye yönleniyor."
            : CreateStatusMessage(
                pacFileExists,
                stateFileExists,
                stateOwnerValid,
                stateAppliedValid,
                autoConfigValid,
                proxyProcessRunning,
                proxyPort);

        return new ScopedWebProxyStatus(
            Required: true,
            IsApplied: isApplied,
            Message: message,
            ExpectedAutoConfigUrl: expectedPacUri,
            CurrentAutoConfigUrl: currentAutoConfigUrl,
            PacFileExists: pacFileExists,
            StateFileExists: stateFileExists,
            ProxyProcessRunning: proxyProcessRunning,
            ProxyPort: proxyPort);
    }

    public async Task<ScopedWebProxyProof> VerifyTargetAccessAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!routingPlan.RequiresWebProxy)
        {
            return ScopedWebProxyProof.NotRequired();
        }

        var elapsed = Stopwatch.StartNew();
        var status = await GetStatusAsync(routingPlan, cancellationToken);
        if (!status.IsApplied)
        {
            return ScopedWebProxyProof.Failed(
                host: null,
                proxyPort: status.ProxyPort,
                "Scoped web proxy is not applied: " + status.Message)
                with
                {
                    ElapsedMilliseconds = (int)elapsed.ElapsedMilliseconds
                };
        }

        if (status.ProxyPort is not int proxyPort)
        {
            return ScopedWebProxyProof.Failed(
                host: null,
                proxyPort: null,
                "Scoped web proxy port could not be determined.")
                with
                {
                    ElapsedMilliseconds = (int)elapsed.ElapsedMilliseconds
                };
        }

        var probeTargets = EnumerateProbeTargets(routingPlan).ToArray();
        if (probeTargets.Length == 0)
        {
            return ScopedWebProxyProof.Failed(
                host: null,
                proxyPort,
                "No safe probe host is available for selected web targets.",
                requiredTargetCount: 0,
                verifiedTargetCount: 0)
                with
                {
                    ElapsedMilliseconds = (int)elapsed.ElapsedMilliseconds
                };
        }

        var proofResults = await VerifyProbeTargetsAsync(
            probeTargets,
            proxyPort,
            cancellationToken);
        var verifiedHosts = proofResults
            .Where(result => result.VerifiedHost is not null)
            .Select(result => result.TargetLabel + "=" + result.VerifiedHost)
            .ToArray();
        var targetFailures = proofResults
            .Where(result => result.Failure is not null)
            .Select(result => result.TargetLabel + "=" + result.Failure)
            .ToArray();

        if (targetFailures.Length == 0)
        {
            var verified = ScopedWebProxyProof.VerifiedAll(
                verifiedHosts,
                proxyPort,
                probeTargets.Length)
                with
                {
                    ElapsedMilliseconds = (int)elapsed.ElapsedMilliseconds
                };
            _diagnostics.Info(
                "webProxy.proof",
                "Scoped web proxy target proof succeeded for all selected web targets.",
                verified.ToDiagnosticDetails());
            return verified;
        }

        var message = "Selected target proxy proof failed: " +
            string.Join("; ", targetFailures);
        var failed = ScopedWebProxyProof.Failed(
            host: null,
            proxyPort,
            message,
            requiredTargetCount: probeTargets.Length,
            verifiedTargetCount: verifiedHosts.Length,
            failedTargets: string.Join("; ", targetFailures))
            with
            {
                ElapsedMilliseconds = (int)elapsed.ElapsedMilliseconds
            };
        _diagnostics.Warning(
            "webProxy.proof",
            "Scoped web proxy target proof failed.",
            failed.ToDiagnosticDetails());
        return failed;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopProxyProcessAsync(cancellationToken);
        await StopOwnedOrphanProxyProcessesAsync(cancellationToken);
        await RunScriptAsync(BuildRestorePacScript(), cancellationToken);

        try
        {
            if (File.Exists(_paths.WebProxyPacFile))
            {
                File.Delete(_paths.WebProxyPacFile);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Warning(
                "webProxy.clear",
                "PAC dosyası temizlenemedi.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ClearAsync(CancellationToken.None);
    }

    private static IEnumerable<ProbeTarget> EnumerateProbeTargets(
        RoutingPlan routingPlan)
    {
        foreach (var target in routingPlan.SelectedTargets)
        {
            if (!target.HasWebScope)
            {
                continue;
            }

            var hosts = EnumerateTargetProbeHosts(target)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            yield return new ProbeTarget(
                target.Id,
                target.Label,
                hosts);
        }
    }

    private static IEnumerable<string> EnumerateTargetProbeHosts(
        TargetDefinition target)
    {
        if (target.Metadata.TryGetValue("probeHosts", out var rawHosts))
        {
            foreach (var rawHost in rawHosts.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries))
            {
                var host = TryNormalizeProbeHost(rawHost);
                if (host is not null)
                {
                    yield return host;
                }
            }
        }

        foreach (var domain in target.Domains)
        {
            if (domain.IsWildcard)
            {
                continue;
            }

            var host = TryNormalizeProbeHost(domain.Value);
            if (host is not null)
            {
                yield return host;
            }
        }
    }

    private static string? TryNormalizeProbeHost(string value)
    {
        try
        {
            if (value.Contains('*', StringComparison.Ordinal))
            {
                return null;
            }

            return DomainPattern.NormalizeHost(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<ScopedWebProxyProof> TryVerifySingleHostAsync(
        string host,
        int proxyPort,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TargetProofTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(
                IPAddress.Loopback,
                proxyPort,
                linked.Token);
            await using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                "CONNECT " + host + ":443 HTTP/1.1\r\n" +
                "Host: " + host + ":443\r\n" +
                "User-Agent: Astral-Probe\r\n" +
                "Proxy-Connection: close\r\n\r\n");
            await stream.WriteAsync(request, linked.Token);
            var responseHeader = await ReadProxyResponseHeaderAsync(
                stream,
                linked.Token);
            var statusCode = TryParseHttpStatusCode(responseHeader);
            return statusCode == 200
                ? ScopedWebProxyProof.Verified(host, proxyPort)
                : ScopedWebProxyProof.Failed(
                    host,
                    proxyPort,
                    "Proxy CONNECT returned HTTP " +
                    (statusCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown") +
                    ".",
                    statusCode);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return ScopedWebProxyProof.Failed(
                host,
                proxyPort,
                "Proxy CONNECT timed out.");
        }
        catch (Exception exception)
            when (exception is IOException
                or SocketException
                or InvalidOperationException)
        {
            return ScopedWebProxyProof.Failed(
                host,
                proxyPort,
                AstralDiagnostics.RedactForLog(
                    exception.Message ?? exception.GetType().Name)
                    ?? "Proxy CONNECT failed.");
        }
    }

    private static async Task<ProbeTargetResult> VerifyProbeTargetAsync(
        ProbeTarget target,
        int proxyPort,
        CancellationToken cancellationToken)
    {
        var probeFailures = new List<string>();
        foreach (var host in target.Hosts.Take(MaxProbeHostsPerTarget))
        {
            var proof = await TryVerifySingleHostAsync(
                host,
                proxyPort,
                cancellationToken);
            if (proof.IsVerified)
            {
                return ProbeTargetResult.Verified(target, host);
            }

            probeFailures.Add(CreateProbeFailureSummary(proof));
        }

        return ProbeTargetResult.Failed(
            target,
            probeFailures.Count == 0
                ? "no-safe-probe-host"
                : string.Join(",", probeFailures));
    }

    private static async Task<IReadOnlyList<ProbeTargetResult>> VerifyProbeTargetsAsync(
        IReadOnlyList<ProbeTarget> probeTargets,
        int proxyPort,
        CancellationToken cancellationToken)
    {
        var pending = probeTargets.ToDictionary(
            target => target.Id,
            StringComparer.OrdinalIgnoreCase);
        var verified = new List<ProbeTargetResult>();
        var lastFailures = Array.Empty<ProbeTargetResult>();

        for (var attempt = 1;
             attempt <= MaxTargetProofAttempts && pending.Count > 0;
             attempt++)
        {
            var results = await VerifyProbeTargetBatchAsync(
                pending.Values.ToArray(),
                proxyPort,
                cancellationToken);

            foreach (var result in results)
            {
                if (result.VerifiedHost is null)
                {
                    continue;
                }

                verified.Add(result);
                pending.Remove(result.TargetId);
            }

            lastFailures = results
                .Where(result => result.VerifiedHost is null)
                .ToArray();

            if (pending.Count > 0 && attempt < MaxTargetProofAttempts)
            {
                await Task.Delay(TargetProofRetryDelay, cancellationToken);
            }
        }

        return verified
            .Concat(lastFailures.Where(failure => pending.ContainsKey(failure.TargetId)))
            .ToArray();
    }

    private static async Task<IReadOnlyList<ProbeTargetResult>> VerifyProbeTargetBatchAsync(
        IReadOnlyList<ProbeTarget> targets,
        int proxyPort,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentProbeTargets);
        var tasks = targets.Select(async target =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await VerifyProbeTargetAsync(
                    target,
                    proxyPort,
                    cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private static async Task<string> ReadProxyResponseHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        const int maxHeaderBytes = 8 * 1024;
        var buffer = new byte[256];
        using var memory = new MemoryStream();
        while (memory.Length < maxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
            if (EndsWithHeaderTerminator(memory))
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(memory.ToArray());
    }

    private static bool EndsWithHeaderTerminator(MemoryStream memory)
    {
        if (memory.Length < 4)
        {
            return false;
        }

        var buffer = memory.GetBuffer();
        var offset = (int)memory.Length - 4;
        return buffer[offset] == '\r'
            && buffer[offset + 1] == '\n'
            && buffer[offset + 2] == '\r'
            && buffer[offset + 3] == '\n';
    }

    private static int? TryParseHttpStatusCode(string responseHeader)
    {
        var firstLine = responseHeader.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            && int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var statusCode)
            ? statusCode
            : null;
    }

    private static string CreateProbeFailureSummary(ScopedWebProxyProof proof)
    {
        var host = string.IsNullOrWhiteSpace(proof.Host)
            ? "unknown-host"
            : proof.Host;
        return host + "=" + proof.Message;
    }

    private sealed record ProbeTarget(
        string Id,
        string Label,
        IReadOnlyList<string> Hosts);

    private sealed record ProbeTargetResult(
        string TargetId,
        string TargetLabel,
        string? VerifiedHost,
        string? Failure)
    {
        public static ProbeTargetResult Verified(ProbeTarget target, string host) =>
            new(target.Id, target.Label, host, null);

        public static ProbeTargetResult Failed(ProbeTarget target, string failure) =>
            new(target.Id, target.Label, null, failure);
    }

    private async Task StopProxyProcessAsync(CancellationToken cancellationToken)
    {
        if (_proxyProcess is null)
        {
            return;
        }

        await _proxyProcess.StopAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await _proxyProcess.DisposeAsync();
        _proxyProcess = null;
    }

    private async Task StopOwnedOrphanProxyProcessesAsync(CancellationToken cancellationToken)
    {
        var currentProxyProcessId = _proxyProcess?.ProcessId;
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(_webProxyExecutable));
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            _diagnostics.Warning(
                "webProxy.orphan",
                "Astral web proxy surec listesi okunamadi.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
            return;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (currentProxyProcessId == process.Id
                    || !IsSameExecutablePath(process, _webProxyExecutable))
                {
                    continue;
                }

                try
                {
                    _diagnostics.Warning(
                        "webProxy.orphan",
                        "Onceki oturumdan kalan Astral.WebProxy sureci kapatiliyor.",
                        new Dictionary<string, string?>
                        {
                            ["processId"] = process.Id.ToString(CultureInfo.InvariantCulture)
                        });
                    process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        timeout.Token);
                    await process.WaitForExitAsync(linked.Token);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    _diagnostics.Warning(
                        "webProxy.orphan",
                        "Astral.WebProxy orphan sureci kapanma zaman asimina ugradi.",
                        new Dictionary<string, string?>
                        {
                            ["processId"] = process.Id.ToString(CultureInfo.InvariantCulture)
                        });
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception)
                {
                    _diagnostics.Warning(
                        "webProxy.orphan",
                        "Astral.WebProxy orphan sureci kapatilamadi.",
                        new Dictionary<string, string?>
                        {
                            ["processId"] = process.Id.ToString(CultureInfo.InvariantCulture),
                            ["diagnostic"] = exception.Message
                        });
                }
            }
        }
    }

    private static bool IsSameExecutablePath(Process process, string executablePath)
    {
        try
        {
            var processPath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(processPath)
                && string.Equals(
                    Path.GetFullPath(processPath),
                    Path.GetFullPath(executablePath),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception
                or IOException)
        {
            return false;
        }
    }

    private async Task EnsureProxyProcessStartedAsync(
        int proxyPort,
        CancellationToken cancellationToken)
    {
        if (_proxyProcess is null)
        {
            throw new InvalidOperationException(
                "Astral yerel web proxy başlatılamadı.");
        }

        await Task.Delay(ProcessStartupValidationDelay, cancellationToken);
        if (!_proxyProcess.HasExited)
        {
            return;
        }

        var exitCode = _proxyProcess.ExitCode;
        await StopProxyProcessAsync(cancellationToken);
        throw new InvalidOperationException(
            "Astral yerel web proxy başlatılamadı. " +
            $"Port {proxyPort.ToString(CultureInfo.InvariantCulture)} dinlenemedi; " +
            "önceki oturumdan kalan proxy süreci veya başka bir yerel servis portu kullanıyor olabilir. " +
            $"Çıkış kodu: {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}.");
    }

    private async Task RunScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        var result = await _commandRunner.RunAsync(
            _powerShellPath,
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script
            ],
            _paths.DataDirectory,
            ScriptTimeout,
            cancellationToken);

        if (result.Succeeded)
        {
            return;
        }

        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            diagnostic = $"PowerShell exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}.";
        }

        throw new InvalidOperationException(
            "Web proxy PAC ayarı güncellenemedi: " +
            diagnostic.Trim().ReplaceLineEndings(" "));
    }

    private async Task<WebProxyStatusScriptResult> RunStatusScriptAsync(
        string expectedPacUri,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        var result = await _commandRunner.RunAsync(
            _powerShellPath,
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                BuildStatusScript(expectedPacUri)
            ],
            _paths.DataDirectory,
            ScriptTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException(
                "Web proxy PAC durumu okunamadı: " +
                (string.IsNullOrWhiteSpace(diagnostic)
                    ? $"PowerShell exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}."
                    : diagnostic.Trim().ReplaceLineEndings(" ")));
        }

        try
        {
            return JsonSerializer.Deserialize<WebProxyStatusScriptResult>(
                    result.StandardOutput,
                    StatusJsonOptions)
                ?? new WebProxyStatusScriptResult();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Web proxy PAC durumu okunamadı: PowerShell JSON çıktısı geçersiz.",
                exception);
        }
    }

    private string BuildApplyPacScript(string pacUri)
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            "$keyPath = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings'",
            $"$statePath = '{QuotePowerShell(_paths.WebProxyStateFile)}'",
            $"$pacUrl = '{QuotePowerShell(pacUri)}'",
            $"$legacyPacPath = '{QuotePowerShell(GetLegacySharedPacFile())}'",
            "$current = Get-ItemProperty -Path $keyPath",
            "$previousAutoConfigUrl = $current.AutoConfigURL",
            "$legacyPacUrl = ([System.Uri]$legacyPacPath).AbsoluteUri",
            "if ($previousAutoConfigUrl -eq $pacUrl -or $previousAutoConfigUrl -eq $legacyPacUrl) {",
            "    $previousAutoConfigUrl = $null",
            "}",
            "$state = [ordered]@{",
            "    Owner = 'Astral'",
            "    AppliedAutoConfigURL = $pacUrl",
            "    PreviousAutoConfigURL = $previousAutoConfigUrl",
            "}",
            "$state | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $statePath -Encoding UTF8",
            "Set-ItemProperty -Path $keyPath -Name AutoConfigURL -Value $pacUrl | Out-Null"
        ]);
    }

    private string BuildRestorePacScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            "$keyPath = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings'",
            $"$statePath = '{QuotePowerShell(_paths.WebProxyStateFile)}'",
            "if (-not (Test-Path -LiteralPath $statePath)) { return }",
            "$state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json",
            "if ($state.Owner -ne 'Astral') { return }",
            "$current = Get-ItemProperty -Path $keyPath",
            "if ($current.AutoConfigURL -eq $state.AppliedAutoConfigURL) {",
            "    if ($null -eq $state.PreviousAutoConfigURL -or [string]::IsNullOrWhiteSpace([string]$state.PreviousAutoConfigURL)) {",
            "        Remove-ItemProperty -Path $keyPath -Name AutoConfigURL -ErrorAction SilentlyContinue",
            "    } else {",
            "        Set-ItemProperty -Path $keyPath -Name AutoConfigURL -Value ([string]$state.PreviousAutoConfigURL) | Out-Null",
            "    }",
            "}",
            "Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue"
        ]);
    }

    private string BuildStatusScript(string expectedPacUri)
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            "$keyPath = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings'",
            $"$statePath = '{QuotePowerShell(_paths.WebProxyStateFile)}'",
            $"$pacPath = '{QuotePowerShell(_paths.WebProxyPacFile)}'",
            $"$expectedPacUrl = '{QuotePowerShell(expectedPacUri)}'",
            "$current = Get-ItemProperty -Path $keyPath",
            "$stateOwner = $null",
            "$stateApplied = $null",
            "$stateExists = Test-Path -LiteralPath $statePath",
            "if ($stateExists) {",
            "    $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json",
            "    $stateOwner = [string]$state.Owner",
            "    $stateApplied = [string]$state.AppliedAutoConfigURL",
            "}",
            "[ordered]@{",
            "    CurrentAutoConfigURL = [string]$current.AutoConfigURL",
            "    ExpectedAutoConfigURL = $expectedPacUrl",
            "    PacFileExists = [bool](Test-Path -LiteralPath $pacPath)",
            "    StateFileExists = [bool]$stateExists",
            "    StateOwner = $stateOwner",
            "    StateAppliedAutoConfigURL = $stateApplied",
            "} | ConvertTo-Json -Compress"
        ]);
    }

    private static string QuotePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static int SelectAvailableProxyPort(int preferredPort)
    {
        if (CanBindLoopback(preferredPort))
        {
            return preferredPort;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool CanBindLoopback(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private string GetLegacySharedPacFile()
    {
        return Path.Combine(
            _paths.SharedDataDirectory,
            "web-proxy",
            "astral-scoped.pac");
    }

    private static int? TryReadProxyPortFromPac(string pacPath)
    {
        try
        {
            if (!File.Exists(pacPath))
            {
                return null;
            }

            var script = File.ReadAllText(pacPath);
            const string marker = "PROXY 127.0.0.1:";
            var markerIndex = script.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var start = markerIndex + marker.Length;
            var end = start;
            while (end < script.Length && char.IsDigit(script[end]))
            {
                end++;
            }

            if (end == start)
            {
                return null;
            }

            return int.TryParse(
                script[start..end],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port)
                && port is > 0 and <= 65535
                    ? port
                    : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CreateStatusMessage(
        bool pacFileExists,
        bool stateFileExists,
        bool stateOwnerValid,
        bool stateAppliedValid,
        bool autoConfigValid,
        bool proxyProcessRunning,
        int? proxyPort)
    {
        var missing = new List<string>();
        if (!pacFileExists)
        {
            missing.Add("PAC dosyası yok");
        }

        if (!stateFileExists)
        {
            missing.Add("PAC state dosyası yok");
        }
        else
        {
            if (!stateOwnerValid)
            {
                missing.Add("PAC state sahibi geçersiz");
            }

            if (!stateAppliedValid)
            {
                missing.Add("PAC state URL eşleşmiyor");
            }
        }

        if (!autoConfigValid)
        {
            missing.Add("Windows AutoConfigURL seçili PAC'a işaret etmiyor");
        }

        if (!proxyProcessRunning)
        {
            missing.Add("Astral.WebProxy süreci çalışmıyor");
        }

        if (proxyPort is null)
        {
            missing.Add("PAC içinden yerel proxy portu okunamadı");
        }

        return "Scoped PAC doğrulanamadı: " + string.Join("; ", missing) + ".";
    }

    private static string GetDefaultPowerShellPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }

    private sealed record WebProxyStatusScriptResult(
        string? CurrentAutoConfigURL = null,
        string? ExpectedAutoConfigURL = null,
        bool? PacFileExists = null,
        bool? StateFileExists = null,
        string? StateOwner = null,
        string? StateAppliedAutoConfigURL = null);
}
