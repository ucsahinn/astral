namespace Astral.App.Installation;

public interface IStartupLaunchService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
