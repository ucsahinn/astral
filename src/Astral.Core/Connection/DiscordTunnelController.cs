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

    private readonly AppPaths _paths;
    private readonly DiscordAppScope _discordScope;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly IProfileProvisioner _provisioner;
    private readonly IProcessLauncher _processLauncher;
    private readonly IDiscordAccessLock _accessLock;
    private readonly IAstralDiagnostics _diagnostics;
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
        _ = discordProcessManager;
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
            ["targetLaunchPolicy"] = "not-started-by-astral",
            ["profilePath"] = _lastProfilePath,
            ["routingProfileSha256"] = _lastRoutingProfileSha256,
            ["profileHashScope"] = "sanitized-routing-lines",
            ["tunnelLog"] = _paths.TunnelLog,
            ["routingSummaryKind"] = "last-applied-or-pending-profile",
            ["routingScope"] = CreateRoutingScopeDescription(routingPlan),
            ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary)
        };

        foreach (var webProxyDetail in _lastWebProxyStatus.ToDiagnosticDetails())
        {
            details[webProxyDetail.Key] = webProxyDetail.Value;
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
            await _accessLock.DisableAsync(cancellationToken);
            _accessLockConfirmed = false;
            await _accessLock.ApplyTunnelScopeAsync(
                routingPlan,
                cancellationToken);
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

            SetStatus(TunnelState.Verifying, "Hedef kapsamı doğrulanıyor");
            await VerifyTunnelReadinessAsync(
                _lastTunnelLogStartPosition,
                cancellationToken);
            LogConnectPhase("tunnel-ready");

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

            var finalState = TunnelState.Connected;
            var nextAction = "Seçili hedefleri şimdi açabilirsiniz.";
            var connectionMessage = "Sadece seçili hedefler tünelden çıkıyor";
            const string targetLaunchPolicy = "not-started-by-astral";

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
                    ["targetLaunchPolicy"] = targetLaunchPolicy,
                    ["nextAction"] = nextAction,
                    ["connectElapsedMs"] = connectStopwatch.ElapsedMilliseconds
                        .ToString(CultureInfo.InvariantCulture),
                    ["wireSockRunning"] = "True"
                }.Concat(_lastWebProxyStatus.ToDiagnosticDetails())
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

    private async Task VerifyTunnelReadinessAsync(
        long tunnelLogStartPosition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTunnelReadinessChecks; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastTunnelReadiness = _tunnelReadinessProbe.Capture();
            CaptureWireSockProcessState();
            _lastWireSockConnectionEstablished =
                HasWireSockConnectionEstablished(tunnelLogStartPosition);
            var ready = TryMarkTunnelReadyForCurrentMode(attempt);
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
            throw new InvalidOperationException(
                "WireSock bağlantı onayı alınamadı. Astral bağlantı motoru çalışıyor görünüyor ancak tünel bağlantısı tamamlandığını doğrulamadı; farklı ağ, DNS veya ISP engeli olabilir.");
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
        details["wireSockMode"] = _lastWireSockMode;
        details["wireSockConnectionEstablished"] =
            _lastWireSockConnectionEstablished.ToString();
        details["wireSockHandshakeDiagnostic"] =
            _lastWireSockHandshakeDiagnostic;
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

    private bool TryMarkTunnelReadyForCurrentMode(int attempt)
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

        if (!IsWireSockHandshakeReady())
        {
            return false;
        }

        if (attempt < MinTransparentReadinessChecks)
        {
            return false;
        }

        _lastTunnelReadiness = TunnelReadinessSnapshot.TransparentProcessRunning(
            _lastTunnelReadiness,
            "WireSock transparent mode is running; virtual adapter status is not required.");

        _lastWireSockHandshakeDiagnostic = null;

        return true;
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
            return string.Equals(
                Path.GetFullPath(profilePath),
                Path.GetFullPath(_paths.DiscordProfile),
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
            ["browserProcessScope"] = "excluded"
        };
    }

    private static string CreateRoutingScopeDescription(RoutingPlan routingPlan)
    {
        return routingPlan.RequiresWebProxy
            ? $"{routingPlan.Summary}: uygulama hedefleri ve Astral.WebProxy kapsamda; genel tarayıcı süreçleri profile eklenmez."
            : $"{routingPlan.Summary}: yalnızca seçili uygulama hedefleri kapsamda.";
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
                await TryClearWebProxyScopeAsync("DisconnectWithoutProcess");
                await TryClearTunnelScopeAsync("DisconnectWithoutProcess");
                await _accessLock.EnableAsync(CurrentRoutingPlan, cancellationToken);
                _accessLockConfirmed = true;
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
            await TryClearWebProxyScopeAsync("Disconnect");
            await TryClearTunnelScopeAsync("Disconnect");
            await _accessLock.EnableAsync(CurrentRoutingPlan, cancellationToken);
            _accessLockConfirmed = true;

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

        await _operationGate.WaitAsync();
        try
        {
            var requiresTunnelCleanup = _wireSockProcess is not null
                || _snapshot.State is not TunnelState.Disconnected;
            var requiresAccessLockRefresh = requiresTunnelCleanup
                || !_accessLockConfirmed;
            _disposed = true;
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
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
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

    private async Task TryEnableAccessLockAsync(string operation)
    {
        try
        {
            await _accessLock.EnableAsync(CurrentRoutingPlan, CancellationToken.None);
            _accessLockConfirmed = true;
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "AccessLock", exception.ToString());
        }
    }

    private async Task TryClearTunnelScopeAsync(string operation)
    {
        try
        {
            await _accessLock.ClearTunnelScopeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "TunnelScope", exception.ToString());
        }
    }

    private async Task TryClearWebProxyScopeAsync(string operation)
    {
        try
        {
            await _webProxyService.ClearAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "WebProxyScope", exception.ToString());
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
