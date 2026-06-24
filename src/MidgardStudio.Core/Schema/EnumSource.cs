namespace MidgardStudio.Core.Schema;

/// <summary>
/// Source of allowed values for an Enum / Flags / BoolMap / Reference field.
/// Static values are known up-front; Reference fields resolve their values at runtime from
/// another loaded database (e.g. item AegisNames). An optional <see cref="Labels"/> map provides
/// friendly display text per value — the UI shows the label, but the raw value is always what is
/// stored and serialized to YAML.
/// </summary>
public sealed class EnumSource
{
    public required string Name { get; init; }

    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();

    /// <summary>Optional friendly display text keyed by raw value (e.g. "Head_Low" =&gt; "Lower Headgear").</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>For Reference fields: the database id whose entries supply values (e.g. "item_db").</summary>
    public string? ReferenceDb { get; init; }

    public bool IsReference => ReferenceDb is not null;

    /// <summary>Friendly display text for a raw value (falls back to the raw value).</summary>
    public string Label(string value) =>
        Labels is not null && Labels.TryGetValue(value, out var label) ? label : value;

    public static EnumSource Static(string name, params string[] values) =>
        new() { Name = name, Values = values };

    /// <summary>
    /// Builds a value set with friendly labels. Each pair is (raw value, display label); the raw
    /// values keep their YAML order.
    /// </summary>
    public static EnumSource Labeled(string name, params (string Value, string Label)[] pairs) =>
        new()
        {
            Name = name,
            Values = pairs.Select(p => p.Value).ToArray(),
            Labels = pairs.ToDictionary(p => p.Value, p => p.Label, StringComparer.Ordinal),
        };

    public static EnumSource Reference(string name, string referenceDb) =>
        new() { Name = name, ReferenceDb = referenceDb };
}
