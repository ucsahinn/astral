namespace Astral.Core.Discord;

public sealed class DiscordAppScope
{
    private static readonly (string InstallationName, string ProcessName)[] DiscordInstallations =
    [
        ("Discord", "Discord.exe"),
        ("DiscordPTB", "DiscordPTB.exe"),
        ("DiscordCanary", "DiscordCanary.exe"),
        ("DiscordDevelopment", "DiscordDevelopment.exe")
    ];

    private readonly string _localAppData;

    public DiscordAppScope(
        string? localAppData = null,
        string? programFiles = null,
        string? programFilesX86 = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _ = programFiles;
        _ = programFilesX86;
    }

    public IReadOnlyList<string> GetAllowedApplications(bool includeBrowserAccess = false)
    {
        _ = includeBrowserAccess;
        var allowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (installationName, processName) in DiscordInstallations)
        {
            allowed.Add(processName);
            if (!string.IsNullOrWhiteSpace(_localAppData))
            {
                var installationPath = Path.GetFullPath(
                    Path.Combine(_localAppData, installationName));

                AddDiscordInstallation(
                    allowed,
                    installationPath,
                    processName);
            }
        }

        return allowed.ToArray();
    }

    private static bool AddDiscordInstallation(
        SortedSet<string> allowed,
        string installationPath,
        string processName)
    {
        if (!IsSafeExistingDirectory(installationPath))
        {
            return false;
        }

        allowed.Add(installationPath);
        var executableFound = AddFileIfExists(
            allowed,
            Path.Combine(installationPath, processName));

        foreach (var applicationDirectory in EnumerateSafeDirectories(
                     installationPath,
                     "app-*"))
        {
            executableFound |= AddFileIfExists(
                allowed,
                Path.Combine(applicationDirectory, processName));
        }

        return executableFound;
    }

    private static string[] EnumerateSafeDirectories(
        string root,
        string searchPattern)
    {
        try
        {
            return Directory
                .EnumerateDirectories(root, searchPattern, SearchOption.TopDirectoryOnly)
                .Where(IsSafeExistingDirectory)
                .ToArray();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool AddFileIfExists(SortedSet<string> allowed, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            allowed.Add(fullPath);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSafeExistingDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

}
