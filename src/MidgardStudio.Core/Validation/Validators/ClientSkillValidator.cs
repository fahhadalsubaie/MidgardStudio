using MidgardStudio.Core.Lua;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// Internal-consistency rules for the client skill files (no server data needed). Findings use the
/// <c>client_skills</c> db id and the SKID constant as the key. Pure-data fixes mutate the in-memory
/// <see cref="ClientSkill"/>; the App layer re-validates and marks the workspace dirty afterwards.
/// Cross-checks against the server <c>skill_db</c> live in the App's WorkspaceValidator (which has it loaded).
/// </summary>
public static class ClientSkillValidator
{
    public const string DbId = "client_skills";

    public static IReadOnlyList<ValidationIssue> Validate(ClientSkillTables tables)
    {
        var issues = new List<ValidationIssue>();

        foreach (var skill in tables.Skills.Values.OrderBy(s => s.Id == 0 ? int.MaxValue : s.Id).ThenBy(s => s.Constant, StringComparer.Ordinal))
        {
            string key = skill.Constant;

            if (!tables.Skid.ContainsKey(skill.Constant))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, DbId, key, "SKID",
                    $"'{skill.Constant}' is used in the skill tables but is not defined in skillid.lub (SKID) — the client can't resolve it.")
                { RuleId = "CSKILL.SKID_MISSING" });

            if (skill is { HasInfo: false } && (skill.HasDescript || skill.HasDelay))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, DbId, key, "SkillName",
                    $"'{skill.Constant}' has a description/delay entry but no SKILL_INFO_LIST entry — it won't appear properly in-client.")
                { RuleId = "CSKILL.INFO_MISSING" });

            if (skill.HasInfo)
            {
                if (!string.IsNullOrEmpty(skill.Aegis) && !string.Equals(skill.Aegis, skill.Constant, StringComparison.Ordinal))
                {
                    string oldAegis = skill.Aegis;
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, DbId, key, "Aegis",
                        $"SKILL_INFO_LIST name '{skill.Aegis}' doesn't match the key SKID.{skill.Constant}.")
                    { RuleId = "CSKILL.NAME_MISMATCH", Fix = new QuickFix($"Set name to '{skill.Constant}'", () => skill.Aegis = skill.Constant, () => skill.Aegis = oldAegis) });
                }

                // Only flag an explicit, invalid MaxLv. A skill with NO MaxLv field is valid (the official
                // client ships ~18% of info entries that way); flagging those would be a false positive AND
                // would steer the user into an edit that writes a spurious `MaxLv = 0` line.
                if (skill.HasMaxLv && skill.MaxLv <= 0)
                {
                    int oldMax = skill.MaxLv;
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, DbId, key, "MaxLv",
                        $"MaxLv is {skill.MaxLv} — it must be at least 1.")
                    { RuleId = "CSKILL.MAXLV_INVALID", Fix = new QuickFix("Set MaxLv to 1", () => skill.MaxLv = 1, () => skill.MaxLv = oldMax) });
                }

                CheckArray(issues, key, "SpAmount", skill.SpAmount, skill.MaxLv);
                CheckArray(issues, key, "AttackRange", skill.AttackRange, skill.MaxLv);

                if (!skill.HasDescript || skill.Description.Count == 0)
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, DbId, key, "Description",
                        $"'{skill.Constant}' has no SKILL_DESCRIPT entry — it shows no description in-client.")
                    { RuleId = "CSKILL.DESC_EMPTY" });

                foreach (var p in skill.NeedSkillList)
                    if (!tables.Skid.ContainsKey(p.Skid))
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, DbId, key, "_NeedSkillList",
                            $"Prerequisite SKID.{p.Skid} is not defined in skillid.lub.")
                        { RuleId = "CSKILL.NEEDSKILL_UNKNOWN" });

                foreach (var job in skill.JobNeedSkillList)
                    foreach (var p in job.Reqs)
                        if (!tables.Skid.ContainsKey(p.Skid))
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, DbId, key, "NeedSkillList",
                                $"Job prerequisite SKID.{p.Skid} (for {job.Job}) is not defined in skillid.lub.")
                            { RuleId = "CSKILL.NEEDSKILL_UNKNOWN" });
            }
        }

        return issues;
    }

    /// <summary>A per-level array shorter than MaxLv (and not the length-1 "applies to all levels" shorthand)
    /// silently reads garbage past its end in-client.</summary>
    private static void CheckArray(List<ValidationIssue> issues, string key, string field, List<int> values, int maxLv)
    {
        if (values.Count <= 1 || maxLv <= 0 || values.Count >= maxLv) return;
        var snapshot = values.ToList();
        issues.Add(new ValidationIssue(ValidationSeverity.Warning, DbId, key, field,
            $"{field} has {values.Count} entries but MaxLv is {maxLv} — higher levels read undefined values.")
        {
            RuleId = "CSKILL.ARRAY_TOO_SHORT",
            Fix = new QuickFix($"Pad {field} to {maxLv} levels",
                () =>
                {
                    int last = values.Count > 0 ? values[^1] : 0;
                    while (values.Count < maxLv) values.Add(last);
                },
                () => { values.Clear(); values.AddRange(snapshot); }),
        });
    }
}
