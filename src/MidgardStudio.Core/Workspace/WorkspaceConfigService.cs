using System.Text.Json;

namespace MidgardStudio.Core.Workspace;

/// <summary>On-disk shape of profiles.json: every saved profile plus which one is active.</summary>
public sealed class WorkspaceProfileStore
{
    public List<WorkspaceConfig> Profiles { get; set; } = new();

    /// <summary>Name of the active profile (case-insensitive match against <see cref="Profiles"/>).</summary>
    public string? ActiveProfile { get; set; }
}

public interface IWorkspaceConfigService
{
    string ConfigDirectory { get; }
    string ConfigPath { get; }
    string ProfilesPath { get; }

    /// <summary>Loads the active profile's config, or sensible defaults if none exists.</summary>
    WorkspaceConfig Load();

    /// <summary>Saves <paramref name="config"/> as a profile and marks it active (back-compat helper).</summary>
    void Save(WorkspaceConfig config);

    /// <summary>All saved profiles, most-recently-opened first.</summary>
    IReadOnlyList<WorkspaceConfig> GetProfiles();

    /// <summary>The active profile, or null when none is saved yet.</summary>
    WorkspaceConfig? ActiveProfile { get; }

    /// <summary>True when at least one profile is saved.</summary>
    bool HasProfiles { get; }

    /// <summary>Inserts or replaces a profile (matched by name) without changing the active selection.</summary>
    void UpsertProfile(WorkspaceConfig config);

    void SetActiveProfile(string name);

    void DeleteProfile(string name);
}

/// <summary>
/// JSON-backed workspace configuration stored in %APPDATA%\Midgard Studio. Multiple named profiles
/// live in profiles.json (one active); on first run with only the legacy workspace.json, that single
/// config is migrated into a "Default" profile. When nothing is saved, defaults point at the
/// custom-items repository layout.
/// </summary>
public sealed class WorkspaceConfigService : IWorkspaceConfigService
{
    /// <summary>Default repository root used when no config exists yet.</summary>
    public const string DefaultRepoRoot = @"C:\Users\fahha\Documents\GitHub\custom-items";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Midgard Studio");

    public string ConfigPath => Path.Combine(ConfigDirectory, "workspace.json");

    public string ProfilesPath => Path.Combine(ConfigDirectory, "profiles.json");

    public bool HasProfiles => LoadStore().Profiles.Count > 0;

    public WorkspaceConfig? ActiveProfile
    {
        get
        {
            var store = LoadStore();
            if (store.Profiles.Count == 0) return null;
            return store.Profiles.FirstOrDefault(
                       p => string.Equals(p.Name, store.ActiveProfile, StringComparison.OrdinalIgnoreCase))
                   ?? store.Profiles[0];
        }
    }

    public IReadOnlyList<WorkspaceConfig> GetProfiles() =>
        LoadStore().Profiles
            .OrderByDescending(p => p.LastOpenedUtc)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public WorkspaceConfig Load() => ActiveProfile ?? CreateDefault();

    public void Save(WorkspaceConfig config)
    {
        UpsertProfile(config);
        SetActiveProfile(config.Name);
    }

    public void UpsertProfile(WorkspaceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name)) config.Name = "Default";
        var store = LoadStore();
        store.Profiles.RemoveAll(p => string.Equals(p.Name, config.Name, StringComparison.OrdinalIgnoreCase));
        store.Profiles.Add(config);
        store.ActiveProfile ??= config.Name;
        SaveStore(store);
    }

    public void SetActiveProfile(string name)
    {
        var store = LoadStore();
        if (store.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            store.ActiveProfile = name;
            SaveStore(store);
        }
    }

    public void DeleteProfile(string name)
    {
        var store = LoadStore();
        store.Profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(store.ActiveProfile, name, StringComparison.OrdinalIgnoreCase))
            store.ActiveProfile = store.Profiles.FirstOrDefault()?.Name;
        SaveStore(store);
    }

    private WorkspaceProfileStore LoadStore()
    {
        try
        {
            if (File.Exists(ProfilesPath))
            {
                var store = JsonSerializer.Deserialize<WorkspaceProfileStore>(File.ReadAllText(ProfilesPath), JsonOptions);
                if (store is not null) return store;
            }

            // Migrate a legacy single-config workspace.json into a "Default" profile.
            if (File.Exists(ConfigPath))
            {
                var legacy = JsonSerializer.Deserialize<WorkspaceConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                if (legacy?.Paths is not null && !string.IsNullOrWhiteSpace(legacy.Paths.ServerDbRoot))
                {
                    if (string.IsNullOrWhiteSpace(legacy.Name)) legacy.Name = "Default";
                    var migrated = new WorkspaceProfileStore { Profiles = { legacy }, ActiveProfile = legacy.Name };
                    SaveStore(migrated);
                    return migrated;
                }
            }
        }
        catch
        {
            // Corrupt or incompatible config -> start empty (first-run wizard).
        }

        return new WorkspaceProfileStore();
    }

    private void SaveStore(WorkspaceProfileStore store)
    {
        Directory.CreateDirectory(ConfigDirectory);
        // Atomic write: a crash mid-write must never truncate the file that holds the user's server paths.
        var json = JsonSerializer.Serialize(store, JsonOptions);
        var tmp = ProfilesPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(ProfilesPath))
        {
            try { File.Copy(ProfilesPath, ProfilesPath + ".bak", overwrite: true); } catch { /* best effort */ }
            File.Replace(tmp, ProfilesPath, null);
        }
        else
        {
            File.Move(tmp, ProfilesPath);
        }
    }

    public static WorkspaceConfig CreateDefault() => new()
    {
        Name = "Default",
        Paths = WorkspacePaths.CreateDefault(DefaultRepoRoot),
        DefaultMode = ServerMode.Renewal,
    };
}
