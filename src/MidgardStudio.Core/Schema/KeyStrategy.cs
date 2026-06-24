using MidgardStudio.Core.Model;

namespace MidgardStudio.Core.Schema;

/// <summary>
/// How a record's identity key is derived. item_db/mob_db/skill_db/achievement_db use an int Id;
/// pet_db (Mob), item_group_db/mob_summon (Group), abra_db (Skill) use a string; item_combos is keyless.
/// </summary>
public abstract class KeyStrategy
{
    public abstract RecordKey Extract(DbRecord record);

    public static KeyStrategy Int(string field) => new IntKeyStrategy(field);

    public static KeyStrategy Str(string field) => new StringKeyStrategy(field);

    public static KeyStrategy Singleton(string label = "Singleton") => new SingletonKeyStrategy(label);

    /// <summary>Derives a string key from the record (for keyless DBs like item_combos).</summary>
    public static KeyStrategy Computed(Func<DbRecord, string> compute) => new ComputedKeyStrategy(compute);
}

public sealed class ComputedKeyStrategy : KeyStrategy
{
    private readonly Func<DbRecord, string> _compute;

    public ComputedKeyStrategy(Func<DbRecord, string> compute) => _compute = compute;

    public override RecordKey Extract(DbRecord record) => RecordKey.Of(_compute(record));
}

public sealed class IntKeyStrategy : KeyStrategy
{
    public IntKeyStrategy(string field) => Field = field;

    public string Field { get; }

    public override RecordKey Extract(DbRecord record) => RecordKey.Of((long)record.GetInt(Field));
}

public sealed class StringKeyStrategy : KeyStrategy
{
    public StringKeyStrategy(string field) => Field = field;

    public string Field { get; }

    public override RecordKey Extract(DbRecord record) => RecordKey.Of(record.GetString(Field) ?? string.Empty);
}

public sealed class SingletonKeyStrategy : KeyStrategy
{
    private readonly string _label;

    public SingletonKeyStrategy(string label) => _label = label;

    public override RecordKey Extract(DbRecord record) => RecordKey.Of(_label);
}
