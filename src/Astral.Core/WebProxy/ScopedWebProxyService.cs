using Astral.Core.Configuration;
using Astral.Core.Diagnostics;
using Astral.Core.Infrastructure;
using Astral.Core.Targets;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
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

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class WindowsScopedWebProxyService : IScopedWebProxyService, IAsyncDisposable
{
    private const int DefaultProxyPort = 18088;
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(20);
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
