using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Astral.App;
using Astral.App.Installation;
using Astral.App.Security;
using Astral.Core.Configuration;
using Astral.Core.Connection;
using Astral.Core.Discord;
using Astral.Core.Firewall;
using Astral.Core.Infrastructure;
using Astral.Core.Maintenance;
using Astral.Core.Provisioning;
using Astral.Core.Targets;
using Astral.Core.Updates;
using Astral.Core.WireSock;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("ATLANDI Windows paket doğrulama testleri Windows gerektirir.");
    return 0;
}

var verifier = new WireSockPackageVerifier();

if (args.Length == 1)
{
    verifier.VerifyInstaller(Path.GetFullPath(args[0]));
    Console.WriteLine("GEÇTİ Resmi WireSock kurucu güven doğrulaması");
    return 0;
}

RenderWindows();

var temporaryFile = Path.Combine(
    Path.GetTempPath(),
    $"Astral-unsigned-{Guid.NewGuid():N}.exe");

try
{
    await File.WriteAllTextAsync(temporaryFile, "authenticode-imzali-olmayan-paket");

    try
    {
        verifier.VerifyInstaller(temporaryFile);
    }
    catch (InvalidDataException)
    {
        Console.WriteLine("GEÇTİ İmzasız WireSock paketi reddedildi");
        return 0;
    }

    Console.Error.WriteLine("KALDI İmzasız WireSock paketi kabul edildi");
    return 1;
}
finally
{
    File.Delete(temporaryFile);
}

static void RenderWindows()
{
    WindowsPresentationEnvironment.EnsureProcessEnvironment();

    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Astral;component/Resources/Theme.xaml",
                    UriKind.Absolute)
            });

            application.Startup += (_, _) =>
            {
                try
                {
                    RenderMainWindow();
                    VerifyConnectedTargetCardsRequireProbeEvidence();
                    VerifyWindowLifetimeBehavior();
                    VerifyPortableInstallDiagnosticsSanitizesLegacyBrand();
                    RenderConsentWindow();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    application.Shutdown();
                }
            };

            application.Run();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

