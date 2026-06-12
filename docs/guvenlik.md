# Discorder Güvenlik Sınırları

Discorder'ın güvenlik modeli dar kapsam, doğrulanmış indirme ve kullanıcı onayı üzerine kuruludur.

## Güven Sınırı

Discorder şu bileşenlere güvenir:

- Windows işletim sistemi ve Authenticode doğrulaması.
- GitHub Releases üzerinden yayınlanan Discorder paketleri.
- Resmi WireSock VPN Client kurucusu.
- Cloudflare WARP profil üretimi için kullanılan wgcf akışı.

Bu bileşenlerden gelen içerik doğrulanmadan uygulanmaz.

## İndirilen Dosyalar

WireSock kurucusu resmi kaynaktan alınır ve şu kontrollerden geçer:

- SHA-256 hash doğrulaması.
- Authenticode imza zinciri.
- Beklenen yayıncı adı.
- Beklenen ürün ve sürüm bilgisi.

Discorder'ın uygulama içi otomatik güncelleme paketleri için hash, manifest ve imza kontrolleri kullanılır. Manuel indirilen GitHub Release ZIP'lerinde kullanıcı ayrıca yayınlanan SHA-256 dosyasını kontrol etmelidir.

## Ne Yapmaz?

Discorder bilinçli olarak şunları yapmaz:

- TLS doğrulamasını kapatma.
- İmza veya hash hatasını yoksayma.
- WireSock ya da wgcf ikili dosyalarını repoya gömme.
- Discord dışı uygulamaları sessizce kapsama alma.
- Sistem DNS'ini kalıcı değiştirme.
- Kullanıcının gizli profil veya hesap dosyalarını release paketine ekleme.

## Kullanıcı Verisi

Yerel ayar, log, profil ve tanılama dosyaları kullanıcının makinesinde kalır:

```text
%LOCALAPPDATA%\Discorder
```

Tanılama paketi paylaşmadan önce özel anahtar, token, cookie, tam profil dosyası veya kişisel veri içermediğini kontrol edin.

## Public Screenshot ve Doküman Kuralı

Repo görselleri ve dokümanları şu verileri içermemelidir:

- Yerel kullanıcı adı veya tam yerel yol.
- Token, cookie, private key veya bağlantı dizesi.
- Redakte edilmemiş log.
- Gizli IP, müşteri verisi veya hesap bilgisi.

Bu nedenle README'deki ana ekran görselinde canlı DNS değerleri maskelenmiştir.

## Güvenlik Açığı Bildirme

Güvenlik açığını public issue olarak paylaşmayın. GitHub Security Advisory kullanın:

<https://github.com/ucsahinn/discorder/security/advisories/new>
