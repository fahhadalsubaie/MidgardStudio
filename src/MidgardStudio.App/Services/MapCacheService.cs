using System.IO;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.MapCache;

namespace MidgardStudio.App.Services;

/// <summary>
/// Reads the base map caches (re + pre-re, unioned for reference) and the editable import overlay,
/// and writes changes back to <c>db/import/map_cache.dat</c> only — the base caches stay untouched.
/// </summary>
public sealed class MapCacheService
{
    private readonly WorkspaceSession _session;

    public MapCacheService(WorkspaceSession session) => _session = session;

    private string Root => _session.Paths.ServerDbRoot;
    public string ImportPath => Path.Combine(Root, "import", "map_cache.dat");

    /// <summary>Maps present in the root + renewal + pre-renewal base caches, unioned by name (first wins).</summary>
    public MapCacheFile LoadBase()
    {
        var file = new MapCacheFile();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     Path.Combine(Root, "map_cache.dat"),        // db/map_cache.dat (root)
                     Path.Combine(Root, "re", "map_cache.dat"),
                     Path.Combine(Root, "pre-re", "map_cache.dat"),
                 })
        {
            if (!File.Exists(path)) continue;
            foreach (var map in MapCacheFile.Read(File.ReadAllBytes(path)).Maps)
                if (seen.Add(map.Name)) file.Maps.Add(map);
        }
        return file;
    }

    public MapCacheFile LoadImport() =>
        File.Exists(ImportPath) ? MapCacheFile.Read(File.ReadAllBytes(ImportPath)) : new MapCacheFile();

    public void SaveImport(MapCacheFile import)
    {
        string dir = Path.GetDirectoryName(ImportPath)!;
        Directory.CreateDirectory(dir);
        var tx = new FileTransaction(Path.Combine(dir, ".midgard-backup"));
        tx.Stage(ImportPath, import.Write());
        tx.Commit();
    }
}