static void RenderMainWindow()
{
    MainWindow? window = null;
    var root = CreateTemporaryDirectory();
    var previousVideoSetting = Environment.GetEnvironmentVariable(
        "ASTRAL_DISABLE_BACKGROUND_VIDEO");
    var previousRemoteFallbackSetting = Environment.GetEnvironmentVariable(
        "ASTRAL_BACKGROUND_VIDEO_REMOTE_FALLBACK");

    try
    {
        Environment.SetEnvironmentVariable(
            "ASTRAL_DISABLE_BACKGROUND_VIDEO",
            null);
        Environment.SetEnvironmentVariable(
            "ASTRAL_BACKGROUND_VIDEO_REMOTE_FALLBACK",
            null);

        window = CreateMainWindow(root);
        window.Show();
        window.UpdateLayout();

        var backgroundVideo = FindVisualChildren<MediaElement>(window).Single();
        Assert(backgroundVideo.Visibility == Visibility.Collapsed);
        Assert(backgroundVideo.Source is null);

        var text = string.Join(
            "\n",
            FindVisualChildren<TextBlock>(window).Select(block => block.Text));
        Assert(text.Contains("Astral", StringComparison.Ordinal));
        Assert(text.Contains("Desteklenen hedef kapsamı hazır", StringComparison.Ordinal));
        Assert(text.Contains("Hedef kapsamı", StringComparison.Ordinal));
        Assert(text.Contains("9 hedef hazır", StringComparison.Ordinal));
        Assert(text.Contains("9 hedef hazır · seçili kapsam bağlantıya dahil edilir.", StringComparison.Ordinal));
        Assert(!text.Contains("TÜNEL KAPSAMI", StringComparison.Ordinal));
        Assert(!text.Contains("Hedefleri Seç", StringComparison.Ordinal));
        Assert(!text.Contains("Hedef Merkezi", StringComparison.Ordinal));
        Assert(!text.Contains("Özel EXE", StringComparison.Ordinal));
        Assert(!text.Contains("Özel Domain", StringComparison.Ordinal));
        Assert(!text.Contains("Tarayıcı modu", StringComparison.Ordinal));
        Assert(text.Contains("İŞLETİM MERKEZİ", StringComparison.Ordinal));
        Assert(text.Contains("Arka planda çalıştır", StringComparison.Ordinal));
        Assert(text.Contains("Windows açılışında çalıştır", StringComparison.Ordinal));
        Assert(text.Contains("Tanılama", StringComparison.Ordinal));
        Assert(text.Contains("Debug tanılama", StringComparison.Ordinal));
        Assert(text.Contains("Kapalı. Normal rapor hafif kalır.", StringComparison.Ordinal));
        Assert(text.Contains("Rapor hazırla", StringComparison.Ordinal));
        Assert(text.Contains("Bağlanınca test", StringComparison.Ordinal));
        Assert(text.Contains("Hedef testi:", StringComparison.Ordinal));
        Assert(text.Contains("Hazır", StringComparison.Ordinal));
        Assert(text.Contains("Astral Bağlı Değil", StringComparison.Ordinal));
        Assert(text.Contains(
            "Bağlantı sorunlarını incelemek için rapor hazırla",
            StringComparison.Ordinal));
        var buttonNames = FindVisualChildren<Button>(window)
            .Where(button => button.Visibility == Visibility.Visible)
            .Select(AutomationProperties.GetName)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();
        Assert(buttonNames.Contains("Astral onar"));
        Assert(buttonNames.Contains("Profil temizle"));
        Assert(buttonNames.Contains("Tanı paketi oluştur"));
        Assert(buttonNames.Contains("Tanılama raporu hazırla"));
        Assert(buttonNames.Contains("Seçili hedefleri hızlı test et"));
        Assert(buttonNames.Contains("Sürüm notlarını aç"));
        Assert(!buttonNames.Any(button => button.Contains("WireSock", StringComparison.OrdinalIgnoreCase)));
        Assert(!buttonNames.Any(button =>
            button.Contains("Denetle", StringComparison.Ordinal)
            || button.Contains("Güncelle", StringComparison.Ordinal)
            || button.Contains("Tekrar dene", StringComparison.Ordinal)
            || button.Contains("Güncel", StringComparison.Ordinal)));
        Assert(!buttonNames.Contains("Yükle"));
        var updateButton = FindVisualChildren<Button>(window)
            .Single(button => button.Name == "AutoUpdateButton");
        Assert(updateButton.Visibility == Visibility.Collapsed);
        var targetQuickTestButton = FindVisualChildren<Button>(window)
            .Single(button => button.Name == "TargetQuickTestButton");
        Assert(!targetQuickTestButton.IsEnabled);
        var releaseNotesButton = FindVisualChildren<Button>(window)
            .Single(button => button.Name == "ReleaseNotesButton");
        var releaseNotesWindowOpened = false;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var releaseWindow = Application.Current.Windows
                .OfType<ReleaseNotesWindow>()
                .SingleOrDefault();
            if (releaseWindow is not null)
            {
                releaseNotesWindowOpened = true;
                releaseWindow.Close();
            }
        }), DispatcherPriority.ApplicationIdle);
        releaseNotesButton.RaiseEvent(new RoutedEventArgs(
            ButtonBase.ClickEvent,
            releaseNotesButton));
        Assert(releaseNotesWindowOpened);
        var switches = FindVisualChildren<CheckBox>(window).ToArray();
        var targetToggles = switches
            .Where(toggle => toggle.Name.StartsWith(
                "TargetToggle_",
                StringComparison.Ordinal))
            .ToArray();
        Assert(targetToggles.Length == 9);
        var targetRows = FindVisualChildren<UniformGrid>(window)
            .Where(grid => grid.Name is "TargetCardsTopPanel" or "TargetCardsBottomPanel")
            .ToDictionary(grid => grid.Name, grid => grid);
        Assert(targetRows["TargetCardsTopPanel"].Children.Count == 5);
        Assert(targetRows["TargetCardsBottomPanel"].Children.Count == 4);
        var targetNames = targetToggles
            .Select(AutomationProperties.GetName)
            .ToArray();
        var expectedTargets = new[]
                 {
                     "Discord hedefi",
                     "Roblox hedefi",
                     "Wattpad hedefi",
                     "Bigo Live hedefi",
                     "Azar hedefi",
                     "Tango hedefi",
                     "LiVU hedefi",
                     "IMVU hedefi",
                     "Blogspot hedefi"
                 };
        foreach (var expectedTarget in expectedTargets)
        {
            Assert(targetNames.Contains(expectedTarget));
        }
        foreach (var iconKey in new[]
                 {
                     "discord",
                     "roblox",
                     "wattpad",
                     "bigo-live",
                     "azar",
                     "tango",
                     "livu",
                     "imvu",
                     "blogspot"
                 })
        {
            Assert(MainWindow.TryLoadTargetIconForTesting(iconKey));
        }
        Assert(MainWindow.TryReadProxyPortFromPacForTesting(
            "function FindProxyForURL(url, host) { return \"PROXY 127.0.0.1:55010\"; }",
            out var parsedProxyPort));
        Assert(parsedProxyPort == 55010);
        Assert(!MainWindow.TryReadProxyPortFromPacForTesting(
            "function FindProxyForURL(url, host) { return \"DIRECT\"; }",
            out _));
        var registry = TargetRegistry.CreateDefault();
        Assert(registry.TryGet("blogspot", out var blogspotTarget));
        Assert(MainWindow.GetTargetTestHostForTesting(blogspotTarget) == "blogspot.com");

        var visibleTargetCardTexts = targetToggles
            .SelectMany(toggle => FindVisualChildren<TextBlock>(toggle))
            .Select(block => block.Text)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        foreach (var expectedTarget in new[]
                 {
                     "Discord",
                     "Roblox",
                     "Wattpad",
                     "Bigo Live",
                     "Azar",
                     "Tango",
                     "LiVU",
                     "IMVU",
                     "Blogspot"
                 })
        {
            Assert(visibleTargetCardTexts.Contains(expectedTarget));
        }
        Assert(visibleTargetCardTexts.Any(value => value is "Seçili"));
        Assert(visibleTargetCardTexts.Any(value => value is "Hazır"));
        Assert(visibleTargetCardTexts.Any(value => value is "Test yok"));

        foreach (var targetToggle in targetToggles)
        {
            var cardTexts = FindVisualChildren<TextBlock>(targetToggle)
                .Select(block => block.Text)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            Assert(!cardTexts.Any(cardText => cardText is "Uygulama + Web" or "Web"));
            Assert(targetToggle.ToolTip is ToolTip);
        }

        Assert(targetToggles.Any(toggle =>
            toggle.Name == "TargetToggle_discord"
            && toggle.IsChecked == true));
        Assert(targetToggles.Any(toggle => toggle.Name == "TargetToggle_roblox"));
        Assert(targetToggles.Any(toggle => toggle.Name == "TargetToggle_wattpad"));
        Assert(targetToggles.All(toggle =>
            !toggle.Name.Contains("custom", StringComparison.OrdinalIgnoreCase)));

        var robloxTarget = targetToggles.Single(toggle =>
            toggle.Name == "TargetToggle_roblox");
        robloxTarget.IsChecked = true;
        window.UpdateLayout();
        Assert(GetController(window).TargetSelection.SelectedTargetIds
            .Contains("roblox", StringComparer.OrdinalIgnoreCase));

        var runInBackgroundSwitch = switches.Single(toggle =>
            toggle.Name == "RunInBackgroundToggle");
        var startupSwitch = switches.Single(toggle =>
            toggle.Name == "StartupToggle");
        var debugDiagnosticsSwitch = switches.Single(toggle =>
            toggle.Name == "DebugDiagnosticsToggle");
        Assert(runInBackgroundSwitch.IsChecked == false);
        Assert(startupSwitch.IsChecked == false);
        Assert(debugDiagnosticsSwitch.IsChecked == false);
        Assert(
            string.Equals(
                debugDiagnosticsSwitch.ToolTip?.ToString(),
                "Ayrıntılı ağ ve performans verisi ekle",
                StringComparison.Ordinal));
        var stageProgressPercent = FindVisualChildren<TextBlock>(window)
            .Single(block => block.Name == "StageProgressPercent");
        Assert(stageProgressPercent.Text == "0%");
        var stageProgressBar = FindVisualChildren<ProgressBar>(window)
            .Single();
        Assert(stageProgressBar.Value == 0);
        Assert(text.Contains("DNS SUNUCUSU", StringComparison.Ordinal));
        Assert(text.Contains("BAĞLANTI DURUMU", StringComparison.Ordinal));
        Assert(text.Contains("KAPSAM", StringComparison.Ordinal));
        Assert(text.Contains("1 hedef seçili", StringComparison.Ordinal));
        Assert(!text.Contains("ÖZEL BAĞLANTI", StringComparison.Ordinal));
        Assert(!text.Contains("Discord bağlantısını yönet", StringComparison.Ordinal));
        Assert(!text.Contains("KAPANIŞ", StringComparison.Ordinal));
        Assert(!text.Contains("Advanced SplitWire", StringComparison.OrdinalIgnoreCase));
        Assert(!text.Contains("Discord-only", StringComparison.OrdinalIgnoreCase));

        SaveWindowPng(window, Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "ui-window.png"));

        window.Close();
        window = null;

        Environment.SetEnvironmentVariable(
            "ASTRAL_BACKGROUND_VIDEO_REMOTE_FALLBACK",
            "1");

        window = CreateMainWindow(root);
        window.Show();
        window.UpdateLayout();

        backgroundVideo = FindVisualChildren<MediaElement>(window).Single();
        Assert(backgroundVideo.Visibility == Visibility.Visible);
        Assert(backgroundVideo.Source is not null);
    }
    finally
    {
        window?.Close();
        Environment.SetEnvironmentVariable(
            "ASTRAL_DISABLE_BACKGROUND_VIDEO",
            previousVideoSetting);
        Environment.SetEnvironmentVariable(
            "ASTRAL_BACKGROUND_VIDEO_REMOTE_FALLBACK",
            previousRemoteFallbackSetting);
        Directory.Delete(root, recursive: true);
    }

    Console.WriteLine("GEÇTİ Ana pencere çizildi");
}

