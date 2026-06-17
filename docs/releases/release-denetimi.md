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

Güncel patch hedef `v2.2.3` olarak belirlendi.

Neden `v2.2.3`?

- Ürün davranışı Discord merkezli tek hedef yapısından seçilebilir hedef/preset
  tabanlı bağlantı yöneticisine taşındı.
- Web hedefleri için genel tarayıcı süreçleri WireSock `AllowedApps` kapsamına
  eklenmiyor; seçili domainler `Astral.WebProxy` ve PAC allowlist üzerinden
  taşınıyor.
- Custom EXE ve Custom Domain doğrulaması eklendi.
- UI'da hedef seçimi ayrı Hedef Merkezi penceresine taşındı.
- `v2.2.1` canlı yayına çıktıktan sonra eski Discorder 2.1.x istemcilerinin
  `Discorder-<version>-win-x64.zip` asset'i beklediği görüldü.
- Güncelleme düğmesi yeni sürüm yokken tamamen kaybolduğu için kullanıcı
  elle denetleme veya hata durumunu göremiyordu.
- Astral marka ikon seti premium A işaretiyle yenilendi.
- Hedef Merkezi kategori paneli olmadan daha geniş kart grid'i, premium servis
  işaretleri ve daha net seçili durum/focus state ile yenilendi.
- WireSock hazırlık denetimi `wt*` + `WireGuard Tunnel` adaptörlerini tanırken
  ilgisiz `wg-*` adaptör adlarını kapsam dışında tutacak şekilde daraltıldı.
- Windows Firewall bağlantı kilidi kapanışındaki DNS flush adımı, boş native
  çıkış kodunu kritik PowerShell hatasına çevirmeyecek şekilde hata toleranslı
  hale getirildi.

## Public Release Yüzeyi

| Release | Canlı durum | Asset durumu | Karar | Gerekçe |
| --- | --- | --- | --- | --- |
| `v2.2.3` | Yayın adayı | Bekliyor | Yayınla | Hedef Merkezi polish, WireSock readiness ve DNS flush hotfix. |
| `v2.2.2` | Yayında | 8 asset | Koru | Güncelleme düğmesi, Discorder köprü assetleri ve premium ikon seti. |
| `v2.2.1` | Yayında | 4 asset | Koru | Web-only hedef süreci, ürün dili ve release workflow idempotency düzeltmesi. |
| `v2.2.0` | Yayında | 4 asset | Koru | Seçilebilir hedef kapsamı ve Astral.WebProxy mimarisi. |
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

`v2.2.3` yayınından önce tamamlanması gereken koşullar:

- Proje, updater ve web proxy sürümü `2.2.3` ile aynı.
- `Astral-2.2.3-win-x64.zip` ve sabit `Astral-win-x64.zip` aynı içeriği gösterir.
- `Discorder-2.2.3-win-x64.zip` ve sabit `Discorder-win-x64.zip` aynı içeriği gösterir.
- ZIP içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, update
  manifest'i ve gerekli runtime dosyaları bulunur.
- Discorder köprü ZIP'i `Discorder.exe`, `Discorder.Updater.exe` ve
  `discorder.update-manifest.json` içerir.
- ZIP içindeki update manifest sürümü `2.2.3`.
- Kod imzalama sertifikası yapılandırılmadıysa paket imzasız olduğu açıkça not edilir.
- `dotnet build`, Core tests, Windows tests, `scripts/verify.ps1`,
  `scripts/build-release.ps1`, `git diff --check` ve Gitleaks geçer.
- GitHub Release assetleri ve SHA-256 dosyaları yayınlandıktan sonra erişilebilir
  olarak doğrulanır.

## Cleanup Notu

2026-06-14 tarihinde verilen `APPROVED - CLEAN RELEASES` onayı sonrasında ara
deneme/hotfix yayınları temizlendi ve public release yüzeyi anlamlı milestone
sürümlere indirildi. Yeni 2.2.x çalışması kapsamında eski release veya tag
silinmeyecek.
