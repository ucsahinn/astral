# Astral Güvenlik Sınırları

Astral'ın güvenlik hedefi, seçili hedefler dışında kalan trafiği kapsam dışında bırakmaktır. Bu nedenle sistem geneli VPN, tüm tarayıcı tüneli veya kalıcı proxy aracı gibi davranmaz.

## Trafik Kapsamı

- Uygulama hedefleri WireSock `AllowedApps` satırına dar kapsamla yazılır.
- Web hedefleri için `chrome.exe`, `msedge.exe`, `firefox.exe`, `brave.exe`, `opera.exe`, `vivaldi.exe` gibi genel tarayıcı süreçleri `AllowedApps` içine eklenmez.
- Web hedeflerinde yalnız `Astral.WebProxy.exe` WireSock kapsamına girer.
- PAC kuralı seçili domainlerde PROXY, diğer tüm domainlerde DIRECT döndürür.
- Eski ayarlarda kalan özel hedef alanları yeni sürümde route planına taşınmaz.
- WireSock varsayılan transparent mode ile çalışır; sanal adaptör oluşturan sistem geneli routing davranışı varsayılan akış değildir.

## TLS ve İçerik

- TLS MITM yoktur.
- Sertifika kurulmaz.
- HTTPS içeriği okunmaz veya çözülmez.
- Proxy sadece CONNECT host, HTTP Host ve domain allowlist kararını verir.

## Hedef Seçimi

- Kullanıcı yalnız hazır hedef kartlarını seçer.
- Seçim bağlantı başlamadan önce yapılır ve bağlantı açıkken kilitlenir.
- `TargetRegistry` desteklenmeyen veya eski ID'leri route planına almaz.
- Web hedefleri sadece kendi domain allowlist'iyle proxy edilir.

## İkili Dosyalar

- WireSock resmi kurucu hash, Authenticode imzası, yayıncı ve sürüm bilgisiyle doğrulanır.
- wgcf sabit SHA-256 özetiyle doğrulanır.
- Güncelleme paketi GitHub release yolu, asset digest, `.sha256.txt` ve manifest eşleşmesiyle doğrulanır.
- Arka plan videosu release paketi hazırlanırken sabit SHA-256 ile doğrulanır ve uygulama çalışma anında yalnız yerel `Assets/background.mp4` dosyasını oynatır.

## Log ve Tanılama

Loglarda şunlar yazılmamalıdır:

- WireGuard private key.
- Token, cookie veya credential.
- Tam hassas WireGuard profili.
- Kullanıcının gereksiz kişisel verisi.

Debug tanılama isteğe bağlıdır. Normal tanılama paketi hafif tutulur ve redaksiyon uygular.
`wgcf` gibi yardımcı araçların hata çıktıları exception ve log yoluna girmeden önce kısaltılır ve anahtar/token/cookie/Authorization benzeri değerler redakte edilir.

## Rollback

PAC/proxy state'i uygulanmadan önce Astral sahiplik marker'ı ile state dosyasına alınır. Bağlantı kesilince, hata alınca, WireSock beklenmedik kapanınca veya uygulama kapanınca restore akışı çalışır. Kullanıcı veya policy bu sırada proxy ayarını değiştirmişse Astral yalnız kendi uyguladığı PAC değerini geri alır.

## Sınırlar

- Presetler erişim durumunu garanti etmez.
- Browser policy, DoH, QUIC, UDP/WebRTC veya manuel proxy ayarları davranışı etkileyebilir.
- Kurumsal proxy/PAC ortamlarında kullanıcı ek doğrulama yapmalıdır.