static void VerifyWindowLifetimeBehavior()
{
    VerifyCloseHidesToTrayWhenRunInBackgroundIsEnabled();
    VerifyTrayExitDisposesActiveConnectionWhenRunInBackgroundIsEnabled();
    VerifyTrayExitDoesNotHangWhenControllerCleanupIsSlow();

    Console.WriteLine("GEÇTİ Pencere kapanış ve arka plan davranışı doğrulandı");
}

static void VerifyConnectedTargetCardsRequireProbeEvidence()
{
    MainWindow? window = null;
    var root = CreateTemporaryDirectory();
    var launcher = new FakeProcessLauncher(
        wireSockLogLines: ["Wireguard tunnel has been started."]);

    try
    {
        window = CreateMainWindow(root, launcher);
        window.Show();
        window.UpdateLayout();

        RunDispatcherTask(
            GetController(window).ConnectAsync(CancellationToken.None),
            TimeSpan.FromSeconds(3));
        window.UpdateLayout();

        var discordCard = FindVisualChildren<CheckBox>(window)
            .Single(toggle => toggle.Name == "TargetToggle_discord");
        var cardTexts = FindVisualChildren<TextBlock>(discordCard)
            .Select(block => block.Text)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert(!cardTexts.Contains("Kapsamda", StringComparer.Ordinal));
        Assert(cardTexts.Contains("Test bekliyor", StringComparer.Ordinal));

        var targetQuickTestButton = FindVisualChildren<Button>(window)
            .Single(button => button.Name == "TargetQuickTestButton");
        Assert(targetQuickTestButton.IsEnabled);

        Console.WriteLine("GEÇTİ Bağlı hedef kartı test kanıtı olmadan kapsamda görünmedi");
    }
    finally
    {
        ForceCloseWindow(window);
        Directory.Delete(root, recursive: true);
    }
}

