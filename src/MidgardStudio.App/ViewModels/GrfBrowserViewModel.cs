using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MidgardStudio.App.Common;
using MidgardStudio.Core.Grf;
using MidgardStudio.Core.Workspace;
using MidgardStudio.Grf;

namespace MidgardStudio.App.ViewModels;

internal static class GrfExt
{
    public static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
        { ".bmp", ".tga", ".png", ".jpg", ".jpeg", ".pal", ".gif", ".ebm" };
    public static readonly HashSet<string> Sprite = new(StringComparer.OrdinalIgnoreCase)
        { ".spr", ".act" };
    public static readonly HashSet<string> Map = new(StringComparer.OrdinalIgnoreCase)
        { ".gnd", ".rsw", ".rsm", ".rsm2" };
    public static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3" };
    public static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase)
        { ".lua", ".lub", ".txt", ".xml", ".ini", ".inf", ".conf", ".log", ".json", ".csv", ".tsv",
          ".ezv", ".lst", ".js", ".c", ".cpp", ".h", ".bat", ".ase", ".scp", ".layout", ".font",
          ".imageset", ".integrity", ".yml", ".yaml", ".xml" };
}

/// <summary>A lazily-loaded folder node in the GRF sidebar tree (folders only; files live in the file area).</summary>
public sealed partial class GrfNode : ObservableObject
{
    private readonly Func<GrfNode, IEnumerable<GrfNode>>? _load;
    private bool _loaded;

    public GrfNode(string name, string fullPath, bool hasChildren, Func<GrfNode, IEnumerable<GrfNode>>? load)
    {
        Name = name;
        FullPath = fullPath;
        _load = load;
        if (hasChildren && load is not null)
            Children.Add(new GrfNode(string.Empty, string.Empty, false, null));
    }

    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<GrfNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    partial void OnIsExpandedChanged(bool value) { if (value) EnsureLoaded(); }

    public void EnsureLoaded()
    {
        if (_loaded || _load is null) return;
        _loaded = true;
        Children.Clear();
        foreach (var child in _load(this)) Children.Add(child);
    }
}

/// <summary>One row (folder or file) in the file table, with lazily-resolved thumbnail + size.</summary>
public sealed partial class GrfItem : ObservableObject
{
    private readonly Func<GrfItem, ImageSource?>? _thumb;
    private readonly Func<GrfItem, long?>? _size;
    private ImageSource? _thumbnail;
    private bool _thumbResolved;
    private long? _sizeValue;
    private bool _sizeResolved;

    public GrfItem(string name, string fullPath, bool isFolder, bool isUp, Func<GrfItem, ImageSource?>? thumb, Func<GrfItem, long?>? size)
    {
        Name = name;
        FullPath = fullPath;
        IsFolder = isFolder;
        IsUp = isUp;
        _thumb = thumb;
        _size = size;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFolder { get; }
    public bool IsUp { get; } // the ".." parent row
    // Extension comes from the raw FullPath, not the (re-projected) display Name: under a DBCS view encoding a
    // trailing high byte could swallow the '.', so only the byte-stable lookup key gives a reliable extension.
    public string Ext => IsFolder ? string.Empty : Path.GetExtension(FullPath).ToLowerInvariant();
    public bool IsImage => !IsFolder && !IsUp && (GrfExt.Image.Contains(Ext) || GrfExt.Sprite.Contains(Ext) || Ext == ".gat");
    public string TypeText => IsUp ? string.Empty : IsFolder ? "Folder" : (Ext.Length > 1 ? Ext[1..].ToUpperInvariant() : "File");

    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbResolved && IsImage) { _thumbResolved = true; _thumbnail = _thumb?.Invoke(this); }
            return _thumbnail;
        }
    }

    public long SizeValue
    {
        get
        {
            if (!_sizeResolved && !IsFolder && !IsUp) { _sizeResolved = true; _sizeValue = _size?.Invoke(this); }
            return _sizeValue ?? 0;
        }
    }

    public string SizeText => IsFolder || IsUp ? string.Empty : GrfBrowserViewModel.HumanSize(SizeValue);
}

