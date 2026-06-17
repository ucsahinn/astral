using Astral.Core.Configuration;

namespace Astral.Core.Targets;

public sealed class TargetSelectionStore
{
    private readonly AppSettingsStore _settingsStore;

    public TargetSelectionStore(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public TargetSelection GetSelection() => _settingsStore.GetTargetSelection();

    public void SetSelection(TargetSelection selection) =>
        _settingsStore.SetTargetSelection(selection);

    public bool EnsureInitialized() =>
        _settingsStore.EnsureTargetSelectionInitialized();
}
