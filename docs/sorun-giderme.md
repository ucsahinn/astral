# Astral Sorun Giderme

## Astral Bağlı Değil

Bu durum bağlantının açık olmadığını gösterir. Seçili hedeflerin tünelden çıkması için ana ekrandaki **Bağlan** düğmesini kullanın.

## Hedef Seçimi Kilitli

Bağlantı açıkken hedef kapsamı değiştirilemez. Önce **Bağlantıyı Kes**, sonra ana ekrandaki hedef kartlarından seçimi güncelleyin.

## Web Hedefi Açılmıyor

Kontrol edin:

- Hedef ana ekrandaki kartlarda seçili mi?
- Tarayıcı Windows PAC/proxy ayarını kullanıyor mu?
- Kurumsal policy veya manuel proxy ayarı PAC kullanımını engelliyor mu?
- QUIC/DoH/WebRTC gibi TCP CONNECT dışı bir yol zorlanıyor mu?
- Tanılama paketindeki proxy/PAC özeti seçili domaini gösteriyor mu?

Astral web hedeflerinde tarayıcı exe'sini WireSock kapsamına almaz. Bu bilinçli güvenlik sınırıdır.

## Uygulama Hedefi Açılmıyor

Kontrol edin:

- Preset seçili mi?
- Uygulama farklı bir launcher veya updater exe üzerinden mi çalışıyor?
- Tanılama paketinde `AllowedApps` özeti seçili hedefi gösteriyor mu?
- WireSock kurulum veya readiness aşamasında hata var mı?

## WireSock Hazırlığı Doğrulanamadı

Astral WireSock'u varsayılan transparent mode ile başlatır. Bu modda sanal adaptörün `Up` görünmesi tek başına yeterli kanıt değildir. Bağlantı başarılı sayılmadan önce şu kanıtlardan biri aranır:

- WireSock logunda tünelin başladığını gösteren onay.
- WireSock adapter sayaçlarında süreç başladıktan sonra trafik artışı.
- Web hedefi seçildiyse `Astral.WebProxy.exe` üzerinden seçili her web hedefi için en az bir başarılı scoped `CONNECT` kanıtı.

Tanılama paketinde şu imzalar birlikte görünüyorsa bağlantı motoru gerçekten hazır olmadan kapanmıştır:

- `wireSockProcessExited=True`
- `wireSockProcessExitCode` değerinin beklenmeyen şekilde dolu olması
- `wireSockMode=transparent`
- `tunnelReadiness` değerinin `transparent-process-running` olmaması

Bu durumda Astral bağlantıyı başarılı saymaz ve proxy/PAC kapsamını geri temizler. Önce **Onar** akışını çalıştırın, ardından tekrar deneyin. Devam ederse WireSock kurulumu, güvenlik yazılımı, profil üretimi veya Windows ağ yığını ayrıca incelenmelidir.

Güncel tanılama paketinde `wireSockMode=transparent`, `tunnelReadiness=transparent-process-running`, `webProxyProof.verified=True`, `webProxyProof.requiredTargetCount` ile `webProxyProof.verifiedTargetCount` eşit, `allowedBareBrowserNameCount=0`, `allowedBrowserApplicationCount=0` ve `webProxy=included` görünüyorsa seçili web hedefleri için kapsam planı dar ve beklenen biçimde kurulmuştur.

## Proxy/PAC Temizlenmedi Şüphesi

Astral bağlantı kapanırken PAC/proxy state'ini geri alır. Şüphe varsa:

1. Astral'ı kapatıp tekrar açın.
2. **Profil Temizle** akışını çalıştırın.
3. Tanılama paketi oluşturun.

Manuel registry temizliği veya sistem proxy ayarı değiştirme işlemleri kullanıcı onayı ve dikkat gerektirir.

## Otomatik Güncelleme Görünmüyor

v2.2.4 ve sonrası sürümlerde **Güncelle** düğmesi yalnız yeni sürüm bulunduğunda görünür. Yeni sürüm yoksa bu düğmenin görünmemesi normaldir. Güncelleme hatası varsa tanılama paketindeki `ui.update.backgroundCheck` ve update log kayıtlarını kontrol edin.

## Tanılama İçin Gerekli Bilgiler

Hata bildirirken şunları ekleyin:

- Astral sürümü.
- Windows sürümü.
- Seçili hedefler.
- Redakte edilmiş tanılama paketi.

Özel anahtar, token, cookie, `wgcf-account.toml`, tam WireGuard profili veya kişisel veri paylaşmayın.
