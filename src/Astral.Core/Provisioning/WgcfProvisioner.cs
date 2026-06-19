using System.Text.RegularExpressions;
using Astral.Core.Configuration;
using Astral.Core.Infrastructure;
using Astral.Core.Profiles;

namespace Astral.Core.Provisioning;

public sealed class WgcfProvisioner : IProfileProvisioner
{
    private const long ProgressByteInterval = 1024 * 1024;
    private const double ProgressPercentInterval = 5;
    private const int FailureDiagnosticMaxLength = 320;
    private static readonly Regex SensitiveFailureAssignmentPattern = new(
        @"(?ix)
        \b(
            privatekey|private[_-]?key|presharedkey|preshared[_-]?key|
            token|access[_-]?token|refresh[_-]?token|password|passwd|
            secret|client[_-]?secret|cookie|authorization|api[_-]?key|
            x-api-key|session|jwt|set-cookie|connection[_-]?string
        )\b
        \s*[:=]\s*
        [^\r\n]+",
        RegexOptions.Compiled);
    private static readonly Regex BearerFailurePattern = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.Compiled);

    public const string Version = "2.2.31";
    public const long WindowsX64MaxBytes = 32L * 1024 * 1024;
    public const string WindowsX64Sha256 =
        "38cad8ab9cf44f8ec25c8a4e99179b1ee3510dd207e654c6aa1f6786e16d404c";

    public static readonly Uri WindowsX64Download = new(
        "https://github.com/ViRb3/wgcf/releases/download/v2.2.31/" +
        "wgcf_2.2.31_windows_amd64.exe");

    private readonly AppPaths _paths;
    private readonly IVerifiedDownloader _downloader;
    private readonly ICommandRunner _commandRunner;

    public WgcfProvisioner(
        AppPaths paths,
        IVerifiedDownloader downloader,
        ICommandRunner commandRunner)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public async Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedApplications);
        _paths.EnsureDirectories();

        progress?.Report("Cloudflare WARP aracı hazırlanıyor");
        double? lastProgressPercent = null;
        long lastProgressBytes = -ProgressByteInterval;
        var downloadProgress = new DirectProgress<DownloadProgress>(
            download =>
            {
                if (ShouldReportDownloadProgress(
                    download,
                    ref lastProgressPercent,
                    ref lastProgressBytes))
                {
                    progress?.Report(FormatDownloadProgress(
                        "Cloudflare WARP aracı",
                        download));
                }
            });

        await _downloader.DownloadAsync(
            WindowsX64Download,
            _paths.WgcfExecutable,
            WindowsX64Sha256,
            cancellationToken,
            maxBytes: WindowsX64MaxBytes,
            progress: downloadProgress);
        progress?.Report("Cloudflare WARP aracı hazır");

        if (!File.Exists(_paths.WgcfAccount))
        {
            progress?.Report("Cloudflare WARP hesabı hazırlanıyor");
            var registerResult = await _commandRunner.RunAsync(
                _paths.WgcfExecutable,
                ["register", "--accept-tos"],
                _paths.ProfileDirectory,
                TimeSpan.FromMinutes(2),
                cancellationToken);

            EnsureSucceeded("Cloudflare WARP kaydı", registerResult);

            if (!File.Exists(_paths.WgcfAccount))
            {
                throw new InvalidDataException(
                    "wgcf hesap dosyası oluşturmadan tamamlandı.");
            }
        }

        progress?.Report("Cloudflare WARP profili hazırlanıyor");
        var generateResult = await _commandRunner.RunAsync(
            _paths.WgcfExecutable,
            ["generate"],
            _paths.ProfileDirectory,
            TimeSpan.FromMinutes(1),
            cancellationToken);

        if (!generateResult.Succeeded && File.Exists(_paths.WgcfBaseProfile))
        {
            progress?.Report(
                "Cloudflare WARP profili yenilenemedi; mevcut profil kullanılacak");
        }
        else
        {
            EnsureSucceeded("WireGuard profil üretimi", generateResult);
        }

        if (!File.Exists(_paths.WgcfBaseProfile))
        {
            throw new InvalidDataException(
                "wgcf WireGuard profili oluşturmadan tamamlandı.");
        }

        var sourceProfile = await File.ReadAllTextAsync(
            _paths.WgcfBaseProfile,
            cancellationToken);
        var scopedProfile = WireGuardProfileBuilder.BuildScopedProfile(
            sourceProfile,
            allowedApplications);

        await File.WriteAllTextAsync(
            _paths.ScopedProfile,
            scopedProfile,
            cancellationToken);

        progress?.Report("Hedef bağlantı profili hazır");
        return _paths.ScopedProfile;
    }

    private static void EnsureSucceeded(string operation, CommandResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            diagnostic = $"wgcf exit code {result.ExitCode}.";
        }

        throw new InvalidOperationException(
            $"{operation} çıkış kodu {result.ExitCode} ile başarısız oldu: " +
            SanitizeFailureDiagnostic(diagnostic));
    }

    private static string SanitizeFailureDiagnostic(string diagnostic)
    {
        var sanitized = SensitiveFailureAssignmentPattern.Replace(
            diagnostic,
            match =>
            {
                var equalsIndex = match.Value.IndexOf('=');
                var colonIndex = match.Value.IndexOf(':');
                var separatorIndex = equalsIndex < 0
                    ? colonIndex
                    : colonIndex < 0
                        ? equalsIndex
                        : Math.Min(equalsIndex, colonIndex);

                var key = separatorIndex < 0
                    ? match.Value.Trim()
                    : match.Value[..separatorIndex].TrimEnd();
                return key + " = [REDACTED]";
            });
        sanitized = BearerFailurePattern.Replace(sanitized, "Bearer [REDACTED]");
        sanitized = sanitized.Trim().ReplaceLineEndings(" ");
        if (sanitized.Length <= FailureDiagnosticMaxLength)
        {
            return sanitized;
        }

        return sanitized[..FailureDiagnosticMaxLength].TrimEnd() + " ...";
    }

    private static bool ShouldReportDownloadProgress(
        DownloadProgress progress,
        ref double? lastPercent,
        ref long lastBytes)
    {
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            return true;
        }

        if (progress.BytesReceived <= 0)
        {
            lastBytes = 0;
            lastPercent = progress.Percent;
            return true;
        }

        if (progress.Percent >= 100)
        {
            lastBytes = progress.BytesReceived;
            lastPercent = progress.Percent;
            return true;
        }

        if (progress.Percent is { } percent)
        {
            if (lastPercent is null
                || percent - lastPercent.Value >= ProgressPercentInterval)
            {
                lastPercent = percent;
                lastBytes = progress.BytesReceived;
                return true;
            }

            return false;
        }

        if (progress.BytesReceived - lastBytes >= ProgressByteInterval)
        {
            lastBytes = progress.BytesReceived;
            return true;
        }

        return false;
    }

    private static string FormatDownloadProgress(
        string label,
        DownloadProgress progress)
    {
        var attempt = progress.Attempt is not null && progress.MaxAttempts is not null
            ? $" ({progress.Attempt}/{progress.MaxAttempts})"
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            return $"{label}: {progress.Message}{attempt}";
        }

        if (progress.TotalBytes is > 0)
        {
            return $"{label} indiriliyor: {FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes.Value)}";
        }

        return $"{label} indiriliyor: {FormatBytes(progress.BytesReceived)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    private sealed class DirectProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public DirectProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}
