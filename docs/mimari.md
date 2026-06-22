# Astral Mimarisi

Astral, seçili uygulama veya web hedefleri için dar kapsamlı bağlantı planı üreten Windows uygulamasıdır. Genel cihaz VPN'i, tüm tarayıcı tüneli veya sistem geneli proxy aracı değildir.

## Ana Kararlar

- Uygulama portable çalışır; kendi servis kaydı yoktur.
- WireSock ve wgcf repoya gömülmez.
- Sistem DNS'i kalıcı değiştirilmez.
- Kalıcı DPI motoru veya paket filtre sürücüsü kurulmaz.
- Kullanıcı ana ekrandaki hedef kartlarını seçer.
- `TargetRegistry` yalnız hazır presetleri tek kaynakta tutar.
- `TargetScopeResolver`, seçili hedeflerden `RoutingPlan` üretir.
- Uygulama hedefleri WireSock `AllowedApps` satırına yazılır.
- Web hedefleri için genel tarayıcı exe'leri `AllowedApps` içine yazılmaz; yalnızca `Astral.WebProxy.exe` girer.
- WireSock yalnız uygulama kapsamı olan hedeflerde `-lac` ile scoped sanal ağ arayüzü modunda başlatılır; web-only seçimlerde sanal adaptör açmadan transparent process modu kullanılır ve kanıt `Astral.WebProxy.exe` üzerinden alınır.
- `Astral.WebProxy` loopback üzerinde çalışır, sadece seçili domain/pattern hedeflerini kabul eder.
- HTTPS için TLS çözme, sertifika kurma veya içerik okuma yoktur; proxy CONNECT host/Host allowlist kontrolü yapar.
- PAC çıktısı seçili domainlerde PROXY, diğer tüm domainlerde DIRECT döndürür.
- PAC/proxy state'i bağlantı kapanınca, hata alınca, WireSock beklenmedik kapanınca ve uygulama kapanınca temizlenir.

## Bileşenler

### `Astral.App`

WPF arayüzünü, ana ekran hedef kartlarını, WireSock kurulum onay ekranını, Authenticode doğrulamasını, otomatik güncelleme UI'ını ve Windows UAC ile kurucu başlatmayı içerir.

### `Astral.Core`

Tünel yaşam döngüsü, hedef registry/selection/resolver, profil üretimi, web proxy policy/PAC üretimi, indirme doğrulaması, WireSock hazırlığı, tanılama ve süreç yönetimi burada yer alır.

### `Astral.WebProxy`

Seçili web domainlerini taşıyan küçük loopback proxy exe'sidir. `--allow <domain>` girdileriyle başlar, allowlist dışı hostlara `403` döndürür ve HTTPS bağlantılarında yalnızca CONNECT tüneli kurar.

### Testler

- `Astral.Core.Tests`: hedef presetleri, migration, PAC policy, profil sertleştirme, WireSock hazırlığı, access-lock hata görünürlüğü ve denetleyici yaşam döngüsünü doğrular.
- `Astral.Windows.Tests`: WireSock imza doğrulaması, ana pencere, inline hedef kartları, güncelleme düğmesi görünürlüğü, tray/kapanma davranışı ve kurulum onay penceresinin gerçek WPF kontrolünü yapar.

## Bağlantı Akışı

1. Uygulama açılışta eski ayarları yeni hedef seçimine taşır; varsayılan hedef Discord'dur.
2. Kullanıcı ana ekrandaki hedef kartlarından preset seçer.
3. Kullanıcı **Bağlan** düğmesine basar.
4. Astral bağlantı korumasını geçici olarak kaldırır.
5. WireSock kurulumu için kullanıcı onayı yoksa onay penceresi açılır.
6. WireSock kurulu değilse resmi kurucu indirilir ve doğrulanır.
7. `wgcf` indirilir, doğrulanır ve Cloudflare WARP profili üretilir.
8. `TargetScopeResolver` seçili hedeflerden `RoutingPlan` üretir.
9. Profildeki eski veya geniş `AllowedApps` / `#@ws:AllowedApps` satırları kaldırılır.
10. Seçili uygulama exe'leri ve gerekiyorsa `Astral.WebProxy.exe` yeni `AllowedApps` satırına yazılır.
11. Web hedefleri varsa PAC dosyası oluşturulur ve `Astral.WebProxy.exe` allowlist ile başlatılır.
12. WireSock VPN Client web-only seçimlerde `run -config <profile> -log-level info`, uygulama kapsamı olan seçimlerde `run -config <profile> -lac -log-level info` komutuyla kullanıcı süreci olarak başlar.
13. Readiness doğrulaması uygulama kapsamlı virtual-adapter modunda WireSock sürecinin sağlıklı kalmasını, scoped adapter'ın hazır olmasını ve tünel başlangıç logu ya da süreç başlangıcından sonra artan adapter trafik kanıtı üretmesini ister. Web-only transparent modda ise sanal adaptör zorunlu değildir; seçili hostlar için scoped `CONNECT` kanıtı ve PAC/WebProxy kapsam doğrulaması yeterlidir. Web kanıtı app hedeflerinin yerine geçmez.
14. Discord dışı uygulama kapsamlı hedeflerde genel profil kapsamı tek başına tam bağlantı kanıtı sayılmaz; `targetAppProof.*` alanları hedefe özel uygulama kanıtının gerekli, doğrulanmış veya eksik olduğunu gösterir. Windows sağlayıcısı hedef exe ipuçlarından çalışan süreci bulur, hedef probe hostlarını DNS ile çözer ve hedef sürecin bu IPv4/IPv6 adreslerine sahip olduğu established TCP bağlantısını arar. Kanıt eksikse fail-closed davranır ve `TargetActionRequired` durumunda kalır.
15. Bağlantı açıkken `Astral.WebProxy` upstream `CONNECT` hataları ortak `tunnel.log` içinde izlenir; tanılama detayları `webProxyRuntime.*` alanlarıyla ilk kanıttan sonra oluşan runtime hedef hatasını görünür kılar.
16. Bağlantı kesilirken WireSock süreci sonlandırılır, PAC/proxy state'i geri alınır ve bağlantı koruması tekrar etkinleştirilir. Process handle artık dispose sonrasında okunamadığında kapanış akışı fatal hataya düşmeden güvenli tanılama üretir.

