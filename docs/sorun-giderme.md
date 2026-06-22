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

Astral web-only hedeflerde WireSock'u sanal adaptör açmadan transparent process modunda başlatır; uygulama kapsamı olan hedeflerde `-lac` scoped sanal ağ arayüzü modunu kullanır. WireSock sürecinin yaşıyor olması tek başına yeterli kanıt değildir. Bağlantı başarılı sayılmadan önce şu kanıtlar aranır:

- Uygulama kapsamı varsa WireSock logunda tünelin başladığını gösteren onay veya adapter sayaçlarında süreç başladıktan sonra trafik artışı.
- Web hedefi seçildiyse `Astral.WebProxy.exe` üzerinden seçili her web hedefi için en az bir başarılı scoped `CONNECT` kanıtı.
- App hedefi seçildiyse WireSock handshake veya adapter trafik kanıtı; web proxy kanıtı app hedefi yerine geçmez.

Tanılama paketinde şu imzalar birlikte görünüyorsa bağlantı motoru gerçekten hazır olmadan kapanmıştır:

- `wireSockProcessExited=True`
- `wireSockProcessExitCode` değerinin beklenmeyen şekilde dolu olması
- `wireSockMode=virtual-adapter` ise `tunnelReadiness` değerinin `ready` olmaması
- `wireSockMode=transparent` ise `tunnelReadiness` değerinin `transparent-process-running` olmaması
- `wireSockConnectionEstablished=False` ve `wireSockTrafficDeltaObserved=False` değerlerinin birlikte görünmesi

Bu durumda Astral bağlantıyı başarılı saymaz ve proxy/PAC kapsamını geri temizler. Önce **Onar** akışını çalıştırın, ardından tekrar deneyin. Devam ederse WireSock kurulumu, güvenlik yazılımı, profil üretimi veya Windows ağ yığını ayrıca incelenmelidir.

Güncel tanılama paketinde web-only hedeflerde `wireSockMode=transparent`, `tunnelReadiness=transparent-process-running`, `webProxyProof.verified=True`, `webProxyProof.requiredTargetCount` ile `webProxyProof.verifiedTargetCount` eşit, `allowedBareBrowserNameCount=0`, `allowedBrowserApplicationCount=0` ve `webProxy=included` görünüyorsa web kapsamı dar kurulmuştur. Uygulama kapsamı olan hedeflerde ayrıca `wireSockMode=virtual-adapter`, `tunnelReadiness=ready` ve app hedeflerinde `wireSockConnectionEstablished=True` veya `wireSockTrafficDeltaObserved=True` gerekir.

`webProxyProof.verified=True` ilk scoped `CONNECT` kanıtının geçtiğini gösterir. Bağlantı açıldıktan sonra sayfa geç açılıyor veya seçili web hedefi 502/time-out veriyorsa tanılama paketindeki `webProxyRuntime.failuresDetected`, `webProxyRuntime.failureCount` ve `webProxyRuntime.lastFailure` alanlarını da kontrol edin. Bu alanlar ilk kanıt geçtikten sonra `Astral.WebProxy` tarafından görülen upstream `CONNECT` hatalarını gösterir; browser'ın WireSock kapsamına alındığı anlamına gelmez.

Discord dışı uygulama kapsamlı hedeflerde `targetAppProof.required=True` ve `targetAppProof.verified=False` görülüyorsa Astral profil kapsamını hazırlamış ama hedef uygulamanın tünel yolundan çıktığını kanıtlayamamış demektir. Bu durumda uygulama kapatılıp tünel açıkken yeniden açılmalı veya hedefe özel canlı smoke kanıtı alınmalıdır; Astral bu kanıt eksikken tam `Connected` raporu vermez.

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
