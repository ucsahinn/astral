# Kaynak Sorun Denetimi

Astral, eski geniş kapsamlı bağlantı yaklaşımlarını büyütmek yerine hedef seçimi modeline taşır. Amaç, kullanıcının seçtiği uygulama veya web hedeflerini tünele almak ve diğer trafiği normal bağlantıda bırakmaktır.

## Kapatılan Risk Sınıfları

| Risk | Yeni davranış |
| --- | --- |
| Tüm tarayıcı sürecinin tünele girmesi | Web hedeflerinde tarayıcı exe `#@ws:AllowedApps` içine yazılmaz; yalnız `Astral.WebProxy.exe` kullanılır. |
| Seçilmemiş uygulamaların etkilenmesi | `TargetScopeResolver` yalnız seçili presetlerden plan üretir. |
| Eski özel hedef alanlarının yanlışlıkla taşınması | Migration eski `CustomExecutables` ve `CustomDomains` alanlarını route planına almaz. |
| Profil bozulması veya geniş `AllowedApps` | Profil yeniden yazılır; eski `AllowedApps` ve `#@ws:AllowedApps` satırları kaldırılır. |
| Proxy/PAC state kalması | Apply öncesi state alınır; disconnect/error/dispose yollarında restore denenir. |
| Release asset adı uyuşmazlığı | Astral release hattı yalnız `Astral-*` ZIP ve SHA assetlerini doğrular. |

## Başlangıç Presetleri

- Discord
- Roblox
- Wattpad
- Bigo Live
- Azar
- Tango
- LiVU
- IMVU
- Blogspot

Bu presetler erişim durumunu kalıcı gerçek gibi ifade etmez. Yalnız hedef kapsamını tanımlar.

## Test Kapsamı

- `TargetRegistry` presetleri.
- `TargetSelectionStore` migration ve persist/restore.
- `TargetScopeResolver` app/web/proxy ayrımı.
- Browser executable denylist.
- Eski custom alanların yok sayılması.
- `WebProxy` allowlist.
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
