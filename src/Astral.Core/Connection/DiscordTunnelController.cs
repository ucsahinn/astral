using Astral.Core.Configuration;
using Astral.Core.Diagnostics;
using Astral.Core.Discord;
using Astral.Core.Firewall;
using Astral.Core.Infrastructure;
using Astral.Core.Provisioning;
using Astral.Core.Targets;
using Astral.Core.WebProxy;
using Astral.Core.WireSock;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astral.Core.Connection;

public sealed class DiscordTunnelController : IAsyncDisposable
{
    private const int MaxTunnelReadinessChecks = 60;
    private const int MinTransparentReadinessChecks = 4;
    private const int MaxWireSockLogReadBytes = 64 * 1024;
    private const string WireSockTransparentMode = "transparent";
    private const string WireSockVirtualAdapterMode = "virtual-adapter";
    private const string ScopedWebProxyExecutableName = "Astral.WebProxy.exe";
    private static readonly string[] WireSockConnectionEstablishedMessages =
    [
        "Connection established",
        "Wireguard tunnel has been started.",
        "WireGuard tunnel has been started."
    ];

    private static readonly string[] WireSockConnectionFailureMessages =
    [
        "WireGuard tunnel has failed to start.",
        "Failed to initialize Wireguard tunnel.",
        "WireSock virtual adapter is not available",
        "WireGuard Server is unreachable"
    ];
    private static readonly TimeSpan TunnelReadinessRetryDelay =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CleanupPhaseTimeout = TimeSpan.FromSeconds(18);

    private readonly AppPaths _paths;
    private readonly DiscordAppScope _discordScope;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly IProfileProvisioner _provisioner;
    private readonly IProcessLauncher _processLauncher;
    private readonly IDiscordAccessLock _accessLock;
    private readonly IAstralDiagnostics _diagnostics;
    private readonly IDiscordProcessManager _discordProcessManager;
    private readonly ITunnelReadinessProbe _tunnelReadinessProbe;
    private readonly TargetScopeResolver _targetScopeResolver;
    private readonly IScopedWebProxyService _webProxyService;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly TimeSpan _startupGracePeriod;
    private readonly TimeSpan _tunnelReadinessRetryDelay;

    private IManagedProcess? _wireSockProcess;
    private TunnelSnapshot _snapshot = new(
        TunnelState.Disconnected,
        "Astral Bağlı Değil",
        DateTimeOffset.Now);
    private bool _disposed;
    private bool _intentionalStop;
    private bool _accessLockConfirmed;
    private TargetSelection _targetSelection = TargetSelection.Default;
    private RoutingPlan? _lastRoutingPlan;
    private string? _lastProfilePath;
    private string? _lastRoutingProfileSha256;
    private string? _lastNextAction;
    private TunnelReadinessSnapshot _lastTunnelReadiness =
        TunnelReadinessSnapshot.ProbeUnavailable("Tunnel readiness probe has not run.");
    private bool _lastWireSockConnectionEstablished;
    private long? _wireSockTrafficBaselineBytesReceived;
    private long? _wireSockTrafficBaselineBytesSent;
    private bool _lastWireSockTrafficDeltaObserved;
    private string? _lastWireSockTrafficDiagnostic =
        "WireSock traffic proof has not been checked.";
    private string? _lastWireSockHandshakeDiagnostic =
        "WireSock handshake has not been checked.";
    private string? _lastWireSockProcessId;
    private bool? _lastWireSockProcessExited;
    private string? _lastWireSockProcessExitCode;
    private string _lastWireSockMode = WireSockTransparentMode;
    private long _lastTunnelLogStartPosition;
    private IReadOnlyDictionary<string, string?> _lastRoutingSummary =
        new Dictionary<string, string?>();
    private ScopedWebProxyStatus _lastWebProxyStatus =
        ScopedWebProxyStatus.NotRequired("Web proxy kapsamı henüz denetlenmedi.");
    private ScopedWebProxyProof _lastWebProxyProof =
        ScopedWebProxyProof.NotRequired("Web proxy target proof has not run.");
    private TargetProcessRefreshResult _lastTargetProcessRefresh =
        TargetProcessRefreshResult.NotRun();

    public DiscordTunnelController(
        AppPaths paths,
        DiscordAppScope discordScope,
        IWireSockBootstrapper wireSockBootstrapper,
        IProfileProvisioner provisioner,
        IProcessLauncher processLauncher,
        TimeSpan? startupGracePeriod = null,
        IDiscordAccessLock? accessLock = null,
        IAstralDiagnostics? diagnostics = null,
        IDiscordProcessManager? discordProcessManager = null,
        ITunnelReadinessProbe? tunnelReadinessProbe = null,
        TimeSpan? tunnelReadinessRetryDelay = null,
        TargetScopeResolver? targetScopeResolver = null,
        IScopedWebProxyService? webProxyService = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _discordScope = discordScope ?? throw new ArgumentNullException(nameof(discordScope));
        _wireSockBootstrapper = wireSockBootstrapper
            ?? throw new ArgumentNullException(nameof(wireSockBootstrapper));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        _processLauncher = processLauncher
            ?? throw new ArgumentNullException(nameof(processLauncher));
        _accessLock = accessLock ?? new NullDiscordAccessLock();
        _diagnostics = diagnostics ?? NullAstralDiagnostics.Instance;
        _discordProcessManager = discordProcessManager ?? new NullDiscordProcessManager();
        _tunnelReadinessProbe = tunnelReadinessProbe
            ?? NullTunnelReadinessProbe.Instance;
        _targetScopeResolver = targetScopeResolver
            ?? new TargetScopeResolver(
                TargetRegistry.CreateDefault(),
                _discordScope,
                Path.Combine(AppContext.BaseDirectory, "Astral.WebProxy.exe"));
        _webProxyService = webProxyService ?? NullScopedWebProxyService.Instance;
        _startupGracePeriod = startupGracePeriod ?? TimeSpan.FromSeconds(2);
        _tunnelReadinessRetryDelay =
            tunnelReadinessRetryDelay ?? TunnelReadinessRetryDelay;
    }

    public event EventHandler<TunnelSnapshot>? StatusChanged;

    public TunnelSnapshot Snapshot => _snapshot;

    public TargetSelection TargetSelection => _targetSelection;

    public RoutingPlan CurrentRoutingPlan => _lastRoutingPlan
        ?? _targetScopeResolver.Resolve(_targetSelection);

    public bool IncludeBrowserAccess
    {
        get => CurrentRoutingPlan.RequiresWebProxy;
        set
        {
            if (!TrySetBrowserAccess(value))
            {
                throw new InvalidOperationException(
                    "Web hedef kapsamı yalnızca bağlantı kapalıyken değiştirilebilir.");
            }
        }
    }

    public bool TrySetBrowserAccess(bool enabled)
    {
        return TrySetTargetSelection(TargetSelection.Default);
    }

    public bool TrySetTargetSelection(TargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (_snapshot.IsConnected || _snapshot.IsBusy)
        {
            _diagnostics.Warning(
                "controller.targetSelection",
                "Bağlantı aktifken hedef kapsamı değiştirme isteği reddedildi.",
                new Dictionary<string, string?>
                {
                    ["requested"] = string.Join(",", selection.SelectedTargetIds),
                    ["current"] = string.Join(",", _targetSelection.SelectedTargetIds),
                    ["state"] = _snapshot.State.ToString()
                });
            return false;
        }

        var changed = !AreSelectionsEquivalent(_targetSelection, selection);
        if (changed)
        {
            _lastProfilePath = null;
            _lastRoutingProfileSha256 = null;
            _lastRoutingPlan = _targetScopeResolver.Resolve(selection);
            _lastRoutingSummary = new Dictionary<string, string?>
            {
                ["profileState"] = "pending-next-connect",
                ["selectedTargets"] = _lastRoutingPlan.Summary,
                ["webProxy"] = _lastRoutingPlan.RequiresWebProxy
                    ? "pending"
                    : "not-required"
            };
        }

        _targetSelection = selection;
        return true;
    }

    public async Task EnsureDisconnectedLockAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_snapshot.IsConnected || _snapshot.IsBusy)
            {
                return;
            }

            _diagnostics.Info("controller.lock", "Hedef bağlantı koruması etkinleştiriliyor.");
            await StopOwnedOrphanWireSockProcessAsync(cancellationToken);
            await _webProxyService.ClearAsync(cancellationToken);
            await _accessLock.EnableAsync(CurrentRoutingPlan, cancellationToken);
            _accessLockConfirmed = true;
            SetStatus(TunnelState.Disconnected, "Astral Bağlı Değil");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteDiagnostic("EnableAccessLock", exception.ToString());
            SetStatus(
                TunnelState.Error,
                exception.Message,
                exception.ToString());
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot.IsConnected)
        {
            await DisconnectAsync(cancellationToken);
        }
        else
        {
            await ConnectAsync(cancellationToken);
        }
    }

    public IReadOnlyDictionary<string, string?> CreateDiagnosticDetails()
    {
        var wireSockRunning = _wireSockProcess is not null
            && !_wireSockProcess.HasExited;
        var routingPlan = CurrentRoutingPlan;

        var details = new Dictionary<string, string?>
        {
            ["selectedTargets"] = routingPlan.Summary,
            ["webProxy"] = routingPlan.RequiresWebProxy.ToString(),
            ["state"] = _snapshot.State.ToString(),
            ["message"] = _snapshot.Message,
            ["diagnostic"] = _snapshot.Diagnostic,
            ["wireSockRunning"] = wireSockRunning.ToString(),
            ["nextAction"] = _lastNextAction,
            ["targetLaunchPolicy"] = _lastTargetProcessRefresh.Policy,
            ["profilePath"] = _lastProfilePath,
            ["routingProfileSha256"] = _lastRoutingProfileSha256,
            ["profileHashScope"] = "sanitized-routing-lines",
            ["tunnelLog"] = _paths.TunnelLog,
            ["routingSummaryKind"] = "last-applied-or-pending-profile",
            ["routingScope"] = CreateRoutingScopeDescription(routingPlan),
            ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary),
            ["applicationTunnelProofRequired"] =
                RoutingPlanRequiresApplicationTunnelProof(routingPlan).ToString()
        };

        foreach (var webProxyDetail in _lastWebProxyStatus.ToDiagnosticDetails())
        {
            details[webProxyDetail.Key] = webProxyDetail.Value;
        }

        foreach (var webProxyProofDetail in _lastWebProxyProof.ToDiagnosticDetails())
        {
            details[webProxyProofDetail.Key] = webProxyProofDetail.Value;
        }

        foreach (var targetProcessDetail in _lastTargetProcessRefresh.ToDiagnosticDetails())
        {
            details[targetProcessDetail.Key] = targetProcessDetail.Value;
        }

        foreach (var readinessDetail in CreateTunnelReadinessDetails())
        {
            details[readinessDetail.Key] = readinessDetail.Value;
        }

        return details;
    }

    public async Task<ScopedWebProxyStatus> EnsureCurrentWebProxyScopeAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            var routingPlan = CurrentRoutingPlan;
            var status = _snapshot.IsConnected
                ? await _webProxyService.EnsureAppliedAsync(
                    routingPlan,
                    progress: null,
                    cancellationToken)
                : await _webProxyService.GetStatusAsync(
                    routingPlan,
                    cancellationToken);
            _lastWebProxyStatus = status;

            var details = new Dictionary<string, string?>
            {
                ["reason"] = reason,
                ["state"] = _snapshot.State.ToString(),
                ["selectedTargets"] = routingPlan.Summary
            };
            foreach (var detail in status.ToDiagnosticDetails())
            {
                details[detail.Key] = detail.Value;
            }

            _diagnostics.Info(
                "controller.webProxyScope",
                "Scoped web proxy kapsamı denetlendi.",
                details);
            return status;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var routingPlan = CurrentRoutingPlan;
            var failedStatus = new ScopedWebProxyStatus(
                Required: routingPlan.RequiresWebProxy,
                IsApplied: !routingPlan.RequiresWebProxy,
                Message: exception.Message);
            _lastWebProxyStatus = failedStatus;
            var details = new Dictionary<string, string?>
            {
                ["reason"] = reason,
                ["state"] = _snapshot.State.ToString(),
                ["selectedTargets"] = routingPlan.Summary
            };
            foreach (var detail in failedStatus.ToDiagnosticDetails())
            {
                details[detail.Key] = detail.Value;
            }

            WriteDiagnostic("WebProxyScopeEnsure", exception.ToString());
            _diagnostics.Failure(
                "controller.webProxyScope",
                "Scoped web proxy kapsamı doğrulanamadı.",
                exception,
                details);

            if (_snapshot.IsConnected && routingPlan.RequiresWebProxy)
            {
                SetStatus(
                    TunnelState.Error,
                    "Hedef web kapsamı doğrulanamadı.",
                    exception.ToString());
            }

            return failedStatus;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_snapshot.IsConnected || _snapshot.IsBusy)
            {
                return;
            }

            var connectStopwatch = Stopwatch.StartNew();
            void LogConnectPhase(string phase)
            {
                _diagnostics.Info(
                    "controller.connect.phase",
                    phase,
                    new Dictionary<string, string?>
                    {
                        ["elapsedMs"] = connectStopwatch.ElapsedMilliseconds
                            .ToString(CultureInfo.InvariantCulture),
                        ["selectedTargets"] = CurrentRoutingPlan.Summary,
                        ["state"] = _snapshot.State.ToString()
                    });
            }

            _intentionalStop = false;
            _lastTunnelReadiness =
                TunnelReadinessSnapshot.ProbeUnavailable("Tunnel readiness probe has not run.");
            _lastWireSockConnectionEstablished = false;
            _lastWireSockHandshakeDiagnostic =
                "WireSock handshake has not been checked.";
            _lastWireSockMode = WireSockTransparentMode;
            _lastWebProxyProof =
                ScopedWebProxyProof.NotRequired("Web proxy target proof has not run.");
            _lastTargetProcessRefresh = TargetProcessRefreshResult.NotRun();
            var routingPlan = _targetScopeResolver.Resolve(_targetSelection);
            _lastRoutingPlan = routingPlan;
            _lastWebProxyStatus = routingPlan.RequiresWebProxy
                ? new ScopedWebProxyStatus(
                    Required: true,
                    IsApplied: false,
                    Message: "Web proxy kapsamı henüz uygulanmadı.")
                : ScopedWebProxyStatus.NotRequired();
            SetStatus(TunnelState.Preparing, "Hedef bağlantısı hazırlanıyor");
            _diagnostics.Info(
                "controller.connect",
                "Bağlantı başlatıldı.",
                new Dictionary<string, string?>
                {
                    ["selectedTargets"] = routingPlan.Summary,
                    ["webProxy"] = routingPlan.RequiresWebProxy.ToString()
                });
            await _accessLock.PrepareTunnelScopeAsync(
                routingPlan,
                cancellationToken);
            _accessLockConfirmed = false;
            LogConnectPhase("access-lock-ready");

            SetStatus(TunnelState.Preparing, "Hedef bağlantısı açılıyor");

            var progress = new CallbackProgress(
                message =>
                {
                    _diagnostics.Info("controller.prepare", message);
                    SetStatus(TunnelState.Preparing, message);
                });
            var wireSockExecutable = await _wireSockBootstrapper.EnsureInstalledAsync(
                progress,
                cancellationToken);
            LogConnectPhase("wiresock-ready");

            var allowedApplications = routingPlan.AllowedApplications;
            _lastRoutingSummary = CreateAllowedApplicationDiagnostics(
                routingPlan);
            var profilePath = await _provisioner.EnsureProfileAsync(
                allowedApplications,
                progress,
                cancellationToken);
            _lastProfilePath = profilePath;
            var routingProfileHash = await ComputeRoutingProfileSha256Async(
                profilePath,
                cancellationToken);
            _lastRoutingProfileSha256 = routingProfileHash;
            LogConnectPhase("profile-ready");
            _diagnostics.Info(
                "controller.profile",
                "Hedef profili hazırlandı.",
                new Dictionary<string, string?>
                {
                    ["profilePath"] = profilePath,
                    ["routingProfileSha256"] = routingProfileHash,
                    ["profileHashScope"] = "sanitized-routing-lines",
                    ["routingScope"] = CreateRoutingScopeDescription(routingPlan)
                }.Concat(_lastRoutingSummary)
                    .ToDictionary(pair => pair.Key, pair => pair.Value));

            _lastTunnelLogStartPosition = await StartWireSockProcessAsync(
                wireSockExecutable,
                profilePath,
                cancellationToken);
            LogConnectPhase("wiresock-process-started");

            await _webProxyService.ApplyAsync(
                routingPlan,
                progress,
                cancellationToken);
            LogConnectPhase("web-proxy-ready");

            _lastWebProxyStatus = await _webProxyService.EnsureAppliedAsync(
                routingPlan,
                progress,
                cancellationToken);
            LogConnectPhase("web-proxy-verified");
            if (_lastWebProxyStatus.Required && !_lastWebProxyStatus.IsApplied)
            {
                throw new InvalidOperationException(
                    "Seçili web hedefleri için scoped proxy kapsamı doğrulanamadı: " +
                    _lastWebProxyStatus.Message);
            }

            _lastWebProxyProof = await _webProxyService.VerifyTargetAccessAsync(
                routingPlan,
                cancellationToken);
            LogConnectPhase("web-proxy-proof");
            if (_lastWebProxyProof.Required && !_lastWebProxyProof.IsVerified)
            {
                throw new InvalidOperationException(
                    "Seçili web hedeflerine scoped proxy üzerinden çıkış doğrulanamadı: " +
                    _lastWebProxyProof.Message);
            }

            if (RoutingPlanRequiresApplicationTunnelProof(routingPlan)
                && _lastWebProxyProof.Required
                && _lastWebProxyProof.IsVerified)
            {
                CaptureWireSockTrafficBaseline();
                _lastWireSockTrafficDiagnostic =
                    "WireSock adapter traffic baseline reset after scoped web proxy proof; subsequent traffic is used for application tunnel proof.";
                LogConnectPhase("application-proof-baseline");
            }

            _lastTargetProcessRefresh = await RefreshRunningTargetProcessesAsync(
                routingPlan,
                cancellationToken);
            LogConnectPhase("target-process-refresh");

            var enforceApplicationTunnelProof =
                RoutingPlanRequiresApplicationTunnelProof(routingPlan)
                && !_lastTargetProcessRefresh.RequiresManualAction;
            SetStatus(TunnelState.Verifying, "Hedef kapsamı doğrulanıyor");
            await VerifyTunnelReadinessAsync(
                _lastTunnelLogStartPosition,
                enforceApplicationTunnelProof,
                cancellationToken);
            LogConnectPhase("tunnel-ready");

            var finalState = _lastTargetProcessRefresh.RequiresManualAction
                ? TunnelState.DiscordRestartRequired
                : TunnelState.Connected;
            var nextAction = _lastTargetProcessRefresh.NextAction;
            var connectionMessage = _lastTargetProcessRefresh.RequiresManualAction
                ? "Seçili uygulama bağlantısı yenilenemedi"
                : "Sadece seçili hedefler tünelden çıkıyor";
            var targetLaunchPolicy = _lastTargetProcessRefresh.Policy;

            _lastNextAction = nextAction;
            connectStopwatch.Stop();

            SetStatus(
                finalState,
                connectionMessage,
                nextAction);
            _diagnostics.Info(
                "controller.verify",
                "WireSock süreci açık; hedef kapsamı için son durum belirlendi.",
                new Dictionary<string, string?>
                {
                    ["webProxy"] = routingPlan.RequiresWebProxy.ToString(),
                    ["selectedTargets"] = routingPlan.Summary,
                    ["routingScope"] = CreateRoutingScopeDescription(routingPlan),
                    ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary),
                    ["applicationTunnelProofRequired"] =
                        RoutingPlanRequiresApplicationTunnelProof(routingPlan).ToString(),
                    ["applicationTunnelProofEnforced"] =
                        enforceApplicationTunnelProof.ToString(),
                    ["targetLaunchPolicy"] = targetLaunchPolicy,
                    ["nextAction"] = nextAction,
                    ["connectElapsedMs"] = connectStopwatch.ElapsedMilliseconds
                        .ToString(CultureInfo.InvariantCulture),
                    ["wireSockRunning"] = "True"
                }.Concat(_lastWebProxyStatus.ToDiagnosticDetails())
                    .Concat(_lastWebProxyProof.ToDiagnosticDetails())
                    .Concat(_lastTargetProcessRefresh.ToDiagnosticDetails())
                    .Concat(CreateTunnelReadinessDetails())
                    .ToDictionary(pair => pair.Key, pair => pair.Value));
            _diagnostics.WriteHealth(
                finalState is TunnelState.Connected
                    ? "bağlantı hazır"
                    : "hedef için ek aksiyon gerekli",
                new Dictionary<string, string?>
                {
                    ["selectedTargets"] = routingPlan.Summary,
                    ["routingScope"] = CreateRoutingScopeDescription(routingPlan),
                    ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary),
                    ["applicationTunnelProofRequired"] =
                        RoutingPlanRequiresApplicationTunnelProof(routingPlan).ToString(),
                    ["applicationTunnelProofEnforced"] =
                        enforceApplicationTunnelProof.ToString(),
                    ["targetLaunchPolicy"] = targetLaunchPolicy,
                    ["nextAction"] = nextAction,
                    ["profilePath"] = profilePath,
                    ["routingProfileSha256"] = routingProfileHash,
                    ["profileHashScope"] = "sanitized-routing-lines",
                    ["connectElapsedMs"] = connectStopwatch.ElapsedMilliseconds
                        .ToString(CultureInfo.InvariantCulture),
                    ["tunnelLog"] = _paths.TunnelLog,
                    ["wireSockRunning"] = "True"
                }.Concat(_lastWebProxyStatus.ToDiagnosticDetails())
                    .Concat(_lastWebProxyProof.ToDiagnosticDetails())
                    .Concat(_lastTargetProcessRefresh.ToDiagnosticDetails())
                    .Concat(CreateTunnelReadinessDetails())
                    .ToDictionary(pair => pair.Key, pair => pair.Value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeProcessAsync();
            await TryClearWebProxyScopeAsync("ConnectCanceled");
            await TryClearTunnelScopeAsync("ConnectCanceled");
            await TryEnableAccessLockAsync("ConnectCanceled");
            SetStatus(TunnelState.Disconnected, "Bağlantı iptal edildi");
            _diagnostics.Warning("controller.connect", "Bağlantı kullanıcı tarafından iptal edildi.");
            throw;
        }
        catch (Exception exception)
        {
            await DisposeProcessAsync();
            await TryClearWebProxyScopeAsync("ConnectFailure");
            await TryClearTunnelScopeAsync("ConnectFailure");
            await TryEnableAccessLockAsync("ConnectFailure");
            WriteDiagnostic("Connect", exception.ToString());
            var userFacingMessage = CreateUserFacingConnectError(exception);
            _diagnostics.WriteHealth(
                "hata",
                new Dictionary<string, string?>
                {
                    ["operation"] = "connect",
                    ["message"] = userFacingMessage,
                    ["diagnostic"] = exception.Message
                }.Concat(_lastWebProxyStatus.ToDiagnosticDetails())
                    .Concat(_lastWebProxyProof.ToDiagnosticDetails())
                    .Concat(_lastTargetProcessRefresh.ToDiagnosticDetails())
                    .Concat(CreateTunnelReadinessDetails())
                    .ToDictionary(pair => pair.Key, pair => pair.Value));
            SetStatus(
                TunnelState.Error,
                userFacingMessage,
                exception.ToString());
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<long> StartWireSockProcessAsync(
        string wireSockExecutable,
        string profilePath,
        CancellationToken cancellationToken)
    {
        SetStatus(TunnelState.Connecting, "Hedef bağlantısı kuruluyor");
        var arguments = await PrepareWireSockArgumentsAsync(
            wireSockExecutable,
            profilePath,
            cancellationToken);
        CaptureWireSockTrafficBaseline();
        _diagnostics.Info(
            "controller.process",
            "WireSock süreci başlatılıyor.",
            new Dictionary<string, string?>
            {
                ["executable"] = wireSockExecutable,
                ["arguments"] = string.Join(" ", arguments)
            });
        var tunnelLogStartPosition = GetFileLength(_paths.TunnelLog);
        _wireSockProcess = _processLauncher.Start(
            wireSockExecutable,
            arguments,
            Path.GetDirectoryName(wireSockExecutable)!,
            _paths.TunnelLog);
        CaptureWireSockProcessState();
        WriteWireSockProcessMarker(profilePath);
        _wireSockProcess.Exited += OnWireSockExited;

        SetStatus(TunnelState.Verifying, "Bağlantı doğrulanıyor");
        await Task.Delay(_startupGracePeriod, cancellationToken);

        CaptureWireSockProcessState();
        if (_wireSockProcess.HasExited)
        {
            var exitCode = _wireSockProcess.ExitCode;
            await DisposeProcessAsync();
            throw new InvalidOperationException(
                $"WireSock bağlantı kurulmadan kapandı. Çıkış kodu: " +
                $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}.");
        }

        _lastTunnelLogStartPosition = tunnelLogStartPosition;
        return tunnelLogStartPosition;
    }

    private async Task<TargetProcessRefreshResult> RefreshRunningTargetProcessesAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        if (!RoutingPlanRequiresDiscordProcessRefresh(routingPlan))
        {
            return TargetProcessRefreshResult.NotRequired();
        }

        var snapshot = _discordProcessManager.Capture();
        if (!snapshot.HasRunningProcesses)
        {
            var notRunning = TargetProcessRefreshResult.NotRunning(snapshot);
            _diagnostics.Info(
                "controller.targetProcess.refresh",
                notRunning.Message,
                notRunning.ToDiagnosticDetails());
            return notRunning;
        }

        SetStatus(
            TunnelState.Verifying,
            "Çalışan Discord bağlantısı yenileniyor");
        _diagnostics.Info(
            "controller.targetProcess.refresh",
            "Çalışan Discord tünel kapsamına alınmak için yenileniyor.",
            new Dictionary<string, string?>
            {
                ["targetProcessRefresh.required"] = "True",
                ["targetProcessRefresh.runningProcessCount"] =
                    snapshot.RunningProcessCount.ToString(CultureInfo.InvariantCulture),
                ["targetProcessRefresh.knownExecutablePathCount"] =
                    snapshot.KnownExecutablePathCount.ToString(CultureInfo.InvariantCulture)
            });

        var restartResult = await _discordProcessManager.RestartAsync(
            snapshot,
            _startupGracePeriod,
            cancellationToken);
        if (!restartResult.Restarted)
        {
            var failure = TargetProcessRefreshResult.ManualActionRequired(
                snapshot,
                restartResult);
            _diagnostics.Warning(
                "controller.targetProcess.refresh",
                failure.Message,
                failure.ToDiagnosticDetails());
            return failure;
        }

        var verifySnapshot = _discordProcessManager.Capture();
        var verifyResult = await _discordProcessManager.VerifyReadyAsync(
            verifySnapshot,
            cancellationToken);
        if (!verifyResult.Restarted)
        {
            var failure = TargetProcessRefreshResult.ManualActionRequired(
                verifySnapshot.HasRunningProcesses ? verifySnapshot : snapshot,
                verifyResult);
            _diagnostics.Warning(
                "controller.targetProcess.refresh",
                failure.Message,
                failure.ToDiagnosticDetails());
            return failure;
        }

        var refreshed = TargetProcessRefreshResult.Succeeded(
            verifySnapshot.HasRunningProcesses ? verifySnapshot : snapshot,
            verifyResult.Message);
        _diagnostics.Info(
            "controller.targetProcess.refresh",
            refreshed.Message,
            refreshed.ToDiagnosticDetails());
        return refreshed;
    }

    private async Task VerifyTunnelReadinessAsync(
        long tunnelLogStartPosition,
        bool enforceApplicationTunnelProof,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTunnelReadinessChecks; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastTunnelReadiness = _tunnelReadinessProbe.Capture();
            CaptureWireSockProcessState();
            _lastWireSockConnectionEstablished =
                HasWireSockConnectionEstablished(tunnelLogStartPosition);
            var ready = TryMarkTunnelReadyForCurrentMode(
                attempt,
                enforceApplicationTunnelProof);
            var details = CreateTunnelReadinessDetails();
            details["attempt"] = attempt.ToString(CultureInfo.InvariantCulture);
            details["maxAttempts"] =
                MaxTunnelReadinessChecks.ToString(CultureInfo.InvariantCulture);

            _diagnostics.Info(
                "controller.tunnelReadiness",
                "Astral tünel adaptörü denetleniyor.",
                details);

            if (ready)
            {
                return;
            }

            if (_lastWireSockProcessExited == true)
            {
                break;
            }

            if (attempt < MaxTunnelReadinessChecks)
            {
                await Task.Delay(_tunnelReadinessRetryDelay, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lastTunnelReadiness = _tunnelReadinessProbe.Capture();
        CaptureWireSockProcessState();
        _lastWireSockConnectionEstablished =
            HasWireSockConnectionEstablished(tunnelLogStartPosition);
        if (TryMarkTunnelReadyForCurrentMode(
                MaxTunnelReadinessChecks,
                enforceApplicationTunnelProof))
        {
            _diagnostics.Info(
                "controller.tunnelReadiness",
                "Astral tünel adaptörü son denetimde hazır.",
                CreateTunnelReadinessDetails());
            return;
        }

        _diagnostics.Warning(
            "controller.tunnelReadiness",
            "Astral bağlantı motoru hazır duruma geçmedi.",
            CreateTunnelReadinessDetails());
        if (_lastWireSockProcessExited == true)
        {
            throw new InvalidOperationException(
                "Astral bağlantı motoru bağlantı doğrulanmadan kapandı. " +
                "Çıkış kodu: " +
                $"{_lastWireSockProcessExitCode ?? "bilinmiyor"}.");
        }

        if (!IsWireSockHandshakeReady())
        {
            if (enforceApplicationTunnelProof
                && RoutingPlanRequiresApplicationTunnelProof(CurrentRoutingPlan)
                && _lastWebProxyProof.Required
                && _lastWebProxyProof.IsVerified)
            {
                throw new InvalidOperationException(
                    "Seçili uygulama tünel kanıtı doğrulanamadı. Web rotaları scoped proxy üzerinden doğrulandı ancak uygulama hedefleri için WireSock handshake veya adapter trafik artışı alınamadı; uygulamayı kapatıp yeniden açın, ardından tekrar bağlanın.");
            }

            throw new InvalidOperationException(
                "WireSock bağlantı kanıtı doğrulanamadı. Astral bağlantı motoru çalışıyor görünüyor ancak bu denemede hazır logu veya adapter trafik artışı alınamadı; onarım akışını çalıştırıp tekrar deneyin.");
        }

        if (!_lastTunnelReadiness.BlocksConnection)
        {
            throw new InvalidOperationException(
                "Astral tünel bağlantısı doğrulanamadı. Cloudflare WARP tüneli bağlantıyı tamamlamadı; farklı ağ, DNS veya ISP engeli olabilir.");
        }

        if (!UsesVirtualAdapterMode())
        {
            throw new InvalidOperationException(
                "Astral bağlantı motoru hazır duruma geçmedi. Bağlantı motorunu onarın veya bilgisayarı yeniden başlatıp tekrar bağlanın.");
        }

        throw new InvalidOperationException(
            "Astral bağlantı motoru hazır duruma geçmedi. Bağlantı motorunu onarın veya bilgisayarı yeniden başlatıp tekrar bağlanın.");
    }

    private Dictionary<string, string?> CreateTunnelReadinessDetails()
    {
        var details = _lastTunnelReadiness.ToDiagnosticDetails();
        var receivedDelta = CalculateCounterDelta(
            _wireSockTrafficBaselineBytesReceived,
            _lastTunnelReadiness.WireSockAdapterBytesReceived);
        var sentDelta = CalculateCounterDelta(
            _wireSockTrafficBaselineBytesSent,
            _lastTunnelReadiness.WireSockAdapterBytesSent);
        var trafficDeltaObserved =
            _lastWireSockTrafficDeltaObserved
            || receivedDelta > 0
            || sentDelta > 0;

        details["wireSockMode"] = _lastWireSockMode;
        details["wireSockConnectionEstablished"] =
            _lastWireSockConnectionEstablished.ToString();
        details["wireSockHandshakeDiagnostic"] =
            _lastWireSockHandshakeDiagnostic;
        details["wireSockTrafficDeltaObserved"] =
            trafficDeltaObserved.ToString();
        details["wireSockTrafficDiagnostic"] =
            trafficDeltaObserved ? null : _lastWireSockTrafficDiagnostic;
        if (_wireSockTrafficBaselineBytesReceived is not null)
        {
            details["wireSockTrafficBaselineBytesReceived"] =
                _wireSockTrafficBaselineBytesReceived.Value
                    .ToString(CultureInfo.InvariantCulture);
        }

        if (_wireSockTrafficBaselineBytesSent is not null)
        {
            details["wireSockTrafficBaselineBytesSent"] =
                _wireSockTrafficBaselineBytesSent.Value
                    .ToString(CultureInfo.InvariantCulture);
        }

        if (_lastTunnelReadiness.WireSockAdapterBytesReceived is not null
            && _wireSockTrafficBaselineBytesReceived is not null)
        {
            details["wireSockTrafficDeltaBytesReceived"] =
                receivedDelta.ToString(CultureInfo.InvariantCulture);
        }

        if (_lastTunnelReadiness.WireSockAdapterBytesSent is not null
            && _wireSockTrafficBaselineBytesSent is not null)
        {
            details["wireSockTrafficDeltaBytesSent"] =
                sentDelta.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(_lastWireSockProcessId))
        {
            details["wireSockProcessId"] = _lastWireSockProcessId;
        }

        if (_lastWireSockProcessExited is not null)
        {
            details["wireSockProcessExited"] =
                _lastWireSockProcessExited.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(_lastWireSockProcessExitCode))
        {
            details["wireSockProcessExitCode"] =
                _lastWireSockProcessExitCode;
        }

        return details;
    }

    private bool TryMarkTunnelReadyForCurrentMode(
        int attempt,
        bool enforceApplicationTunnelProof)
    {
        if (_lastWireSockProcessExited == true)
        {
            return false;
        }

        if (!_lastTunnelReadiness.BlocksConnection
            && IsWireSockHandshakeReady())
        {
            return true;
        }

        if (UsesVirtualAdapterMode())
        {
            return false;
        }

        if (attempt < MinTransparentReadinessChecks)
        {
            return false;
        }

        if (_lastWebProxyProof.Required && _lastWebProxyProof.IsVerified)
        {
            var routingPlan = CurrentRoutingPlan;
            var planRequiresApplicationTunnelProof =
                RoutingPlanRequiresApplicationTunnelProof(routingPlan);
            var requiresApplicationTunnelProof =
                enforceApplicationTunnelProof
                && planRequiresApplicationTunnelProof;
            if (requiresApplicationTunnelProof && !IsWireSockHandshakeReady())
            {
                if (!_lastTunnelReadiness.BlocksConnection
                    && HasWireSockTrafficDelta())
                {
                    _lastTunnelReadiness = TunnelReadinessSnapshot.TransparentProcessRunning(
                        _lastTunnelReadiness,
                        "WireSock transparent mode is running; adapter traffic and scoped web proxy target proof were observed.");

                    _lastWireSockHandshakeDiagnostic =
                        "WireSock log marker was not observed; adapter traffic plus scoped web proxy proof confirmed app/web readiness.";

                    return true;
                }

                _lastWireSockHandshakeDiagnostic =
                    "Seçili uygulama hedefleri için uygulama tünel kanıtı alınamadı; scoped web proxy kanıtı yalnızca web rotalarını doğrular ve app bağlantısı yerine geçmez.";
                return false;
            }

            _lastTunnelReadiness = TunnelReadinessSnapshot.TransparentProcessRunning(
                _lastTunnelReadiness,
                requiresApplicationTunnelProof
                    ? "WireSock transparent mode is running; adapter readiness and scoped web proxy target proof were observed."
                    : planRequiresApplicationTunnelProof
                        ? "WireSock transparent mode is running; scoped web proxy target proof was observed while application proof waits for user action."
                    : "WireSock transparent mode is running; scoped web proxy target proof was observed.");

            _lastWireSockHandshakeDiagnostic =
                requiresApplicationTunnelProof
                    ? "WireSock log marker was not observed; adapter readiness plus scoped web proxy proof confirmed app/web readiness."
                    : planRequiresApplicationTunnelProof
                        ? "WireSock log marker was not observed; scoped web proxy proof confirmed web readiness while application proof remains pending."
                    : "WireSock log marker was not observed; scoped web proxy target proof confirmed readiness.";

            return true;
        }

        if (IsWireSockHandshakeReady())
        {
            _lastTunnelReadiness = TunnelReadinessSnapshot.TransparentProcessRunning(
                _lastTunnelReadiness,
                "WireSock transparent mode is running; tunnel-started log marker was observed.");

            _lastWireSockHandshakeDiagnostic = null;

            return true;
        }

        if (!_lastTunnelReadiness.BlocksConnection
            && HasWireSockTrafficDelta())
        {
            _lastTunnelReadiness = TunnelReadinessSnapshot.TransparentProcessRunning(
                _lastTunnelReadiness,
                "WireSock transparent mode is running; adapter traffic increased after process start.");

            _lastWireSockHandshakeDiagnostic =
                "WireSock log marker was not observed; adapter traffic delta confirmed readiness.";

            return true;
        }

        return false;
    }

    private void CaptureWireSockTrafficBaseline()
    {
        _wireSockTrafficBaselineBytesReceived = null;
        _wireSockTrafficBaselineBytesSent = null;
        _lastWireSockTrafficDeltaObserved = false;
        try
        {
            var baseline = _tunnelReadinessProbe.Capture();
            _wireSockTrafficBaselineBytesReceived =
                baseline.WireSockAdapterBytesReceived;
            _wireSockTrafficBaselineBytesSent =
                baseline.WireSockAdapterBytesSent;
            _lastWireSockTrafficDiagnostic =
                _wireSockTrafficBaselineBytesReceived is null
                    && _wireSockTrafficBaselineBytesSent is null
                    ? "WireSock adapter traffic baseline is unavailable."
                    : "WireSock adapter traffic baseline captured before process start.";
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException)
        {
            _lastWireSockTrafficDiagnostic =
                AstralDiagnostics.RedactForLog(exception.Message);
        }
    }

    private bool HasWireSockTrafficDelta()
    {
        if (!_lastTunnelReadiness.WireSockAdapterDetected
            || !_lastTunnelReadiness.WireSockAdapterUp)
        {
            _lastWireSockTrafficDeltaObserved = false;
            _lastWireSockTrafficDiagnostic =
                "WireSock adapter traffic delta is unavailable because adapter is not ready.";
            return false;
        }

        var receivedDelta = CalculateCounterDelta(
            _wireSockTrafficBaselineBytesReceived,
            _lastTunnelReadiness.WireSockAdapterBytesReceived);
        var sentDelta = CalculateCounterDelta(
            _wireSockTrafficBaselineBytesSent,
            _lastTunnelReadiness.WireSockAdapterBytesSent);
        if (receivedDelta > 0 || sentDelta > 0)
        {
            _lastWireSockTrafficDeltaObserved = true;
            _lastWireSockTrafficDiagnostic = null;
            return true;
        }

        _lastWireSockTrafficDeltaObserved = false;
        _lastWireSockTrafficDiagnostic =
            "WireSock adapter counters did not increase after process start.";
        return false;
    }

    private static long CalculateCounterDelta(long? baseline, long? current)
    {
        if (baseline is null || current is null || current.Value <= baseline.Value)
        {
            return 0;
        }

        return current.Value - baseline.Value;
    }

    private void CaptureWireSockProcessState()
    {
        var process = _wireSockProcess;
        if (process is null)
        {
            return;
        }

        _lastWireSockProcessId =
            process.ProcessId.ToString(CultureInfo.InvariantCulture);
        _lastWireSockProcessExited = process.HasExited;
        if (process.HasExited)
        {
            _lastWireSockProcessExitCode =
                process.ExitCode?.ToString(CultureInfo.InvariantCulture)
                ?? "unknown";
            return;
        }

        _lastWireSockProcessExitCode = null;
    }

    private bool IsWireSockHandshakeReady()
    {
        return _lastWireSockConnectionEstablished;
    }

    private bool UsesVirtualAdapterMode() =>
        string.Equals(
            _lastWireSockMode,
            WireSockVirtualAdapterMode,
            StringComparison.OrdinalIgnoreCase);

    private bool HasWireSockConnectionEstablished(long startPosition)
    {
        try
        {
            if (!File.Exists(_paths.TunnelLog))
            {
                _lastWireSockHandshakeDiagnostic =
                    "WireSock log is not available.";
                return false;
            }

            using var stream = new FileStream(
                _paths.TunnelLog,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= startPosition)
            {
                _lastWireSockHandshakeDiagnostic =
                    "WireSock log has no new entries for this connection attempt.";
                return false;
            }

            var readStart = Math.Max(
                startPosition,
                stream.Length - MaxWireSockLogReadBytes);
            stream.Seek(readStart, SeekOrigin.Begin);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);
            var recentLog = reader.ReadToEnd();
            if (WireSockConnectionEstablishedMessages.Any(message =>
                    recentLog.Contains(message, StringComparison.OrdinalIgnoreCase)))
            {
                _lastWireSockHandshakeDiagnostic = null;
                return true;
            }

            var failureMessage = WireSockConnectionFailureMessages.FirstOrDefault(message =>
                recentLog.Contains(message, StringComparison.OrdinalIgnoreCase));
            _lastWireSockHandshakeDiagnostic = failureMessage is null
                ? "WireSock log does not include a tunnel-started entry for this attempt."
                : "WireSock log indicates startup failure: " + failureMessage;
            return false;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            _lastWireSockHandshakeDiagnostic =
                AstralDiagnostics.RedactForLog(exception.Message);
            return false;
        }
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path)
                ? new FileInfo(path).Length
                : 0;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return 0;
        }
    }

    private static Task<IReadOnlyList<string>> PrepareWireSockArgumentsAsync(
        string wireSockExecutable,
        string profilePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<string>>([
            "run",
            "-config",
            profilePath,
            "-log-level",
            "info"
        ]);
    }

    private static async Task<string> ComputeRoutingProfileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        var safeLines = new List<string>();
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("AllowedApps", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("#@ws:AllowedApps", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("DNS", StringComparison.OrdinalIgnoreCase))
            {
                safeLines.Add(trimmed);
            }
        }

        var projection = string.Join("\n", safeLines.Order(StringComparer.Ordinal));
        var bytes = Encoding.UTF8.GetBytes(projection);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private void WriteWireSockProcessMarker(string profilePath)
    {
        if (_wireSockProcess is null)
        {
            return;
        }

        try
        {
            _paths.EnsureDirectories();
            var marker = new WireSockProcessMarker(
                _wireSockProcess.ProcessId,
                _wireSockProcess.StartTime,
                profilePath);
            File.WriteAllText(
                _paths.WireSockProcessMarker,
                JsonSerializer.Serialize(marker));
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            _diagnostics.Warning(
                "controller.wiresock.marker",
                "WireSock süreç işareti yazılamadı.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
    }

    private async Task StopOwnedOrphanWireSockProcessAsync(
        CancellationToken cancellationToken)
    {
        if (_wireSockProcess is not null
            || !File.Exists(_paths.WireSockProcessMarker))
        {
            return;
        }

        WireSockProcessMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<WireSockProcessMarker>(
                await File.ReadAllTextAsync(
                    _paths.WireSockProcessMarker,
                    cancellationToken));
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Eski WireSock süreç işareti okunamadı.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
            return;
        }

        if (marker is null || marker.ProcessId <= 0)
        {
            DeleteWireSockProcessMarker();
            return;
        }

        var deleteMarker = false;
        try
        {
            using var process = Process.GetProcessById(marker.ProcessId);
            process.Refresh();
            if (!string.Equals(
                    process.ProcessName,
                    "wiresock-client",
                    StringComparison.OrdinalIgnoreCase)
                || !IsSameProcessStart(process, marker.StartTime))
            {
                deleteMarker = true;
                return;
            }

            if (!IsOwnedWireSockProfilePath(marker.ProfilePath))
            {
                _diagnostics.Warning(
                    "controller.wiresock.orphan",
                    "Eski WireSock süreç işareti beklenen Astral profiline ait değil.",
                    new Dictionary<string, string?>
                    {
                        ["processId"] = marker.ProcessId.ToString(CultureInfo.InvariantCulture),
                        ["profilePath"] = marker.ProfilePath
                    });
                deleteMarker = true;
                return;
            }

            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Önceki oturumdan kalan Astral WireSock süreci kapatılıyor.",
                new Dictionary<string, string?>
                {
                    ["processId"] = marker.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["profilePath"] = marker.ProfilePath
                });
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            await process.WaitForExitAsync(linked.Token);
            deleteMarker = true;
        }
        catch (ArgumentException)
        {
            deleteMarker = true;
        }
        catch (InvalidOperationException)
        {
            deleteMarker = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Eski WireSock süreci kapatma zaman aşımına uğradı; sonraki açılışta tekrar denenecek.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Eski WireSock süreci otomatik kapatılamadı; sonraki açılışta tekrar denenecek.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
        finally
        {
            if (deleteMarker)
            {
                DeleteWireSockProcessMarker();
            }
        }
    }

    private bool IsOwnedWireSockProfilePath(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(profilePath);
            return string.Equals(
                    fullPath,
                    Path.GetFullPath(_paths.ScopedProfile),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    fullPath,
                    Path.GetFullPath(_paths.LegacyDiscordProfile),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSameProcessStart(
        Process process,
        DateTimeOffset? expectedStartTime)
    {
        if (expectedStartTime is null)
        {
            return false;
        }

        try
        {
            var actual = new DateTimeOffset(process.StartTime);
            return Math.Abs((actual - expectedStartTime.Value).TotalSeconds) < 2;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void DeleteWireSockProcessMarker()
    {
        try
        {
            if (File.Exists(_paths.WireSockProcessMarker))
            {
                File.Delete(_paths.WireSockProcessMarker);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Warning(
                "controller.wiresock.marker",
                "WireSock süreç işareti silinemedi.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
    }

    private static Dictionary<string, string?> CreateAllowedApplicationDiagnostics(
        RoutingPlan routingPlan)
    {
        var allowedApplications = routingPlan.AllowedApplications;
        var pathCount = allowedApplications.Count(Path.IsPathRooted);
        var bareProcessCount = allowedApplications.Count(app => !Path.IsPathRooted(app));
        var browserPathCount = allowedApplications.Count(RoutingPlan.IsBrowserExecutable);
        var discordPathCount = allowedApplications.Count(IsDiscordExecutable);
        var broadBrowserNameCount = allowedApplications.Count(app =>
            !Path.IsPathRooted(app) && RoutingPlan.IsBrowserExecutable(app));
        var applicationTunnelProofRequired =
            RoutingPlanRequiresApplicationTunnelProof(routingPlan);

        return new Dictionary<string, string?>
        {
            ["allowedApplicationCount"] =
                allowedApplications.Count.ToString(CultureInfo.InvariantCulture),
            ["allowedApplicationPathCount"] =
                pathCount.ToString(CultureInfo.InvariantCulture),
            ["allowedApplicationBareNameCount"] =
                bareProcessCount.ToString(CultureInfo.InvariantCulture),
            ["allowedDiscordApplicationCount"] =
                discordPathCount.ToString(CultureInfo.InvariantCulture),
            ["allowedBrowserApplicationCount"] =
                browserPathCount.ToString(CultureInfo.InvariantCulture),
            ["allowedBareBrowserNameCount"] =
                broadBrowserNameCount.ToString(CultureInfo.InvariantCulture),
            ["selectedTargets"] = routingPlan.Summary,
            ["webProxy"] = routingPlan.RequiresWebProxy ? "included" : "not-required",
            ["proxyRuleCount"] =
                routingPlan.ProxyRules.Count.ToString(CultureInfo.InvariantCulture),
            ["applicationTunnelProofRequired"] =
                applicationTunnelProofRequired.ToString(),
            ["browserProcessScope"] = "excluded"
        };
    }

    private static string CreateRoutingScopeDescription(RoutingPlan routingPlan)
    {
        var requiresApplicationTunnelProof =
            RoutingPlanRequiresApplicationTunnelProof(routingPlan);

        if (routingPlan.RequiresWebProxy)
        {
            return requiresApplicationTunnelProof
                ? $"{routingPlan.Summary}: seçili uygulama hedefleri ve Astral.WebProxy kapsamda; genel tarayıcı süreçleri profile eklenmez."
                : $"{routingPlan.Summary}: yalnızca Astral.WebProxy kapsamda; genel tarayıcı süreçleri profile eklenmez.";
        }

        return requiresApplicationTunnelProof
            ? $"{routingPlan.Summary}: yalnızca seçili uygulama hedefleri kapsamda."
            : $"{routingPlan.Summary}: web proxy dışı uygulama hedefi yok.";
    }

    private static bool RoutingPlanRequiresApplicationTunnelProof(
        RoutingPlan routingPlan)
    {
        return routingPlan.SelectedTargets.Any(target => target.HasApplicationScope)
            || routingPlan.AllowedApplications.Any(IsApplicationTunnelExecutable);
    }

    private static bool RoutingPlanRequiresDiscordProcessRefresh(
        RoutingPlan routingPlan)
    {
        return routingPlan.SelectedTargets.Any(target =>
                target.Id.Equals(TargetIds.Discord, StringComparison.OrdinalIgnoreCase))
            && routingPlan.AllowedApplications.Any(IsDiscordExecutable);
    }

    private static bool IsApplicationTunnelExecutable(string application)
    {
        if (string.IsNullOrWhiteSpace(application))
        {
            return false;
        }

        var fileName = Path.GetFileName(application.Trim().Trim('"'));
        return !string.Equals(
            fileName,
            ScopedWebProxyExecutableName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRoutingSummary(
        IReadOnlyDictionary<string, string?> summary)
    {
        if (summary.Count == 0)
        {
            return "henüz profil hazırlanmadı";
        }

        return string.Join(
            "; ",
            summary
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static bool AreSelectionsEquivalent(
        TargetSelection left,
        TargetSelection right)
    {
        return left.SelectedTargetIds.SequenceEqual(
                right.SelectedTargetIds,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDiscordExecutable(string value)
    {
        var name = Path.GetFileName(value);
        return name.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DiscordPTB.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DiscordCanary.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DiscordDevelopment.exe", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TargetProcessRefreshResult(
        bool Required,
        bool Refreshed,
        bool RequiresManualAction,
        string Policy,
        string Message,
        string NextAction,
        int RunningProcessCount,
        int KnownExecutablePathCount,
        string? FailureKind = null,
        string? Diagnostic = null)
    {
        public static TargetProcessRefreshResult NotRun() =>
            new(
                Required: false,
                Refreshed: false,
                RequiresManualAction: false,
                Policy: "not-started-by-astral",
                Message: "Target process refresh has not run.",
                NextAction: "Seçili hedefleri şimdi açabilirsiniz.",
                RunningProcessCount: 0,
                KnownExecutablePathCount: 0);

        public static TargetProcessRefreshResult NotRequired() =>
            new(
                Required: false,
                Refreshed: false,
                RequiresManualAction: false,
                Policy: "not-required",
                Message: "Seçili hedefler için uygulama yenilemesi gerekmiyor.",
                NextAction: "Seçili hedefleri şimdi açabilirsiniz.",
                RunningProcessCount: 0,
                KnownExecutablePathCount: 0);

        public static TargetProcessRefreshResult NotRunning(
            DiscordProcessSnapshot snapshot) =>
            new(
                Required: true,
                Refreshed: false,
                RequiresManualAction: true,
                Policy: "not-started-by-astral",
                Message: "Discord kapalı; Astral uygulamayı kendisi başlatmadı.",
                NextAction: "Discord'u şimdi açabilirsiniz.",
                RunningProcessCount: snapshot.RunningProcessCount,
                KnownExecutablePathCount: snapshot.KnownExecutablePathCount);

        public static TargetProcessRefreshResult Succeeded(
            DiscordProcessSnapshot snapshot,
            string message) =>
            new(
                Required: true,
                Refreshed: true,
                RequiresManualAction: false,
                Policy: "refreshed-running-discord",
                Message: string.IsNullOrWhiteSpace(message)
                    ? "Çalışan Discord tünel kapsamı için yenilendi."
                    : message,
                NextAction: "Çalışan Discord yenilendi. Seçili hedefleri kullanabilirsiniz.",
                RunningProcessCount: snapshot.RunningProcessCount,
                KnownExecutablePathCount: snapshot.KnownExecutablePathCount);

        public static TargetProcessRefreshResult ManualActionRequired(
            DiscordProcessSnapshot snapshot,
            DiscordRestartResult result) =>
            new(
                Required: true,
                Refreshed: false,
                RequiresManualAction: true,
                Policy: "manual-restart-required",
                Message: result.Message,
                NextAction: "Discord'u kapatıp yeniden açın; ardından bağlantıyı tekrar kontrol edin.",
                RunningProcessCount: snapshot.RunningProcessCount,
                KnownExecutablePathCount: snapshot.KnownExecutablePathCount,
                FailureKind: result.FailureKind.ToString(),
                Diagnostic: result.Diagnostic);

        public Dictionary<string, string?> ToDiagnosticDetails() =>
            new Dictionary<string, string?>
            {
                ["targetProcessRefresh.required"] = Required.ToString(),
                ["targetProcessRefresh.refreshed"] = Refreshed.ToString(),
                ["targetProcessRefresh.manualActionRequired"] =
                    RequiresManualAction.ToString(),
                ["targetProcessRefresh.policy"] = Policy,
                ["targetProcessRefresh.message"] = Message,
                ["targetProcessRefresh.runningProcessCount"] =
                    RunningProcessCount.ToString(CultureInfo.InvariantCulture),
                ["targetProcessRefresh.knownExecutablePathCount"] =
                    KnownExecutablePathCount.ToString(CultureInfo.InvariantCulture),
                ["targetProcessRefresh.failureKind"] = FailureKind,
                ["targetProcessRefresh.diagnostic"] = Diagnostic
            };
    }

    private sealed record WireSockProcessMarker(
        int ProcessId,
        DateTimeOffset? StartTime,
        string ProfilePath);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_wireSockProcess is null)
            {
                _diagnostics.Info("controller.disconnect", "Aktif WireSock süreci yok; bağlantı koruması etkinleştiriliyor.");
                var webProxyCleared =
                    await TryClearWebProxyScopeAsync("DisconnectWithoutProcess");
                var tunnelScopeCleared =
                    await TryClearTunnelScopeAsync("DisconnectWithoutProcess");
                var accessLockEnabled =
                    await TryEnableAccessLockAsync("DisconnectWithoutProcess");
                if (!webProxyCleared || !tunnelScopeCleared)
                {
                    SetDisconnectedCleanupFailure(
                        "DisconnectWithoutProcess",
                        webProxyCleared,
                        tunnelScopeCleared,
                        accessLockEnabled);
                    return;
                }

                if (!accessLockEnabled)
                {
                    SetDisconnectedProtectionFailure("DisconnectWithoutProcess");
                    return;
                }
                SetStatus(TunnelState.Disconnected, "Astral Bağlı Değil");
                return;
            }

            _intentionalStop = true;
            SetStatus(TunnelState.Disconnecting, "Bağlantı kapatılıyor");
            _diagnostics.Info("controller.disconnect", "Bağlantı kapatılıyor.");

            if (_wireSockProcess is not null)
            {
                await _wireSockProcess.StopAsync(
                    TimeSpan.FromSeconds(3),
                    cancellationToken);
            }

            await DisposeProcessAsync();
            var disconnectWebProxyCleared =
                await TryClearWebProxyScopeAsync("Disconnect");
            var disconnectTunnelScopeCleared =
                await TryClearTunnelScopeAsync("Disconnect");
            var disconnectAccessLockEnabled =
                await TryEnableAccessLockAsync("Disconnect");
            if (!disconnectWebProxyCleared || !disconnectTunnelScopeCleared)
            {
                SetDisconnectedCleanupFailure(
                    "Disconnect",
                    disconnectWebProxyCleared,
                    disconnectTunnelScopeCleared,
                    disconnectAccessLockEnabled);
                return;
            }

            if (!disconnectAccessLockEnabled)
            {
                SetDisconnectedProtectionFailure("Disconnect");
                return;
            }

            SetStatus(TunnelState.Disconnected, "Astral Bağlı Değil");
            _lastNextAction = null;
            _diagnostics.WriteHealth("kapalı", new Dictionary<string, string?>
            {
                ["accessLock"] = "enabled"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(
                TunnelState.Error,
                "Bağlantı kapatma işlemi iptal edildi.");
            throw;
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Disconnect", exception.ToString());
            _diagnostics.WriteHealth(
                "hata",
                new Dictionary<string, string?>
                {
                    ["operation"] = "disconnect",
                    ["message"] = exception.Message
                });
            SetStatus(
                TunnelState.Error,
                "Bağlantı kapatılamadı.",
                exception.ToString());
        }
        finally
        {
            _intentionalStop = false;
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (!await _operationGate.WaitAsync(CleanupPhaseTimeout))
        {
            _diagnostics.Warning(
                "controller.cleanup.timeout",
                "Denetleyici kapanisi devam eden islem nedeniyle zaman asimina ugradi.",
                new Dictionary<string, string?>
                {
                    ["operation"] = "dispose",
                    ["phase"] = "operation-gate",
                    ["timeoutMs"] = CleanupPhaseTimeout.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture)
                });
            throw new TimeoutException(
                "Controller cleanup could not acquire the operation gate before timeout.");
        }
        var cleanupComplete = false;
        try
        {
            var requiresTunnelCleanup = _wireSockProcess is not null
                || _snapshot.State is not TunnelState.Disconnected;
            var requiresAccessLockRefresh = requiresTunnelCleanup
                || !_accessLockConfirmed;
            _intentionalStop = true;

            if (_wireSockProcess is not null)
            {
                await _wireSockProcess.StopAsync(
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None);
                await DisposeProcessAsync();
            }

            if (requiresTunnelCleanup)
            {
                await TryClearWebProxyScopeAsync("Dispose");
                await TryClearTunnelScopeAsync("Dispose");
            }

            if (requiresAccessLockRefresh)
            {
                await TryEnableAccessLockAsync("Dispose");
            }

            _disposed = true;
            cleanupComplete = true;
        }
        finally
        {
            _operationGate.Release();
            if (cleanupComplete)
            {
                _operationGate.Dispose();
            }
        }
    }

    private async void OnWireSockExited(object? sender, EventArgs e)
    {
        if (_intentionalStop || _disposed)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                if (_intentionalStop || _disposed)
                {
                    return;
                }

                var exitCode = _wireSockProcess?.ExitCode;
                await DisposeProcessAsync();
                await TryClearWebProxyScopeAsync("WireSockExited");
                await TryClearTunnelScopeAsync("WireSockExited");
                await TryEnableAccessLockAsync("WireSockExited");
                WriteDiagnostic(
                    "WireSockExited",
                    $"WireSock exit code: " +
                    $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
                _diagnostics.WriteHealth(
                    "hata",
                    new Dictionary<string, string?>
                    {
                        ["operation"] = "WireSockExited",
                        ["exitCode"] = exitCode?.ToString(CultureInfo.InvariantCulture)
                            ?? "unknown"
                    });
                SetStatus(
                    TunnelState.Error,
                    "Tünel beklenmedik şekilde kapandı.",
                    $"WireSock exit code: " +
                    $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task DisposeProcessAsync()
    {
        if (_wireSockProcess is null)
        {
            return;
        }

        _wireSockProcess.Exited -= OnWireSockExited;
        var process = _wireSockProcess;
        await process.DisposeAsync();
        _wireSockProcess = null;
        if (process.ExitConfirmed)
        {
            DeleteWireSockProcessMarker();
        }
        else
        {
            _diagnostics.Warning(
                "controller.wiresock.marker",
                "WireSock kapanışı doğrulanamadı; süreç işareti sonraki açılış için korunuyor.");
        }
    }

    private async Task<bool> TryEnableAccessLockAsync(string operation)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CleanupPhaseTimeout);
            await _accessLock.EnableAsync(CurrentRoutingPlan, timeout.Token);
            _accessLockConfirmed = true;
            return true;
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or TimeoutException)
        {
            WriteDiagnostic(
                operation + "AccessLockTimeout",
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Access lock cleanup timed out."
                    : exception.Message);
            _diagnostics.Warning(
                "controller.cleanup.timeout",
                "Erisim kilidi temizleme fazi zaman asimina ugradi.",
                new Dictionary<string, string?>
                {
                    ["operation"] = operation,
                    ["phase"] = "access-lock",
                    ["timeoutMs"] = CleanupPhaseTimeout.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture)
                });
            return false;
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "AccessLock", exception.ToString());
            return false;
        }
    }

    private void SetDisconnectedProtectionFailure(string operation)
    {
        _diagnostics.WriteHealth(
            "koruma guncellenemedi",
            new Dictionary<string, string?>
            {
                ["operation"] = operation,
                ["accessLock"] = "not-confirmed",
                ["webProxy"] = "cleared",
                ["tunnelScope"] = "clear-attempted"
            });
        SetStatus(
            TunnelState.Error,
            "Baglanti kapandi, hedef korumasi dogrulanamadi.",
            "Hedef korumasi yeniden etkinlestirilemedi. Onar veya Profili Temizle akisini calistirin.");
    }

    private void SetDisconnectedCleanupFailure(
        string operation,
        bool webProxyCleared,
        bool tunnelScopeCleared,
        bool accessLockEnabled)
    {
        _diagnostics.WriteHealth(
            "temizlik dogrulanamadi",
            new Dictionary<string, string?>
            {
                ["operation"] = operation,
                ["accessLock"] = accessLockEnabled ? "enabled" : "not-confirmed",
                ["webProxy"] = webProxyCleared ? "cleared" : "not-confirmed",
                ["tunnelScope"] = tunnelScopeCleared ? "cleared" : "not-confirmed"
            });
        SetStatus(
            TunnelState.Error,
            "Baglanti kapandi, hedef temizligi dogrulanamadi.",
            "Web proxy veya tunel kapsami temizligi dogrulanamadi. Onar veya Profili Temizle akisini calistirin.");
    }

    private async Task<bool> TryClearWebProxyScopeAsync(string operation)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CleanupPhaseTimeout);
            await _webProxyService.ClearAsync(timeout.Token);
            return true;
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or TimeoutException)
        {
            WriteDiagnostic(
                operation + "WebProxyScopeTimeout",
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Web proxy cleanup timed out."
                    : exception.Message);
            _diagnostics.Warning(
                "controller.cleanup.timeout",
                "Web proxy temizleme fazi zaman asimina ugradi.",
                new Dictionary<string, string?>
                {
                    ["operation"] = operation,
                    ["phase"] = "web-proxy",
                    ["timeoutMs"] = CleanupPhaseTimeout.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture)
                });
            return false;
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "WebProxyScope", exception.ToString());
            return false;
        }
    }

    private async Task<bool> TryClearTunnelScopeAsync(string operation)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CleanupPhaseTimeout);
            await _accessLock.ClearTunnelScopeAsync(timeout.Token);
            return true;
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or TimeoutException)
        {
            WriteDiagnostic(
                operation + "TunnelScopeTimeout",
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Tunnel scope cleanup timed out."
                    : exception.Message);
            _diagnostics.Warning(
                "controller.cleanup.timeout",
                "Tunnel scope temizleme fazi zaman asimina ugradi.",
                new Dictionary<string, string?>
                {
                    ["operation"] = operation,
                    ["phase"] = "tunnel-scope",
                    ["timeoutMs"] = CleanupPhaseTimeout.TotalMilliseconds
                        .ToString(CultureInfo.InvariantCulture)
                });
            return false;
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "TunnelScope", exception.ToString());
            return false;
        }
    }

    private static string CreateUserFacingConnectError(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return "Gerekli dosya indirilemedi. Bağlantı zaman aşımına uğradı; internet erişimini kontrol edip tekrar bağlanın.";
        }

        if (exception is TaskCanceledException
            && exception.InnerException is TimeoutException)
        {
            return "Gerekli dosya indirilemedi. Bağlantı zaman aşımına uğradı; internet erişimini kontrol edip tekrar bağlanın.";
        }

        if (exception is HttpRequestException httpException)
        {
            if (httpException.InnerException is SocketException socketException
                && socketException.SocketErrorCode is SocketError.HostNotFound
                    or SocketError.NoData)
            {
                return "Gerekli GitHub dosyası indirilemedi. DNS veya internet bağlantısını kontrol edip tekrar bağlanın.";
            }

            return "Gerekli dosya indirilemedi. İnternet bağlantısını kontrol edip tekrar bağlanın.";
        }

        if (exception is InvalidOperationException
            && exception.Message.Contains(
                "Hedef VPN kilidi",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Hedef bağlantı koruması güncellenemedi. Astral'ı yönetici olarak açıp tekrar deneyin; hosts dosyasını koruyan güvenlik yazılımı varsa izin verin.";
        }

        return exception.Message;
    }

    private void SetStatus(
        TunnelState state,
        string message,
        string? diagnostic = null)
    {
        _snapshot = new TunnelSnapshot(
            state,
            message,
            DateTimeOffset.Now,
            diagnostic);
        _diagnostics.Info(
            "controller.status",
            message,
            new Dictionary<string, string?>
            {
                ["state"] = state.ToString()
            });
        StatusChanged?.Invoke(this, _snapshot);
    }

    private void WriteDiagnostic(string operation, string diagnostic)
    {
        var redactedDiagnostic = AstralDiagnostics.RedactForLog(diagnostic)
            ?? string.Empty;
        _diagnostics.Failure(
            "controller." + operation,
            "Denetleyici tanılaması yazıldı.",
            details: new Dictionary<string, string?>
            {
                ["diagnostic"] = redactedDiagnostic
            });

        try
        {
            _paths.EnsureDirectories();
            File.AppendAllText(
                _paths.ErrorLog,
                $"[{DateTimeOffset.Now:O}] {operation}{Environment.NewLine}" +
                $"{redactedDiagnostic}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class CallbackProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value)
        {
            report(value);
        }
    }
}
