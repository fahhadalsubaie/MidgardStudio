using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Core.Overlay;

public sealed record PortNote(string Field, string Message);

public sealed record PortReport(bool Ported, IReadOnlyList<PortNote> Notes);

/// <summary>
/// Copies the effective Renewal record into the shared import layer so Pre-Renewal mode serves it too,
/// and reports renewal-only fields (MagicAttack, Gradable, ...) the pre-renewal server will ignore.
/// </summary>
public static class RenewalPortService
{
    public static PortReport PortToPreRenewal(ModeSet set, RecordKey key)
    {
        var source = set.Renewal.GetEffective(key);
        if (source is null)
            return new PortReport(false, new[] { new PortNote(string.Empty, "No Renewal record to port.") });

        var notes = new List<PortNote>();
        foreach (var field in set.Renewal.Schema.Fields)
        {
            if (field.Renewal == RenewalScope.RenewalOnly
                && source.Has(field.Name)
                && !YamlDbWriter.IsOmitted(field, source.Get(field.Name)))
            {
                notes.Add(new PortNote(field.Name,
                    $"{field.Label} is Renewal-only; the Pre-Renewal server ignores it (value kept)."));
            }
        }

        if (!set.SharedImport.Contains(key))
        {
            var clone = source.DeepClone();
            clone.Origin = RecordOrigin.NewCustom;
            clone.IsDirty = true;
            set.SharedImport.Add(clone);
        }

        return new PortReport(true, notes);
    }
}
