using MidgardStudio.Core.Lookup;

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
        var schema = _schemas.Get(referenceDb);
        if (schema is null) return Array.Empty<string>();

        var overlay = _session.GetActiveOverlay(schema);
        string q = query.Trim();

        var results = new List<string>(limit);
        foreach (var record in overlay.Effective())
        {
            var aegis = record.GetString("AegisName");
            if (string.IsNullOrEmpty(aegis)) continue;
            if (q.Length == 0 || aegis.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(aegis);
                if (results.Count >= limit) break;
            }
        }
        return results;
    }
}
