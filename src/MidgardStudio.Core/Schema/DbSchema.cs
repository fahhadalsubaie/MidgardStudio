namespace MidgardStudio.Core.Schema;

/// <summary>
/// Declarative description of a database (its identity, header, key strategy, and ordered fields).
/// The reader, writer, validator, and generated UI form are all driven from this single definition.
/// </summary>
public sealed class DbSchema
{
    /// <summary>Stable id, e.g. "item_db".</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>YAML Header.Type, e.g. "ITEM_DB". Empty for nested sub-schemas.</summary>
    public string HeaderType { get; init; } = string.Empty;

    /// <summary>YAML Header.Version.</summary>
    public int HeaderVersion { get; init; }

    public KeyStrategy Key { get; init; } = KeyStrategy.Singleton();

    /// <summary>On-disk file layout (re/pre-re base + import override). Empty for nested sub-schemas.</summary>
    public FileLayout Layout { get; init; } = new();

    public required IReadOnlyList<FieldSchema> Fields { get; init; }

    /// <summary>True for Object/ObjectList sub-schemas (no Header/Footer, not a top-level db).</summary>
    public bool IsNested { get; init; }

    public FieldSchema? KeyField => Fields.FirstOrDefault(f => f.IsKey);

    public FieldSchema? DisplayField => Fields.FirstOrDefault(f => f.IsDisplay);

    public FieldSchema? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    public static DbSchema Nested(string id, IReadOnlyList<FieldSchema> fields) =>
        new() { Id = id, DisplayName = id, Fields = fields, IsNested = true };
}
