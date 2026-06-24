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

    public OverlayTable LoadOverlay(DbSchema schema, WorkspacePaths paths, ServerMode mode)
    {
        var baseLayer = LoadBase(schema, paths, mode);
        var importLayer = LoadImport(schema, paths, out var importPath);
        return new OverlayTable(schema, baseLayer, importLayer, importPath);
    }

    /// <summary>Loads both modes against a single shared import layer.</summary>
    public ModeSet LoadModeSet(DbSchema schema, WorkspacePaths paths)
    {
        var import = LoadImport(schema, paths, out var importPath);
        var renewalBase = LoadBase(schema, paths, ServerMode.Renewal);
        var preRenewalBase = LoadBase(schema, paths, ServerMode.PreRenewal);

        var renewal = new OverlayTable(schema, renewalBase, import, importPath);
        var preRenewal = new OverlayTable(schema, preRenewalBase, import, importPath);
        return new ModeSet(renewal, preRenewal, import);
    }

    private DbLayer LoadBase(DbSchema schema, WorkspacePaths paths, ServerMode mode)
    {
        var layer = new DbLayer();
        foreach (var rel in schema.Layout.BaseFiles(mode))
        {
            var path = ResolvePath(paths.ServerDbRoot, rel);
            if (!File.Exists(path)) continue;
            foreach (var rec in _reader.ReadFile(path, schema, RecordOrigin.Base).Records)
                layer.Add(rec);
        }
        return layer;
    }

    private DbLayer LoadImport(DbSchema schema, WorkspacePaths paths, out string importPath)
    {
        var layer = new DbLayer();
        importPath = ResolvePath(paths.ServerDbRoot, schema.Layout.ImportFile);
        if (File.Exists(importPath))
        {
            foreach (var rec in _reader.ReadFile(importPath, schema, RecordOrigin.NewCustom).Records)
                layer.Add(rec);
        }
        return layer;
    }

    private static string ResolvePath(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
