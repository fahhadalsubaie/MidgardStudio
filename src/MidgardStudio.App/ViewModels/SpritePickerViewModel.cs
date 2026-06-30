using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;

namespace MidgardStudio.App.ViewModels;

/// <summary>Backing for the headgear sprite picker: browse the accessory sprite base names in one chosen
/// GRF / loose source (the same source list the icon picker uses). Names-only — a <c>.spr</c> needs its
/// <c>.act</c> to render, so there's no thumbnail; the chosen base name is exactly what the Forge writes as
/// the sprite name. Results are capped and the enumeration runs off the UI thread, mirroring the icon picker.</summary>
public sealed partial class SpritePickerViewModel : ObservableObject
{
    private readonly GrfImageService _images;
    private List<string> _all = new();
    private const int MaxRows = 400;

    public SpritePickerViewModel(GrfImageService images)
    {
        _images = images;
        Sources = images.Sources.Select(p => new IconSourceOption(p, SourceName(p))).ToList();
        SelectedSource = Sources.FirstOrDefault(); // triggers the load
    }

    private static string SourceName(string path) =>
        Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;

    public ObservableCollection<string> Sprites { get; } = new();
    public IReadOnlyList<IconSourceOption> Sources { get; }
    public bool HasSources => Sources.Count > 0;

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private IconSourceOption? _selectedSource;
    [ObservableProperty] private string? _selectedSprite;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private string _countText = string.Empty;

    /// <summary>The chosen sprite base name, or null if the dialog was cancelled.</summary>
    public string? Result { get; private set; }

    public event Action<bool>? CloseRequested;

    partial void OnSearchChanged(string value) => Filter();
    partial void OnSelectedSourceChanged(IconSourceOption? value) => _ = LoadAsync();

    private int _loadGen; // bumped per source switch; a stale (superseded) load discards its results

    private async Task LoadAsync()
    {
        if (SelectedSource is not { } opt) return;
        int gen = ++_loadGen;
        string src = opt.Path;
        Loading = true;
        Sprites.Clear();
        _all = new List<string>();
        CountText = string.Empty;
        try
        {
            var names = await Task.Run(() => { _images.OpenIconSource(src); return _images.HeadgearSpriteNames(); });
            if (gen != _loadGen) return; // a newer source switch superseded this load
            _all = names.ToList();
            Filter();
        }
        finally { if (gen == _loadGen) Loading = false; }
    }

    private void Filter()
    {
        var q = Search.Trim();
        var hits = q.Length == 0
            ? (IEnumerable<string>)_all
            : _all.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase));
        var shown = hits.Take(MaxRows).ToList();
        Sprites.Clear();
        foreach (var n in shown) Sprites.Add(n);
        int total = q.Length == 0 ? _all.Count : hits.Count();
        CountText = total > shown.Count ? $"Showing {shown.Count} of {total} — refine your search" : $"{shown.Count} sprite(s)";
    }

    [RelayCommand]
    private void Pick()
    {
        if (SelectedSprite is { } s) { Result = s; CloseRequested?.Invoke(true); }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
