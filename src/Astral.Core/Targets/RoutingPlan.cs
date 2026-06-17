using Astral.Core.WebProxy;

namespace Astral.Core.Targets;

public sealed record RoutingPlan(
    IReadOnlyList<TargetDefinition> SelectedTargets,
    IReadOnlyList<string> AllowedApplications,
    IReadOnlyList<DomainPattern> ProxyRules)
{
    public bool RequiresWebProxy => ProxyRules.Count > 0;

    public string Summary => SelectedTargets.Count == 0
        ? "Hedef seçilmedi"
        : string.Join(", ", SelectedTargets.Select(target => target.Label));

    public static bool IsBrowserExecutable(string value)
    {
        var name = Path.GetFileName(value);
        return name.Equals("brave.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("chromium.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("opera.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("vivaldi.exe", StringComparison.OrdinalIgnoreCase);
    }
}
