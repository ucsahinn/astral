using Astral.Core.Security;
using System.Net;

namespace Astral.Core.Provisioning;

public sealed class VerifiedDownloader : IVerifiedDownloader
{
    private const int DefaultMaxAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultReadIdleTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _readIdleTimeout;

    public VerifiedDownloader(
        HttpClient httpClient,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? retryDelay = null,
        TimeSpan? readIdleTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "En az bir indirme denemesi yapılmalıdır.");
        }

        _readIdleTimeout = readIdleTimeout ?? DefaultReadIdleTimeout;
        if (_readIdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readIdleTimeout),
                "İndirme veri bekleme süresi pozitif olmalıdır.");
        }

        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (await FileHashVerifier.MatchesSha256Async(
                destination,
                expectedSha256,
                cancellationToken))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".download";

        try
        {
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    progress?.Report(new DownloadProgress(
                        0,
                        maxBytes,
                        0,
                        Message: "İndirme bağlantısı kuruluyor.",
                        Attempt: attempt,
                        MaxAttempts: _maxAttempts));
                    await DownloadOnceAsync(
                        source,
                        temporaryPath,
                        destination,
                        expectedSha256,
                        maxBytes,
                        cancellationToken,
                        progress);
                    return;
                }
                catch (Exception exception) when (
                    attempt < _maxAttempts
                    && IsTransientDownloadFailure(exception, cancellationToken))
                {
                    TryDelete(temporaryPath);
                    progress?.Report(new DownloadProgress(
                        0,
                        maxBytes,
                        null,
                        Message: "Bağlantı kurulamadı, tekrar deneniyor.",
                        Attempt: attempt + 1,
                        MaxAttempts: _maxAttempts,
                        IsRetry: true));
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task DownloadOnceAsync(
        Uri source,
        string temporaryPath,
        string destination,
        string expectedSha256,
        long? maxBytes,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress>? progress)
    {
        using var response = await _httpClient.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{source.Host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        if (maxBytes is not null
            && response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength.Value > maxBytes.Value)
        {
            throw new InvalidDataException(
                $"{source.Host} dosyası beklenenden büyük.");
        }

        var contentLength = response.Content.Headers.ContentLength;
        var hasExactContentLength = contentLength is > 0;
        var totalBytes = hasExactContentLength
            ? contentLength.GetValueOrDefault()
            : maxBytes;

        progress?.Report(new DownloadProgress(0, totalBytes, 0));

        await using (var sourceStream = await response.Content.ReadAsStreamAsync(
                         cancellationToken))
        await using (var destinationStream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await CopyToAsync(
                sourceStream,
                destinationStream,
                maxBytes,
                totalBytes,
                hasExactContentLength,
                _readIdleTimeout,
                progress,
                cancellationToken);
        }

        if (!await FileHashVerifier.MatchesSha256Async(
                temporaryPath,
                expectedSha256,
                cancellationToken))
        {
            throw new InvalidDataException(
                $"{source.Host} için SHA-256 doğrulaması başarısız oldu.");
        }

        File.Move(temporaryPath, destination, overwrite: true);
    }

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? maxBytes,
        long? expectedBytes,
        bool expectedBytesAreExact,
        TimeSpan readIdleTimeout,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        var bytesReceived = 0L;
        while (true)
        {
            var bytesRead = await ReadWithIdleTimeoutAsync(
                source,
                buffer,
                readIdleTimeout,
                cancellationToken);
            if (bytesRead == 0)
            {
                if (expectedBytesAreExact
                    && expectedBytes is > 0
                    && bytesReceived != expectedBytes.Value)
                {
                    throw new InvalidDataException(
                        "İndirilen dosya beklenen boyuta ulaşmadı.");
                }

                progress?.Report(new DownloadProgress(
                    bytesReceived,
                    expectedBytesAreExact ? expectedBytes : bytesReceived,
                    100));
                return;
            }

            bytesReceived += bytesRead;
            if (maxBytes is not null && bytesReceived > maxBytes.Value)
            {
                throw new InvalidDataException(
                    "İndirilen dosya beklenenden büyük.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);

            double? percent = null;
            if (expectedBytes is > 0)
            {
                percent = Math.Clamp(
                    bytesReceived * 100d / expectedBytes.Value,
                    0,
                    100);
            }

            progress?.Report(new DownloadProgress(
                bytesReceived,
                expectedBytes,
                percent));
        }
    }

    private static async ValueTask<int> ReadWithIdleTimeoutAsync(
        Stream source,
        Memory<byte> buffer,
        TimeSpan readIdleTimeout,
        CancellationToken cancellationToken)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        readTimeout.CancelAfter(readIdleTimeout);

        try
        {
            return await source.ReadAsync(buffer, readTimeout.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested
                  && readTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "İndirme sırasında veri akışı zaman aşımına uğradı.",
                exception);
        }
    }

    private static bool IsTransientDownloadFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            TaskCanceledException => true,
            OperationCanceledException => true,
            TimeoutException => true,
            HttpRequestException httpException
                => IsTransientStatusCode(httpException.StatusCode),
            _ => false
        };
    }

    private static bool IsTransientStatusCode(HttpStatusCode? statusCode)
    {
        if (statusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return (int)statusCode.Value >= 500;
    }

    private TimeSpan GetRetryDelay(int failedAttempt)
    {
        if (_retryDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * failedAttempt);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
