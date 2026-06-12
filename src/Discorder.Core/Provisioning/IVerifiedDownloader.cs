namespace Discorder.Core.Provisioning;

public interface IVerifiedDownloader
{
    Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null);
}

public sealed record DownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double? Percent,
    string? Message = null,
    int? Attempt = null,
    int? MaxAttempts = null,
    bool IsRetry = false);