/// <summary>A key/value row in the metadata preview.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>A content-search hit: the display name plus the raw (1252) lookup path to navigate to.</summary>
public sealed record SearchHit(string Display, string Path);

/// <summary>A lazily-decoded texture thumbnail in the model/ground preview.</summary>
public sealed partial class GrfThumb : ObservableObject
{
    private readonly Func<string, ImageSource?> _resolve;
    private ImageSource? _image;
    private bool _resolved;

    public GrfThumb(string name, string path, Func<string, ImageSource?> resolve)
    {
        Name = name;
        Path = path;
        _resolve = resolve;
    }

    public string Name { get; }
    public string Path { get; }

    public ImageSource? Image
    {
        get { if (!_resolved) { _resolved = true; _image = _resolve(Path); } return _image; }
    }
}

/// <summary>
/// Read-only GRF Explorer: themed sidebar tree, a sortable file table (name/type/size), and a rich
/// preview (image with zoom + transparency grid, animated sprites, map/model metadata with texture
/// thumbnails, GAT minimaps, text/lua, hex), plus export. Never opens a GRF for writing.
/// </summary>
public sealed partial class GrfBrowserViewModel : ObservableObject
{
    private readonly GrfService _grf;
    private readonly IWorkspaceConfigService _config;
    private readonly Dictionary<string, (SortedSet<string> Sub, SortedSet<string> Files)> _dirs =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _previewPath;
    private int _viewCodePage = ViewEncoding.DefaultCodePage;
    private CancellationTokenSource? _searchCts;

    private static readonly EncodingChoice CustomEncoding = new(-1, "Custom codepage…");

    public GrfBrowserViewModel(GrfService grf, IWorkspaceConfigService config)
    {
        _grf = grf;
        _config = config;
        RefreshFromConfig();
        SelectedEncoding = EncodingChoices[0]; // 1252 default; never written, resets each launch
    }

    public ObservableCollection<string> Sources { get; } = new();
    public ObservableCollection<GrfNode> RootNodes { get; } = new();
    public RangeObservableCollection<GrfItem> Items { get; } = new();
    public ObservableCollection<InfoRow> InfoRows { get; } = new();
    public ObservableCollection<string> InfoItems { get; } = new();
    public ObservableCollection<GrfThumb> Thumbs { get; } = new();

    /// <summary>The view-encoding selector contents: the fixed codepages plus a "Custom codepage…" entry.</summary>
    public ObservableCollection<EncodingChoice> EncodingChoices { get; } =
        new(ViewEncoding.Choices.Append(CustomEncoding));
    public ObservableCollection<InfoRow> DetailRows { get; } = new();
    public ObservableCollection<SearchHit> SearchResults { get; } = new();

    [ObservableProperty] private string? _selectedSource;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private string _sortColumn = "Name";
    [ObservableProperty] private bool _sortAscending = true;

    // Preview kind flags
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _showImage;
    [ObservableProperty] private bool _showAnimation;
    [ObservableProperty] private bool _showInfo;
    [ObservableProperty] private bool _showText;

    // Preview payloads
    [ObservableProperty] private ImageSource? _previewImage;
    [ObservableProperty] private SpriteAnimation? _animation;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string _previewTitle = string.Empty;
    [ObservableProperty] private string _previewSubtitle = string.Empty;
    [ObservableProperty] private string _infoKind = string.Empty;
    [ObservableProperty] private string _previewExt = string.Empty; // drives syntax highlighting in the text view

    // Tools
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private bool _wrapText;
    [ObservableProperty] private Brush? _previewBackground; // null => the checkerboard underlay shows through

    // View encoding (re-decodes content + display names; never written, resets each launch)
    [ObservableProperty] private EncodingChoice? _selectedEncoding;
    [ObservableProperty] private bool _customEncodingVisible;
    [ObservableProperty] private string _customEncodingText = string.Empty;

    // File filter
    [ObservableProperty] private string _filterText = string.Empty;

    // Details / hashes
    [ObservableProperty] private bool _showDetails;
    [ObservableProperty] private string? _crc32;
    [ObservableProperty] private string? _md5;

