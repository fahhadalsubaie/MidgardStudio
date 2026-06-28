using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MidgardStudio.Core.Validation;

/// <summary>
/// Pure save-gate logic, lifted out of the shell god-module: which databases to validate before a save,
/// and how to summarise the blocking errors. The App keeps only the dialog interaction around these.
/// </summary>
public static class SaveGate
{
    /// <summary>The db ids to validate before saving: the dirty save targets, plus <c>item_db</c> when the
    /// client items changed, <c>skill_db</c> when the client skills changed, and <c>item_cash</c> when the cash
    /// shop changed (those pull in the cross-file / bespoke checks for the files about to be written).</summary>
    public static List<string> TargetsToValidate(IEnumerable<string> saveTargetIds, bool clientItemsDirty,
        bool clientSkillsDirty, bool cashShopDirty = false)
    {
        var ids = saveTargetIds.ToList();
        if (clientItemsDirty && !ids.Contains("item_db")) ids.Add("item_db");
        if (clientSkillsDirty && !ids.Contains("skill_db")) ids.Add("skill_db");
        if (cashShopDirty && !ids.Contains("item_cash")) ids.Add("item_cash");
        return ids;
    }

    /// <summary>A human summary of the error-level findings in a save-gate report (first <paramref name="max"/> listed).</summary>
    public static string FormatErrors(ValidationReport report, int max = 10)
    {
        var errors = report.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"{errors.Count} error(s) were found in the entries you're about to save:");
        sb.AppendLine();
        foreach (var e in errors.Take(max))
            sb.AppendLine($"•  [{e.DbId} #{e.Key}]  {e.Message}");
        if (errors.Count > max) sb.AppendLine($"…and {errors.Count - max} more.");
        return sb.ToString();
    }
}
