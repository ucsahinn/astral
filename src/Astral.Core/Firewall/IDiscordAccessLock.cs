using Astral.Core.Targets;

namespace Astral.Core.Firewall;

public interface IDiscordAccessLock
{
    Task EnableAsync(CancellationToken cancellationToken);

    Task EnableAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        return EnableAsync(cancellationToken);
    }

    Task DisableAsync(CancellationToken cancellationToken);

    Task ApplyTunnelScopeAsync(
        bool includeBrowserAccess,
        CancellationToken cancellationToken);

    Task ApplyTunnelScopeAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        return ApplyTunnelScopeAsync(
            routingPlan.RequiresWebProxy,
            cancellationToken);
    }

    Task ClearTunnelScopeAsync(CancellationToken cancellationToken);

    Task RemoveAsync(CancellationToken cancellationToken);
}
