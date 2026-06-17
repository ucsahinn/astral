namespace Astral.App.Installation;

public interface IWireSockUninstaller
{
    Task UninstallIfAstralInstalledAsync(
        bool installedByAstral,
        CancellationToken cancellationToken);
}
