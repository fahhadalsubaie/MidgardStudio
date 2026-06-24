using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.MapCache;

namespace MidgardStudio.App.ViewModels;

/// <summary>One row in the map-cache list (effective base + import view).</summary>
public sealed class MapCacheRowViewModel
{
    public MapCacheRowViewModel(MapCacheEntry entry, string origin, bool isImport)
    {
        Name = entry.Name;
        Xs = entry.Xs;
        Ys = entry.Ys;
        Origin = origin;
        IsImport = isImport;
    }

    public string Name { get; }
    public int Xs { get; }
    public int Ys { get; }
    public string Origin { get; }
    public bool IsImport { get; }
    public string SizeText => $"{Xs} × {Ys}";
    public string CellsText => (Xs * Ys).ToString("N0");
}

/// <summary>
/// Tools ▸ Map Cache Editor. Manages rAthena's map_cache.dat the same way the rest of the app handles
/// the DBs: the re + pre-re caches are read-only base, edits land in db/import/map_cache.dat. Add maps
/// from .gat files, merge another .dat, remove import entries, then Save to import.
/// </summary>
public sealed partial class MapCacheEditorViewModel : ObservableObject
{
    private readonly MapCacheService _service;
    private List<MapCacheEntry> _base = new();
    private readonly List<MapCacheEntry> _import = new();

    public MapCacheEditorViewModel(MapCacheService service)
    {
        _service = service;
        Reload();
    }

    public ObservableCollection<MapCacheRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private MapCacheRowViewModel? _selected;

    public string ImportPath => _service.ImportPath;
    public int TotalCount { get; private set; }
    public int ImportCount => _import.Count;

    private string? _sortColumn; // null = Map ascending (default); "Map"; "Origin"
    private bool _sortAscending = true;

    /// <summary>Header text for a sortable column, including the active sort glyph.</summary>
    public string HeaderText(string key)
    {
        string label = key == "Map" ? "Map" : "Origin";
        if (_sortColumn != key) return label;
        return label + (_sortAscending ? "  ▲" : "  ▼");
    }

    /// <summary>Cycles a column's sort: ascending → descending → default (map name ascending).</summary>
    public void ToggleSort(string key)
    {
        if (_sortColumn != key) { _sortColumn = key; _sortAscending = true; }
        else if (_sortAscending) _sortAscending = false;
        else { _sortColumn = null; _sortAscending = true; }
        Rebuild();
    }

    partial void OnSearchChanged(string value) => Rebuild();

    [RelayCommand]
    private void Reload()
    {
        try
        {
            _base = _service.LoadBase().Maps.ToList();
            _import.Clear();
            _import.AddRange(_service.LoadImport().Maps);
            IsDirty = false;
            Rebuild();
            StatusMessage = $"Loaded {TotalCount} map(s) — {_import.Count} from import.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Load failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void AddGat()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add maps from .gat files",
            Filter = "Map geometry (*.gat)|*.gat|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        int added = 0;
        var errors = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var entry = MapCacheFile.FromGat(Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));
                UpsertImport(entry);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)} — {ex.Message}");
            }
        }

        if (added > 0) IsDirty = true;
        Rebuild();
        StatusMessage = $"Added/updated {added} map(s)." + (errors.Count > 0 ? $"  {errors.Count} failed: {string.Join("; ", errors.Take(3))}" : "");
    }

    [RelayCommand]
    private void MergeDat()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Merge another map_cache.dat",
            Filter = "map_cache (*.dat)|*.dat|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var other = MapCacheFile.Read(File.ReadAllBytes(dialog.FileName));
            foreach (var map in other.Maps) UpsertImport(map);
            if (other.Maps.Count > 0) IsDirty = true;
            Rebuild();
            StatusMessage = $"Merged {other.Maps.Count} map(s) from {Path.GetFileName(dialog.FileName)} into the import overlay.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Merge failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Remove(MapCacheRowViewModel? row)
    {
        row ??= Selected;
        if (row is null || !row.IsImport) return;
        _import.RemoveAll(m => m.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
        IsDirty = true;
        Rebuild();
        StatusMessage = $"Removed \"{row.Name}\" from the import overlay.";
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _service.SaveImport(new MapCacheFile { Maps = _import.ToList() });
            IsDirty = false;
            StatusMessage = $"Saved {_import.Count} map(s) to import/map_cache.dat.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Save failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            string dir = Path.GetDirectoryName(ImportPath)!;
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* shell-open is non-fatal */ }
    }

    private void UpsertImport(MapCacheEntry entry)
    {
        _import.RemoveAll(m => m.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        _import.Add(entry);
    }

    private void Rebuild()
    {
        var importByName = _import.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var baseByName = _base.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

        var allRows = new List<MapCacheRowViewModel>();
        foreach (var name in baseByName.Keys.Union(importByName.Keys, StringComparer.OrdinalIgnoreCase))
        {
            bool inImport = importByName.TryGetValue(name, out var imp);
            bool inBase = baseByName.TryGetValue(name, out var bse);
            var entry = inImport ? imp! : bse!;
            string origin = inImport ? (inBase ? "Override" : "Custom") : "Base";
            allRows.Add(new MapCacheRowViewModel(entry, origin, inImport));
        }

        IEnumerable<MapCacheRowViewModel> sorted = (_sortColumn, _sortAscending) switch
        {
            ("Map", false) => allRows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Origin", true) => allRows.OrderBy(r => r.Origin, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ("Origin", false) => allRows.OrderByDescending(r => r.Origin, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => allRows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
        };

        string query = Search?.Trim() ?? string.Empty;
        Rows.Clear();
        foreach (var row in sorted)
            if (query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                Rows.Add(row);

        TotalCount = allRows.Count;
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ImportCount));
    }
}
