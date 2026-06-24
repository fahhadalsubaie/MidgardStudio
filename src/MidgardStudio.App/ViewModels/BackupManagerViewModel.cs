using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Tools ▸ Backup Manager. Lists the dated snapshots for the active profile and lets the user
/// create a manual backup, restore a snapshot (replacing the current files), reveal it on disk,
/// or delete it. A restore reloads the workspace via the supplied callback.
/// </summary>
public sealed partial class BackupManagerViewModel : ObservableObject
{
    private readonly BackupService _backups;
    private readonly Action _reloadAfterRestore;

    public BackupManagerViewModel(BackupService backups, Action reloadAfterRestore)
    {
        _backups = backups;
        _reloadAfterRestore = reloadAfterRestore;
        Refresh();
    }

    public ObservableCollection<BackupEntry> Items { get; } = new();

    [ObservableProperty]
    private BackupEntry? _selected;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => Items.Count == 0;

    partial void OnSelectedChanged(BackupEntry? value) => OnPropertyChanged(nameof(HasSelection));

    [RelayCommand]
    private void Refresh()
    {
        var keep = Selected?.FolderPath;
        Items.Clear();
        foreach (var entry in _backups.List()) Items.Add(entry);
        Selected = Items.FirstOrDefault(e => e.FolderPath == keep) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void CreateNow()
    {
        var entry = _backups.CreateBackup("Manual backup", "Created from the Backup Manager.");
        Refresh();
        if (entry is not null)
        {
            Selected = Items.FirstOrDefault(e => e.FolderPath == entry.FolderPath) ?? Selected;
            StatusMessage = $"Backup created — {entry.SummaryText}.";
        }
        else
        {
            StatusMessage = "Nothing to back up (no editable files found).";
        }
    }

    [RelayCommand]
    private void Restore(BackupEntry? entry)
    {
        entry ??= Selected;
        if (entry is null) return;

        if (!Views.ConfirmDialog.Show("Restore backup",
                $"Restore the backup from {entry.WhenText}?\n\n\"{entry.Label}\"\n\n" +
                "This replaces your current import data and client itemInfo with this snapshot. " +
                "A safety backup of the current state is taken first.",
                yes: "Restore"))
            return;

        try
        {
            _backups.Restore(entry);
            _reloadAfterRestore();
            Refresh();
            StatusMessage = $"Restored backup from {entry.WhenText}. A safety backup of the previous state was saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Restore failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Delete(BackupEntry? entry)
    {
        entry ??= Selected;
        if (entry is null) return;

        if (!Views.ConfirmDialog.Show("Delete backup",
                $"Delete the backup from {entry.WhenText} permanently?", yes: "Delete")) return;

        _backups.Delete(entry);
        Refresh();
        StatusMessage = "Backup deleted.";
    }

    [RelayCommand]
    private void OpenFolder(BackupEntry? entry)
    {
        string path = entry?.FolderPath ?? _backups.RootDir;
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { /* shell-open failure is non-fatal */ }
    }
}
