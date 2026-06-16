using System.IO;

namespace Discorder.App;

public static class WindowsPresentationEnvironment
{
    public static void EnsureProcessEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory)
            || !Directory.Exists(windowsDirectory))
        {
            return;
        }

        EnsureVariable("SystemRoot", windowsDirectory);
        EnsureVariable("windir", windowsDirectory);
    }

    private static void EnsureVariable(string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(name)))
        {
            return;
        }

        Environment.SetEnvironmentVariable(name, value);
    }
}
