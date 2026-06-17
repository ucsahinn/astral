namespace Astral.Core.WebProxy;

public sealed class WebProxyAccessPolicy
{
    private readonly IReadOnlyList<DomainPattern> _patterns;

    public WebProxyAccessPolicy(IReadOnlyList<DomainPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _patterns = patterns
            .DistinctBy(pattern => pattern.Pattern, StringComparer.OrdinalIgnoreCase)
            .OrderBy(pattern => pattern.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<DomainPattern> Patterns => _patterns;

    public bool IsAllowedHost(string host)
    {
        var normalized = TryNormalizeProxyHost(host);
        return normalized is not null
            && _patterns.Any(pattern => pattern.Matches(normalized));
    }

    private static string? TryNormalizeProxyHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var candidate = host.Trim();
        if (candidate.StartsWith('['))
        {
            return null;
        }

        var colonIndex = candidate.LastIndexOf(':');
        if (colonIndex > -1
            && candidate.IndexOf(':') == colonIndex
            && int.TryParse(candidate[(colonIndex + 1)..], out _))
        {
            candidate = candidate[..colonIndex];
        }

        try
        {
            return DomainPattern.NormalizeHost(candidate);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
