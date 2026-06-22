using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Astral.Core.Diagnostics;
using Astral.Core.Targets;

namespace Astral.Core.Connection;

public sealed class WindowsTargetApplicationProofProvider : ITargetApplicationProofProvider
{
    private readonly ITargetApplicationProcessProvider _processProvider;
    private readonly IOwnedTcpConnectionProvider _tcpConnectionProvider;
    private readonly ITargetHostAddressResolver _addressResolver;

    public WindowsTargetApplicationProofProvider()
        : this(
            new WindowsTargetApplicationProcessProvider(),
            new WindowsOwnedTcpConnectionProvider(),
            new DnsTargetHostAddressResolver())
    {
    }

    public WindowsTargetApplicationProofProvider(
        ITargetApplicationProcessProvider processProvider,
        IOwnedTcpConnectionProvider tcpConnectionProvider,
        ITargetHostAddressResolver addressResolver)
    {
        _processProvider = processProvider
            ?? throw new ArgumentNullException(nameof(processProvider));
        _tcpConnectionProvider = tcpConnectionProvider
            ?? throw new ArgumentNullException(nameof(tcpConnectionProvider));
        _addressResolver = addressResolver
            ?? throw new ArgumentNullException(nameof(addressResolver));
    }

    public async Task<TargetApplicationProofResult> VerifyAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        var targets = routingPlan.SelectedTargets
            .Where(target => target.HasApplicationScope)
            .ToArray();
        if (targets.Length == 0)
        {
            return TargetApplicationProofResult.NotRequired();
        }

        var tcpConnections = _tcpConnectionProvider.GetActiveTcpConnections();
        var verifiedTargetIds = new List<string>();
        var missingTargetIds = new List<string>();
        var diagnostics = new List<string>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetProcesses = _processProvider.GetProcesses(target.ExecutableHints);
            if (targetProcesses.Count == 0)
            {
                missingTargetIds.Add(target.Id);
                diagnostics.Add($"{target.Id}:process-not-running");
                continue;
            }

            var proofHosts = EnumerateProofHosts(target).ToArray();
            var proofAddresses = await ResolveProofAddressesAsync(
                proofHosts,
                cancellationToken);
            if (proofAddresses.Count == 0)
            {
                missingTargetIds.Add(target.Id);
                diagnostics.Add($"{target.Id}:probe-host-unresolved");
                continue;
            }

            var targetProcessIds = targetProcesses
                .Select(process => process.ProcessId)
                .ToHashSet();
            var hasOwnedConnection = tcpConnections.Any(connection =>
                targetProcessIds.Contains(connection.ProcessId)
                && connection.State is TcpState.Established
                && proofAddresses.Contains(connection.RemoteAddress));
            if (hasOwnedConnection)
            {
                verifiedTargetIds.Add(target.Id);
                continue;
            }

