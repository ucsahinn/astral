# Üçüncü Taraf Bildirimleri

Discorder, Discord trafiğini tünellemek için bazı üçüncü taraf ürün ve projelerle birlikte çalışır. Bu dosya paketlenen değil, çalışma zamanında kullanılan bileşenleri açıklar.

## WireSock VPN Client

- Üretici: NT KERNEL
- Kullanılan sürüm: `1.4.7.1`
- Dağıtım biçimi: Repoya eklenmez. Yayın arşivinde doğrulanmış fallback kurucu olarak yer alabilir.
- Kullanım biçimi: İlk bağlantıda mevcut güvenilir kurulum yeniden kullanılır. Kurulum yoksa release arşivindeki yerel MSI fallback'i veya resmi indirme SHA-256, Authenticode, yayıncı ve sürüm bilgisiyle doğrulanır; ardından kullanıcı onayıyla çalıştırılır.

WireSock ayrı bir üründür. Ticari veya kurumsal kullanım lisans gerektirebilir. Lisans uygunluğunu kontrol etmek kullanıcının sorumluluğundadır.

## wgcf

- Proje: `ViRb3/wgcf`
- Kullanılan sürüm: `2.2.31`
- Dağıtım biçimi: Repoya veya yayın arşivine eklenmez.
- Kullanım biçimi: İlk profil üretiminde resmi GitHub yayınından indirilir ve SHA-256 ile doğrulanır.

wgcf, Cloudflare tarafından üretilen veya desteklenen resmi bir araç değildir. Cloudflare WARP koşulları kullanıcıya ilk kurulum ekranında gösterilir.

## Discord ve Cloudflare

Discord ve Cloudflare adları ilgili sahiplerine aittir. Discorder bağımsız bir uyumluluk aracıdır; Discord, Cloudflare veya WireSock tarafından onaylanmış resmi bir ürün değildir.
