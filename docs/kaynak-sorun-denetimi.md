# Kaynak Sorun Denetimi

Astral, eski geniş kapsamlı bağlantı yaklaşımlarını büyütmek yerine hedef seçimi modeline taşır. Amaç, kullanıcının seçtiği uygulama veya web hedeflerini tünele almak ve diğer trafiği normal bağlantıda bırakmaktır.

## Kapatılan Risk Sınıfları

| Risk | Yeni davranış |
| --- | --- |
| Tüm tarayıcı sürecinin tünele girmesi | Web hedeflerinde tarayıcı exe `AllowedApps` içine yazılmaz; yalnız `Astral.WebProxy.exe` kullanılır. |
| Seçilmemiş uygulamaların etkilenmesi | `TargetScopeResolver` yalnız seçili preset/özel hedeflerden plan üretir. |
| Geniş domain wildcard'ları | `*`, `*.com`, URL, port ve IP literal reddedilir. |
| Özel EXE ile path enjeksiyonu | Mutlak `.exe`, mevcut dosya ve reparse point kontrolleri uygulanır. |
| Profil bozulması veya geniş `AllowedApps` | Profil yeniden yazılır; eski `AllowedApps` satırları kaldırılır. |
| Proxy/PAC state kalması | Apply öncesi state alınır; disconnect/error/dispose yollarında restore denenir. |

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
- Özel EXE
- Özel Domain

Bu presetler erişim durumunu kalıcı gerçek gibi ifade etmez. Yalnız hedef kapsamını tanımlar.

## Test Kapsamı

- TargetRegistry presetleri.
- TargetSelection migration ve persist/restore.
- TargetScopeResolver app/web/proxy ayrımı.
- Browser executable denylist.
- Custom EXE/domain validation.
- WebProxy allowlist.
- PAC PROXY/DIRECT üretimi.
- Controller connect/disconnect proxy apply/clear yaşam döngüsü.
- WPF ana ekran ve Hedef Merkezi render testi.

## Kalan Operasyonel Riskler

- Tarayıcı veya kurumsal policy PAC davranışını değiştirebilir.
- QUIC/UDP/WebRTC yolları TCP CONNECT proxy modelinin dışındadır.
- Canlı erişim durumu ISS, servis ve ülke politikalarına göre değişebilir.
