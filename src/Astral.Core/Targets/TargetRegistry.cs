using Astral.Core.WebProxy;

namespace Astral.Core.Targets;

public sealed class TargetRegistry
{
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
                "discord"),
            Create(
                TargetIds.Roblox,
                "Roblox",
                TargetCategory.Game,
                TargetScopeKind.ApplicationAndWeb,
                ["roblox.com", "rbxcdn.com", "roblox.com.tr"],
                ["RobloxPlayerBeta.exe", "RobloxStudioBeta.exe"],
                "roblox"),
            Create(
                TargetIds.Wattpad,
                "Wattpad",
                TargetCategory.ReadingWriting,
                TargetScopeKind.Web,
                ["wattpad.com", "www.wattpad.com"],
                [],
                "wattpad"),
            Create(
                TargetIds.BigoLive,
                "Bigo Live",
                TargetCategory.LiveSocial,
                TargetScopeKind.Web,
                ["bigo.tv", "bigolive.tv"],
                [],
                "bigo-live"),
            Create(
                TargetIds.Azar,
                "Azar",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                ["azarlive.com", "azarlive.io"],
                ["Azar.exe"],
                "azar"),
            Create(
                TargetIds.Tango,
                "Tango",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                ["tango.me"],
                ["Tango.exe"],
                "tango"),
            Create(
                TargetIds.LiVU,
                "LiVU",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                ["livu.me"],
                ["LiVU.exe", "Livu.exe"],
                "livu"),
            Create(
                TargetIds.IMVU,
                "IMVU",
                TargetCategory.LiveSocial,
                TargetScopeKind.ApplicationAndWeb,
                ["imvu.com"],
                ["IMVUClient.exe", "IMVU.exe"],
                "imvu"),
            Create(
                TargetIds.Blogspot,
                "Blogspot",
                TargetCategory.Blog,
                TargetScopeKind.Web,
                ["blogspot.com", "*.blogspot.com"],
                [],
                "blogspot"),
            Create(
                TargetIds.CustomExecutable,
                "Özel EXE",
                TargetCategory.Custom,
                TargetScopeKind.CustomExecutable,
                [],
                [],
                "custom-exe"),
            Create(
                TargetIds.CustomDomain,
                "Özel Domain",
                TargetCategory.Custom,
                TargetScopeKind.CustomDomain,
                [],
                [],
                "custom-domain")
        ]);
    }

    public IReadOnlyList<TargetDefinition> GetBuiltInTargets()
    {
        return _targets.Values
            .OrderBy(target => target.Category)
            .ThenBy(target => target.Label, StringComparer.CurrentCultureIgnoreCase)
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
        string iconKey)
    {
        return new TargetDefinition(
            id,
            label,
            category,
            scopeKind,
            domains.Select(DomainPattern.Parse).ToArray(),
            executableNames.Select(name => new ExecutableHint(name)).ToArray(),
            iconKey,
            NeutralMetadata);
    }
}
