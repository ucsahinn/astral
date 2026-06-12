using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Discorder.Core.Updates;

using var progress = UpdateProgressPresenter.Start();
return await DiscorderUpdater.RunAsync(args, progress);

internal static class DiscorderUpdater
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FailureWindowTimeout = TimeSpan.FromMinutes(10);

    public static async Task<int> RunAsync(
        string[] args,
        UpdateProgressPresenter progress)
    {
        UpdateOptions options;
        try
        {
            options = UpdateOptions.Parse(args);
        }
        catch (Exception exception)
        {
            progress.Fail(
                "Güncelleme başlatılamadı",
                exception.Message);
            Console.Error.WriteLine(exception.Message);
            progress.WaitForClose(FailureWindowTimeout);
            return 2;
        }

        var log = new UpdateLog(options.LogPath);
        try
        {
            progress.Report(
                4,
                "Güncelleme başlatılıyor",
                "Discorder'ın kapanması bekleniyor.");
            log.Write("Update helper started.");
            await WaitForDiscorderToExitAsync(options.ProcessId, log);

            progress.Report(
                18,
                "Paket doğrulanıyor",
                "İndirilen ZIP dosyası kontrol ediliyor.");
            var packageHash = UpdatePackageValidator.ComputeSha256(options.PackagePath);
            if (!string.Equals(
                    packageHash,
                    options.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Update package hash changed before apply.");
            }

            progress.Report(
                32,
                "Paket açılıyor",
                "Dosyalar güvenli staging alanında hazırlanıyor.");
            var updateRoot = Path.GetDirectoryName(options.PackagePath)
                ?? Path.GetTempPath();
            var applyPayload = Path.Combine(
                updateRoot,
                "apply-payload-" + Guid.NewGuid().ToString("N"));
            UpdatePackageValidator.ExtractToDirectory(
                options.PackagePath,
                applyPayload,
                options.ExecutableName,
                options.ExpectedVersion,
                options.ExpectedSignerThumbprint,
                options.ExpectedSha256);
            progress.Report(
                48,
                "Yeni sürüm doğrulanıyor",
                "Manifest ve dosya hashleri kontrol ediliyor.");
            var newManifest = UpdatePackageValidator.ValidatePayload(
                applyPayload,
                options.ExecutableName,
                options.ExpectedVersion,
                options.ExpectedSignerThumbprint);

            ApplyPayload(
                applyPayload,
                options.TargetDirectory,
                newManifest,
                options.ExecutableName,
                options.ExpectedVersion,
                options.ExpectedSignerThumbprint,
                log,
                progress);

            var executablePath = Path.Combine(
                options.TargetDirectory,
                options.ExecutableName);
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "Updated executable not found.",
                    executablePath);
            }

            progress.Report(
                94,
                "Discorder yeniden açılıyor",
                "Yeni sürüm başlatılıyor.");
            log.Write("Starting updated Discorder.");
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = options.TargetDirectory,
                UseShellExecute = false
            });

            log.Write("Update completed.");
            progress.Succeed(
                "Güncelleme tamamlandı",
                "Discorder yeni sürümle açılıyor.");
            await Task.Delay(1400);
            return 0;
        }
        catch (Exception exception)
        {
            log.Write("Update failed: " + exception.Message);
            progress.Fail(
                "Güncelleme tamamlanamadı",
                "Mevcut sürüm korundu. " + exception.Message);
            progress.WaitForClose(FailureWindowTimeout);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task WaitForDiscorderToExitAsync(
        int processId,
        UpdateLog log)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            log.Write("Waiting for Discorder to exit.");
            using var timeout = new CancellationTokenSource(ProcessExitTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "Discorder did not exit before the update timeout.");
            }
        }
        catch (ArgumentException)
        {
            log.Write("Discorder process was already closed.");
        }
    }

    private static void ApplyPayload(
        string payloadDirectory,
        string targetDirectory,
        UpdateManifest newManifest,
        string executableName,
        string expectedVersion,
        string? expectedSignerThumbprint,
        UpdateLog log,
        UpdateProgressPresenter progress)
    {
        var targetRoot = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(targetRoot))
        {
            throw new DirectoryNotFoundException(targetRoot);
        }
        RejectReparsePointsInExistingTree(targetRoot);

        var backupRoot = Path.Combine(
            Path.GetDirectoryName(payloadDirectory) ?? Path.GetTempPath(),
            "backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);

        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copiedWithoutBackup = new List<string>();
        var filesToInstall = newManifest.Files
            .Select(file => UpdatePackageValidator.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        filesToInstall.Add(UpdatePackageValidator.ManifestFileName);

        var staleFiles = ReadOldManifest(targetRoot, log)?
            .Files
            .Select(file => UpdatePackageValidator.NormalizeRelativePath(file.Path))
            .Where(path => !filesToInstall.Contains(path))
            .ToArray() ?? [];

        try
        {
            foreach (var relativePath in filesToInstall.Concat(staleFiles)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                BackupTargetFile(targetRoot, backupRoot, relativePath, backups);
            }

            var installIndex = 0;
            var installCount = Math.Max(1, newManifest.Files.Count);
            foreach (var file in newManifest.Files)
            {
                installIndex++;
                var installPercent = 58 + (installIndex * 28d / installCount);
                progress.Report(
                    Math.Min(86, installPercent),
                    "Dosyalar güncelleniyor",
                    FormatInstallDetail(file.Path, installIndex, installCount));
                var relativePath = UpdatePackageValidator.NormalizeRelativePath(file.Path);
                var source = UpdatePackageValidator.GetSafePath(
                    payloadDirectory,
                    relativePath);
                var destination = UpdatePackageValidator.GetSafePath(
                    targetRoot,
                    relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                RejectReparsePointsInExistingAncestors(targetRoot, destination);
                if (!backups.ContainsKey(destination))
                {
                    copiedWithoutBackup.Add(destination);
                }

                File.Copy(source, destination, overwrite: true);
            }

            progress.Report(
                88,
                "Son kontrol yapılıyor",
                "Yeni dosyalar doğrulanıyor.");
            CopyManifest(
                targetRoot,
                newManifest,
                backups,
                copiedWithoutBackup);

            UpdatePackageValidator.ValidatePayload(
                targetRoot,
                executableName,
                expectedVersion,
                expectedSignerThumbprint,
                newManifest);
            log.Write("Payload applied.");
        }
        catch
        {
            log.Write("Restoring previous files.");
            RestoreBackup(backups, copiedWithoutBackup, log);
            throw;
        }
    }

    private static void CopyManifest(
        string targetRoot,
        UpdateManifest manifest,
        Dictionary<string, string> backups,
        List<string> copiedWithoutBackup)
    {
        var destination = UpdatePackageValidator.GetSafePath(
            targetRoot,
            UpdatePackageValidator.ManifestFileName);
        if (!backups.ContainsKey(destination))
        {
            copiedWithoutBackup.Add(destination);
        }

        UpdatePackageValidator.WriteManifest(targetRoot, manifest);
    }

    private static UpdateManifest? ReadOldManifest(
        string targetRoot,
        UpdateLog log)
    {
        try
        {
            return UpdatePackageValidator.TryReadManifest(targetRoot);
        }
        catch (Exception exception)
        {
            log.Write("Existing manifest ignored: " + exception.Message);
            return null;
        }
    }

    private static void BackupTargetFile(
        string targetRoot,
        string backupRoot,
        string relativePath,
        IDictionary<string, string> backups)
    {
        var source = UpdatePackageValidator.GetSafePath(targetRoot, relativePath);
        if (!File.Exists(source))
        {
            return;
        }
        RejectReparsePoint(source);

        var backup = UpdatePackageValidator.GetSafePath(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Move(source, backup, overwrite: true);
        backups[source] = backup;
    }

    private static string FormatInstallDetail(
        string path,
        int index,
        int total)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = path;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{index}/{total} {fileName}");
    }

    private static void RejectReparsePointsInExistingTree(string rootDirectory)
    {
        RejectReparsePoint(rootDirectory);
        var pending = new Stack<string>();
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(
                         current,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                RejectReparsePoint(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         current,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                RejectReparsePoint(directory);
                pending.Push(directory);
            }
        }
    }

    private static void RejectReparsePointsInExistingAncestors(
        string rootDirectory,
        string path)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var current = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrWhiteSpace(current)
            && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(current))
            {
                RejectReparsePoint(current);
            }

            if (string.Equals(
                    current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var info = File.GetAttributes(path);
        if (info.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "Güncelleme hedefinde junction veya symlink bulunamaz.");
        }
    }

    private static void RestoreBackup(
        IReadOnlyDictionary<string, string> backups,
        IEnumerable<string> copiedWithoutBackup,
        UpdateLog log)
    {
        foreach (var path in copiedWithoutBackup)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                log.Write("Could not remove partial file: " + exception.Message);
            }
        }

        foreach (var item in backups)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);
                File.Move(item.Value, item.Key, overwrite: true);
            }
            catch (Exception exception)
            {
                log.Write("Could not restore backup: " + exception.Message);
            }
        }
    }
}

