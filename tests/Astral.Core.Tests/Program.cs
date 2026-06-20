using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;
using Astral.Core.Configuration;
using Astral.Core.Connection;
using Astral.Core.Diagnostics;
using Astral.Core.Discord;
using Astral.Core.Firewall;
using Astral.Core.Infrastructure;
using Astral.Core.Maintenance;
using Astral.Core.Profiles;
using Astral.Core.Provisioning;
using Astral.Core.Security;
using Astral.Core.Targets;
using Astral.Core.Updates;
using Astral.Core.WebProxy;
using Astral.Core.WireSock;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Release project versions stay aligned", ReleaseProjectsUseSameVersionAsync),
    ("Hedef kayıt defteri yerleşik presetleri güvenli kapsamla tanımlar", TargetRegistryDefinesBuiltInPresetsAsync),
    ("Hedef seçimi varsayılan Discord kapsamını ve eski tarayıcı ayarını güvenli taşır", TargetSelectionStoreDefaultsAndMigratesLegacyBrowserAsync),
    ("Hedef çözümleyici web hedeflerinde tarayıcıları değil web proxy sürecini kapsar", TargetScopeResolverUsesWebProxyForWebTargetsAsync),
    ("Hedef çözümleyici tüm presetleri tek tek ve toplu güvenli kapsar", TargetScopeResolverCoversEveryPresetWithoutBrowsersAsync),
    ("Hedef kaydı rota domainlerini korur ama probe hostlarını ayırır", TargetRegistryKeepsRouteOnlyDomainsOutOfProbeHostsAsync),
    ("Hedef çözümleyici eski özel hedef girdilerini kapsama almaz", TargetScopeResolverIgnoresLegacyCustomTargetsAsync),
    ("Web proxy politikası yalnızca seçili domainleri kabul eder", WebProxyPolicyAllowsOnlySelectedDomainsAsync),
    ("PAC üretici yalnızca seçili domainleri proxyye yönlendirir", ProxyPacRoutesOnlySelectedDomainsAsync),
    ("Web proxy PAC dosyası kullanıcı veri alanında tutulur", AppPathsStoresWebProxyPacUnderUserDataDirectoryAsync),
    ("Web proxy varsayılan port doluyken yedek porta geçer", ScopedWebProxyUsesFallbackPortWhenPreferredPortIsBusyAsync),
    ("Web proxy PAC sapmasında kapsamı yeniden uygular", ScopedWebProxyEnsureReappliesMissingPacScopeAsync),
    ("Web proxy erken kapanırsa PAC uygulanmaz", ScopedWebProxyRejectsExitedProcessBeforeApplyingPacAsync),
    ("Web proxy kanıtı seçili her web hedefinden başarı ister", ScopedWebProxyProofRequiresEverySelectedWebTargetAsync),
    ("Web proxy kanıtı toplu hedeflerde seri beklemeye dönmez", ScopedWebProxyProofKeepsBoundedParallelismAsync),
    ("Web proxy kanıtı gecikmeli hedefleri paralel ölçer", ScopedWebProxyProofRunsDelayedTargetsInParallelAsync),
    ("Discord kapsamı parametresiz çağrıda sadece uygulamayı içerir", DiscordScopeDefaultsToAppOnlyAsync),
    ("Eski web kapsamı çağrısı tarayıcıları profile eklemez", DiscordScopeDoesNotIncludeBrowsersWhenLegacyFlagIsEnabledAsync),
    ("Web kapsamı PATH üzerindeki sahte tarayıcıları kapsama almaz", DiscordScopeIgnoresPathSpoofedBrowsersAsync),
    ("Profil üretici geniş AllowedApps değerlerini değiştirir", ProfileBuilderIsStrictAsync),
    ("Profil üretici boşluklu uygulama yollarını tırnaklar", ProfileBuilderQuotesApplicationsWithSpacesAsync),
    ("Profil üretici yapılandırma enjeksiyonunu reddeder", ProfileBuilderRejectsInjectionAsync),
    ("wgcf seçili hedefler için WireSock uyumlu Astral scoped profil üretir", WgcfProvisionerBuildsDiscordAccessProfileAsync),
    ("wgcf yenileme hatasında mevcut profili kullanır", WgcfProvisionerUsesCachedProfileWhenRefreshFailsAsync),
    ("wgcf boş hata çıktısını tanısız bırakmaz", WgcfProvisionerEmptyFailureIsDiagnosticAsync),
    ("wgcf hata çıktısındaki hassas değerleri redakte eder", WgcfProvisionerRedactsSensitiveFailureOutputAsync),
    ("SHA-256 doğrulayıcı yalnızca sabit özeti kabul eder", HashVerifierIsStrictAsync),
    ("Doğrulanmış indirici geçici zaman aşımını tekrar dener", VerifiedDownloaderRetriesTransientTimeoutAsync),
    ("Doğrulanmış indirici geçici DNS hatasını tekrar dener", VerifiedDownloaderRetriesTransientDnsFailureAsync),
    ("Doğrulanmış indirici duran veri akışını tekrar dener", VerifiedDownloaderRetriesStalledBodyAsync),
    ("Doğrulanmış indirici uzunluğu bilinmeyen büyük dosyayı reddeder", VerifiedDownloaderRejectsOversizedUnknownLengthAsync),
    ("Doğrulanmış indirici uzunluğu bilinmeyen indirmeyi tamamlandı bildirir", VerifiedDownloaderReportsUnknownLengthCompletionAsync),
    ("Doğrulanmış indirici eksik gelen bilinen boyutu reddeder", VerifiedDownloaderRejectsTruncatedKnownLengthAsync),
    ("WireSock kurucu indirmesi canlı ilerleme bildirir", BootstrapReportsDownloadProgressAsync),
    ("Otomatik güncelleme güncel sürümde indirme yapmaz", AppUpdateSkipsCurrentReleaseAsync),
    ("Otomatik güncelleme yeni sürümü indirmeden bildirir", AppUpdateCheckFindsReleaseWithoutDownloadAsync),
    ("Otomatik güncelleme geçici release hatasını tekrar dener", AppUpdateCheckRetriesTransientMetadataFailureAsync),
    ("Otomatik güncelleme geçici release gövde hatasını tekrar dener", AppUpdateCheckRetriesTransientMetadataBodyFailureAsync),
    ("Otomatik güncelleme geçici checksum hatasını tekrar dener", AppUpdateCheckRetriesTransientChecksumFailureAsync),
    ("Otomatik güncelleme yeni GitHub paketini hazırlar", AppUpdatePreparesVerifiedReleaseAsync),
    ("Otomatik güncelleme indirme ilerlemesini log dostu sınırlar", AppUpdateThrottlesDownloadProgressAsync),
    ("Otomatik güncelleme indirme tekrar denemesini görünür tutar", AppUpdatePreservesDownloadRetryProgressAsync),
    ("Otomatik güncelleme GitHub digest uyuşmazlığını reddeder", AppUpdateRejectsDigestMismatchAsync),
    ("Otomatik güncelleme manifest sürüm uyuşmazlığını reddeder", AppUpdateRejectsManifestVersionMismatchAsync),
    ("Otomatik güncelleme büyük checksum dosyasını reddeder", AppUpdateRejectsOversizedChecksumAsync),
    ("Otomatik güncelleme güvensiz zip yolunu reddeder", AppUpdateRejectsUnsafeArchiveEntryAsync),
    ("Otomatik güncelleme manifest dışı zip dosyasını reddeder", AppUpdateRejectsArchiveEntryOutsideManifestAsync),
    ("Otomatik güncelleme extract öncesi paket özetini yeniden doğrular", AppUpdateExtractRejectsPackageHashMismatchAsync),
    ("Otomatik güncelleme staging sürüm klasör adını doğrular", AppUpdateStagingRejectsUnsafeVersionDirectoryAsync),
    ("Otomatik güncelleme varsayılan olarak GitHub doğrulamalı paketi hazırlar", AppUpdateDefaultsToGitHubVerifiedPackageAsync),
    ("Otomatik güncelleme imza modu açılırsa tüm PE dosyalarını doğrulamaya alır", AppUpdateRequiresSignaturesForAllPortableBinariesAsync),
    ("Ayarlar WireSock onayını sürüm bazında saklar", SettingsPersistConsentAsync),
    ("Eski web kapsamı açık ayarı güvenli varsayılana taşınır", BrowserAccessLegacyImplicitEnabledMigratesToOptInAsync),
    ("WireSock hazırlığı onaysız kurulumu reddeder", BootstrapRequiresConsentAsync),
    ("WireSock hazırlığı güvenilir kurulumu yeniden kullanır", BootstrapReusesTrustedInstallAsync),
    ("WireSock hazırlığı güvenilmeyen kurulumu yok sayar", BootstrapIgnoresUntrustedInstallAsync),
    ("WireSock hazırlığı resmi paketi doğrulayıp kurar", BootstrapLifecycleAsync),
    ("WireSock hazırlığı doğrulanmış yerel kurucuyu indirmeden kullanır", BootstrapUsesVerifiedLocalInstallerAsync),
    ("WireSock hazırlığı yeniden başlatma gerektiren başarı kodunu kabul eder", BootstrapAcceptsRestartRequiredExitCodeAsync),
    ("Denetleyici idempotent bağlanır ve keser", ControllerLifecycleAsync),
    ("Denetleyici kapatma koruması yenilenemezse başarı raporlamaz", ControllerDisconnectDoesNotReportProtectedWhenAccessLockRestoreFailsAsync),
    ("Denetleyici scoped PAC doğrulanmazsa bağlı raporlamaz", ControllerDoesNotReportConnectedWhenScopedPacMissingAsync),
    ("Denetleyici web-only hedefte Discord sürecine dokunmaz", ControllerConnectsWebOnlyTargetWithoutDiscordProcessAsync),
    ("Denetleyici temiz kapanışta firewall scriptini tekrar çalıştırmaz", ControllerDisposeSkipsDisconnectedCleanupAsync),
    ("Denetleyici kilit doğrulanmadan kapanışta kilidi yeniler", ControllerDisposeRefreshesUnconfirmedDisconnectedLockAsync),
    ("Denetleyici aktif bağlantıyı kapanışta güvenle temizler", ControllerDisposeCleansActiveConnectionAsync),
    ("Denetleyici kapanışta erişim kilidi zaman aşımını fatal yapmaz", ControllerDisposeTreatsAccessLockTimeoutAsBestEffortAsync),
    ("Denetleyici WireSock kapanışı doğrulanmazsa süreç işaretini korur", ControllerKeepsWireSockMarkerWhenExitIsUnconfirmedAsync),
    ("Denetleyici yanlış WireSock süreç işaretini temizler", ControllerDeletesMismatchedWireSockMarkerAsync),
    ("Denetleyici bağlantı kapanınca hedef uygulamaları kapatmaz", ControllerDoesNotCloseTargetProcessesOnDisconnectAsync),
    ("Denetleyici hedef kapsamını bağlıyken kilitler", ControllerLocksTargetScopeWhileConnectedAsync),
    ("Denetleyici kapalı Discord'u otomatik başlatmadan doğrular", ControllerConnectsDefaultScopeWithoutLaunchingClosedTargetProcessAsync),
    ("Denetleyici transparent modda pasif adaptörü tunnel-started loguyla kabul eder", ControllerAcceptsInactiveWireSockAdapterInTransparentModeAsync),
    ("Denetleyici transparent modda eksik adaptörü tunnel-started loguyla kabul eder", ControllerAcceptsMissingWireSockAdapterInTransparentModeAsync),
    ("Denetleyici transparent modda eksik probe'u tunnel-started loguyla kabul eder", ControllerAcceptsUnavailableWireSockProbeInTransparentModeAsync),
    ("Denetleyici transparent modda trafik kanıtıyla eksik handshake logunu kabul eder", ControllerAcceptsTrafficProofWithoutWireSockHandshakeLogAsync),
    ("Denetleyici transparent modda scoped web proxy kanıtıyla pasif adaptörü kabul eder", ControllerAcceptsScopedWebProxyProofInTransparentModeAsync),
    ("Denetleyici app+web hedeflerde app kanıtı yokken bağlı raporlamaz", ControllerRejectsAppWebTargetWhenOnlyScopedWebProxyProofExistsAsync),
    ("Denetleyici çalışan Discord'u app kanıtından önce yeniler", ControllerRefreshesRunningDiscordBeforeAppTunnelProofAsync),
    ("Denetleyici uygulama ve web hedeflerinde adapter trafik kanıtını tanılamaya yazar", ControllerAcceptsAppWebTargetWithTrafficProofAsync),
    ("Denetleyici transparent modda handshake ve trafik kanıtı yokken bağlı raporlamaz", ControllerRejectsMissingWireSockHandshakeInTransparentModeAsync),
    ("Denetleyici son WireSock çıkışını tanılama detayına yazar", ControllerReportsWireSockExitAfterFinalReadinessProbeAsync),
    ("Denetleyici WireSock tunnel-started loguyla bağlı raporlar", ControllerAcceptsWireSockTunnelStartedLogAsync),
    ("Denetleyici WireSock adaptörü gecikirse yeniden dener", ControllerRetriesDelayedWireSockAdapterAsync),
    ("Denetleyici WireSock doğrulamasından önce çalışan Discord'u yeniler", ControllerRefreshesRunningDiscordBeforeWireSockReadinessAsync),
    ("Denetleyici çalışan Discord yenilenemezse bağlı raporlamaz", ControllerRequiresManualActionWhenRunningDiscordRefreshFailsAsync),
    ("Denetleyici tüm yerleşik presetleri hedef uygulama başlatmadan bağlar", ControllerConnectsEveryBuiltInPresetWithoutLaunchingTargetsAsync),
    ("Denetleyici WireSock hazırlık hatasını bildirir", ControllerBootstrapFailureAsync),
    ("Denetleyici GitHub DNS hatasını Türkçe açıklar", ControllerNetworkFailureIsUserFriendlyAsync),
    ("Denetleyici indirme zaman aşımını Türkçe açıklar", ControllerDownloadTimeoutIsUserFriendlyAsync),
    ("Denetleyici duran indirme zaman aşımını Türkçe açıklar", ControllerDirectDownloadTimeoutIsUserFriendlyAsync),
    ("Denetleyici bağlantı koruması hatasını Türkçe açıklar", ControllerAccessLockFailureIsUserFriendlyAsync),
    ("Windows Firewall koruması sessiz PowerShell hatasını operasyonla açıklar", WindowsFirewallAccessLockLabelsSilentPowerShellFailureAsync),
    ("Windows Firewall koruması seçili hedef domainleriyle kilit üretir", WindowsFirewallAccessLockUsesSelectedTargetDomainsAsync),
    ("Windows Firewall tünel kapsamı seçili hedef domainleriyle tarayıcı kaçağını kilitler", WindowsFirewallTunnelScopeUsesSelectedTargetDomainsAsync),
    ("Windows Firewall tünel hazırlığı tek PowerShell komutuyla uygulanır", WindowsFirewallPrepareTunnelScopeUsesSingleCommandAsync),
    ("Windows Firewall tünel hazırlığı sessiz PowerShell hatasını operasyonla açıklar", WindowsFirewallPrepareTunnelScopeLabelsSilentPowerShellFailureAsync),
    ("Windows Firewall koruması hedef alan adı kuralını yönetir", WindowsFirewallAccessLockBuildsExpectedCommandsAsync),
    ("Windows Firewall koruması DNS temizleme hatasını kritik çıkış kodu yapmaz", WindowsFirewallAccessLockIgnoresDnsFlushExitCodeAsync),
    ("WireSock hazırlık denetimi wt WireGuard adaptörünü tanır", TunnelReadinessRecognizesWireSockWireGuardAdapterAsync),
    ("Onarım ayarları ve logları koruyup üretilen veriyi yeniler", CleanupServiceRepairsGeneratedStateAsync),
    ("Profil temizliği Astral verisini ve korumayı siler", CleanupServiceRemovesAstralStateAsync),
    ("Profil temizliği eski marka veri köklerini de temizler", CleanupServiceRemovesLegacyBrandStateAsync),
    ("Profil temizliği tanılama loglarını geri oluşturmaz", CleanupServiceStopsDiagnosticsAfterRemovingStateAsync),
    ("Profil temizliği beklenmeyen veri kökünü reddeder", CleanupServiceRejectsUnexpectedDataRootAsync),
    ("Profil temizliği salt okunur Astral dosyalarını siler", CleanupServiceDeletesReadOnlyFilesAsync),
    ("Tanılama logları devops paketi üretir", DiagnosticsWritesDevOpsBundleAsync),
    ("Debug tanılama yalnızca açıkken ayrıntılı paket üretir", DiagnosticsWritesDebugBundleOnlyWhenEnabledAsync),
    ("Tanılama kalıcı hata loglarını redakte eder", DiagnosticsRedactsPersistentErrorLogAsync),
    ("Tanılama eski portable marka yolunu redakte eder", DiagnosticsRedactsLegacyPortableBrandInPathsAsync),
    ("Process launcher keeps dispose waits short for responsive shutdown", ProcessLauncherDisposeTimeoutsStayShortAsync),
    ("WireSock süreç logları yazılırken redakte edilir", ProcessLauncherRedactsTunnelLogAndConfirmsExitAsync),
    ("Süreç başlatıcı WireSock ve web proxy için ortak tunnel logunu paylaşır", ProcessLauncherSharesTunnelLogAcrossScopedProcessesAsync),
    ("Tanılama özeti son bilgi durumunu gecikmeli yazar", DiagnosticsFlushesDebouncedSummaryAsync)
};

_ = new Func<Task>[]
{
    ControllerClosesDiscordOnDisconnectAsync,
    ControllerVerifiesTunnelWithoutClaimingDiscordSessionAsync,
    ControllerChecksWireSockAfterDiscordRestartAsync,
    WindowsDiscordLaunchPlanUsesAppExecutableAsync,
    WindowsDiscordLaunchPlanCanUseUpdaterFallbackAsync,
    WindowsDiscordBackgroundProcessesCountAsReadyAsync,
    ControllerOpensDiscordWhenItIsClosedAsync,
    ControllerKeepsTunnelActiveWhenDiscordRestartFailsAsync,
    ControllerRequiresManualActionWhenDiscordWindowIsHiddenAsync,
    ControllerVerifiesDiscordWhenWindowAppearsLateAsync,
    ControllerRecoversDiscordUpdaterLoopAsync,
    ControllerRequiresManualActionWhenPostRecoveryVerificationFailsAsync,
    ControllerVerifiesDiscordAfterUpdaterDirectRetryStillShowsUpdaterAsync,
    ControllerKeepsTunnelActiveWhenDiscordUpdaterDirectRestartFailsAsync,
    ControllerRestoresLockWhenDiscordUpdaterRecoveryFailsAsync
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"GEÇTİ {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.Error.WriteLine($"KALDI {test.Name}");
        Console.Error.WriteLine(exception);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test başarısız oldu.");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} test geçti.");
return 0;

static Task ReleaseProjectsUseSameVersionAsync()
{
    var root = FindRepositoryRoot();
    var projects = new[]
    {
        ("Astral.App", Path.Combine(root, "src", "Astral.App", "Astral.App.csproj")),
        ("Astral.Updater", Path.Combine(root, "src", "Astral.Updater", "Astral.Updater.csproj")),
        ("Astral.WebProxy", Path.Combine(root, "src", "Astral.WebProxy", "Astral.WebProxy.csproj"))
    };

    var appVersion = GetProjectProperty(projects[0].Item2, "Version");
    Assert(!string.IsNullOrWhiteSpace(appVersion));
    var expectedFileVersion = appVersion + ".0";

    foreach (var (name, path) in projects)
    {
        var version = GetProjectProperty(path, "Version");
        var fileVersion = GetProjectProperty(path, "FileVersion");
        if (!string.Equals(version, appVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} Version {version} != {appVersion}");
        }

        if (!string.Equals(fileVersion, expectedFileVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} FileVersion {fileVersion} != {expectedFileVersion}");
        }
    }

    var manifest = File.ReadAllText(Path.Combine(
        root,
        "src",
        "Astral.App",
        "app.manifest"));
    Assert(manifest.Contains(
        $"<assemblyIdentity version=\"{expectedFileVersion}\" name=\"Astral\"",
        StringComparison.Ordinal));

    return Task.CompletedTask;
}

