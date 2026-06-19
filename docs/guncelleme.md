# Astral Güncelleme ve Portable ZIP Davranışı

Astral portable ZIP olarak çalışır. Bu yüzden güncelleme akışı, klasik kurulum dizini yerine kullanıcının çıkardığı mevcut Astral klasörünü hedef alır.

GitHub Releases üzerinden indirilen ZIP paketi manuel kullanım içindir. Uygulama içi otomatik güncelleme, aynı paketi GitHub release yolu, asset digest, SHA-256 dosyası ve manifest eşleşmesiyle doğrular.

Manuel indirme için sabit dosya adı `Astral-win-x64.zip` kullanılır. Otomatik güncelleme ise denetlenen sürümü kilitlemek için `Astral-<version>-win-x64.zip` asset'ini ve aynı sürüme ait SHA-256 dosyasını doğrular.

Eski istemcilerden Astral'a geçiş ana Astral release assetleriyle yapılmaz. Geçiş dönemi için ayrı köprü reposu kullanılabilir; o yüzey yalnız eski updater'ın beklediği eski paket adını karşılar. Astral reposunun güncel release hattı sadece `Astral-*` assetlerini üretir ve doğrular.

## Kullanıcının Gördüğü Akış

1. Astral açılışta GitHub Releases üzerinden yeni sürüm olup olmadığını arka planda denetler.
2. Yeni sürüm yoksa ana ekranda güncelleme düğmesi görünmez; normal bağlantı arayüzü yer kaplamadan kalır.
3. Yeni sürüm varsa tek **Güncelle** düğmesi görünür.
4. **Güncelle**, arka planda denetlenip bulunan aynı sürümü indirir; tıklama sırasında farklı bir sürüm yeniden seçilmez.
5. Paket doğrulanır.
6. Astral güncelleme için hazırlanır.
7. Uygulama kapanır, dosyalar güvenli şekilde değiştirilir ve `Astral.exe` yeniden başlatılır.

## İndirme ve Staging

Güncelleme paketi doğrudan uygulama klasörünün üstüne açılmaz. Önce yöneticiye özel staging alanına alınır:

```text
%PROGRAMDATA%\Astral\updates
```

Bu alan, yarım inen veya doğrulanamayan paketlerin çalışan portable klasörü bozmasını engeller.

## Doğrulama

Güncelleme uygulanmadan önce şu kontroller yapılır:

- GitHub release asset adı beklenen `Astral-<version>-win-x64.zip` biçimiyle eşleşir.
- Asset GitHub HTTPS indirme yolundan gelir.
- Asset boyutu ve durum bilgisi beklenen sınırlar içindedir.
- GitHub `sha256:` digest bilgisi ve `.sha256.txt` dosyası ZIP ile eşleşir.
- ZIP içinde `astral.update-manifest.json` bulunur.
- Manifest sürümü, denetlenen release sürümüyle eşleşir.
- Kod imzalama varsa Authenticode imzası ayrıca korunur; sertifika yoksa güncelleme GitHub release yolu, asset digest, SHA-256 ve manifest doğrulamasıyla ilerler.

Bu kontrollerden biri başarısız olursa güncelleme uygulanmaz.

## Portable Klasör Hedefi

Güncelleme, çalışmakta olan `Astral.exe` dosyasının bulunduğu çıkarılmış klasöre uygulanır. Kullanıcı uygulamayı hangi klasörden çalıştırıyorsa güncelleme hedefi de odur.

Örnek:

```text
C:\Tools\Astral\Astral.exe
```

Bu durumda güncelleme `C:\Tools\Astral` klasörünü hedef alır.

Yeni elle kurulumlarda klasör adını sabit `Astral` tutmak önerilir. `Astral-2.2.28-win-x64` veya `2.2.28` gibi sürüm adlı klasörlerden çalıştırma desteklenir, ancak Astral bu durumu tanılama paketine yazar ve kullanıcı onayı olmadan klasör taşımaz.

## Güvenli Değiştirme ve Geri Dönüş

Çalışan executable Windows tarafından kilitlenebileceği için dosya değişimi ayrı updater süreciyle yapılır.

Güncelleme sırasında:

- Yeni dosyalar staging alanında hazırlanır.
- Mevcut uygulama klasörü değiştirilmeden önce yedeklenir.
- Hata oluşursa önceki dosyalar geri taşınmaya çalışılır.
- Başarılı olursa yeni `Astral.exe` başlatılır.

Amaç, indirme veya doğrulama hatasının çalışan uygulama klasörünü kullanılamaz hale getirmemesidir. Dosya kilidi, antivirüs/EDR veya yetki problemi geri yüklemeyi de engellerse kullanıcı doğrulanmış ZIP paketini yeniden çıkararak kurtarabilir.

## Kısayol ve Başlangıç Kaydı

Astral portable klasörü yerinde güncellendiği için mevcut kısayollar ve Windows başlangıç kaydı aynı `Astral.exe` yolunu kullanmaya devam eder.

Kullanıcı klasörü elle taşırsa kısayol veya başlangıç kaydını yeniden oluşturması gerekebilir.

## Bilinen Sınırlar

- Kod imzalama yoksa paket imzasız yayınlanabilir; bu durumda güven sınırı GitHub yayın yetkisi, release yolu, asset digest, SHA-256 dosyası ve manifest doğrulamasıdır.
- GitHub digest, SHA-256 dosyası veya manifest doğrulaması geçmeyen paketler uygulanmaz.
- GitHub erişimi olmayan ağlarda güncelleme denetimi başarısız olabilir; uygulama mevcut sürümle çalışmaya devam eder ve güncelleme düğmesi göstermez.
- Çok yavaş bağlantılarda indirme zaman aşımı yaşanırsa kullanıcı daha sonra tekrar deneyebilir.
