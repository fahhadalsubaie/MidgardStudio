using MidgardStudio.Core.Model;

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

    /// <summary>Optional conditional visibility (e.g. WeaponLevel only for Type == Weapon).</summary>
    public Func<DbRecord, bool>? IsApplicable { get; init; }

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
