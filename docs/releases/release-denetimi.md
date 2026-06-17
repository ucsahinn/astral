# Astral Release ve Tag Denetimi

Son denetim: 2026-06-17
Son cleanup: 2026-06-14

Bu doküman, GitHub Releases yüzeyindeki sürüm kararlarını ve yayın kapısını
kısa biçimde tutar. Release/tag/asset silme veya `--cleanup-tag` kullanımı
yıkıcı işlem sayılır; yalnız açık cleanup onayıyla yapılır.

## İnceleme Kaynakları

- `git tag --list`
- `git ls-remote --tags origin`
- `gh release list --repo ucsahinn/astral --limit 100`
- Yerel release notları ve build script'leri
- `scripts/verify.ps1`
- `scripts/build-release.ps1`

## Güncel Sürüm Kararı

Yeni minor hedef `v2.2.0` olarak belirlendi.

Neden `v2.2.0`?

- Ürün davranışı Discord merkezli tek hedef yapısından seçilebilir hedef/preset
  tabanlı bağlantı yöneticisine taşındı.
- Web hedefleri için genel tarayıcı süreçleri WireSock `AllowedApps` kapsamına
  eklenmiyor; seçili domainler `Astral.WebProxy` ve PAC allowlist üzerinden
  taşınıyor.
- Custom EXE ve Custom Domain doğrulaması eklendi.
- UI'da hedef seçimi ayrı Hedef Merkezi penceresine taşındı.
- Bu değişiklik patch değil, geriye uyumlu ancak görünür ürün kapsamını genişleten
  minor sürümdür.

## Public Release Yüzeyi

| Release | Canlı durum | Asset durumu | Karar | Gerekçe |
| --- | --- | --- | --- | --- |
| `v2.2.0` | Yayın adayı | Bekliyor | Yayınla | Seçilebilir hedef kapsamı ve Astral.WebProxy mimarisi. |
| `v2.1.6` | Yayında | 4 asset | Koru | Önceki patch baseline. |
| `v2.1.5` | Yayında | 4 asset | Koru | Önceki patch baseline. |
| `v2.1.4` | Yayında | 4 asset | Koru | Önceki patch baseline. |
| `v2.1.3` | Yayında | 4 asset | Koru | Önceki patch baseline. |
| `v2.1.2` | Yayında | 4 asset | Koru | Önceki patch baseline. |
| `v2.1.1` | Yayında | 4 asset | Koru | Önceki patch baseline. |
| `v2.1.0` | Yayında | 4 asset | Koru | Önceki minor baseline. |
| `v2.0.30` | Yayında | 4 asset | Koru | 2.0 hattı milestone'u. |
| `v2.0.24` | Yayında | 2 asset | Koru | 2.0 hattı milestone'u. |
| `v2.0.20` | Yayında | 2 asset | Koru | 2.0 hattı milestone'u. |
| `v2.0.14` | Yayında | 2 asset | Koru | 2.0 hattı milestone'u. |
| `v2.0.0` | Yayında | 2 asset | Koru | 2.x hattının başlangıcı. |

## Yayın Kapısı

`v2.2.0` yayınından önce tamamlanması gereken koşullar:

- Proje, updater ve web proxy sürümü `2.2.0` ile aynı.
- `Astral-2.2.0-win-x64.zip` ve sabit `Astral-win-x64.zip` aynı içeriği gösterir.
- ZIP içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, update
  manifest'i ve gerekli runtime dosyaları bulunur.
- ZIP içindeki update manifest sürümü `2.2.0`.
- Kod imzalama sertifikası yapılandırılmadıysa paket imzasız olduğu açıkça not edilir.
- `dotnet build`, Core tests, Windows tests, `scripts/verify.ps1`,
  `scripts/build-release.ps1`, `git diff --check` ve Gitleaks geçer.
- GitHub Release assetleri ve SHA-256 dosyaları yayınlandıktan sonra erişilebilir
  olarak doğrulanır.

## Cleanup Notu

2026-06-14 tarihinde verilen `APPROVED - CLEAN RELEASES` onayı sonrasında ara
deneme/hotfix yayınları temizlendi ve public release yüzeyi anlamlı milestone
sürümlere indirildi. Yeni 2.2.0 çalışması kapsamında eski release veya tag
silinmeyecek.
