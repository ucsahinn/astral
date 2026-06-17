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
    public const string CustomExecutable = "custom-executable";
    public const string CustomDomain = "custom-domain";
}

public enum TargetCategory
{
    Popular,
    Communication,
    Game,
    ReadingWriting,
    LiveSocial,
    Blog,
    Custom
}

public enum TargetScopeKind
{
    Application,
    Web,
    ApplicationAndWeb,
    CustomExecutable,
    CustomDomain
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
        _ => "Özel"
    };

    public bool HasApplicationScope =>
        ScopeKind is TargetScopeKind.Application
            or TargetScopeKind.ApplicationAndWeb
            or TargetScopeKind.CustomExecutable;

    public bool HasWebScope =>
        ScopeKind is TargetScopeKind.Web
            or TargetScopeKind.ApplicationAndWeb
            or TargetScopeKind.CustomDomain;
}

public sealed record CustomExecutableTarget
{
    private CustomExecutableTarget(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static CustomExecutableTarget Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException("EXE yolu komut veya tırnak içeremez.", nameof(path));
        }

        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("EXE yolu mutlak olmalıdır.", nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException("EXE yolu çözümlenemedi.", nameof(path), exception);
        }

        if (RoutingPlan.IsBrowserExecutable(fullPath))
        {
            throw new ArgumentException(
                "Genel tarayıcı süreçleri özel EXE hedefi olamaz.",
                nameof(path));
        }

        if (!string.Equals(
                System.IO.Path.GetExtension(fullPath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Özel uygulama hedefi .exe olmalıdır.", nameof(path));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Özel uygulama hedefi bulunamadı.", fullPath);
        }

        RejectReparsePointsInExistingAncestors(fullPath);
        if (File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ArgumentException("Özel uygulama hedefi reparse point olamaz.", nameof(path));
        }

        return new CustomExecutableTarget(fullPath);
    }

    private static void RejectReparsePointsInExistingAncestors(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("EXE yolu kökü çözümlenemedi.", nameof(path));
        }

        var current = root;
        foreach (var part in fullPath[root.Length..].Split(
                     [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                continue;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ArgumentException("EXE yolu reparse point içinden geçemez.", nameof(path));
            }
        }
    }
}

public sealed record CustomDomainTarget
{
    private CustomDomainTarget(string pattern)
    {
        Pattern = pattern;
    }

    public string Pattern { get; }

    public DomainPattern DomainPattern => DomainPattern.Parse(Pattern);

    public static CustomDomainTarget Create(string pattern)
    {
        return new CustomDomainTarget(DomainPattern.Parse(pattern).Pattern);
    }
}

public sealed record TargetSelection(
    IReadOnlyList<string> SelectedTargetIds,
    IReadOnlyList<CustomExecutableTarget> CustomExecutables,
    IReadOnlyList<CustomDomainTarget> CustomDomains)
{
    public static TargetSelection Default { get; } = new(
        [TargetIds.Discord],
        [],
        []);
}
