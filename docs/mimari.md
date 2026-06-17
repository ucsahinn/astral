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
9. Profildeki eski veya geniş `AllowedApps` satırları kaldırılır.
10. Seçili uygulama exe'leri ve gerekiyorsa `Astral.WebProxy.exe` yeni `AllowedApps` satırına yazılır.
11. Web hedefleri varsa PAC dosyası oluşturulur ve `Astral.WebProxy.exe` allowlist ile başlatılır.
12. WireSock VPN Client `run -config <profile> -log-level info` komutuyla kullanıcı süreci olarak başlar.
13. Bağlantı kesilirken WireSock süreci sonlandırılır, PAC/proxy state'i geri alınır ve bağlantı koruması tekrar etkinleştirilir.

## Hedef Modeli

- `TargetDefinition`: id, ad, kategori, kapsam türü, domainler, executable ipuçları ve ikon anahtarını taşır.
- `TargetRegistry`: Discord, Roblox, Wattpad, Bigo Live, Azar, Tango, LiVU, IMVU ve Blogspot presetlerini sağlar.
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
- Virgül, satır sonu ve boş `AllowedApps` enjeksiyonu reddedilir.
- Loglarda özel anahtar, token, cookie veya tam hassas WireGuard profili yazılmaz.
- PAC/proxy rollback state'i Astral sahiplik marker'ı ile tutulur ve yalnız Astral'ın uyguladığı PAC değeri aktifse restore edilir.

## Bilinen Sınırlar

- PAC kullanımı bazı tarayıcı veya kurumsal policy ayarlarında farklı davranabilir.
- Firefox gibi bazı uygulamalar Windows proxy ayarını kullanmayacak şekilde yapılandırılmış olabilir.
- QUIC/UDP/WebRTC gibi TCP CONNECT dışı yollar bu tasarımın kapsamı dışındadır.
- Presetler erişim durumunu garanti etmez; sadece hedef kapsamını tanımlar.

## Yayın Modeli

`scripts/build-release.ps1` doğrulama scriptini çalıştırır, Windows x64 bağımsız yayın çıktısı üretir ve proje sürümünden `artifacts/Astral-<sürüm>-win-x64.zip` arşivini oluşturur. Paket içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, manifest ve gerekli runtime dosyaları yer alır.
