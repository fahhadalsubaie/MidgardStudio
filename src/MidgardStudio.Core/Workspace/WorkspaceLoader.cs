using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Core.Workspace;

/// <summary>
/// Loads a database's base bundle (re or pre-re leaves, read-only) plus the editable import layer
/// into an <see cref="OverlayTable"/>.
/// </summary>
public sealed class WorkspaceLoader
{
    private readonly YamlDbReader _reader = new();

    public OverlayTable LoadOverlay(DbSchema schema, WorkspacePaths paths, ServerMode mode, int clientCodepage = 1252)
    {
        var baseLayer = LoadBase(schema, paths, mode, clientCodepage);
        var importLayer = LoadImport(schema, paths, out var importPath, clientCodepage);
        return new OverlayTable(schema, baseLayer, importLayer, importPath);
    }

    /// <summary>Loads the import layer + only <paramref name="activeMode"/>'s base now; the other mode's
    /// base is parsed lazily on first access (see <see cref="ModeSet"/>). Both modes share one import
    /// layer (rAthena's db/import applies in both). <paramref name="clientCodepage"/> is the legacy-encoding
    /// fallback used to display names in non-UTF-8 (translated) base data.</summary>
    public ModeSet LoadModeSet(DbSchema schema, WorkspacePaths paths, ServerMode activeMode, int clientCodepage = 1252)
    {
        var import = LoadImport(schema, paths, out var importPath, clientCodepage);
        OverlayTable Build(ServerMode mode) =>
            new(schema, LoadBase(schema, paths, mode, clientCodepage), import, importPath);
        return new ModeSet(activeMode, Build, import, importPath);
    }

    private DbLayer LoadBase(DbSchema schema, WorkspacePaths paths, ServerMode mode, int clientCodepage)
    {
        var layer = new DbLayer();
        foreach (var rel in schema.Layout.BaseFiles(mode))
        {
            var path = ResolvePath(paths.ServerDbRoot, rel);
            if (!File.Exists(path)) continue;
            foreach (var rec in _reader.ReadFile(path, schema, RecordOrigin.Base, clientCodepage).Records)
                layer.Add(rec);
        }
        return layer;
    }

    private DbLayer LoadImport(DbSchema schema, WorkspacePaths paths, out string importPath, int clientCodepage)
    {
        var layer = new DbLayer();
        importPath = ResolvePath(paths.ServerDbRoot, schema.Layout.ImportFile);
        if (File.Exists(importPath))
        {
            foreach (var rec in _reader.ReadFile(importPath, schema, RecordOrigin.NewCustom, clientCodepage).Records)
                layer.Add(rec);
        }
        return layer;
    }

    private static string ResolvePath(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
