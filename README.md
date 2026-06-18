# Astral

<p align="center">
  <img src="src/Astral.App/Assets/astral-mark.png" alt="Astral uygulama ikonu" width="118">
</p>

<p align="center">
  <strong>Windows için seçili uygulama ve web hedeflerine odaklanan dar kapsamlı bağlantı yöneticisi.</strong>
</p>

<p align="center">
  <a href="https://github.com/ucsahinn/astral/releases"><img alt="GitHub release" src="https://img.shields.io/github/v/release/ucsahinn/astral?display_name=tag&style=for-the-badge"></a>
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%2B-2f81f7?style=for-the-badge&logo=windows">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8-7c3aed?style=for-the-badge&logo=dotnet">
  <img alt="Portable-ZIP" src="https://img.shields.io/badge/Portable-ZIP-22c55e?style=for-the-badge">
</p>

<p align="center">
  <a href="https://github.com/ucsahinn/astral/releases">İndir</a>
  · <a href="docs/kullanim.md">Kullanım</a>
  · <a href="docs/guncelleme.md">Güncelleme</a>
  · <a href="docs/guvenlik.md">Güvenlik</a>
  · <a href="docs/sorun-giderme.md">Sorun giderme</a>
</p>

Astral, Windows'ta yalnızca kullanıcının seçtiği uygulama veya web hedefleri için bağlantı kapsamı oluşturan portable bir masaüstü uygulamasıdır. Tüm PC'yi, tüm tarayıcıyı veya sistem genelini VPN'e almayı amaçlamaz.

Uygulama hedefleri WireSock `AllowedApps` satırına dar kapsamla yazılır. Web hedeflerinde genel tarayıcı süreçleri profile eklenmez; seçili domainler `Astral.WebProxy.exe` ve PAC allowlist üzerinden yönlendirilir, diğer domainler DIRECT kalır.

Astral resmi bir Discord, Cloudflare, WireSock veya diğer marka sahibi ürünü değildir. Presetler yalnızca hedef kapsamını tanımlar; erişim durumu zamanla değişebilir.

## Ekran Görüntüleri

**Ana ekran - bağlantı kapalı**

![Astral Windows ana ekranı ve bağlantı kapalı durumu](docs/assets/screenshots/astral-windows-ana-ekran.png)

**Bağlantı açık ekranı**

![Astral Windows bağlantı açık durumu ve uygulama kapsamı](docs/assets/screenshots/astral-baglanti-aktif-ekrani.png)

## Astral Nedir?

Astral, seçili hedef/preset tabanlı bir bağlantı yöneticisidir. Kullanıcı app/web ayrımıyla uğraşmadan hedef kartlarını seçer; Astral seçimin uygulama, web veya karma kapsamını kendisi çözer.

Başlangıç presetleri:

- Discord: Uygulama + Web
- Wattpad: Web
- Bigo Live: Web
- Azar: Uygulama + Web
- Tango: Uygulama + Web
- LiVU: Uygulama + Web
- IMVU: Uygulama + Web
- Blogspot: Web

## Ne İşe Yarar?

| Özellik | Kullanıcıya etkisi |
| --- | --- |
| Tek tuşla bağlantı | Seçili hedefler için bağlantıyı açıp kapatmayı kolaylaştırır. |
| İlk ekranda hedef kartları | Discord, Wattpad ve diğer hazır hedefler ana ekrandan seçilir. |
| Domain kapsamı | Web hedeflerinde yalnızca seçili domainler Astral.WebProxy üzerinden gider; diğer domainler DIRECT kalır. |
| Canlı durum kartları | DNS, bağlantı durumu ve hedef kapsamını sade biçimde gösterir. |
| Tanılama paketi | Bağlantı sorunlarını incelemek için paylaşıma uygun rapor hazırlar. |
| Portable kullanım | Kurulum sihirbazı olmadan ZIP içinden çalışır. |
| Güncelleme akışı | Yeni sürümü arka planda sessizce denetler; yalnız yeni sürüm varsa **Güncelle** düğmesini gösterir. |

