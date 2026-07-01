using System.Globalization;
using GRF.Core.GroupedGrf;
using GRF.FileFormats.GatFormat;
using GRF.FileFormats.GndFormat;
using GRF.FileFormats.LubFormat;
using GRF.FileFormats.RsmFormat;
using GRF.FileFormats.RswFormat;
using GRF.Image;
using MidgardStudio.Core.Grf;
using Utilities.Services;

namespace MidgardStudio.Grf;

/// <summary>Structured metadata extracted from a map/model file (ground, world or model) for the preview.</summary>
public sealed class GrfFileInfo
{
    public string Kind { get; set; } = string.Empty;
    public List<KeyValuePair<string, string>> Properties { get; } = new();
    /// <summary>Named resources contained by the file (e.g. the models a world references).</summary>
    public List<string> Items { get; } = new();
    /// <summary>In-GRF texture paths that can be previewed as thumbnails.</summary>
    public List<string> Textures { get; } = new();
}

/// <summary>
/// Reads one or more layered client GRF archives (plus optional loose data folders) via the Tokeiburu
/// GRF library. All client content uses the Windows-1252 default codepage. GRF support is optional —
/// when no paths are configured, every read returns null/empty gracefully.
/// </summary>
public sealed class GrfService : IDisposable
{
    private readonly MultiGrfReader _multi = new();
    private readonly MultiGrfReader _browse = new(); // a single source opened for the Explorer (read-only)
    private readonly MultiGrfReader _icon = new();   // a single source opened for the icon picker (isolated)
    private string? _browseSource;
    private string? _iconSource;
    private bool _configured;

    static GrfService()
    {
        // RO client default codepage for GRF entry names and lua text (1252 = Western European).
        try { EncodingService.SetDisplayEncoding(1252); } catch { /* provider not registered yet */ }
    }

    public bool IsConfigured => _configured;

    /// <summary>
    /// Sets the codepage used to decode GRF entry names and lua text for display (a global in the GRF
    /// library). Call this BEFORE (re)configuring sources so entries decode with the right encoding —
    /// kRO archives store names as EUC-KR (949), Western/translated ones as Windows-1252. This is a
    /// display concern only; the app never writes GRF archives.
    /// </summary>
    public void SetDisplayCodepage(int codepage)
    {
        try { EncodingService.SetDisplayEncoding(codepage > 0 ? codepage : 1252); }
        catch { /* provider not registered / unknown codepage — keep the previous display encoding */ }
    }

    /// <summary>Raised when the configured sources change, so caches built over GRF content can be dropped.</summary>
    public event Action? SourcesChanged;

    /// <summary>The validated layered sources (GRF files and/or loose data folders) currently configured.</summary>
    public IReadOnlyList<string> Sources { get; private set; } = Array.Empty<string>();

    /// <summary>Configure the layered sources (GRF files and/or loose data folders); last wins.</summary>
    public void Configure(IEnumerable<string> sources)
    {
        var valid = sources
            .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
            .ToList();
        Sources = valid;

        if (valid.Count == 0)
        {
            _configured = false;
            SourcesChanged?.Invoke();
            return;
        }

        _multi.Update(valid.Select(p => new MultiGrfPath(p)).ToList());
        _configured = true;
        SourcesChanged?.Invoke();
    }

    // ----- Read-only Explorer: browse ONE source at a time (never opened for writing) -----

    /// <summary>Opens a single source (GRF or data folder) for read-only browsing in the Explorer.</summary>
    public void OpenBrowseSource(string source)
    {
        _browse.Update(new List<MultiGrfPath> { new MultiGrfPath(source) });
        _browseSource = source;
    }

    /// <summary>Every entry path in the browse source (full flat list, read-only).</summary>
    public IReadOnlyList<string> BrowseEntries()
    {
        if (_browseSource is null) return Array.Empty<string>();
        try
        {
            // FileTable.Entries is empty until the reader's buffers are lazily generated; FilesInDirectory
            // from the root with AllDirectories enumerates the whole tree via the live read path instead.
#pragma warning disable CS0618 // obsolete-but-correct on MultiFileTable (same call the icon lookups use)
            return _browse.FileTable.FilesInDirectory(string.Empty, System.IO.SearchOption.AllDirectories, true);
#pragma warning restore CS0618
        }
        catch { return Array.Empty<string>(); }
    }

    public byte[]? BrowseData(string relativePath)
    {
        if (_browseSource is null) return null;
        try { return _browse.GetData(Normalize(relativePath)); }
        catch { return null; }
    }

