using Astral.Core.Configuration;
using Astral.Core.Diagnostics;
using Astral.Core.Infrastructure;
using Astral.Core.Targets;
using System.Globalization;

namespace Astral.Core.WebProxy;

public interface IScopedWebProxyService
{
    Task ApplyAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
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

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class WindowsScopedWebProxyService : IScopedWebProxyService, IAsyncDisposable
{
    private const int ProxyPort = 18088;
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(20);

    private readonly AppPaths _paths;
    private readonly ICommandRunner _commandRunner;
    private readonly IProcessLauncher _processLauncher;
    private readonly string _webProxyExecutable;
    private readonly IAstralDiagnostics _diagnostics;
    private readonly string _powerShellPath;
    private IManagedProcess? _proxyProcess;

    public WindowsScopedWebProxyService(
        AppPaths paths,
        ICommandRunner commandRunner,
        IProcessLauncher processLauncher,
        string webProxyExecutable,
        IAstralDiagnostics? diagnostics = null,
        string? powerShellPath = null)
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
        var pacScript = ProxyPacScriptBuilder.Build(
            routingPlan.ProxyRules,
            "127.0.0.1",
            ProxyPort);
        await File.WriteAllTextAsync(_paths.WebProxyPacFile, pacScript, cancellationToken);
        var pacUri = new Uri(_paths.WebProxyPacFile).AbsoluteUri;

        await StopProxyProcessAsync(cancellationToken);
        var arguments = new List<string>
        {
            "--port",
            ProxyPort.ToString(CultureInfo.InvariantCulture)
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
                ["pac"] = "file"
            });
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopProxyProcessAsync(cancellationToken);
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

    private string BuildApplyPacScript(string pacUri)
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            "$keyPath = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings'",
            $"$statePath = '{QuotePowerShell(_paths.WebProxyStateFile)}'",
            $"$pacUrl = '{QuotePowerShell(pacUri)}'",
            "$current = Get-ItemProperty -Path $keyPath",
            "$state = [ordered]@{",
            "    Owner = 'Astral'",
            "    AppliedAutoConfigURL = $pacUrl",
            "    PreviousAutoConfigURL = $current.AutoConfigURL",
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

    private static string QuotePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string GetDefaultPowerShellPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }
}