static void VerifyTrayExitDoesNotHangWhenControllerCleanupIsSlow()
{
    MainWindow? window = null;
    var root = CreateTemporaryDirectory();
    var launcher = new FakeProcessLauncher(
        TimeSpan.FromSeconds(5),
        ["Wireguard tunnel has been started."]);
    var previousTimeout = MainWindow.ShutdownCleanupTimeout;

    try
    {
        MainWindow.ShutdownCleanupTimeout = TimeSpan.FromMilliseconds(75);
        new AppSettingsStore(new AppPaths(root))
            .SetRunInBackgroundOnCloseEnabled(true);
        window = CreateMainWindow(root, launcher);
        var controller = GetController(window);
        window.Show();
        window.UpdateLayout();
        RunDispatcherTask(
            controller.ConnectAsync(CancellationToken.None),
            TimeSpan.FromSeconds(3));
        Assert(launcher.LastProcess is { HasExited: false });
        var closed = false;
        window.Closed += (_, _) => closed = true;

        var exitStartedAt = DateTimeOffset.UtcNow;
        InvokeTrayExit(window);
        PumpDispatcherUntil(() => closed, TimeSpan.FromSeconds(2));
        var exitElapsed = DateTimeOffset.UtcNow - exitStartedAt;

        Assert(closed);
        Assert(exitElapsed < TimeSpan.FromSeconds(1));
        window = null;
    }
    finally
    {
        MainWindow.ShutdownCleanupTimeout = previousTimeout;
        ForceCloseWindow(window);
        Directory.Delete(root, recursive: true);
    }
}

