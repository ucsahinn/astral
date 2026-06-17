using Microsoft.Win32;
using System.Diagnostics;

namespace Astral.App.Installation;

public sealed class WindowsStartupLaunchService : IStartupLaunchService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Astral";
    private static readonly string LegacyValueName = string.Concat("Dis", "corder");
    private const string StartupArgument = "--background-start";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(
                GetExecutablePath(),
                StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException(
                "Windows başlangıç kaydı açılamadı.");

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (enabled)
        {
            key.SetValue(
                ValueName,
                $"\"{GetExecutablePath()}\" {StartupArgument}",
                RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        using var process = Process.GetCurrentProcess();
        var modulePath = process.MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(modulePath))
        {
            return modulePath;
        }

        throw new InvalidOperationException(
            "Astral çalıştırılabilir dosya yolu bulunamadı.");
    }
}