            missingTargetIds.Add(target.Id);
            diagnostics.Add(
                $"{target.Id}:no-owned-established-target-tcp;processes={targetProcesses.Count};hosts={proofHosts.Length}");
        }

        if (missingTargetIds.Count == 0)
        {
            return new TargetApplicationProofResult(
                Required: true,
                IsVerified: true,
                VerifiedTargetIds: verifiedTargetIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                MissingTargetIds: [],
                Message: "Selected application targets have owned TCP proof to their target hosts.");
        }

        return new TargetApplicationProofResult(
            Required: true,
            IsVerified: false,
            VerifiedTargetIds: verifiedTargetIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MissingTargetIds: missingTargetIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Message: "Selected application targets do not yet have target-owned TCP proof.",
            FailureKind: "TargetApplicationProofMissingOwnedConnection",
            Diagnostic: string.Join("; ", diagnostics));
    }

    private async Task<HashSet<IPAddress>> ResolveProofAddressesAsync(
        IReadOnlyList<string> proofHosts,
        CancellationToken cancellationToken)
    {
        var resolveTasks = proofHosts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(host => _addressResolver.ResolveAsync(
                host,
                cancellationToken))
            .ToArray();
        var resolvedHosts = await Task.WhenAll(resolveTasks);

        return resolvedHosts
            .SelectMany(addresses => addresses)
            .ToHashSet();
    }

    private static string[] EnumerateProofHosts(TargetDefinition target)
    {
        if (target.Metadata.TryGetValue("probeHosts", out var rawHosts)
            && !string.IsNullOrWhiteSpace(rawHosts))
        {
            var hosts = rawHosts
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(host => !host.Contains('*', StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(host => host, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (hosts.Length > 0)
            {
                return hosts;
            }
        }

        return target.Domains
            .Select(domain => domain.Pattern)
            .Where(pattern => !pattern.Contains('*', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public interface ITargetApplicationProcessProvider
{
    IReadOnlyList<TargetApplicationProcessInfo> GetProcesses(
        IReadOnlyList<ExecutableHint> executableHints);
}

public sealed record TargetApplicationProcessInfo(
    int ProcessId,
    string ProcessName);

public interface IOwnedTcpConnectionProvider
{
    IReadOnlyList<OwnedTcpConnectionInfo> GetActiveTcpConnections();
}

public sealed record OwnedTcpConnectionInfo(
    int ProcessId,
    IPAddress RemoteAddress,
    TcpState State);

public interface ITargetHostAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}

internal sealed class WindowsTargetApplicationProcessProvider
    : ITargetApplicationProcessProvider
{
    public IReadOnlyList<TargetApplicationProcessInfo> GetProcesses(
        IReadOnlyList<ExecutableHint> executableHints)
    {
        var processes = new List<TargetApplicationProcessInfo>();
        foreach (var processName in EnumerateProcessNames(executableHints))
        {
            Process[] runningProcesses;
            try
            {
                runningProcesses = Process.GetProcessesByName(processName);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception)
            {
                continue;
            }

            foreach (var process in runningProcesses)
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            processes.Add(new TargetApplicationProcessInfo(
                                process.Id,
                                process.ProcessName));
                        }
                    }
                    catch (Exception exception)
                        when (exception is InvalidOperationException
                            or System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
        }

        return processes
            .GroupBy(process => process.ProcessId)
            .Select(group => group.First())
            .OrderBy(process => process.ProcessId)
            .ToArray();
    }

    private static string[] EnumerateProcessNames(
        IReadOnlyList<ExecutableHint> executableHints) =>
        executableHints
            .Select(hint => Path.GetFileNameWithoutExtension(hint.FileName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
}

internal sealed class DnsTargetHostAddressResolver : ITargetHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return [];
        }

        try
        {
            return await Dns.GetHostAddressesAsync(
                host,
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is SocketException
                or ArgumentException
                or InvalidOperationException)
        {
            return [];
        }
    }
}

internal sealed class WindowsOwnedTcpConnectionProvider : IOwnedTcpConnectionProvider
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;

    public IReadOnlyList<OwnedTcpConnectionInfo> GetActiveTcpConnections() =>
        ReadIpv4Connections()
            .Concat(ReadIpv6Connections())
            .ToArray();

    private static List<OwnedTcpConnectionInfo> ReadIpv4Connections()
    {
        var bufferSize = 0;
        _ = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferSize,
            sort: true,
            ipVersion: AfInet,
            TcpTableClass.OwnerPidAll,
            reserved: 0);
        if (bufferSize <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedTcpTable(
                buffer,
                ref bufferSize,
                sort: true,
                ipVersion: AfInet,
                TcpTableClass.OwnerPidAll,
                reserved: 0);
            if (result != 0)
            {
                return [];
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<Tcp4RowOwnerPid>();
            var connections = new List<OwnedTcpConnectionInfo>(rowCount);
            for (var index = 0; index < rowCount; index++)
            {
                var row = Marshal.PtrToStructure<Tcp4RowOwnerPid>(
                    IntPtr.Add(rowPointer, index * rowSize));
                connections.Add(new OwnedTcpConnectionInfo(
                    Convert.ToInt32(row.OwningPid, CultureInfo.InvariantCulture),
                    new IPAddress(row.RemoteAddr),
                    ConvertTcpState(row.State)));
            }

            return connections;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static List<OwnedTcpConnectionInfo> ReadIpv6Connections()
    {
        var bufferSize = 0;
        _ = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferSize,
            sort: true,
            ipVersion: AfInet6,
            TcpTableClass.OwnerPidAll,
            reserved: 0);
        if (bufferSize <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = GetExtendedTcpTable(
                buffer,
                ref bufferSize,
                sort: true,
                ipVersion: AfInet6,
                TcpTableClass.OwnerPidAll,
                reserved: 0);
            if (result != 0)
            {
                return [];
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<Tcp6RowOwnerPid>();
            var connections = new List<OwnedTcpConnectionInfo>(rowCount);
            for (var index = 0; index < rowCount; index++)
            {
                var row = Marshal.PtrToStructure<Tcp6RowOwnerPid>(
                    IntPtr.Add(rowPointer, index * rowSize));
                if (row.RemoteAddr is null || row.RemoteAddr.Length != 16)
                {
                    continue;
                }

                connections.Add(new OwnedTcpConnectionInfo(
                    Convert.ToInt32(row.OwningPid, CultureInfo.InvariantCulture),
                    new IPAddress(row.RemoteAddr, row.RemoteScopeId),
                    ConvertTcpState(row.State)));
            }

            return connections;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static TcpState ConvertTcpState(uint state) =>
        Enum.IsDefined(typeof(TcpState), (int)state)
            ? (TcpState)(int)state
            : TcpState.Unknown;

    private enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Tcp4RowOwnerPid
    {
        public readonly uint State;
        public readonly uint LocalAddr;
        public readonly uint LocalPort;
        public readonly uint RemoteAddr;
        public readonly uint RemotePort;
        public readonly uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[]? LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[]? RemoteAddr;

        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        TcpTableClass tableClass,
        int reserved);
}