    /// <summary>Decodes a browse entry as an image (bmp/tga/jpg/png/pal/spr/…), or null.</summary>
    public GrfImage? BrowseImage(string relativePath)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        try { return ImageProvider.GetImage(data, Path.GetExtension(relativePath)); }
        catch { return null; }
    }

    /// <summary>Renders a .gat as its walkability/height minimap image, or null.</summary>
    public GrfImage? GatPreview(string relativePath)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        try { return GatPreviewImageMaker.LoadQuickPreviewImage(data); }
        catch { return null; }
    }

    /// <summary>Width/height of a .gat in cells, or null.</summary>
    public (int Width, int Height)? GatSize(string relativePath)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        try { var g = new Gat(data); return (g.Width, g.Height); }
        catch { return null; }
    }

    /// <summary>Parses a map/model file (.gnd/.rsw/.rsm) into structured metadata + texture list, or null.</summary>
    public GrfFileInfo? FileInfo(string relativePath)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        string ext = Path.GetExtension(relativePath).ToLowerInvariant();
        try
        {
            var info = new GrfFileInfo();
            switch (ext)
            {
                case ".rsm":
                case ".rsm2":
                    var rsm = new Rsm(data);
                    info.Kind = "3D Model";
                    info.Properties.Add(new("Version", rsm.Version.ToString("0.0", CultureInfo.InvariantCulture)));
                    info.Properties.Add(new("Meshes", rsm.Meshes.Count.ToString(CultureInfo.InvariantCulture)));
                    info.Properties.Add(new("Textures", rsm.Textures.Count.ToString(CultureInfo.InvariantCulture)));
                    foreach (var t in rsm.Textures) info.Textures.Add(ResolveTexture(t));
                    return info;

                case ".rsw":
                    var rsw = new Rsw(data);
                    info.Kind = "World";
                    info.Properties.Add(new("Objects", rsw.Objects.Count.ToString(CultureInfo.InvariantCulture)));
                    var models = rsw.ModelResources;
                    info.Properties.Add(new("Models", models.Count.ToString(CultureInfo.InvariantCulture)));
                    foreach (var m in models) info.Items.Add(m);
                    return info;

                case ".gnd":
                    var gnd = new Gnd(data);
                    info.Kind = "Ground";
                    info.Properties.Add(new("Textures", gnd.Textures.Count.ToString(CultureInfo.InvariantCulture)));
                    info.Properties.Add(new("Lightmaps", gnd.Lightmaps.Count.ToString(CultureInfo.InvariantCulture)));
                    foreach (var t in gnd.Textures) info.Textures.Add(ResolveTexture(t));
                    return info;

                default:
                    return null;
            }
        }
        catch { return null; }
    }

    /// <summary>Parses an .rsm/.rsm2 model and bakes it into GL-ready geometry (with textures), or null.</summary>
    public ModelGeometry? BuildModel(string relativePath)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        try { return RsmModelBuilder.Build(new Rsm(data), LoadModelTexture); }
        catch { return null; }
    }

    /// <summary>Host-provided decoder for encoded images the GRF library's ImageProvider can't handle
    /// (notably .jpg water textures). Set by the App to a WPF-based decoder; returns BGRA pixels.</summary>
    public static Func<byte[], (int Width, int Height, byte[] Bgra)?>? EncodedImageDecoder;

    private ModelTexture? LoadModelTexture(string rawTextureName)
    {
        try
        {
            string path = ResolveTexture(rawTextureName);
            var img = BrowseImage(path);
            if (img is not null) return GrfTexture.ToBgra(img);

            // fallback: raw bytes decoded by the host (e.g. .jpg, which the GRF ImageProvider skips)
            if (EncodedImageDecoder is not null)
            {
                var data = BrowseData(path);
                if (data is not null && EncodedImageDecoder(data) is { } d && d.Bgra.Length >= d.Width * d.Height * 4)
                    return new ModelTexture { Width = d.Width, Height = d.Height, Bgra = d.Bgra };
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Builds a 3D map (GND terrain + RSW-placed RSM models) from a .gnd or .rsw, or null.</summary>
    public MapGeometry? BuildMap(string relativePath)
    {
        try
        {
            string ext = Path.GetExtension(relativePath).ToLowerInvariant();
            string gndPath = relativePath;
            Rsw? rsw = null;
            if (ext == ".rsw")
            {
                var rswData = BrowseData(relativePath);
                if (rswData is null) return null;
                rsw = new Rsw(rswData);
                string gndName = rsw.Header.GroundFile;
                if (string.IsNullOrEmpty(gndName)) return null;
                string gndDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                gndPath = gndDir.Length == 0 ? gndName : gndDir + "\\" + gndName;
            }

            var gndData = BrowseData(gndPath);
            if (gndData is null) return null;
            var gnd = new Gnd(gndData);
            var terrain = GndTerrainBuilder.Build(gnd, LoadModelTexture);

            IReadOnlyList<MapModelInstance> models = Array.Empty<MapModelInstance>();
            float[] dir = terrain.LightDir, amb = terrain.Ambient, dif = terrain.Diffuse;
            if (rsw is not null)
            {
                var cache = new Dictionary<string, ModelGeometry?>(StringComparer.OrdinalIgnoreCase);
                models = RswModelPlacement.Build(rsw, gnd, mp =>
                {
                    if (!cache.TryGetValue(mp, out var g)) cache[mp] = g = BuildModel(mp);
                    return g;
                });
                if (rsw.Light is not null) (dir, amb, dif) = RswLighting.Compute(rsw.Light);
            }

            // water: GND zones (v1.8+) take precedence, else the single RSW water zone
            MapWater? water = null;
            var wzone = gnd.Water?.Zones.Count > 0 ? gnd.Water.Zones[0] : rsw?.Water;
            if (wzone is not null) water = WaterBuilder.Build(terrain.Min, terrain.Max, wzone, LoadModelTexture);

            return new MapGeometry
            {
                Terrain = terrain.Terrain, Models = models, Water = water,
                Center = terrain.Center, Radius = terrain.Radius, Min = terrain.Min, Max = terrain.Max,
                LightDir = dir, Ambient = amb, Diffuse = dif,
            };
        }
        catch { return null; }
    }

    private static string ResolveTexture(string name)
    {
        name = name.Replace('/', '\\').TrimStart('\\');
        return name.StartsWith("data\\", StringComparison.OrdinalIgnoreCase) ? name : "data\\texture\\" + name;
    }

    /// <summary>Decompressed size of an entry (read-only), or null.</summary>
    public long? BrowseSize(string relativePath)
    {
        if (_browseSource is null) return null;
        try { return _browse.FileTable.TryGet(Normalize(relativePath))?.SizeDecompressed; }
        catch { return null; }
    }

    /// <summary>Decodes a browse entry as text (lub bytecode is decompiled), or null when binary.</summary>
    public string? BrowseText(string relativePath)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        try
        {
            return Lub.IsCompiled(data)
                ? new Lub(data).Decompile()
                : EncodingService.DisplayEncoding.GetString(data);
        }
        catch { return null; }
    }

    /// <summary>
    /// Decodes a browse entry as text in a specific <paramref name="codePage"/> (lub bytecode still decompiles).
    /// Used by the browser's view-encoding selector; does NOT touch the global display encoding.
    /// </summary>
    public string? BrowseText(string relativePath, int codePage)
    {
        var data = BrowseData(relativePath);
        if (data is null) return null;
        try
        {
            return Lub.IsCompiled(data) ? new Lub(data).Decompile() : ViewEncoding.Decode(data, codePage);
        }
        catch { return null; }
    }

    /// <summary>Size / compressed size / offset / flags for a browse entry, or null.</summary>
    public GrfEntryInfo? BrowseEntryInfo(string relativePath)
    {
        if (_browseSource is null) return null;
        try
        {
            var e = _browse.FileTable.TryGet(Normalize(relativePath));
            return e is null ? null : new GrfEntryInfo(e.SizeDecompressed, e.SizeCompressed, e.FileExactOffset, (int)e.Flags);
        }
        catch { return null; }
    }

    // ----- Icon picker: browse the item-icon folder of ONE chosen source only (isolated reader) -----

    /// <summary>Opens a single source (GRF or data folder) for the icon picker, kept separate from the
    /// Explorer's browse reader so opening the picker never disturbs the GRF Browser's current source.</summary>
    public void OpenIconSource(string source)
    {
        _icon.Update(new List<MultiGrfPath> { new MultiGrfPath(source) });
        _iconSource = source;
    }

    public void CloseIconSource() => _iconSource = null;

    /// <summary>The base names (no folder, no <c>.bmp</c>) of every item inventory icon in the chosen source —
    /// listing ONLY the item-icon directory (not the whole archive) and ONLY <c>.bmp</c> entries.</summary>
    public IReadOnlyList<string> ItemIconResourceNames()
    {
        if (_iconSource is null) return Array.Empty<string>();
        try
        {
#pragma warning disable CS0618 // obsolete-but-correct on MultiFileTable (same call the icon lookups use)
            var files = _icon.FileTable.FilesInDirectory(GrfAssetPaths.ItemIconDir, System.IO.SearchOption.TopDirectoryOnly, true);
#pragma warning restore CS0618
            var names = new List<string>();
            foreach (var f in files)
                if (f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                    names.Add(Path.GetFileNameWithoutExtension(f));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Base names (no folder, no "여_" prefix, no ".spr") of every headgear accessory sprite in the
    /// chosen source — listing ONLY the female accessory sprite dir. The base name is exactly what the user
    /// types as the sprite name (the male sprite mirrors it under 남\남_). Uses the isolated picker reader.</summary>
    public IReadOnlyList<string> HeadgearSpriteNames()
    {
        if (_iconSource is null) return Array.Empty<string>();
        try
        {
#pragma warning disable CS0618 // obsolete-but-correct on MultiFileTable (same call the icon lookups use)
            var files = _icon.FileTable.FilesInDirectory(GrfAssetPaths.HeadgearSpriteDir, System.IO.SearchOption.TopDirectoryOnly, true);
#pragma warning restore CS0618
            string prefix = GrfAssetPaths.HeadgearSpritePrefix;
            var names = new List<string>();
            foreach (var f in files)
                if (f.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
                    names.Add(name);
                }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Decodes one item icon (by resource base name) from the chosen icon source.</summary>
    public GrfImage? ItemIconFromSource(string resourceName)
    {
        if (_iconSource is null || string.IsNullOrWhiteSpace(resourceName)) return null;
        try
        {
            var data = _icon.GetData(Normalize(GrfAssetPaths.ItemIcon(resourceName)));
            return data is null ? null : ImageProvider.GetImage(data, ".bmp");
        }
        catch { return null; }
    }

    /// <summary>Raw bytes of a file from the chosen icon/loose source (not the main configured GRF) — lets the
    /// pickers read a sprite's .spr/.act from whichever source the user is browsing.</summary>
    public byte[]? GetDataFromSource(string relativePath)
    {
        if (_iconSource is null || string.IsNullOrWhiteSpace(relativePath)) return null;
        try { return _icon.GetData(Normalize(relativePath)); }
        catch { return null; }
    }

    public byte[]? GetData(string relativePath)
    {
        if (!_configured) return null;
        try { return _multi.GetData(Normalize(relativePath)); }
        catch { return null; }
    }

    public bool Exists(string relativePath)
    {
        if (!_configured) return false;
        try { return _multi.Exists(Normalize(relativePath)); }
        catch { return false; }
    }

    public IEnumerable<string> FilesInDirectory(string directory)
    {
        if (!_configured) return Array.Empty<string>();
        try
        {
            // MultiGrfReader.FilesInDirectory routes through ContainerTable.GetFiles -> Files, which the
            // MultiFileTable does not support (it throws, leaving us with nothing). Call the file table's
            // own overridden FilesInDirectory, which correctly aggregates every layered GRF and folder.
#pragma warning disable CS0618 // obsolete-but-correct on MultiFileTable
            return _multi.FileTable.FilesInDirectory(Normalize(directory), System.IO.SearchOption.TopDirectoryOnly, true);
#pragma warning restore CS0618
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Reads a lua/lub file as text, decompiling compiled bytecode on the fly.</summary>
    public string? ReadLuaText(string relativePath)
    {
        var data = GetData(relativePath);
        if (data is null) return null;
        try
        {
            return Lub.IsCompiled(data)
                ? new Lub(data).Decompile()
                : EncodingService.DisplayEncoding.GetString(data);
        }
        catch { return null; }
    }

    /// <summary>Decodes an image entry (bmp/spr/tga/...) into a <see cref="GrfImage"/>, or null.</summary>
    public GrfImage? GetImage(string relativePath)
    {
        var data = GetData(relativePath);
        if (data is null) return null;
        try { return ImageProvider.GetImage(data, Path.GetExtension(relativePath)); }
        catch { return null; }
    }

    private static string Normalize(string p) => p.Replace('/', '\\');

    public void Dispose()
    {
        _multi.Dispose();
        _browse.Dispose();
        _icon.Dispose();
    }
}
