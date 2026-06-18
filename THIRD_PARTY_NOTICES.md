# Üçüncü Taraf Bildirimleri

Astral, seçili uygulama veya web hedefleri için bazı üçüncü taraf ürün ve projelerle birlikte çalışır. Bu dosya paketlenen değil, çalışma zamanında kullanılan bileşenleri açıklar.

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

## Marka ve Servis Adları

Discord, Wattpad, Bigo Live, Azar, Tango, LiVU, IMVU, Blogspot, Cloudflare ve WireSock adları ilgili sahiplerine aittir. Astral bağımsız bir bağlantı kapsamı yöneticisidir; bu servisler veya marka sahipleri tarafından onaylanmış resmi bir ürün değildir.

Ana ekrandaki hedef kapsamı kartları için `src/Astral.App/Assets/targets` altında renkli hedef ikonları bundle edilir.

- `discord.svg`, `wattpad.svg` ve `blogspot.svg`: Simple Icons SVG kaynaklarından alınmıştır (`https://cdn.simpleicons.org/discord`, `https://cdn.simpleicons.org/wattpad`, `https://cdn.simpleicons.org/blogger`). Simple Icons projesi ikon verilerini CC0 1.0 altında yayımlar; marka ve ticari marka hakları ilgili sahiplerinde kalır.
- `azar.svg`, `bigo-live.svg`, `imvu.svg`, `livu.svg` ve `tango.svg`: İlgili alan adlarının herkese açık favicon/app ikonlarından oluşturulmuş SVG wrapper dosyalarıdır. Kaynak URL biçimi `https://www.google.com/s2/favicons?domain=<alan-adi>&sz=128`; kullanılan alan adları sırasıyla `azarlive.com`, `bigo.tv`, `imvu.com`, `livu.me` ve `tango.me` şeklindedir. Bu ikonlar yalnızca hedef seçimini tanıtmak için kullanılır; Astral bu servisler veya marka sahipleri tarafından onaylanmış resmi bir ürün değildir.
