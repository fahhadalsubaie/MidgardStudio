using MidgardStudio.Core.Validation;

namespace MidgardStudio.Tests;

/// <summary>The pure save-gate logic (target selection + error summary), previously trapped in the shell.</summary>
public class SaveGateTests
{
    [Fact]
    public void Targets_pass_through_when_no_client_files_are_dirty()
    {
        Assert.Equal(new[] { "mob_db" }, SaveGate.TargetsToValidate(new[] { "mob_db" }, false, false));
    }

    [Fact]
    public void Dirty_client_items_pull_in_item_db()
    {
        Assert.Contains("item_db", SaveGate.TargetsToValidate(new[] { "mob_db" }, clientItemsDirty: true, clientSkillsDirty: false));
    }

    [Fact]
    public void Dirty_client_skills_pull_in_skill_db()
    {
        Assert.Contains("skill_db", SaveGate.TargetsToValidate(new[] { "mob_db" }, clientItemsDirty: false, clientSkillsDirty: true));
    }

    [Fact]
    public void Dirty_cash_shop_pulls_in_item_cash()
    {
        Assert.Contains("item_cash", SaveGate.TargetsToValidate(new[] { "mob_db" }, false, false, cashShopDirty: true));
    }

    [Fact]
    public void Item_db_is_not_duplicated_when_already_a_target()
    {
        var ids = SaveGate.TargetsToValidate(new[] { "item_db" }, clientItemsDirty: true, clientSkillsDirty: false);
        Assert.Single(ids, id => id == "item_db");
    }

    [Fact]
    public void FormatErrors_counts_errors_ignores_warnings_and_summarizes_overflow()
    {
        var issues = new List<ValidationIssue>();
        for (int i = 0; i < 12; i++)
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "item_db", i.ToString(), "f", "bad " + i));
        issues.Add(new ValidationIssue(ValidationSeverity.Warning, "item_db", "99", "f", "just a warning"));

        string msg = SaveGate.FormatErrors(new ValidationReport(issues));

        Assert.Contains("12 error(s)", msg);
        Assert.Contains("…and 2 more.", msg);   // 12 errors, 10 listed
        Assert.DoesNotContain("just a warning", msg);
    }
}