    // Audio — decompressed bytes played in-memory (no temp file, no MediaElement)
    [ObservableProperty] private bool _showAudio;
    [ObservableProperty] private byte[]? _audioData;

    // Content search
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _showSearch;

    public bool CanExport => _previewPath is not null;
    public bool IsImageKind => ShowImage;

    /// <summary>Re-projects a raw 1252 name into the current view codepage for display (lookup keys stay raw).</summary>
    private string Display(string raw) => ViewEncoding.Reproject(raw, _viewCodePage);

    public string SourceLabel(string s) => Path.GetFileName(s);

    public void RefreshFromConfig()
    {
        // GRF entry names are ALWAYS decoded as Windows-1252: GrfAssetPaths builds icon/sprite paths in the
        // 1252 representation of the Korean RO data folders, so the GRF must match. The per-profile Display
        // Encoding does NOT apply here (it would mojibake every resource path and hide all icons).
        _grf.SetDisplayCodepage(1252);
        _grf.Configure(_config.Load().GrfPaths);

        Sources.Clear();
        foreach (var s in _grf.Sources) Sources.Add(s);

        if (Sources.Count == 0)
        {
            Status = "No GRF or data folder configured. Add your client GRF in the Configuration Wizard.";
            RootNodes.Clear();
            Items.Clear();
            return;
        }

        SelectedSource = Sources.Contains(SelectedSource ?? string.Empty) ? SelectedSource : Sources[0];
    }

    partial void OnSelectedSourceChanged(string? value) => _ = OpenAsync(value);