## Neden Güvenli?

- Sistem DNS ayarını kalıcı değiştirmez.
- Genel tarayıcı exe'lerini WireSock `AllowedApps` kapsamına almaz.
- HTTPS içeriğini çözmez, TLS MITM yapmaz ve sertifika kurmaz; proxy yalnızca CONNECT host/Host allowlist kontrolü yapar.
- WireSock ve wgcf ikili dosyalarını repoya gömmez; release paketindeki WireSock fallback kurucusu varsa hash, imza, yayıncı ve sürümle doğrulanır.
- Otomatik güncelleme paketini GitHub release asset bilgisi, `.sha256.txt`, GitHub digest ve manifest kontrolleriyle eşleştirir.
- Gizli profil, hesap ve log dosyalarını repoya veya release arşivine eklemez.

Daha teknik sınırlar için [güvenlik dokümanına](docs/guvenlik.md) ve [SECURITY.md](SECURITY.md) dosyasına bakın.

## Hızlı Başlangıç

1. [GitHub Releases](https://github.com/ucsahinn/astral/releases) sayfasından en güncel `Astral-win-x64.zip` arşivini indirin.
2. ZIP içeriğini istediğiniz klasöre çıkarın.
3. `Astral.exe` dosyasını çalıştırın.
4. İlk kullanım ekranında WireSock ve WARP koşullarını okuyup onaylayın.
5. Ana ekrandaki hedef kartlarından kapsamı belirleyin.
6. Ana ekranda **Bağlan** düğmesine basın.

Release sayfasındaki ZIP paketini manuel indirdiğinizde yanında verilen SHA-256 dosyasıyla kontrol etmeniz önerilir. Uygulama içi otomatik güncelleme zinciri GitHub release yolu, asset digest, SHA-256 dosyası ve manifest eşleşmesi olmadan paketi uygulamaz.

## Doküman Haritası

| Konu | Bağlantı |
| --- | --- |
| Kullanım ve ilk çalıştırma | [docs/kullanim.md](docs/kullanim.md) |
| Güncelleme ve portable ZIP davranışı | [docs/guncelleme.md](docs/guncelleme.md) |
| Güvenlik sınırları | [docs/guvenlik.md](docs/guvenlik.md) |
| Sorun giderme | [docs/sorun-giderme.md](docs/sorun-giderme.md) |
| Mimari | [docs/mimari.md](docs/mimari.md) |
| Kaynak sorun denetimi | [docs/kaynak-sorun-denetimi.md](docs/kaynak-sorun-denetimi.md) |
| v2.2.19 release notu | [docs/releases/v2.2.19.md](docs/releases/v2.2.19.md) |

## Geliştirme

```powershell
dotnet build Astral.sln --configuration Release
dotnet run --project tests\Astral.Core.Tests --configuration Release
dotnet run --project tests\Astral.Windows.Tests --configuration Release
.\scripts\verify.ps1
```

Release paketi yerel olarak hazırlanacaksa:

```powershell
.\scripts\build-release.ps1
```

## Destek ve Güvenlik

Hata bildirirken Astral sürümünü, Windows sürümünü, seçili hedefleri ve redakte edilmiş tanılama paketini ekleyin. Özel anahtar, token, cookie, `wgcf-account.toml`, tam WireGuard profili veya kişisel veri paylaşmayın.

Güvenlik açığı bildirmek için [GitHub Security Advisory](https://github.com/ucsahinn/astral/security/advisories/new) kullanın.

## Lisans ve Üçüncü Taraf Notu

Astral kaynak kodu bu repodaki [LICENSE](LICENSE) koşullarıyla yayınlanır. WireSock, Cloudflare WARP, Discord, Wattpad, Bigo Live, Azar, Tango, LiVU, IMVU, Blogspot ve diğer marka/adlar kendi sahiplerine aittir. Üçüncü taraf sınırları için [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) dosyasına bakın.
