using Astral.Core.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;

namespace Astral.Core.Connection;

public interface ITunnelReadinessProbe
{
    TunnelReadinessSnapshot Capture();
}

internal sealed class NullTunnelReadinessProbe : ITunnelReadinessProbe
{
    public static readonly NullTunnelReadinessProbe Instance = new();

    private NullTunnelReadinessProbe()
    {
    }

    public TunnelReadinessSnapshot Capture() =>
        TunnelReadinessSnapshot.ProbeNotConfigured(
            "Tunnel readiness probe is not configured.");
}

public sealed record TunnelReadinessSnapshot(
    string Status,
    bool WireSockAdapterDetected,
    bool WireSockAdapterUp,
    string? WireSockAdapterStatus,
    long? WireSockAdapterBytesReceived,
    long? WireSockAdapterBytesSent,
    string? Diagnostic)
{
    public const string ReadyStatus = "ready";
    public const string WireSockAdapterInactiveStatus = "wiresock-adapter-inactive";
    public const string WireSockAdapterNotDetectedStatus = "wiresock-adapter-not-detected";
    public const string TransparentProcessRunningStatus = "transparent-process-running";
    public const string ProbeUnavailableStatus = "probe-unavailable";
    public const string ProbeNotConfiguredStatus = "probe-not-configured";

    public bool BlocksConnection =>
        Status is WireSockAdapterInactiveStatus
            or WireSockAdapterNotDetectedStatus
            or ProbeUnavailableStatus;

    public static TunnelReadinessSnapshot Ready(
        string? adapterStatus,
        long? bytesReceived,
        long? bytesSent) =>
        new(
            ReadyStatus,
            WireSockAdapterDetected: true,
            WireSockAdapterUp: true,
            adapterStatus,
            bytesReceived,
            bytesSent,
            Diagnostic: null);

    public static TunnelReadinessSnapshot WireSockAdapterInactive(
        string? adapterStatus,
        long? bytesReceived,
        long? bytesSent,
        string? diagnostic) =>
        new(
            WireSockAdapterInactiveStatus,
            WireSockAdapterDetected: true,
            WireSockAdapterUp: false,
            adapterStatus,
            bytesReceived,
            bytesSent,
            diagnostic);

    public static TunnelReadinessSnapshot WireSockAdapterNotDetected() =>
        new(
            WireSockAdapterNotDetectedStatus,
            WireSockAdapterDetected: false,
            WireSockAdapterUp: false,
            WireSockAdapterStatus: null,
            WireSockAdapterBytesReceived: null,
            WireSockAdapterBytesSent: null,
            Diagnostic: "WireSock network adapter was not detected.");

    public static TunnelReadinessSnapshot TransparentProcessRunning(
        TunnelReadinessSnapshot captured,
        string? diagnostic) =>
        new(
            TransparentProcessRunningStatus,
            captured.WireSockAdapterDetected,
            captured.WireSockAdapterUp,
            captured.WireSockAdapterStatus,
            captured.WireSockAdapterBytesReceived,
            captured.WireSockAdapterBytesSent,
            diagnostic);

    public static TunnelReadinessSnapshot ProbeUnavailable(string? diagnostic) =>
        new(
            ProbeUnavailableStatus,
            WireSockAdapterDetected: false,
            WireSockAdapterUp: false,
            WireSockAdapterStatus: null,
            WireSockAdapterBytesReceived: null,
            WireSockAdapterBytesSent: null,
            diagnostic);

    public static TunnelReadinessSnapshot ProbeNotConfigured(string? diagnostic) =>
        new(
            ProbeNotConfiguredStatus,
            WireSockAdapterDetected: false,
            WireSockAdapterUp: false,
            WireSockAdapterStatus: null,
            WireSockAdapterBytesReceived: null,
            WireSockAdapterBytesSent: null,
            diagnostic);

    public Dictionary<string, string?> ToDiagnosticDetails()
    {
        var details = new Dictionary<string, string?>
        {
            ["tunnelReadiness"] = Status,
            ["wireSockAdapterDetected"] = WireSockAdapterDetected.ToString(),
            ["wireSockAdapterUp"] = WireSockAdapterUp.ToString(),
            ["wireSockAdapterStatus"] = WireSockAdapterStatus,
            ["tunnelReadinessDiagnostic"] = Diagnostic
        };

        if (WireSockAdapterBytesReceived is not null)
        {
            details["wireSockAdapterBytesReceived"] =
                WireSockAdapterBytesReceived.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (WireSockAdapterBytesSent is not null)
        {
            details["wireSockAdapterBytesSent"] =
                WireSockAdapterBytesSent.Value.ToString(CultureInfo.InvariantCulture);
        }

        return details;
    }
}

public sealed class WindowsTunnelReadinessProbe : ITunnelReadinessProbe
{
    public TunnelReadinessSnapshot Capture()
    {
        try
        {
            var adapters = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(IsWireSockAdapter)
                .OrderByDescending(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up)
                .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (adapters.Length == 0)
            {
                return TunnelReadinessSnapshot.WireSockAdapterNotDetected();
            }

            var adapter = adapters[0];
            var (bytesReceived, bytesSent) = CaptureTrafficCounters(adapter);
            var status = adapter.OperationalStatus.ToString();

            if (adapter.OperationalStatus == OperationalStatus.Up)
            {
                return TunnelReadinessSnapshot.Ready(
                    status,
                    bytesReceived,
                    bytesSent);
            }

            var description = AstralDiagnostics.RedactForLog(adapter.Description)
                ?? "WireSock adapter";
            return TunnelReadinessSnapshot.WireSockAdapterInactive(
                status,
                bytesReceived,
                bytesSent,
                $"{description} status is {status}.");
        }
        catch (Exception exception)
            when (exception is NetworkInformationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            return TunnelReadinessSnapshot.ProbeUnavailable(
                AstralDiagnostics.RedactForLog(exception.Message));
        }
    }

    private static bool IsWireSockAdapter(NetworkInterface adapter) =>
        IsWireSockCompatibleAdapter(adapter.Name, adapter.Description);

    internal static bool IsWireSockCompatibleAdapter(
        string? name,
        string? description)
    {
        if (ContainsWireSock(name) || ContainsWireSock(description))
        {
            return true;
        }

        return IsWireSockWireGuardTunnelName(name)
            && ContainsWireGuardTunnel(description);
    }

    private static bool ContainsWireSock(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains("WireSock", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWireGuardTunnel(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains("WireGuard Tunnel", StringComparison.OrdinalIgnoreCase);

    private static bool IsWireSockWireGuardTunnelName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length < 3
            || !value.StartsWith("wt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value[2..].All(char.IsDigit);
    }

    private static (long? BytesReceived, long? BytesSent) CaptureTrafficCounters(
        NetworkInterface adapter)
    {
        try
        {
            var statistics = adapter.GetIPv4Statistics();
            return (statistics.BytesReceived, statistics.BytesSent);
        }
        catch (NetworkInformationException)
        {
            return (null, null);
        }
    }
}