    private async Task OpenAsync(string? source)
    {
        RootNodes.Clear();
        Items.Clear();
        ClearPreview();
        if (string.IsNullOrEmpty(source)) return;

        IsBusy = true;
        Status = $"Opening {Path.GetFileName(source)} …";
        try
        {
            var roots = await Task.Run(() =>
            {
                _grf.OpenBrowseSource(source);
                BuildIndex(_grf.BrowseEntries());
                return LoadTreeChildren(string.Empty).ToList();
            });

            foreach (var node in roots) RootNodes.Add(node);
            Navigate(string.Empty);
            // Auto-expand the top "data" folder (or the first root) so the tree opens ready to browse.
            var open = RootNodes.FirstOrDefault(n => string.Equals(n.Name, "data", StringComparison.OrdinalIgnoreCase))
                       ?? RootNodes.FirstOrDefault();
            if (open is not null) open.IsExpanded = true;
            Status = $"{Path.GetFileName(source)} · {_dirs.Count:N0} folders · {CountFiles():N0} files";
        }
        catch (Exception ex)
        {
            Status = "Couldn't open: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int CountFiles()
    {
        int n = 0;
        foreach (var d in _dirs.Values) n += d.Files.Count;
        return n;
    }

    private void BuildIndex(IReadOnlyList<string> entries)
    {
        _dirs.Clear();
        Ensure(string.Empty);

        foreach (var raw in entries)
        {
            var path = raw.Replace('/', '\\').Trim('\\');
            if (path.Length == 0) continue;

            int slash = path.LastIndexOf('\\');
            string dir = slash < 0 ? string.Empty : path[..slash];
            string file = slash < 0 ? path : path[(slash + 1)..];

            string cur = string.Empty;
            foreach (var part in dir.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                Ensure(cur).Sub.Add(part);
                cur = cur.Length == 0 ? part : cur + "\\" + part;
                Ensure(cur);
            }
            Ensure(dir).Files.Add(file);
        }
    }

    private (SortedSet<string> Sub, SortedSet<string> Files) Ensure(string dir)
    {
        if (!_dirs.TryGetValue(dir, out var d))
        {
            d = (new SortedSet<string>(StringComparer.OrdinalIgnoreCase), new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
            _dirs[dir] = d;
        }
        return d;
    }

    private IEnumerable<GrfNode> LoadTreeChildren(string dir)
    {
        if (!_dirs.TryGetValue(dir, out var d)) yield break;
        foreach (var sub in d.Sub)
        {
            string full = dir.Length == 0 ? sub : dir + "\\" + sub;
            bool hasChildren = _dirs.TryGetValue(full, out var cd) && cd.Sub.Count > 0;
            yield return new GrfNode(Display(sub), full, hasChildren, n => LoadTreeChildren(n.FullPath));
        }
    }

    /// <summary>Shows the contents of <paramref name="path"/> in the file table.</summary>
    public void Navigate(string path)
    {
        CurrentPath = path;
        ClearPreview();
        RebuildItems();
    }

    private void RebuildItems()
    {
        var folders = new List<GrfItem>();
        var files = new List<GrfItem>();
        if (_dirs.TryGetValue(CurrentPath, out var d))
        {
            foreach (var sub in d.Sub)
            {
                if (!GrfSearch.NameMatches(Display(sub), FilterText)) continue;
                folders.Add(new GrfItem(Display(sub), CurrentPath.Length == 0 ? sub : CurrentPath + "\\" + sub, isFolder: true, isUp: false, null, null));
            }
            foreach (var file in d.Files)
            {
                if (!GrfSearch.NameMatches(Display(file), FilterText)) continue;
                files.Add(new GrfItem(Display(file), CurrentPath.Length == 0 ? file : CurrentPath + "\\" + file, isFolder: false, isUp: false, ResolveThumb, p => _grf.BrowseSize(p.FullPath)));
            }
        }

        Comparison<GrfItem> cmp = SortColumn switch
        {
            "Type" => (a, b) => string.Compare(a.Ext, b.Ext, StringComparison.OrdinalIgnoreCase),
            "Size" => (a, b) => a.SizeValue.CompareTo(b.SizeValue),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        };
        Comparison<GrfItem> dir = SortAscending ? cmp : (a, b) => cmp(b, a);
        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort(dir);

        var result = new List<GrfItem>(folders.Count + files.Count + 1);
        if (CurrentPath.Length > 0)
        {
            string parent = CurrentPath.Contains('\\') ? CurrentPath[..CurrentPath.LastIndexOf('\\')] : string.Empty;
            result.Add(new GrfItem("..", parent, isFolder: true, isUp: true, null, null));
        }
        result.AddRange(folders);
        result.AddRange(files);
        Items.ReplaceAll(result);
    }

    /// <summary>Re-sorts the file table by a column (click again to reverse).</summary>
    public void SortBy(string column)
    {
        if (SortColumn == column) SortAscending = !SortAscending;
        else { SortColumn = column; SortAscending = true; }
        RebuildItems();
    }

    private const int ThumbDecodeWidth = 96; // decode encoded thumbnails small instead of at full resolution

    private ImageSource? ResolveThumb(GrfItem item)
    {
        if (item.Ext == ".gat") return GrfImaging.ToImageSource(_grf.GatPreview(item.FullPath));
        string path = item.FullPath;
        if (item.Ext == ".act") path = Path.ChangeExtension(path, ".spr");
        try { return GrfImaging.ToImageSource(_grf.BrowseImage(path), ThumbDecodeWidth); }
        catch { return null; }
    }

    /// <summary>Opens an item: navigate into a folder (or up), or preview a file.</summary>
    public void Open(GrfItem? item)
    {
        if (item is null) return;
        if (item.IsFolder || item.IsUp) Navigate(item.FullPath);
        else Preview(item.FullPath);
    }

    public void SelectItem(GrfItem? item)
    {
        if (item is null || item.IsFolder || item.IsUp) return;
        Preview(item.FullPath);
    }

    public void SelectNode(GrfNode? node)
    {
        if (node is not null) Navigate(node.FullPath);
    }

    [RelayCommand]
    private void Up()
    {
        if (CurrentPath.Length == 0) return;
        Navigate(CurrentPath.Contains('\\') ? CurrentPath[..CurrentPath.LastIndexOf('\\')] : string.Empty);
    }

    private void Preview(string path)
    {
        ClearPreview();
        _previewPath = path;
        OnPropertyChanged(nameof(CanExport));
        HasPreview = true;
        PreviewTitle = Display(Path.GetFileName(path));
        Zoom = 1.0;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        PreviewExt = ext;
        PopulateDetails(path);

        // Audio decompresses + writes a temp file off the UI thread so a big .mp3 never freezes the app.
        if (GrfExt.Audio.Contains(ext)) { _ = PreviewAudioAsync(path); return; }

        byte[]? data = _grf.BrowseData(path);
        if (GrfExt.Sprite.Contains(ext)) PreviewSprite(path, ext, data);
        else if (ext == ".gat") PreviewGat(path);
        else if (GrfExt.Image.Contains(ext)) ShowImagePreview(GrfImaging.ToImageSource(_grf.BrowseImage(path)), data);
        else if (GrfExt.Map.Contains(ext)) PreviewMapInfo(path, data);
        else if (GrfExt.Text.Contains(ext)) ShowTextPreview(_grf.BrowseText(path, _viewCodePage) ?? (data is null ? "(unreadable)" : HexDump(data)));
        else ShowTextPreview(data is null ? "(unreadable)" : HexDump(data));

        PreviewSubtitle = BuildSubtitle(data);
    }

    private string BuildSubtitle(byte[]? data)
    {
        string size = data is null ? string.Empty : HumanSize(data.Length);
        string dims = PreviewImage is { } img ? $"{(int)img.Width} × {(int)img.Height}px" : string.Empty;
        if (ShowInfo) return $"{InfoKind}{(size.Length > 0 ? "  ·  " + size : string.Empty)}";
        return string.Join("  ·  ", new[] { dims, size }.Where(s => s.Length > 0));
    }

    private void ShowImagePreview(ImageSource? img, byte[]? data)
    {
        if (img is null) { ShowTextPreview(data is null ? "(unreadable)" : HexDump(data)); return; }
        PreviewImage = img;
        ShowImage = true;
        OnPropertyChanged(nameof(IsImageKind));
    }

    private void PreviewGat(string path)
    {
        var img = GrfImaging.ToImageSource(_grf.GatPreview(path));
        if (img is null) { var d = _grf.BrowseData(path); ShowTextPreview(d is null ? "(unreadable)" : HexDump(d)); return; }
        PreviewImage = img;
        ShowImage = true;
        OnPropertyChanged(nameof(IsImageKind));
        var sz = _grf.GatSize(path);
        InfoKind = sz is { } s ? $"Map · {s.Width} × {s.Height} cells" : "Map";
    }

    private void PreviewSprite(string path, string ext, byte[]? data)
    {
        string sprPath = ext == ".act" ? Path.ChangeExtension(path, ".spr") : path;
        byte[]? spr = ext == ".spr" ? data : _grf.BrowseData(sprPath);
        byte[]? act = _grf.BrowseData(Path.ChangeExtension(sprPath, ".act"));
        if (spr is null) { if (data is not null) ShowTextPreview(HexDump(data)); return; }

        var anim = SpriteRenderer.Build(spr, act);
        if (anim is { Frames.Count: > 0 })
        {
            Animation = anim;
            ShowAnimation = true;
            InfoKind = anim.Frames.Count > 1 ? $"Animation · {anim.Frames.Count} frames" : "Sprite";
        }
        else ShowImagePreview(GrfImaging.ToImageSource(_grf.BrowseImage(sprPath)), spr);
    }

    private void PreviewMapInfo(string path, byte[]? data)
    {
        var info = _grf.FileInfo(path);
        if (info is null) { ShowTextPreview(data is null ? "(unreadable)" : HexDump(data)); return; }

        InfoKind = info.Kind;
        foreach (var p in info.Properties) InfoRows.Add(new InfoRow(p.Key, p.Value));
        foreach (var item in info.Items.Take(200)) InfoItems.Add(item);
        foreach (var tex in info.Textures.Take(120))
            Thumbs.Add(new GrfThumb(Path.GetFileName(tex), tex, p => GrfImaging.ToImageSource(_grf.BrowseImage(p), ThumbDecodeWidth)));
        ShowInfo = true;
    }

    private void ShowTextPreview(string text)
    {
        PreviewText = text;
        ShowText = true;
    }

    private void ClearPreview()
    {
        _previewPath = null;
        HasPreview = ShowImage = ShowAnimation = ShowInfo = ShowText = false;
        CleanupAudio();
        PreviewImage = null;
        Animation = null;
        PreviewText = null;
        PreviewTitle = PreviewSubtitle = InfoKind = string.Empty;
        InfoRows.Clear();
        InfoItems.Clear();
        Thumbs.Clear();
        DetailRows.Clear();
        Crc32 = Md5 = null;
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(IsImageKind));
    }

    // ----- View encoding: re-decode content + re-project display names (never writes) -----

    partial void OnSelectedEncodingChanged(EncodingChoice? value)
    {
        if (value is null) return;
        if (value.CodePage < 0) { CustomEncodingVisible = true; return; } // "Custom codepage…" — reveal the box
        CustomEncodingVisible = false;
        ApplyCodePage(value.CodePage);
    }

    /// <summary>Applies the codepage typed into the custom box (bound to Enter / a Go button).</summary>
    [RelayCommand]
    private void ApplyCustomEncoding()
    {
        if (int.TryParse(CustomEncodingText.Trim(), out int cp) && ViewEncoding.IsKnown(cp)) ApplyCodePage(cp);
        else Status = $"Unknown codepage \"{CustomEncodingText}\".";
    }

    private void ApplyCodePage(int codePage)
    {
        if (codePage == _viewCodePage) return;
        _viewCodePage = codePage;

        // re-decode the open text preview in place
        if (_previewPath is not null && ShowText && GrfExt.Text.Contains(Path.GetExtension(_previewPath).ToLowerInvariant()))
            PreviewText = _grf.BrowseText(_previewPath, _viewCodePage) ?? PreviewText;
        if (_previewPath is not null) PreviewTitle = Display(Path.GetFileName(_previewPath));

        // re-project the displayed tree + list names (lookup keys stay 1252)
        RootNodes.Clear();
        foreach (var node in LoadTreeChildren(string.Empty)) RootNodes.Add(node);
        RebuildItems();
    }

    partial void OnFilterTextChanged(string value) => RebuildItems();

    // ----- Details (properties) + hashes -----

    private void PopulateDetails(string path)
    {
        DetailRows.Clear();
        Crc32 = Md5 = null;
        if (_grf.BrowseEntryInfo(path) is { } info)
            foreach (var (label, val) in GrfEntryProperties.Format(info))
                DetailRows.Add(new InfoRow(label, val));
    }

    [RelayCommand]
    private async Task ComputeHashes()
    {
        if (_previewPath is null) return;
        string path = _previewPath;
        Crc32 = Md5 = "…";
        var (crc, md5) = await Task.Run(() =>
        {
            var data = _grf.BrowseData(path);
            return data is null ? (null, null) : ((string?)GrfHashing.Crc32(data), (string?)GrfHashing.Md5(data));
        });
        if (_previewPath == path) { Crc32 = crc ?? "—"; Md5 = md5 ?? "—"; }
    }

    // ----- Audio (.wav / .mp3): play from a temp file, never the data folder -----

    private async Task PreviewAudioAsync(string path)
    {
        ShowAudio = true;          // show the player chrome immediately
        InfoKind = "Audio";
        PreviewSubtitle = "Audio · loading…";

        byte[]? data;
        try { data = await Task.Run(() => _grf.BrowseData(path)); }
        catch { data = null; }

        if (_previewPath != path) return;                       // user moved on while we were loading
        if (data is null) { ShowAudio = false; ShowTextPreview("(couldn't load audio)"); return; }
        PreviewSubtitle = "Audio";
        AudioData = data;          // the player loads + auto-plays in-memory (codec init off the UI thread)
    }

    private void CleanupAudio()
    {
        ShowAudio = false;
        AudioData = null;          // null stops + frees the player
    }

    // ----- Preview background swatch -----

    [RelayCommand]
    private void SetPreviewBackground(string? spec)
    {
        PreviewBackground = spec switch
        {
            "black" => Brushes.Black,
            "white" => Brushes.White,
            "magenta" => Brushes.Magenta,
            "checker" or null or "" => null, // null => the checkerboard underlay shows
            _ => TryParseColor(spec, out var c) ? new SolidColorBrush(c) : PreviewBackground,
        };
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex)!; return true; }
        catch { color = default; return false; }
    }

