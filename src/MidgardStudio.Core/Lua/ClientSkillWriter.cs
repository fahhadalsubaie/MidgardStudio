using System.Text;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Formats <see cref="ClientSkill"/> data back into the three editable skill tables as
/// <c>\t[SKID.&lt;const&gt;] = { ... },\n</c> blocks (tabs, field order and color codes matching the client
/// files). Only fields that are present are emitted, so a round-tripped entry is byte-stable and an
/// untouched-then-saved skill produces no spurious diff. Mirrors <see cref="ItemInfoWriter.FormatEntry"/>.
/// </summary>
public static class ClientSkillWriter
{
    public static string FormatInfo(ClientSkill s)
    {
        // The positional aegis ([0]) is always present and anchors the comma chain; every other field leads
        // with its own ",\n" so an absent one (e.g. MaxLv on ~18% of entries) leaves no spurious line. For an
        // entry that DOES have MaxLv this is byte-identical to the previous always-emit layout.
        var sb = new StringBuilder();
        sb.Append($"\t[SKID.{s.Constant}] = {{\n");
        sb.Append($"\t\t{Quote(s.Aegis)}");
        sb.Append($",\n\t\tSkillName = {Quote(s.SkillName)}");
        if (s.HasMaxLv) sb.Append($",\n\t\tMaxLv = {s.MaxLv}");
        if (s.HasAttackRange || s.AttackRange.Count > 0) sb.Append($",\n\t\tAttackRange = {IntArray(s.AttackRange)}");
        if (s.HasSpAmount || s.SpAmount.Count > 0) sb.Append($",\n\t\tSpAmount = {IntArray(s.SpAmount)}");
        if (s.BSeperateLv.HasValue) sb.Append($",\n\t\tbSeperateLv = {Bool(s.BSeperateLv.Value)}");
        if (s.NeedSkillList.Count > 0) sb.Append($",\n\t\t_NeedSkillList = {Prereqs(s.NeedSkillList, 2)}");
        if (s.Type is not null) sb.Append($",\n\t\tType = {Quote(s.Type)}");
        if (s.JobNeedSkillList.Count > 0) sb.Append($",\n\t\tNeedSkillList = {JobPrereqList(s.JobNeedSkillList)}");
        foreach (var (k, v) in s.ExtraInfoFields) // unmodeled info keys, re-emitted so an edit can't drop them (audit sweep)
            sb.Append($",\n\t\t{k} = {v}");
        sb.Append("\n\t},\n");
        return sb.ToString();
    }

    public static string FormatDescript(ClientSkill s)
    {
        var sb = new StringBuilder();
        sb.Append($"\t[SKID.{s.Constant}] = {{\n");
        for (int i = 0; i < s.Description.Count; i++)
        {
            sb.Append("\t\t").Append(Quote(s.Description[i]));
            sb.Append(i < s.Description.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("\t},\n");
        return sb.ToString();
    }

    // Default SKILL_DELAY_LIST key order for a newly-created entry (no recorded source order).
    private static readonly string[] DefaultDelayOrder =
        { "SkillFlag", "SkillCastFixedDelay", "SkillCastStatDelay", "SkillGlobalPostDelay", "SkillSinglePostDelay" };

    public static string FormatDelay(ClientSkill s)
    {
        // Build each present key's formatted part, then emit in the original on-disk order (audit #36) — a few
        // real entries (HW_MAGICPOWER) place SkillFlag last; falling back to the default order for new entries.
        var parts = new Dictionary<string, string>(System.StringComparer.Ordinal);
        if (s.SkillFlag.Count > 0) parts["SkillFlag"] = $"SkillFlag = {{ {string.Join(", ", s.SkillFlag)} }}";
        if (s.CastFixedDelay.Count > 0) parts["SkillCastFixedDelay"] = $"SkillCastFixedDelay = {IntArray(s.CastFixedDelay)}";
        if (s.CastStatDelay.Count > 0) parts["SkillCastStatDelay"] = $"SkillCastStatDelay = {IntArray(s.CastStatDelay)}";
        if (s.GlobalPostDelay.Count > 0) parts["SkillGlobalPostDelay"] = $"SkillGlobalPostDelay = {IntArray(s.GlobalPostDelay)}";
        if (s.SinglePostDelay.Count > 0) parts["SkillSinglePostDelay"] = $"SkillSinglePostDelay = {IntArray(s.SinglePostDelay)}";

        var ordered = new List<string>();
        foreach (var k in s.DelayKeyOrder)
            if (parts.ContainsKey(k) && !ordered.Contains(k)) ordered.Add(k);
        foreach (var k in DefaultDelayOrder)
            if (parts.ContainsKey(k) && !ordered.Contains(k)) ordered.Add(k);

        var sb = new StringBuilder();
        sb.Append($"\t[SKID.{s.Constant}] = {{\n");
        for (int i = 0; i < ordered.Count; i++)
        {
            sb.Append("\t\t").Append(parts[ordered[i]]);
            sb.Append(i < ordered.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("\t},\n");
        return sb.ToString();
    }

    private static string Quote(string s) => LuaString.Quote(s); // shared, symmetric with LuaTableParser (audit #2)

    private static string Bool(bool b) => b ? "true" : "false";

    private static string IntArray(List<int> values) => // empty -> "{}" (matches real files; was "{  }") (audit #13)
        values.Count == 0 ? "{}" : "{ " + string.Join(", ", values) + " }";

    private static string Prereqs(List<SkillPrereq> reqs, int indentTabs)
    {
        string inner = new string('\t', indentTabs + 1);
        string close = new string('\t', indentTabs);
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < reqs.Count; i++)
        {
            sb.Append(inner).Append(reqs[i].Level is int lvl
                ? $"{{ SKID.{reqs[i].Skid}, {lvl} }}"
                : $"{{ SKID.{reqs[i].Skid} }}"); // level-less form round-trips (audit #6)
            sb.Append(i < reqs.Count - 1 ? ",\n" : "\n");
        }
        sb.Append(close).Append('}');
        return sb.ToString();
    }

    private static string JobPrereqList(List<JobPrereqs> jobs)
    {
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < jobs.Count; i++)
        {
            sb.Append($"\t\t\t[JOBID.{jobs[i].Job}] = {Prereqs(jobs[i].Reqs, 3)}");
            sb.Append(i < jobs.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("\t\t}");
        return sb.ToString();
    }
}
