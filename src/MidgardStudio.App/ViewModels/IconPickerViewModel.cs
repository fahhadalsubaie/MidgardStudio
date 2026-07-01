using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Model;

namespace MidgardStudio.App.ViewModels;

/// <summary>A selectable GRF/loose source — the dropdown shows just the file/folder name, the full path drives
/// the load.</summary>
public sealed record IconSourceOption(string Path, string Name);

/// <summary>An existing-item row in the icon picker: id, name and its icon resource, with a lazy thumbnail
/// (resolved only when the row is realized by list virtualization, and cached by <see cref="GrfImageService"/>).</summary>
public sealed class IconItemRow
{
    private readonly GrfImageService _images;
    public IconItemRow(GrfImageService images, int id, string name, string resource)
    {
        _images = images; Id = id; Name = name; Resource = resource;
    }
    public int Id { get; }
    public string Name { get; }
    public string Resource { get; }
    public string Display => $"#{Id}  {Name}";
    public ImageSource? Icon => _images.ItemIcon(Resource);
}

/// <summary>A GRF icon row: the resource base name + a lazy thumbnail decoded from the chosen source.</summary>
public sealed class IconGrfRow
{
    private readonly GrfImageService _images;
    public IconGrfRow(GrfImageService images, string name) { _images = images; Name = name; }
    public string Name { get; }
    public ImageSource? Icon => _images.IconThumbnail(Name);
}

/// <summary>The "copy an existing item's icon" rows: every item that actually has a client icon resource,
/// with a lazy thumbnail. Shared by the Forge and the Client Items editor so both offer the same picker.</summary>
public static class IconPickerRows
{
    public static List<IconItemRow> BuildItemRows(IEnumerable<DbRecord> items, GrfImageService images, ClientItemService clientItems)
    {
        var rows = new List<IconItemRow>();
        foreach (var r in items)
        {
            int id = r.GetInt("Id");
            var res = clientItems.ResourceOf(id);
            if (string.IsNullOrWhiteSpace(res)) continue; // only items that actually have an icon to copy
            var name = r.GetString("Name");
            rows.Add(new IconItemRow(images, id,
                string.IsNullOrWhiteSpace(name) ? (r.GetString("AegisName") ?? string.Empty) : name!, res!));
        }
        return rows;
    }
}

/// <summary>
/// Backing for the icon picker dialog: choose an icon resource by copying an existing item's icon (searchable
/// by id/name) OR by browsing the inventory-icon BMPs of one chosen GRF / loose source. Results are capped and
/// thumbnails load lazily so neither tab enumerates or decodes more than it must.
/// </summary>
public sealed partial class IconPickerViewModel : ObservableObject
{
    private readonly GrfImageService _images;
    private readonly List<IconItemRow> _allItems;
    private List<IconGrfRow> _allGrfIcons = new();
    private const int MaxRows = 400; // keep filtering snappy on the ~6k-item / multi-thousand-icon lists

    public IconPickerViewModel(GrfImageService images, IEnumerable<IconItemRow> items)
    {
        _images = images;
        _allItems = items.OrderBy(r => r.Id).ToList(); // always ascending by id (filtering preserves the order)
        Items = new ObservableCollection<IconItemRow>();
        FilterItems();

        Sources = images.Sources.Select(p => new IconSourceOption(p, SourceName(p))).ToList();
        SelectedSource = Sources.FirstOrDefault(); // triggers the GRF load
    }

    private static string SourceName(string path) =>
        Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;

    public ObservableCollection<IconItemRow> Items { get; }
    public ObservableCollection<IconGrfRow> GrfIcons { get; } = new();
    public IReadOnlyList<IconSourceOption> Sources { get; }
    public bool HasSources => Sources.Count > 0;

    [ObservableProperty] private string _itemSearch = string.Empty;
    [ObservableProperty] private string _grfSearch = string.Empty;
    [ObservableProperty] private IconSourceOption? _selectedSource;
    [ObservableProperty] private IconItemRow? _selectedItem;
    [ObservableProperty] private IconGrfRow? _selectedGrfIcon;
    [ObservableProperty] private bool _loadingGrf;
    [ObservableProperty] private string _itemCountText = string.Empty;
    [ObservableProperty] private string _grfCountText = string.Empty;

    /// <summary>The chosen resource base name, or null if the dialog was cancelled.</summary>
    public string? Result { get; private set; }

    /// <summary>Asks the host window to close with this dialog result.</summary>
    public event Action<bool>? CloseRequested;

    partial void OnItemSearchChanged(string value) => FilterItems();
    partial void OnGrfSearchChanged(string value) => FilterGrfIcons();
    partial void OnSelectedSourceChanged(IconSourceOption? value) => _ = LoadGrfAsync();

    private void FilterItems()
    {
        var q = ItemSearch.Trim();
        var hits = q.Length == 0
            ? (IEnumerable<IconItemRow>)_allItems
            : _allItems.Where(r => r.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                                || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        var shown = hits.Take(MaxRows).ToList();
        Items.Clear();
        foreach (var r in shown) Items.Add(r);
        int total = q.Length == 0 ? _allItems.Count : hits.Count();
        ItemCountText = total > shown.Count ? $"Showing {shown.Count} of {total} — refine your search" : $"{shown.Count} item(s)";
    }

    private int _grfLoadGen; // bumped per source switch; a stale (superseded) load discards its results

    private async Task LoadGrfAsync()
    {
        if (SelectedSource is not { } opt) return;
        int gen = ++_grfLoadGen;
        string src = opt.Path;
        LoadingGrf = true;
        GrfIcons.Clear();
        _allGrfIcons = new List<IconGrfRow>();
        GrfCountText = string.Empty;
        try
        {
            // Enumerate the chosen source's item-icon folder off the UI thread (the GRF file-table build can
            // take a moment); thumbnails still decode lazily per visible row.
            var names = await Task.Run(() => { _images.OpenIconSource(src); return _images.IconResourceNames(); });
            if (gen != _grfLoadGen) return; // a newer source switch superseded this load — don't show its results
            _allGrfIcons = names.Select(n => new IconGrfRow(_images, n)).ToList();
            FilterGrfIcons();
        }
        finally { if (gen == _grfLoadGen) LoadingGrf = false; }
    }

    private void FilterGrfIcons()
    {
        var q = GrfSearch.Trim();
        var hits = q.Length == 0
            ? (IEnumerable<IconGrfRow>)_allGrfIcons
            : _allGrfIcons.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        var shown = hits.Take(MaxRows).ToList();
        GrfIcons.Clear();
        foreach (var r in shown) GrfIcons.Add(r);
        int total = q.Length == 0 ? _allGrfIcons.Count : hits.Count();
        GrfCountText = total > shown.Count ? $"Showing {shown.Count} of {total} — refine your search" : $"{shown.Count} icon(s)";
    }

    [RelayCommand]
    private void PickItem() { if (SelectedItem is { } r) Confirm(r.Resource); }

    [RelayCommand]
    private void PickGrf() { if (SelectedGrfIcon is { } r) Confirm(r.Name); }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private void Confirm(string resource)
    {
        Result = resource;
        CloseRequested?.Invoke(true);
    }
}
