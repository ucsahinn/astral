# Astral Sorun Giderme

## Astral Bağlı Değil

Bu durum bağlantının açık olmadığını gösterir. Seçili hedeflerin tünelden çıkması için ana ekrandaki **Bağlan** düğmesini kullanın.

## Hedef Seçimi Kilitli

Bağlantı açıkken hedef kapsamı değiştirilemez. Önce **Bağlantıyı Kes**, sonra **Hedefleri Seç** düğmesiyle seçimi güncelleyin.

## Web Hedefi Açılmıyor

Kontrol edin:

- Hedef Hedef Merkezi'nde seçili mi?
- Domain özel hedefse pattern geçerli mi?
- Tarayıcı Windows PAC/proxy ayarını kullanıyor mu?
- Kurumsal policy veya manuel proxy ayarı PAC kullanımını engelliyor mu?
- QUIC/DoH/WebRTC gibi TCP CONNECT dışı bir yol zorlanıyor mu?

Astral web hedeflerinde tarayıcı exe'sini WireSock kapsamına almaz. Bu bilinçli güvenlik sınırıdır.

## Uygulama Hedefi Açılmıyor

Kontrol edin:

- Preset seçili mi?
- Özel EXE ise path mutlak ve gerçek `.exe` dosyası mı?
- Uygulama farklı bir launcher veya updater exe üzerinden mi çalışıyor?
- Tanılama paketinde `AllowedApps` özeti seçili hedefi gösteriyor mu?

## Proxy/PAC Temizlenmedi Şüphesi

Astral bağlantı kapanırken PAC/proxy state'ini geri alır. Şüphe varsa:

1. Astral'ı kapatıp tekrar açın.
2. **Profil Temizle** akışını çalıştırın.
3. Tanılama paketi oluşturun.

Manuel registry temizliği veya sistem proxy ayarı değiştirme işlemleri kullanıcı onayı ve dikkat gerektirir.

## Tanılama İçin Gerekli Bilgiler

Hata bildirirken şunları ekleyin:

- Astral sürümü.
- Windows sürümü.
- Seçili hedefler.
- Özel hedef kullanılıyorsa domain pattern veya redakte edilmiş exe adı.
- Redakte edilmiş tanılama paketi.

Özel anahtar, token, cookie, `wgcf-account.toml`, tam WireGuard profili veya kişisel veri paylaşmayın.
