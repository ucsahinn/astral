using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Astral.Core.Configuration;
using Astral.Core.Connection;
using Astral.Core.Diagnostics;
using Astral.Core.Discord;
using Astral.Core.Firewall;
using Astral.Core.Infrastructure;
using Astral.Core.Maintenance;
using Astral.Core.Provisioning;
using Astral.Core.WebProxy;
using Astral.Core.Updates;
using Astral.Core.WireSock;
using Astral.App.Installation;
using Astral.App.Security;
using MessageBox = System.Windows.MessageBox;

namespace Astral.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string MutexName = @"Local\Astral.ucsahinn.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private HttpClient? _httpClient;
    private DiscordTunnelController? _tunnelController;
    private AstralDiagnostics? _diagnostics;
    private AppPaths? _paths;
    private bool _disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        WindowsPresentationEnvironment.EnsureProcessEnvironment();
        base.OnStartup(e);

        if (TryRedirectLegacyDiscorderExecutable(e.Args))
        {
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            MutexName,
            out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Astral zaten çalışıyor. Simge bildirim alanında olabilir.",
                "Astral",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _paths = new AppPaths();
        _paths.EnsureDirectories();
        var settingsStore = new AppSettingsStore(_paths);
        _diagnostics = new AstralDiagnostics(
            _paths,
            isDebugDiagnosticsEnabled: settingsStore.IsDebugDiagnosticsEnabled);
        var browserAccessMigrated = settingsStore.EnsureBrowserAccessPreferenceInitialized();
        var targetSelectionInitialized = settingsStore.EnsureTargetSelectionInitialized();
        if (browserAccessMigrated)
        {
            _diagnostics.Info(
                "settings.browserAccess.migrated",
                "Eski web kapsami ayari guvenli hedef secimine tasindi.",
                new Dictionary<string, string?>
                {
                    ["reason"] = "legacy-implicit-enabled",
                    ["legacyBrowserAccess"] = "False"
                });
        }
        if (targetSelectionInitialized)
        {
            _diagnostics.Info(
                "settings.targetSelection.initialized",
                "Hedef seçimi güvenli varsayılanla hazırlandı.",
                new Dictionary<string, string?>
                {
                    ["selectedTargets"] = "discord"
                });
        }

        var startupDetails = new Dictionary<string, string?>
        {
            ["args"] = string.Join(" ", e.Args),
            ["processPath"] = Environment.ProcessPath
        };
        foreach (var detail in PortableInstallDiagnostics.Capture())
        {
            startupDetails[detail.Key] = detail.Value;
        }

        _diagnostics.Info(
            "app.startup",
            "Astral başlatıldı.",
            startupDetails);

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Astral", "2.2.3"));

        var downloader = new VerifiedDownloader(_httpClient, maxAttempts: 5);
        var wireSockLocator = new WireSockLocator();
        var wireSockBootstrapper = new WireSockBootstrapper(
            _paths,
            settingsStore,
            wireSockLocator,
            downloader,
            new WireSockPackageVerifier(),
            new WindowsElevatedInstallerLauncher());
        var updateService = new AppUpdateService(_httpClient, _paths, downloader);
        var commandRunner = new CommandRunner();
        var provisioner = new WgcfProvisioner(
            _paths,
            downloader,
            commandRunner);
        var accessLock = new WindowsFirewallDiscordAccessLock(
            _paths,
            commandRunner);
        var webProxyService = new WindowsScopedWebProxyService(
            _paths,
            commandRunner,
            new ProcessLauncher(),
            Path.Combine(AppContext.BaseDirectory, "Astral.WebProxy.exe"),
            _diagnostics);

        _tunnelController = new DiscordTunnelController(
            _paths,
            new DiscordAppScope(),
            wireSockBootstrapper,
            provisioner,
            new ProcessLauncher(),
            accessLock: accessLock,
            diagnostics: _diagnostics,
            discordProcessManager: new WindowsDiscordProcessInspector(),
            tunnelReadinessProbe: new WindowsTunnelReadinessProbe(),
            webProxyService: webProxyService);
        _tunnelController.TrySetTargetSelection(settingsStore.GetTargetSelection());

        var window = new MainWindow(
            _tunnelController,
            _paths,
            wireSockBootstrapper,
            settingsStore,
            new AstralCleanupService(_paths, accessLock, _diagnostics),
            new WindowsStartupLaunchService(),
            new WindowsWireSockUninstaller(_diagnostics),
            updateService,
            _diagnostics);
        MainWindow = window;
        window.Show();

        if (e.Args.Any(arg => string.Equals(
                arg,
                "--background-start",
                StringComparison.OrdinalIgnoreCase))
            && settingsStore.IsRunInBackgroundOnCloseEnabled())
        {
            window.HideToTrayOnStartup();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    internal static ProcessStartInfo? CreateLegacyDiscorderRedirectStartInfo(
        string? processPath,
        string applicationDirectory,
        IEnumerable<string> args)
    {
        var executableName = Path.GetFileName(processPath);
        if (!string.Equals(
                executableName,
                "Discorder.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var astralPath = Path.Combine(applicationDirectory, "Astral.exe");
        if (!File.Exists(astralPath)
            || string.Equals(
                Path.GetFullPath(processPath ?? string.Empty),
                Path.GetFullPath(astralPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = astralPath,
            WorkingDirectory = applicationDirectory,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static bool TryRedirectLegacyDiscorderExecutable(
        IReadOnlyList<string> args)
    {
        var startInfo = CreateLegacyDiscorderRedirectStartInfo(
            Environment.ProcessPath,
            AppContext.BaseDirectory,
            args);
        if (startInfo is null)
        {
            return false;
        }

        Process.Start(startInfo);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

        if (_tunnelController is not null)
        {
            _tunnelController.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _httpClient?.Dispose();
        _diagnostics?.Info("app.exit", "Astral kapatıldı.");

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstanceMutex.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        _diagnostics?.Failure(
            "app.dispatcherUnhandledException",
            "Beklenmeyen arayüz hatası yakalandı.",
            e.Exception);
        MessageBox.Show(
            "Beklenmeyen bir hata oluştu. Ayrıntılar tanılama günlüğüne yazıldı.",
            "Astral",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
            _diagnostics?.Failure(
                "app.unhandledException",
                "Beklenmeyen uygulama hatası yakalandı.",
                exception);
        }
    }

    private void LogException(Exception exception)
    {
        try
        {
            if (_paths is null)
            {
                return;
            }

            File.AppendAllText(
                _paths.ErrorLog,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"{AstralDiagnostics.RedactForLog(exception.ToString())}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logException)
        {
            Debug.WriteLine(logException);
        }
    }
}
