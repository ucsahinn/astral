# Astral Mimarisi

Astral, seçili uygulama veya web hedefleri için dar kapsamlı bağlantı planı üreten Windows uygulamasıdır. Genel cihaz VPN'i, tüm tarayıcı tüneli veya sistem geneli proxy aracı değildir.

## Ana Kararlar

- Uygulama portable çalışır; kendi servis kaydı veya görev zamanlayıcısı yoktur.
- WireSock ve wgcf repoya gömülmez.
- Sistem DNS'i kalıcı değiştirilmez.
- Kalıcı DPI motoru veya paket filtre sürücüsü kurulmaz.
- Kullanıcı hedef seçer; `TargetRegistry` presetleri ve özel hedefleri tek kaynakta tutar.
- `TargetScopeResolver`, seçili hedeflerden `RoutingPlan` üretir.
- Uygulama hedefleri WireSock `AllowedApps` satırına yazılır.
- Web hedefleri için genel tarayıcı exe'leri `AllowedApps` içine yazılmaz; yalnızca `Astral.WebProxy.exe` girer.
- Astral.WebProxy loopback üzerinde çalışır, sadece seçili domain/pattern hedeflerini kabul eder.
- HTTPS için TLS çözme, sertifika kurma veya içerik okuma yoktur; proxy CONNECT host/Host allowlist kontrolü yapar.
- PAC çıktısı seçili domainlerde PROXY, diğer tüm domainlerde DIRECT döndürür.
- PAC/proxy state'i bağlantı kapanınca, hata alınca, WireSock beklenmedik kapanınca ve uygulama kapanınca temizlenir.

## Bileşenler

### `Astral.App`

WPF arayüzünü, Hedef Merkezi modalını, WireSock kurulum onay ekranını, Authenticode doğrulamasını ve Windows UAC ile kurucu başlatmayı içerir.

### `Astral.Core`

Tünel yaşam döngüsü, hedef registry/selection/resolver, profil üretimi, web proxy policy/PAC üretimi, indirme doğrulaması, WireSock hazırlığı, tanılama ve süreç yönetimi burada yer alır.

### `Astral.WebProxy`

Seçili web domainlerini taşıyan küçük loopback proxy exe'sidir. `--allow <domain>` girdileriyle başlar, allowlist dışı hostlara `403` döndürür ve HTTPS bağlantılarında yalnızca CONNECT tüneli kurar.

### Testler

- `Astral.Core.Tests`: hedef presetleri, migration, domain/custom validation, PAC policy, profil sertleştirme, WireSock hazırlığı ve denetleyici yaşam döngüsünü doğrular.
- `Astral.Windows.Tests`: WireSock imza doğrulaması, ana pencere, Hedef Merkezi ve kurulum onay penceresinin gerçek WPF görsel kontrolünü yapar.

## Bağlantı Akışı

1. Uygulama açılışta eski ayarları yeni hedef seçimine taşır; varsayılan hedef Discord'dur.
2. Kullanıcı **Hedefleri Seç** ile preset veya özel hedefleri kaydeder.
3. Kullanıcı **Bağlan** düğmesine basar.
4. Astral bağlantı korumasını geçici olarak kaldırır.
5. WireSock kurulumu için kullanıcı onayı yoksa onay penceresi açılır.
6. WireSock kurulu değilse resmi kurucu indirilir ve doğrulanır.
7. `wgcf` indirilir, doğrulanır ve Cloudflare WARP profili üretilir.
8. `TargetScopeResolver` seçili hedeflerden `RoutingPlan` üretir.
9. Profildeki eski veya geniş `AllowedApps` satırları kaldırılır.
10. Seçili uygulama exe'leri ve gerekiyorsa `Astral.WebProxy.exe` yeni `AllowedApps` satırına yazılır.
11. Web hedefleri varsa PAC dosyası oluşturulur ve Astral.WebProxy allowlist ile başlatılır.
12. WireSock VPN Client `run -config <discord.conf> -log-level info` komutuyla kullanıcı süreci olarak başlar.
13. Bağlantı kesilirken WireSock süreci sonlandırılır, PAC/proxy state'i geri alınır ve bağlantı koruması tekrar etkinleştirilir.

## Hedef Modeli

- `TargetDefinition`: id, ad, kategori, kapsam türü, domainler, executable ipuçları ve ikon anahtarını taşır.
- `TargetRegistry`: Discord, Roblox, Wattpad, Bigo Live, Azar, Tango, LiVU, IMVU, Blogspot, Özel EXE ve Özel Domain presetlerini sağlar.
- `TargetSelectionStore`: seçimi `settings.json` içinde saklar.
- `TargetScopeResolver`: seçimi `RoutingPlan` haline getirir.
- `RoutingPlan`: `AllowedApplications`, proxy domain kuralları ve kullanıcıya gösterilecek kapsam özetini içerir.

## Custom Validation

- Özel EXE mutlak canonical path olmak zorundadır.
- `.exe` uzantısı dışındaki dosyalar reddedilir.
- Dosya mevcut olmalı ve reparse point olmamalıdır.
- Genel tarayıcı exe'leri özel EXE hedefi olarak reddedilir.
- Özel domain IDN/punycode normalize edilir.
- URL, port, IP literal, tek label, `*`, `*.com` gibi geniş wildcard patternleri reddedilir.

## Web Proxy ve PAC

PAC örüntüsü:

```javascript
function FindProxyForURL(url, host) {
  if (host == "wattpad.com" || dnsDomainIs(host, "wattpad.com")) return "PROXY 127.0.0.1:18088";
  return "DIRECT";
}
```

Bu tasarımda tarayıcı süreci WireSock kapsamına girmez. Tarayıcı normal kalır; yalnız seçili domain istekleri PAC üzerinden Astral.WebProxy'ye yönlenir. Proxy süreci WireSock `AllowedApps` içinde olduğu için yalnız bu seçili web hedefleri tünel yoluna girer.

## Güvenlik Sınırları

- TLS doğrulaması devre dışı bırakılamaz.
- İndirilen her ikili dosya sabit hash ile doğrulanır.
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
