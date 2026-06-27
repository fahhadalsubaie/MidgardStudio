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

    public static string FormatDelay(ClientSkill s)
    {
        var parts = new List<string>();
        if (s.SkillFlag.Count > 0) parts.Add($"SkillFlag = {{ {string.Join(", ", s.SkillFlag)} }}");
        if (s.CastFixedDelay.Count > 0) parts.Add($"SkillCastFixedDelay = {IntArray(s.CastFixedDelay)}");
        if (s.CastStatDelay.Count > 0) parts.Add($"SkillCastStatDelay = {IntArray(s.CastStatDelay)}");
        if (s.GlobalPostDelay.Count > 0) parts.Add($"SkillGlobalPostDelay = {IntArray(s.GlobalPostDelay)}");
        if (s.SinglePostDelay.Count > 0) parts.Add($"SkillSinglePostDelay = {IntArray(s.SinglePostDelay)}");

        var sb = new StringBuilder();
        sb.Append($"\t[SKID.{s.Constant}] = {{\n");
        for (int i = 0; i < parts.Count; i++)
        {
            sb.Append("\t\t").Append(parts[i]);
            sb.Append(i < parts.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("\t},\n");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Bool(bool b) => b ? "true" : "false";

    private static string IntArray(List<int> values) => "{ " + string.Join(", ", values) + " }";

    private static string Prereqs(List<SkillPrereq> reqs, int indentTabs)
    {
        string inner = new string('\t', indentTabs + 1);
        string close = new string('\t', indentTabs);
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < reqs.Count; i++)
        {
            sb.Append(inner).Append($"{{ SKID.{reqs[i].Skid}, {reqs[i].Level} }}");
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
