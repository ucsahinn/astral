using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Astral.App.Installation;
using Astral.Core.Configuration;
using Astral.Core.Connection;
using Astral.Core.Diagnostics;
using Astral.Core.Maintenance;
using Astral.Core.Targets;
using Astral.Core.Updates;
using Astral.Core.WireSock;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfImage = System.Windows.Controls.Image;
using WpfPath = System.Windows.Shapes.Path;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace Astral.App;

public partial class MainWindow : Window, IDisposable
{
    internal static TimeSpan ShutdownCleanupTimeout { get; set; } =
        TimeSpan.FromSeconds(20);
    internal static TimeSpan MaintenanceCleanupTimeout { get; set; } =
        TimeSpan.FromSeconds(90);
    internal bool HasAttemptedControllerShutdownCleanup { get; private set; }

    private static readonly TimeSpan LiveDnsRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly char[] SvgViewBoxSeparators = new[] { ' ', ',' };
    internal static Action<Uri>? OpenUriOverrideForTesting { get; set; }

    private static readonly Uri RepositoryUri = new(
        "https://github.com/ucsahinn/astral");
    private static readonly Uri ReleaseNotesUri = new(
        "https://github.com/ucsahinn/astral/releases/tag/v2.2.25");
    private static readonly string LocalBackgroundVideoPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "background.mp4");

    private readonly DiscordTunnelController _controller;
    private readonly AppPaths _paths;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly AppSettingsStore _settingsStore;
    private readonly AstralCleanupService _cleanupService;
    private readonly IStartupLaunchService _startupLaunchService;
    private readonly IWireSockUninstaller _wireSockUninstaller;
    private readonly AppUpdateService _updateService;
    private readonly IAstralDiagnostics _diagnostics;
    private readonly TargetRegistry _targetRegistry = TargetRegistry.CreateDefault();
    private readonly Dictionary<string, WpfCheckBox> _targetToggles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _targetStatusLabels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _targetTestLabels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _targetStatusDots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _targetIconBadges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _targetWarningBadges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _targetOrder =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _isApplyingSettings;
    private bool _isRunInBackgroundEnabled;
    private bool _isToggleOperationRunning;
    private bool _isTargetTestRunning;
    private bool _automaticTargetTestQueued;
    private bool _isUpdateCheckRunning;
    private bool _isUpdateOperationRunning;
    private bool _isUpdateProgressPinned;
    private string? _lastLoggedUpdateProgressKey;
    private int _lastLoggedUpdateProgressPercent = -1;
    private string _lastTargetTestSummary = "Kapsam doğrulaması UI'da gösterilmez.";
    private readonly Dictionary<string, TargetProbeResult> _targetProbeResults =
        new(StringComparer.OrdinalIgnoreCase);
    private AppUpdateCheckResult? _pendingUpdate;
    private bool _isBackgroundVideoStarted;
    private DateTimeOffset _lastDnsRefreshUtc = DateTimeOffset.MinValue;
    private (string Summary, string Detail) _cachedDnsStatus = ("Okunuyor", "Makinenin aldığı DNS");
    private Forms.NotifyIcon? _trayIcon;
    private bool _hasShownTrayNotice;
    private readonly CancellationTokenSource _windowLifetimeCancellation = new();
    private CancellationTokenSource? _operationCancellation;
    private bool _allowClose;
    private bool _exitRequested;
    private bool _isClosing;
    private bool _disposed;
    private TunnelState _lastObservedTunnelState = TunnelState.Disconnected;

    public MainWindow(
        DiscordTunnelController controller,
        AppPaths paths,
        IWireSockBootstrapper wireSockBootstrapper,
        AppSettingsStore settingsStore,
        AstralCleanupService cleanupService,
        IStartupLaunchService startupLaunchService,
        IWireSockUninstaller wireSockUninstaller,
        AppUpdateService updateService,
        IAstralDiagnostics? diagnostics = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _wireSockBootstrapper = wireSockBootstrapper
            ?? throw new ArgumentNullException(nameof(wireSockBootstrapper));
        _settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        _cleanupService = cleanupService
            ?? throw new ArgumentNullException(nameof(cleanupService));
        _startupLaunchService = startupLaunchService
            ?? throw new ArgumentNullException(nameof(startupLaunchService));
        _wireSockUninstaller = wireSockUninstaller
            ?? throw new ArgumentNullException(nameof(wireSockUninstaller));
        _updateService = updateService
            ?? throw new ArgumentNullException(nameof(updateService));
        _diagnostics = diagnostics ?? NullAstralDiagnostics.Instance;

        InitializeComponent();
        RenderTargetCards();
        ApplyTargetSelectionSetting(_settingsStore.GetTargetSelection());
        ApplyRunInBackgroundSetting(
            _settingsStore.IsRunInBackgroundOnCloseEnabled());
        ApplyStartupSetting(SynchronizeStartupLaunchSetting(
            _settingsStore.IsStartWithWindowsEnabled()));
        ApplyDebugDiagnosticsSetting(_settingsStore.IsDebugDiagnosticsEnabled());
        _controller.StatusChanged += OnStatusChanged;
        ApplySnapshot(_controller.Snapshot);
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isToggleOperationRunning || _controller.Snapshot.IsBusy)
        {
            return;
        }

        _isToggleOperationRunning = true;
        ToggleButton.IsEnabled = false;

        try
        {
            if (!_controller.Snapshot.IsConnected
                && !_wireSockBootstrapper.IsSetupConsentAccepted)
            {
                var consentWindow = new WireSockConsentWindow(_wireSockBootstrapper)
                {
                    Owner = this
                };

                if (consentWindow.ShowDialog() != true)
                {
                    return;
                }

                _wireSockBootstrapper.AcceptSetupConsent();
            }

            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();

            if (_controller.Snapshot.IsConnected)
            {
                await _controller.DisconnectAsync(CancellationToken.None);
            }
            else
            {
                await _controller.ConnectAsync(_operationCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isToggleOperationRunning = false;
            ApplySnapshot(_controller.Snapshot);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = CheckForUpdatesOnStartupAsync();

        try
        {
            await _controller.EnsureDisconnectedLockAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            StopBackgroundVideo();
            return;
        }

        UpdateBackgroundVideoForSnapshot(_controller.Snapshot);
    }

    private void OnStatusChanged(object? sender, TunnelSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(TunnelSnapshot snapshot)
    {
        _lastObservedTunnelState = snapshot.State;

        if (!_isUpdateOperationRunning && snapshot.IsBusy)
        {
            _isUpdateProgressPinned = false;
        }

        if (!snapshot.IsConnected && !snapshot.IsBusy)
        {
            _automaticTargetTestQueued = false;
            _targetProbeResults.Clear();
            _lastTargetTestSummary = "Kapsam doğrulaması çalıştırılmadı.";
            if (!_isTargetTestRunning)
            {
                TargetTestSummary.Text =
                    "Kapsam durumu: seçili hedefler bağlantıya dahil edilir; Astral uygulamaları otomatik açmaz.";
            }
        }

        ToggleButton.IsEnabled = !_isToggleOperationRunning && !snapshot.IsBusy;
        ApplyRepairButtonState(snapshot);
        ApplyUpdateControls(snapshot.IsBusy);
        ApplyTargetTestSummaryState(snapshot);
        StatusMessage.Text = GetStatusMessage(snapshot);
        StatusDetail.Text = GetDetail(snapshot);
        RefreshTargetScopeView(snapshot.IsBusy || snapshot.IsConnected);
        RefreshLiveStatusCards(snapshot);
        UpdateBackgroundVideoForSnapshot(snapshot);

        var templateLabel = ToggleButton.Template.FindName(
            "ToggleButtonLabel",
            ToggleButton) as System.Windows.Controls.TextBlock;
        var powerIcon = ToggleButton.Template.FindName(
            "PowerIcon",
            ToggleButton) as System.Windows.Controls.TextBlock;
        var stateRing = ToggleButton.Template.FindName(
            "StateRing",
            ToggleButton) as System.Windows.Shapes.Ellipse;
        var coreGlow = ToggleButton.Template.FindName(
            "CoreGlow",
            ToggleButton) as System.Windows.Shapes.Ellipse;
        var outerRing = ToggleButton.Template.FindName(
            "OuterRing",
            ToggleButton) as System.Windows.Shapes.Ellipse;
        var visual = GetStateVisual(snapshot.State);

        if (templateLabel is not null)
        {
            templateLabel.Text = visual.ButtonText;
        }

        StatusLabel.Text = visual.BadgeText;
        StatusDot.Fill = visual.StatusBrush;
        StatusBadge.BorderBrush = visual.StatusBrush;
        StatusBadge.Background = visual.BadgeBackground;
        ToggleButton.BorderBrush = visual.StatusBrush;

        if (outerRing is not null)
        {
            outerRing.Effect = CreateGlowEffect(34, 0.9, visual.GlowColor);
        }

        if (powerIcon is not null)
        {
            powerIcon.Foreground = visual.PowerBrush;
            powerIcon.Effect = CreateGlowEffect(18, 0.86, visual.GlowColor);
        }

        if (stateRing is not null)
        {
            stateRing.Stroke = visual.StatusBrush;
        }

        if (coreGlow is not null)
        {
            coreGlow.Fill = visual.CoreFill;
            coreGlow.Stroke = visual.PowerBrush;
        }

        if (!_isUpdateProgressPinned)
        {
            ApplyProgress(snapshot, visual.StatusBrush);
        }
    }

    private void ApplyRepairButtonState(TunnelSnapshot snapshot)
    {
        var enabled = ShouldEnableRepair(snapshot);
        RepairButton.IsEnabled = enabled;
        RepairButton.ToolTip = snapshot.IsBusy
            ? "Devam eden işlem bitince onarım kullanılabilir."
            : enabled
                ? "Algılanan bağlantı sorunlarını onarmayı dene."
                : "Sorun algılanmadığı için onarım gerekmiyor.";
    }

    private static bool ShouldEnableRepair(TunnelSnapshot snapshot)
    {
        if (snapshot.IsBusy)
        {
            return false;
        }

        if (snapshot.State is TunnelState.Error
            or TunnelState.DiscordRestartRequired)
        {
            return true;
        }

        return false;
    }

    private void RefreshLiveStatusCards(TunnelSnapshot snapshot)
    {
        var dns = GetCachedDnsStatus();
        DnsSummary.Text = dns.Summary;
        DnsDetail.Text = dns.Detail;

        ConnectionSummary.Text = GetConnectionSummary(snapshot.State);
        ConnectionDetail.Text = GetConnectionDetail(snapshot);

        var routingPlan = _controller.CurrentRoutingPlan;
        var selectedTargetCount = _controller.TargetSelection.SelectedTargetIds.Count;
        if (snapshot.IsConnected)
        {
            ScopeSummary.Text = $"{selectedTargetCount} hedef aktif";
        }
        else
        {
            ScopeSummary.Text = $"{selectedTargetCount} hedef seçili";
        }
        ScopeSummary.ToolTip = routingPlan.Summary;
        ScopeDetail.Text = routingPlan.RequiresWebProxy
            ? "Uygulama + web hedefleri kapsamda"
            : "Yalnızca uygulama hedefleri kapsamda";
        ScopeDetail.ToolTip = routingPlan.Summary;
    }

    private (string Summary, string Detail) GetCachedDnsStatus()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastDnsRefreshUtc < LiveDnsRefreshInterval)
        {
            return _cachedDnsStatus;
        }

        _cachedDnsStatus = GetDnsStatus();
        _lastDnsRefreshUtc = now;
        return _cachedDnsStatus;
    }

    private static (string Summary, string Detail) GetDnsStatus()
    {
        try
        {
            var addresses = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(IsActiveAdapter)
                .SelectMany(GetDnsAddresses)
                .Where(IsDisplayableDnsAddress)
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();

            return addresses.Length switch
            {
                0 => ("Algılanmadı", "Windows DNS adresi yok"),
                1 => (addresses[0], "Makinenin aldığı DNS"),
                _ => (
                    string.Join(", ", addresses.Take(2)),
                    $"{addresses.Length} DNS adresi aktif")
            };
        }
        catch (NetworkInformationException)
        {
            return ("Okunamadı", "DNS bilgisi alınamadı");
        }
        catch (UnauthorizedAccessException)
        {
            return ("Okunamadı", "DNS bilgisi alınamadı");
        }
    }

    private static bool IsActiveAdapter(NetworkInterface adapter)
    {
        return adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback
            && adapter.NetworkInterfaceType is not NetworkInterfaceType.Tunnel;
    }

    private static IEnumerable<IPAddress> GetDnsAddresses(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().DnsAddresses;
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static bool IsDisplayableDnsAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && !address.IsIPv6SiteLocal;
        }

        return address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static string GetConnectionSummary(TunnelState state)
    {
        return state switch
        {
            TunnelState.DiscordRestartRequired => "Hedef kontrolü gerekiyor",
            TunnelState.Connected => "Bağlı",
            TunnelState.Verifying => "Doğrulanıyor",
            TunnelState.Preparing => "Hazırlanıyor",
            TunnelState.Connecting => "Bağlanıyor",
            TunnelState.Disconnecting => "Kapanıyor",
            TunnelState.Error => "Kontrol gerekiyor",
            _ => "Bağlı değil"
        };
    }

    private static string GetConnectionDetail(TunnelSnapshot snapshot)
    {
        var time = snapshot.ChangedAt
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.CurrentCulture);

        return snapshot.State switch
        {
            TunnelState.DiscordRestartRequired => $"Hedef bağlantısı kontrol edilmeli, {time}",
            TunnelState.Connected => $"Tünel aktif, son durum {time}",
            TunnelState.Verifying => $"Seçili hedef kapsamı kontrol ediliyor, {time}",
            TunnelState.Preparing or TunnelState.Connecting
                => $"Kurulum ve profil kontrolü, {time}",
            TunnelState.Disconnecting => $"Bağlantı kapatılıyor, {time}",
            TunnelState.Error => $"Tanılama gerekebilir, {time}",
            _ => $"Son durum {time}"
        };
    }

    private static DropShadowEffect CreateGlowEffect(
        double blurRadius,
        double opacity,
        MediaColor color)
    {
        return new DropShadowEffect
        {
            BlurRadius = blurRadius,
            Direction = 270,
            Opacity = opacity,
            ShadowDepth = 0,
            Color = color
        };
    }

    private static StateVisual GetStateVisual(TunnelState state)
    {
        return state switch
        {
            TunnelState.DiscordRestartRequired => new StateVisual(
                "ONARIM GEREKİYOR",
                "BAĞLANTIYI KES",
                MediaColor.FromRgb(249, 214, 107),
                MediaColor.FromRgb(255, 232, 145),
                MediaColor.FromRgb(82, 62, 22),
                MediaColor.FromRgb(249, 214, 107)),
            TunnelState.Connected => new StateVisual(
                "BAĞLI",
                "BAĞLANTIYI KES",
                MediaColor.FromRgb(86, 240, 123),
                MediaColor.FromRgb(128, 255, 167),
                MediaColor.FromRgb(26, 80, 48),
                MediaColor.FromRgb(86, 240, 123)),
            TunnelState.Preparing or TunnelState.Connecting or TunnelState.Verifying => new StateVisual(
                "BAĞLANIYOR",
                "BAĞLANIYOR",
                MediaColor.FromRgb(249, 214, 107),
                MediaColor.FromRgb(115, 232, 255),
                MediaColor.FromRgb(82, 62, 22),
                MediaColor.FromRgb(249, 214, 107)),
            TunnelState.Disconnecting => new StateVisual(
                "KAPANIYOR",
                "KAPANIYOR",
                MediaColor.FromRgb(255, 163, 92),
                MediaColor.FromRgb(115, 232, 255),
                MediaColor.FromRgb(86, 43, 22),
                MediaColor.FromRgb(255, 163, 92)),
            TunnelState.Error => new StateVisual(
                "SORUN VAR",
                "TEKRAR DENE",
                MediaColor.FromRgb(255, 107, 122),
                MediaColor.FromRgb(255, 176, 187),
                MediaColor.FromRgb(86, 24, 34),
                MediaColor.FromRgb(255, 68, 92)),
            _ => new StateVisual(
                "KAPALI",
                "BAĞLAN",
                MediaColor.FromRgb(255, 78, 106),
                MediaColor.FromRgb(255, 118, 135),
                MediaColor.FromRgb(90, 28, 42),
                MediaColor.FromRgb(255, 78, 106))
        };
    }

    private sealed class StateVisual
    {
        public StateVisual(
            string badgeText,
            string buttonText,
            MediaColor statusColor,
            MediaColor powerColor,
            MediaColor coreColor,
            MediaColor glowColor)
        {
            BadgeText = badgeText;
            ButtonText = buttonText;
            StatusBrush = new SolidColorBrush(statusColor);
            PowerBrush = new SolidColorBrush(powerColor);
            GlowColor = glowColor;
            CoreFill = CreateCoreFill(coreColor, powerColor);
            BadgeBackground = new SolidColorBrush(MediaColor.FromArgb(
                126,
                coreColor.R,
                coreColor.G,
                coreColor.B));
        }

        public string BadgeText { get; }

        public string ButtonText { get; }

        public SolidColorBrush StatusBrush { get; }

        public SolidColorBrush PowerBrush { get; }

        public MediaColor GlowColor { get; }

        public MediaBrush CoreFill { get; }

        public SolidColorBrush BadgeBackground { get; }

        private static RadialGradientBrush CreateCoreFill(
            MediaColor coreColor,
            MediaColor powerColor)
        {
            return new RadialGradientBrush
            {
                Center = new System.Windows.Point(0.46, 0.38),
                GradientOrigin = new System.Windows.Point(0.34, 0.25),
                RadiusX = 0.82,
                RadiusY = 0.82,
                GradientStops =
                [
                    new GradientStop(MediaColor.FromArgb(
                        62,
                        powerColor.R,
                        powerColor.G,
                        powerColor.B), 0),
                    new GradientStop(MediaColor.FromArgb(
                        39,
                        coreColor.R,
                        coreColor.G,
                        coreColor.B), 0.48),
                    new GradientStop(MediaColor.FromArgb(
                        20,
                        4,
                        7,
                        16), 1)
                ]
            };
        }
    }

    private void ApplyProgress(
        TunnelSnapshot snapshot,
        System.Windows.Media.Brush stateBrush)
    {
        var progress = GetProgress(snapshot);

        StageProgressBar.Value = progress.Value;
        StageProgressBar.Foreground = stateBrush;
        StageProgressLabel.Text = progress.Label;
        StageProgressPercent.Text = $"{progress.Value:0}%";

        ApplyStageStep(StageStepLock, progress.ActiveStep >= 1, stateBrush);
        ApplyStageStep(StageStepSetup, progress.ActiveStep >= 2, stateBrush);
        ApplyStageStep(StageStepProfile, progress.ActiveStep >= 3, stateBrush);
        ApplyStageStep(StageStepTunnel, progress.ActiveStep >= 4, stateBrush);
    }

    private static (double Value, string Label, int ActiveStep) GetProgress(
        TunnelSnapshot snapshot)
    {
        var message = snapshot.Message;
        return snapshot.State switch
        {
            TunnelState.Connected => (100, "Bağlandı", 4),
            TunnelState.DiscordRestartRequired => (96, "Hedef kontrol edilmeli", 4),
            TunnelState.Verifying => (92, "Bağlantı doğrulanıyor", 4),
            TunnelState.Connecting => (82, "Bağlantı açılıyor", 4),
            TunnelState.Disconnecting => (62, "Bağlantı kapatılıyor", 4),
            TunnelState.Error => (100, "Sorun var", 4),
            TunnelState.Preparing when message.Contains(
                    "Cloudflare",
                    StringComparison.OrdinalIgnoreCase)
                || message.Contains("WARP", StringComparison.OrdinalIgnoreCase)
                || message.Contains("wgcf", StringComparison.OrdinalIgnoreCase)
                || message.Contains("profil", StringComparison.OrdinalIgnoreCase)
                => (68, "Profil hazırlanıyor", 3),
            TunnelState.Preparing when message.Contains(
                    "Hedef bağlantısı",
                    StringComparison.OrdinalIgnoreCase)
                => (22, "Hedef bağlantısı hazırlanıyor", 1),
            TunnelState.Preparing when message.Contains(
                    "WireSock",
                    StringComparison.OrdinalIgnoreCase)
                || message.Contains("kurul", StringComparison.OrdinalIgnoreCase)
                || message.Contains("kurucu", StringComparison.OrdinalIgnoreCase)
                || message.Contains("doğrulan", StringComparison.OrdinalIgnoreCase)
                => (48, "Astral motoru doğrulanıyor", 2),
            TunnelState.Preparing => (38, "Hazırlık çalışıyor", 2),
            _ => (0, "Hazır", 0)
        };
    }

    private static void ApplyStageStep(
        System.Windows.Controls.TextBlock textBlock,
        bool isActive,
        System.Windows.Media.Brush activeBrush)
    {
        var inactiveBrush = (System.Windows.Media.Brush)
            System.Windows.Application.Current.Resources["SecondaryTextBrush"];
        textBlock.Foreground = isActive
            ? activeBrush
            : inactiveBrush;
        textBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        textBlock.Opacity = isActive ? 1 : 0.58;
    }

    private static string GetStatusMessage(TunnelSnapshot snapshot)
    {
        if (snapshot.State is TunnelState.Error)
        {
            if (snapshot.Message.Contains(
                    "Hedef bağlantı koruması",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Hedef bağlantı koruması güncellenemedi";
            }

            if (snapshot.Message.Length > 72)
            {
                return "Bağlantı tamamlanamadı";
            }
        }

        return snapshot.Message;
    }

    private static string GetDetail(TunnelSnapshot snapshot)
    {
        if (snapshot.State is TunnelState.Error
            && !string.IsNullOrWhiteSpace(snapshot.Message))
        {
            return snapshot.Message;
        }

        return GetDetail(snapshot.State);
    }

    private static string GetDetail(TunnelState state)
    {
        return state switch
        {
            TunnelState.Connected => "Sadece seçili hedefler tünelden çıkıyor.",
            TunnelState.DiscordRestartRequired => "Seçili hedef kapsamını kontrol edip tekrar bağlanın.",
            TunnelState.Verifying => "Astral tünel motoru açık, hedef kapsamı kontrol ediliyor.",
            TunnelState.Preparing => "Kurulum, dijital imza ve bağlantı profili doğrulanıyor.",
            TunnelState.Connecting => "Astral tünel motoru başlatılıyor.",
            TunnelState.Disconnecting => "Bağlantı güvenli biçimde sonlandırılıyor.",
            TunnelState.Error => "Tanı paketi oluştur veya onarım akışını başlat.",
            _ => "Astral bağlı değilken seçili hedefler düz bağlantıya çıkmaz."
        };
    }

    private void ApplyTargetSelectionSetting(TargetSelection selection)
    {
        if (!_controller.TrySetTargetSelection(selection))
        {
            selection = _controller.TargetSelection;
        }

        SyncTargetTogglesFromSelection(_controller.TargetSelection);
        RefreshTargetScopeView(_controller.Snapshot.IsBusy
            || _controller.Snapshot.IsConnected);
    }

    private void RenderTargetCards()
    {
        TargetCardsTopPanel.Children.Clear();
        TargetCardsBottomPanel.Children.Clear();
        _targetToggles.Clear();
        _targetStatusLabels.Clear();
        _targetTestLabels.Clear();
        _targetStatusDots.Clear();
        _targetIconBadges.Clear();
        _targetWarningBadges.Clear();
        _targetOrder.Clear();

        var targets = _targetRegistry.GetBuiltInTargets();
        var splitIndex = Math.Max(1, (targets.Count + 1) / 2);
        var index = 0;
        foreach (var target in targets)
        {
            var toggle = new WpfCheckBox
            {
                Name = "TargetToggle_" + CreateSafeTargetName(target.Id),
                Tag = target,
                Width = 141,
                Height = 42,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                Style = (Style)FindResource("TargetCardCheckBoxStyle"),
                ToolTip = $"{target.Label} hedefi hazır",
                Content = CreateTargetCardContent(target)
            };
            AutomationProperties.SetName(toggle, $"{target.Label} hedefi");
            toggle.Checked += TargetToggle_Changed;
            toggle.Unchecked += TargetToggle_Changed;
            toggle.PreviewMouseLeftButtonUp += TargetToggle_PreviewMouseLeftButtonUp;
            toggle.PreviewKeyDown += TargetToggle_PreviewKeyDown;
            _targetToggles[target.Id] = toggle;
            _targetOrder[target.Id] = index;
            if (index < splitIndex)
            {
                TargetCardsTopPanel.Children.Add(toggle);
            }
            else
            {
                TargetCardsBottomPanel.Children.Add(toggle);
            }

            index++;
        }
    }

    private void TargetToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        if (_controller.Snapshot.IsBusy || _controller.Snapshot.IsConnected)
        {
            SyncTargetTogglesFromSelection(_controller.TargetSelection);
            RefreshTargetScopeView(locked: true);
            return;
        }

        var selectedIds = _targetToggles
            .Where(pair => pair.Value.IsChecked == true)
            .Select(pair => pair.Key)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            _isApplyingSettings = true;
            try
            {
                if (sender is WpfCheckBox checkbox)
                {
                    checkbox.IsChecked = true;
                }
            }
            finally
            {
                _isApplyingSettings = false;
            }

            return;
        }

        var selection = new TargetSelection(selectedIds);
        if (!_controller.TrySetTargetSelection(selection))
        {
            SyncTargetTogglesFromSelection(_controller.TargetSelection);
            RefreshTargetScopeView(locked: true);
            return;
        }

        _settingsStore.SetTargetSelection(selection);
        _targetProbeResults.Clear();
        _automaticTargetTestQueued = false;
        _lastTargetTestSummary = "Kapsam doğrulaması çalıştırılmadı.";
        TargetTestSummary.Text =
            "Kapsam durumu: seçili hedefler bağlantıya dahil edilir; Astral uygulamaları otomatik açmaz.";
        _diagnostics.Info(
            "ui.targetSelection",
            "Hedef seçimi güncellendi.",
            new Dictionary<string, string?>
            {
                ["selectedTargets"] = _controller.CurrentRoutingPlan.Summary
            });
        RefreshTargetScopeView(locked: false);
        RefreshLiveStatusCards(_controller.Snapshot);
    }

    private void TargetToggle_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (TryGetTargetOpenButton(e.OriginalSource, out var openTarget))
        {
            OpenTargetWebsite(openTarget);
            e.Handled = true;
            return;
        }

        if (HandleLockedTargetActivation(sender))
        {
            e.Handled = true;
        }
    }

    private static bool TryGetTargetOpenButton(
        object originalSource,
        out TargetDefinition target)
    {
        target = null!;
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is WpfButton { Tag: TargetDefinition buttonTarget } button
                && button.Name.StartsWith("TargetOpen_", StringComparison.Ordinal))
            {
                target = buttonTarget;
                return true;
            }

            current = GetParentObject(current);
        }

        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        try
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private void TargetToggle_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space))
        {
            return;
        }

        if (TryGetTargetOpenButton(e.OriginalSource, out var openTarget))
        {
            OpenTargetWebsite(openTarget);
            e.Handled = true;
            return;
        }

        if (HandleLockedTargetActivation(sender))
        {
            e.Handled = true;
        }
    }

    private bool HandleLockedTargetActivation(object sender)
    {
        var snapshot = _controller.Snapshot;
        if (!snapshot.IsBusy && !snapshot.IsConnected)
        {
            return false;
        }

        if (sender is WpfCheckBox { Tag: TargetDefinition target }
            && snapshot.IsConnected
            && IsTargetSelected(target.Id))
        {
            OpenTargetWebsite(target);
        }

        return true;
    }

    private void SyncTargetTogglesFromSelection(TargetSelection selection)
    {
        _isApplyingSettings = true;
        try
        {
            var selectedIds = selection.SelectedTargetIds
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (targetId, toggle) in _targetToggles)
            {
                toggle.IsChecked = selectedIds.Contains(targetId);
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void RefreshTargetScopeView(bool locked)
    {
        var snapshot = _controller.Snapshot;
        _isApplyingSettings = true;
        try
        {
            foreach (var toggle in _targetToggles.Values)
            {
                toggle.IsEnabled = true;
                if (toggle.Tag is TargetDefinition target)
                {
                    var selected = toggle.IsChecked == true;
                    var state = GetTargetCardState(target, snapshot, locked, selected);
                    ApplyTargetCardState(target, state, snapshot);
                    toggle.ToolTip = CreateTargetToolTip(target, state, snapshot);
                    AutomationProperties.SetHelpText(
                        toggle,
                        $"{target.Label} hedefi {GetTargetCardStateText(state).ToLowerInvariant()}");
                }
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }

    }

    private TargetCardState GetTargetCardState(
        TargetDefinition target,
        TunnelSnapshot snapshot,
        bool locked,
        bool selected)
    {
        if (!selected)
        {
            return TargetCardState.Passive;
        }

        if (snapshot.State is TunnelState.Error
            or TunnelState.DiscordRestartRequired)
        {
            return _targetProbeResults.TryGetValue(target.Id, out var failedProbe)
                && failedProbe.Status is TargetProbeStatus.Failed
                    ? TargetCardState.Problem
                    : TargetCardState.Selected;
        }

        if (snapshot.IsConnected
            && _targetProbeResults.TryGetValue(target.Id, out var probe))
        {
            return probe.Status switch
            {
                TargetProbeStatus.Running => TargetCardState.Connecting,
                TargetProbeStatus.Success or TargetProbeStatus.ProfileScope => TargetCardState.InScope,
                TargetProbeStatus.Failed => TargetCardState.Selected,
                TargetProbeStatus.Skipped => TargetCardState.Selected,
                _ => TargetCardState.Selected
            };
        }

        return snapshot.State switch
        {
            TunnelState.Connected => TargetCardState.Selected,
            TunnelState.Preparing
                or TunnelState.Connecting
                or TunnelState.Verifying => TargetCardState.Connecting,
            _ => TargetCardState.Selected
        };
    }

    private void ApplyTargetCardState(
        TargetDefinition target,
        TargetCardState state,
        TunnelSnapshot snapshot)
    {
        if (_targetStatusLabels.GetValueOrDefault(target.Id) is { } label)
        {
            label.Text = GetTargetCardStateText(state);
            label.Foreground = GetTargetCardStateBrush(state);
        }

        if (_targetStatusDots.GetValueOrDefault(target.Id) is { } dot)
        {
            dot.Background = GetTargetCardStateBrush(state);
            dot.Opacity = state is TargetCardState.Passive ? 0.62 : 1;
        }

        if (_targetTestLabels.GetValueOrDefault(target.Id) is { } testLabel)
        {
            testLabel.Text = GetTargetTestLabel(target.Id, snapshot);
            testLabel.Foreground = _targetProbeResults.TryGetValue(target.Id, out var probe)
                ? GetTargetProbeBrush(probe.Status)
                : new SolidColorBrush(MediaColor.FromRgb(120, 148, 166));
        }

        if (_targetWarningBadges.GetValueOrDefault(target.Id) is { } warningBadge)
        {
            warningBadge.Visibility = state is TargetCardState.Problem
                && _targetProbeResults.TryGetValue(target.Id, out var probe)
                && probe.Status is TargetProbeStatus.Failed
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_targetIconBadges.GetValueOrDefault(target.Id) is not { } iconBadge)
        {
            ApplyTargetToggleState(target, state);
            return;
        }

        iconBadge.BorderBrush = state switch
        {
            TargetCardState.Problem => new SolidColorBrush(MediaColor.FromRgb(255, 122, 122)),
            TargetCardState.InScope => new SolidColorBrush(MediaColor.FromRgb(93, 255, 146)),
            TargetCardState.Selected or TargetCardState.Connecting => new SolidColorBrush(MediaColor.FromRgb(125, 235, 255)),
            _ => new SolidColorBrush(MediaColor.FromArgb(172, 245, 247, 251))
        };

        if (state is TargetCardState.Connecting)
        {
            var beginDelay = _targetOrder.TryGetValue(target.Id, out var order)
                ? TimeSpan.FromMilliseconds(order * 110)
                : TimeSpan.Zero;
            iconBadge.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation
                {
                    From = 0.72,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(700),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = beginDelay
                });
        }
        else
        {
            iconBadge.BeginAnimation(OpacityProperty, null);
            iconBadge.Opacity = state is TargetCardState.Passive ? 0.58 : 1;
        }

        ApplyTargetToggleState(target, state);
    }

    private void ApplyTargetToggleState(TargetDefinition target, TargetCardState state)
    {
        if (!_targetToggles.TryGetValue(target.Id, out var toggle))
        {
            return;
        }

        toggle.Opacity = state switch
        {
            TargetCardState.Passive => 0.52,
            TargetCardState.Problem => 0.98,
            _ => 1
        };
        toggle.BorderBrush = state switch
        {
            TargetCardState.Problem => new SolidColorBrush(MediaColor.FromRgb(255, 122, 122)),
            TargetCardState.InScope => new SolidColorBrush(MediaColor.FromRgb(93, 255, 146)),
            TargetCardState.Selected => new SolidColorBrush(MediaColor.FromRgb(93, 255, 146)),
            TargetCardState.Connecting => new SolidColorBrush(MediaColor.FromRgb(125, 235, 255)),
            _ => new SolidColorBrush(MediaColor.FromRgb(97, 126, 151))
        };
    }

    private static WpfToolTip CreateTargetToolTip(
        TargetDefinition target,
        TargetCardState state,
        TunnelSnapshot snapshot)
    {
        var stateText = GetTargetCardStateText(state);
        var scopeText = snapshot.IsConnected && state is not TargetCardState.Passive
            ? "Bağlantı açık; bu hedef kapsamda."
            : "Bağlantı açıldığında bu hedef kapsama alınır.";
        return new WpfToolTip
        {
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{target.Label} hedefi {stateText.ToLowerInvariant()}",
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = target.ScopeLabel,
                        Margin = new Thickness(0, 3, 0, 0),
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(170, 211, 226))
                    },
                    new TextBlock
                    {
                        Text = scopeText,
                        Margin = new Thickness(0, 3, 0, 0),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(170, 211, 226))
                    },
                    new TextBlock
                    {
                        Text = "Astral hedef uygulamayı otomatik açmaz; seçili uygulama ve web trafiğini kapsam içine alır.",
                        Margin = new Thickness(0, 3, 0, 0),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(170, 211, 226))
                    }
                }
            }
        };
    }

    private static string GetTargetProbeStatusText(TargetProbeStatus status)
    {
        return status switch
        {
            TargetProbeStatus.Running => "test ediliyor",
            TargetProbeStatus.Success => "başarılı",
            TargetProbeStatus.Failed => "sorunlu",
            TargetProbeStatus.ProfileScope => "profil kapsamında",
            TargetProbeStatus.Skipped => "atlandı",
            _ => "çalıştırılmadı"
        };
    }

    private static string GetTargetCardStateText(TargetCardState state)
    {
        return state switch
        {
            TargetCardState.Selected => "Seçili",
            TargetCardState.Connecting => "Bağlanıyor",
            TargetCardState.InScope => "Kapsamda",
            TargetCardState.Problem => "Sorunlu",
            TargetCardState.Passive => "Pasif",
            _ => "Hazır"
        };
    }

    private static SolidColorBrush GetTargetCardStateBrush(TargetCardState state)
    {
        return state switch
        {
            TargetCardState.Selected => new SolidColorBrush(MediaColor.FromRgb(125, 235, 255)),
            TargetCardState.Connecting => new SolidColorBrush(MediaColor.FromRgb(255, 217, 85)),
            TargetCardState.InScope => new SolidColorBrush(MediaColor.FromRgb(93, 255, 146)),
            TargetCardState.Problem => new SolidColorBrush(MediaColor.FromRgb(255, 122, 122)),
            TargetCardState.Passive => new SolidColorBrush(MediaColor.FromRgb(120, 148, 166)),
            _ => new SolidColorBrush(MediaColor.FromRgb(170, 211, 226))
        };
    }

    private string GetTargetTestLabel(string targetId, TunnelSnapshot snapshot)
    {
        if (!_targetProbeResults.TryGetValue(targetId, out var result))
        {
            return snapshot.IsConnected
                ? "Kapsam aktif"
                : "Kapsam hazır";
        }

        return result.Status switch
        {
            TargetProbeStatus.Running => "Doğrulanıyor",
            TargetProbeStatus.Success or TargetProbeStatus.ProfileScope => "Kapsam aktif",
            TargetProbeStatus.Failed => snapshot.IsConnected ? "Kapsam aktif" : "Kontrol",
            TargetProbeStatus.Skipped => "Kapsam hazır",
            _ => snapshot.IsConnected ? "Kapsam aktif" : "Kapsam hazır"
        };
    }

    private static SolidColorBrush GetTargetProbeBrush(TargetProbeStatus status)
    {
        return status switch
        {
            TargetProbeStatus.Running => new SolidColorBrush(MediaColor.FromRgb(255, 217, 85)),
            TargetProbeStatus.Success or TargetProbeStatus.ProfileScope
                => new SolidColorBrush(MediaColor.FromRgb(93, 255, 146)),
            TargetProbeStatus.Failed => new SolidColorBrush(MediaColor.FromRgb(125, 235, 255)),
            TargetProbeStatus.Skipped => new SolidColorBrush(MediaColor.FromRgb(170, 211, 226)),
            _ => new SolidColorBrush(MediaColor.FromRgb(120, 148, 166))
        };
    }

    private void QueueAutomaticTargetTestIfNeeded(
        TunnelSnapshot snapshot,
        TunnelState previousState)
    {
        if (snapshot.State is not TunnelState.Connected
            || previousState is TunnelState.Connected
            || _automaticTargetTestQueued
            || _isTargetTestRunning)
        {
            return;
        }

        var hasSelectedTarget = _controller.CurrentRoutingPlan
            .SelectedTargets
            .Any(target => target.HasWebScope
                || target.HasApplicationScope);
        if (!hasSelectedTarget)
        {
            return;
        }

        _automaticTargetTestQueued = true;
        TargetTestSummary.Text =
            "Kapsam doğrulaması: seçili hedefler sırayla ölçülecek.";
        _diagnostics.Info(
            "ui.targetTest.queued",
            "Bağlantı sonrası kapsam doğrulaması kuyruğa alındı.",
            new Dictionary<string, string?>
            {
                ["selectedTargets"] = _controller.CurrentRoutingPlan.Summary
            });

        Dispatcher.BeginInvoke(
            new Action(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(450));
                    if (_disposed
                        || _isClosing
                        || !_controller.Snapshot.IsConnected
                        || _controller.Snapshot.IsBusy)
                    {
                        return;
                    }

                    await RunSelectedTargetsProbeAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    _automaticTargetTestQueued = false;
                    if (!_disposed)
                    {
                        ApplyTargetTestSummaryState(_controller.Snapshot);
                        RefreshLiveStatusCards(_controller.Snapshot);
                    }
                }
            }),
            DispatcherPriority.Background);
    }

    private void ApplyTargetTestSummaryState(TunnelSnapshot snapshot)
    {
        if (_isTargetTestRunning)
        {
            TargetTestSummary.Text =
                "Kapsam doğrulaması çalışıyor; seçili hedefler sırayla ölçülüyor.";
            return;
        }

        var hasSelectedTarget = _controller.CurrentRoutingPlan
            .SelectedTargets
            .Any(target => target.HasWebScope
                || target.HasApplicationScope);
        if (!hasSelectedTarget)
        {
            TargetTestSummary.Text = "Kapsam durumu: seçili hedef yok.";
            return;
        }

        if (!snapshot.IsConnected)
        {
            TargetTestSummary.Text =
                "Kapsam durumu: seçili hedefler bağlantıya dahil edilir; Astral uygulamaları otomatik açmaz.";
            return;
        }

        TargetTestSummary.Text =
            "Kapsam durumu: seçili hedefler bağlantıya dahil edildi; hedef uygulamaları siz açarsınız.";
    }

    private async Task RunSelectedTargetsProbeAsync()
    {
        if (_isTargetTestRunning)
        {
            return;
        }

        var snapshot = _controller.Snapshot;
        if (!snapshot.IsConnected || snapshot.IsBusy)
        {
            TargetTestSummary.Text = "Kapsam doğrulaması için önce Astral bağlantısını açın.";
            ApplyTargetTestSummaryState(snapshot);
            return;
        }

        var selectedTargets = _controller.CurrentRoutingPlan
            .SelectedTargets
            .Where(target => _targetToggles.TryGetValue(target.Id, out var toggle)
                && toggle.IsChecked == true)
            .ToArray();
        if (selectedTargets.Length == 0)
        {
            TargetTestSummary.Text = "Kapsam doğrulaması yapılacak seçili hedef yok.";
            return;
        }

        _isTargetTestRunning = true;
        TargetTestSummary.Text =
            "Kapsam doğrulaması başladı; seçili hedefler sırayla ölçülüyor.";
        _lastTargetTestSummary = "Kapsam doğrulaması çalışıyor.";
        _diagnostics.Info(
            "ui.targetTest.autoStart",
            "Seçili hedefler için kapsam doğrulaması başlatıldı.",
            new Dictionary<string, string?>
            {
                ["selectedTargets"] = string.Join(", ", selectedTargets.Select(target => target.Label))
            });

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _windowLifetimeCancellation.Token);

        var results = new List<TargetProbeResult>();
        try
        {
            var scopeStatus = await _controller.EnsureCurrentWebProxyScopeAsync(
                "target-auto-test",
                linkedSource.Token);
            if (scopeStatus.Required && !scopeStatus.IsApplied)
            {
                foreach (var target in selectedTargets)
                {
                    var hosts = GetTargetTestHosts(target);
                    if (hosts.Length == 0)
                    {
                        var scoped = new TargetProbeResult(
                            TargetProbeStatus.ProfileScope,
                            null,
                            "Uygulama kapsamı WireSock profilinde doğrulandı.",
                            DateTimeOffset.Now);
                        _targetProbeResults[target.Id] = scoped;
                        results.Add(scoped);
                        continue;
                    }

                    var failed = new TargetProbeResult(
                        TargetProbeStatus.Failed,
                        string.Join(", ", hosts),
                        scopeStatus.Message,
                        DateTimeOffset.Now);
                    _targetProbeResults[target.Id] = failed;
                    results.Add(failed);
                }

                _lastTargetTestSummary =
                    "Scoped web kapsamı doğrulanamadı: " + scopeStatus.Message;
                TargetTestSummary.Text = _lastTargetTestSummary;
                _diagnostics.WriteHealth(
                    "kapsam doğrulaması scoped proxy kontrolünde durdu",
                    CreateTargetTestDiagnosticDetails(results)
                        .Concat(scopeStatus.ToDiagnosticDetails())
                        .ToDictionary(pair => pair.Key, pair => pair.Value));
                RefreshTargetScopeView(locked: true);
                return;
            }

            foreach (var target in selectedTargets)
            {
                linkedSource.Token.ThrowIfCancellationRequested();
                var hosts = GetTargetTestHosts(target);
                if (hosts.Length == 0)
                {
                    var scoped = new TargetProbeResult(
                        TargetProbeStatus.ProfileScope,
                        null,
                        "Uygulama kapsamı WireSock profilinde doğrulandı.",
                        DateTimeOffset.Now);
                    _targetProbeResults[target.Id] = scoped;
                    results.Add(scoped);
                    RefreshTargetScopeView(_controller.Snapshot.IsBusy
                        || _controller.Snapshot.IsConnected);
                    continue;
                }

                var running = new TargetProbeResult(
                    TargetProbeStatus.Running,
                    string.Join(", ", hosts),
                    "Kapsam doğrulaması çalışıyor",
                    DateTimeOffset.Now);
                _targetProbeResults[target.Id] = running;
                RefreshTargetScopeView(locked: true);

                var result = await ProbeTargetHostsViaScopedProxyAsync(
                    target,
                    hosts,
                    linkedSource.Token);
                _targetProbeResults[target.Id] = result;
                results.Add(result);
                RefreshTargetScopeView(locked: true);
            }

            var successCount = results.Count(result =>
                result.Status is TargetProbeStatus.Success
                    or TargetProbeStatus.ProfileScope);
            var failedCount = results.Count(result =>
                result.Status is TargetProbeStatus.Failed);
            var skippedCount = results.Count(result =>
                result.Status is TargetProbeStatus.Skipped);
            _lastTargetTestSummary =
                $"{successCount} rota OK, {failedCount} kontrol gerekli, {skippedCount} atlandı.";
            TargetTestSummary.Text = "Kapsam doğrulaması: " + _lastTargetTestSummary;
            _diagnostics.Info(
                "ui.targetTest.complete",
                "Kapsam doğrulaması tamamlandı.",
                CreateTargetTestDiagnosticDetails(results));
            _diagnostics.WriteHealth(
                "kapsam doğrulaması tamamlandı",
                CreateTargetTestDiagnosticDetails(results));
        }
        catch (OperationCanceledException)
        {
            _lastTargetTestSummary = "Kapsam doğrulaması iptal edildi veya zaman aşımına uğradı.";
            TargetTestSummary.Text = _lastTargetTestSummary;
            _diagnostics.Warning(
                "ui.targetTest.cancelled",
                "Kapsam doğrulaması iptal edildi veya zaman aşımına uğradı.");
        }
        finally
        {
            _isTargetTestRunning = false;
            ApplyTargetTestSummaryState(_controller.Snapshot);
            RefreshTargetScopeView(_controller.Snapshot.IsBusy
                || _controller.Snapshot.IsConnected);
            RefreshLiveStatusCards(_controller.Snapshot);
            ApplyRepairButtonState(_controller.Snapshot);
        }
    }

    private async Task<TargetProbeResult> ProbeTargetHostsViaScopedProxyAsync(
        TargetDefinition target,
        string[] hosts,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var tested = 0;
        try
        {
            if (!TryReadProxyPortFromPacFile(out var proxyPort))
            {
                return new TargetProbeResult(
                    TargetProbeStatus.Failed,
                    string.Join(", ", hosts),
                    "Aktif proxy portu bulunamadı.",
                    startedAt);
            }

            foreach (var host in hosts)
            {
                tested++;
                var hostResult = await ProbeSingleTargetHostViaScopedProxyAsync(
                    host,
                    proxyPort,
                    cancellationToken);
                if (hostResult is not null)
                {
                    return hostResult;
                }
            }

            return new TargetProbeResult(
                TargetProbeStatus.Success,
                string.Join(", ", hosts),
                $"CONNECT 443 rota OK: {tested.ToString(CultureInfo.InvariantCulture)}/{hosts.Length.ToString(CultureInfo.InvariantCulture)} host.",
                startedAt);
        }
        catch (OperationCanceledException)
        {
            return new TargetProbeResult(
                TargetProbeStatus.Failed,
                string.Join(", ", hosts),
                $"Kapsam doğrulaması zaman aşımına uğradı: {tested.ToString(CultureInfo.InvariantCulture)}/{hosts.Length.ToString(CultureInfo.InvariantCulture)} host denendi.",
                startedAt);
        }
    }

    private static async Task<TargetProbeResult?> ProbeSingleTargetHostViaScopedProxyAsync(
        string host,
        int proxyPort,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxyPort)
                .WaitAsync(TimeSpan.FromSeconds(3), linked.Token);
            await using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"CONNECT {host}:443 HTTP/1.1\r\nHost: {host}:443\r\nProxy-Connection: close\r\n\r\n");
            await stream.WriteAsync(request, linked.Token);
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token);
            var response = read <= 0
                ? string.Empty
                : Encoding.ASCII.GetString(buffer, 0, read);

            if (response.StartsWith(
                    "HTTP/1.1 200",
                    StringComparison.OrdinalIgnoreCase)
                || response.StartsWith(
                    "HTTP/1.0 200",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var status = response.Split(
                    ["\r\n", "\n"],
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?? "Yanıt alınamadı.";
            return new TargetProbeResult(
                TargetProbeStatus.Failed,
                host,
                status,
                startedAt);
        }
        catch (OperationCanceledException)
        {
            return new TargetProbeResult(
                TargetProbeStatus.Failed,
                host,
                "Zaman aşımı.",
                startedAt);
        }
        catch (Exception exception)
            when (exception is SocketException
                or IOException
                or InvalidOperationException
                or TimeoutException)
        {
            return new TargetProbeResult(
                TargetProbeStatus.Failed,
                host,
                exception.Message,
                startedAt);
        }
    }

    private bool TryReadProxyPortFromPacFile(out int proxyPort)
    {
        proxyPort = 0;
        try
        {
            if (!File.Exists(_paths.WebProxyPacFile))
            {
                return false;
            }

            var script = File.ReadAllText(_paths.WebProxyPacFile);
            return TryReadProxyPortFromPac(script, out proxyPort);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            _diagnostics.Warning(
                "ui.targetTest",
                "PAC dosyası kapsam doğrulaması için okunamadı.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
            return false;
        }
    }

    internal static bool TryReadProxyPortFromPacForTesting(
        string pacScript,
        out int proxyPort)
    {
        return TryReadProxyPortFromPac(pacScript, out proxyPort);
    }

    private static bool TryReadProxyPortFromPac(
        string pacScript,
        out int proxyPort)
    {
        proxyPort = 0;
        const string marker = "PROXY 127.0.0.1:";
        var index = pacScript.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var start = index + marker.Length;
        var end = start;
        while (end < pacScript.Length && char.IsDigit(pacScript[end]))
        {
            end++;
        }

        return end > start
            && int.TryParse(
                pacScript[start..end],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out proxyPort)
            && proxyPort is > 0 and <= 65535;
    }

    internal static string[] GetTargetTestHostsForTesting(TargetDefinition target)
    {
        return GetTargetTestHosts(target);
    }

    private static string[] GetTargetTestHosts(TargetDefinition target)
    {
        if (target.Metadata.TryGetValue("probeHosts", out var probeHosts)
            && !string.IsNullOrWhiteSpace(probeHosts))
        {
            return probeHosts
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return target.Domains
            .Where(pattern => !pattern.IsWildcard)
            .Select(pattern => pattern.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private Dictionary<string, string?> CreateTargetTestDiagnosticDetails(
        IReadOnlyList<TargetProbeResult> results)
    {
        var details = new Dictionary<string, string?>
        {
            ["summary"] = _lastTargetTestSummary,
            ["selectedTargets"] = _controller.CurrentRoutingPlan.Summary
        };

        var index = 0;
        foreach (var target in _controller.CurrentRoutingPlan.SelectedTargets)
        {
            if (!_targetProbeResults.TryGetValue(target.Id, out var result))
            {
                continue;
            }

            details[$"target.{index}.id"] = target.Id;
            details[$"target.{index}.label"] = target.Label;
            details[$"target.{index}.host"] = result.Host;
            details[$"target.{index}.status"] = result.Status.ToString();
            details[$"target.{index}.message"] = result.Message;
            details[$"target.{index}.testedAt"] = result.TestedAt
                .ToString("O", CultureInfo.InvariantCulture);
            index++;
        }

        details["resultCount"] = results.Count.ToString(CultureInfo.InvariantCulture);
        return details;
    }

    private enum TargetCardState
    {
        Ready,
        Selected,
        Connecting,
        InScope,
        Problem,
        Passive
    }

    private enum TargetProbeStatus
    {
        NotTested,
        Running,
        Success,
        ProfileScope,
        Failed,
        Skipped
    }

    private sealed record TargetProbeResult(
        TargetProbeStatus Status,
        string? Host,
        string Message,
        DateTimeOffset TestedAt);

    private void SetTargetCardsEnabled(bool enabled)
    {
        foreach (var toggle in _targetToggles.Values)
        {
            toggle.IsEnabled = enabled;
        }
    }

    private static string CreateSafeTargetName(string targetId)
    {
        return targetId.Replace('-', '_');
    }

    private Grid CreateTargetCardContent(TargetDefinition target)
    {
        var visual = GetTargetVisual(target.IconKey);
        var grid = new Grid
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBadge = new Border
        {
            Width = 30,
            Height = 30,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(9),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(172, 245, 247, 251)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(MediaColor.FromArgb(232, 245, 247, 251)),
            Child = CreateTargetMark(target.IconKey, visual)
        };
        iconBadge.Effect = new DropShadowEffect
        {
            BlurRadius = 12,
            Direction = 270,
            Opacity = 0.52,
            ShadowDepth = 0,
            Color = visual.EndColor
        };
        Grid.SetColumn(iconBadge, 0);
        grid.Children.Add(iconBadge);
        _targetIconBadges[target.Id] = iconBadge;

        var textStack = new StackPanel
        {
            Margin = new Thickness(8, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(textStack, 1);
        textStack.Children.Add(new TextBlock
        {
            Text = target.Label,
            FontSize = 10.8,
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfBrushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 74,
            VerticalAlignment = VerticalAlignment.Center
        });
        grid.Children.Add(textStack);

        var openButton = new WpfButton
        {
            Name = "TargetOpen_" + CreateSafeTargetName(target.Id),
            Tag = target,
            Width = 18,
            Height = 18,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Style = (Style)FindResource("TargetLinkButtonStyle"),
            ToolTip = $"{target.Label} sayfasını aç",
            Content = CreateExternalLinkIcon(new SolidColorBrush(MediaColor.FromRgb(198, 244, 255)))
        };
        AutomationProperties.SetName(openButton, $"{target.Label} sayfasını aç");
        ToolTipService.SetShowsToolTipOnKeyboardFocus(openButton, true);
        openButton.Click += TargetOpenButton_Click;
        Grid.SetColumn(openButton, 2);
        grid.Children.Add(openButton);

        var warningBadge = new Border
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(MediaColor.FromRgb(255, 199, 89)),
            BorderBrush = WpfBrushes.White,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "!",
                FontSize = 9,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(19, 20, 28)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        Grid.SetColumn(warningBadge, 2);
        warningBadge.Margin = new Thickness(0, 16, 2, 0);
        grid.Children.Add(warningBadge);
        _targetWarningBadges[target.Id] = warningBadge;

        return grid;
    }

    private void TargetOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: TargetDefinition target })
        {
            OpenTargetWebsite(target);
            e.Handled = true;
        }
    }

    private static FrameworkElement CreateTargetMark(
        string iconKey,
        TargetVisual visual)
    {
        if (TryCreateTargetIcon(iconKey, visual, out var icon))
        {
            return icon;
        }

        var foreground = new SolidColorBrush(MediaColor.FromRgb(245, 247, 251));
        return new Grid
        {
            Width = 28,
            Height = 28,
            Children =
            {
                new TextBlock
                {
                    Text = visual.Mark,
                    FontSize = visual.Mark.Length > 1 ? 11 : 16,
                    FontWeight = FontWeights.Black,
                    Foreground = foreground,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            }
        };
    }

    internal static bool TryLoadTargetIconForTesting(string iconKey)
    {
        return TryCreateTargetIcon(iconKey, GetTargetVisual(iconKey), out _);
    }

    private static bool TryCreateTargetIcon(
        string iconKey,
        TargetVisual visual,
        out FrameworkElement icon)
    {
        icon = new Grid();
        try
        {
            var uri = new Uri(
                $"pack://application:,,,/Astral;component/Assets/targets/{iconKey}.svg",
                UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource?.Stream is null)
            {
                return false;
            }

            using var stream = resource.Stream;
            var document = XDocument.Load(stream);
            var root = document.Root;
            if (root is null || !string.Equals(
                    root.Name.LocalName,
                    "svg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var imageElement = root
                .Descendants()
                .FirstOrDefault(element => string.Equals(
                    element.Name.LocalName,
                    "image",
                    StringComparison.OrdinalIgnoreCase));
            if (imageElement is not null
                && TryCreateEmbeddedImageIcon(imageElement, out icon))
            {
                return true;
            }

            var viewBox = ParseSvgViewBox(root.Attribute("viewBox")?.Value);
            var canvas = new Canvas
            {
                Width = viewBox.Width,
                Height = viewBox.Height,
                ClipToBounds = false
            };
            var rootFill = root.Attribute("fill")?.Value;
            var hasPath = false;
            foreach (var pathElement in root
                         .Descendants()
                         .Where(element => string.Equals(
                             element.Name.LocalName,
                             "path",
                             StringComparison.OrdinalIgnoreCase)))
            {
                var data = pathElement.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                var geometry = Geometry.Parse(data);
                geometry.Freeze();
                var fill = CreateSvgBrush(
                    pathElement.Attribute("fill")?.Value,
                    rootFill,
                    visual.StartColor);
                canvas.Children.Add(new WpfPath
                {
                    Data = geometry,
                    Fill = fill,
                    Stretch = Stretch.None
                });
                hasPath = true;
            }

            if (!hasPath)
            {
                return false;
            }

            icon = new Viewbox
            {
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                Child = canvas
            };
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or NotSupportedException
            or System.Xml.XmlException
            or FormatException)
        {
            return false;
        }
    }

    private static bool TryCreateEmbeddedImageIcon(
        XElement imageElement,
        out FrameworkElement icon)
    {
        icon = new Grid();
        var href = imageElement.Attribute("href")?.Value
            ?? imageElement.Attribute(XName.Get(
                "href",
                "http://www.w3.org/1999/xlink"))?.Value;
        const string Base64Marker = ";base64,";
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var markerIndex = href.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var base64 = href[(markerIndex + Base64Marker.Length)..];
        var bytes = Convert.FromBase64String(base64);
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        icon = new WpfImage
        {
            Width = 28,
            Height = 28,
            Source = bitmap,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        return true;
    }

    private static (double Width, double Height) ParseSvgViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (24, 24);
        }

        var parts = value
            .Split(SvgViewBoxSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(
                part,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : double.NaN)
            .ToArray();
        if (parts.Length != 4
            || double.IsNaN(parts[2])
            || double.IsNaN(parts[3])
            || parts[2] <= 0
            || parts[3] <= 0)
        {
            return (24, 24);
        }

        return (parts[2], parts[3]);
    }

    private static WpfBrush CreateSvgBrush(
        string? localFill,
        string? rootFill,
        MediaColor fallback)
    {
        var fill = string.IsNullOrWhiteSpace(localFill)
            ? rootFill
            : localFill;
        if (string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
        {
            return WpfBrushes.Transparent;
        }

        if (!string.IsNullOrWhiteSpace(fill)
            && (fill.StartsWith('#') || fill.StartsWith("sc#", StringComparison.OrdinalIgnoreCase)))
        {
            var brush = (WpfBrush)new BrushConverter().ConvertFromString(fill)!;
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        var fallbackBrush = new SolidColorBrush(fallback);
        fallbackBrush.Freeze();
        return fallbackBrush;
    }

    private static Canvas CreatePathIcon(string data, WpfBrush foreground, bool fill)
    {
        var canvas = CreateIconCanvas();
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse(data),
            Stroke = foreground,
            StrokeThickness = fill ? 0 : 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = fill ? foreground : WpfBrushes.Transparent
        });
        return canvas;
    }

    private static Canvas CreateIconCanvas()
    {
        return new Canvas
        {
            Width = 32,
            Height = 32,
            ClipToBounds = false,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Viewbox CreateExternalLinkIcon(WpfBrush foreground)
    {
        return new Viewbox
        {
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            Child = CreatePathIcon(
                "M18 6H26V14 M26 6L14 18 M12 9H8C6.9 9 6 9.9 6 11V24C6 25.1 6.9 26 8 26H21C22.1 26 23 25.1 23 24V20",
                foreground,
                fill: false)
        };
    }

    private static TargetVisual GetTargetVisual(string iconKey)
    {
        return iconKey switch
        {
            "discord" => new("DC", TargetVisualKind.Letter, Rgb(88, 101, 242), Rgb(125, 235, 255)),
            "roblox" => new("RB", TargetVisualKind.Letter, Rgb(20, 24, 32), Rgb(245, 247, 251)),
            "wattpad" => new("W", TargetVisualKind.Letter, Rgb(255, 103, 26), Rgb(255, 175, 72)),
            "bigo-live" => new("BG", TargetVisualKind.Letter, Rgb(27, 169, 255), Rgb(93, 255, 146)),
            "azar" => new("AZ", TargetVisualKind.Letter, Rgb(255, 89, 139), Rgb(125, 235, 255)),
            "tango" => new("TG", TargetVisualKind.Letter, Rgb(255, 124, 50), Rgb(255, 217, 85)),
            "livu" => new("LV", TargetVisualKind.Letter, Rgb(165, 91, 255), Rgb(93, 255, 146)),
            "imvu" => new("IM", TargetVisualKind.Letter, Rgb(35, 215, 162), Rgb(125, 235, 255)),
            "blogspot" => new("BL", TargetVisualKind.Letter, Rgb(245, 132, 31), Rgb(255, 199, 89)),
            "radio-garden" => new("RG", TargetVisualKind.Letter, Rgb(29, 185, 84), Rgb(93, 255, 146)),
            "deutsche-welle" => new("DW", TargetVisualKind.Letter, Rgb(17, 24, 39), Rgb(245, 247, 251)),
            "voice-of-america" => new("VOA", TargetVisualKind.Letter, Rgb(30, 90, 168), Rgb(210, 31, 60)),
            "eksi-sozluk" => new("E", TargetVisualKind.Letter, Rgb(106, 168, 79), Rgb(245, 247, 251)),
            "grok" => new("G", TargetVisualKind.Letter, Rgb(16, 24, 32), Rgb(125, 235, 255)),
            "imgur" => new("I", TargetVisualKind.Letter, Rgb(27, 183, 110), Rgb(245, 247, 251)),
            "pastebin" => new("PB", TargetVisualKind.Letter, Rgb(26, 158, 119), Rgb(125, 235, 255)),
            _ => new("A", TargetVisualKind.Letter, Rgb(125, 235, 255), Rgb(93, 255, 146))
        };
    }

    private static MediaColor Rgb(byte red, byte green, byte blue) =>
        MediaColor.FromRgb(red, green, blue);

    private sealed record TargetVisual(
        string Mark,
        TargetVisualKind Kind,
        MediaColor StartColor,
        MediaColor EndColor);

    private enum TargetVisualKind
    {
        Letter,
        Discord,
        Live,
        Spark,
        Chat,
        Heart,
        Cube,
        Blog
    }

    private void DebugDiagnosticsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = DebugDiagnosticsToggle.IsChecked == true;
        _settingsStore.SetDebugDiagnosticsEnabled(enabled);
        _diagnostics.Info(
            "ui.debugDiagnostics",
            enabled
                ? "Debug tanılama açıldı."
                : "Debug tanılama kapatıldı.",
            new Dictionary<string, string?>
            {
                ["debugDiagnostics"] = enabled.ToString()
            });
        ApplyDebugDiagnosticsSetting(enabled);
    }

    private void ApplyDebugDiagnosticsSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            DebugDiagnosticsToggle.IsChecked = enabled;
            DebugDiagnosticsStatus.Text = enabled
                ? "Debug ayrıntısı açık."
                : "Ayrıntılı tanı kapalı.";
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void RunInBackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = RunInBackgroundToggle.IsChecked == true;
        _settingsStore.SetRunInBackgroundOnCloseEnabled(enabled);
        _diagnostics.Info(
            "ui.runInBackground",
            enabled
                ? "Pencere kapanınca arka planda kalma açıldı."
                : "Pencere kapanınca arka planda kalma kapatıldı.");
        ApplyRunInBackgroundSetting(enabled);
    }

    private void ApplyRunInBackgroundSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            _isRunInBackgroundEnabled = enabled;
            RunInBackgroundToggle.IsChecked = enabled;
            RunInBackgroundStatus.Text = enabled
                ? "Kapanınca arka planda."
                : "Kapanınca kapanır.";

            if (enabled)
            {
                EnsureTrayIcon();
            }
            else if (IsVisible)
            {
                DisposeTrayIcon();
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }

        RefreshLiveStatusCards(_controller.Snapshot);
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = StartupToggle.IsChecked == true;
        try
        {
            _startupLaunchService.SetEnabled(enabled);
            _settingsStore.SetStartWithWindowsEnabled(enabled);
            _diagnostics.Info(
                "ui.startup",
                enabled
                    ? "Windows açılışında çalıştırma açıldı."
                    : "Windows açılışında çalıştırma kapatıldı.");
            ApplyStartupSetting(enabled);
        }
        catch (Exception exception)
        {
            var currentState = TryGetStartupLaunchState();
            _settingsStore.SetStartWithWindowsEnabled(currentState);
            ApplyStartupSetting(currentState);

            MessageBox.Show(
                "Windows açılışında çalıştırma ayarı kaydedilemedi.\n\n" + exception.Message,
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _diagnostics.Failure(
                "ui.startup",
                "Windows başlangıç ayarı kaydedilemedi.",
                exception);
        }
    }

    private void ApplyStartupSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            StartupToggle.IsChecked = enabled;
            StartupStatus.Text = enabled
                ? "Windows ile başlar."
                : "Elle başlatılır.";
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private bool SynchronizeStartupLaunchSetting(bool desiredState)
    {
        try
        {
            _startupLaunchService.SetEnabled(desiredState);
            return desiredState;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            var currentState = TryGetStartupLaunchState();
            _settingsStore.SetStartWithWindowsEnabled(currentState);
            return currentState;
        }
    }

    private bool TryGetStartupLaunchState()
    {
        try
        {
            return _startupLaunchService.IsEnabled();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return false;
        }
    }

    private async void CleanProfile_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Astral bağlantıyı kapatacak; kendi bağlantı kilidini, yerel ayarlarını, loglarını, profilini ve yönetilen verilerini temizleyecek. " +
            "Portable ZIP klasörü ve seçili hedef uygulamaları silinmez. WireSock Astral tarafından kurulduysa Windows'tan kaldırılacak.\n\nDevam edilsin mi?",
            "Astral profil temizliği",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        _operationCancellation?.Cancel();
        StopBackgroundVideo();
        StatusMessage.Text = "Profil temizliği çalışıyor";
        StatusDetail.Text = "Bağlantı kapatılıyor; Astral profili, ayarları ve kilidi temizleniyor.";
        SetMaintenanceProgress(24, "Bağlantı kapatılıyor");
        _diagnostics.Warning("ui.profileClean", "Kullanıcı profil temizliğini başlattı.");

        try
        {
            _startupLaunchService.SetEnabled(false);
            var removeWireSock = _settingsStore.IsWireSockInstalledByAstral()
                || File.Exists(_paths.WireSockInstallMarker)
                || File.Exists(_paths.LegacyWireSockInstallMarker);
            _settingsStore.SetStartWithWindowsEnabled(false);
            _settingsStore.SetRunInBackgroundOnCloseEnabled(false);
            if (!await DisposeControllerForShutdownAsync(
                    "profile-clean",
                    MaintenanceCleanupTimeout))
            {
                throw new TimeoutException(
                    "Astral kapanış temizliği süresinde tamamlanamadı. Lütfen tanılama raporu oluşturup tekrar deneyin.");
            }
            SetMaintenanceProgress(48, removeWireSock
                ? "WireSock kaldırılıyor"
                : "WireSock korunuyor");
            await _wireSockUninstaller.UninstallIfAstralInstalledAsync(
                removeWireSock,
                CancellationToken.None);
            _settingsStore.SetWireSockInstalledByAstral(installed: false);
            SetMaintenanceProgress(78, "Profil ve yerel veriler temizleniyor");
            await Task.Run(
                () => _cleanupService.CleanProfileAsync(CancellationToken.None));

            MessageBox.Show(
                "Astral profili temizlendi. Portable ZIP klasörü korunur; uygulama kapanacak.",
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IsEnabled = true;
            StatusMessage.Text = "Profil temizliği tamamlanamadı";
            StatusDetail.Text = exception.Message;

            MessageBox.Show(
                "Profil temizliği tamamlanamadı.\n\n" + exception.Message,
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (!ShouldEnableRepair(_controller.Snapshot))
        {
            ApplyRepairButtonState(_controller.Snapshot);
            return;
        }

        var confirmation = MessageBox.Show(
            "Astral bağlantıyı kapatacak; profil, wgcf aracı ve kurucu önbelleğini yeniden üretilecek hale getirecek. " +
            "Ayarlar ve tanılama kayıtları korunur. Son tanı WireSock motoru arızasına işaret ediyorsa ve WireSock Astral tarafından kurulduysa motor da yeniden kurulacak hale getirilir.\n\nDevam edilsin mi?",
            "Astral onar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        ToggleButton.IsEnabled = false;
        RepairButton.IsEnabled = false;
        SetUpdateControlsEnabled(false);
        SetTargetCardsEnabled(false);
        _operationCancellation?.Cancel();
        StatusMessage.Text = "Onarım çalışıyor";
        StatusDetail.Text = "Bağlantı kapatılıyor, koruma geri alınıyor ve profil dosyaları yenileniyor.";
        SetMaintenanceProgress(36, "Onarım hazırlanıyor");
        _diagnostics.Info("ui.repair", "Kullanıcı onarımı başlattı.");

        try
        {
            if (!await DisconnectControllerForMaintenanceAsync(
                    "repair",
                    MaintenanceCleanupTimeout))
            {
                throw new TimeoutException(
                    "Bağlantı kapatma işlemi süresinde tamamlanamadı. Lütfen tanılama raporu oluşturup tekrar deneyin.");
            }
            var reinstallWireSock = ShouldRepairWireSockEngine();
            if (reinstallWireSock)
            {
                SetMaintenanceProgress(54, "WireSock motoru yenileniyor");
                await _wireSockUninstaller.UninstallIfAstralInstalledAsync(
                    installedByAstral: true,
                    CancellationToken.None);
                _settingsStore.SetWireSockInstalledByAstral(installed: false);
            }

            SetMaintenanceProgress(68, reinstallWireSock
                ? "Profil ve kurulum önbelleği temizleniyor"
                : "Profil ve önbellek temizleniyor");
            await Task.Run(
                () => _cleanupService.RepairAsync(CancellationToken.None));

            StatusMessage.Text = "Onarım tamamlandı";
            StatusDetail.Text = reinstallWireSock
                ? "Sonraki Bağlan işleminde WireSock, profil ve wgcf dosyaları yeniden kurulacak."
                : "Sonraki Bağlan işleminde profil ve wgcf dosyaları yeniden üretilecek.";
            SetMaintenanceProgress(100, "Onarım tamamlandı");

            MessageBox.Show(
                reinstallWireSock
                    ? "Astral onarıldı. Sonraki bağlantıda WireSock motoru ve profil yeniden hazırlanacak."
                    : "Astral onarıldı. Sonraki bağlantıda profil yeniden oluşturulacak.",
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage.Text = "Onarım tamamlanamadı";
            StatusDetail.Text = exception.Message;
            SetMaintenanceProgress(100, "Müdahale gerekiyor");

            MessageBox.Show(
                "Onarım tamamlanamadı.\n\n" + exception.Message,
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ToggleButton.IsEnabled = !_controller.Snapshot.IsBusy;
            ApplyRepairButtonState(_controller.Snapshot);
            ApplyUpdateControls(_controller.Snapshot.IsBusy);
            RefreshTargetScopeView(_controller.Snapshot.IsBusy
                || _controller.Snapshot.IsConnected);
        }
    }

    private bool ShouldRepairWireSockEngine()
    {
        var installedByAstral = _settingsStore.IsWireSockInstalledByAstral()
            || File.Exists(_paths.WireSockInstallMarker)
            || File.Exists(_paths.LegacyWireSockInstallMarker);
        if (!installedByAstral)
        {
            return false;
        }

        var details = _controller.CreateDiagnosticDetails();
        if (!details.TryGetValue("tunnelReadiness", out var readiness)
            || !string.Equals(
                readiness,
                TunnelReadinessSnapshot.WireSockAdapterInactiveStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var received = details.TryGetValue(
                "wireSockAdapterBytesReceived",
                out var bytesReceived)
            ? bytesReceived
            : null;
        var sent = details.TryGetValue(
                "wireSockAdapterBytesSent",
                out var bytesSent)
            ? bytesSent
            : null;

        return string.Equals(received, "0", StringComparison.Ordinal)
            && string.Equals(sent, "0", StringComparison.Ordinal);
    }

    private void SetMaintenanceProgress(double value, string label)
    {
        StageProgressBar.Value = value;
        StageProgressBar.Foreground = (System.Windows.Media.Brush)
            System.Windows.Application.Current.Resources["AccentCyanBrush"];
        StageProgressLabel.Text = label;
        StageProgressPercent.Text = $"{value:0}%";
    }

    private void SetUpdateStageProgress(double value, string label)
    {
        _isUpdateProgressPinned = true;
        SetMaintenanceProgress(value, label);
    }

    private void BackgroundVideo_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateBackgroundVideoForSnapshot(_controller.Snapshot);
    }

    private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!ShouldPlayBackgroundVideo(_controller.Snapshot))
        {
            StopBackgroundVideo();
            return;
        }

        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
        _isBackgroundVideoStarted = true;
    }

    private void BackgroundVideo_MediaFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        if (!ShouldPlayBackgroundVideo(_controller.Snapshot))
        {
            StopBackgroundVideo();
            return;
        }

        _isBackgroundVideoStarted = false;
        BackgroundVideo.Visibility = Visibility.Collapsed;
        _diagnostics.Warning(
            "ui.backgroundVideo",
            "Arka plan videosu yüklenemedi.",
            new Dictionary<string, string?>
            {
                ["error"] = e.ErrorException?.Message
            });
    }

    private static bool IsBackgroundVideoDisabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("ASTRAL_DISABLE_BACKGROUND_VIDEO"),
            "1",
            StringComparison.Ordinal);
    }

    private bool ShouldPlayBackgroundVideo(TunnelSnapshot snapshot)
    {
        if (IsBackgroundVideoDisabled() || IsReducedMotionPreferred())
        {
            return false;
        }

        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return false;
        }

        return true;
    }

    private static bool IsReducedMotionPreferred()
    {
        return !SystemParameters.ClientAreaAnimation;
    }

    private void UpdateBackgroundVideoForSnapshot(TunnelSnapshot snapshot)
    {
        if (ShouldPlayBackgroundVideo(snapshot))
        {
            StartBackgroundVideo();
            return;
        }

        StopBackgroundVideo();
    }

    private void StopBackgroundVideo()
    {
        if (!_isBackgroundVideoStarted
            && BackgroundVideo.Source is null
            && BackgroundVideo.Visibility == Visibility.Collapsed)
        {
            return;
        }

        try
        {
            BackgroundVideo.Stop();
            BackgroundVideo.Source = null;
            BackgroundVideo.Visibility = Visibility.Collapsed;
            _isBackgroundVideoStarted = false;
        }
        catch (InvalidOperationException)
        {
            _isBackgroundVideoStarted = false;
        }
    }

    private void StartBackgroundVideo()
    {
        if (IsBackgroundVideoDisabled())
        {
            StopBackgroundVideo();
            return;
        }

        if (_isBackgroundVideoStarted && BackgroundVideo.Source is not null)
        {
            return;
        }

        var videoUri = GetBackgroundVideoUri();
        if (videoUri is null)
        {
            StopBackgroundVideo();
            return;
        }

        BackgroundVideo.Visibility = Visibility.Visible;
        BackgroundVideo.Source ??= videoUri;

        if (BackgroundVideo.IsLoaded)
        {
            BackgroundVideo.Play();
            _isBackgroundVideoStarted = true;
        }
    }

    private static Uri? GetBackgroundVideoUri()
    {
        if (File.Exists(LocalBackgroundVideoPath))
        {
            return new Uri(LocalBackgroundVideoPath, UriKind.Absolute);
        }

        return null;
    }

    internal static string GetLocalBackgroundVideoPathForTesting()
    {
        return LocalBackgroundVideoPath;
    }

    internal static bool TryGetBackgroundVideoUriForTesting(out Uri uri)
    {
        var videoUri = GetBackgroundVideoUri();
        if (videoUri is null)
        {
            uri = RepositoryUri;
            return false;
        }

        uri = videoUri;
        return true;
    }

    public void HideToTrayOnStartup()
    {
        if (_isRunInBackgroundEnabled)
        {
            HideToTray(showNotification: false);
        }
    }

    private void HideToTray(bool showNotification)
    {
        EnsureTrayIcon();
        StopBackgroundVideo();
        ShowInTaskbar = false;
        Hide();
        _diagnostics.Info("ui.tray", "Astral bildirim alanına alındı.");

        if (showNotification && !_hasShownTrayNotice && _trayIcon is not null)
        {
            _hasShownTrayNotice = true;
            _trayIcon.ShowBalloonTip(
                2500,
                "Astral arka planda çalışıyor",
                "Geri açmak veya tamamen çıkmak için bildirim alanı simgesini kullanın.",
                Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        UpdateBackgroundVideoForSnapshot(_controller.Snapshot);
        _diagnostics.Info("ui.tray", "Astral penceresi geri açıldı.");
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(RestoreFromTray)));
        menu.Items.Add("Bağlantıyı kes", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(async () =>
                await DisconnectFromTrayAsync())));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(ExitFromTray)));

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
            Text = "Astral",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RestoreFromTray));
    }

    private async Task DisconnectFromTrayAsync()
    {
        if (_controller.Snapshot.IsBusy)
        {
            _operationCancellation?.Cancel();
        }

        await _controller.DisconnectAsync(CancellationToken.None);
    }

    private async void ExitFromTray()
    {
        _exitRequested = true;
        _diagnostics.Info("ui.tray", "Astral bildirim alanından kapatılıyor.");
        await CloseAfterShutdownCleanupAsync(
            "tray-exit",
            recoverOnFailure: false,
            deferClose: false);
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return Drawing.Icon.ExtractAssociatedIcon(path)
                ?? Drawing.SystemIcons.Application;
        }

        return Drawing.SystemIcons.Application;
    }

    private async void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _paths.EnsureDirectories();
            _diagnostics.Info("ui.diagnostics", "Tanılama istendi.");
            await _controller.EnsureCurrentWebProxyScopeAsync(
                "diagnostics",
                _windowLifetimeCancellation.Token);
            var details = new Dictionary<string, string?>(
                _controller.CreateDiagnosticDetails(),
                StringComparer.Ordinal);
            details["debugDiagnostics"] =
                _settingsStore.IsDebugDiagnosticsEnabled().ToString();
            details["lastTargetTest"] = _lastTargetTestSummary;
            foreach (var detail in PortableInstallDiagnostics.Capture())
            {
                details[detail.Key] = detail.Value;
            }

            _diagnostics.WriteHealth(
                "tanılama istendi",
                details);
            var bundlePath = _diagnostics.CreateBundle();
            var bundleDirectory = !string.IsNullOrWhiteSpace(bundlePath)
                ? Path.GetDirectoryName(bundlePath)
                : _paths.DiagnosticBundleDirectory;
            DiagnosticsStatus.Text = "Son tanılama hazır. Klasör açıldı.";

            if (!string.IsNullOrWhiteSpace(bundlePath))
            {
                MessageBox.Show(
                    "Tanılama hazırlandı.\n\n" + bundlePath,
                    "Astral tanılama",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = bundleDirectory ?? _paths.DiagnosticBundleDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            DiagnosticsStatus.Text = "Tanılama hazırlanamadı. Ayrıntı loglara yazıldı.";
            _diagnostics.Failure(
                "ui.diagnostics",
                "Tanılama hazırlanamadı.",
                exception);
            MessageBox.Show(
                "Tanılama hazırlanamadı.\n\n" + exception.Message,
                "Astral tanılama",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(RepositoryUri);
    }

    private bool IsTargetSelected(string targetId)
    {
        return _controller.TargetSelection.SelectedTargetIds
            .Any(selected => string.Equals(
                selected,
                targetId,
                StringComparison.OrdinalIgnoreCase));
    }

    private void OpenTargetWebsite(TargetDefinition target)
    {
        if (!TryGetTargetLaunchUri(target, out var uri))
        {
            _diagnostics.Warning(
                "ui.targetOpen",
                "Hedef web giriş URL'si bulunamadı.",
                new Dictionary<string, string?>
                {
                    ["targetId"] = target.Id
                });
            return;
        }

        try
        {
            OpenUri(uri);
            _diagnostics.Info(
                "ui.targetOpen",
                "Hedef web girişi açıldı.",
                new Dictionary<string, string?>
                {
                    ["targetId"] = target.Id,
                    ["host"] = uri.Host
                });
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            _diagnostics.Failure(
                "ui.targetOpen",
                "Hedef web girişi açılamadı.",
                exception);
        }
    }

    private static bool TryGetTargetLaunchUri(
        TargetDefinition target,
        out Uri uri)
    {
        uri = RepositoryUri;
        if (target.Metadata.TryGetValue("launchUrl", out var rawLaunchUrl)
            && Uri.TryCreate(rawLaunchUrl, UriKind.Absolute, out var launchUri)
            && launchUri.Scheme == Uri.UriSchemeHttps
            && IsLaunchHostAllowed(target, launchUri.Host))
        {
            uri = launchUri;
            return true;
        }

        var domain = target.Domains
            .Select(pattern => pattern.Pattern)
            .FirstOrDefault(pattern => !pattern.StartsWith("*.", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        if (!Uri.TryCreate("https://" + domain, UriKind.Absolute, out var fallbackUri))
        {
            return false;
        }

        uri = fallbackUri;
        return true;
    }

    internal static bool TryGetTargetLaunchUriForTesting(
        TargetDefinition target,
        out Uri uri)
    {
        return TryGetTargetLaunchUri(target, out uri);
    }

    private static bool IsLaunchHostAllowed(TargetDefinition target, string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return target.Domains.Any(domain => domain.Matches(host));
    }

    private void OpenReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        var window = new ReleaseNotesWindow(ReleaseNotesUri)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private async void RestartAstral_Click(object sender, RoutedEventArgs e)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)
            || !File.Exists(executablePath))
        {
            MessageBox.Show(
                "Astral yeniden başlatılamadı. Uygulama yolu bulunamadı.",
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            IsEnabled = false;
            _operationCancellation?.Cancel();
            StopBackgroundVideo();
            StatusMessage.Text = "Astral yeniden başlatılıyor";
            StatusDetail.Text = "Bağlantı ve koruma durumu kapatılıyor.";
            if (!await DisposeControllerForShutdownAsync(
                    "restart",
                    MaintenanceCleanupTimeout))
            {
                StatusDetail.Text =
                    "Kapanış temizliği zaman aşımına uğradı; Astral yeniden başlatma yine de başlatılıyor.";
                _diagnostics.Warning(
                    "ui.restart",
                    "Kapanış temizliği zaman aşımına uğradı; yeniden başlatma best-effort devam ediyor.");
            }
            StartRestartHelper(executablePath);
            _diagnostics.Info("ui.restart", "Kullanıcı Astral yeniden başlatma istedi.");
            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            _diagnostics.Failure(
                "ui.restart",
                "Astral yeniden başlatılamadı.",
                exception);
            MessageBox.Show(
                "Astral yeniden başlatılamadı.\n\n" + exception.Message,
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            IsEnabled = true;
        }
    }

    private static void StartRestartHelper(string executablePath)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath)
            ?? AppContext.BaseDirectory;
        var script =
            "Wait-Process -Id " +
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture) +
            " -ErrorAction SilentlyContinue; Start-Process -FilePath " +
            ToPowerShellLiteral(executablePath) +
            " -WorkingDirectory " +
            ToPowerShellLiteral(workingDirectory);

        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        Process.Start(startInfo);
    }

    private static string ToPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await CheckForUpdatesAsync(
                showStatus: false,
                _windowLifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckForUpdatesAsync(
        bool showStatus,
        CancellationToken cancellationToken)
    {
        if (_isUpdateCheckRunning || _isUpdateOperationRunning)
        {
            return;
        }

        _isUpdateCheckRunning = true;
        ApplyUpdateControls(_controller.Snapshot.IsBusy);

        try
        {
            _diagnostics.Info(
                showStatus ? "ui.update.check" : "ui.update.backgroundCheck",
                showStatus
                    ? "Güncelleme denetimi istendi."
                    : "Güncelleme arka planda denetleniyor.");
            if (showStatus)
            {
                DiagnosticsStatus.Text = "Güncelleme denetleniyor…";
            }

            using var timeoutSource = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromMinutes(3));
            var update = await _updateService.CheckLatestUpdateAsync(
                GetCurrentVersion(),
                timeoutSource.Token);

            if (update.Status == AppUpdateCheckStatus.UpToDate)
            {
                _pendingUpdate = null;
                _diagnostics.Info(
                    "ui.update.current",
                    "Astral güncel.",
                    new Dictionary<string, string?>
                    {
                        ["currentVersion"] = AppUpdateService.FormatVersion(update.CurrentVersion),
                        ["latestVersion"] = AppUpdateService.FormatVersion(update.LatestVersion)
                    });
                if (showStatus)
                {
                    DiagnosticsStatus.Text = "Astral güncel.";
                }

                return;
            }

            _pendingUpdate = update;
            DiagnosticsStatus.Text =
                $"v{AppUpdateService.FormatVersion(update.LatestVersion)} hazır. Güncelle, bu Astral klasörünü yeniler.";
            _diagnostics.Info(
                "ui.update.available",
                "Güncelleme bulundu.",
                new Dictionary<string, string?>
                {
                    ["latestVersion"] = AppUpdateService.FormatVersion(update.LatestVersion),
                    ["expectedSha256"] = update.ExpectedSha256
                });
        }
        catch (OperationCanceledException)
        {
            if (showStatus)
            {
                DiagnosticsStatus.Text = "Güncelleme denetimi iptal edildi.";
            }
        }
        catch (Exception exception)
        {
            _pendingUpdate = null;
            _diagnostics.Failure(
                showStatus ? "ui.update" : "ui.update.backgroundCheck",
                "Güncelleme denetimi tamamlanamadı.",
                exception);
            if (showStatus)
            {
                DiagnosticsStatus.Text = "Güncelleme denetlenemedi. Mevcut sürüm kullanılabilir.";
                MessageBox.Show(
                    "Güncelleme denetlenemedi.\n\n" + exception.Message,
                    "Astral güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                DiagnosticsStatus.Text = "Güncelleme denetlenemedi. Tekrar dene.";
            }
        }
        finally
        {
            _isUpdateCheckRunning = false;
            if (!_disposed)
            {
                ApplyUpdateControls(_controller.Snapshot.IsBusy);
            }
        }
    }

    private async void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.Status == AppUpdateCheckStatus.UpdateAvailable)
        {
            await InstallPendingUpdateAsync();
            return;
        }

        await CheckForUpdatesAsync(
            showStatus: true,
            _windowLifetimeCancellation.Token);
    }

    private async Task InstallPendingUpdateAsync()
    {
        if (_isUpdateOperationRunning)
        {
            return;
        }

        if (_pendingUpdate is null
            || _pendingUpdate.Status != AppUpdateCheckStatus.UpdateAvailable)
        {
            DiagnosticsStatus.Text = "Güncelleme henüz hazır değil.";
            return;
        }

        if (_controller.Snapshot.IsBusy)
        {
            MessageBox.Show(
                "Devam eden işlem bitince güncellemeyi yükleyin.",
                "Astral güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _isUpdateOperationRunning = true;
        SetUpdateControlsEnabled(false);
        ToggleButton.IsEnabled = false;
        RepairButton.IsEnabled = false;

        try
        {
            _diagnostics.Info("ui.update.install", "Güncelleme yükleme istendi.");
            ResetUpdateProgressLogThrottle();

            if (_controller.Snapshot.IsConnected)
            {
                DiagnosticsStatus.Text = "Bağlantı kapatılıyor. Ardından güncelleme yüklenecek.";
                SetUpdateStageProgress(8, "Bağlantı kapatılıyor");
                if (!await DisconnectControllerForMaintenanceAsync(
                        "update-install",
                        MaintenanceCleanupTimeout))
                {
                    throw new TimeoutException(
                        "Güncelleme öncesi bağlantı kapatma işlemi süresinde tamamlanamadı.");
                }
            }

            using var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromMinutes(15));
            var executableName = GetExecutableName();
            DiagnosticsStatus.Text =
                $"v{AppUpdateService.FormatVersion(_pendingUpdate.LatestVersion)} indiriliyor ve doğrulanıyor…";
            SetUpdateStageProgress(18, "Güncelleme hazırlanıyor");
            var progress = new Progress<AppUpdateProgress>(
                value => SetUpdateProgress(value));
            var update = await _updateService.PrepareCheckedUpdateAsync(
                _pendingUpdate,
                AppContext.BaseDirectory,
                executableName,
                timeoutSource.Token,
                progress);

            if (update.Status == AppUpdatePreparationStatus.UpToDate)
            {
                _pendingUpdate = null;
                DiagnosticsStatus.Text = "Astral güncel.";
                SetUpdateStageProgress(100, "Astral güncel");
                return;
            }

            DiagnosticsStatus.Text =
                "Güncelleme hazır. Astral kapanıp yeni sürümle açılacak.";
            SetUpdateStageProgress(96, "Yükleme penceresi açılıyor");
            _diagnostics.Info(
                "ui.update.prepared",
                "Güncelleme paketi hazırlandı.",
                CreateUpdateDiagnosticDetails(update));

            await StartUpdateApplicatorAsync(
                update,
                executableName,
                timeoutSource.Token);
            SetUpdateStageProgress(100, "Astral yeniden başlatılıyor");
            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            DiagnosticsStatus.Text = "Güncelleme yüklemesi iptal edildi.";
            SetUpdateStageProgress(100, "Yükleme iptal edildi");
        }
        catch (Exception exception)
        {
            DiagnosticsStatus.Text = "Güncelleme yüklenemedi. Mevcut sürüm kullanılabilir.";
            SetUpdateStageProgress(100, "Güncelleme başarısız");
            _diagnostics.Failure(
                "ui.update.install",
                "Güncelleme yükleme tamamlanamadı.",
                exception);
            MessageBox.Show(
                "Güncelleme yüklenemedi.\n\nMevcut sürüm kullanılabilir. Ayrıntılar tanılama loglarına yazıldı.\n\n" + exception.Message,
                "Astral güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isUpdateOperationRunning = false;

            if (!_allowClose)
            {
                ApplySnapshot(_controller.Snapshot);
            }
        }
    }

    private async Task StartUpdateApplicatorAsync(
        AppUpdatePreparation update,
        string executableName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.ApplicatorPath)
            || string.IsNullOrWhiteSpace(update.PackagePath)
            || string.IsNullOrWhiteSpace(update.ExpectedSha256))
        {
            throw new InvalidOperationException(
                "Güncelleme hazırlığı eksik olduğu için uygulanamadı.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = update.ApplicatorPath,
            WorkingDirectory = Path.GetDirectoryName(update.ApplicatorPath),
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add("--process-id");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(
            CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(update.PackagePath);
        startInfo.ArgumentList.Add("--expected-sha256");
        startInfo.ArgumentList.Add(update.ExpectedSha256);
        startInfo.ArgumentList.Add("--expected-version");
        startInfo.ArgumentList.Add(AppUpdateService.FormatVersion(update.LatestVersion));
        if (!string.IsNullOrWhiteSpace(update.ExpectedSignerThumbprint))
        {
            startInfo.ArgumentList.Add("--expected-signer-thumbprint");
            startInfo.ArgumentList.Add(update.ExpectedSignerThumbprint);
        }
        startInfo.ArgumentList.Add("--target-directory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("--executable-name");
        startInfo.ArgumentList.Add(executableName);
        startInfo.ArgumentList.Add("--log");
        startInfo.ArgumentList.Add(Path.Combine(_paths.LogDirectory, "update.log"));

        _diagnostics.Info(
            "ui.update.apply",
            "Güncelleme uygulayıcısı başlatılıyor.",
            CreateUpdateDiagnosticDetails(update));

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Güncelleme uygulayıcısı başlatılamadı.");
        await Task.WhenAny(
            process.WaitForExitAsync(cancellationToken),
            Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken));
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                $"Güncelleme uygulayıcısı erken kapandı. Çıkış kodu: {process.ExitCode}.");
        }
    }

    private void SetUpdateProgress(AppUpdateProgress progress)
    {
        SetUpdateStageProgress(progress.Percent, progress.Message);
        DiagnosticsStatus.Text = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Message
            : $"{progress.Message}. {progress.Detail}";
        if (ShouldLogUpdateProgress(progress))
        {
            _diagnostics.Info(
                "ui.update.progress",
                progress.Message,
                new Dictionary<string, string?>
                {
                    ["percent"] = progress.Percent.ToString("0", CultureInfo.InvariantCulture),
                    ["detail"] = progress.Detail
                });
        }
    }

    private static Dictionary<string, string?> CreateUpdateDiagnosticDetails(
        AppUpdatePreparation update)
    {
        var details = new Dictionary<string, string?>
        {
            ["latestVersion"] = AppUpdateService.FormatVersion(update.LatestVersion),
            ["packagePath"] = update.PackagePath,
            ["payloadDirectory"] = update.PayloadDirectory,
            ["expectedSha256"] = update.ExpectedSha256
        };

        foreach (var detail in PortableInstallDiagnostics.Capture())
        {
            details[detail.Key] = detail.Value;
        }

        return details;
    }

    private void ResetUpdateProgressLogThrottle()
    {
        _lastLoggedUpdateProgressKey = null;
        _lastLoggedUpdateProgressPercent = -1;
    }

    private bool ShouldLogUpdateProgress(AppUpdateProgress progress)
    {
        var percent = (int)Math.Floor(progress.Percent);
        var key = IsByteDownloadDetail(progress.Detail)
            ? progress.Message
            : $"{progress.Message}|{progress.Detail}";
        if (!string.Equals(
                key,
                _lastLoggedUpdateProgressKey,
                StringComparison.Ordinal)
            || percent >= 100
            || percent - _lastLoggedUpdateProgressPercent >= 2)
        {
            _lastLoggedUpdateProgressKey = key;
            _lastLoggedUpdateProgressPercent = percent;
            return true;
        }

        return false;
    }

    private static bool IsByteDownloadDetail(string? detail)
    {
        return !string.IsNullOrWhiteSpace(detail)
            && detail.EndsWith(" indirildi.", StringComparison.Ordinal);
    }

    private static Version GetCurrentVersion()
    {
        return typeof(MainWindow).Assembly.GetName().Version
            ?? new Version(0, 0, 0, 0);
    }

    private static string GetExecutableName()
    {
        return ResolveUpdateExecutableName(
            Environment.ProcessPath,
            AppContext.BaseDirectory);
    }

    internal static string ResolveUpdateExecutableName(
        string? processPath,
        string applicationDirectory)
    {
        var executableName = Path.GetFileName(processPath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return "Astral.exe";
        }

        return executableName;
    }

    private void ApplyUpdateControls(bool isBusy)
    {
        var hasUpdate = _pendingUpdate?.Status == AppUpdateCheckStatus.UpdateAvailable;
        var shouldShowButton = hasUpdate || _isUpdateOperationRunning;
        AutoUpdateButton.Visibility = shouldShowButton
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (hasUpdate)
        {
            var versionText = AppUpdateService.FormatVersion(_pendingUpdate!.LatestVersion);
            AutoUpdateButton.Content = "↻ Güncelle";
            AutoUpdateButton.ToolTip = $"v{versionText} hazır. Güncellemeyi yükle.";
            AutomationProperties.SetName(
                AutoUpdateButton,
                $"v{versionText} güncellemesini yükle");
        }
        else
        {
            AutoUpdateButton.Content = "↻ Güncelle";
            AutoUpdateButton.ToolTip = "Yeni sürüm varsa burada görünür.";
            AutomationProperties.SetName(
                AutoUpdateButton,
                "Yeni sürüm varsa görünen güncelleme düğmesi");
        }

        SetUpdateControlsEnabled(shouldShowButton
            && hasUpdate
            && !_isUpdateOperationRunning
            && !_isUpdateCheckRunning
            && !isBusy);
    }

    private void SetUpdateControlsEnabled(bool enabled)
    {
        AutoUpdateButton.IsEnabled = enabled;
    }

    private static void OpenUri(Uri uri)
    {
        if (OpenUriOverrideForTesting is { } openUriForTesting)
        {
            openUriForTesting(uri);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_isRunInBackgroundEnabled && !_exitRequested)
        {
            e.Cancel = true;
            HideToTray(showNotification: true);
            return;
        }

        e.Cancel = true;
        await CloseAfterShutdownCleanupAsync(
            "window-close",
            recoverOnFailure: true,
            deferClose: true);
    }

    private async Task CloseAfterShutdownCleanupAsync(
        string operation,
        bool recoverOnFailure,
        bool deferClose)
    {
        if (_allowClose || _isClosing)
        {
            return;
        }

        _isClosing = true;
        IsEnabled = false;
        StatusMessage.Text = "Astral kapanıyor";
        StatusDetail.Text = "Bağlantı ve koruma durumu güvenle kapatılıyor.";
        _operationCancellation?.Cancel();
        StopBackgroundVideo();

        try
        {
            await DisposeControllerForShutdownAsync(
                operation,
                ShutdownCleanupTimeout);
        }
        catch (Exception exception)
        {
            _diagnostics.Failure(
                "ui.shutdown",
                "Kapanış temizliği tamamlanamadı.",
                exception);
            if (recoverOnFailure)
            {
                _exitRequested = false;
                _isClosing = false;
                IsEnabled = true;
                Show();
                Activate();
                StatusMessage.Text = "Kapanış tamamlanamadı";
                StatusDetail.Text = "Bağlantı koruması doğrulanamadı. Tanı paketi oluşturun.";
                return;
            }
        }

        Dispose();
        _allowClose = true;
        if (deferClose)
        {
            _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }

        Close();
    }

    private async Task<bool> DisconnectControllerForMaintenanceAsync(
        string operation,
        TimeSpan timeout)
    {
        _operationCancellation?.Cancel();
        using var timeoutSource = new CancellationTokenSource(timeout);
        var disconnectTask = _controller.DisconnectAsync(timeoutSource.Token);
        var completed = await AwaitControllerOperationAsync(
            disconnectTask,
            operation,
            timeout);
        if (!completed)
        {
            timeoutSource.Cancel();
        }

        return completed;
    }

    private async Task<bool> DisposeControllerForShutdownAsync(
        string operation,
        TimeSpan timeout)
    {
        HasAttemptedControllerShutdownCleanup = true;
        var disposeTask = _controller.DisposeAsync().AsTask();
        return await AwaitControllerOperationAsync(
            disposeTask,
            operation,
            timeout);
    }

    private async Task<bool> AwaitControllerOperationAsync(
        Task operationTask,
        string operation,
        TimeSpan timeout)
    {
        var completedTask = await Task.WhenAny(
            operationTask,
            Task.Delay(timeout));

        if (ReferenceEquals(completedTask, operationTask))
        {
            await operationTask;
            return true;
        }

        _diagnostics.Warning(
            "ui.shutdown.timeout",
            "Kapanış temizliği süresinde tamamlanamadı; UI kilitlenmemesi için devam ediliyor.",
            new Dictionary<string, string?>
            {
                ["operation"] = operation,
                ["timeoutMs"] = timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                ["state"] = _controller.Snapshot.State.ToString()
            });
        _diagnostics.WriteHealth(
            "kapanış temizliği zaman aşımı",
            new Dictionary<string, string?>
            {
                ["operation"] = operation,
                ["timeoutMs"] = timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)
            });

        _ = operationTask.ContinueWith(
            task =>
            {
                if (task.Exception is not null)
                {
                    _diagnostics.Failure(
                        "ui.shutdown.timeout",
                        "Geciken kapanış temizliği hata ile bitti.",
                        task.Exception.GetBaseException());
                }
            },
            TaskScheduler.Default);

        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowLifetimeCancellation.Cancel();
        _windowLifetimeCancellation.Dispose();
        StopBackgroundVideo();
        DisposeTrayIcon();
        _controller.StatusChanged -= OnStatusChanged;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