internal sealed record UpdateOptions(
    int ProcessId,
    string PackagePath,
    string ExpectedSha256,
    string ExpectedVersion,
    string? ExpectedSignerThumbprint,
    string TargetDirectory,
    string ExecutableName,
    string LogPath)
{
    public static UpdateOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length)
            {
                throw new ArgumentException("Update helper arguments are invalid.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        return new UpdateOptions(
            int.Parse(
                Require(values, "process-id"),
                CultureInfo.InvariantCulture),
            Path.GetFullPath(Require(values, "package")),
            Require(values, "expected-sha256"),
            Require(values, "expected-version"),
            Optional(values, "expected-signer-thumbprint"),
            Path.GetFullPath(Require(values, "target-directory")),
            Require(values, "executable-name"),
            Path.GetFullPath(Require(values, "log")));
    }

    private static string Require(
        Dictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Missing update helper argument: --{name}");
        }

        return value;
    }

    private static string? Optional(
        Dictionary<string, string> values,
        string name)
    {
        return values.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }
}

internal sealed class UpdateLog
{
    private readonly string _path;

    public UpdateLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(string message)
    {
        var line = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:O} {message}");
        File.AppendAllText(_path, line + Environment.NewLine);
    }
}

internal sealed class UpdateProgressPresenter : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private UpdateProgressForm? _form;
    private bool _disposed;

    private UpdateProgressPresenter()
    {
        _thread = new Thread(RunWindow)
        {
            IsBackground = false,
            Name = "Discorder update progress"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public static UpdateProgressPresenter Start() => new();

    public void Report(
        double percent,
        string message,
        string? detail = null)
    {
        if (_disposed)
        {
            return;
        }

        Post(form => form.SetProgress(percent, message, detail, failed: false));
    }

    public void Succeed(string message, string detail)
    {
        if (_disposed)
        {
            return;
        }

        Post(form => form.SetProgress(100, message, detail, failed: false));
    }

    public void Fail(string message, string detail)
    {
        if (_disposed)
        {
            return;
        }

        Post(form => form.SetProgress(100, message, detail, failed: true));
    }

    public void WaitForClose(TimeSpan timeout)
    {
        if (_thread.IsAlive && !_thread.Join(timeout))
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Post(form => form.CloseFromPresenter());
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }

    private void RunWindow()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new UpdateProgressForm();
        _form = form;
        _ready.Set();
        Application.Run(form);
    }

    private void Post(Action<UpdateProgressForm> action)
    {
        if (!_ready.IsSet)
        {
            return;
        }

        var form = _form;
        if (form is null || form.IsDisposed)
        {
            return;
        }

        try
        {
            if (form.InvokeRequired)
            {
                form.BeginInvoke(new Action(() => action(form)));
            }
            else
            {
                action(form);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly Label _detail;
    private readonly ProgressBar _progress;
    private readonly Button _closeButton;
    private bool _canClose;

    public UpdateProgressForm()
    {
        Text = "Discorder güncelleme";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 212);
        BackColor = Color.FromArgb(16, 24, 38);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f, FontStyle.Regular);

        _title = new Label
        {
            AutoSize = false,
            Text = "Discorder güncelleniyor",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(24, 22),
            Size = new Size(392, 30)
        };

        _message = new Label
        {
            AutoSize = false,
            Text = "Hazırlanıyor",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 232, 255),
            Location = new Point(24, 70),
            Size = new Size(392, 24)
        };

        _detail = new Label
        {
            AutoSize = false,
            Text = "Lütfen bu pencere kapanana kadar bekleyin.",
            ForeColor = Color.FromArgb(205, 215, 230),
            Location = new Point(24, 98),
            Size = new Size(392, 40)
        };

        _progress = new ProgressBar
        {
            Location = new Point(24, 146),
            Size = new Size(392, 16),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };

        _closeButton = new Button
        {
            Text = "Kapat",
            Location = new Point(326, 174),
            Size = new Size(90, 28),
            Visible = false
        };
        _closeButton.Click += (_, _) => CloseFromPresenter();

        Controls.AddRange([
            _title,
            _message,
            _detail,
            _progress,
            _closeButton
        ]);
    }

    public void SetProgress(
        double percent,
        string message,
        string? detail,
        bool failed)
    {
        var value = (int)Math.Clamp(Math.Round(percent), 0, 100);
        _progress.Value = value;
        _message.Text = message;
        _message.ForeColor = failed
            ? Color.FromArgb(255, 102, 130)
            : Color.FromArgb(112, 232, 255);
        _detail.Text = string.IsNullOrWhiteSpace(detail)
            ? "Lütfen bu pencere kapanana kadar bekleyin."
            : SanitizeDisplayText(detail);
        _closeButton.Visible = failed;
    }

    public void CloseFromPresenter()
    {
        _canClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private static string SanitizeDisplayText(string text)
    {
        var clean = new string(text
            .Where(character => !char.IsControl(character) || char.IsWhiteSpace(character))
            .ToArray())
            .ReplaceLineEndings(" ")
            .Trim();

        const int maxLength = 150;
        return clean.Length <= maxLength
            ? clean
            : clean[..(maxLength - 1)] + "…";
    }
}