    // ----- Full-content search (grep): async + cancellable, over text-type entries -----

    [RelayCommand]
    private async Task Search()
    {
        string q = SearchQuery.Trim();
        if (q.Length == 0) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        SearchResults.Clear();
        ShowSearch = true;
        IsSearching = true;
        int cp = _viewCodePage;
        try
        {
            var hits = await Task.Run(() =>
            {
                var list = new List<SearchHit>();
                foreach (var raw in _grf.BrowseEntries())
                {
                    ct.ThrowIfCancellationRequested();
                    string path = raw.Replace('/', '\\').Trim('\\');
                    if (path.Length == 0 || !GrfSearch.IsTextExtension(Path.GetExtension(path).ToLowerInvariant())) continue;
                    var text = _grf.BrowseText(path, cp);
                    if (text is not null && GrfSearch.ContentMatches(text, q))
                    {
                        list.Add(new SearchHit(Display(Path.GetFileName(path)), path));
                        if (list.Count >= 500) break;
                    }
                }
                return list;
            }, ct);
            foreach (var h in hits) SearchResults.Add(h);
            Status = $"{SearchResults.Count} file(s) contain \"{q}\"" + (SearchResults.Count >= 500 ? " (showing first 500)." : ".");
        }
        catch (OperationCanceledException) { Status = "Search cancelled."; }
        catch (Exception ex) { Status = "Search failed: " + ex.Message; }
        finally { IsSearching = false; }
    }

