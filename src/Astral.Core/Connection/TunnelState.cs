namespace Astral.Core.Connection;

public enum TunnelState
{
    Disconnected,
    Preparing,
    Connecting,
    Verifying,
    TargetActionRequired,
    Connected,
    Disconnecting,
    Error
}
