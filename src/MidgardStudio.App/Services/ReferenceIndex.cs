using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.App.Services;

/// <summary>
/// Cached <see cref="IReferenceIndex"/> over the loaded workspace. For each referenced database it
/// precomputes the set of valid reference names (AegisName, or the skill <c>Name</c>, or the string
/// key) once per scan, so the validator's reference checks are O(1) instead of a per-field linear
/// scan. The cache is dropped on profile reload, mode change, or an explicit <see cref="Invalidate"/>.
/// </summary>
public sealed class ReferenceIndex : IReferenceIndex
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private readonly Dictionary<string, HashSet<string>> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ReferenceIndex(WorkspaceSession session, SchemaRegistry schemas)
    {
        _session = session;
        _schemas = schemas;
        _session.WorkspaceReloaded += Invalidate;
        _session.ModeChanged += Invalidate;
        // A record add/edit/delete/undo/redo can change the set of valid reference names, so drop the cache
        // on any command-stack change. This lets the validator reuse the cache between no-edit scans.
        _session.Commands.Changed += Invalidate;
    }

    public void Invalidate()
    {
        lock (_lock) _cache.Clear();
    }

    public bool Knows(string dbId) => dbId == MobAvailConstants.SpriteRefDb || _schemas.Has(dbId);

    public bool Contains(string dbId, string referenceValue)
    {
        var set = GetSet(dbId);
        return set is not null && set.Contains(referenceValue);
    }

    private HashSet<string>? GetSet(string dbId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(dbId, out var cached)) return cached;

            // The synthetic mob_avail "Sprite" source: a value is "known" if it's a mob AegisName or a player
            // job constant (NPC sprite constants stay unknown -> a soft warning, which is acceptable).
            if (dbId == MobAvailConstants.SpriteRefDb)
            {
                var sprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_schemas.Get("mob_db") is { } mobSchema)
                {
                    try
                    {
                        foreach (var rec in _session.GetActiveOverlay(mobSchema).Effective())
                        {
                            var n = rec.GetString("AegisName");
                            if (!string.IsNullOrWhiteSpace(n)) sprites.Add(n);
                        }
                    }
                    catch { /* mob_db not loadable — jobs alone still resolve */ }
                }
                foreach (var job in MobAvailConstants.Jobs) sprites.Add(job);
                _cache[dbId] = sprites;
                return sprites;
            }

            var schema = _schemas.Get(dbId);
            if (schema is null) return null;

            string nameField = NameField(schema);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var rec in _session.GetActiveOverlay(schema).Effective())
                {
                    var name = rec.GetString(nameField);
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
                }
            }
            catch { /* db not loadable in this workspace — leave the set empty */ }

            _cache[dbId] = set;
            return set;
        }
    }

    /// <summary>The field that references to a database resolve against: AegisName, else the skill
    /// Aegis (<c>Name</c>), else the string key field.</summary>
    internal static string NameField(DbSchema s) =>
        s.Field("AegisName") is not null ? "AegisName"
        : s.Field("Name") is not null ? "Name"
        : s.KeyField?.Name ?? "AegisName";
}
