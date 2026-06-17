# Astral Release ve Tag Denetimi

Son denetim: 2026-06-17
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

Güncel patch hedef `v2.2.5` olarak belirlendi.

Neden `v2.2.5`?

- v2.2.4 tanılama paketlerinde bağlantı başlamadan önce `Disable` access-lock adımında düşen stale lock hatası görüldü.
- Firewall rule disable işlemi idempotent hale getirildi; kural yoksa başarı sayılır, bozuk app kuralı kaldırılarak temizlenebilir.
- Bağlantı kapalıyken hedef koruması seçili hedef planındaki exact domainlerden üretilir; Discord-only varsayıma dönmez.
- Access-lock hata metni stdout/stderr kuyruğu ve script fazını da içerir.
- Ana ekran hedef kartları ve uzun hata kopyası okunurluğu iyileştirildi.
- Eski marka klasöründen çalışan Astral artık tanılamada yanlış “sabit Astral klasörü” raporu üretmez.
- Astral ana release hattı yalnız Astral assetlerini üretir; eski istemci geçişi ayrı köprü repo yüzeyinin sorumluluğudur.

## Public Release Yüzeyi

| Release | Canlı durum | Asset durumu | Karar | Gerekçe |
| --- | --- | --- | --- | --- |
| `v2.2.5` | Yayın adayı | Bekliyor | Yayınla | Access-lock disable dayanıklılığı, seçili hedef koruması ve hedef kartı okunurluğu hotfixleri. |
| `v2.2.4` | Yayında | 4 asset | Koru | Inline hedef kartları, Astral-only release assetleri ve update görünürlüğü hotfixleri. |
| `v2.2.3` | Yayında | 8 asset | Koru | Önceki hedef seçim yüzeyi polish, WireSock readiness ve DNS flush hotfix. |
| `v2.2.2` | Yayında | 8 asset | Koru | Geçiş dönemi köprü assetleri ve premium ikon seti. |
| `v2.2.1` | Yayında | 4 asset | Koru | Web-only hedef süreci, ürün dili ve release workflow idempotency düzeltmesi. |
| `v2.2.0` | Yayında | 4 asset | Koru | Seçilebilir hedef kapsamı ve Astral.WebProxy mimarisi. |
| `v2.1.6` ve öncesi | Yayında | Değişken | Koru | Önceki public baseline sürümleri. |

## Yayın Kapısı

`v2.2.5` yayınından önce tamamlanması gereken koşullar:

- Proje, updater ve web proxy sürümü `2.2.5` ile aynı.
- `Astral-2.2.5-win-x64.zip` ve sabit `Astral-win-x64.zip` aynı içeriği gösterir.
- ZIP içinde `Astral.exe`, `Astral.Updater.exe`, `Astral.WebProxy.exe`, update manifest'i ve gerekli runtime dosyaları bulunur.
- ZIP içindeki update manifest sürümü `2.2.5`.
- Astral release içinde eski isimli uyumluluk ZIP'i yayınlanmaz.
- Kod imzalama sertifikası yapılandırılmadıysa paket imzasız olduğu açıkça not edilir.
- `dotnet build`, Core tests, Windows tests, `scripts/verify.ps1`, `scripts/build-release.ps1`, `git diff --check` ve Gitleaks geçer.
- GitHub Release assetleri ve SHA-256 dosyaları yayınlandıktan sonra erişilebilir olarak doğrulanır.

## Eski İstemci Geçişi

Eski istemcilerin beklediği eski paket adı, ana Astral release hattında üretilmez. Geçiş için ayrı köprü repo/yayın yüzeyi kullanılabilir. Bu köprü geçiş dönemi tamamlandıktan sonra ayrıca kaldırılabilir; Astral kaynak paketi ve updater yolu kalıcı olarak `Astral-*` assetlerine bağlıdır.

## Cleanup Notu

2026-06-14 tarihinde verilen `APPROVED - CLEAN RELEASES` onayı sonrasında ara deneme/hotfix yayınları temizlendi ve public release yüzeyi anlamlı milestone sürümlere indirildi. Yeni 2.2.x çalışması kapsamında eski release veya tag silinmeyecek.
