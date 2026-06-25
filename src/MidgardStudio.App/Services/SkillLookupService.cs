using System;
using System.Collections.Generic;

namespace MidgardStudio.App.Services;

/// <summary>One skill row for the script generator's skill picker.</summary>
public readonly record struct SkillEntry(int Id, string Aegis, string Display, bool IsMonster);

/// <summary>
/// Searchable index of <c>skill_db</c> for the script generator. Player job (and player-summon) skills are
/// shown by default; <b>monster skills</b> — AegisName <c>NPC_*</c> — are flagged and excluded unless the
/// user opts in, since players can't normally cast them. Built lazily from the active overlay and dropped
/// when the profile changes.
/// </summary>
public sealed class SkillLookupService
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private List<SkillEntry>? _cache;

    public SkillLookupService(WorkspaceSession session, SchemaRegistry schemas)
    {
        _session = session;
        _schemas = schemas;
        _session.WorkspaceReloaded += () => _cache = null;
    }

    private IReadOnlyList<SkillEntry> All()
    {
        if (_cache is not null) return _cache;

        var list = new List<SkillEntry>();
        if (_schemas.Get("skill_db") is { } schema)
        {
            foreach (var r in _session.GetActiveOverlay(schema).Effective())
            {
                string aegis = r.GetString("Name") ?? string.Empty;
                if (string.IsNullOrEmpty(aegis)) continue;
                string? desc = r.GetString("Description");
                bool monster = aegis.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase);
                list.Add(new SkillEntry(r.GetInt("Id"), aegis, string.IsNullOrEmpty(desc) ? aegis : desc!, monster));
            }
        }
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        _cache = list;
        return list;
    }

    public bool HasData => All().Count > 0;

    /// <summary>Skills matching <paramref name="query"/> (id / aegis / name). Monster (NPC_) skills are
    /// included only when <paramref name="includeMonster"/> is set.</summary>
    public IReadOnlyList<SkillEntry> Search(string? query, bool includeMonster, int limit = 300)
    {
        string q = (query ?? string.Empty).Trim();
        var results = new List<SkillEntry>();
        foreach (var s in All())
        {
            if (!includeMonster && s.IsMonster) continue;
            if (q.Length == 0
                || s.Aegis.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Display.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Id.ToString().Contains(q))
            {
                results.Add(s);
                if (results.Count >= limit) break;
            }
        }
        return results;
    }
}
