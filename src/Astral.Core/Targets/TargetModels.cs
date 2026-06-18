using Astral.Core.WebProxy;

namespace Astral.Core.Targets;

public static class TargetIds
{
    public const string Discord = "discord";
    public const string Roblox = "roblox";
    public const string Wattpad = "wattpad";
    public const string BigoLive = "bigo-live";
    public const string Azar = "azar";
    public const string Tango = "tango";
    public const string LiVU = "livu";
    public const string IMVU = "imvu";
    public const string Blogspot = "blogspot";
    public const string RadioGarden = "radio-garden";
    public const string DeutscheWelle = "deutsche-welle";
    public const string VoiceOfAmerica = "voice-of-america";
    public const string EksiSozluk = "eksi-sozluk";
    public const string Grok = "grok";
    public const string Imgur = "imgur";
    public const string Pastebin = "pastebin";
}

public enum TargetCategory
{
    Popular,
    Communication,
    Game,
    ReadingWriting,
    LiveSocial,
    Blog,
    News,
    Media,
    Utility
}

public enum TargetScopeKind
{
    Application,
    Web,
    ApplicationAndWeb
}

public sealed record ExecutableHint(string FileName);

public sealed record TargetDefinition(
    string Id,
    string Label,
    TargetCategory Category,
    TargetScopeKind ScopeKind,
    IReadOnlyList<DomainPattern> Domains,
    IReadOnlyList<ExecutableHint> ExecutableHints,
    string IconKey,
    IReadOnlyDictionary<string, string> Metadata)
{
    public string ScopeLabel => ScopeKind switch
    {
        TargetScopeKind.Application => "Uygulama",
        TargetScopeKind.Web => "Web",
        TargetScopeKind.ApplicationAndWeb => "Uygulama + Web",
        _ => "Hedef"
    };

    public bool HasApplicationScope =>
        ScopeKind is TargetScopeKind.Application
            or TargetScopeKind.ApplicationAndWeb;

    public bool HasWebScope =>
        ScopeKind is TargetScopeKind.Web
            or TargetScopeKind.ApplicationAndWeb;
}

public sealed record TargetSelection(
    IReadOnlyList<string> SelectedTargetIds)
{
    public static TargetSelection Default { get; } = new(
        [TargetIds.Discord]);
}
