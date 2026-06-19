using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Astral.Core.Diagnostics;

namespace Astral.Core.Infrastructure;

public sealed class ProcessLauncher : IProcessLauncher
{
    private static readonly ConcurrentDictionary<string, object> LogLocks =
        new(StringComparer.OrdinalIgnoreCase);
    internal static readonly TimeSpan DisposeKilledProcessExitTimeout =
        TimeSpan.FromMilliseconds(1200);
    internal static readonly TimeSpan DisposeLogPumpTimeout =
        TimeSpan.FromMilliseconds(600);

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} başlatılamadı.");
        }

        return new ManagedProcess(
            process,
            logPath,
            Path.GetFileNameWithoutExtension(executable));
    }

    private sealed class ManagedProcess : IManagedProcess
    {
        private readonly Process _process;
        private readonly string _processName;
        private readonly object _logLock;
        private readonly StreamWriter _log;
        private readonly Task _stdoutPump;
        private readonly Task _stderrPump;
        private bool _disposed;
        private bool _exitConfirmed;

        public ManagedProcess(
            Process process,
            string logPath,
            string? processName)
        {
            _process = process;
            _processName = string.IsNullOrWhiteSpace(processName)
                ? "Hedef"
                : processName;
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            _logLock = LogLocks.GetOrAdd(
                Path.GetFullPath(logPath),
                static _ => new object());
            _log = new StreamWriter(
                new FileStream(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            _process.Exited += OnExited;
            _stdoutPump = PumpAsync(_process.StandardOutput, _processName + ":OUT");
            _stderrPump = PumpAsync(_process.StandardError, _processName + ":ERR");
            WriteLog("SYS", $"{_processName} süreci başladı. PID={_process.Id}");
        }

        public event EventHandler? Exited;

        public bool HasExited
        {
            get
            {
                try
                {
                    if (_process.HasExited)
                    {
                        _exitConfirmed = true;
                        return true;
                    }

                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return _exitConfirmed;
                }
                catch (InvalidOperationException)
                {
                    _exitConfirmed = true;
                    return true;
                }
            }
        }

        public bool ExitConfirmed => _exitConfirmed || HasExited;

        public int? ExitCode
        {
            get
            {
                if (!HasExited)
                {
                    return null;
                }

                try
                {
                    return _process.ExitCode;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public int ProcessId => _process.Id;

        public DateTimeOffset? StartTime
        {
            get
            {
                try
                {
                    return new DateTimeOffset(_process.StartTime);
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        or NotSupportedException
                        or System.ComponentModel.Win32Exception)
                {
                    return null;
                }
            }
        }

        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (HasExited)
            {
                return;
            }

            try
            {
                var gracefulStopRequested = _process.CloseMainWindow();
                if (!gracefulStopRequested)
                {
                    _process.Kill(entireProcessTree: true);
                    await WaitForExitAfterKillAsync(timeout, cancellationToken);
                    return;
                }

                using var timeoutSource = new CancellationTokenSource(timeout);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);
                await _process.WaitForExitAsync(linkedSource.Token);
                _exitConfirmed = true;
            }
            catch (OperationCanceledException)
            {
                if (!HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await WaitForExitAfterKillAsync(timeout, cancellationToken);
                }
            }
        }

        private async Task WaitForExitAfterKillAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            try
            {
                await _process.WaitForExitAsync(linkedSource.Token);
                _exitConfirmed = true;
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                WriteLog("SYS", $"{_processName} zorla kapatma bekleme suresini asti.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _process.Exited -= OnExited;

            if (!HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(
                        DisposeKilledProcessExitTimeout);
                    await _process.WaitForExitAsync(timeout.Token);
                    _exitConfirmed = true;
                }
                catch (OperationCanceledException)
                {
                    WriteLog("SYS", $"{_processName} süreci kapanma bekleme süresini aştı.");
                }
                catch (InvalidOperationException)
                {
                    _exitConfirmed = true;
                }
            }
            else
            {
                _exitConfirmed = true;
            }

            try
            {
                await Task.WhenAll(_stdoutPump, _stderrPump)
                    .WaitAsync(DisposeLogPumpTimeout);
            }
            catch (TimeoutException)
            {
                WriteLog("SYS", $"{_processName} log akışı kapanma bekleme süresini aştı.");
            }
            WriteLog(
                "SYS",
                $"{_processName} süreci kapandı. Kod={ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}");
            _exitConfirmed = _exitConfirmed || HasExited;
            await _log.DisposeAsync();
            _process.Dispose();
        }

        private async Task PumpAsync(StreamReader reader, string channel)
        {
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    WriteLog(channel, line);
                }
            }
            catch (Exception exception)
                when (exception is IOException
                    or ObjectDisposedException
                    or InvalidOperationException)
            {
                // Process shutdown can close the pipe while the async pump is still unwinding.
            }
        }

        private void OnExited(object? sender, EventArgs e)
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private void WriteLog(string channel, string message)
        {
            var redactedMessage = AstralDiagnostics.RedactForLog(message)
                ?? string.Empty;
            lock (_logLock)
            {
                try
                {
                    _log.WriteLine(
                        $"{DateTimeOffset.Now:O} [{channel}] {redactedMessage.ReplaceLineEndings(" ")}");
                }
                catch (Exception exception)
                    when (exception is IOException
                        or ObjectDisposedException)
                {
                }
            }
        }
    }
}
