using System.IO;
using System.Text.RegularExpressions;

namespace Astral.App;

internal static class PortableInstallDiagnostics
{
    public const string StableDirectoryName = "Astral";

    private static readonly Regex VersionedDirectoryPattern = new(
        @"^(Astral[-_])?v?\d+\.\d+\.\d+(-win-(x64|x86|arm64))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string?> Capture(
        string? applicationDirectory = null)
    {
        var directory = NormalizeDirectory(
            applicationDirectory ?? AppContext.BaseDirectory);
        var directoryName = Path.GetFileName(directory);
        var parentDirectory = Path.GetDirectoryName(directory);
        var looksVersioned = LooksVersionedDirectoryName(directoryName);
        var parentDirectoryName = string.IsNullOrWhiteSpace(parentDirectory)
            ? string.Empty
            : Path.GetFileName(parentDirectory);

        return new Dictionary<string, string?>
        {
            ["portableDirectoryName"] = directoryName,
            ["portableParentDirectoryName"] = parentDirectoryName,
            ["portableDirectoryLooksVersioned"] = looksVersioned.ToString(),
            ["portableRecommendedDirectoryName"] = StableDirectoryName,
            ["portableDirectoryGuidance"] = looksVersioned
                ? "Yeni indirmelerde sabit Astral klasoru kullanin; mevcut klasoru tasima kullanici onayi ister."
                : "Sabit Astral klasoru kullaniliyor.",
            ["targetScopeMeaning"] =
                "Secili uygulama hedefleri ve Astral.WebProxy uzerinden secili web domainleri kapsama alinir."
        };
    }

    public static bool LooksVersionedDirectoryName(string? directoryName)
    {
        return !string.IsNullOrWhiteSpace(directoryName)
            && VersionedDirectoryPattern.IsMatch(directoryName);
    }

    private static string NormalizeDirectory(string directory)
    {
        return Path.GetFullPath(directory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }
}
