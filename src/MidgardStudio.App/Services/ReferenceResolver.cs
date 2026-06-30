using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.App.Services;

/// <summary>Resolves reference-field autocomplete candidates by searching another database's AegisNames.</summary>
public sealed class ReferenceResolver : IReferenceResolver
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;

    public ReferenceResolver(WorkspaceSession session, SchemaRegistry schemas)
    {
        _session = session;
        _schemas = schemas;
    }

    public IReadOnlyList<string> Search(string referenceDb, string query, int limit = 40)
    {
        // The mob_avail "Sprite" field is a synthetic source: mobs ∪ player jobs (NPC constants are typed).
        if (referenceDb == MobAvailConstants.SpriteRefDb) return SearchSprite(query.Trim(), limit);

        var schema = _schemas.Get(referenceDb);
        if (schema is null) return Array.Empty<string>();

        var overlay = _session.GetActiveOverlay(schema);
        string q = query.Trim();
        string nameField = ReferenceIndex.NameField(schema); // skill_db identifies by "Name", not "AegisName" — match the index/validator

        var results = new List<string>(limit);
        foreach (var record in overlay.Effective())
        {
            var aegis = record.GetString(nameField);
            if (string.IsNullOrEmpty(aegis)) continue;
            if (q.Length == 0 || aegis.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(aegis);
                if (results.Count >= limit) break;
            }
        }
        return results;
    }

    private IReadOnlyList<string> SearchSprite(string q, int limit)
    {
        var results = new List<string>(limit);
        if (_schemas.Get("mob_db") is { } mobSchema)
        {
            foreach (var record in _session.GetActiveOverlay(mobSchema).Effective())
            {
                var aegis = record.GetString("AegisName");
                if (string.IsNullOrEmpty(aegis)) continue;
                if (q.Length == 0 || aegis.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(aegis);
                    if (results.Count >= limit) return results;
                }
            }
        }
        foreach (var job in MobAvailConstants.Jobs)
        {
            if (q.Length == 0 || job.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(job);
                if (results.Count >= limit) break;
            }
        }
        return results;
    }
}
