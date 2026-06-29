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
    private string? _browseSource;
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
    }
}
