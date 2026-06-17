# Astral Kullanım Rehberi

Astral ZIP içinden çalışan portable bir Windows uygulamasıdır. Seçili uygulama veya web hedefleri için dar kapsamlı bağlantı planı oluşturur; tüm bilgisayarı veya tüm tarayıcıyı tünellemez.

## İlk Çalıştırma

1. `Astral-win-x64.zip` arşivini release sayfasından indirin.
2. ZIP içeriğini sabit bir klasöre çıkarın.
3. `Astral.exe` dosyasını yönetici yetkisiyle çalıştırın.
4. İlk kurulum ekranında WireSock ve Cloudflare WARP koşullarını okuyup onaylayın.
5. Ana ekranda **Hedefleri Seç** düğmesini açın.
6. Hedef Merkezi'nde presetleri veya özel hedefleri seçip **Kaydet** deyin.
7. Ana ekranda **Bağlan** düğmesine basın.

## Hedef Merkezi

Hedef Merkezi ana ekrana gömülü değildir; ayrı modal olarak açılır. Arama alanı ve kategorilerle şu hedefleri seçebilirsiniz:

- Discord
- Roblox
- Wattpad
- Bigo Live
- Azar
- Tango
- LiVU
- IMVU
- Blogspot
- Özel EXE
- Özel Domain

Kartlardaki “Uygulama + Web”, “Web”, “Uygulama” ve “Özel” etiketleri yalnız kapsam türünü anlatır. Erişim durumunu, çalışma garantisini veya servislerin güncel durumunu iddia etmez.

## Bağlanma Davranışı

Bağlantı kapalıyken:

- Ana durum **Astral Bağlı Değil** görünür.
- Seçili hedefler düz bağlantıya çıkmaz.
- Diğer trafik normal bağlantıda kalır.

Bağlantı açıkken:

- Ana durum **Astral Bağlı** görünür.
- Sadece seçili hedefler tünelden çıkar.
- Hedef seçimi bu oturumda kilitlenir; değiştirmek için önce bağlantıyı kesin.

## Web Hedefleri

Web hedeflerinde genel tarayıcı süreçleri WireSock profiline eklenmez. Astral bunun yerine:

1. Seçili domainler için PAC kuralı üretir.
2. `Astral.WebProxy.exe` sürecini allowlist ile başlatır.
3. WireSock `AllowedApps` satırına tarayıcıyı değil yalnız `Astral.WebProxy.exe` sürecini ekler.
4. PAC içinde seçili domainlere PROXY, diğer tüm domainlere DIRECT döndürür.

HTTPS içeriği çözülmez. Astral sertifika kurmaz, TLS MITM yapmaz ve sayfa içeriğini okumaz.

## Özel Hedefler

Özel EXE:

- Mutlak `.exe` yolu olmalıdır.
- Dosya gerçekten var olmalıdır.
- Reparse point veya genel tarayıcı exe'si olamaz.

Özel Domain:

- `example.com` veya `*.example.com` biçiminde olmalıdır.
- URL, port, IP adresi, `*`, `*.com` gibi geniş patternler reddedilir.
- IDN domainler punycode'a normalize edilir.

## Profil Temizle

**Profil Temizle** portable ZIP klasörünü veya seçtiğiniz uygulamaları silmez. Astral'ın ürettiği profil, WireSock/WARP çalışma state'i, bağlantı kilidi ve yönetilen proxy/PAC state'i temizlenir. WireSock Astral tarafından kurulmuşsa kaldırma akışı Windows onayı isteyebilir.

## Tanılama

**Tanılama** düğmesi redakte edilmiş bir paket üretir. Paket paylaşılırken özel anahtar, token, cookie, tam WireGuard profili veya kişisel veri eklemeyin.
