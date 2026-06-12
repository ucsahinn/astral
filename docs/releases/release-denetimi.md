# Discorder Release ve Tag Denetimi

Bu tablo, v2.0.12 public yüzeyi hazırlanırken çıkarılan non-destructive release temizliği planıdır. GitHub Release veya tag silme işlemi yapılmadı. Silme/cleanup için ayrıca `APPROVED - CLEAN RELEASES` onayı gerekir.

## İnceleme Kaynakları

- Yerel git tag listesi.
- Remote tag listesi.
- Yerel `artifacts/` klasöründeki ZIP, hash ve release note dosyaları.
- Repo release workflow ve build script'leri.

Aşağıdaki kararlar, erişilebilen tag ve artifact kanıtına göre hazırlanmış güvenli önerilerdir. Herhangi bir release veya tag silme kararı, canlı GitHub Release listesi release sahibi tarafından ayrıca doğrulandıktan sonra uygulanmalıdır.

## Cleanup Tablosu

| Release/tag | Öneri | Gerekçe | Risk | Komut/API aksiyonu |
| --- | --- | --- | --- | --- |
| `v2.0.12` | Oluştur / latest yap | Mevcut ürün sürümü ve uygulama dosyaları 2.0.12'ye ayarlı. | İmzalı artifact olmadan otomatik güncelleme zinciri güvenilmez olur. | İmzalı artifact hazır olunca GitHub Release workflow veya `gh release create v2.0.12 ... --latest`. |
| `v2.0.11` | Koru | Remote ve local tag mevcut; önceki stabil yayın hattı. | Latest olarak kalırsa README v2.0.12 hazırlığıyla çelişebilir. | v2.0.12 yayınlanınca latest otomatik/manuel güncellenir. |
| `v2.0.10` | Koru / release notunu eski olarak bırak | v2.0.11 öncesi anlamlı geçiş sürümü olabilir. | Canlı release body görülmeden silmek yanlış olabilir. | Silme yok. |
| `v2.0.9` | Koru, local artifact tekrarlarını temizleme adayı | `artifacts/` altında `complete`, `fix`, `complete2`, `complete3` gibi local tekrarlar var. | Public release silinirse indirme geçmişi ve güven kaybı oluşabilir. | Release silme yok; local artifact cleanup ayrı onay gerektirir. |
| `v2.0.8` | Koru / arşiv olarak bırak | 2.0.9 öncesi tag mevcut. | Canlı release durumu bilinmiyor. | Silme yok. |
| `v2.0.7` | Koru / arşiv olarak bırak | Tag ve release-check kalıntısı var. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.6` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.5` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.4` | Tag yok, local artifact var | Local artifact karmaşası; public tag görünmüyor. | Silme local dosya temizliği sayılır ve ayrıca onay ister. | Şimdilik işlem yok. |
| `v2.0.3` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.2` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.1` | Koru / arşiv olarak bırak | Tag, artifact ve release note mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.0` | Koru | 2.x hattının başlangıç noktası. | Silmek geçmişi gereksiz kırar. | Silme yok. |

## Sürüm Kararı

Bu public yüzey için anlamlı SemVer sürümü `v2.0.12` olarak belirlendi.

Neden patch?

- Ürün adı, portable model ve çalışma kapsamı değişmedi.
- Kullanıcıya görünen UI dili, güncelleme akışı ve güvenlik doğrulama ayrıntıları iyileştirildi.
- Breaking change veya yeni major/minor ürün hattı yok.

## Yayın Kapısı

v2.0.12 GitHub Release yayınlanmadan önce şu koşullar gerekir:

- GitHub yayın yetkisi geçerli olmalı.
- `v2.0.12` tag'i doğru commit'e işaret etmeli.
- Release workflow veya local build imzalı `Discorder-2.0.12-win-x64.zip` üretmeli.
- `.sha256.txt` dosyası ZIP ile eşleşmeli.
- Secret scan temiz olmalı.
- Release body `docs/releases/v2.0.12.md` ile uyumlu olmalı.
