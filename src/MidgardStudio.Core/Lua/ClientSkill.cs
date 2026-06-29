namespace MidgardStudio.Core.Lua;

/// <summary>One prerequisite skill: a SKID constant (without the <c>SKID.</c> prefix) + required level.
/// <c>Level</c> is null for the level-less form <c>{ SKID.X }</c> some real entries use (audit #6) — it
/// must round-trip, or an unrelated edit would silently add a level and change the client's requirements.</summary>
public sealed record SkillPrereq(string Skid, int? Level);

/// <summary>Job-specific prerequisite block: a JOBID constant (without the <c>JOBID.</c> prefix) + its prereqs.</summary>
public sealed class JobPrereqs
{
    public JobPrereqs(string job, List<SkillPrereq> reqs) { Job = job; Reqs = reqs; }
    public string Job { get; set; }
    public List<SkillPrereq> Reqs { get; set; }
    public JobPrereqs Clone() => new(Job, Reqs.Select(r => r with { }).ToList());
}

/// <summary>
/// A client-side skill aggregated from the four skillinfoz lua files, keyed by its <c>SKID</c> constant
/// (e.g. <c>SM_BASH</c>). Carries the editable info/description/delay fields plus the rarer fields
/// (<c>Type</c>, job-specific <c>NeedSkillList</c>, <c>SkillFlag</c>) so a reformatted entry round-trips
/// losslessly. The numeric <see cref="Id"/> comes from the SKID table (display/sort + skill_db cross-check).
/// </summary>
public sealed class ClientSkill
{
    public string Constant { get; set; } = string.Empty;
    public int Id { get; set; }

    // --- SKILL_INFO_LIST ---
    public bool HasInfo { get; set; }
    public string Aegis { get; set; } = string.Empty;     // positional [0] (usually == Constant)
    public string SkillName { get; set; } = string.Empty; // in-game display name
    public int MaxLv { get; set; }
    // Presence flags: ~18% of official info entries have no MaxLv field, and a few carry an explicit empty
    // array. The reader can't tell absent from 0/empty, so track presence to re-emit faithfully (otherwise an
    // edited entry gains a spurious `MaxLv = 0` line — a real change for the client).
    public bool HasMaxLv { get; set; }
    public List<int> AttackRange { get; set; } = new();
    public bool HasAttackRange { get; set; }
    public List<int> SpAmount { get; set; } = new();
    public bool HasSpAmount { get; set; }
    public bool? BSeperateLv { get; set; }
    public List<SkillPrereq> NeedSkillList { get; set; } = new();      // _NeedSkillList
    public string? Type { get; set; }                                  // e.g. "Quest" (preserved)
    public List<JobPrereqs> JobNeedSkillList { get; set; } = new();    // NeedSkillList[JOBID.x] (preserved)

    /// <summary>Already-Lua-serialized values of any SKILL_INFO_LIST name-keys this editor doesn't model, in
    /// source order — captured on read, re-emitted on write so an edit can't drop an unknown field (audit sweep,
    /// the skill twin of <c>ItemInfoEntry.ExtraFields</c>).</summary>
    public Dictionary<string, string> ExtraInfoFields { get; set; } = new(System.StringComparer.Ordinal);

    // --- SKILL_DESCRIPT ---
    public bool HasDescript { get; set; }
    public List<string> Description { get; set; } = new();

    // --- SKILL_DELAY_LIST ---
    public bool HasDelay { get; set; }
    public List<int> CastFixedDelay { get; set; } = new();
    public List<int> CastStatDelay { get; set; } = new();
    public List<int> GlobalPostDelay { get; set; } = new();
    public List<int> SinglePostDelay { get; set; } = new();
    public List<string> SkillFlag { get; set; } = new();              // SKFLAG_* identifiers (preserved)

    public ClientSkill Clone() => new()
    {
        Constant = Constant,
        Id = Id,
        HasInfo = HasInfo,
        Aegis = Aegis,
        SkillName = SkillName,
        MaxLv = MaxLv,
        HasMaxLv = HasMaxLv,
        AttackRange = new List<int>(AttackRange),
        HasAttackRange = HasAttackRange,
        SpAmount = new List<int>(SpAmount),
        HasSpAmount = HasSpAmount,
        BSeperateLv = BSeperateLv,
        NeedSkillList = NeedSkillList.Select(p => p with { }).ToList(),
        Type = Type,
        JobNeedSkillList = JobNeedSkillList.Select(j => j.Clone()).ToList(),
        ExtraInfoFields = new Dictionary<string, string>(ExtraInfoFields, System.StringComparer.Ordinal),
        HasDescript = HasDescript,
        Description = new List<string>(Description),
        HasDelay = HasDelay,
        CastFixedDelay = new List<int>(CastFixedDelay),
        CastStatDelay = new List<int>(CastStatDelay),
        GlobalPostDelay = new List<int>(GlobalPostDelay),
        SinglePostDelay = new List<int>(SinglePostDelay),
        SkillFlag = new List<string>(SkillFlag),
    };
}

/// <summary>The parsed client skill workspace: the SKID constant→id map plus the aggregated skills (keyed
/// by constant). A constant can be in <see cref="Skid"/> without a skill entry (marker constants) and an
/// entry can exist in a table without being in <see cref="Skid"/> (a validation finding).</summary>
public sealed class ClientSkillTables
{
    public Dictionary<string, int> Skid { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ClientSkill> Skills { get; } = new(StringComparer.Ordinal);
}
