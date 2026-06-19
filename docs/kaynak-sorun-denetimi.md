# Kaynak Sorun Denetimi

Astral, eski geniş kapsamlı bağlantı yaklaşımlarını büyütmek yerine hedef seçimi modeline taşır. Amaç, kullanıcının seçtiği uygulama veya web hedeflerini tünele almak ve diğer trafiği normal bağlantıda bırakmaktır.

## Kapatılan Risk Sınıfları

| Risk | Yeni davranış |
| --- | --- |
| Tüm tarayıcı sürecinin tünele girmesi | Web hedeflerinde tarayıcı exe `AllowedApps` içine yazılmaz; yalnız `Astral.WebProxy.exe` kullanılır. |
| Seçilmemiş uygulamaların etkilenmesi | `TargetScopeResolver` yalnız seçili presetlerden plan üretir. |
| Eski özel hedef alanlarının yanlışlıkla taşınması | Migration eski `CustomExecutables` ve `CustomDomains` alanlarını route planına almaz. |
| Profil bozulması veya geniş `AllowedApps` | Profil yeniden yazılır; eski `AllowedApps` ve `#@ws:AllowedApps` satırları kaldırılır. |
| Proxy/PAC state kalması | Apply öncesi state alınır; disconnect/error/dispose yollarında restore denenir. |
| Release asset adı uyuşmazlığı | Astral release hattı yalnız `Astral-*` ZIP ve SHA assetlerini doğrular. |

## Başlangıç Presetleri

- Discord
- Wattpad
- Bigo Live
- Azar
- Tango
- LiVU
- IMVU
- Blogspot
- Radio Garden
- DW
- VOA
- Ekşi Sözlük
- Grok
- Imgur
- Pastebin

Bu presetler erişim durumunu kalıcı gerçek gibi ifade etmez. Yalnız hedef kapsamını tanımlar.

## Hedef Aday Kuralları

Yeni hedef ekleme kararı erişim durumunu kalıcı gerçek gibi kodlamaz. Bir hedef ancak şu kurallar sağlanırsa aktif preset olur:

- Hedef kullanıcı açısından anlamlı ve sık kullanılan bir uygulama veya web ürünü olmalı.
- Web hedeflerinde genel tarayıcı exe'leri `AllowedApps` içine eklenmemeli; yalnız `Astral.WebProxy.exe` kapsamlanmalı.
- Her web hedefi için route domainleri ile kısa `probeHosts` listesi ayrı tutulmalı.
- `probeHosts` wildcard, public suffix veya geniş domain olmamalı; gerçek host olmalı.
- Giriş/bootstrap için gereken birinci taraf hostlar eklenmeli, fakat tüm Google, tüm Meta veya tüm CDN gibi geniş kapsamlar eklenmemeli.
- Gerçek marka ikonu lisans açısından güvenli değilse release'e generic resmi logo gibi davranan asset eklenmemeli; kontrollü fallback veya ayrı asset görevi yazılmalı.
- Erişim durumu zamanla değiştiği için UI veya dokümanda "kesin çalışır", "engelli", "ban riski yok" gibi kalıcı iddia kullanılmamalı.

2026-06-18 araştırma notuna göre ek aday havuzu:

| Aday | Kapsam | Kural notu | Durum |
| --- | --- | --- | --- |
| Threads | Web/App | `threads.net` tek başına yeterli olmayabilir; Türkiye'deki servis kapatma/account-region davranışı VPN ile çözülemeyebilir. | Watchlist |
| Tidal | Web/App | RTÜK/lisans kaynaklı erişim değişken olabilir; player/CDN hostları genişlemeden önce tek tek ölçülmeli. | Watchlist |

Radio Garden, DW, VOA, Ekşi Sözlük, Grok, Imgur ve Pastebin artık aktif preset listesindedir. Roblox aktif preset listesinden çıkarıldı. Bu durum erişimin kalıcı olarak engelli veya kesin çalışır olduğu anlamına gelmez; yalnız dar kapsamlı rota tanımının kaynakta bulunduğunu gösterir.

## Test Kapsamı

- `TargetRegistry` presetleri.
- `TargetSelectionStore` migration ve persist/restore.
- `TargetScopeResolver` app/web/proxy ayrımı.
- Browser executable denylist.
- Eski custom alanların yok sayılması.
- `WebProxy` allowlist.
- Scoped WebProxy hedef çıkış kanıtı.
- PAC PROXY/DIRECT üretimi.
- Controller connect/disconnect proxy apply/clear yaşam döngüsü.
- Access-lock PowerShell hata aşaması görünürlüğü.
- WPF ana ekran inline hedef kartları.
- Güncelleme düğmesinin yalnız update varsa görünmesi.
- Tray, kapanma ve restart yardımcı davranışları.

## Kalan Operasyonel Riskler

- Tarayıcı veya kurumsal policy PAC davranışını değiştirebilir.
- QUIC/UDP/WebRTC yolları TCP CONNECT proxy modelinin dışındadır.
- Canlı erişim durumu ISS, servis ve ülke politikalarına göre değişebilir.
