using System;
using System.IO;
using System.Text.Json;

namespace MidgardStudio.App.Services;

/// <summary>How edits reach disk.</summary>
public enum SaveMode
{
    /// <summary>Files are written only when the user clicks Save (the default — nothing is automatic).</summary>
    Manual,
    /// <summary>Pending changes are written on a fixed timer.</summary>
    Interval,
    /// <summary>Pending changes are written shortly after each edit.</summary>
    OnEdit,
}

/// <summary>App-wide preferences (not tied to a server profile).</summary>
public sealed class AppSettings
{
    public SaveMode SaveMode { get; set; } = SaveMode.Manual;
    public int SaveIntervalSeconds { get; set; } = 60;

    /// <summary>Play a bike-chain ratchet click while scrolling the lists.</summary>
    public bool ScrollSound { get; set; } = true;

    /// <summary>User-overridden keyboard shortcuts, keyed by action (e.g. "Save" → "Ctrl+S").</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    /// <summary>How many dated backup snapshots to keep per profile before the oldest are pruned.</summary>
    public int BackupRetention { get; set; } = 30;
}

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON in %APPDATA%\Midgard Studio.</summary>
public sealed class AppSettingsService
{
    /// <summary>The configurable keyboard shortcuts: action key, friendly label, default gesture.</summary>
    public static readonly (string Key, string Display, string Default)[] ShortcutDefs =
    {
        ("Save", "Save", "Ctrl+S"),
        ("Undo", "Undo", "Ctrl+Z"),
        ("Redo", "Redo", "Ctrl+Y"),
        ("QuickOpen", "Quick open", "Ctrl+K"),
        ("Configuration", "Profiles & configuration", "Ctrl+OemComma"),
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public AppSettingsService()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Midgard Studio", "app-settings.json");
        Settings = Load();
    }

    public AppSettings Settings { get; }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch { /* corrupt settings -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Atomic write so a crash mid-save can't truncate the settings file.
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, Json));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        catch { /* best effort */ }
    }
}