static void VerifyPortableInstallDiagnosticsSanitizesLegacyBrand()
{
    var legacyProductName = string.Concat("Dis", "corder");
    var details = PortableInstallDiagnostics.Capture(
        Path.Combine(
            Path.GetTempPath(),
            legacyProductName + "-2.0.20-win-x64"));

    Assert(details["portableDirectoryLooksLegacyBrand"] == "True");
    Assert(details["portableDirectoryName"] == "Astral-legacy-portable");
    Assert(details["portableDirectoryBrandStatus"] == "legacy-portable-folder");
    Assert(!details.Values
        .OfType<string>()
        .Any(value => value.Contains(legacyProductName, StringComparison.OrdinalIgnoreCase)));

    Console.WriteLine("GEÇTİ Legacy portable klasör tanılaması Astral diliyle kaldı");
}

static void VerifyCloseHidesToTrayWhenRunInBackgroundIsEnabled()
{
    MainWindow? window = null;
    var root = CreateTemporaryDirectory();

    try
    {
        new AppSettingsStore(new AppPaths(root))
            .SetRunInBackgroundOnCloseEnabled(true);
        window = CreateMainWindow(root);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        window.UpdateLayout();

        window.Close();
        PumpDispatcherUntil(() => !window.IsVisible, TimeSpan.FromSeconds(2));

        Assert(!window.IsVisible);
        Assert(!closed);
    }
    finally
    {
        ForceCloseWindow(window);
        Directory.Delete(root, recursive: true);
    }
}

static void VerifyTrayExitDisposesActiveConnectionWhenRunInBackgroundIsEnabled()
{
    MainWindow? window = null;
    var root = CreateTemporaryDirectory();
    var launcher = new FakeProcessLauncher(
        wireSockLogLines: ["Wireguard tunnel has been started."]);

    try
    {
        new AppSettingsStore(new AppPaths(root))
            .SetRunInBackgroundOnCloseEnabled(true);
        window = CreateMainWindow(root, launcher);
        var controller = GetController(window);
        window.Show();
        window.UpdateLayout();
        RunDispatcherTask(
            controller.ConnectAsync(CancellationToken.None),
            TimeSpan.FromSeconds(3));
        Assert(launcher.LastProcess is { HasExited: false });
        var closed = false;
        window.Closed += (_, _) => closed = true;

        InvokeTrayExit(window);
        PumpDispatcherUntil(() => closed, TimeSpan.FromSeconds(3));

        Assert(closed);
        Assert(launcher.LastProcess is { HasExited: true });
        window = null;
    }
    finally
    {
        ForceCloseWindow(window);
        Directory.Delete(root, recursive: true);
    }
}

static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (!condition() && DateTimeOffset.UtcNow < deadline)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

static void RunDispatcherTask(Task task, TimeSpan timeout)
{
    PumpDispatcherUntil(() => task.IsCompleted, timeout);
    task.GetAwaiter().GetResult();
}

static DiscordTunnelController GetController(MainWindow window) =>
    (DiscordTunnelController)typeof(MainWindow)
        .GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(window)!;

static void InvokeTrayExit(MainWindow window)
{
    typeof(MainWindow)
        .GetMethod("ExitFromTray", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, null);
}

static void ForceCloseWindow(MainWindow? window)
{
    if (window is null)
    {
        return;
    }

    typeof(MainWindow)
        .GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(window, true);
    window.Close();
}

static MainWindow CreateMainWindow(
    string root,
    FakeProcessLauncher? processLauncher = null)
{
    var paths = new AppPaths(root);
    var bootstrapper = new FakeWireSockBootstrapper(
        Path.Combine(root, "wiresock-client.exe"));
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        bootstrapper,
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher ?? new FakeProcessLauncher(),
        TimeSpan.Zero,
        discordProcessManager: new NullDiscordProcessManager());

    return new MainWindow(
        controller,
        paths,
        bootstrapper,
        new AppSettingsStore(paths),
        new AstralCleanupService(
            paths,
            new NullDiscordAccessLock(),
            allowNonDefaultDataRoots: true),
        new FakeStartupLaunchService(),
        new FakeWireSockUninstaller(),
        new AppUpdateService(
            new HttpClient(),
            paths,
            new FakeVerifiedDownloader(),
            requireUpdateAuthenticode: false));
}

