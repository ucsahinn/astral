namespace Discorder.Core.Infrastructure;

public interface IManagedProcess : IAsyncDisposable
{
    event EventHandler? Exited;

    bool HasExited { get; }

    bool ExitConfirmed { get; }

    int? ExitCode { get; }

    int ProcessId { get; }

    DateTimeOffset? StartTime { get; }

    Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
