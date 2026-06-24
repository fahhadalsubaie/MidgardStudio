using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Core.Overlay;

/// <summary>
/// The base + import overlay for one database (the SDE "MetaTable" idea). The base layer
/// (re or pre-re) is read-only; the import layer is editable. Effective value = import if present
/// else base. Editing a base record clones it into the import layer (copy-on-write). On save, only
/// the import layer is written, keeping core files pristine and upgrade-safe.
/// </summary>
public sealed class OverlayTable
{
    private readonly DbLayer _base;
    private readonly DbLayer _import;
    private bool _importDirty;

    public OverlayTable(DbSchema schema, DbLayer baseLayer, DbLayer importLayer, string importFilePath)
    {
        Schema = schema;
        _base = baseLayer;
        _import = importLayer;
        ImportFilePath = importFilePath;
    }

    public DbSchema Schema { get; }

    public string ImportFilePath { get; }

    public int BaseCount => _base.Records.Count;

    public int ImportCount => _import.Records.Count;

    public bool IsDirty => _importDirty || _import.Records.Any(r => r.IsDirty);

    public DbRecord? GetEffective(RecordKey key) =>
        _import.ByKey.TryGetValue(key, out var o) ? o : _base.Get(key);

    public RecordOrigin OriginOf(RecordKey key) =>
        _import.Contains(key)
            ? (_base.Contains(key) ? RecordOrigin.Overridden : RecordOrigin.NewCustom)
            : RecordOrigin.Base;

    /// <summary>Effective records with Origin set: base order first (import wins per key), then import-only customs.</summary>
    public IEnumerable<DbRecord> Effective()
    {
        var seen = new HashSet<RecordKey>();
        foreach (var b in _base.Records)
        {
            var key = b.Key;
            seen.Add(key);
            if (_import.ByKey.TryGetValue(key, out var ov))
            {
                ov.Origin = RecordOrigin.Overridden;
                yield return ov;
            }
            else
            {
                b.Origin = RecordOrigin.Base;
                yield return b;
            }
        }

        foreach (var im in _import.Records)
        {
            if (seen.Contains(im.Key)) continue;
            im.Origin = RecordOrigin.NewCustom;
            yield return im;
        }
    }

    /// <summary>Returns the editable import record for a key, cloning the base into import on first edit.</summary>
    public DbRecord BeginOverride(RecordKey key)
    {
        if (_import.ByKey.TryGetValue(key, out var existing))
            return existing;

        var baseRec = _base.Get(key) ?? throw new KeyNotFoundException($"No base record for key {key}.");
        var clone = baseRec.DeepClone();
        clone.Origin = RecordOrigin.Overridden;
        _import.Add(clone);
        _importDirty = true;
        return clone;
    }

    public void AddCustom(DbRecord record)
    {
        record.Origin = RecordOrigin.NewCustom;
        _import.Add(record);
        _importDirty = true;
    }

    /// <summary>Removes the import entry for a key (reverting an override back to the core value).</summary>
    public bool RevertToCore(RecordKey key)
    {
        if (_import.Remove(key))
        {
            _importDirty = true;
            return true;
        }
        return false;
    }

    public DbFile BuildImportFile()
    {
        var file = new DbFile { HeaderType = Schema.HeaderType, HeaderVersion = Schema.HeaderVersion };
        file.Records.AddRange(_import.Records);
        return file;
    }

    /// <summary>Writes the import layer to disk (defaults to <see cref="ImportFilePath"/>).</summary>
    public void Save(string? path = null)
    {
        new YamlDbWriter().WriteFile(path ?? ImportFilePath, Schema, BuildImportFile());
        foreach (var r in _import.Records) r.IsDirty = false;
        _importDirty = false;
    }
}