## Hedef Modeli

- `TargetDefinition`: id, ad, kategori, kapsam türü, domainler, executable ipuçları ve ikon anahtarını taşır.
- `TargetRegistry`: Discord, Wattpad, Bigo Live, Azar, Tango, LiVU, IMVU, Blogspot, Radio Garden, DW, VOA, Ekşi Sözlük, Grok, Imgur ve Pastebin presetlerini sağlar.
- `TargetSelectionStore`: seçimi `settings.json` içinde saklar; eski özel hedef alanlarını sessizce yok sayar.
- `TargetScopeResolver`: seçimi `RoutingPlan` haline getirir.
- `RoutingPlan`: `AllowedApplications`, proxy domain kuralları ve kullanıcıya gösterilecek kapsam özetini içerir.

## Web Proxy ve PAC

PAC örüntüsü:

```javascript
function FindProxyForURL(url, host) {
  if (host == "wattpad.com" || dnsDomainIs(host, "wattpad.com")) return "PROXY 127.0.0.1:18088";
  return "DIRECT";
}
```

Bu tasarımda tarayıcı süreci WireSock kapsamına girmez. Tarayıcı normal kalır; yalnız seçili domain istekleri PAC üzerinden `Astral.WebProxy.exe` sürecine yönlenir. Proxy süreci WireSock `AllowedApps` içinde olduğu için yalnız bu seçili web hedefleri tünel yoluna girer.

## Güvenlik Sınırları

- TLS doğrulaması devre dışı bırakılamaz.
- İndirilen her ikili dosya sabit hash veya güven zinciriyle doğrulanır.
- Kurulu WireSock komut satırı dosyası imza ve yayıncı kontrolünden geçmeden çalıştırılmaz.
- Virgül, satır sonu ve boş `AllowedApps` / `#@ws:AllowedApps` enjeksiyonu reddedilir.
- Loglarda özel anahtar, token, cookie veya tam hassas WireGuard profili yazılmaz.
- PAC/proxy rollback state'i Astral sahiplik marker'ı ile tutulur ve yalnız Astral'ın uyguladığı PAC değeri aktifse restore edilir.

## Bilinen Sınırlar

- PAC kullanımı bazı tarayıcı veya kurumsal policy ayarlarında farklı davranabilir.
- Firefox gibi bazı uygulamalar Windows proxy ayarını kullanmayacak şekilde yapılandırılmış olabilir.
- QUIC/UDP/WebRTC gibi TCP CONNECT dışı yollar bu tasarımın kapsamı dışındadır.
- Virtual-adapter modunda WireSock süreci sağlıklı kalsa bile adapter hazır değilse veya tünel başlangıç/adapter trafik kanıtı yoksa bağlantı başarılı sayılmaz; `wireSockConnectionEstablished=False` ve `wireSockTrafficDeltaObserved=False` birlikte hata kanıtıdır.
- Presetler erişim durumunu garanti etmez; sadece hedef kapsamını tanımlar.

## Yayın Modeli

`scripts/build-release.ps1` doğrulama scriptini çalıştırır, Windows x64 bağımsız yayın çıktısı üretir ve proje sürümünden `artifacts/Astral-<sürüm>-win-x64.zip` arşivini oluşturur. Paket içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, manifest ve gerekli runtime dosyaları yer alır.
