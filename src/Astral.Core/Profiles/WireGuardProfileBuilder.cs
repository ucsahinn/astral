namespace Astral.Core.Profiles;

public static class WireGuardProfileBuilder
{
    public static string BuildScopedProfile(
        string sourceProfile,
        IEnumerable<string> allowedApplications)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfile);
        ArgumentNullException.ThrowIfNull(allowedApplications);

        var apps = allowedApplications
            .Select(NormalizeApplication)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(FormatApplication)
            .ToArray();

        if (apps.Length == 0)
        {
            throw new InvalidOperationException(
                "At least one application must be present in AllowedApps.");
        }

        var sourceLines = sourceProfile
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        if (!sourceLines.Any(line =>
                line.Trim().Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
            || !sourceLines.Any(line =>
                line.Trim().Equals("[Peer]", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The generated WireGuard profile is incomplete.");
        }

        var result = new List<string>(sourceLines.Length + 1);
        var inserted = false;

        foreach (var sourceLine in sourceLines)
        {
            var trimmed = sourceLine.Trim();
            if (IsAllowedAppsDirective(trimmed))
            {
                continue;
            }

            result.Add(sourceLine.TrimEnd());

            if (!inserted && trimmed.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                result.Add($"#@ws:AllowedApps = {string.Join(", ", apps)}");
                inserted = true;
            }
        }

        if (!inserted)
        {
            throw new InvalidDataException("The generated profile does not contain a peer endpoint.");
        }

        return string.Join("\r\n", result).TrimEnd() + "\r\n";
    }

    private static string NormalizeApplication(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException("AllowedApps cannot contain an empty value.");
        }

        if (normalized.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        {
            throw new InvalidDataException(
                $"AllowedApps contains an unsafe value: {normalized}");
        }

        return normalized;
    }

    private static string FormatApplication(string value)
    {
        if (!value.Any(char.IsWhiteSpace))
        {
            return value;
        }

        return "\"" + value + "\"";
    }

    private static bool IsAllowedAppsDirective(string value)
    {
        return value.StartsWith("AllowedApps", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("#@ws:AllowedApps", StringComparison.OrdinalIgnoreCase);
    }
}
