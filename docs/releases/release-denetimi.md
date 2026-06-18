# Astral Release ve Tag Denetimi

Son denetim: 2026-06-18
Son cleanup: 2026-06-14

Bu doküman, GitHub Releases yüzeyindeki sürüm kararlarını ve yayın kapısını kısa biçimde tutar. Release/tag/asset silme veya `--cleanup-tag` kullanımı yıkıcı işlem sayılır; yalnız açık cleanup onayıyla yapılır.

## İnceleme Kaynakları

- `git tag --list`
- `git ls-remote --tags origin`
- `gh release list --repo ucsahinn/astral --limit 100`
- Yerel release notları ve build script'leri
- `scripts/verify.ps1`
- `scripts/build-release.ps1`

## Güncel Sürüm Kararı

Güncel patch hedef `v2.2.16` olarak belirlendi.

Neden `v2.2.16`?

- v2.2.15 üstüne hedef kartları global `Connected` durumunu tek başına başarı kanıtı saymayacak şekilde sertleştirildi.
- Bağlantı hazır olunca seçili hedefler otomatik test kuyruğuna alınır; manuel hızlı test aynı per-target ölçümü tekrar çalıştırır.
- Bir hedef sorunluyken diğer hedeflerin OK kalabilmesi için kapsam özeti ve kart durumu hedef bazlı sayımla güncellendi.
- Tanılama ve sürüm notu aksiyonları görünür etiketlerle ayrıldı; release notes butonu artık log klasörü aksiyonu gibi görünmez.
- v2.2.15 yayınlandığı için bu UI/kanıt hotfix'i ayrı `v2.2.16` sürümü olarak çıkarılır; böylece 2.2.15 kullanıcıları da otomatik güncelleme görür.

## Public Release Yüzeyi

| Release | Canlı durum | Asset durumu | Karar | Gerekçe |
| --- | --- | --- | --- | --- |
| `v2.2.16` | Yayın adayı | Bekliyor | Yayınla | Kanıtlı hedef kartı durumu, otomatik hedef testi ve aksiyon barı netliği hotfixleri. |
| `v2.2.15` | Yayında | 4 asset | Koru | WireSock tunnel-started kapısı, ortak log kilidi, hedef hızlı testi ve tanılama hotfixleri. |
| `v2.2.14` | Yayında | 4 asset | Koru | Scoped PAC doğrulama, kullanıcı alanı PAC dosyası, hedef hızlı testi ve tanılama hotfixleri. |
| `v2.2.13` | Tarihsel aday | Yerine geçti | Koru | Plain `AllowedApps`, normal internet DIRECT smoke ve scope hardening hotfixleri. |
| `v2.2.12` | Tarihsel aday | Yerine geçti | Koru | Live smoke sınıflandırması, WireSock transparent readiness ve `#@ws:AllowedApps` hotfixleri. |
| `v2.2.11` | Tarihsel aday | Yerine geçti | Koru | WireSock transparent readiness, `#@ws:AllowedApps` ve WebProxy orphan cleanup hotfixleri. |
| `v2.2.10` | Tarihsel aday | Yerine geçti | Koru | Gerçek hedef ikonları, state sistemi, lifecycle hotfixleri ve Astral-only update hattı. |
| `v2.2.9` | Tarihsel aday | Yerine geçti | Koru | İsimli 5+4 hedef kartları ve geçiş release notu v2.2.10 tarafından genişletildi. |
| `v2.2.8` | Yayında | 4 asset | Koru | Icon-only hedef grid, legacy tanılama temizliği ve güncelleme sürekliliği. |
| `v2.2.7` | Yayında | 4 asset | Koru | WebProxy port fallback, proxy readiness doğrulaması ve isimli hedef kartları hotfixleri. |
| `v2.2.6` | Yayında | 4 asset | Koru | WireSock/WebProxy ortak log paylaşımı ve hedef paneli hotfixleri. |
| `v2.2.5` | Yayında | 4 asset | Koru | Access-lock disable dayanıklılığı, seçili hedef koruması ve hedef kartı okunurluğu hotfixleri. |
| `v2.2.4` | Yayında | 4 asset | Koru | Inline hedef kartları, Astral-only release assetleri ve update görünürlüğü hotfixleri. |
| `v2.2.3` | Yayında | 8 asset | Koru | Önceki hedef seçim yüzeyi polish, WireSock readiness ve DNS flush hotfix. |
| `v2.2.2` | Yayında | 8 asset | Koru | Geçiş dönemi köprü assetleri ve premium ikon seti. |
| `v2.2.1` | Yayında | 4 asset | Koru | Web-only hedef süreci, ürün dili ve release workflow idempotency düzeltmesi. |
| `v2.2.0` | Yayında | 4 asset | Koru | Seçilebilir hedef kapsamı ve Astral.WebProxy mimarisi. |
| `v2.1.6` ve öncesi | Yayında | Değişken | Koru | Önceki public baseline sürümleri. |

## Yayın Kapısı

`v2.2.16` yayınından önce tamamlanması gereken koşullar:

- Proje, updater ve web proxy sürümü `2.2.16` ile aynı.
- `src/Astral.App/app.manifest` kimlik sürümü proje sürümüyle aynı.
- `Astral-2.2.16-win-x64.zip` ve sabit `Astral-win-x64.zip` aynı içeriği gösterir.
- ZIP içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, update manifest'i ve gerekli runtime dosyaları bulunur.
- ZIP içindeki update manifest sürümü `2.2.16`.
- Astral release içinde eski isimli uyumluluk ZIP'i yayınlanmaz.
- Kod imzalama sertifikası yapılandırılmadıysa paket imzasız olduğu açıkça not edilir.
- `dotnet build`, Core tests, Windows tests, `scripts/verify.ps1`, `scripts/build-release.ps1`, `git diff --check` ve Gitleaks geçer.
- GitHub Release assetleri ve SHA-256 dosyaları yayınlandıktan sonra erişilebilir olarak doğrulanır.

## Eski İstemci Geçişi

Eski istemcilerin beklediği eski paket adı, ana Astral release hattında üretilmez. Geçiş için ayrı köprü repo/yayın yüzeyi kullanılabilir. Bu köprü geçiş dönemi tamamlandıktan sonra ayrıca kaldırılabilir; Astral kaynak paketi ve updater yolu kalıcı olarak `Astral-*` assetlerine bağlıdır.

## Cleanup Notu

2026-06-14 tarihinde verilen `APPROVED - CLEAN RELEASES` onayı sonrasında ara deneme/hotfix yayınları temizlendi ve public release yüzeyi anlamlı milestone sürümlere indirildi. Yeni 2.2.x çalışması kapsamında eski release veya tag silinmeyecek.
