using System.Text.Json;
using Astral.Core.Targets;

namespace Astral.Core.Configuration;

public sealed class AppSettingsStore
{
    private const int CurrentBrowserAccessPreferenceVersion = 1;
    private const int CurrentTargetSelectionPreferenceVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public AppSettingsStore(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool IsSetupConsentAccepted(string wireSockVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockVersion);

        lock (_gate)
        {
            var settings = Load();
            return string.Equals(
                settings.AcceptedWireSockVersion,
                wireSockVersion,
                StringComparison.Ordinal)
                && settings.AcceptedCloudflareWarpTerms;
        }
    }

    public bool IsBrowserAccessEnabled()
    {
        lock (_gate)
        {
            return Load().BrowserAccessEnabled ?? false;
        }
    }

    public bool EnsureBrowserAccessPreferenceInitialized()
    {
        lock (_gate)
        {
            var settings = Load();
            if (settings.BrowserAccessPreferenceVersion >= CurrentBrowserAccessPreferenceVersion)
            {
                return false;
            }

            _paths.EnsureDirectories();
            var migratedFromEnabled = settings.BrowserAccessEnabled == true;
            Save(settings with
            {
                BrowserAccessEnabled = false,
                BrowserAccessPreferenceVersion = CurrentBrowserAccessPreferenceVersion
            });

            return migratedFromEnabled;
        }
    }

    public bool EnsureTargetSelectionInitialized()
    {
        lock (_gate)
        {
            var settings = Load();
            if (settings.TargetSelectionPreferenceVersion >= CurrentTargetSelectionPreferenceVersion
                && settings.TargetSelection is not null)
            {
                return false;
            }

            _paths.EnsureDirectories();
            Save(settings with
            {
                BrowserAccessEnabled = false,
                BrowserAccessPreferenceVersion = CurrentBrowserAccessPreferenceVersion,
                TargetSelectionPreferenceVersion = CurrentTargetSelectionPreferenceVersion,
                TargetSelection = StoredTargetSelection.From(TargetSelection.Default)
            });

            return true;
        }
    }

    public TargetSelection GetTargetSelection()
    {
        lock (_gate)
        {
            var settings = Load();
            return settings.TargetSelection?.ToTargetSelection()
                ?? TargetSelection.Default;
        }
    }

    public bool IsRunInBackgroundOnCloseEnabled()
    {
        lock (_gate)
        {
            return Load().RunInBackgroundOnClose ?? false;
        }
    }

    public bool IsStartWithWindowsEnabled()
    {
        lock (_gate)
        {
            return Load().StartWithWindows ?? false;
        }
    }

    public bool IsDebugDiagnosticsEnabled()
    {
        lock (_gate)
        {
            return Load().DebugDiagnosticsEnabled ?? false;
        }
    }

    public bool IsWireSockInstalledByAstral()
    {
        lock (_gate)
        {
            return Load().WireSockInstalledByAstral ?? false;
        }
    }

    public void SetBrowserAccessEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                BrowserAccessEnabled = enabled,
                BrowserAccessPreferenceVersion = CurrentBrowserAccessPreferenceVersion
            };

            Save(settings);
        }
    }

    public void SetTargetSelection(TargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                BrowserAccessEnabled = false,
                BrowserAccessPreferenceVersion = CurrentBrowserAccessPreferenceVersion,
                TargetSelectionPreferenceVersion = CurrentTargetSelectionPreferenceVersion,
                TargetSelection = StoredTargetSelection.From(selection)
            };

            Save(settings);
        }
    }

    public void SetRunInBackgroundOnCloseEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                RunInBackgroundOnClose = enabled
            };

            Save(settings);
        }
    }

    public void SetStartWithWindowsEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                StartWithWindows = enabled
            };

            Save(settings);
        }
    }

    public void SetDebugDiagnosticsEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                DebugDiagnosticsEnabled = enabled
            };

            Save(settings);
        }
    }

    public void SetWireSockInstalledByAstral(bool installed)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                WireSockInstalledByAstral = installed
            };

            Save(settings);
        }
    }

    public void AcceptSetupConsent(string wireSockVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockVersion);

        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                AcceptedWireSockVersion = wireSockVersion,
                AcceptedCloudflareWarpTerms = true
            };

            Save(settings);
        }
    }

    private StoredSettings Load()
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return StoredSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_paths.SettingsFile);
            return JsonSerializer.Deserialize<StoredSettings>(json)
                ?? StoredSettings.Default;
        }
        catch (JsonException)
        {
            return StoredSettings.Default;
        }
    }

    private void Save(StoredSettings settings)
    {
        var temporaryPath = _paths.SettingsFile + ".tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _paths.SettingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record StoredSettings(
        string? AcceptedWireSockVersion,
        bool AcceptedCloudflareWarpTerms,
        bool? BrowserAccessEnabled,
        int? BrowserAccessPreferenceVersion,
        int? TargetSelectionPreferenceVersion,
        StoredTargetSelection? TargetSelection,
        bool? RunInBackgroundOnClose,
        bool? StartWithWindows,
        bool? DebugDiagnosticsEnabled,
        bool? WireSockInstalledByAstral)
    {
        public static StoredSettings Default { get; } = new(
            null,
            AcceptedCloudflareWarpTerms: false,
            BrowserAccessEnabled: false,
            BrowserAccessPreferenceVersion: CurrentBrowserAccessPreferenceVersion,
            TargetSelectionPreferenceVersion: CurrentTargetSelectionPreferenceVersion,
            TargetSelection: StoredTargetSelection.From(
                Astral.Core.Targets.TargetSelection.Default),
            RunInBackgroundOnClose: false,
            StartWithWindows: false,
            DebugDiagnosticsEnabled: false,
            WireSockInstalledByAstral: false);
    }

    private sealed record StoredTargetSelection(
        IReadOnlyList<string>? SelectedTargetIds,
        IReadOnlyList<string>? CustomExecutables,
        IReadOnlyList<string>? CustomDomains)
    {
        public static StoredTargetSelection From(TargetSelection selection)
        {
            return new StoredTargetSelection(
                selection.SelectedTargetIds.ToArray(),
                selection.CustomExecutables.Select(target => target.Path).ToArray(),
                selection.CustomDomains.Select(target => target.Pattern).ToArray());
        }

        public TargetSelection ToTargetSelection()
        {
            var selectedTargetIds = (SelectedTargetIds is { Count: > 0 }
                    ? SelectedTargetIds
                    : Astral.Core.Targets.TargetSelection.Default.SelectedTargetIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var customExecutables = new List<CustomExecutableTarget>();
            foreach (var path in CustomExecutables ?? [])
            {
                try
                {
                    customExecutables.Add(CustomExecutableTarget.Create(path));
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        or IOException
                        or NotSupportedException
                        or UnauthorizedAccessException)
                {
                }
            }

            var customDomains = new List<CustomDomainTarget>();
            foreach (var pattern in CustomDomains ?? [])
            {
                try
                {
                    customDomains.Add(CustomDomainTarget.Create(pattern));
                }
                catch (ArgumentException)
                {
                }
            }

            return new TargetSelection(
                selectedTargetIds,
                customExecutables,
                customDomains);
        }
    }
}
