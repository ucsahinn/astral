using Astral.Core.WebProxy;

namespace Astral.Core.Targets;

public sealed class TargetRegistry
{
    private static readonly IReadOnlyList<string> BuiltInTargetOrder =
    [
        TargetIds.Discord,
        TargetIds.Wattpad,
        TargetIds.Azar,
        TargetIds.BigoLive,
        TargetIds.IMVU,
        TargetIds.LiVU,
        TargetIds.Tango,
        TargetIds.Blogspot,
        TargetIds.RadioGarden,
        TargetIds.DeutscheWelle,
        TargetIds.VoiceOfAmerica,
        TargetIds.EksiSozluk,
        TargetIds.Grok,
        TargetIds.Imgur,
        TargetIds.Pastebin
    ];

    private static readonly IReadOnlyDictionary<string, string> NeutralMetadata =
        new Dictionary<string, string>
        {
            ["note"] = "Bu preset yalnızca hedef kapsamını tanımlar. Erişim durumu zamanla değişebilir."
        };

    private readonly Dictionary<string, TargetDefinition> _targets;

    private TargetRegistry(IEnumerable<TargetDefinition> targets)
    {
        _targets = targets.ToDictionary(
            target => target.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public static TargetRegistry CreateDefault()
    {
        return new TargetRegistry(
        [
            Create(
                TargetIds.Discord,
                "Discord",
                TargetCategory.Communication,
                TargetScopeKind.ApplicationAndWeb,
                ["discord.com", "discordapp.com", "discordapp.net", "discord.gg", "discord.gift", "discord.media", "discordstatus.com", "discordcdn.com", "cdn.discordapp.com", "dl.discordapp.net", "updates.discord.com", "gateway.discord.gg", "media.discordapp.net"],
                ["Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe", "DiscordDevelopment.exe"],
                "discord",
                [
                    "discord.com",
                    "discord.gg",
                    "discordapp.com",
                    "discordapp.net",
                    "discordcdn.com",
                    "cdn.discordapp.com",
                    "dl.discordapp.net",
                    "gateway.discord.gg",
                    "media.discordapp.net",
                    "updates.discord.com"
                ],
                "https://discord.com/app"),
            Create(
                TargetIds.Wattpad,
                "Wattpad",
                TargetCategory.ReadingWriting,
                TargetScopeKind.Web,
                [
                    "wattpad.com",
                    "www.wattpad.com",
                    "api.wattpad.com",
                    "img.wattpad.com",
                    "static.wattpad.com"
                ],
                [],
                "wattpad",
                [
                    "wattpad.com",
                    "www.wattpad.com"
                ],
                "https://www.wattpad.com"),
            Create(
                TargetIds.BigoLive,
                "Bigo Live",
                TargetCategory.LiveSocial,
                TargetScopeKind.Web,
                [
                    "bigo.tv",
                    "www.bigo.tv",
                    "mobile.bigo.tv",
                    "bigolive.tv",
                    "www.bigolive.tv"
                ],
                [],
                "bigo-live",
                [
                    "bigo.tv",
                    "www.bigo.tv"
                ],
                "https://www.bigo.tv"),
            Create(
                TargetIds.Azar,
                "Azar",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                [
                    "azarlive.com",
                    "www.azarlive.com",
                    "api.azarlive.com",
                    "azarlive.io",
                    "api.azarlive.io"
                ],
                ["Azar.exe"],
                "azar",
                [
                    "azarlive.com",
                    "www.azarlive.com"
                ],
                "https://azarlive.com"),
            Create(
                TargetIds.Tango,
                "Tango",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                [
                    "tango.me",
                    "www.tango.me",
                    "api.tango.me"
                ],
                ["Tango.exe"],
                "tango",
                [
                    "tango.me",
                    "www.tango.me"
                ],
                "https://www.tango.me"),
            Create(
                TargetIds.LiVU,
                "LiVU",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                [
                    "livu.me",
                    "www.livu.me",
                    "api.livu.me"
                ],
                ["LiVU.exe", "Livu.exe"],
                "livu",
                [
                    "livu.me",
                    "www.livu.me"
                ],
                "https://www.livu.me"),
            Create(
                TargetIds.IMVU,
                "IMVU",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                [
                    "imvu.com",
                    "www.imvu.com",
                    "secure.imvu.com",
                    "api.imvu.com",
                    "userimages-akm.imvu.com"
                ],
                ["IMVUClient.exe", "IMVU.exe"],
                "imvu",
                [
                    "imvu.com",
                    "www.imvu.com"
                ],
                "https://www.imvu.com"),
            Create(
                TargetIds.Blogspot,
                "Blogspot",
                TargetCategory.Blog,
                TargetScopeKind.Web,
                [
                    "blogspot.com",
                    "*.blogspot.com",
                    "blogger.com",
                    "www.blogger.com",
                    "blogger.googleusercontent.com"
                ],
                [],
                "blogspot",
                [
                    "blogspot.com",
                    "blogger.com",
                    "www.blogger.com",
                    "blogger.googleusercontent.com"
                ],
                "https://www.blogger.com"),
            Create(
                TargetIds.RadioGarden,
                "Radio Garden",
                TargetCategory.Media,
                TargetScopeKind.Web,
                [
                    "radio.garden",
                    "www.radio.garden"
                ],
                [],
                "radio-garden",
                [
                    "radio.garden",
                    "www.radio.garden"
                ],
                "https://radio.garden"),
            Create(
                TargetIds.DeutscheWelle,
                "DW",
                TargetCategory.News,
                TargetScopeKind.Web,
                [
                    "dw.com",
                    "www.dw.com",
                    "amp.dw.com",
                    "static.dw.com"
                ],
                [],
                "deutsche-welle",
                [
                    "dw.com",
                    "www.dw.com"
                ],
                "https://www.dw.com"),
            Create(
                TargetIds.VoiceOfAmerica,
                "VOA",
                TargetCategory.News,
                TargetScopeKind.Web,
                [
                    "voanews.com",
                    "www.voanews.com",
                    "learningenglish.voanews.com",
                    "gdb.voanews.com"
                ],
                [],
                "voice-of-america",
                [
                    "www.voanews.com",
                    "voanews.com"
                ],
                "https://www.voanews.com"),
            Create(
                TargetIds.EksiSozluk,
                "Ekşi Sözlük",
                TargetCategory.ReadingWriting,
                TargetScopeKind.Web,
                [
                    "eksisozluk.com",
                    "www.eksisozluk.com",
                    "eksisozluk1923.com",
                    "www.eksisozluk1923.com"
                ],
                [],
                "eksi-sozluk",
                [
                    "eksisozluk.com",
                    "www.eksisozluk.com"
                ],
                "https://eksisozluk.com"),
            Create(
                TargetIds.Grok,
                "Grok",
                TargetCategory.Utility,
                TargetScopeKind.Web,
                [
                    "grok.com",
                    "www.grok.com",
                    "x.ai",
                    "www.x.ai"
                ],
                [],
                "grok",
                [
                    "grok.com",
                    "x.ai"
                ],
                "https://grok.com"),
            Create(
                TargetIds.Imgur,
                "Imgur",
                TargetCategory.Media,
                TargetScopeKind.Web,
                [
                    "imgur.com",
                    "www.imgur.com",
                    "i.imgur.com",
                    "api.imgur.com",
                    "s.imgur.com"
                ],
                [],
                "imgur",
                [
                    "imgur.com",
                    "www.imgur.com"
                ],
                "https://imgur.com"),
            Create(
                TargetIds.Pastebin,
                "Pastebin",
                TargetCategory.Utility,
                TargetScopeKind.Web,
                [
                    "pastebin.com",
                    "www.pastebin.com",
                    "pastebin.pl",
                    "www.pastebin.pl"
                ],
                [],
                "pastebin",
                [
                    "pastebin.com",
                    "www.pastebin.com"
                ],
                "https://pastebin.com")
        ]);
    }

    public IReadOnlyList<TargetDefinition> GetBuiltInTargets()
    {
        return BuiltInTargetOrder
            .Where(_targets.ContainsKey)
            .Select(id => _targets[id])
            .ToArray();
    }

    public bool TryGet(string id, out TargetDefinition target)
    {
        return _targets.TryGetValue(id, out target!);
    }

    private static TargetDefinition Create(
        string id,
        string label,
        TargetCategory category,
        TargetScopeKind scopeKind,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> executableNames,
        string iconKey,
        IReadOnlyList<string>? probeHosts = null,
        string? launchUrl = null)
    {
        var metadata = new Dictionary<string, string>(
            NeutralMetadata,
            StringComparer.OrdinalIgnoreCase);
        if (probeHosts is { Count: > 0 })
        {
            metadata["probeHosts"] = string.Join(";", probeHosts);
        }

        var targetLaunchUrl = string.IsNullOrWhiteSpace(launchUrl)
            ? "https://" + domains.First(domain => !domain.StartsWith("*.", StringComparison.Ordinal))
            : launchUrl.Trim();
        metadata["launchUrl"] = targetLaunchUrl;

        return new TargetDefinition(
            id,
            label,
            category,
            scopeKind,
            domains.Select(DomainPattern.Parse).ToArray(),
            executableNames.Select(name => new ExecutableHint(name)).ToArray(),
            iconKey,
            metadata);
    }
}
