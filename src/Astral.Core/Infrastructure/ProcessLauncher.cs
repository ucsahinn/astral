using System.Diagnostics;
using System.Globalization;
using System.Text;
using Astral.Core.Diagnostics;

namespace Astral.Core.Infrastructure;

public sealed class ProcessLauncher : IProcessLauncher
{
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

        return new ManagedProcess(process, logPath);
    }

    private sealed class ManagedProcess : IManagedProcess
    {
        private readonly Process _process;
        private readonly StreamWriter _log;
        private readonly Task _stdoutPump;
        private readonly Task _stderrPump;
        private bool _disposed;
        private bool _exitConfirmed;

        public ManagedProcess(Process process, string logPath)
        {
            _process = process;
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            _log = new StreamWriter(
                new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            _process.Exited += OnExited;
            _stdoutPump = PumpAsync(_process.StandardOutput, "OUT");
            _stderrPump = PumpAsync(_process.StandardError, "ERR");
            WriteLog("SYS", $"WireSock süreci başladı. PID={_process.Id}");
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
                    await _process.WaitForExitAsync(cancellationToken);
                    _exitConfirmed = true;
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
                    await _process.WaitForExitAsync(cancellationToken);
                    _exitConfirmed = true;
                }
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
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _process.WaitForExitAsync(timeout.Token);
                    _exitConfirmed = true;
                }
                catch (OperationCanceledException)
                {
                    WriteLog("SYS", "WireSock süreci kapanma bekleme süresini aştı.");
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
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                WriteLog("SYS", "WireSock log akışı kapanma bekleme süresini aştı.");
            }
            WriteLog(
                "SYS",
                $"WireSock süreci kapandı. Kod={ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}");
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
            lock (_log)
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
