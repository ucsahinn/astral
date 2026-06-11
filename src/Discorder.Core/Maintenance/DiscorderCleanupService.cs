using Discorder.Core.Configuration;
using Discorder.Core.Firewall;

namespace Discorder.Core.Maintenance;

public sealed class DiscorderCleanupService
{
    private readonly AppPaths _paths;
    private readonly IDiscordAccessLock _accessLock;

    public DiscorderCleanupService(
        AppPaths paths,
        IDiscordAccessLock accessLock)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _accessLock = accessLock ?? throw new ArgumentNullException(nameof(accessLock));
    }

    public async Task CleanUninstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _accessLock.RemoveAsync(cancellationToken);
        DeleteDiscorderDataDirectory(cancellationToken);
    }

    public async Task RepairAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _accessLock.EnableAsync(cancellationToken);

        DeleteGeneratedDirectory(_paths.ProfileDirectory, cancellationToken);
        DeleteGeneratedDirectory(_paths.ToolsDirectory, cancellationToken);
        DeleteGeneratedDirectory(_paths.InstallerDirectory, cancellationToken);
        DeleteGeneratedDirectory(_paths.LogDirectory, cancellationToken);

        _paths.EnsureDirectories();
    }

    private void DeleteDiscorderDataDirectory(CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(_paths.DataDirectory);
        if (!directory.Exists)
        {
            return;
        }

        if (!string.Equals(directory.Name, "Discorder", StringComparison.Ordinal)
            || directory.Parent is null)
        {
            throw new InvalidOperationException(
                "Discorder veri klasoru beklenen konumda degil.");
        }

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                directory.Refresh();
                if (!directory.Exists)
                {
                    return;
                }

                directory.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
        }

        directory.Refresh();
        if (directory.Exists)
        {
            throw new IOException(
                "Discorder veri klasoru temizlenemedi: " +
                directory.FullName);
        }
    }

    private void DeleteGeneratedDirectory(
        string path,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        var dataRoot = Path.GetFullPath(_paths.DataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(directory.FullName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!target.StartsWith(
                dataRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Onarim klasoru Discorder veri kokunun disinda.");
        }

        DeleteDirectoryWithRetry(
            directory,
            "Discorder onarim klasoru temizlenemedi: ",
            cancellationToken);
    }

    private static void DeleteDirectoryWithRetry(
        DirectoryInfo directory,
        string failurePrefix,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                directory.Refresh();
                if (!directory.Exists)
                {
                    return;
                }

                directory.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
        }

        directory.Refresh();
        if (directory.Exists)
        {
            throw new IOException(failurePrefix + directory.FullName);
        }
    }
}