static void RenderConsentWindow()
{
    WireSockConsentWindow? window = null;

    try
    {
        window = new WireSockConsentWindow(new FakeWireSockBootstrapper());
        window.Show();
        window.UpdateLayout();

        var text = string.Join(
            "\n",
            FindVisualChildren<TextBlock>(window).Select(block => block.Text));
        Assert(text.Contains("İlk kurulum gerekiyor", StringComparison.Ordinal));
        Assert(text.Contains("WireSock VPN Client 1.4.7.1", StringComparison.Ordinal));
        Assert(text.Contains("Cloudflare WARP", StringComparison.Ordinal));

        var buttons = FindVisualChildren<Button>(window)
            .Select(button => button.Content?.ToString())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();
        Assert(buttons.Contains("Lisansı aç"));
        Assert(buttons.Contains("WARP koşulları"));
        Assert(buttons.Contains("Vazgeç"));
        Assert(buttons.Contains("Kabul et ve kur"));

        SaveWindowPng(window, Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "ui-consent-window.png"));
    }
    finally
    {
        window?.Close();
    }

    Console.WriteLine("GEÇTİ WireSock kurulum onay penceresi çizildi");
}

static void SaveWindowPng(Window window, string outputPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var width = (int)Math.Ceiling(window.ActualWidth);
    var height = (int)Math.Ceiling(window.ActualHeight);
    Assert(width >= 500);
    Assert(height >= 470);

    var bitmap = new RenderTargetBitmap(
        width,
        height,
        96,
        96,
        PixelFormats.Pbgra32);
    bitmap.Render(window);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(outputPath);
    encoder.Save(stream);
}

static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
    where T : DependencyObject
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = VisualTreeHelper.GetChild(root, index);

        if (child is T typedChild)
        {
            yield return typedChild;
        }

        foreach (var descendant in FindVisualChildren<T>(child))
        {
            yield return descendant;
        }
    }
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Astral.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Environment.CurrentDirectory;
}

static string CreateTemporaryDirectory()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "Astral.Windows.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Doğrulama koşulu başarısız oldu.");
    }
}

file sealed class FakeWireSockBootstrapper(string? executablePath = null) : IWireSockBootstrapper
{
    public string RequiredVersion => WireSockPackage.Version;

    public Uri ProductPage => WireSockPackage.ProductPage;

    public bool IsInstalled => executablePath is not null;

    public bool IsSetupConsentAccepted => executablePath is not null;

    public void AcceptSetupConsent()
    {
    }

    public Task<string> EnsureInstalledAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (executablePath is null)
        {
            throw new NotSupportedException();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        return Task.FromResult(executablePath);
    }
}

file sealed class FakeStartupLaunchService : IStartupLaunchService
{
    public bool Enabled { get; private set; }

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }
}

file sealed class FakeWireSockUninstaller : IWireSockUninstaller
{
    public Task UninstallIfAstralInstalledAsync(
        bool installedByAstral,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

file sealed class FakeVerifiedDownloader : IVerifiedDownloader
{
    public Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        throw new NotSupportedException();
    }
}

file sealed class FakeProfileProvisioner(string profilePath) : IProfileProvisioner
{
    public Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(
            profilePath,
            """
            [Interface]
            DNS = 1.1.1.1

            [Peer]
            AllowedIPs = 0.0.0.0/0
            Endpoint = 127.0.0.1:2408
            """);
        return Task.FromResult(profilePath);
    }
}

file sealed class FakeCommandRunner : ICommandRunner
{
    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }
}

file sealed class FakeProcessLauncher(
    TimeSpan? stopDelay = null,
    string[]? wireSockLogLines = null) : IProcessLauncher
{
    public FakeManagedProcess? LastProcess { get; private set; }

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        if (wireSockLogLines is { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllLines(logPath, wireSockLogLines);
        }

        LastProcess = new FakeManagedProcess(stopDelay);
        return LastProcess;
    }
}

file sealed class FakeManagedProcess(TimeSpan? stopDelay = null) : IManagedProcess
{
    public int ProcessId { get; } = 1;

    public DateTimeOffset? StartTime { get; } = DateTimeOffset.Now;

    public event EventHandler? Exited;

    public bool HasExited { get; private set; }

    public bool ExitConfirmed => HasExited;

    public int? ExitCode => HasExited ? 0 : null;

    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (stopDelay is { } delay && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        HasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        HasExited = true;
        return ValueTask.CompletedTask;
    }
}
