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

Güncel patch hedef `v2.2.25` olarak belirlendi.

Neden `v2.2.25`?

- v2.2.24 release asset'i yayınlandıktan sonra arka plan video paketinin yerel doğrulanan build ile aynı olmadığı görüldü; aynı sürüm numarasına tekrar güncelleme yapılamayacağı için düzeltme zorunlu olarak `v2.2.25` hotfix'ine taşındı.
- v2.2.25, istenen yeni arka plan videosunu SHA-256 doğrulamalı repo-local `Assets/background.mp4` olarak paketler; release build ve runtime uzak video fallback'i kullanmaz.
- v2.2.21 tanı paketleri WireSock sürecinin ve Astral.WebProxy/PAC kapsamının başladığını, ancak transparent modda sanal adapter `Down` kaldığı için pasif readiness kapısının bağlantıyı yanlış başarısız saydığını gösterdi.
- WireSock transparent mode sanal adapter `Up` kanıtına bağlı değildir; web hedefleri için doğru aktif kanıt, yalnız `Astral.WebProxy.exe` üzerinden seçili hedef hostlarına yapılan scoped `CONNECT` denemesidir.
- v2.2.23, PAC uygulandıktan sonra seçili her web hedefi için scoped WebProxy kanıtı alır ve bu kanıtı transparent readiness kararına dahil eder.
- Tarayıcı executable'ları yine WireSock `AllowedApps` içine girmez; proof trafiği yalnız `Astral.WebProxy.exe` sürecinden çıkar.
- v2.2.21 yayınlandığı için bu bağlantı doğrulama düzeltmesi ayrı `v2.2.22` hotfix sürümü olarak çıkarılır; böylece 2.2.21 kullanıcıları otomatik güncelleme görür.
- v2.2.22 yayınlandıktan sonra tek başarılı hostun yeterli olması daraltıldı; `v2.2.23` tüm seçili web hedefleri için hedef bazlı kanıt ister.
- v2.2.25, hedef listeli canlı smoke proof kapısı, 16 preset, karttan web açma, yeni arka plan videosu ve Astral VPN ürün yüzeyiyle bu hattı tamamlar.

## Public Release Yüzeyi

| Release | Canlı durum | Asset durumu | Karar | Gerekçe |
| --- | --- | --- | --- | --- |
| `v2.2.25` | Aday | Bekleniyor | Yayına hazırsa oluştur | Yeni arka plan video paketi, hedef linkleri, Astral VPN ürün yüzeyi ve hedef listeli canlı proof kapısı. |
| `v2.2.24` | Yayında | 4 asset | v2.2.25 ile üstlen | Yayın asset'i yerel doğrulanan yeni video build'iyle eşleşmediği için aynı sürüm yerine v2.2.25 hotfix'iyle ilerlenir. |
| `v2.2.23` | Yayında | 4 asset | Koru | Transparent mode için seçili her web hedefinde scoped WebProxy aktif hedef kanıtı, tanı alanları ve kısmi başarıyı bağlı saymama hotfix'i. |
| `v2.2.22` | Yayında | 4 asset | Koru | Transparent mode için scoped WebProxy aktif hedef kanıtı, tanı alanları ve v2.2.21 bağlantı başarısızlığı hotfix'i; v2.2.23 hedef bazlı kanıtla tamamlanır. |
| `v2.2.21` | Yayında | 4 asset | Koru | Onarım timeout ayrımı, WireSock motor yenileme yolu, son süreç çıkışı tanısı ve hedef kartı rozet/layout hotfix'i; v2.2.22 tarafından scoped proof readiness ile tamamlanır. |
| `v2.2.20` | Yayında | 4 asset | Koru | WireSock profil adı uyumluluğu, stabil probe hostları, logo ağırlıklı hedef kartları ve işletim merkezi layout hotfix'i; v2.2.21 tarafından onarım timeout'u giderilir. |
| `v2.2.19` | Yayında | 4 asset | Koru | Sabit ana pencere boyutu, sade kapsam durumu, restart timeout toleransı, açılışta bağımsız güncelleme denetimi ve WebProxy DNS fallback hotfixleri; v2.2.20 tarafından WireSock profil regresyonu giderildi. |
| `v2.2.18` | Yayında | 4 asset | Koru | Çoklu host rota testi, ek hedef hostları, WebProxy denied-host tanılaması ve Geçmiş butonu kaldırma hotfixleri. |
| `v2.2.17` | Yayında | 4 asset | Koru | WireSock trafik kanıtı, 8 hedef kapsamı, otomatik hedef testi ve aksiyon barı netliği hotfixleri. |
| `v2.2.16` | Yayında | 4 asset | Koru | Kanıtlı hedef kartı durumu, otomatik hedef testi ve aksiyon barı netliği hotfixleri. |
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

`v2.2.25` yayınından önce tamamlanması gereken koşullar:

- Proje, updater ve web proxy sürümü `2.2.25` ile aynı.
- `src/Astral.App/app.manifest` kimlik sürümü proje sürümüyle aynı.
- `Astral-2.2.25-win-x64.zip` ve sabit `Astral-win-x64.zip` aynı içeriği gösterir.
- ZIP içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, update manifest'i ve gerekli runtime dosyaları bulunur.
- ZIP içindeki update manifest sürümü `2.2.25`.
- Astral release içinde eski isimli uyumluluk ZIP'i yayınlanmaz.
- Kod imzalama sertifikası yapılandırılmadıysa paket imzasız olduğu açıkça not edilir.
- `dotnet build`, Core tests, Windows tests, `scripts/verify.ps1`, `scripts/build-release.ps1`, `git diff --check` ve Gitleaks geçer.
- GitHub Release assetleri ve SHA-256 dosyaları yayınlandıktan sonra erişilebilir olarak doğrulanır.

## Eski İstemci Geçişi

Eski istemcilerin beklediği eski paket adı, ana Astral release hattında üretilmez. Geçiş için ayrı köprü repo/yayın yüzeyi kullanılabilir. Bu köprü geçiş dönemi tamamlandıktan sonra ayrıca kaldırılabilir; Astral kaynak paketi ve updater yolu kalıcı olarak `Astral-*` assetlerine bağlıdır.

## Cleanup Notu

2026-06-14 tarihinde verilen `APPROVED - CLEAN RELEASES` onayı sonrasında ara deneme/hotfix yayınları temizlendi ve public release yüzeyi anlamlı milestone sürümlere indirildi. Yeni 2.2.x çalışması kapsamında eski release veya tag silinmeyecek.
