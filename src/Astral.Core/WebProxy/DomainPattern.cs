using System.Globalization;
using System.Net;

namespace Astral.Core.WebProxy;

public sealed record DomainPattern
{
    private DomainPattern(string pattern, bool isWildcard)
    {
        Pattern = pattern;
        IsWildcard = isWildcard;
        Value = isWildcard ? pattern[2..] : pattern;
    }

    public string Pattern { get; }

    public string Value { get; }

    public bool IsWildcard { get; }

    public static DomainPattern Parse(string value)
    {
        var normalized = Normalize(value, allowWildcard: true);
        var wildcard = normalized.StartsWith("*.", StringComparison.Ordinal);
        return new DomainPattern(normalized, wildcard);
    }

    public bool Matches(string host)
    {
        var normalizedHost = NormalizeHost(host);
        if (normalizedHost is null)
        {
            return false;
        }

        if (IsWildcard)
        {
            return normalizedHost.EndsWith(
                    "." + Value,
                    StringComparison.OrdinalIgnoreCase)
                && !normalizedHost.Equals(Value, StringComparison.OrdinalIgnoreCase);
        }

        return normalizedHost.Equals(Value, StringComparison.OrdinalIgnoreCase)
            || normalizedHost.EndsWith(
                "." + Value,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeHost(string host)
    {
        return Normalize(host, allowWildcard: false);
    }

    private static string? TryNormalizeHost(string host)
    {
        try
        {
            return NormalizeHost(host);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Normalize(string value, bool allowWildcard)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Domain boş olamaz.", nameof(value));
        }

        var candidate = value.Trim();
        if (candidate.Contains("://", StringComparison.Ordinal)
            || candidate.Contains('/')
            || candidate.Contains('\\')
            || candidate.Contains('@')
            || candidate.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Domain yalnızca host/pattern içermelidir.", nameof(value));
        }

        if (candidate.StartsWith('[')
            || IPAddress.TryParse(candidate, out _))
        {
            throw new ArgumentException("IP adresi hedef domain olarak kullanılamaz.", nameof(value));
        }

        var wildcard = false;
        if (candidate.StartsWith("*.", StringComparison.Ordinal))
        {
            if (!allowWildcard)
            {
                throw new ArgumentException("Host değeri wildcard içeremez.", nameof(value));
            }

            wildcard = true;
            candidate = candidate[2..];
        }
        else if (candidate.Contains('*'))
        {
            throw new ArgumentException("Wildcard yalnızca prefix olarak kullanılabilir.", nameof(value));
        }

        if (candidate.Contains(':'))
        {
            throw new ArgumentException("Domain port içeremez.", nameof(value));
        }

        var ascii = ToAsciiDomain(candidate);
        var labels = ascii.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
        {
            throw new ArgumentException("Domain en az ikinci seviye alan adı içermelidir.", nameof(value));
        }

        if (wildcard && labels.Length < 2)
        {
            throw new ArgumentException("Wildcard public suffix üzerinde kullanılamaz.", nameof(value));
        }

        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63
                || label.StartsWith('-')
                || label.EndsWith('-')
                || !label.All(IsAsciiDomainCharacter))
            {
                throw new ArgumentException("Domain etiketi geçersiz.", nameof(value));
            }
        }

        return wildcard ? "*." + ascii : ascii;
    }

    private static string ToAsciiDomain(string domain)
    {
        try
        {
            var ascii = new IdnMapping().GetAscii(domain).TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ascii))
            {
                throw new ArgumentException("Domain boş olamaz.", nameof(domain));
            }

            return ascii;
        }
        catch (ArgumentException)
        {
            throw;
        }
    }

    private static bool IsAsciiDomainCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            || value is >= '0' and <= '9'
            || value == '-';
    }
}