static Task TargetRegistryDefinesBuiltInPresetsAsync()
{
    var registry = TargetRegistry.CreateDefault();
    var targets = registry.GetBuiltInTargets().ToArray();

    Assert(targets.Length == 15);
    Assert(targets.Select(target => target.Id).SequenceEqual(
        [
            TargetIds.Discord,
            TargetIds.Wattpad,
            TargetIds.Azar,
            TargetIds.BigoLive,
            TargetIds.IMVU,
            TargetIds.LiVU,
            TargetIds.Tango,
            TargetIds.Blogspot,
            TargetIds.RadioGarden,
            TargetIds.DeutscheWelle,
            TargetIds.VoiceOfAmerica,
            TargetIds.EksiSozluk,
            TargetIds.Grok,
            TargetIds.Imgur,
            TargetIds.Pastebin
        ]));
    Assert(registry.TryGet(TargetIds.Discord, out var discord));
    Assert(discord.Label == "Discord");
    Assert(discord.ScopeKind == TargetScopeKind.ApplicationAndWeb);
    Assert(discord.Domains.Any(domain => domain.Value == "discord.com"));
    Assert(discord.ExecutableHints.Any(hint =>
        hint.FileName.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));

    Assert(registry.TryGet(TargetIds.Wattpad, out var wattpad));
    Assert(wattpad.ScopeKind == TargetScopeKind.Web);
    Assert(wattpad.ExecutableHints.Count == 0);
    Assert(wattpad.Domains.Any(domain => domain.Value == "api.wattpad.com"));

    Assert(registry.TryGet(TargetIds.Blogspot, out var blogspot));
    Assert(blogspot.Domains.Any(domain => domain.Value == "blogspot.com"));
    Assert(blogspot.Domains.Any(domain => domain.Value == "blogger.com"));

    Assert(registry.TryGet(TargetIds.IMVU, out var imvu));
    Assert(imvu.Domains.Any(domain => domain.Value == "secure.imvu.com"));

    Assert(registry.TryGet(TargetIds.BigoLive, out var bigoLive));
    Assert(bigoLive.Domains.Any(domain => domain.Value == "www.bigo.tv"));

    Assert(registry.TryGet(TargetIds.RadioGarden, out var radioGarden));
    Assert(radioGarden.ScopeKind == TargetScopeKind.Web);
    Assert(radioGarden.Domains.Any(domain => domain.Value == "radio.garden"));

    Assert(registry.TryGet(TargetIds.DeutscheWelle, out var deutscheWelle));
    Assert(deutscheWelle.Domains.Any(domain => domain.Value == "dw.com"));

    Assert(registry.TryGet(TargetIds.VoiceOfAmerica, out var voiceOfAmerica));
    Assert(voiceOfAmerica.Domains.Any(domain => domain.Value == "voanews.com"));

    Assert(registry.TryGet(TargetIds.EksiSozluk, out var eksiSozluk));
    Assert(eksiSozluk.Domains.Any(domain => domain.Value == "eksisozluk.com"));

    Assert(registry.TryGet(TargetIds.Grok, out var grok));
    Assert(grok.Domains.Any(domain => domain.Value == "grok.com"));

    Assert(registry.TryGet(TargetIds.Imgur, out var imgur));
    Assert(imgur.Domains.Any(domain => domain.Value == "imgur.com"));

    Assert(registry.TryGet(TargetIds.Pastebin, out var pastebin));
    Assert(pastebin.Domains.Any(domain => domain.Value == "pastebin.com"));

    Assert(targets.All(target =>
        target.Metadata.TryGetValue("launchUrl", out var launchUrl)
        && Uri.TryCreate(launchUrl, UriKind.Absolute, out var launchUri)
        && launchUri.Scheme == Uri.UriSchemeHttps
        && target.Domains.Any(domain => domain.Matches(launchUri.Host))));

    Assert(!targets.Any(target =>
        target.Id.Equals("custom-executable", StringComparison.OrdinalIgnoreCase)
        || target.Id.Equals("custom-domain", StringComparison.OrdinalIgnoreCase)));

    Assert(targets.All(target =>
        !target.Metadata.Values.Any(value =>
            value.Contains("kesin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ban riski yok", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tespit edilemez", StringComparison.OrdinalIgnoreCase)
            || value.Contains("engelli", StringComparison.OrdinalIgnoreCase))));

    return Task.CompletedTask;
}

static async Task TargetSelectionStoreDefaultsAndMigratesLegacyBrowserAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var paths = new AppPaths(root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "AcceptedWireSockVersion": "1.4.7.1",
              "AcceptedCloudflareWarpTerms": true,
              "BrowserAccessEnabled": true
            }
            """);

        var store = new AppSettingsStore(paths);
        Assert(store.EnsureTargetSelectionInitialized());
        var selection = store.GetTargetSelection();

        Assert(selection.SelectedTargetIds.SequenceEqual([TargetIds.Discord]));
        Assert(!store.IsBrowserAccessEnabled());

        var reloaded = new AppSettingsStore(paths);
        Assert(!reloaded.EnsureTargetSelectionInitialized());
        Assert(reloaded.GetTargetSelection().SelectedTargetIds.SequenceEqual([TargetIds.Discord]));

        var updated = new TargetSelection(
            [TargetIds.Discord, TargetIds.Wattpad, TargetIds.Blogspot]);
        reloaded.SetTargetSelection(updated);

        var saved = new AppSettingsStore(paths).GetTargetSelection();
        Assert(saved.SelectedTargetIds.SequenceEqual(
            [TargetIds.Discord, TargetIds.Wattpad, TargetIds.Blogspot]));

        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "BrowserAccessEnabled": true,
              "BrowserAccessPreferenceVersion": 1,
              "TargetSelectionPreferenceVersion": 1,
              "TargetSelection": {
                "SelectedTargetIds": [
                  "discord",
                  "custom-domain",
                  "wattpad",
                  "custom-executable",
                  "not-supported"
                ],
                "CustomExecutables": [
                  "C:\\Tools\\legacy.exe"
                ],
                "CustomDomains": [
                  "*.example.com"
                ]
              }
            }
            """);
        var migratedLegacyCustom = new AppSettingsStore(paths).GetTargetSelection();
        Assert(migratedLegacyCustom.SelectedTargetIds.SequenceEqual(
            [TargetIds.Discord, TargetIds.Wattpad]));

        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "acceptedWireSockVersion": "1.4.7.1",
              "acceptedCloudflareWarpTerms": true,
              "browserAccessEnabled": false,
              "browserAccessPreferenceVersion": 1,
              "targetSelectionPreferenceVersion": 2,
              "targetSelection": {
                "selectedTargetIds": [
                  "wattpad"
                ]
              }
            }
            """);
        var camelCaseSelection = new AppSettingsStore(paths).GetTargetSelection();
        Assert(camelCaseSelection.SelectedTargetIds.SequenceEqual([TargetIds.Wattpad]));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TargetScopeResolverUsesWebProxyForWebTargetsAsync()
{
    var root = CreateTemporaryDirectory();
    var webProxyPath = Path.Combine(root, "Astral.WebProxy.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(webProxyPath)!);
    File.WriteAllText(webProxyPath, "proxy");

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "Discord", "app-1.0.9999"));
        File.WriteAllText(
            Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe"),
            "discord");

        var resolver = new TargetScopeResolver(
            TargetRegistry.CreateDefault(),
            new DiscordAppScope(root, root, root),
            webProxyPath);

        var plan = resolver.Resolve(new TargetSelection(
            [TargetIds.Discord, TargetIds.Wattpad, TargetIds.Blogspot]));

        Assert(plan.AllowedApplications.Any(app =>
            app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(plan.AllowedApplications.Any(app =>
            app.Equals(webProxyPath, StringComparison.OrdinalIgnoreCase)));
        Assert(plan.ProxyRules.Any(rule => rule.Pattern == "wattpad.com"));
        Assert(plan.ProxyRules.Any(rule => rule.Pattern == "blogspot.com"));
        Assert(!plan.ProxyRules.Any(rule => rule.Pattern.Contains("gameclient", StringComparison.OrdinalIgnoreCase)));
        Assert(plan.AllowedApplications.All(app => !RoutingPlan.IsBrowserExecutable(app)));

        var wattpadOnly = resolver.Resolve(new TargetSelection(
            [TargetIds.Wattpad]));
        Assert(wattpadOnly.AllowedApplications.SequenceEqual([webProxyPath]));
        Assert(wattpadOnly.AllowedApplications.All(app => !RoutingPlan.IsBrowserExecutable(app)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TargetScopeResolverCoversEveryPresetWithoutBrowsersAsync()
{
    var root = CreateTemporaryDirectory();
    var webProxyPath = Path.Combine(root, "Astral.WebProxy.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(webProxyPath)!);
    File.WriteAllText(webProxyPath, "proxy");

    try
    {
        var discordAppDirectory = Path.Combine(root, "Discord", "app-1.0.9999");
        Directory.CreateDirectory(discordAppDirectory);
        File.WriteAllText(Path.Combine(discordAppDirectory, "Discord.exe"), "discord");

        var registry = TargetRegistry.CreateDefault();
        var resolver = new TargetScopeResolver(
            registry,
            new DiscordAppScope(root, root, root),
            webProxyPath);
        var targets = registry.GetBuiltInTargets();

        foreach (var target in targets)
        {
            var plan = resolver.Resolve(new TargetSelection([target.Id]));
            Assert(plan.SelectedTargets.SequenceEqual([target]));
            Assert(plan.AllowedApplications.All(app => !RoutingPlan.IsBrowserExecutable(app)));

            if (target.HasWebScope)
            {
                Assert(plan.AllowedApplications.Contains(webProxyPath, StringComparer.OrdinalIgnoreCase));
                foreach (var domain in target.Domains)
                {
                    Assert(plan.ProxyRules.Any(rule => rule.Pattern == domain.Pattern));
                }
            }
            else
            {
                Assert(!plan.AllowedApplications.Contains(webProxyPath, StringComparer.OrdinalIgnoreCase));
                Assert(plan.ProxyRules.Count == 0);
            }

            if (target.HasApplicationScope
                && !target.Id.Equals(TargetIds.Discord, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var hint in target.ExecutableHints)
                {
                    Assert(plan.AllowedApplications.Contains(
                        hint.FileName,
                        StringComparer.OrdinalIgnoreCase));
                }
            }

            foreach (var other in targets.Where(other => other.Id != target.Id))
            {
                foreach (var hint in other.ExecutableHints)
                {
                    Assert(!plan.AllowedApplications.Contains(
                        hint.FileName,
                        StringComparer.OrdinalIgnoreCase));
                }
            }

            if (!target.Id.Equals(TargetIds.Discord, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var other in targets.Where(other => other.Id != target.Id))
                {
                    Assert(!plan.ProxyRules.Any(rule =>
                        other.Domains.Any(domain => domain.Pattern == rule.Pattern)));
                }
            }
        }

        var allPlan = resolver.Resolve(new TargetSelection(targets.Select(target => target.Id).ToArray()));
        Assert(allPlan.SelectedTargets.Count == targets.Count);
        Assert(allPlan.AllowedApplications.Contains(webProxyPath, StringComparer.OrdinalIgnoreCase));
        Assert(allPlan.AllowedApplications.All(app => !RoutingPlan.IsBrowserExecutable(app)));
        foreach (var hint in targets
                     .Where(target => !target.Id.Equals(
                         TargetIds.Discord,
                         StringComparison.OrdinalIgnoreCase))
                     .SelectMany(target => target.ExecutableHints))
        {
            Assert(allPlan.AllowedApplications.Contains(
                hint.FileName,
                StringComparer.OrdinalIgnoreCase));
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TargetRegistryKeepsRouteOnlyDomainsOutOfProbeHostsAsync()
{
    var root = CreateTemporaryDirectory();
    var webProxyPath = Path.Combine(root, "Astral.WebProxy.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(webProxyPath)!);
    File.WriteAllText(webProxyPath, "proxy");

    try
    {
        var registry = TargetRegistry.CreateDefault();
        var resolver = new TargetScopeResolver(
            registry,
            new DiscordAppScope(root, root, root),
            webProxyPath);
        var targets = registry.GetBuiltInTargets();
        var plan = resolver.Resolve(new TargetSelection(targets.Select(target => target.Id).ToArray()));
        var routeRules = plan.ProxyRules
            .Select(rule => rule.Pattern)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert(routeRules.Contains("discordapp.net"));
        Assert(routeRules.Contains("api.azarlive.io"));
        Assert(routeRules.Contains("api.livu.me"));
        Assert(routeRules.Contains("api.tango.me"));

        Assert(registry.TryGet(TargetIds.Discord, out var discord));
        var discordProbes = GetProbeHosts(discord);
        Assert(discordProbes.Contains("discord.com"));
        Assert(!discordProbes.Contains("discordapp.net"));

        Assert(registry.TryGet(TargetIds.Azar, out var azar));
        var azarProbes = GetProbeHosts(azar);
        Assert(azarProbes.Contains("azarlive.com"));
        Assert(!azarProbes.Contains("api.azarlive.io"));

        Assert(registry.TryGet(TargetIds.LiVU, out var livu));
        var livuProbes = GetProbeHosts(livu);
        Assert(livuProbes.Contains("livu.me"));
        Assert(!livuProbes.Contains("api.livu.me"));

        Assert(registry.TryGet(TargetIds.Tango, out var tango));
        var tangoProbes = GetProbeHosts(tango);
        Assert(tangoProbes.Contains("tango.me"));
        Assert(!tangoProbes.Contains("api.tango.me"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TargetScopeResolverIgnoresLegacyCustomTargetsAsync()
{
    var root = CreateTemporaryDirectory();
    var validExe = Path.Combine(root, "tools", "custom.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(validExe)!);
    File.WriteAllText(validExe, "exe");
    var webProxyPath = Path.Combine(root, "Astral.WebProxy.exe");
    File.WriteAllText(webProxyPath, "proxy");

    try
    {
        var resolver = new TargetScopeResolver(
            TargetRegistry.CreateDefault(),
            new DiscordAppScope(root, root, root),
            webProxyPath);
        var plan = resolver.Resolve(new TargetSelection(
            [TargetIds.Wattpad, "custom-domain", "custom-executable"]));

        Assert(plan.SelectedTargets.Single().Id == TargetIds.Wattpad);
        Assert(plan.AllowedApplications.SequenceEqual([webProxyPath]));
        Assert(!plan.AllowedApplications.Contains(validExe, StringComparer.OrdinalIgnoreCase));
        Assert(plan.ProxyRules.Any(rule => rule.Pattern == "wattpad.com"));
        Assert(!plan.ProxyRules.Any(rule => rule.Pattern == "*.example.com"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task WebProxyPolicyAllowsOnlySelectedDomainsAsync()
{
    var policy = new WebProxyAccessPolicy(
        [
            DomainPattern.Parse("wattpad.com"),
            DomainPattern.Parse("*.blogspot.com")
        ]);

    Assert(policy.IsAllowedHost("wattpad.com"));
    Assert(policy.IsAllowedHost("www.wattpad.com"));
    Assert(policy.IsAllowedHost("demo.blogspot.com"));
    Assert(!policy.IsAllowedHost("blogspot.com"));
    Assert(!policy.IsAllowedHost("evilwattpad.com"));
    Assert(!policy.IsAllowedHost("discord.com"));
    Assert(!policy.IsAllowedHost("wattpad.com.evil.test"));

    return Task.CompletedTask;
}

static Task ProxyPacRoutesOnlySelectedDomainsAsync()
{
    var script = ProxyPacScriptBuilder.Build(
        [
            DomainPattern.Parse("wattpad.com"),
            DomainPattern.Parse("*.blogspot.com")
        ],
        "127.0.0.1",
        18088);

    Assert(script.Contains("PROXY 127.0.0.1:18088", StringComparison.Ordinal));
    Assert(script.Contains("DIRECT", StringComparison.Ordinal));
    Assert(script.Contains("dnsDomainIs(host, \"wattpad.com\")", StringComparison.Ordinal));
    Assert(script.Contains("shExpMatch(host, \"*.blogspot.com\")", StringComparison.Ordinal));
    Assert(!script.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
    Assert(!script.Contains("discord.com", StringComparison.OrdinalIgnoreCase));

    return Task.CompletedTask;
}

static Task AppPathsStoresWebProxyPacUnderUserDataDirectoryAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));

    try
    {
        Assert(paths.WebProxyPacFile.StartsWith(
            paths.DataDirectory,
            StringComparison.OrdinalIgnoreCase));
        Assert(paths.WebProxyStateFile.StartsWith(
            paths.DataDirectory,
            StringComparison.OrdinalIgnoreCase));
        Assert(!paths.WebProxyPacFile.StartsWith(
            paths.SharedDataDirectory,
            StringComparison.OrdinalIgnoreCase));

        paths.EnsureDirectories();

        Assert(Directory.Exists(paths.WebProxyDirectory));
        Assert(File.GetAttributes(paths.WebProxyDirectory)
            .HasFlag(FileAttributes.Directory));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static async Task ScopedWebProxyUsesFallbackPortWhenPreferredPortIsBusyAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var preferredPort = ReserveTcpPort();
    using var listener = new TcpListener(IPAddress.Loopback, preferredPort);
    listener.Start();
    var runner = new RecordingCommandRunner();
    var process = new FakeManagedProcess();
    var launcher = new FakeProcessLauncher(process, logLines: []);
    var service = new WindowsScopedWebProxyService(
        paths,
        runner,
        launcher,
        Path.Combine(root, "Astral.WebProxy.exe"),
        powerShellPath: "powershell.exe",
        preferredProxyPort: preferredPort);
    File.WriteAllText(Path.Combine(root, "Astral.WebProxy.exe"), "proxy");
    var plan = CreateWebProxyRoutingPlan(root);

    try
    {
        await service.ApplyAsync(plan, progress: null, CancellationToken.None);

        var portIndex = launcher.LastArguments
            .Select((value, index) => (value, index))
            .Single(item => item.value == "--port")
            .index + 1;
        var selectedPort = int.Parse(
            launcher.LastArguments[portIndex],
            CultureInfo.InvariantCulture);
        Assert(selectedPort != preferredPort);
        var pacScript = await File.ReadAllTextAsync(paths.WebProxyPacFile);
        Assert(pacScript.Contains(
            $"PROXY 127.0.0.1:{selectedPort.ToString(CultureInfo.InvariantCulture)}",
            StringComparison.Ordinal));
        Assert(runner.Commands.Count == 1);
    }
    finally
    {
        await service.ClearAsync(CancellationToken.None);
        listener.Stop();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ScopedWebProxyEnsureReappliesMissingPacScopeAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var expectedPacUri = new Uri(paths.WebProxyPacFile).AbsoluteUri;
    var missingPacStatus = JsonSerializer.Serialize(new
    {
        CurrentAutoConfigURL = "",
        ExpectedAutoConfigURL = expectedPacUri,
        PacFileExists = false,
        StateFileExists = false,
        StateOwner = "",
        StateAppliedAutoConfigURL = ""
    });
    var healthyPacStatus = JsonSerializer.Serialize(new
    {
        CurrentAutoConfigURL = expectedPacUri,
        ExpectedAutoConfigURL = expectedPacUri,
        PacFileExists = true,
        StateFileExists = true,
        StateOwner = "Astral",
        StateAppliedAutoConfigURL = expectedPacUri
    });
    var runner = new SequencedCommandRunner(
        new CommandResult(0, missingPacStatus, string.Empty),
        new CommandResult(0, string.Empty, string.Empty),
        new CommandResult(0, healthyPacStatus, string.Empty));
    var process = new FakeManagedProcess();
    var launcher = new FakeProcessLauncher(process, logLines: []);
    var webProxyExecutable = Path.Combine(root, "Astral.WebProxy.exe");
    var service = new WindowsScopedWebProxyService(
        paths,
        runner,
        launcher,
        webProxyExecutable,
        powerShellPath: "powershell.exe");
    File.WriteAllText(webProxyExecutable, "proxy");
    var plan = CreateWebProxyRoutingPlan(root);

    try
    {
        var status = await service.EnsureAppliedAsync(
            plan,
            progress: null,
            CancellationToken.None);

        Assert(status.IsApplied);
        Assert(status.ProxyPort is > 0 and <= 65535);
        Assert(File.Exists(paths.WebProxyPacFile));
        Assert(runner.Commands.Count == 3);
        Assert(runner.Commands[0].Contains(
            "CurrentAutoConfigURL",
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            "Set-ItemProperty",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "CurrentAutoConfigURL",
            StringComparison.Ordinal));
    }
    finally
    {
        await service.ClearAsync(CancellationToken.None);
        Directory.Delete(root, recursive: true);
    }
}

static async Task ScopedWebProxyRejectsExitedProcessBeforeApplyingPacAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var runner = new RecordingCommandRunner();
    var process = new FakeManagedProcess(initialHasExited: true);
    var service = new WindowsScopedWebProxyService(
        paths,
        runner,
        new FakeProcessLauncher(process, logLines: []),
        Path.Combine(root, "Astral.WebProxy.exe"),
        powerShellPath: "powershell.exe");
    File.WriteAllText(Path.Combine(root, "Astral.WebProxy.exe"), "proxy");
    var plan = CreateWebProxyRoutingPlan(root);

    try
    {
        var exception = await AssertThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(plan, progress: null, CancellationToken.None));

        Assert(exception.Message.Contains(
            "yerel web proxy başlatılamadı",
            StringComparison.OrdinalIgnoreCase));
        Assert(runner.Commands.Count == 0);
        Assert(process.StopCount == 1);
    }
    finally
    {
        await service.ClearAsync(CancellationToken.None);
        Directory.Delete(root, recursive: true);
    }
}

static async Task ScopedWebProxyProofRequiresEverySelectedWebTargetAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var expectedPacUri = new Uri(paths.WebProxyPacFile).AbsoluteUri;
    var healthyPacStatus = JsonSerializer.Serialize(new
    {
        CurrentAutoConfigURL = expectedPacUri,
        ExpectedAutoConfigURL = expectedPacUri,
        PacFileExists = true,
        StateFileExists = true,
        StateOwner = "Astral",
        StateAppliedAutoConfigURL = expectedPacUri
    });
    var runner = new SequencedCommandRunner(
        new CommandResult(0, string.Empty, string.Empty),
        new CommandResult(0, healthyPacStatus, string.Empty));
    var launcher = new ConnectProofProcessLauncher(host =>
        host.EndsWith("wattpad.com", StringComparison.OrdinalIgnoreCase));
    var webProxyExecutable = Path.Combine(root, "Astral.WebProxy.exe");
    var service = new WindowsScopedWebProxyService(
        paths,
        runner,
        launcher,
        webProxyExecutable,
        powerShellPath: "powershell.exe",
        preferredProxyPort: ReserveTcpPort());
    File.WriteAllText(webProxyExecutable, "proxy");

    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        webProxyExecutable);
    var plan = resolver.Resolve(new TargetSelection(
        [TargetIds.Wattpad, TargetIds.Blogspot]));

    try
    {
        await service.ApplyAsync(plan, progress: null, CancellationToken.None);

        var proof = await service.VerifyTargetAccessAsync(
            plan,
            CancellationToken.None);

        Assert(proof.Required);
        Assert(!proof.IsVerified);
        Assert(proof.RequiredTargetCount == 2);
        Assert(proof.VerifiedTargetCount == 1);
        var failedTargets = proof.FailedTargets ?? string.Empty;
        Assert(failedTargets.Contains("Blogspot", StringComparison.Ordinal));
        Assert(!failedTargets.Contains("Wattpad", StringComparison.Ordinal));
    }
    finally
    {
        await service.ClearAsync(CancellationToken.None);
        await launcher.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static Task ScopedWebProxyProofKeepsBoundedParallelismAsync()
{
    Assert(WindowsScopedWebProxyService.MaxConcurrentProbeTargetsForTesting >= 8);
    Assert(WindowsScopedWebProxyService.TargetProofTimeoutForTesting <= TimeSpan.FromSeconds(10));
    return Task.CompletedTask;
}

static async Task ScopedWebProxyProofRunsDelayedTargetsInParallelAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var expectedPacUri = new Uri(paths.WebProxyPacFile).AbsoluteUri;
    var healthyPacStatus = JsonSerializer.Serialize(new
    {
        CurrentAutoConfigURL = expectedPacUri,
        ExpectedAutoConfigURL = expectedPacUri,
        PacFileExists = true,
        StateFileExists = true,
        StateOwner = "Astral",
        StateAppliedAutoConfigURL = expectedPacUri
    });
    var runner = new SequencedCommandRunner(
        new CommandResult(0, string.Empty, string.Empty),
        new CommandResult(0, healthyPacStatus, string.Empty));
    var launcher = new ConnectProofProcessLauncher(
        _ => true,
        TimeSpan.FromMilliseconds(250));
    var webProxyExecutable = Path.Combine(root, "Astral.WebProxy.exe");
    var service = new WindowsScopedWebProxyService(
        paths,
        runner,
        launcher,
        webProxyExecutable,
        powerShellPath: "powershell.exe",
        preferredProxyPort: ReserveTcpPort());
    File.WriteAllText(webProxyExecutable, "proxy");

    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        webProxyExecutable);
    var plan = resolver.Resolve(new TargetSelection(
    [
        TargetIds.Wattpad,
        TargetIds.Blogspot,
        TargetIds.RadioGarden,
        TargetIds.DeutscheWelle,
        TargetIds.VoiceOfAmerica,
        TargetIds.EksiSozluk,
        TargetIds.Grok,
        TargetIds.Imgur,
        TargetIds.Pastebin
    ]));

    try
    {
        await service.ApplyAsync(plan, progress: null, CancellationToken.None);

        var proof = await service.VerifyTargetAccessAsync(
            plan,
            CancellationToken.None);

        Assert(proof.IsVerified);
        Assert(proof.RequiredTargetCount == 9);
        Assert(proof.VerifiedTargetCount == 9);
        Assert(launcher.MaxActiveConnections >= 2);
    }
    finally
    {
        await service.ClearAsync(CancellationToken.None);
        await launcher.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static Task DiscordScopeDefaultsToAppOnlyAsync()
{
    var root = CreateTemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(root, "Discord"));
    var discordAppDirectory = Path.Combine(root, "Discord", "app-1.0.9999");
    Directory.CreateDirectory(discordAppDirectory);
    File.WriteAllText(Path.Combine(discordAppDirectory, "Discord.exe"), "discord");
    Directory.CreateDirectory(Path.Combine(root, "Google", "Chrome", "Application"));

    try
    {
        var apps = new DiscordAppScope(root, root, root).GetAllowedApplications();

        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(discordAppDirectory, "Discord.exe"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("Chrome", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("gameclient", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task DiscordScopeDoesNotIncludeBrowsersWhenLegacyFlagIsEnabledAsync()
{
    var root = CreateTemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(root, "Discord"));
    var discordAppDirectory = Path.Combine(root, "Discord", "app-1.0.9999");
    Directory.CreateDirectory(discordAppDirectory);
    File.WriteAllText(Path.Combine(discordAppDirectory, "Discord.exe"), "discord");
    Directory.CreateDirectory(Path.Combine(
        root,
        "Google",
        "Chrome",
        "Application"));
    File.WriteAllText(
        Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"),
        "chrome");
    Directory.CreateDirectory(Path.Combine(
        root,
        "Chromium",
        "Application"));
    File.WriteAllText(
        Path.Combine(root, "Chromium", "Application", "chromium.exe"),
        "chromium");
    Directory.CreateDirectory(Path.Combine(
        root,
        "Mozilla Firefox"));
    File.WriteAllText(Path.Combine(root, "Mozilla Firefox", "firefox.exe"), "firefox");
    Directory.CreateDirectory(Path.Combine(root, "Opera"));
    File.WriteAllText(Path.Combine(root, "Opera", "opera.exe"), "opera");

    try
    {
        var apps = new DiscordAppScope(root, root, root)
            .GetAllowedApplications(includeBrowserAccess: true);

        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(discordAppDirectory, "Discord.exe"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !RoutingPlan.IsBrowserExecutable(app)));
        Assert(apps.All(app => !app.Contains("Chrome", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("Chromium", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("Firefox", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("Opera", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("Update.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("gameclient", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task DiscordScopeIgnoresPathSpoofedBrowsersAsync()
{
    var root = CreateTemporaryDirectory();
    var spoofDirectory = Path.Combine(root, "SpoofedPath");
    Directory.CreateDirectory(Path.Combine(root, "Discord"));
    Directory.CreateDirectory(spoofDirectory);
    File.WriteAllText(Path.Combine(root, "Discord", "Discord.exe"), "discord");
    var spoofedChrome = Path.Combine(spoofDirectory, "chrome.exe");
    File.WriteAllText(spoofedChrome, "not chrome");
    var originalPath = Environment.GetEnvironmentVariable("PATH");

    try
    {
        Environment.SetEnvironmentVariable(
            "PATH",
            string.IsNullOrWhiteSpace(originalPath)
                ? spoofDirectory
                : spoofDirectory + Path.PathSeparator + originalPath);

        var apps = new DiscordAppScope(root, root, root)
            .GetAllowedApplications(includeBrowserAccess: true);

        Assert(!apps.Contains(spoofedChrome, StringComparer.OrdinalIgnoreCase));
        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Environment.SetEnvironmentVariable("PATH", originalPath);
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task ProfileBuilderIsStrictAsync()
{
    const string source = """
        [Interface]
        PrivateKey = secret
        Address = 172.16.0.2/32

        [Peer]
        PublicKey = public
        Endpoint = engage.cloudflareclient.com:2408
        AllowedApps = chrome.exe, gameclient.exe, Update.exe
        AllowedIPs = 0.0.0.0/0
        """;

    var profile = WireGuardProfileBuilder.BuildScopedProfile(
        source,
        [
            "Discord.exe",
            "chrome.exe",
            "msedge.exe",
            @"C:\Users\test\AppData\Local\Discord"
        ]);

    Assert(profile.Contains("AllowedApps = ", StringComparison.Ordinal));
    Assert(!profile.Contains("#@ws:AllowedApps", StringComparison.Ordinal));
    Assert(profile.Contains("Discord.exe", StringComparison.Ordinal));
    Assert(profile.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
    Assert(profile.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase));
    Assert(!profile.Contains("gameclient.exe", StringComparison.OrdinalIgnoreCase));
    Assert(!profile.Contains("Update.exe", StringComparison.OrdinalIgnoreCase));
    Assert(profile.Split("AllowedApps =", StringSplitOptions.None).Length == 2);
    return Task.CompletedTask;
}

static Task ProfileBuilderQuotesApplicationsWithSpacesAsync()
{
    const string source = """
        [Interface]
        PrivateKey = secret

        [Peer]
        PublicKey = public
        Endpoint = engage.cloudflareclient.com:2408
        AllowedIPs = 0.0.0.0/0
        """;

    const string chromePath =
        @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    const string bravePath =
        @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";

    var profile = WireGuardProfileBuilder.BuildScopedProfile(
        source,
        [
            "Discord.exe",
            chromePath,
            bravePath
        ]);

    var allowedAppsLine = profile
        .Split(["\r\n", "\n"], StringSplitOptions.None)
        .Single(line => line.StartsWith("AllowedApps =", StringComparison.Ordinal));

    Assert(allowedAppsLine.Contains("Discord.exe", StringComparison.Ordinal));
    Assert(allowedAppsLine.Contains($"\"{chromePath}\"", StringComparison.Ordinal));
    Assert(allowedAppsLine.Contains($"\"{bravePath}\"", StringComparison.Ordinal));
    Assert(!allowedAppsLine.Contains(
        "AllowedApps = C:\\Program Files\\",
        StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task ProfileBuilderRejectsInjectionAsync()
{
    const string source = """
        [Interface]
        PrivateKey = secret
        [Peer]
        Endpoint = example.com:2408
        """;

    AssertThrows<InvalidDataException>(() =>
        WireGuardProfileBuilder.BuildScopedProfile(
            source,
            ["Discord.exe\r\nAllowedApps = chrome.exe"]));
    return Task.CompletedTask;
}

static async Task WgcfProvisionerBuildsDiscordAccessProfileAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var downloader = new FakeVerifiedDownloader();
    var commandRunner = new FakeWgcfCommandRunner(paths);
    var provisioner = new WgcfProvisioner(
        paths,
        downloader,
        commandRunner);
    var progressMessages = new List<string>();

    try
    {
        var discordDirectory = Path.Combine(root, "Discord");
        var profilePath = await provisioner.EnsureProfileAsync(
            ["Discord.exe", "chrome.exe", "msedge.exe", discordDirectory],
            new ImmediateProgress<string>(progressMessages.Add),
            CancellationToken.None);

        var profile = await File.ReadAllTextAsync(profilePath);
        Assert(profilePath == paths.ScopedProfile);
        Assert(profilePath.EndsWith(
            "discord.conf",
            StringComparison.OrdinalIgnoreCase));
        Assert(paths.LegacyDiscordProfile == paths.ScopedProfile);
        Assert(File.Exists(paths.WgcfExecutable));
        var allowedAppsLine = profile
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.StartsWith("AllowedApps =", StringComparison.Ordinal));
        Assert(allowedAppsLine.Contains("Discord.exe", StringComparison.Ordinal));
        Assert(allowedAppsLine.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
        Assert(allowedAppsLine.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase));
        Assert(allowedAppsLine.Contains(discordDirectory, StringComparison.OrdinalIgnoreCase));
        Assert(profile.Contains(discordDirectory, StringComparison.OrdinalIgnoreCase));
        Assert(!profile.Contains("gameclient.exe", StringComparison.OrdinalIgnoreCase));
        Assert(!profile.Contains("Update.exe", StringComparison.OrdinalIgnoreCase));
        Assert(commandRunner.Commands.SequenceEqual(
            ["register --accept-tos", "generate"],
            StringComparer.Ordinal));
        Assert(progressMessages.Any(message => message.Contains(
            "Cloudflare WARP aracı",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressMessages.Any(message => message.Contains(
            "20 B / 20 B",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressMessages.Any(message => message.Contains(
            "Hedef bağlantı profili hazır",
            StringComparison.OrdinalIgnoreCase)));
        Assert(downloader.LastMaxBytes == WgcfProvisioner.WindowsX64MaxBytes);

        await provisioner.EnsureProfileAsync(
            ["Discord.exe"],
            null,
            CancellationToken.None);
        Assert(commandRunner.Commands.SequenceEqual(
            ["register --accept-tos", "generate", "generate"],
            StringComparer.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WgcfProvisionerUsesCachedProfileWhenRefreshFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    paths.EnsureDirectories();
    await File.WriteAllTextAsync(paths.WgcfAccount, "account = true");
    await File.WriteAllTextAsync(paths.WgcfBaseProfile, """
        [Interface]
        PrivateKey = cached-private-key
        Address = 172.16.0.2/32

        [Peer]
        PublicKey = cached-public-key
        Endpoint = engage.cloudflareclient.com:2408
        AllowedApps = Update.exe
        AllowedIPs = 0.0.0.0/0
        """);
    var commandRunner = new FailingGenerateWgcfCommandRunner();
    var progressMessages = new List<string>();
    var provisioner = new WgcfProvisioner(
        paths,
        new FakeVerifiedDownloader(),
        commandRunner);

    try
    {
        var profilePath = await provisioner.EnsureProfileAsync(
            ["Discord.exe"],
            new ImmediateProgress<string>(progressMessages.Add),
            CancellationToken.None);

        var profile = await File.ReadAllTextAsync(profilePath);
        Assert(commandRunner.Commands.SequenceEqual(
            ["generate"],
            StringComparer.Ordinal));
        Assert(profile.Contains("Discord.exe", StringComparison.Ordinal));
        Assert(profile.Contains("AllowedApps", StringComparison.Ordinal));
        Assert(!profile.Contains("#@ws:AllowedApps", StringComparison.Ordinal));
        Assert(!profile.Contains("Update.exe", StringComparison.OrdinalIgnoreCase));
        Assert(progressMessages.Any(message => message.Contains(
            "mevcut profil",
            StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WgcfProvisionerEmptyFailureIsDiagnosticAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var provisioner = new WgcfProvisioner(
        paths,
        new FakeVerifiedDownloader(),
        new EmptyFailureCommandRunner(exitCode: 9));

    try
    {
        var exception = await AssertThrowsAsync<InvalidOperationException>(
            () => provisioner.EnsureProfileAsync(
                ["Discord.exe"],
                null,
                CancellationToken.None));

        Assert(exception.Message.Contains(
            "Cloudflare WARP kaydı çıkış kodu 9",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "wgcf exit code 9",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WgcfProvisionerRedactsSensitiveFailureOutputAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var provisioner = new WgcfProvisioner(
        paths,
        new FakeVerifiedDownloader(),
        new SensitiveFailureCommandRunner());

    try
    {
        var exception = await AssertThrowsAsync<InvalidOperationException>(
            () => provisioner.EnsureProfileAsync(
                ["Discord.exe"],
                null,
                CancellationToken.None));

        Assert(exception.Message.Contains(
            "PrivateKey = [REDACTED]",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "Authorization = [REDACTED]",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "normal diagnostic",
            StringComparison.Ordinal));
        Assert(!exception.Message.Contains("super-private-key", StringComparison.Ordinal));
        Assert(!exception.Message.Contains("fake-private-key-with-padding", StringComparison.Ordinal));
        Assert(!exception.Message.Contains("example-token-value", StringComparison.Ordinal));
        Assert(!exception.Message.Contains("fake:token", StringComparison.Ordinal));
        Assert(!exception.Message.Contains("session-cookie-value", StringComparison.Ordinal));
        Assert(exception.Message.Length < 520);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task HashVerifierIsStrictAsync()
{
    var root = CreateTemporaryDirectory();
    var path = Path.Combine(root, "payload.bin");
    var payload = Encoding.UTF8.GetBytes("astral");
    await File.WriteAllBytesAsync(path, payload);
    var expected = Convert.ToHexString(SHA256.HashData(payload));

    try
    {
        Assert(await FileHashVerifier.MatchesSha256Async(path, expected));
        Assert(!await FileHashVerifier.MatchesSha256Async(path, new string('0', 64)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRetriesTransientTimeoutAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("dogrulanmis-kurucu");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var handler = new FlakyDownloadHandler(failuresBeforeSuccess: 1, payload);
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(
        httpClient,
        maxAttempts: 2,
        retryDelay: TimeSpan.Zero);
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await downloader.DownloadAsync(
            new Uri("https://downloads.example.test/wiresock.msi"),
            destination,
            expectedSha256,
            CancellationToken.None,
            progress: new ImmediateProgress<DownloadProgress>(
                progressEvents.Add));

        Assert(handler.RequestCount == 2);
        Assert(await File.ReadAllTextAsync(destination) == "dogrulanmis-kurucu");
        Assert(!File.Exists(destination + ".download"));
        Assert(progressEvents.Any(item =>
            item.IsRetry
            && item.Attempt == 2
            && item.MaxAttempts == 2
            && item.Message is not null
            && item.Message.Contains(
                "tekrar deneniyor",
                StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRetriesTransientDnsFailureAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("dogrulanmis-kurucu");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var handler = new FlakyDnsDownloadHandler(payload);
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(
        httpClient,
        maxAttempts: 2,
        retryDelay: TimeSpan.Zero);
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await downloader.DownloadAsync(
            new Uri("https://github.com/example/wiresock.msi"),
            destination,
            expectedSha256,
            CancellationToken.None,
            progress: new ImmediateProgress<DownloadProgress>(
                progressEvents.Add));

        Assert(handler.RequestCount == 2);
        Assert(await File.ReadAllTextAsync(destination) == "dogrulanmis-kurucu");
        Assert(!File.Exists(destination + ".download"));
        Assert(progressEvents.Any(item =>
            item.IsRetry
            && item.Message is not null
            && item.Message.Contains(
                "Bağlantı kurulamadı",
                StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRetriesStalledBodyAsync()
{
    var root = CreateTemporaryDirectory();
    var handler = new StalledDownloadHandler();
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(
        httpClient,
        maxAttempts: 2,
        retryDelay: TimeSpan.Zero,
        readIdleTimeout: TimeSpan.FromMilliseconds(20));
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await AssertThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(
                new Uri("https://downloads.example.test/wiresock.msi"),
                destination,
                new string('0', 64),
                CancellationToken.None,
                maxBytes: 64 * 1024,
                progress: new ImmediateProgress<DownloadProgress>(
                    progressEvents.Add)));

        Assert(handler.RequestCount == 2);
        Assert(!File.Exists(destination));
        Assert(!File.Exists(destination + ".download"));
        Assert(progressEvents.Any(item => item.IsRetry));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRejectsOversizedUnknownLengthAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("bu-dosya-beklenen-sinirdan-buyuk");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var uri = new Uri("https://downloads.example.test/wiresock.msi");
    var handler = new MapHttpMessageHandler();
    handler.AddResponse(uri, () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new UnknownLengthContent(payload)
    });
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(httpClient);
    var destination = Path.Combine(root, "wiresock.msi");

    try
    {
        await AssertThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(
                uri,
                destination,
                expectedSha256,
                CancellationToken.None,
                maxBytes: 8));

        Assert(!File.Exists(destination));
        Assert(!File.Exists(destination + ".download"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderReportsUnknownLengthCompletionAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("dogrulanmis-kurucu");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var uri = new Uri("https://downloads.example.test/wiresock.msi");
    var handler = new MapHttpMessageHandler();
    handler.AddResponse(uri, () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new UnknownLengthContent(payload)
    });
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(httpClient);
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await downloader.DownloadAsync(
            uri,
            destination,
            expectedSha256,
            CancellationToken.None,
            maxBytes: 64 * 1024,
            progress: new ImmediateProgress<DownloadProgress>(
                progressEvents.Add));

        var finalProgress = progressEvents.Last();
        Assert(finalProgress.BytesReceived == payload.Length);
        Assert(finalProgress.TotalBytes == payload.Length);
        Assert(finalProgress.Percent == 100);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRejectsTruncatedKnownLengthAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("eksik");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var uri = new Uri("https://downloads.example.test/wiresock.msi");
    var handler = new MapHttpMessageHandler();
    handler.AddResponse(uri, () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new DeclaredLengthContent(payload, declaredLength: payload.Length + 10)
    });
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(httpClient);
    var destination = Path.Combine(root, "wiresock.msi");

    try
    {
        var exception = await AssertThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(
                uri,
                destination,
                expectedSha256,
                CancellationToken.None,
                maxBytes: 64 * 1024));

        Assert(exception.Message.Contains(
            "beklenen boyuta",
            StringComparison.OrdinalIgnoreCase));
        Assert(!File.Exists(destination));
        Assert(!File.Exists(destination + ".download"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateSkipsCurrentReleaseAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var handler = new MapHttpMessageHandler();
        handler.AddJson(latestUri, """
            {
              "tag_name": "v2.0.12",
              "html_url": "https://github.com/ucsahinn/astral/releases/tag/v2.0.12",
              "assets": []
            }
            """);
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader([]);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri);

        var update = await service.PrepareLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            root,
            "Astral.exe",
            CancellationToken.None);

        Assert(update.Status == AppUpdatePreparationStatus.UpToDate);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckFindsReleaseWithoutDownloadAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(check.LatestVersion == new Version(2, 0, 14, 0));
        Assert(check.PackageUri == zipUri);
        Assert(check.ExpectedSha256 == expectedSha256);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckRetriesTransientMetadataFailureAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        var attempts = 0;
        handler.AddResponse(
            latestUri,
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        CreateReleaseJson(
                            "2.0.14",
                            zipUri,
                            checksumUri,
                            expectedSha256,
                            packageBytes.Length),
                        Encoding.UTF8,
                        "application/json")
                };
            });
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(attempts == 2);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckRetriesTransientMetadataBodyFailureAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        var attempts = 0;
        handler.AddResponse(
            latestUri,
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new FailingHttpContent(new IOException("temporary body failure"))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        CreateReleaseJson(
                            "2.0.14",
                            zipUri,
                            checksumUri,
                            expectedSha256,
                            packageBytes.Length),
                        Encoding.UTF8,
                        "application/json")
                };
            });
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(attempts == 2);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckRetriesTransientChecksumFailureAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        var checksumAttempts = 0;
        handler.AddResponse(
            checksumUri,
            () =>
            {
                checksumAttempts++;
                if (checksumAttempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{expectedSha256}  Astral-2.0.14-win-x64.zip",
                        Encoding.UTF8,
                        "text/plain")
                };
            });
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(checksumAttempts == 2);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdatePreparesVerifiedReleaseAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);
        var progressEvents = new List<AppUpdateProgress>();
        var progress = new ImmediateProgress<AppUpdateProgress>(
            progressEvents.Add);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);
        var update = await service.PrepareCheckedUpdateAsync(
            check,
            root,
            "Astral.exe",
            CancellationToken.None,
            progress);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(downloader.DownloadCount == 1);
        Assert(downloader.LastSource == zipUri);
        Assert(downloader.LastExpectedSha256 == expectedSha256);
        Assert(downloader.LastMaxBytes == packageBytes.Length);
        Assert(File.Exists(update.PackagePath));
        Assert(File.Exists(Path.Combine(update.PayloadDirectory!, "Astral.exe")));
        Assert(File.Exists(Path.Combine(
            update.PayloadDirectory!,
            UpdatePackageValidator.ManifestFileName)));
        Assert(File.Exists(update.ApplicatorPath));
        Assert(Path.GetFileName(update.ApplicatorPath!) == "Astral.Updater.exe");
        Assert(File.Exists(Path.Combine(
            Path.GetDirectoryName(update.ApplicatorPath!)!,
            "runtimes",
            "win",
            "native",
            "helper.dll")));
        Assert(update.ExpectedSignerThumbprint is null);
        Assert(progressEvents.Any(item => item.Message.Contains(
            "indiriliyor",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressEvents.Any(item => item.Percent >= 90));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateThrottlesDownloadProgressAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new FloodingVerifiedDownloader(packageBytes, reports: 5000);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);
        var progressEvents = new List<AppUpdateProgress>();
        var progress = new ImmediateProgress<AppUpdateProgress>(
            progressEvents.Add);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);
        var update = await service.PrepareCheckedUpdateAsync(
            check,
            root,
            "Astral.exe",
            CancellationToken.None,
            progress);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(downloader.ReportCount == 5000);
        Assert(progressEvents.Count < 80);
        Assert(progressEvents.Any(item => item.Message.Contains(
            "indiriliyor",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressEvents.Any(item => item.Percent >= 90));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdatePreservesDownloadRetryProgressAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new RetryProgressVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);
        var progressEvents = new List<AppUpdateProgress>();
        var progress = new ImmediateProgress<AppUpdateProgress>(
            progressEvents.Add);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);
        var update = await service.PrepareCheckedUpdateAsync(
            check,
            root,
            "Astral.exe",
            CancellationToken.None,
            progress);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(progressEvents.Any(item => item.Message.Contains(
            "tekrar",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressEvents.Any(item => item.Detail?.Contains(
            "Deneme 2/2",
            StringComparison.OrdinalIgnoreCase) == true));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateRejectsDigestMismatchAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson(
                "2.0.14",
                zipUri,
                checksumUri,
                new string('A', 64),
                packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            new CapturingVerifiedDownloader(packageBytes),
            latestUri,
            requireUpdateAuthenticode: false);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.CheckLatestUpdateAsync(
                new Version(2, 0, 12, 0),
                CancellationToken.None));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateDefaultsToGitHubVerifiedPackageAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri);
        await WriteUpdaterHelperAsync(root);

        var update = await service.PrepareLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            root,
            "Astral.exe",
            CancellationToken.None);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(update.ExpectedSignerThumbprint is null);
        Assert(downloader.LastExpectedSha256 == expectedSha256);
        Assert(File.Exists(update.PackagePath));
        Assert(File.Exists(Path.Combine(update.PayloadDirectory!, "Astral.exe")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateRejectsManifestVersionMismatchAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage(version: "2.0.15");
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Astral-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            new CapturingVerifiedDownloader(packageBytes),
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.PrepareLatestUpdateAsync(
                new Version(2, 0, 12, 0),
                root,
                "Astral.exe",
                CancellationToken.None));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateRejectsOversizedChecksumAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/astral/releases/download/v2.0.14/Astral-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            new string('A', 8193));
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            new CapturingVerifiedDownloader(packageBytes),
            latestUri,
            requireUpdateAuthenticode: false);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.CheckLatestUpdateAsync(
                new Version(2, 0, 12, 0),
                CancellationToken.None));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateRejectsUnsafeArchiveEntryAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var path = Path.Combine(root, "unsafe.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Astral.exe");
            archive.CreateEntry("../outside.txt");
        }

        AssertThrows<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateArchive(path, "Astral.exe"));
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateRejectsArchiveEntryOutsideManifestAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var path = Path.Combine(root, "extra.zip");
        File.WriteAllBytes(path, CreateUpdatePackage(extraEntryName: "unexpected.txt"));

        AssertThrows<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateArchive(
                path,
                "Astral.exe",
                expectedVersion: "2.0.14"));
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateExtractRejectsPackageHashMismatchAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var packagePath = Path.Combine(root, "Astral-2.0.14-win-x64.zip");
        File.WriteAllBytes(packagePath, CreateUpdatePackage());
        var destination = Path.Combine(root, "payload");

        AssertThrows<InvalidDataException>(
            () => UpdatePackageValidator.ExtractToDirectory(
                packagePath,
                destination,
                "Astral.exe",
                "2.0.14",
                expectedSha256: new string('0', 64)));

        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateStagingRejectsUnsafeVersionDirectoryAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        AssertThrows<ArgumentException>(
            () => ProtectedUpdateStaging.CreateVersionDirectory(
                Path.Combine(root, "updates"),
                "..\\2.0.14",
                restrictAccess: false));

        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateRequiresSignaturesForAllPortableBinariesAsync()
{
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("Astral.exe"));
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("Astral.Updater.dll"));
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("third-party/native-helper.exe"));
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("third-party/native-helper.dll"));
    Assert(!AuthenticodeSignatureVerifier.ShouldVerify("astral.update-manifest.json"));
    Assert(!AuthenticodeSignatureVerifier.ShouldVerify("README.md"));
    return Task.CompletedTask;
}

static async Task SettingsPersistConsentAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var paths = new AppPaths(root);
        var firstStore = new AppSettingsStore(paths);
        Assert(!firstStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!firstStore.IsBrowserAccessEnabled());
        Assert(!firstStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!firstStore.IsStartWithWindowsEnabled());
        Assert(!firstStore.IsDebugDiagnosticsEnabled());
        Assert(!firstStore.IsWireSockInstalledByAstral());

        firstStore.SetBrowserAccessEnabled(true);
        firstStore.SetRunInBackgroundOnCloseEnabled(true);
        firstStore.SetStartWithWindowsEnabled(true);
        firstStore.SetDebugDiagnosticsEnabled(true);
        firstStore.SetWireSockInstalledByAstral(true);
        firstStore.AcceptSetupConsent(WireSockPackage.Version);

        var reloadedStore = new AppSettingsStore(paths);
        Assert(reloadedStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!reloadedStore.IsSetupConsentAccepted("next-version"));
        Assert(reloadedStore.IsBrowserAccessEnabled());
        Assert(reloadedStore.IsRunInBackgroundOnCloseEnabled());
        Assert(reloadedStore.IsStartWithWindowsEnabled());
        Assert(reloadedStore.IsDebugDiagnosticsEnabled());
        Assert(reloadedStore.IsWireSockInstalledByAstral());

        reloadedStore.SetBrowserAccessEnabled(false);
        reloadedStore.SetRunInBackgroundOnCloseEnabled(false);
        reloadedStore.SetStartWithWindowsEnabled(false);
        reloadedStore.SetDebugDiagnosticsEnabled(false);
        reloadedStore.SetWireSockInstalledByAstral(false);
        var disabledStore = new AppSettingsStore(paths);
        Assert(!disabledStore.IsBrowserAccessEnabled());
        Assert(!disabledStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!disabledStore.IsStartWithWindowsEnabled());
        Assert(!disabledStore.IsDebugDiagnosticsEnabled());
        Assert(!disabledStore.IsWireSockInstalledByAstral());

        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "AcceptedWireSockVersion": "1.4.7.1",
              "AcceptedCloudflareWarpTerms": true
            }
            """);
        var legacyStore = new AppSettingsStore(paths);
        Assert(legacyStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!legacyStore.IsBrowserAccessEnabled());
        Assert(!legacyStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!legacyStore.IsStartWithWindowsEnabled());
        Assert(!legacyStore.IsDebugDiagnosticsEnabled());
        Assert(!legacyStore.IsWireSockInstalledByAstral());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BrowserAccessLegacyImplicitEnabledMigratesToOptInAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var paths = new AppPaths(root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "AcceptedWireSockVersion": "1.4.7.1",
              "AcceptedCloudflareWarpTerms": true,
              "BrowserAccessEnabled": true,
              "RunInBackgroundOnClose": true,
              "StartWithWindows": true
            }
            """);

        var legacyStore = new AppSettingsStore(paths);
        Assert(legacyStore.EnsureBrowserAccessPreferenceInitialized());
        Assert(!legacyStore.IsBrowserAccessEnabled());
        Assert(legacyStore.IsRunInBackgroundOnCloseEnabled());
        Assert(legacyStore.IsStartWithWindowsEnabled());

        var migratedStore = new AppSettingsStore(paths);
        Assert(!migratedStore.EnsureBrowserAccessPreferenceInitialized());
        Assert(!migratedStore.IsBrowserAccessEnabled());

        migratedStore.SetBrowserAccessEnabled(true);
        var explicitStore = new AppSettingsStore(paths);
        Assert(!explicitStore.EnsureBrowserAccessPreferenceInitialized());
        Assert(explicitStore.IsBrowserAccessEnabled());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapRequiresConsentAsync()
{
    var root = CreateTemporaryDirectory();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var launcher = new FakeInstallerLauncher();
    var bootstrapper = new WireSockBootstrapper(
        new AppPaths(root),
        new AppSettingsStore(new AppPaths(root)),
        new MutableWireSockLocator(),
        downloader,
        verifier,
        launcher);

    try
    {
        await AssertThrowsAsync<InvalidOperationException>(
            () => bootstrapper.EnsureInstalledAsync(null, CancellationToken.None));
        Assert(downloader.DownloadCount == 0);
        Assert(verifier.InstallerVerifyCount == 0);
        Assert(verifier.ClientVerifyCount == 0);
        Assert(launcher.LaunchCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapReusesTrustedInstallAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator
    {
        Path = Path.Combine(root, "WireSock", "wiresock-client.exe")
    };
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var launcher = new FakeInstallerLauncher();
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == locator.Path);
        Assert(!settings.IsWireSockInstalledByAstral());
        Assert(downloader.DownloadCount == 0);
        Assert(verifier.InstallerVerifyCount == 0);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapIgnoresUntrustedInstallAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var trustedPath = Path.Combine(root, "Trusted", "wiresock-client.exe");
    var locator = new MutableWireSockLocator
    {
        Path = Path.Combine(root, "Untrusted", "wiresock-client.exe")
    };
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier(path =>
        path.Contains("Untrusted", StringComparison.OrdinalIgnoreCase));
    var launcher = new FakeInstallerLauncher(() => locator.Path = trustedPath);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == trustedPath);
        Assert(downloader.DownloadCount == 1);
        Assert(verifier.InstallerVerifyCount == 2);
        Assert(verifier.ClientVerifyCount == 2);
        Assert(launcher.LaunchCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapLifecycleAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(() => locator.Path = installedPath);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == installedPath);
        Assert(downloader.DownloadCount == 1);
        Assert(downloader.LastMaxBytes == WireSockPackage.WindowsX64MaxBytes);
        Assert(verifier.InstallerVerifyCount == 2);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
        var launchedInstallerPath = launcher.LastInstallerPath
            ?? throw new InvalidOperationException("Kurucu yolu kaydedilmedi.");
        Assert(Path.GetFullPath(launchedInstallerPath).StartsWith(
            Path.GetFullPath(paths.WireSockInstallerStagingDirectory),
            StringComparison.OrdinalIgnoreCase));
        Assert(!File.Exists(launchedInstallerPath));
        Assert(settings.IsWireSockInstalledByAstral());
        Assert(File.Exists(paths.WireSockInstallMarker));
        Assert(!File.Exists(Path.Combine(
            paths.InstallerDirectory,
            WireSockPackage.InstallerFileName)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapUsesVerifiedLocalInstallerAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(() => locator.Path = installedPath);
    var localInstallerPath = Path.Combine(
        paths.InstallerDirectory,
        WireSockPackage.InstallerFileName);
    var hashVerifyCount = 0;
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher,
        (path, expectedSha256, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            hashVerifyCount++;
            Assert(expectedSha256 == WireSockPackage.WindowsX64Sha256);
            return Task.FromResult(string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(localInstallerPath),
                StringComparison.OrdinalIgnoreCase));
        });
    var progressMessages = new List<string>();

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(localInstallerPath, "yerel-kurucu");

        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            new ImmediateProgress<string>(progressMessages.Add),
            CancellationToken.None);

        Assert(result == installedPath);
        Assert(downloader.DownloadCount == 0);
        Assert(hashVerifyCount >= 1);
        Assert(verifier.InstallerVerifyCount == 3);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
        Assert(File.Exists(localInstallerPath));
        var launchedInstallerPath = launcher.LastInstallerPath
            ?? throw new InvalidOperationException("Kurucu yolu kaydedilmedi.");
        Assert(Path.GetFullPath(launchedInstallerPath).StartsWith(
            Path.GetFullPath(paths.WireSockInstallerStagingDirectory),
            StringComparison.OrdinalIgnoreCase));
        Assert(!File.Exists(launchedInstallerPath));
        Assert(progressMessages.Any(message => message.Contains(
            "yerel paketten",
            StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapAcceptsRestartRequiredExitCodeAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(
        () => locator.Path = installedPath,
        exitCode: 3010);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == installedPath);
        Assert(downloader.DownloadCount == 1);
        Assert(verifier.InstallerVerifyCount == 2);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapReportsDownloadProgressAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(() => locator.Path = installedPath);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);
    var progressMessages = new List<string>();

    try
    {
        bootstrapper.AcceptSetupConsent();
        await bootstrapper.EnsureInstalledAsync(
            new ImmediateProgress<string>(progressMessages.Add),
            CancellationToken.None);

        Assert(progressMessages.Any(message => message.Contains(
            "WireSock kurucusu",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressMessages.Any(message => message.Contains(
            "20 B / 20 B",
            StringComparison.OrdinalIgnoreCase)));
        Assert(downloader.LastMaxBytes == WireSockPackage.WindowsX64MaxBytes);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerLifecycleAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var processLauncher = new FakeProcessLauncher(process);
    var accessLock = new FakeDiscordAccessLock();
    var webProxyService = new FakeScopedWebProxyService();
    var profileProvisioner = new FakeProfileProvisioner(
        Path.Combine(root, "discord.conf"));
    var discordExecutable = Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe");
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [discordExecutable],
            [100]));
    var readinessProbe = new ObservingTunnelReadinessProbe(() =>
        discordManager.RestartCount > 0
            ? TunnelReadinessSnapshot.Ready(
                "Up",
                128,
                512,
                "wt0",
                "WireGuard Tunnel")
            : TunnelReadinessSnapshot.Ready(
                "Up",
                128,
                256,
                "wt0",
                "WireGuard Tunnel"));
    var chromePath = Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
    var edgePath = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
    var pathChromePath = Path.Combine(root, "PathBrowsers", "chrome.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(chromePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(edgePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(pathChromePath)!);
    File.WriteAllText(chromePath, "chrome");
    File.WriteAllText(edgePath, "edge");
    File.WriteAllText(pathChromePath, "chrome");
    var originalPath = Environment.GetEnvironmentVariable("PATH");
    Environment.SetEnvironmentVariable(
        "PATH",
        Path.GetDirectoryName(pathChromePath) + Path.PathSeparator + originalPath);
    var wireSockExecutable = Path.Combine(
        root,
        "WireSock VPN Client",
        "bin",
        WireSockPackage.CliExecutableFileName);
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(wireSockExecutable),
        profileProvisioner,
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        discordProcessManager: discordManager,
        tunnelReadinessProbe: readinessProbe,
        webProxyService: webProxyService)
    {
        IncludeBrowserAccess = true
    };

    try
    {
        await controller.EnsureDisconnectedLockAsync();
        Assert(accessLock.EnableCount == 1);
        Assert(webProxyService.ClearCount == 1);
        Assert(controller.Snapshot.Message.Contains("Bağlı Değil", StringComparison.Ordinal));

        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(discordManager.RestartCount == 1);
        Assert(discordManager.VerifyReadyCount == 1);
        Assert(accessLock.PrepareTunnelScopeCount == 1);
        Assert(accessLock.DisableCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 1);
        Assert(accessLock.LastIncludeBrowserAccess == false);
        Assert(webProxyService.ApplyCount == 1);
        Assert(webProxyService.LastPlan?.RequiresWebProxy == true);
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.EndsWith("Astral.WebProxy.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals(chromePath, StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals(edgePath, StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals(pathChromePath, StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Contains("gameclient", StringComparison.OrdinalIgnoreCase)));
        Assert(processLauncher.LastExecutable == wireSockExecutable);
        Assert(processLauncher.LastArguments.SequenceEqual(
            [
                "run",
                "-config",
                Path.Combine(root, "discord.conf"),
                "-log-level",
                "info"
            ],
            StringComparer.Ordinal));

        await controller.ConnectAsync();
        Assert(process.StopCount == 0);

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(accessLock.EnableCount == 2);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(webProxyService.ClearCount == 2);
        Assert(process.StopCount == 1);

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(accessLock.EnableCount == 3);
        Assert(accessLock.ClearTunnelScopeCount == 2);
        Assert(webProxyService.ClearCount == 3);
    }
    finally
    {
        Environment.SetEnvironmentVariable("PATH", originalPath);
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisconnectDoesNotReportProtectedWhenAccessLockRestoreFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var accessLock = new FakeDiscordAccessLock(
        enableException: new TimeoutException("Access lock restore timed out."));
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics);

    try
    {
        await controller.ConnectAsync();
        await controller.DisconnectAsync();

        Assert(process.StopCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.EnableCount == 1);
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "korumasi dogrulanamadi",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"koruma guncellenemedi\"", StringComparison.Ordinal));
        Assert(health.Contains("\"accessLock\":\"not-confirmed\"", StringComparison.Ordinal));
        Assert(!health.Contains("\"accessLock\":\"enabled\"", StringComparison.Ordinal));

        await ControllerDisconnectDoesNotReportCleanWhenWebProxyCleanupFailsAsync();
        await ControllerDisconnectDoesNotReportCleanWhenTunnelScopeCleanupFailsAsync();
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisconnectDoesNotReportCleanWhenWebProxyCleanupFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var webProxy = new FakeScopedWebProxyService
    {
        ClearException = new TimeoutException("Web proxy cleanup timed out.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        new FakeDiscordAccessLock(),
        diagnostics,
        webProxyService: webProxy);
    controller.TrySetTargetSelection(new TargetSelection([TargetIds.Wattpad]));

    try
    {
        await controller.ConnectAsync();
        await controller.DisconnectAsync();

        Assert(process.StopCount == 1);
        Assert(webProxy.ClearCount > 0);
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "temizligi dogrulanamadi",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"webProxy\":\"not-confirmed\"", StringComparison.Ordinal));
        Assert(health.Contains("\"accessLock\":\"enabled\"", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisconnectDoesNotReportCleanWhenTunnelScopeCleanupFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var accessLock = new FakeDiscordAccessLock(
        clearTunnelScopeException: new TimeoutException("Tunnel scope cleanup timed out."));
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics);
    controller.TrySetTargetSelection(new TargetSelection([TargetIds.Wattpad]));

    try
    {
        await controller.ConnectAsync();
        await controller.DisconnectAsync();

        Assert(process.StopCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.EnableCount == 1);
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "temizligi dogrulanamadi",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"tunnelScope\":\"not-confirmed\"", StringComparison.Ordinal));
        Assert(health.Contains("\"accessLock\":\"enabled\"", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDoesNotReportConnectedWhenScopedPacMissingAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var webProxyService = new FakeScopedWebProxyService
    {
        EnsureStatus = new ScopedWebProxyStatus(
            Required: true,
            IsApplied: false,
            Message: "Windows AutoConfigURL seçili PAC'a işaret etmiyor.")
    };
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        webProxyService: webProxyService)
    {
        IncludeBrowserAccess = true
    };

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(!controller.Snapshot.IsConnected);
        Assert(controller.Snapshot.Message.Contains(
            "web",
            StringComparison.OrdinalIgnoreCase));
        Assert(webProxyService.ApplyCount == 1);
        Assert(webProxyService.EnsureCount == 1);
        Assert(webProxyService.ClearCount == 1);
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerConnectsWebOnlyTargetWithoutDiscordProcessAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var webProxyService = new FakeScopedWebProxyService();
    var profileProvisioner = new FakeProfileProvisioner(
        Path.Combine(root, "wattpad.conf"));
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        profileProvisioner,
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        discordProcessManager: discordManager,
        webProxyService: webProxyService);

    try
    {
        Assert(controller.TrySetTargetSelection(new TargetSelection(
            [TargetIds.Wattpad])));

        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "seçili hedefler",
            StringComparison.OrdinalIgnoreCase));
        Assert(discordManager.RestartCount == 0);
        Assert(discordManager.VerifyReadyCount == 0);
        Assert(webProxyService.ApplyCount == 1);
        Assert(webProxyService.LastPlan?.Summary == "Wattpad");
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.EndsWith("Astral.WebProxy.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !RoutingPlan.IsBrowserExecutable(app)));

        await controller.DisconnectAsync();

        Assert(discordManager.CloseCount == 0);
        Assert(webProxyService.ClearCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeSkipsDisconnectedCleanupAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.EnsureDisconnectedLockAsync();
        await controller.DisposeAsync();

        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeRefreshesUnconfirmedDisconnectedLockAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.DisposeAsync();

        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeCleansActiveConnectionAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        await controller.DisposeAsync();

        Assert(process.StopCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeTreatsAccessLockTimeoutAsBestEffortAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var paths = new AppPaths(root);
    var accessLock = new FakeDiscordAccessLock(
        enableException: new TimeoutException("Access lock cleanup timed out."));
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics);

    try
    {
        await controller.ConnectAsync();
        await controller.DisposeAsync();

        Assert(process.StopCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.EnableCount == 1);
        var events = await File.ReadAllTextAsync(paths.EventLog);
        Assert(events.Contains("\"source\":\"controller.cleanup.timeout\"", StringComparison.Ordinal));
        Assert(events.Contains("\"phase\":\"access-lock\"", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerKeepsWireSockMarkerWhenExitIsUnconfirmedAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess(
        exitOnStop: false,
        exitOnDispose: false);
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics);

    try
    {
        await controller.ConnectAsync();
        Assert(File.Exists(paths.WireSockProcessMarker));

        await controller.DisposeAsync();

        Assert(File.Exists(paths.WireSockProcessMarker));
        var events = await File.ReadAllTextAsync(paths.EventLog);
        Assert(events.Contains(
            "WireSock kapanışı doğrulanamadı",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDeletesMismatchedWireSockMarkerAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock,
        new AstralDiagnostics(paths, TimeSpan.Zero));

    try
    {
        paths.EnsureDirectories();
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var marker = new
        {
            ProcessId = currentProcess.Id,
            StartTime = new DateTimeOffset(currentProcess.StartTime),
            ProfilePath = paths.DiscordProfile
        };
        await File.WriteAllTextAsync(
            paths.WireSockProcessMarker,
            JsonSerializer.Serialize(marker));

        await controller.EnsureDisconnectedLockAsync();

        Assert(!File.Exists(paths.WireSockProcessMarker));
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDoesNotCloseTargetProcessesOnDisconnectAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var targetProcessManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        targetProcessManager);

    try
    {
        await controller.ConnectAsync();
        await controller.DisconnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(process.StopCount == 1);
        Assert(targetProcessManager.RestartCount == 1);
        Assert(targetProcessManager.VerifyReadyCount == 1);
        Assert(targetProcessManager.CloseCount == 0);

        var details = controller.CreateDiagnosticDetails();
        Assert(!details.ContainsKey("discordRestartStatus"));
        Assert(!details.ContainsKey("discordProcessCount"));
        Assert(details["targetLaunchPolicy"] == "refreshed-running-discord");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerConnectsDefaultScopeWithoutLaunchingClosedTargetProcessAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var targetProcessManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            0,
            [],
            []));
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "Connection established"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        targetProcessManager,
        new FakeTunnelReadinessProbe(TunnelReadinessSnapshot.Ready("Up", 128, 256)),
        tunnelReadinessRetryDelay: TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.Message.Contains(
            "yenilenemedi",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "Discord açıldı",
            StringComparison.OrdinalIgnoreCase));
        Assert(targetProcessManager.RestartCount == 0);
        Assert(targetProcessManager.VerifyReadyCount == 0);

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"hedef için ek aksiyon gerekli\"", StringComparison.Ordinal));
        Assert(health.Contains("\"targetLaunchPolicy\":\"not-started-by-astral\"", StringComparison.Ordinal));
        Assert(health.Contains("\"targetProcessRefresh.runningProcessCount\":\"0\"", StringComparison.Ordinal));
        Assert(!health.Contains("discordProcessCount", StringComparison.OrdinalIgnoreCase));
        Assert(!health.Contains("discordRestartStatus", StringComparison.OrdinalIgnoreCase));
        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockRunning"] == "True");
        Assert(details["targetLaunchPolicy"] == "not-started-by-astral");
        Assert(details["nextAction"] == "Discord'u şimdi açabilirsiniz.");
        Assert(details["targetProcessRefresh.required"] == "True");
        Assert(details["targetProcessRefresh.refreshed"] == "False");
        Assert(!details.ContainsKey("discordProcessCount"));
        Assert(!details.ContainsKey("discordRestartStatus"));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRefreshesRunningDiscordBeforeWireSockReadinessAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var targetProcessManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessCaptureCount = 0;
    var readinessProbe = new ObservingTunnelReadinessProbe(() =>
    {
        readinessCaptureCount++;
        if (readinessCaptureCount > 1)
        {
            Assert(targetProcessManager.RestartCount == 1);
            Assert(targetProcessManager.VerifyReadyCount == 1);
        }

        return TunnelReadinessSnapshot.Ready(
            "Up",
            512,
            1024);
    });
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "Connection established"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        targetProcessManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(readinessProbe.CaptureCount >= 2);
        Assert(targetProcessManager.RestartCount == 1);
        Assert(targetProcessManager.VerifyReadyCount == 1);
        var details = controller.CreateDiagnosticDetails();
        Assert(details["targetLaunchPolicy"] == "refreshed-running-discord");
        Assert(details["targetProcessRefresh.refreshed"] == "True");
        Assert(details["nextAction"] == "Çalışan Discord yenilendi. Seçili hedefleri kullanabilirsiniz.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRequiresManualActionWhenRunningDiscordRefreshFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var targetProcessManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]),
        new DiscordRestartResult(
            false,
            "Discord yenilenemedi.",
            "Restart failed in test.",
            DiscordRestartFailureKind.Unknown));
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "Connection established"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        targetProcessManager,
        new FakeTunnelReadinessProbe(TunnelReadinessSnapshot.Ready("Up", 128, 256)),
        tunnelReadinessRetryDelay: TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(targetProcessManager.RestartCount == 1);
        Assert(targetProcessManager.VerifyReadyCount == 0);
        var details = controller.CreateDiagnosticDetails();
        Assert(details["targetLaunchPolicy"] == "manual-restart-required");
        Assert(details["targetProcessRefresh.manualActionRequired"] == "True");
        Assert(details["targetProcessRefresh.failureKind"] == "Unknown");
        Assert(details["nextAction"] ==
            "Discord'u kapatıp yeniden açın; ardından bağlantıyı tekrar kontrol edin.");

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"hedef için ek aksiyon gerekli\"", StringComparison.Ordinal));
        Assert(health.Contains("\"targetLaunchPolicy\":\"manual-restart-required\"", StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerConnectsEveryBuiltInPresetWithoutLaunchingTargetsAsync()
{
    var registry = TargetRegistry.CreateDefault();
    foreach (var target in registry.GetBuiltInTargets())
    {
        var root = CreateTemporaryDirectory();
        var process = new FakeManagedProcess();
        var paths = new AppPaths(root);
        var accessLock = new FakeDiscordAccessLock();
        var webProxyService = new FakeScopedWebProxyService();
        var profileProvisioner = new FakeProfileProvisioner(
            Path.Combine(root, $"{target.Id}.conf"));
        var targetProcessManager = new FakeDiscordProcessManager(
            new DiscordProcessSnapshot(
                0,
                [],
                []));
        var controller = new DiscordTunnelController(
            paths,
            new DiscordAppScope(root, root, root),
            new FakeWireSockBootstrapper(Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
            profileProvisioner,
            new FakeProcessLauncher(process, "Connection established"),
            TimeSpan.Zero,
            accessLock,
            new AstralDiagnostics(paths, TimeSpan.Zero),
            targetProcessManager,
            new FakeTunnelReadinessProbe(TunnelReadinessSnapshot.Ready("Up", 128, 256)),
            tunnelReadinessRetryDelay: TimeSpan.Zero,
            targetScopeResolver: null,
            webProxyService: webProxyService);

        try
        {
            Assert(controller.TrySetTargetSelection(new TargetSelection([target.Id])));

            await controller.ConnectAsync();

            if (target.Id.Equals(TargetIds.Discord, StringComparison.OrdinalIgnoreCase))
            {
                Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
            }
            else
            {
                Assert(controller.Snapshot.State == TunnelState.Connected);
            }
            Assert(targetProcessManager.RestartCount == 0);
            Assert(targetProcessManager.VerifyReadyCount == 0);
            Assert(targetProcessManager.CloseCount == 0);
            Assert(profileProvisioner.LastAllowedApplications.All(app =>
                !RoutingPlan.IsBrowserExecutable(app)));
            Assert(webProxyService.ApplyCount == 1);

            await controller.DisconnectAsync();

            Assert(targetProcessManager.CloseCount == 0);
        }
        finally
        {
            await controller.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ControllerClosesDiscordOnDisconnectAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();
        await controller.DisconnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(process.StopCount == 1);
        Assert(discordManager.RestartCount == 1);
        Assert(discordManager.CloseCount == 1);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["discordRestartStatus"] == "Discord kapatıldı.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerLocksTargetScopeWhileConnectedAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var targetProcessManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var profileProvisioner = new FakeProfileProvisioner(
        Path.Combine(root, "discord.conf"));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        profileProvisioner,
        new FakeProcessLauncher(new FakeManagedProcess(), "Connection established"),
        TimeSpan.Zero,
        accessLock,
        discordProcessManager: targetProcessManager);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(targetProcessManager.RestartCount == 1);
        Assert(accessLock.LastIncludeBrowserAccess == false);
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.EndsWith("Astral.WebProxy.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(!controller.TrySetTargetSelection(new TargetSelection(
            [TargetIds.Wattpad])));
        Assert(controller.TargetSelection.SelectedTargetIds.SequenceEqual([TargetIds.Discord]));

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(controller.TrySetTargetSelection(new TargetSelection(
            [TargetIds.Wattpad])));
        Assert(controller.IncludeBrowserAccess);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerVerifiesTunnelWithoutClaimingDiscordSessionAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            2,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100, 101]));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.Ordinal));
        Assert(!controller.Snapshot.Message.Contains(
            "Discord uygulaması bağlı",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Astral", "logs", "health.json"));
        Assert(health.Contains("\"status\":\"bağlantı hazır\"", StringComparison.Ordinal));
        Assert(health.Contains("\"discordProcessCount\":\"2\"", StringComparison.Ordinal));
        Assert(health.Contains("\"discordRestartStatus\":\"Discord yenilendi.\"", StringComparison.Ordinal));
        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockRunning"] == "True");
        Assert(details["discordProcessCount"] == "2");
        Assert(details["nextAction"] == "Discord yenilendi. Bağlantı hazır.");
        Assert(details["discordRestartStatus"] == "Discord yenilendi.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAcceptsInactiveWireSockAdapterInTransparentModeAsync()
{
    await ControllerAcceptsTransparentTunnelReadinessAsync(
        TunnelReadinessSnapshot.WireSockAdapterInactive(
            "Down",
            0,
            0,
            "WireSock Virtual Adapter is Down with no traffic."),
        "transparent-process-running",
        "Down",
        "Wireguard tunnel has been started.");
}

static async Task ControllerAcceptsMissingWireSockAdapterInTransparentModeAsync()
{
    await ControllerAcceptsTransparentTunnelReadinessAsync(
        TunnelReadinessSnapshot.WireSockAdapterNotDetected(),
        "transparent-process-running",
        wireSockLogLines: "Wireguard tunnel has been started.");
}

static async Task ControllerAcceptsUnavailableWireSockProbeInTransparentModeAsync()
{
    await ControllerAcceptsTransparentTunnelReadinessAsync(
        TunnelReadinessSnapshot.ProbeUnavailable(
            "WireSock adapter inventory is unavailable."),
        "transparent-process-running",
        wireSockLogLines: "Wireguard tunnel has been started.");
}

static async Task ControllerAcceptsTrafficProofWithoutWireSockHandshakeLogAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            256,
            "wt0",
            "WireGuard Tunnel"),
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            384,
            "wt0",
            "WireGuard Tunnel"));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.NotRequired(
            "Fake scoped web proxy proof disabled for traffic readiness test.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(!process.HasExited);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockMode"] == "transparent");
        Assert(details["wireSockConnectionEstablished"] == "False");
        Assert(details["wireSockTrafficDeltaObserved"] == "True");
        Assert(details["wireSockAdapterName"] == "wt0");
        Assert(details["wireSockAdapterDescription"] == "WireGuard Tunnel");

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"bağlantı hazır\"", StringComparison.Ordinal));
        Assert(health.Contains(
            "\"wireSockTrafficDeltaObserved\":\"True\"",
            StringComparison.Ordinal));
        Assert(health.Contains(
            "adapter traffic delta confirmed readiness",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAcceptsScopedWebProxyProofInTransparentModeAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.WireSockAdapterInactive(
            "Down",
            0,
            0,
            "WireSock Virtual Adapter is Down with no traffic."));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.Verified(
            "wattpad.com",
            18088,
            "Fake scoped web proxy target proof succeeded.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);
    controller.TrySetTargetSelection(new TargetSelection([TargetIds.Wattpad]));

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(!process.HasExited);
        Assert(webProxy.VerifyTargetAccessCount == 1);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockMode"] == "transparent");
        Assert(details["wireSockConnectionEstablished"] == "False");
        Assert(details["wireSockAdapterStatus"] == "Down");
        Assert(details["webProxyProof.verified"] == "True");
        Assert(details["webProxyProof.host"] == "wattpad.com");
        Assert(details["tunnelReadiness"] == "transparent-process-running");
        Assert(details["wireSockHandshakeDiagnostic"]!.Contains(
            "scoped web proxy target proof",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\"", StringComparison.Ordinal));
        Assert(health.Contains(
            "\"webProxyProof.verified\":\"True\"",
            StringComparison.Ordinal));
        Assert(health.Contains(
            "scoped web proxy target proof confirmed readiness",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRejectsAppWebTargetWhenOnlyScopedWebProxyProofExistsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.WireSockAdapterInactive(
            "Down",
            0,
            0,
            "WireSock Virtual Adapter is Down with no app traffic yet."));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.Verified(
            "discord.com",
            18088,
            "Fake scoped web proxy target proof succeeded.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);
    controller.TrySetTargetSelection(new TargetSelection([TargetIds.Discord]));

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(!controller.Snapshot.IsConnected);
        Assert(controller.Snapshot.Message.Contains(
            "uygulama tünel kanıtı",
            StringComparison.OrdinalIgnoreCase));
        Assert(process.HasExited);
        Assert(webProxy.VerifyTargetAccessCount > 0);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockMode"] == "transparent");
        Assert(details["wireSockConnectionEstablished"] == "False");
        Assert(details["webProxyProof.verified"] == "True");
        Assert(details["tunnelReadiness"] == "wiresock-adapter-inactive");
        Assert(details["wireSockHandshakeDiagnostic"]!.Contains(
            "uygulama",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"hata\"", StringComparison.Ordinal));
        Assert(!health.Contains("\"state\":\"Connected\"", StringComparison.Ordinal));
        Assert(health.Contains(
            "uygulama",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRefreshesRunningDiscordBeforeAppTunnelProofAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new ObservingTunnelReadinessProbe(() =>
        discordManager.RestartCount > 0
            ? TunnelReadinessSnapshot.Ready(
                "Up",
                128,
                512,
                "wt0",
                "WireGuard Tunnel")
            : TunnelReadinessSnapshot.Ready(
                "Up",
                128,
                256,
                "wt0",
                "WireGuard Tunnel"));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.Verified(
            "discord.com",
            18088,
            "Fake scoped web proxy target proof succeeded.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);
    controller.TrySetTargetSelection(new TargetSelection([TargetIds.Discord]));

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(discordManager.RestartCount == 1);
        Assert(discordManager.VerifyReadyCount == 1);
        Assert(!process.HasExited);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["targetLaunchPolicy"] == "refreshed-running-discord");
        Assert(details["targetProcessRefresh.refreshed"] == "True");
        Assert(details["webProxyProof.verified"] == "True");
        Assert(details["applicationTunnelProofRequired"] == "True");
        Assert(details["wireSockTrafficDeltaObserved"] == "True");
        Assert(details["wireSockTrafficDeltaBytesSent"] == "256");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAcceptsAppWebTargetWithTrafficProofAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            256,
            "wt0",
            "WireGuard Tunnel"),
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            512,
            "wt0",
            "WireGuard Tunnel"),
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            768,
            "wt0",
            "WireGuard Tunnel"));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.Verified(
            "discord.com",
            18088,
            "Fake scoped web proxy target proof succeeded.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);
    controller.TrySetTargetSelection(new TargetSelection([TargetIds.Discord]));

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(!process.HasExited);
        Assert(webProxy.VerifyTargetAccessCount == 1);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockMode"] == "transparent");
        Assert(details["wireSockConnectionEstablished"] == "False");
        Assert(details["wireSockTrafficDeltaObserved"] == "True");
        Assert(details["wireSockTrafficDeltaBytesSent"] == "256");
        Assert(details["applicationTunnelProofRequired"] == "True");
        Assert(details["webProxyProof.verified"] == "True");
        Assert(details["wireSockHandshakeDiagnostic"]!.Contains(
            "adapter traffic plus scoped web proxy proof",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"bağlantı hazır\"", StringComparison.Ordinal));
        Assert(health.Contains(
            "\"wireSockTrafficDeltaObserved\":\"True\"",
            StringComparison.Ordinal));
        Assert(health.Contains(
            "\"applicationTunnelProofRequired\":\"True\"",
            StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRejectsMissingWireSockHandshakeInTransparentModeAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            256));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.NotRequired(
            "Fake scoped web proxy proof disabled for this readiness test.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(process.HasExited);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockMode"] == "transparent");
        Assert(details["wireSockConnectionEstablished"] == "False");

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"hata\"", StringComparison.Ordinal));
        Assert(health.Contains(
            "WireSock bağlantı kanıtı doğrulanamadı",
            StringComparison.Ordinal));
        Assert(health.Contains(
            "\"wireSockConnectionEstablished\":\"False\"",
            StringComparison.Ordinal));
        Assert(health.Contains(
            "\"wireSockTrafficDeltaObserved\":\"False\"",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerReportsWireSockExitAfterFinalReadinessProbeAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new ExitAfterHasExitedChecksManagedProcess(
        exitAfterChecks: 64,
        exitCode: -1);
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.WireSockAdapterInactive(
            "Down",
            0,
            0,
            "WireSock Virtual Adapter status is Down."));
    var webProxy = new FakeScopedWebProxyService
    {
        Proof = ScopedWebProxyProof.NotRequired(
            "Fake scoped web proxy proof disabled for exit readiness test.")
    };
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "WireSock started"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        new NullDiscordProcessManager(),
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero,
        webProxyService: webProxy);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "bağlantı doğrulanmadan kapandı",
            StringComparison.OrdinalIgnoreCase));

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockProcessExited"] == "True");
        Assert(details["wireSockProcessExitCode"] == "-1");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAcceptsWireSockTunnelStartedLogAsync()
{
    await ControllerAcceptsTransparentTunnelReadinessAsync(
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            256),
        "ready",
        "Up",
        "Wireguard tunnel has been started.");
}

static async Task ControllerRetriesDelayedWireSockAdapterAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        TunnelReadinessSnapshot.WireSockAdapterInactive(
            "Down",
            0,
            0,
            "WireSock adapter is still starting."),
        TunnelReadinessSnapshot.Ready(
            "Up",
            128,
            256));
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "Connection established"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(readinessProbe.CaptureCount >= 2);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["tunnelReadiness"] == "ready");
        Assert(details["wireSockAdapterStatus"] == "Up");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerChecksWireSockAfterDiscordRestartAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new ObservingTunnelReadinessProbe(() =>
    {
        Assert(discordManager.RestartCount > 0);
        return TunnelReadinessSnapshot.Ready(
            "Up",
            512,
            1024);
    });
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process, "Connection established"),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(readinessProbe.CaptureCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAcceptsTransparentTunnelReadinessAsync(
    TunnelReadinessSnapshot readinessSnapshot,
    string expectedStatus,
    string? expectedAdapterStatus = null,
    params string[] wireSockLogLines)
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var readinessProbe = new FakeTunnelReadinessProbe(
        readinessSnapshot);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        wireSockLogLines.Length == 0
            ? new FakeProcessLauncher(process)
            : new FakeProcessLauncher(process, wireSockLogLines),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager,
        readinessProbe,
        tunnelReadinessRetryDelay: TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(!process.HasExited);
        Assert(accessLock.ClearTunnelScopeCount == 0);
        Assert(accessLock.EnableCount == 0);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["tunnelReadiness"] == expectedStatus);
        Assert(details["wireSockMode"] == "transparent");
        Assert(details["wireSockConnectionEstablished"] == "True");
        Assert(!string.IsNullOrWhiteSpace(details["wireSockProcessId"]));
        Assert(details["wireSockProcessExited"] == "False");
        if (expectedAdapterStatus is not null)
        {
            Assert(details["wireSockAdapterStatus"] == expectedAdapterStatus);
        }

        var health = await File.ReadAllTextAsync(paths.HealthReport);
        Assert(health.Contains("\"status\":\"bağlantı hazır\"", StringComparison.Ordinal));
        Assert(health.Contains(
            $"\"tunnelReadiness\":\"{expectedStatus}\"",
            StringComparison.Ordinal));
        Assert(health.Contains(
            "\"wireSockProcessExited\":\"False\"",
            StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static Task WindowsDiscordLaunchPlanUsesAppExecutableAsync()
{
    var root = Path.Combine(
        CreateTemporaryDirectory(),
        "Discord");
    var executablePath = Path.Combine(
        root,
        "app-1.0.9999",
        "Discord.exe");

    try
    {
        var plan = WindowsDiscordProcessInspector.CreateLaunchPlanForTesting(
            [executablePath]);

        Assert(plan.Count == 1);
        Assert(plan[0].Contains(
            Path.Combine("app-1.0.9999", "Discord.exe"),
            StringComparison.OrdinalIgnoreCase));
        Assert(!plan[0].Contains(
            "Update.exe",
            StringComparison.OrdinalIgnoreCase));
        Assert(!plan[0].Contains(
            "--processStart",
            StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
    finally
    {
        var testRoot = Directory.GetParent(root)?.FullName;
        if (!string.IsNullOrWhiteSpace(testRoot)
            && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}

static Task WindowsDiscordLaunchPlanCanUseUpdaterFallbackAsync()
{
    var root = Path.Combine(
        CreateTemporaryDirectory(),
        "Discord");

    try
    {
        var plan = WindowsDiscordProcessInspector.CreateUpdaterLaunchPlanForTesting(
            root,
            "Discord.exe",
            "Discord");

        Assert(plan.Count == 1);
        Assert(plan[0].Contains(
            "Update.exe",
            StringComparison.OrdinalIgnoreCase));
        Assert(plan[0].Contains(
            "--processStart Discord.exe",
            StringComparison.OrdinalIgnoreCase));
        Assert(!plan[0].Contains(
            Path.Combine("app-1.0.9999", "Discord.exe"),
            StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
    finally
    {
        var testRoot = Directory.GetParent(root)?.FullName;
        if (!string.IsNullOrWhiteSpace(testRoot)
            && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}

static Task WindowsDiscordBackgroundProcessesCountAsReadyAsync()
{
    Assert(WindowsDiscordProcessInspector.ResolveVisibleDiscordWindowResultForTesting(
        updaterWindowSeen: false,
        trustedProcessSeen: true,
        maxTrustedProcessCount: 1) == "TrustedBackgroundProcess");
    Assert(WindowsDiscordProcessInspector.ResolveVisibleDiscordWindowResultForTesting(
        updaterWindowSeen: true,
        trustedProcessSeen: true,
        maxTrustedProcessCount: 5) == "TrustedBackgroundProcess");
    Assert(WindowsDiscordProcessInspector.ResolveVisibleDiscordWindowResultForTesting(
        updaterWindowSeen: true,
        trustedProcessSeen: true,
        maxTrustedProcessCount: 1) == "UpdaterWindow");
    Assert(WindowsDiscordProcessInspector.ResolveVisibleDiscordWindowResultForTesting(
        updaterWindowSeen: false,
        trustedProcessSeen: false,
        maxTrustedProcessCount: 0) == "None");

    return Task.CompletedTask;
}

static async Task ControllerOpensDiscordWhenItIsClosedAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(0, []),
        new DiscordRestartResult(true, "Discord açıldı."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message == "Discord açıldı. Bağlantı hazır");
        Assert(discordManager.RestartCount == 1);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["discordRestartStatus"] == "Discord açıldı.");
        Assert(details["nextAction"] == "Discord açıldı. Bağlantı hazır.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerKeepsTunnelActiveWhenDiscordRestartFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]),
        new DiscordRestartResult(
            false,
            "Discord otomatik yenilenemedi.",
            "access denied"));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.IsConnected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord otomatik yenilenemedi.",
            StringComparison.Ordinal));
        Assert(discordManager.RestartCount == 1);

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Astral", "logs", "health.json"));
        Assert(health.Contains("\"status\":\"hedef için ek aksiyon gerekli\"", StringComparison.Ordinal));
        Assert(health.Contains("\"discordRestartStatus\":\"Discord otomatik yenilenemedi.\"", StringComparison.Ordinal));

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(process.StopCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRequiresManualActionWhenDiscordWindowIsHiddenAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No visible Discord main window was detected.",
            DiscordRestartFailureKind.WindowNotVisible))
    {
        VerifyResult = new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No visible Discord main window was detected.",
            DiscordRestartFailureKind.WindowNotVisible)
    };
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        diagnostics: diagnostics,
        discordProcessManager: discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.Message.Contains(
            "pencere görünmedi",
            StringComparison.OrdinalIgnoreCase));
        Assert(discordManager.RestartCount == 1);
        Assert(discordManager.VerifyReadyCount == 2);

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Astral", "logs", "health.json"));
        Assert(health.Contains("Discord açıldı ama pencere görünmedi", StringComparison.Ordinal));
        Assert(health.Contains("\"status\":\"hedef için ek aksiyon gerekli\"", StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerVerifiesDiscordWhenWindowAppearsLateAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new SequencedDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]),
        new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No visible Discord main window was detected.",
            DiscordRestartFailureKind.WindowNotVisible))
    {
        VerifyResult = new DiscordRestartResult(true, "Discord güncellendi.")
    };
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics: diagnostics,
        discordProcessManager: discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.OrdinalIgnoreCase));
        Assert(discordManager.RestartCount == 1);
        Assert(discordManager.VerifyReadyCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 0);
        Assert(accessLock.ApplyTunnelScopeCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRecoversDiscordUpdaterLoopAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var secondProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(true, "Discord açıldı."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 2);
        Assert(firstProcess.HasExited);
        Assert(!secondProcess.HasExited);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(discordManager.RestartCount == 2);

        var events = await File.ReadAllTextAsync(
            Path.Combine(root, "Astral", "logs", "events.jsonl"));
        Assert(events.Contains("controller.discordUpdaterRecovery", StringComparison.Ordinal));

        var details = controller.CreateDiagnosticDetails();
        Assert(details["discordRestartStatus"] == "Discord güncellendi.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRequiresManualActionWhenPostRecoveryVerificationFailsAsync()
{
    var cases = new[]
    {
        new DiscordRestartResult(
            false,
            "Discord kapanmış. Discord'u açıp tekrar deneyin.",
            "No trusted Discord process was running after tunnel resume.",
            DiscordRestartFailureKind.Unknown),
        new DiscordRestartResult(
            false,
            "Discord güncellendi ama ana pencere hazır olmadı. Discord'u kapatıp tekrar deneyin.",
            "Only a trusted Discord updater window was detected after tunnel resume.",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No trusted visible Discord main window was detected after tunnel resume.",
            DiscordRestartFailureKind.WindowNotVisible),
        new DiscordRestartResult(
            false,
            "Discord yolu doğrulanamadı.",
            "No trusted Discord executable path was available after tunnel resume.",
            DiscordRestartFailureKind.Unknown)
    };

    foreach (var verifyResult in cases)
    {
        var root = CreateTemporaryDirectory();
        var firstProcess = new FakeManagedProcess();
        var secondProcess = new FakeManagedProcess();
        var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
        var accessLock = new FakeDiscordAccessLock();
        var diagnostics = new AstralDiagnostics(
            new AppPaths(root),
            TimeSpan.Zero);
        var snapshot = new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]);
        var discordManager = new SequencedDiscordProcessManager(
            snapshot,
            new DiscordRestartResult(
                false,
                "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
                "updater window",
                DiscordRestartFailureKind.UpdaterWindow),
            new DiscordRestartResult(true, "Discord açıldı."))
        {
            VerifyResult = verifyResult
        };
        var controller = new DiscordTunnelController(
            new AppPaths(root),
            new DiscordAppScope(root, root, root),
            new FakeWireSockBootstrapper(Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
            new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
            processLauncher,
            TimeSpan.Zero,
            accessLock,
            diagnostics,
            discordManager);

        try
        {
            await controller.ConnectAsync();

            Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
            Assert(controller.Snapshot.Message == verifyResult.Message);
            Assert(processLauncher.StartCount == 2);
            Assert(firstProcess.HasExited);
            Assert(!secondProcess.HasExited);
            Assert(accessLock.ClearTunnelScopeCount == 1);
            Assert(accessLock.ApplyTunnelScopeCount == 2);
            Assert(discordManager.RestartCount == 2);
            var expectedVerifyReadyCount =
                verifyResult.FailureKind is DiscordRestartFailureKind.UpdaterWindow
                    or DiscordRestartFailureKind.WindowNotVisible
                    ? 2
                    : 1;
            Assert(discordManager.VerifyReadyCount == expectedVerifyReadyCount);

            var details = controller.CreateDiagnosticDetails();
            Assert(details["discordRestartStatus"] == verifyResult.Message);
        }
        finally
        {
            await controller.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ControllerVerifiesDiscordAfterUpdaterDirectRetryStillShowsUpdaterAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var secondProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "initial updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "direct retry still saw updater",
            DiscordRestartFailureKind.UpdaterWindow))
    {
        VerifyResult = new DiscordRestartResult(true, "Discord güncellendi.")
    };
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 2);
        Assert(firstProcess.HasExited);
        Assert(!secondProcess.HasExited);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(discordManager.RestartCount == 2);
        Assert(discordManager.VerifyReadyCount == 1);

        var events = await File.ReadAllTextAsync(
            Path.Combine(root, "Astral", "logs", "events.jsonl"));
        Assert(events.Contains(
            "Discord updater sonrasında tünel üstünden tekrar doğrulandı.",
            StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerKeepsTunnelActiveWhenDiscordUpdaterDirectRestartFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var secondProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(
            false,
            "Discord update ekranında kaldı.",
            "direct retry failed",
            DiscordRestartFailureKind.UpdaterWindow))
    {
        VerifyResult = new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı.",
            "post-resume verify still saw updater",
            DiscordRestartFailureKind.UpdaterWindow)
    };
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.Message.Contains(
            "Discord güncelleme bağlantısı tamamlanamadı",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 2);
        Assert(firstProcess.HasExited);
        Assert(!secondProcess.HasExited);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(discordManager.RestartCount == 2);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockRunning"] == "True");
        Assert((details["discordRestartStatus"] ?? string.Empty).Contains(
            "Discord güncelleme bağlantısı tamamlanamadı",
            StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRestoresLockWhenDiscordUpdaterRecoveryFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess);
    var accessLock = new FakeDiscordAccessLock(
        applyTunnelScopeException: new InvalidOperationException("Tunnel scope failed."),
        applyTunnelScopeFailureAttempt: 2);
    var diagnostics = new AstralDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(true, "Discord açıldı."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "Tunnel scope failed",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 1);
        Assert(firstProcess.HasExited);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(accessLock.ClearTunnelScopeCount >= 2);
        Assert(accessLock.EnableCount == 1);

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Astral", "logs", "health.json"));
        Assert(health.Contains("\"operation\":\"connect\"", StringComparison.Ordinal));
        Assert(health.Contains("Tunnel scope failed", StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerBootstrapFailureAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            new InvalidOperationException(
                "WireSock VPN Client kurulamadı.")),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "WireSock VPN Client",
            StringComparison.Ordinal));
        Assert(accessLock.DisableCount == 1);
        Assert(accessLock.EnableCount == 1);
        Assert(File.Exists(new AppPaths(root).ErrorLog));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerNetworkFailureIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(
            Path.Combine(root, "discord.conf"),
            new HttpRequestException(
                "No such host is known. (github.com:443)",
                new SocketException((int)SocketError.HostNotFound))),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "DNS",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "No such host",
            StringComparison.OrdinalIgnoreCase));
        Assert(accessLock.EnableCount == 1);
        Assert(File.Exists(paths.ErrorLog));
        Assert(File.ReadAllText(paths.ErrorLog).Contains(
            "No such host",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDownloadTimeoutIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(
            Path.Combine(root, "discord.conf"),
            new TaskCanceledException(
                "The operation was canceled.",
                new TimeoutException(
                    "A connection could not be established within the configured ConnectTimeout."))),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "zaman aşımına",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "operation was canceled",
            StringComparison.OrdinalIgnoreCase));
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDirectDownloadTimeoutIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(
            Path.Combine(root, "discord.conf"),
            new TimeoutException(
                "İndirme sırasında veri akışı zaman aşımına uğradı.")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "zaman aşımına",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "veri akışı",
            StringComparison.OrdinalIgnoreCase));
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAccessLockFailureIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock(
        disableException: new InvalidOperationException(
            "Hedef VPN kilidi güncellenemedi (ApplyTunnelScope): Set-Content : Akış okunabilir değildi."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "Hedef bağlantı koruması güncellenemedi",
            StringComparison.OrdinalIgnoreCase));
        Assert(controller.Snapshot.Message.Contains(
            "yönetici",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "Set-Content",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallAccessLockLabelsSilentPowerShellFailureAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner(
        new CommandResult(
            1,
            "astral-phase=FirewallRule:Astral.BlockTargetDomains",
            string.Empty));
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");

    try
    {
        var exception = await AssertThrowsAsync<InvalidOperationException>(
            () => accessLock.ApplyTunnelScopeAsync(
                includeBrowserAccess: false,
                CancellationToken.None));

        Assert(exception.Message.Contains(
            "ApplyTunnelScope",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "PowerShell exit code 1",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "FirewallRule:Astral.BlockTargetDomains",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallAccessLockUsesSelectedTargetDomainsAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner();
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");
    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        Path.Combine(root, "Astral.WebProxy.exe"));
    var plan = resolver.Resolve(new TargetSelection(
        [TargetIds.Wattpad, TargetIds.Blogspot]));

    try
    {
        await accessLock.EnableAsync(plan, CancellationToken.None);

        Assert(runner.Commands.Count == 1);
        Assert(runner.Commands[0].Contains(
            "'wattpad.com'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'blogspot.com'",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "'*.blogspot.com'",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "'discord.com'",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallTunnelScopeUsesSelectedTargetDomainsAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner();
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");
    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        Path.Combine(root, "Astral.WebProxy.exe"));
    var plan = resolver.Resolve(new TargetSelection(
        [TargetIds.Wattpad, TargetIds.Blogspot]));

    try
    {
        await accessLock.ApplyTunnelScopeAsync(plan, CancellationToken.None);

        Assert(runner.Commands.Count == 1);
        Assert(runner.Commands[0].Contains(
            "$includeBrowserAccess = $false",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'wattpad.com'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'blogspot.com'",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "'*.blogspot.com'",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "'discord.com'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-Program $program",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-RemoteAddress $addressList",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallPrepareTunnelScopeUsesSingleCommandAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner();
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");
    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        Path.Combine(root, "Astral.WebProxy.exe"));
    var plan = resolver.Resolve(new TargetSelection(
        [TargetIds.Wattpad, TargetIds.Blogspot]));

    try
    {
        await accessLock.PrepareTunnelScopeAsync(plan, CancellationToken.None);

        Assert(runner.Commands.Count == 1);
        var command = runner.Commands[0];
        var disableIndex = command.IndexOf(
            "PrepareTunnelScope:disable-domain-lock",
            StringComparison.Ordinal);
        var scopeIndex = command.IndexOf(
            "PrepareTunnelScope:clear-browser-scope",
            StringComparison.Ordinal);
        Assert(disableIndex >= 0);
        Assert(scopeIndex > disableIndex);
        Assert(command.Contains(
            "'wattpad.com'",
            StringComparison.Ordinal));
        Assert(command.Contains(
            "'blogspot.com'",
            StringComparison.Ordinal));
        Assert(!command.Contains(
            "'*.blogspot.com'",
            StringComparison.Ordinal));
        Assert(!command.Contains(
            "'discord.com'",
            StringComparison.Ordinal));
        Assert(command.Contains(
            WindowsFirewallDiscordAccessLock.RuleName,
            StringComparison.Ordinal));
        Assert(command.Contains(
            WindowsFirewallDiscordAccessLock.BrowserScopeGroup,
            StringComparison.Ordinal));
        Assert(command.Contains(
            "-Enabled False",
            StringComparison.Ordinal));
        Assert(command.Contains(
            "-Program $program",
            StringComparison.Ordinal));
        Assert(command.Contains(
            "-RemoteAddress $addressList",
            StringComparison.Ordinal));
        Assert(command.Contains(
            "direct browser bypass protection was not installed",
            StringComparison.Ordinal));
        Assert(!command.Contains(
            "Get-Command chrome",
            StringComparison.OrdinalIgnoreCase));
        Assert(!command.Contains(
            "HKCU:\\",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallPrepareTunnelScopeLabelsSilentPowerShellFailureAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner(
        new CommandResult(
            1,
            "astral-phase=PrepareTunnelScope:disable-domain-lock",
            string.Empty));
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");
    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        Path.Combine(root, "Astral.WebProxy.exe"));
    var plan = resolver.Resolve(new TargetSelection([TargetIds.Wattpad]));

    try
    {
        var exception = await AssertThrowsAsync<InvalidOperationException>(
            () => accessLock.PrepareTunnelScopeAsync(
                plan,
                CancellationToken.None));

        Assert(exception.Message.Contains(
            "PrepareTunnelScope",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "PowerShell exit code 1",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "PrepareTunnelScope:disable-domain-lock",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallAccessLockBuildsExpectedCommandsAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner();
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");
    var legacyProductName = string.Concat("Dis", "corder");
    var previousAstralRuleName = "Astral.BlockDiscordDomains";
    var legacyRuleName = legacyProductName + ".BlockDiscordDomains";
    var legacyBrowserScopeGroup = legacyProductName + ".TunnelScope.Browsers";
    var previousBeginMarker = "# BEGIN Astral Discord kilidi";
    var previousEndMarker = "# END Astral Discord kilidi";
    var legacyBeginMarker = "# BEGIN " + legacyProductName + " Discord kilidi";
    var legacyEndMarker = "# END " + legacyProductName + " Discord kilidi";

    try
    {
        await accessLock.EnableAsync(CancellationToken.None);
        await accessLock.DisableAsync(CancellationToken.None);
        await accessLock.ApplyTunnelScopeAsync(
            includeBrowserAccess: false,
            CancellationToken.None);
        await accessLock.ApplyTunnelScopeAsync(
            includeBrowserAccess: true,
            CancellationToken.None);
        await accessLock.ClearTunnelScopeAsync(CancellationToken.None);
        await accessLock.RemoveAsync(CancellationToken.None);

        Assert(runner.Commands.Count == 6);
        Assert(runner.Timeouts.All(timeout => timeout >= TimeSpan.FromSeconds(60)));
        Assert(runner.Commands[0].Contains(
            "System32\\drivers\\etc\\hosts",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "# BEGIN Astral hedef kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            previousAstralRuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            legacyRuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            legacyBrowserScopeGroup,
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            previousBeginMarker,
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            legacyBeginMarker,
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "0.0.0.0 ' + $domain",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "::1 ' + $domain",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "Resolve-DnsName",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-QuickTimeout",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'gateway.discord.gg'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'ptb.discord.com'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'updates.discord.com'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-RemoteAddress $addressList",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-DisplayName $displayName",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-Enabled True",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "[IO.File]::WriteAllText",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "Set-Content -LiteralPath $hostsPath",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "Add-Content -LiteralPath $hostsPath",
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            WindowsFirewallDiscordAccessLock.RuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            "# END Astral hedef kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            previousEndMarker,
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            legacyRuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            legacyEndMarker,
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            "-Enabled False",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            WindowsFirewallDiscordAccessLock.BrowserScopeGroup,
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            legacyBrowserScopeGroup,
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "Get-AstralBrowserPrograms",
            StringComparison.Ordinal));
        Assert(!runner.Commands[2].Contains(
            "Get-Process -Name $processName",
            StringComparison.Ordinal));
        Assert(!runner.Commands[2].Contains(
            "Get-Command chrome",
            StringComparison.OrdinalIgnoreCase));
        Assert(!runner.Commands[2].Contains(
            "HKCU:\\",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "CurrentVersion\\App Paths",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "chromium.exe",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "$allCandidates",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "-Program $program",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "-RemoteAddress $addressList",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "-QuickTimeout",
            StringComparison.Ordinal));
        Assert(runner.Commands[3].Contains(
            "$includeBrowserAccess = $true",
            StringComparison.Ordinal));
        Assert(runner.Commands[3].Contains(
            "if ($includeBrowserAccess) { return }",
            StringComparison.Ordinal));
        Assert(runner.Commands[4].Contains(
            "Clear-AstralFirewallGroup",
            StringComparison.Ordinal));
        Assert(!runner.Commands[4].Contains(
            "System32\\drivers\\etc\\hosts",
            StringComparison.Ordinal));
        Assert(!runner.Commands[4].Contains(
            "# BEGIN Astral Discord kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            WindowsFirewallDiscordAccessLock.RuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            legacyRuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            "# END Astral hedef kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            previousEndMarker,
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            "Remove-NetFirewallRule",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallAccessLockIgnoresDnsFlushExitCodeAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner();
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");

    try
    {
        await accessLock.EnableAsync(CancellationToken.None);
        await accessLock.DisableAsync(CancellationToken.None);
        await accessLock.RemoveAsync(CancellationToken.None);

        Assert(runner.Commands.Count == 3);
        Assert(runner.Commands.All(command => command.Contains(
            "Invoke-AstralFlushDns",
            StringComparison.Ordinal)));
        Assert(runner.Commands.All(command => !command.Contains(
            "ipconfig /flushdns | Out-Null",
            StringComparison.Ordinal)));
        Assert(runner.Commands.All(command => command.Contains(
            "$global:LASTEXITCODE = 0",
            StringComparison.Ordinal)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TunnelReadinessRecognizesWireSockWireGuardAdapterAsync()
{
    Assert(WindowsTunnelReadinessProbe.IsWireSockCompatibleAdapter(
        "wt0",
        "WireGuard Tunnel"));
    Assert(WindowsTunnelReadinessProbe.IsWireSockCompatibleAdapter(
        "WireSock Virtual Adapter",
        "WireSock VPN Client"));
    Assert(!WindowsTunnelReadinessProbe.IsWireSockCompatibleAdapter(
        "wg-company",
        "WireGuard Tunnel"));
    return Task.CompletedTask;
}

static async Task CleanupServiceRemovesAstralStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new AstralCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, "ayar");
        await File.WriteAllTextAsync(paths.DiscordProfile, "profil");
        await File.WriteAllTextAsync(paths.ErrorLog, "log");

        await cleanup.CleanProfileAsync(CancellationToken.None);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
        Assert(Directory.Exists(root));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceRemovesLegacyBrandStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new AstralCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);

    try
    {
        paths.EnsureDirectories();
        Directory.CreateDirectory(paths.LegacyDataDirectory);
        Directory.CreateDirectory(paths.LegacySharedDataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(paths.LegacyDataDirectory, "settings.json"),
            "legacy-settings");
        await File.WriteAllTextAsync(
            Path.Combine(paths.LegacySharedDataDirectory, "profiles.json"),
            "legacy-profile");

        await cleanup.CleanProfileAsync(CancellationToken.None);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
        Assert(!Directory.Exists(paths.LegacyDataDirectory));
        Assert(!Directory.Exists(paths.LegacySharedDataDirectory));
        Assert(Directory.Exists(root));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceStopsDiagnosticsAfterRemovingStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.FromMilliseconds(25));
    var cleanup = new AstralCleanupService(
        paths,
        accessLock,
        diagnostics,
        allowNonDefaultDataRoots: true);

    try
    {
        paths.EnsureDirectories();
        diagnostics.Info("test.beforeCleanup", "temizlik öncesi");

        await cleanup.CleanProfileAsync(CancellationToken.None);

        diagnostics.Info("test.afterCleanup", "temizlik sonrası");
        diagnostics.WriteHealth("temizlik sonrası");
        await AssertThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(diagnostics.CreateBundle()));
        await Task.Delay(80);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceRejectsUnexpectedDataRootAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.Zero);
    var cleanup = new AstralCleanupService(paths, accessLock, diagnostics);

    try
    {
        paths.EnsureDirectories();

        await AssertThrowsAsync<InvalidOperationException>(
            () => cleanup.CleanProfileAsync(CancellationToken.None));

        Assert(accessLock.RemoveCount == 0);
        diagnostics.Info("test.afterRejectedCleanup", "beklenen hata sonrasi");
        Assert(File.Exists(paths.EventLog));
        Assert(Directory.Exists(paths.DataDirectory));
        Assert(Directory.Exists(paths.SharedDataDirectory));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceDeletesReadOnlyFilesAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new AstralCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);
    var localReadOnlyFile = Path.Combine(paths.LogDirectory, "readonly.log");
    var sharedReadOnlyFile = Path.Combine(paths.ProfileDirectory, "readonly.conf");

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(localReadOnlyFile, "log");
        await File.WriteAllTextAsync(sharedReadOnlyFile, "profile");
        File.SetAttributes(
            localReadOnlyFile,
            File.GetAttributes(localReadOnlyFile) | FileAttributes.ReadOnly);
        File.SetAttributes(
            sharedReadOnlyFile,
            File.GetAttributes(sharedReadOnlyFile) | FileAttributes.ReadOnly);

        await cleanup.CleanProfileAsync(CancellationToken.None);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
    }
    finally
    {
        ResetReadOnlyAttribute(localReadOnlyFile);
        ResetReadOnlyAttribute(sharedReadOnlyFile);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceRepairsGeneratedStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new AstralCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, "ayar");
        await File.WriteAllTextAsync(paths.DiscordProfile, "profil");
        await File.WriteAllTextAsync(paths.WgcfExecutable, "wgcf");
        await File.WriteAllTextAsync(
            Path.Combine(paths.InstallerDirectory, "wiresock.msi"),
            "kurucu");
        await File.WriteAllTextAsync(paths.ErrorLog, "log");

        await cleanup.RepairAsync(CancellationToken.None);

        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.RemoveCount == 0);
        Assert(File.Exists(paths.SettingsFile));
        Assert(Directory.Exists(paths.ProfileDirectory));
        Assert(Directory.Exists(paths.ToolsDirectory));
        Assert(Directory.Exists(paths.InstallerDirectory));
        Assert(Directory.Exists(paths.LogDirectory));
        Assert(!File.Exists(paths.DiscordProfile));
        Assert(!File.Exists(paths.WgcfExecutable));
        Assert(File.Exists(paths.ErrorLog));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task DiagnosticsWritesDevOpsBundleAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(paths);

    try
    {
        diagnostics.Info(
            "test.start",
            "Tanılama başladı.",
            new Dictionary<string, string?>
            {
                ["path"] = paths.DataDirectory,
                ["programDataPath"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Astral",
                    "updates"),
                ["programFilesPath"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Astral")
            });
        diagnostics.WriteHealth(
            "test sağlıklı",
            new Dictionary<string, string?>
            {
                ["state"] = "ready"
            });
        await File.WriteAllTextAsync(
            Path.Combine(paths.LogDirectory, "unexpected.tmp"),
            "pakete alinmamali");
        await File.WriteAllTextAsync(
            paths.ErrorLog,
            "PrivateKey = fake-private-key-with-padding=\nAuthorization: Basic fake:token==\n");
        await File.WriteAllTextAsync(
            paths.TunnelLog,
            "access_token = fake-access-token\nnormal line\n");

        var bundlePath = diagnostics.CreateBundle();

        Assert(File.Exists(paths.EventLog));
        Assert(File.Exists(paths.HealthReport));
        Assert(File.Exists(paths.DiagnosticSummary));
        Assert(File.Exists(bundlePath));

        var events = await File.ReadAllTextAsync(paths.EventLog);
        var health = await File.ReadAllTextAsync(paths.HealthReport);
        var summary = await File.ReadAllTextAsync(paths.DiagnosticSummary);

        Assert(events.Contains("\"source\":\"test.start\"", StringComparison.Ordinal));
        Assert(health.Contains("test sağlıklı", StringComparison.Ordinal));
        Assert(health.Contains("\"runtime\"", StringComparison.Ordinal));
        Assert(summary.Contains("Astral tanılama özeti", StringComparison.Ordinal));
        Assert(summary.Contains("Performans", StringComparison.Ordinal));
        Assert(!events.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        AssertRedactedKnownFolder(
            events,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "%PROGRAMDATA%");
        AssertRedactedKnownFolder(
            events,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "%PROGRAMFILES%");
        using (var healthDocument = JsonDocument.Parse(health))
        {
            var runtime = healthDocument.RootElement.GetProperty("runtime");
            AssertRuntimeMetricIsNonNegative(runtime, "workingSetBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "privateMemoryBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "managedMemoryBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "gcHeapBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "gcFragmentedBytes");
        }

        using var archive = ZipFile.OpenRead(bundlePath);
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("events.jsonl", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("health.json", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("diagnostics.md", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("runtime.json", StringComparison.OrdinalIgnoreCase)));
        Assert(!archive.Entries.Any(entry =>
            entry.FullName.Equals("debug.json", StringComparison.OrdinalIgnoreCase)));
        Assert(!archive.Entries.Any(entry =>
            entry.FullName.Equals("unexpected.tmp", StringComparison.OrdinalIgnoreCase)));
        var bundledSummary = archive.GetEntry("diagnostics.md")
            ?? throw new InvalidOperationException("diagnostics.md pakette yok.");
        await using var bundledSummaryStream = bundledSummary.Open();
        using var bundledSummaryReader = new StreamReader(bundledSummaryStream);
        var bundledSummaryText = await bundledSummaryReader.ReadToEndAsync();
        Assert(!bundledSummaryText.Contains("unexpected.tmp", StringComparison.OrdinalIgnoreCase));

        var bundledRuntime = archive.GetEntry("runtime.json")
            ?? throw new InvalidOperationException("runtime.json pakette yok.");
        await using (var bundledRuntimeStream = bundledRuntime.Open())
        using (var bundledRuntimeDocument = await JsonDocument.ParseAsync(bundledRuntimeStream))
        {
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "workingSetBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "privateMemoryBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "managedMemoryBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "gcHeapBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "gcFragmentedBytes");
        }

        var bundledErrors = archive.GetEntry("errors.log")
            ?? throw new InvalidOperationException("errors.log pakette yok.");
        await using var bundledErrorsStream = bundledErrors.Open();
        using var bundledErrorsReader = new StreamReader(bundledErrorsStream);
        var bundledErrorsText = await bundledErrorsReader.ReadToEndAsync();
        Assert(!bundledErrorsText.Contains("fake-private-key", StringComparison.Ordinal));
        Assert(!bundledErrorsText.Contains("fake:token", StringComparison.Ordinal));
        Assert(bundledErrorsText.Contains("PrivateKey = [REDACTED]", StringComparison.Ordinal));
        Assert(bundledErrorsText.Contains("Authorization: [REDACTED]", StringComparison.Ordinal));

        var bundledTunnel = archive.GetEntry("tunnel.log")
            ?? throw new InvalidOperationException("tunnel.log pakette yok.");
        await using var bundledTunnelStream = bundledTunnel.Open();
        using var bundledTunnelReader = new StreamReader(bundledTunnelStream);
        var bundledTunnelText = await bundledTunnelReader.ReadToEndAsync();
        Assert(!bundledTunnelText.Contains("fake-access-token", StringComparison.Ordinal));
        Assert(bundledTunnelText.Contains("access_token = [REDACTED]", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void AssertRuntimeMetricIsNonNegative(
    JsonElement runtime,
    string propertyName)
{
    var value = runtime.GetProperty(propertyName).GetInt64();
    Assert(value >= 0);
}

static void AssertRedactedKnownFolder(
    string content,
    string knownFolderPath,
    string marker)
{
    if (string.IsNullOrWhiteSpace(knownFolderPath))
    {
        return;
    }

    Assert(!content.Contains(knownFolderPath, StringComparison.OrdinalIgnoreCase));
    Assert(content.Contains(marker, StringComparison.Ordinal));
}

static async Task DiagnosticsWritesDebugBundleOnlyWhenEnabledAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var debugEnabled = false;
    var diagnostics = new AstralDiagnostics(
        paths,
        isDebugDiagnosticsEnabled: () => debugEnabled);

    try
    {
        diagnostics.Info("test.debug", "normal tanılama");
        var normalBundle = diagnostics.CreateBundle();
        using (var normalArchive = ZipFile.OpenRead(normalBundle))
        {
            Assert(!normalArchive.Entries.Any(entry =>
                entry.FullName.Equals("debug.json", StringComparison.OrdinalIgnoreCase)));
        }

        debugEnabled = true;
        diagnostics.Info(
            "test.debug",
            "debug tanılama",
            new Dictionary<string, string?>
            {
                ["private_key"] = "raw-secret-value"
            });
        var debugBundle = diagnostics.CreateBundle();
        using var debugArchive = ZipFile.OpenRead(debugBundle);
        var debugEntry = debugArchive.GetEntry("debug.json")
            ?? throw new InvalidOperationException("debug.json pakette yok.");
        await using var debugStream = debugEntry.Open();
        using var debugDocument = await JsonDocument.ParseAsync(debugStream);

        Assert(debugDocument.RootElement.GetProperty("debugMode").GetString() == "enabled");
        Assert(debugDocument.RootElement.TryGetProperty("runtime", out _));
        Assert(debugDocument.RootElement.TryGetProperty("cpuSample", out _));
        Assert(debugDocument.RootElement.TryGetProperty("network", out _));
        Assert(debugDocument.RootElement.GetProperty("processScope").GetString() == "astral-owned-only");
        Assert(debugDocument.RootElement.TryGetProperty("processes", out _));
        Assert(AstralDiagnostics.IsProcessNameCapturedForDiagnostics("Astral"));
        Assert(AstralDiagnostics.IsProcessNameCapturedForDiagnostics("Astral.WebProxy"));
        Assert(AstralDiagnostics.IsProcessNameCapturedForDiagnostics("wiresock-client"));
        Assert(!AstralDiagnostics.IsProcessNameCapturedForDiagnostics("Discord"));
        Assert(!AstralDiagnostics.IsProcessNameCapturedForDiagnostics("DiscordPTB"));
        Assert(!AstralDiagnostics.IsProcessNameCapturedForDiagnostics("chrome"));
        Assert(!AstralDiagnostics.IsProcessNameCapturedForDiagnostics("msedge"));
        Assert(!AstralDiagnostics.IsProcessNameCapturedForDiagnostics("firefox"));

        var eventLog = await File.ReadAllTextAsync(paths.EventLog);
        Assert(!eventLog.Contains("raw-secret-value", StringComparison.Ordinal));
        Assert(eventLog.Contains("[REDACTED]", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task DiagnosticsRedactsPersistentErrorLogAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(paths);

    try
    {
        diagnostics.Failure(
            "test.secret",
            "PrivateKey = fake-private-key",
            new InvalidOperationException("Authorization: Bearer fake-token-value"),
            new Dictionary<string, string?>
            {
                ["access_token"] = "access_token = fake-access-token",
                ["private_key"] = "raw-private-key"
            });

        var eventLog = await File.ReadAllTextAsync(paths.EventLog);
        var errorLog = await File.ReadAllTextAsync(paths.ErrorLog);

        foreach (var text in new[] { eventLog, errorLog })
        {
            Assert(!text.Contains("fake-private-key", StringComparison.Ordinal));
            Assert(!text.Contains("fake-token-value", StringComparison.Ordinal));
            Assert(!text.Contains("fake-access-token", StringComparison.Ordinal));
            Assert(!text.Contains("raw-private-key", StringComparison.Ordinal));
            Assert(text.Contains("[REDACTED]", StringComparison.Ordinal));
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static Task DiagnosticsRedactsLegacyPortableBrandInPathsAsync()
{
    var legacyProductName = string.Concat("Dis", "corder");
    var redacted = AstralDiagnostics.RedactForLog(
        $@"C:\Users\operator\Desktop\{legacyProductName}-2.0.20-win-x64\Astral.exe");

    if (redacted is null)
    {
        throw new InvalidOperationException("Redaksiyon sonucu bos dondu.");
    }

    Assert(!redacted.Contains(legacyProductName, StringComparison.OrdinalIgnoreCase));
    Assert(redacted.Contains("Astral-legacy-portable", StringComparison.OrdinalIgnoreCase));
    Assert(redacted.Contains("Astral.exe", StringComparison.OrdinalIgnoreCase));

    return Task.CompletedTask;
}

static Task ProcessLauncherDisposeTimeoutsStayShortAsync()
{
    Assert(ProcessLauncher.DisposeKilledProcessExitTimeout <= TimeSpan.FromMilliseconds(1200));
    Assert(ProcessLauncher.DisposeLogPumpTimeout <= TimeSpan.FromMilliseconds(600));
    return Task.CompletedTask;
}

static async Task ProcessLauncherRedactsTunnelLogAndConfirmsExitAsync()
{
    var root = CreateTemporaryDirectory();
    var logPath = Path.Combine(root, "logs", "tunnel.log");
    var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
    var commandShell = Environment.GetEnvironmentVariable("ComSpec");
    if (string.IsNullOrWhiteSpace(commandShell))
    {
        commandShell = Path.Combine(systemDirectory, "cmd.exe");
    }

    try
    {
        Assert(File.Exists(commandShell));
        var launcher = new ProcessLauncher();
        await using var process = launcher.Start(
            commandShell,
            [
                "/c",
                "echo access_token = fake-access-token && echo PrivateKey = fake-private-key"
            ],
            Path.GetDirectoryName(commandShell)!,
            logPath);

        for (var index = 0; index < 50 && !process.HasExited; index++)
        {
            await Task.Delay(50);
        }

        Assert(process.HasExited);
        await process.DisposeAsync();
        Assert(process.ExitConfirmed);

        var text = await File.ReadAllTextAsync(logPath);
        Assert(!text.Contains("fake-access-token", StringComparison.Ordinal));
        Assert(!text.Contains("fake-private-key", StringComparison.Ordinal));
        Assert(text.Contains("access_token = [REDACTED]", StringComparison.Ordinal));
        Assert(text.Contains("PrivateKey = [REDACTED]", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ProcessLauncherSharesTunnelLogAcrossScopedProcessesAsync()
{
    var root = CreateTemporaryDirectory();
    var logPath = Path.Combine(root, "logs", "tunnel.log");
    var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
    var commandShell = Environment.GetEnvironmentVariable("ComSpec");
    if (string.IsNullOrWhiteSpace(commandShell))
    {
        commandShell = Path.Combine(systemDirectory, "cmd.exe");
    }

    try
    {
        Assert(File.Exists(commandShell));
        var launcher = new ProcessLauncher();
        await using var wireSockProcess = launcher.Start(
            commandShell,
            [
                "/c",
                "ping -n 3 127.0.0.1 > nul"
            ],
            Path.GetDirectoryName(commandShell)!,
            logPath);
        await using var webProxyProcess = launcher.Start(
            commandShell,
            [
                "/c",
                "echo Astral.WebProxy ready"
            ],
            Path.GetDirectoryName(commandShell)!,
            logPath);

        for (var index = 0; index < 50 && !webProxyProcess.HasExited; index++)
        {
            await Task.Delay(50);
        }

        Assert(webProxyProcess.HasExited);
        await webProxyProcess.DisposeAsync();
        await wireSockProcess.DisposeAsync();

        var text = await File.ReadAllTextAsync(logPath);
        Assert(text.Contains("Astral.WebProxy ready", StringComparison.Ordinal));
        Assert(text.Contains("cmd süreci başladı", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task DiagnosticsFlushesDebouncedSummaryAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var diagnostics = new AstralDiagnostics(
        paths,
        TimeSpan.FromMilliseconds(50));

    try
    {
        diagnostics.Info("test.first", "ilk durum");
        diagnostics.Info("test.second", "son durum");

        await WaitForConditionAsync(async () =>
        {
            if (!File.Exists(paths.DiagnosticSummary))
            {
                return false;
            }

            var summary = await ReadAllTextSharedAsync(paths.DiagnosticSummary);
            return summary.Contains("son durum", StringComparison.Ordinal);
        });
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static string CreateTemporaryDirectory()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "Astral.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Astral.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Astral.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Astral repo kökü bulunamadı.");
}

static string GetProjectProperty(string projectPath, string propertyName)
{
    var project = XDocument.Load(projectPath);
    return project
        .Descendants(propertyName)
        .Select(element => element.Value.Trim())
        .FirstOrDefault() ?? string.Empty;
}

static RoutingPlan CreateWebProxyRoutingPlan(string root)
{
    var registry = TargetRegistry.CreateDefault();
    var resolver = new TargetScopeResolver(
        registry,
        new DiscordAppScope(root, root, root),
        Path.Combine(root, "Astral.WebProxy.exe"));

    return resolver.Resolve(new TargetSelection([TargetIds.Wattpad]));
}

static HashSet<string> GetProbeHosts(TargetDefinition target)
{
    if (!target.Metadata.TryGetValue("probeHosts", out var probeHosts)
        || string.IsNullOrWhiteSpace(probeHosts))
    {
        throw new InvalidOperationException("Target probe host metadata is missing.");
    }

    return probeHosts
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

static int ReserveTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void ResetReadOnlyAttribute(string path)
{
    if (!File.Exists(path))
    {
        return;
    }

    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
}

static async Task WaitForConditionAsync(Func<Task<bool>> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    while (!timeout.IsCancellationRequested)
    {
        if (await condition())
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    throw new InvalidOperationException("Koşul beklenen sürede gerçekleşmedi.");
}

static async Task<string> ReadAllTextSharedAsync(string path)
{
    await using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        useAsync: true);
    using var reader = new StreamReader(
        stream,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true);
    return await reader.ReadToEndAsync();
}

static byte[] CreateUpdatePackage(
    string version = "2.0.14",
    string? extraEntryName = null)
{
    using var buffer = new MemoryStream();
    var executableBytes = Encoding.UTF8.GetBytes("astral-update");
    var executableHash = Convert.ToHexString(SHA256.HashData(executableBytes));
    var manifest = new UpdateManifest(
        version,
        [
            new UpdateManifestFile(
                "Astral.exe",
                executableBytes.Length,
                executableHash)
        ]);
    var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        manifest,
        TestJsonOptions.Manifest));

    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        var executable = archive.CreateEntry("Astral.exe");
        using (var stream = executable.Open())
        {
            stream.Write(executableBytes);
        }

        var manifestEntry = archive.CreateEntry(
            UpdatePackageValidator.ManifestFileName);
        using (var manifestStream = manifestEntry.Open())
        {
            manifestStream.Write(manifestBytes);
        }

        if (!string.IsNullOrWhiteSpace(extraEntryName))
        {
            var extraEntry = archive.CreateEntry(extraEntryName);
            using var extraStream = extraEntry.Open();
            extraStream.Write(Encoding.UTF8.GetBytes("unexpected"));
        }
    }

    return buffer.ToArray();
}

static string CreateReleaseJson(
    string version,
    Uri packageUri,
    Uri checksumUri,
    string packageSha256,
    long packageSize)
{
    return $$"""
        {
          "tag_name": "v{{version}}",
          "html_url": "https://github.com/ucsahinn/astral/releases/tag/v{{version}}",
          "assets": [
            {
              "name": "Astral-{{version}}-win-x64.zip",
              "browser_download_url": "{{packageUri.AbsoluteUri}}",
              "state": "uploaded",
              "size": {{packageSize}},
              "digest": "sha256:{{packageSha256}}"
            },
            {
              "name": "Astral-{{version}}-win-x64.sha256.txt",
              "browser_download_url": "{{checksumUri.AbsoluteUri}}",
              "state": "uploaded",
              "size": 96,
              "digest": null
            }
          ]
        }
        """;
}

static async Task WriteUpdaterHelperAsync(string root)
{
    await File.WriteAllTextAsync(
        Path.Combine(root, "Astral.Updater.exe"),
        "updater");
    await File.WriteAllTextAsync(
        Path.Combine(root, "Astral.Core.dll"),
        "core");
    var runtimeDirectory = Path.Combine(root, "runtimes", "win", "native");
    Directory.CreateDirectory(runtimeDirectory);
    await File.WriteAllTextAsync(
        Path.Combine(runtimeDirectory, "helper.dll"),
        "runtime");
}

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Doğrulama koşulu başarısız oldu.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"{typeof(TException).Name} beklenen şekilde fırlatılmadı.");
}

static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException(
        $"{typeof(TException).Name} beklenen şekilde fırlatılmadı.");
}

file static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Manifest = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

file sealed class FakeDiscordAccessLock(
    Exception? disableException = null,
    Exception? applyTunnelScopeException = null,
    int applyTunnelScopeFailureAttempt = 1,
    Exception? enableException = null,
    Exception? clearTunnelScopeException = null) : IDiscordAccessLock
{
    public int EnableCount { get; private set; }

    public int DisableCount { get; private set; }

    public int ApplyTunnelScopeCount { get; private set; }

    public int PrepareTunnelScopeCount { get; private set; }

    public bool? LastIncludeBrowserAccess { get; private set; }

    public int ClearTunnelScopeCount { get; private set; }

    public int RemoveCount { get; private set; }

    public Task EnableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnableCount++;
        if (enableException is not null)
        {
            return Task.FromException(enableException);
        }

        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisableCount++;
        if (disableException is not null)
        {
            return Task.FromException(disableException);
        }

        return Task.CompletedTask;
    }

    public Task ApplyTunnelScopeAsync(
        bool includeBrowserAccess,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyTunnelScopeCount++;
        LastIncludeBrowserAccess = includeBrowserAccess;
        if (applyTunnelScopeException is not null
            && ApplyTunnelScopeCount == applyTunnelScopeFailureAttempt)
        {
            return Task.FromException(applyTunnelScopeException);
        }

        return Task.CompletedTask;
    }

    public async Task PrepareTunnelScopeAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        PrepareTunnelScopeCount++;
        await DisableAsync(cancellationToken);
        await ApplyTunnelScopeAsync(
            !routingPlan.RequiresWebProxy,
            cancellationToken);
    }

    public Task ClearTunnelScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearTunnelScopeCount++;
        if (clearTunnelScopeException is not null)
        {
            return Task.FromException(clearTunnelScopeException);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveCount++;
        return Task.CompletedTask;
    }
}

file sealed class FakeScopedWebProxyService : IScopedWebProxyService
{
    public int ApplyCount { get; private set; }

    public int EnsureCount { get; private set; }

    public int StatusCount { get; private set; }

    public int VerifyTargetAccessCount { get; private set; }

    public int ClearCount { get; private set; }

    public RoutingPlan? LastPlan { get; private set; }

    public ScopedWebProxyStatus? EnsureStatus { get; init; }

    public ScopedWebProxyStatus? Status { get; init; }

    public ScopedWebProxyProof? Proof { get; init; }

    public Exception? ClearException { get; init; }

    public Task ApplyAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCount++;
        LastPlan = routingPlan;
        return Task.CompletedTask;
    }

    public Task<ScopedWebProxyStatus> EnsureAppliedAsync(
        RoutingPlan routingPlan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCount++;
        LastPlan = routingPlan;
        return Task.FromResult(EnsureStatus ?? CreateDefaultStatus(routingPlan));
    }

    public Task<ScopedWebProxyStatus> GetStatusAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StatusCount++;
        LastPlan = routingPlan;
        return Task.FromResult(Status ?? CreateDefaultStatus(routingPlan));
    }

    public Task<ScopedWebProxyProof> VerifyTargetAccessAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VerifyTargetAccessCount++;
        LastPlan = routingPlan;
        return Task.FromResult(Proof ?? CreateDefaultProof(routingPlan));
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearCount++;
        if (ClearException is not null)
        {
            return Task.FromException(ClearException);
        }

        return Task.CompletedTask;
    }

    private static ScopedWebProxyStatus CreateDefaultStatus(RoutingPlan routingPlan)
    {
        return routingPlan.RequiresWebProxy
            ? new ScopedWebProxyStatus(
                Required: true,
                IsApplied: true,
                Message: "Fake scoped web proxy sağlıklı.")
            : ScopedWebProxyStatus.NotRequired();
    }

    private static ScopedWebProxyProof CreateDefaultProof(RoutingPlan routingPlan)
    {
        return routingPlan.RequiresWebProxy
            ? ScopedWebProxyProof.VerifiedAll(
                routingPlan.SelectedTargets
                    .Where(target => target.HasWebScope)
                    .Select(target => target.Id + "=fake.local")
                    .ToArray(),
                18088,
                routingPlan.SelectedTargets.Count(target => target.HasWebScope))
            : ScopedWebProxyProof.NotRequired();
    }
}

file sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value)
    {
        callback(value);
    }
}

file sealed class MutableWireSockLocator : IWireSockLocator
{
    public string? Path { get; set; }

    public string? FindExecutable() => Path;
}

file sealed class FakeWireSockBootstrapper : IWireSockBootstrapper
{
    private readonly string? _path;
    private readonly Exception? _exception;

    public FakeWireSockBootstrapper(string path)
    {
        _path = path;
    }

    public FakeWireSockBootstrapper(Exception exception)
    {
        _exception = exception;
    }

    public string RequiredVersion => WireSockPackage.Version;

    public Uri ProductPage => WireSockPackage.ProductPage;

    public bool IsInstalled => _path is not null;

    public bool IsSetupConsentAccepted => true;

    public void AcceptSetupConsent()
    {
    }

    public Task<string> EnsureInstalledAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_exception is not null)
        {
            return Task.FromException<string>(_exception);
        }

        return Task.FromResult(_path!);
    }
}

file sealed class FlakyDownloadHandler(
    int failuresBeforeSuccess,
    byte[] payload) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;

        if (RequestCount <= failuresBeforeSuccess)
        {
            return Task.FromException<HttpResponseMessage>(
                new TaskCanceledException(
                    "The operation was canceled.",
                    new TimeoutException(
                        "A connection could not be established within the configured ConnectTimeout.")));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
    }
}

file sealed class FlakyDnsDownloadHandler(byte[] payload) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;

        if (RequestCount == 1)
        {
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException(
                    "No such host is known. (github.com:443)",
                    new SocketException((int)SocketError.HostNotFound)));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
    }
}

file sealed class StalledDownloadHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StalledHttpContent()
        });
    }
}

file sealed class MapHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];

    public void AddResponse(Uri uri, Func<HttpResponseMessage> response)
    {
        _responses[uri.AbsoluteUri] = response;
    }

    public void AddJson(Uri uri, string json)
    {
        _responses[uri.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public void AddText(Uri uri, string text)
    {
        _responses[uri.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.RequestUri is not null
            && _responses.TryGetValue(request.RequestUri.AbsoluteUri, out var response))
        {
            return Task.FromResult(response());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

file sealed class FailingHttpContent(Exception exception) : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return Task.FromException(exception);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}

file sealed class UnknownLengthContent(byte[] payload) : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return stream.WriteAsync(payload).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}

file sealed class DeclaredLengthContent(
    byte[] payload,
    long declaredLength) : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return stream.WriteAsync(payload).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = declaredLength;
        return true;
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult<Stream>(new MemoryStream(payload, writable: false));
    }
}

file sealed class StalledHttpContent : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return Task.CompletedTask;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult<Stream>(new StalledStream());
    }
}

file sealed class StalledStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}

file sealed class CapturingVerifiedDownloader(byte[] payload) : IVerifiedDownloader
{
    public int DownloadCount { get; private set; }

    public Uri? LastSource { get; private set; }

    public string? LastExpectedSha256 { get; private set; }

    public long? LastMaxBytes { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DownloadCount++;
        LastSource = source;
        LastExpectedSha256 = expectedSha256;
        LastMaxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report(new DownloadProgress(0, payload.Length, 0));
        await File.WriteAllBytesAsync(destination, payload, cancellationToken);
        progress?.Report(new DownloadProgress(payload.Length, payload.Length, 100));
    }
}

file sealed class FloodingVerifiedDownloader(byte[] payload, int reports) : IVerifiedDownloader
{
    public int ReportCount { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        for (var index = 0; index < reports; index++)
        {
            var percent = reports <= 1
                ? 100
                : index * 100d / (reports - 1);
            var bytesReceived = (long)Math.Round(payload.Length * (percent / 100d));
            progress?.Report(new DownloadProgress(
                bytesReceived,
                payload.Length,
                percent));
            ReportCount++;
        }

        await File.WriteAllBytesAsync(destination, payload, cancellationToken);
    }
}

file sealed class RetryProgressVerifiedDownloader(byte[] payload) : IVerifiedDownloader
{
    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report(new DownloadProgress(
            0,
            payload.Length,
            0,
            Message: "İndirme bağlantısı kuruluyor.",
            Attempt: 1,
            MaxAttempts: 2));
        progress?.Report(new DownloadProgress(
            0,
            payload.Length,
            null,
            Message: "Bağlantı kurulamadı, tekrar deneniyor.",
            Attempt: 2,
            MaxAttempts: 2,
            IsRetry: true));
        progress?.Report(new DownloadProgress(
            payload.Length,
            payload.Length,
            100));

        await File.WriteAllBytesAsync(destination, payload, cancellationToken);
    }
}

file sealed class FakeVerifiedDownloader : IVerifiedDownloader
{
    public int DownloadCount { get; private set; }

    public long? LastMaxBytes { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        DownloadCount++;
        LastMaxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report(new DownloadProgress(0, 20, 0));
        await File.WriteAllTextAsync(
            destination,
            "dogrulanmis-kurucu",
            cancellationToken);
        progress?.Report(new DownloadProgress(20, 20, 100));
    }
}

file sealed class EmptyFailureCommandRunner(int exitCode) : ICommandRunner
{
    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandResult(
            exitCode,
            string.Empty,
            string.Empty));
    }
}

file sealed class SensitiveFailureCommandRunner : ICommandRunner
{
    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandResult(
            11,
            "PrivateKey = super-private-key=\nAuthorization: Bearer example-token-value\nnormal diagnostic",
            "Cookie = session-cookie-value\nAuthorization: Basic fake:token==\nPrivateKey: fake-private-key-with-padding=\nnormal diagnostic"));
    }
}

file sealed class FakeWgcfCommandRunner(AppPaths paths) : ICommandRunner
{
    public List<string> Commands { get; } = [];

    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(string.Join(" ", arguments));

        if (!string.Equals(executable, paths.WgcfExecutable, StringComparison.Ordinal))
        {
            return Task.FromResult(new CommandResult(
                1,
                string.Empty,
                "beklenmeyen çalıştırılabilir dosya"));
        }

        if (arguments.SequenceEqual(["register", "--accept-tos"]))
        {
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(paths.WgcfAccount, "account = true");
            return Task.FromResult(new CommandResult(0, "registered", string.Empty));
        }

        if (arguments.SequenceEqual(["generate"]))
        {
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(paths.WgcfBaseProfile, """
                [Interface]
                PrivateKey = test-private-key
                Address = 172.16.0.2/32

                [Peer]
                PublicKey = test-public-key
                Endpoint = engage.cloudflareclient.com:2408
                AllowedApps = chrome.exe, gameclient.exe, Update.exe
                AllowedIPs = 0.0.0.0/0
                """);
            return Task.FromResult(new CommandResult(0, "generated", string.Empty));
        }

        return Task.FromResult(new CommandResult(
            1,
            string.Empty,
            "beklenmeyen komut"));
    }
}

file sealed class FailingGenerateWgcfCommandRunner : ICommandRunner
{
    public List<string> Commands { get; } = [];

    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(string.Join(" ", arguments));
        return Task.FromResult(new CommandResult(
            7,
            string.Empty,
            "temporary wgcf failure"));
    }
}

file sealed class FakeWireSockPackageVerifier(
    Func<string, bool>? rejectClient = null) : IWireSockPackageVerifier
{
    public int InstallerVerifyCount { get; private set; }

    public int ClientVerifyCount { get; private set; }

    public void VerifyInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new InvalidOperationException("Kurucu indirilmedi.");
        }

        InstallerVerifyCount++;
    }

    public void VerifyClient(string executablePath)
    {
        ClientVerifyCount++;

        if (rejectClient?.Invoke(executablePath) == true)
        {
            throw new InvalidDataException("İstemci güven doğrulaması başarısız oldu.");
        }
    }
}

file sealed class FakeInstallerLauncher(
    Action? onLaunch = null,
    int exitCode = 0)
    : IElevatedInstallerLauncher
{
    public int LaunchCount { get; private set; }

    public string? LastInstallerPath { get; private set; }

    public Task<int> InstallAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(installerPath))
        {
            throw new InvalidOperationException("Kurucu indirilmedi.");
        }

        LaunchCount++;
        LastInstallerPath = installerPath;
        onLaunch?.Invoke();
        return Task.FromResult(exitCode);
    }
}

file sealed class FakeProfileProvisioner(
    string profilePath,
    Exception? exception = null) : IProfileProvisioner
{
    public IReadOnlyList<string> LastAllowedApplications { get; private set; } = [];

    public Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        LastAllowedApplications = allowedApplications.ToArray();

        if (allowedApplications.Count == 0)
        {
            throw new InvalidOperationException("Uygulama listesi boş geldi.");
        }

        if (exception is not null)
        {
            return Task.FromException<string>(exception);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(profilePath, "test-profile");
        return Task.FromResult(profilePath);
    }
}

file sealed class RecordingCommandRunner(CommandResult? result = null) : ICommandRunner
{
    public List<string> Commands { get; } = [];

    public List<TimeSpan> Timeouts { get; } = [];

    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(string.Join(" ", arguments));
        Timeouts.Add(timeout);
        return Task.FromResult(result ?? new CommandResult(0, string.Empty, string.Empty));
    }
}

file sealed class SequencedCommandRunner(params CommandResult[] results) : ICommandRunner
{
    private int _index;

    public List<string> Commands { get; } = [];

    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(string.Join(" ", arguments));
        if (_index >= results.Length)
        {
            return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
        }

        return Task.FromResult(results[_index++]);
    }
}

file sealed class ConnectProofProcessLauncher(
    Func<string, bool> shouldSucceed,
    TimeSpan? responseDelay = null)
    : IProcessLauncher, IAsyncDisposable
{
    private ConnectProofManagedProcess? _process;

    public int MaxActiveConnections => _process?.MaxActiveConnections ?? 0;

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        var portIndex = arguments
            .Select((value, index) => (value, index))
            .Single(item => item.value == "--port")
            .index + 1;
        var port = int.Parse(arguments[portIndex], CultureInfo.InvariantCulture);
        _process = new ConnectProofManagedProcess(
            port,
            shouldSucceed,
            responseDelay ?? TimeSpan.Zero);
        return _process;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            await _process.DisposeAsync();
        }
    }
}

file sealed class ConnectProofManagedProcess : IManagedProcess
{
    private static int _nextProcessId = 7000;
    private readonly Func<string, bool> _shouldSucceed;
    private readonly TimeSpan _responseDelay;
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener;
    private readonly Task _acceptLoop;
    private int _activeConnections;
    private int _maxActiveConnections;
    private bool _hasExited;

    public ConnectProofManagedProcess(
        int port,
        Func<string, bool> shouldSucceed,
        TimeSpan responseDelay)
    {
        _shouldSucceed = shouldSucceed;
        _responseDelay = responseDelay;
        ProcessId = Interlocked.Increment(ref _nextProcessId);
        StartTime = DateTimeOffset.Now;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public event EventHandler? Exited;

    public bool HasExited => _hasExited;

    public bool ExitConfirmed => _hasExited;

    public int? ExitCode => _hasExited ? 0 : null;

    public int ProcessId { get; }

    public DateTimeOffset? StartTime { get; }

    public int MaxActiveConnections => Volatile.Read(ref _maxActiveConnections);

    public async Task StopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop.WaitAsync(timeout, cancellationToken);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException
                or ObjectDisposedException
                or SocketException
                or TimeoutException)
        {
        }

        Exited?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException
                    or ObjectDisposedException
                    or SocketException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client), _stop.Token);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        var active = Interlocked.Increment(ref _activeConnections);
        UpdateMaxActiveConnections(active);
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, _stop.Token);
            var request = Encoding.ASCII.GetString(buffer, 0, read);
            var host = ParseConnectHost(request);
            if (_responseDelay > TimeSpan.Zero)
            {
                await Task.Delay(_responseDelay, _stop.Token);
            }

            var response = _shouldSucceed(host)
                ? "HTTP/1.1 200 Connection Established\r\n\r\n"
                : "HTTP/1.1 502 Bad Gateway\r\n\r\n";
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(response),
                _stop.Token);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private void UpdateMaxActiveConnections(int active)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maxActiveConnections);
            if (active <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _maxActiveConnections,
                    active,
                    current) == current)
            {
                return;
            }
        }
    }

    private static string ParseConnectHost(string request)
    {
        var firstLine = request.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        var authority = parts[1];
        var colon = authority.LastIndexOf(':');
        return colon > 0 ? authority[..colon] : authority;
    }
}

file sealed class FakeProcessLauncher(
    IManagedProcess process,
    params string[] logLines)
    : IProcessLauncher
{
    public string? LastExecutable { get; private set; }

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        LastExecutable = executable;
        LastArguments = arguments.ToArray();
        var lines = logLines.Length > 0
            ? logLines
            : ["Connection established"];
        if (lines.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllLines(logPath, lines);
        }

        return process;
    }
}

file sealed class SequencedProcessLauncher(params FakeManagedProcess[] processes) : IProcessLauncher
{
    private int _index;

    public int StartCount { get; private set; }

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        if (_index >= processes.Length)
        {
            throw new InvalidOperationException("Beklenenden fazla süreç başlatıldı.");
        }

        StartCount++;
        return processes[_index++];
    }
}

file sealed class FakeManagedProcess : IManagedProcess
{
    private static int _nextProcessId = 1000;
    private readonly bool _exitOnStop;
    private readonly bool _exitOnDispose;
    private readonly int _exitCode;

    public event EventHandler? Exited;

    public FakeManagedProcess(
        bool initialHasExited = false,
        bool exitOnStop = true,
        bool exitOnDispose = true,
        int exitCode = 0)
    {
        _exitOnStop = exitOnStop;
        _exitOnDispose = exitOnDispose;
        _exitCode = exitCode;
        ProcessId = Interlocked.Increment(ref _nextProcessId);
        StartTime = DateTimeOffset.Now;
        HasExited = initialHasExited;
    }

    public bool HasExited { get; private set; }

    public bool ExitConfirmed => HasExited;

    public int? ExitCode => HasExited ? _exitCode : null;

    public int ProcessId { get; }

    public DateTimeOffset? StartTime { get; }

    public int StopCount { get; private set; }

    public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        StopCount++;
        if (_exitOnStop)
        {
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_exitOnDispose)
        {
            HasExited = true;
        }

        return ValueTask.CompletedTask;
    }
}

file sealed class ExitAfterHasExitedChecksManagedProcess(
    int exitAfterChecks,
    int exitCode) : IManagedProcess
{
    private int _hasExitedChecks;
    private bool _hasExited;

    public event EventHandler? Exited;

    public int ProcessId { get; } = 9001;

    public DateTimeOffset? StartTime { get; } = DateTimeOffset.Now;

    public bool HasExited
    {
        get
        {
            if (!_hasExited
                && Interlocked.Increment(ref _hasExitedChecks) >= exitAfterChecks)
            {
                _hasExited = true;
                Exited?.Invoke(this, EventArgs.Empty);
            }

            return _hasExited;
        }
    }

    public bool ExitConfirmed => _hasExited;

    public int? ExitCode => HasExited ? exitCode : null;

    public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _hasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _hasExited = true;
        return ValueTask.CompletedTask;
    }
}

file sealed class SequencedDiscordProcessManager(
    DiscordProcessSnapshot snapshot,
    params DiscordRestartResult[] restartResults)
    : IDiscordProcessManager
{
    private int _restartIndex;

    public int RestartCount { get; private set; }

    public int VerifyReadyCount { get; private set; }

    public DiscordRestartResult VerifyResult { get; init; } =
        new(true, "Discord güncellendi.");

    public DiscordProcessSnapshot Capture() => snapshot;

    public Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_restartIndex >= restartResults.Length)
        {
            throw new InvalidOperationException("Beklenenden fazla Discord restart denendi.");
        }

        RestartCount++;
        return Task.FromResult(restartResults[_restartIndex++]);
    }

    public Task<DiscordRestartResult> VerifyReadyAsync(
        DiscordProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VerifyReadyCount++;
        return Task.FromResult(VerifyResult);
    }

    public Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken) =>
        Task.FromResult(new DiscordRestartResult(true, "Discord kapatıldı."));
}

file sealed class FakeDiscordProcessManager(
    DiscordProcessSnapshot snapshot,
    DiscordRestartResult? restartResult = null)
    : IDiscordProcessManager
{
    public int RestartCount { get; private set; }

    public int VerifyReadyCount { get; private set; }

    public int CloseCount { get; private set; }

    public DiscordProcessSnapshot Capture() => snapshot;

    public Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        RestartCount++;
        return Task.FromResult(
            restartResult
            ?? new DiscordRestartResult(true, "Discord yenilendi."));
    }

    public Task<DiscordRestartResult> VerifyReadyAsync(
        DiscordProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        VerifyReadyCount++;
        return Task.FromResult(new DiscordRestartResult(true, "Discord güncellendi."));
    }

    public Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        CloseCount++;
        return Task.FromResult(new DiscordRestartResult(true, "Discord kapatıldı."));
    }
}

file sealed class FakeTunnelReadinessProbe(
    params TunnelReadinessSnapshot[] snapshots)
    : ITunnelReadinessProbe
{
    public int CaptureCount { get; private set; }

    public TunnelReadinessSnapshot Capture()
    {
        CaptureCount++;
        if (snapshots.Length == 0)
        {
            throw new InvalidOperationException(
                "At least one tunnel readiness snapshot is required.");
        }

        return snapshots[Math.Min(CaptureCount - 1, snapshots.Length - 1)];
    }
}

file sealed class ObservingTunnelReadinessProbe(
    Func<TunnelReadinessSnapshot> capture)
    : ITunnelReadinessProbe
{
    public int CaptureCount { get; private set; }

    public TunnelReadinessSnapshot Capture()
    {
        CaptureCount++;
        return capture();
    }
}
