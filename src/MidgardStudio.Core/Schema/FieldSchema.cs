using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.Core.Schema;

/// <summary>
/// Declarative description of a single database field. The <c>Fields</c> order in a
/// <see cref="DbSchema"/> is also the serialization order.
/// </summary>
public sealed class FieldSchema
{
    /// <summary>Exact YAML key (case-sensitive), e.g. "AegisName", "WeaponLevel".</summary>
    public required string Name { get; init; }

    /// <summary>Human-friendly label for the UI.</summary>
    public required string Label { get; init; }

    public required FieldKind Kind { get; init; }

    /// <summary>UI section/tab this field belongs to (e.g. "General", "Equip", "Flags", "Scripts").</summary>
    public string? Group { get; init; }

    public string? Description { get; init; }

    /// <summary>Value that, when equal, is omitted on write (rAthena omits defaults).</summary>
    public object? Default { get; init; }

    public bool IsKey { get; init; }

    public bool IsDisplay { get; init; }

    public bool IsSearchable { get; init; } = true;

    /// <summary>For Enum/Flags/BoolMap/Reference.</summary>
    public EnumSource? Enum { get; init; }

    /// <summary>For Object/ObjectList: the nested schema.</summary>
    public DbSchema? ObjectSchema { get; init; }

    /// <summary>For ScalarList: the element kind.</summary>
    public FieldKind? ElementKind { get; init; }

    /// <summary>For LevelInt: the inner per-level value key in YAML (e.g. "Size", "Time", "Amount", "Count").</summary>
    public string LevelValueKey { get; init; } = "Value";

    public RenewalScope Renewal { get; init; } = RenewalScope.Both;

    /// <summary>When &gt; 1, the UI shows the value as a percentage (e.g. 10000 =&gt; 100.00%).</summary>
    public int RateScale { get; init; } = 1;

    /// <summary>Optional inclusive bounds for Int fields. The editor clamps user input into this range
    /// so an out-of-range value (e.g. a summon Rate above 1,000,000) can't be entered.</summary>
    public int? Min { get; init; }
    public int? Max { get; init; }

    /// <summary>Clamps a value into this field's [<see cref="Min"/>, <see cref="Max"/>] bounds (no-op when unbounded).</summary>
    public int Clamp(int value)
    {
        if (Min is { } min && value < min) return min;
        if (Max is { } max && value > max) return max;
        return value;
    }

    // ---- validation metadata (all default-off; consumed by the validation engine) ----

    /// <summary>When set, an empty value yields a <see cref="RequiredSeverity"/> issue.</summary>
    public bool IsRequired { get; init; }

    public ValidationSeverity RequiredSeverity { get; init; } = ValidationSeverity.Error;

    /// <summary>Maximum character length for String/Enum/Reference fields (e.g. AegisName ≤ 49).</summary>
    public int? MaxLength { get; init; }

    public ValidationSeverity MaxLengthSeverity { get; init; } = ValidationSeverity.Warning;

    /// <summary>When true, the value must be unique across the database (case-insensitive). Used for
    /// AegisName, which is unique but is not the record key.</summary>
    public bool Unique { get; init; }

    /// <summary>Severity for an unresolved cross-database reference. Defaults to Warning (rAthena
    /// silently skips, e.g. a missing drop item); set to Error where the record is rejected (alias, etc.).</summary>
    public ValidationSeverity ReferenceSeverity { get; init; } = ValidationSeverity.Warning;

    /// <summary>Optional conditional visibility (e.g. WeaponLevel only for Type == Weapon).</summary>
    public Func<DbRecord, bool>? IsApplicable { get; init; }

    /// <summary>Name of another field whose value, when it changes, resets THIS field's value (e.g. SubType
    /// resets when Type changes — a weapon subtype is meaningless once the item becomes a card). Honored only
    /// by editors opted into auto-clean (the Item Forge); the main editor preserves + warns instead.</summary>
    public string? ResetOnChangeOf { get; init; }

    /// <summary>Optional record-dependent value set for an Enum field — overrides <see cref="Enum"/> in the
    /// editor's dropdown so the options narrow with another field (e.g. SubType shows only the Weapon/Ammo/Card
    /// set matching Type). The form rebuilds on the trigger's applicability change, so the options refresh.</summary>
    public Func<DbRecord, EnumSource?>? EnumSelector { get; init; }

    /// <summary>Conditional visibility that needs cross-database knowledge (e.g. mob_avail PetEquip only when
    /// the Mob is a pet). Evaluated with the reference index when available; takes precedence over
    /// <see cref="IsApplicable"/>. Defaults to applicable when no index is supplied.</summary>
    public Func<DbRecord, IReferenceIndex, bool>? IsApplicableRefs { get; init; }

    /// <summary>When true, the generic detail form skips this field — it is presented by a dedicated
    /// card instead (e.g. mob Drops/MvpDrops shown as drop tables). Still serialized normally.</summary>
    public bool HideInForm { get; init; }

    // ---- convenience factories ----

    public static FieldSchema Int(string name, string label, int @default = 0, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.Int, Default = @default, Group = group };

    /// <summary>A dual-typed numeric field (single value OR a per-level array). <paramref name="valueKey"/>
    /// is the inner YAML key for per-level entries (e.g. "Size", "Time", "Amount").</summary>
    public static FieldSchema Level(string name, string label, string valueKey, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.LevelInt, LevelValueKey = valueKey, Group = group, IsSearchable = false };

    public static FieldSchema Str(string name, string label, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.String, Group = group };

    public static FieldSchema Bool(string name, string label, bool @default = false, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.Bool, Default = @default, Group = group };

    public static FieldSchema EnumField(string name, string label, EnumSource source, object? @default = null, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.Enum, Enum = source, Default = @default, Group = group };

    public static FieldSchema ScriptField(string name, string label, string? group = "Scripts") =>
        new() { Name = name, Label = label, Kind = FieldKind.Script, IsSearchable = false, Group = group };

    public static FieldSchema ObjectField(string name, string label, DbSchema schema, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.Object, ObjectSchema = schema, Group = group };

    public static FieldSchema ObjectListField(string name, string label, DbSchema element, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.ObjectList, ObjectSchema = element, Group = group, IsSearchable = false };

    public static FieldSchema BoolMapField(string name, string label, EnumSource source, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.BoolMap, Enum = source, Group = group };

    public static FieldSchema FlagsField(string name, string label, EnumSource source, string? group = null) =>
        new() { Name = name, Label = label, Kind = FieldKind.Flags, Enum = source, Group = group };
}
