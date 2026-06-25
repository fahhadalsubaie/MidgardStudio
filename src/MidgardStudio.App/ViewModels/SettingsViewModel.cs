using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MidgardStudio.App.Services;

namespace MidgardStudio.App.ViewModels;

/// <summary>One editable keyboard shortcut row in the Settings ▸ Shortcuts tab.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly Action<string, string> _set;

    public ShortcutRowViewModel(string key, string display, string gesture, Action<string, string> set)
    {
        Key = key;
        Display = display;
        _gesture = gesture;
        _set = set;
    }

    public string Key { get; }
    public string Display { get; }

    [ObservableProperty] private string _gesture;

    partial void OnGestureChanged(string value) => _set(Key, value);
}

/// <summary>
/// Settings panel (File ▸ Settings). Currently hosts the saving-behaviour preference; changes persist
/// immediately and notify the shell to re-arm its auto-save timers.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settings;
    private readonly Action _onChanged;

    public SettingsViewModel(AppSettingsService settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;
        _mode = settings.Settings.SaveMode;
        _intervalSeconds = settings.Settings.SaveIntervalSeconds;
        _backupRetention = settings.Settings.BackupRetention;

        foreach (var (key, display, def) in AppSettingsService.ShortcutDefs)
        {
            string gesture = settings.Settings.Shortcuts.TryGetValue(key, out var g) && !string.IsNullOrWhiteSpace(g) ? g : def;
            Shortcuts.Add(new ShortcutRowViewModel(key, display, gesture, SetShortcut));
        }
    }

    /// <summary>Editable keyboard shortcuts (Settings ▸ Shortcuts).</summary>
    public ObservableCollection<ShortcutRowViewModel> Shortcuts { get; } = new();

    private void SetShortcut(string key, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) _settings.Settings.Shortcuts.Remove(key);
        else _settings.Settings.Shortcuts[key] = gesture.Trim();
        _settings.Save();
        _onChanged();
    }

    [ObservableProperty] private SaveMode _mode;
    [ObservableProperty] private int _intervalSeconds;
    [ObservableProperty] private int _backupRetention;

    partial void OnBackupRetentionChanged(int value)
    {
        _settings.Settings.BackupRetention = Math.Max(1, value);
        _settings.Save();
    }

    public bool IsManual { get => Mode == SaveMode.Manual; set { if (value) Mode = SaveMode.Manual; } }
    public bool IsInterval { get => Mode == SaveMode.Interval; set { if (value) Mode = SaveMode.Interval; } }
    public bool IsOnEdit { get => Mode == SaveMode.OnEdit; set { if (value) Mode = SaveMode.OnEdit; } }

    partial void OnModeChanged(SaveMode value)
    {
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsInterval));
        OnPropertyChanged(nameof(IsOnEdit));
        Persist();
    }

    partial void OnIntervalSecondsChanged(int value) => Persist();

    private void Persist()
    {
        _settings.Settings.SaveMode = Mode;
        _settings.Settings.SaveIntervalSeconds = Math.Max(5, IntervalSeconds);
        _settings.Save();
        _onChanged();
    }
}
