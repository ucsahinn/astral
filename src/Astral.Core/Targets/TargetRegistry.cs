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
        TargetIds.Blogspot
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
                    "discord.gg"
                ]),
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
                ]),
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
                ]),
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
                ]),
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
                ]),
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
                ]),
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
                ]),
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
                ])
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
        IReadOnlyList<string>? probeHosts = null)
    {
        var metadata = new Dictionary<string, string>(
            NeutralMetadata,
            StringComparer.OrdinalIgnoreCase);
        if (probeHosts is { Count: > 0 })
        {
            metadata["probeHosts"] = string.Join(";", probeHosts);
        }

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
