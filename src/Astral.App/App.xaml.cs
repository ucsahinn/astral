using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    private const string ShowWindowEventName = @"Local\Astral.ucsahinn.ShowWindow";
    private const string DiagnosticExitEventName = @"Local\Astral.ucsahinn.DiagnosticExit";
    private const string DiagnosticConnectSmokeArgument = "--diagnostic-connect-smoke";
    private const string DiagnosticExitSmokeArgument = "--diagnostic-exit-smoke";
    private static readonly TimeSpan ExitControllerDisposeTimeout = TimeSpan.FromSeconds(2);
    internal static TimeSpan ExitControllerDisposeTimeoutForTesting =>
        ExitControllerDisposeTimeout;

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private EventWaitHandle? _diagnosticExitEvent;
    private CancellationTokenSource? _showWindowEventCancellation;
    private HttpClient? _httpClient;
    private DiscordTunnelController? _tunnelController;
    private AstralDiagnostics? _diagnostics;
    private AppPaths? _paths;
    private bool _disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        WindowsPresentationEnvironment.EnsureProcessEnvironment();
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            MutexName,
            out var createdNew);

        if (!createdNew)
        {
            var runningInstances = GetRunningAstralInstances();
            WriteSingleInstanceDiagnostic(e.Args, runningInstances);
            var signaledExistingInstance = HasArgument(
                    e.Args,
                    DiagnosticExitSmokeArgument)
                ? TrySignalDiagnosticExit()
                : TrySignalExistingInstance();
            if (!signaledExistingInstance)
            {
                MessageBox.Show(
                    CreateSingleInstanceMessage(runningInstances),
                    "Astral VPN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        if (HasArgument(e.Args, DiagnosticExitSmokeArgument))
        {
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
            new ProductInfoHeaderValue("AstralVPN", "2.2.32"));

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
            webProxyService: webProxyService,
            targetApplicationProofProvider: new WindowsTargetApplicationProofProvider());
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
        StartExistingInstanceActivationListener(window);
        window.Show();

        if (e.Args.Any(arg => string.Equals(
                arg,
                "--background-start",
                StringComparison.OrdinalIgnoreCase))
            && settingsStore.IsRunInBackgroundOnCloseEnabled())
        {
            window.HideToTrayOnStartup();
        }

        if (HasArgument(e.Args, DiagnosticConnectSmokeArgument))
        {
            window.BeginDiagnosticConnectSmoke();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
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

        _showWindowEventCancellation?.Cancel();
        _showWindowEventCancellation?.Dispose();
        _showWindowEvent?.Dispose();
        _diagnosticExitEvent?.Dispose();

        DisposeTunnelControllerForExit();

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

    private void StartExistingInstanceActivationListener(MainWindow window)
    {
        _showWindowEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShowWindowEventName);
        _diagnosticExitEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            DiagnosticExitEventName);
        _showWindowEventCancellation = new CancellationTokenSource();
        var cancellationToken = _showWindowEventCancellation.Token;
        var showWindowHandle = _showWindowEvent;
        var diagnosticExitHandle = _diagnosticExitEvent;

        _ = Task.Run(() =>
        {
            var waitHandles = new WaitHandle[]
            {
                showWindowHandle,
                diagnosticExitHandle,
                cancellationToken.WaitHandle
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var index = WaitHandle.WaitAny(waitHandles);
                    if (index == 2)
                    {
                        break;
                    }

                    Dispatcher.BeginInvoke(index == 0
                        ? new Action(window.RestoreFromExternalActivation)
                        : new Action(window.BeginDiagnosticExitSmoke));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, cancellationToken);
    }

    private static bool TrySignalExistingInstance()
    {
        try
        {
            using var showWindowEvent = EventWaitHandle.OpenExisting(
                ShowWindowEventName);
            return showWindowEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TrySignalDiagnosticExit()
    {
        try
        {
            using var diagnosticExitEvent = EventWaitHandle.OpenExisting(
                DiagnosticExitEventName);
            return diagnosticExitEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasArgument(IReadOnlyList<string> args, string argument) =>
        args.Any(arg => string.Equals(
            arg,
            argument,
            StringComparison.OrdinalIgnoreCase));

    internal sealed record RunningAstralInstanceInfo(
        int ProcessId,
        DateTime? StartTime,
        bool? Responding,
        bool HasMainWindow,
        string? MainWindowTitle);

    internal static string CreateSingleInstanceMessageForTesting(
        IReadOnlyList<RunningAstralInstanceInfo> runningInstances) =>
        CreateSingleInstanceMessage(runningInstances);

    private static string CreateSingleInstanceMessage(
        IReadOnlyList<RunningAstralInstanceInfo> runningInstances)
    {
        var message = new StringBuilder(
            "Astral VPN zaten çalışıyor. Simge bildirim alanında olabilir.");
        if (runningInstances.Count > 0)
        {
            message.AppendLine();
            message.AppendLine();
            message.Append("Çalışan süreç: ");
            message.Append(string.Join(
                ", ",
                runningInstances.Select(FormatRunningInstance)));
        }

        message.AppendLine();
        message.AppendLine();
        message.Append(
            "Pencere görünmüyorsa Görev Yöneticisi'ni yönetici olarak açıp " +
            "Astral.exe sürecini kapatın, sonra tekrar deneyin.");
        return message.ToString();
    }

    private static string FormatRunningInstance(RunningAstralInstanceInfo instance)
    {
        var parts = new List<string>
        {
            "PID " + instance.ProcessId.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        };
        if (instance.StartTime is { } startTime)
        {
            parts.Add("başlangıç " + startTime.ToString(
                "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture));
        }

        if (instance.Responding is false)
        {
            parts.Add("yanıt vermiyor");
        }

        if (!instance.HasMainWindow)
        {
            parts.Add("pencere yok");
        }

        return string.Join(" / ", parts);
    }

    private static RunningAstralInstanceInfo[] GetRunningAstralInstances()
    {
        var currentProcessId = Environment.ProcessId;
        return Process.GetProcessesByName("Astral")
            .Where(process => process.Id != currentProcessId)
            .OrderBy(process => TryReadProcessStartTime(process) ?? DateTime.MaxValue)
            .Take(4)
            .Select(process => new RunningAstralInstanceInfo(
                process.Id,
                TryReadProcessStartTime(process),
                TryReadProcessResponding(process),
                TryReadProcessHasMainWindow(process),
                TryReadProcessMainWindowTitle(process)))
            .ToArray();
    }

    private static void WriteSingleInstanceDiagnostic(
        IReadOnlyList<string> args,
        IReadOnlyList<RunningAstralInstanceInfo> runningInstances)
    {
        try
        {
            var paths = new AppPaths();
            paths.EnsureDirectories();
            var diagnostics = new AstralDiagnostics(paths);
            diagnostics.Warning(
                "app.singleInstance",
                "Astral VPN zaten çalışan bir süreç algıladığı için yeni pencere açılmadı.",
                new Dictionary<string, string?>
                {
                    ["args"] = string.Join(" ", args),
                    ["runningInstanceCount"] = runningInstances.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["runningInstances"] = string.Join(
                        "; ",
                        runningInstances.Select(FormatRunningInstance))
                });
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            Debug.WriteLine(exception);
        }
    }

    private static DateTime? TryReadProcessStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            return null;
        }
    }

    private static bool? TryReadProcessResponding(Process process)
    {
        try
        {
            return process.Responding;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryReadProcessHasMainWindow(Process process)
    {
        try
        {
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            return false;
        }
    }

    private static string? TryReadProcessMainWindowTitle(Process process)
    {
        try
        {
            var title = process.MainWindowTitle;
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            return null;
        }
    }

    private void DisposeTunnelControllerForExit()
    {
        if (_tunnelController is null)
        {
            return;
        }

        if (MainWindow is MainWindow mainWindow)
        {
            if (mainWindow.HasCompletedControllerShutdownCleanup)
            {
                _diagnostics?.Info(
                    "app.exit.controller",
                    "Tunnel controller cleanup was already completed by the main window.");
                return;
            }

            if (mainWindow.ControllerShutdownCleanupTaskForExit is { } pendingCleanup)
            {
                WaitForControllerDisposeTask(
                    pendingCleanup,
                    "pending-main-window-cleanup");
                return;
            }
        }

        try
        {
            var disposeTask = _tunnelController.DisposeAsync().AsTask();
            WaitForControllerDisposeTask(
                disposeTask,
                "app-exit-dispose");
        }
        catch (Exception exception)
        {
            _diagnostics?.Failure(
                "app.exit.controller",
                "Tunnel controller dispose failed during app exit.",
                exception);
        }
    }

    private void WaitForControllerDisposeTask(
        Task disposeTask,
        string operation)
    {
        try
        {
            if (!disposeTask.Wait(ExitControllerDisposeTimeout))
            {
                _diagnostics?.Warning(
                    "app.exit.controllerTimeout",
                    "Tunnel controller dispose timed out during app exit; exit was not blocked.",
                    new Dictionary<string, string?>
                    {
                        ["operation"] = operation,
                        ["timeoutMs"] = ExitControllerDisposeTimeout
                            .TotalMilliseconds
                            .ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    });
                _ = disposeTask.ContinueWith(
                    task =>
                    {
                        if (task.Exception is not null)
                        {
                            _diagnostics?.Failure(
                                "app.exit.controllerTimeout",
                                "Delayed tunnel controller dispose failed.",
                                task.Exception.GetBaseException());
                        }
                    },
                    TaskScheduler.Default);
                return;
            }

            disposeTask.GetAwaiter().GetResult();
            _diagnostics?.Info(
                "app.exit.controller",
                "Tunnel controller dispose completed during app exit.",
                new Dictionary<string, string?>
                {
                    ["operation"] = operation
                });
        }
        catch (Exception exception)
        {
            _diagnostics?.Failure(
                "app.exit.controller",
                "Tunnel controller dispose failed during app exit.",
                exception);
        }
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