    [RelayCommand] private void OpenSearch() => ShowSearch = true;
    [RelayCommand] private void CancelSearch() => _searchCts?.Cancel();
    [RelayCommand] private void CloseSearch() { _searchCts?.Cancel(); ShowSearch = false; SearchResults.Clear(); }

    /// <summary>Opens a search result: navigate to its folder and preview the file.</summary>
    public void OpenSearchHit(SearchHit? hit)
    {
        if (hit is null) return;
        string parent = hit.Path.Contains('\\') ? hit.Path[..hit.Path.LastIndexOf('\\')] : string.Empty;
        ShowSearch = false; // reveal the preview behind the results overlay
        Navigate(parent);
        Preview(hit.Path);
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(Zoom * 1.5, 16);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(Zoom / 1.5, 0.2);
    [RelayCommand] private void ZoomReset() => Zoom = 1.0;
    [RelayCommand] private void ToggleWrap() => WrapText = !WrapText;
    [RelayCommand] private void Reload() => RefreshFromConfig();

    /// <summary>Exports the previewed entry to a loose file the user picks (read from GRF, written to disk — never to the GRF).</summary>
    [RelayCommand]
    private void Export()
    {
        if (_previewPath is null) return;
        var data = _grf.BrowseData(_previewPath);
        if (data is null) { Status = "Couldn't read the file to export."; return; }

        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(_previewPath),
            Filter = "All files|*.*",
            Title = "Export GRF file",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, data);
            Status = $"Exported {Path.GetFileName(dialog.FileName)} ({HumanSize(data.Length)}).";
        }
        catch (Exception ex)
        {
            Status = "Export failed: " + ex.Message;
        }
    }

    internal static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.0} {units[u]}";
    }

    private static string HexDump(byte[] data, int max = 4096)
    {
        var sb = new StringBuilder();
        int len = Math.Min(data.Length, max);
        for (int i = 0; i < len; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            var ascii = new StringBuilder();
            for (int j = 0; j < 16; j++)
            {
                if (i + j < len)
                {
                    byte b = data[i + j];
                    sb.Append(b.ToString("X2")).Append(' ');
                    ascii.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
                }
                else sb.Append("   ");
            }
            sb.Append(' ').Append(ascii).Append('\n');
        }
        if (data.Length > max) sb.Append($"\n… {HumanSize(data.Length - max)} more (binary preview truncated)");
        return sb.ToString();
    }
}
