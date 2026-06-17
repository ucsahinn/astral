using System.Globalization;
using System.Text;

namespace Astral.Core.WebProxy;

public static class ProxyPacScriptBuilder
{
    public static string Build(
        IReadOnlyList<DomainPattern> patterns,
        string proxyHost,
        int proxyPort)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyHost);
        if (proxyPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(proxyPort));
        }

        var orderedPatterns = patterns
            .DistinctBy(pattern => pattern.Pattern, StringComparer.OrdinalIgnoreCase)
            .OrderBy(pattern => pattern.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var proxy = $"PROXY {proxyHost}:{proxyPort.ToString(CultureInfo.InvariantCulture)}";
        var builder = new StringBuilder();
        builder.AppendLine("function FindProxyForURL(url, host) {");
        builder.AppendLine("  host = host.toLowerCase();");

        foreach (var pattern in orderedPatterns)
        {
            if (pattern.IsWildcard)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  if (shExpMatch(host, \"{pattern.Pattern}\")) return \"{proxy}\";");
            }
            else
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  if (host == \"{pattern.Value}\" || dnsDomainIs(host, \"{pattern.Value}\")) return \"{proxy}\";");
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  if (dnsDomainIs(host, \".{pattern.Value}\")) return \"{proxy}\";");
            }
        }

        builder.AppendLine("  return \"DIRECT\";");
        builder.AppendLine("}");
        return builder.ToString();
    }
}
