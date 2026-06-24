using CommunityToolkit.Mvvm.ComponentModel;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Model;

/// <summary>Effective provenance of a record within the base/import overlay.</summary>
public enum RecordOrigin
{
    Base,        // from the read-only re/pre-re layer only
    Overridden,  // base id also present (and edited) in the import layer
    NewCustom,   // exists only in the import layer
}

/// <summary>
/// A schema-described database entry, held generically as values keyed by field name.
/// This single type backs every database, so adding a database is "write a schema", not "write a model".
/// Values by <see cref="FieldKind"/>:
///   Int/Long -&gt; int/long, Bool -&gt; bool, String/Enum/Reference -&gt; string,
///   Flags/BoolMap -&gt; ISet&lt;string&gt; (true keys), Object -&gt; DbRecord,
///   ObjectList -&gt; IList&lt;DbRecord&gt;, Script -&gt; ScriptValue, ScalarList -&gt; IList&lt;object&gt;.
/// </summary>
public sealed class DbRecord : ObservableObject
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public DbRecord(DbSchema schema) => Schema = schema;

    public DbSchema Schema { get; }

    /// <summary>Unknown keys preserved verbatim so a future rAthena field round-trips untouched.</summary>
    public Dictionary<string, object?> Extras { get; } = new(StringComparer.Ordinal);

    public RecordOrigin Origin { get; set; } = RecordOrigin.Base;

    /// <summary>The record that owns this one as a nested Object/ObjectList value (null for top-level).</summary>
    public DbRecord? Owner { get; set; }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value) && value && Owner is not null)
                Owner.IsDirty = true; // bubble dirtiness up to the import record that gets saved
        }
    }

    public RecordKey Key => Schema.Key.Extract(this);

    public IReadOnlyDictionary<string, object?> Values => _values;

    public bool Has(string field) => _values.ContainsKey(field);

    public object? Get(string field) => _values.GetValueOrDefault(field);

    public int GetInt(string field) =>
        _values.TryGetValue(field, out var v) && v is not null ? Convert.ToInt32(v) : 0;

    public long GetLong(string field) =>
        _values.TryGetValue(field, out var v) && v is not null ? Convert.ToInt64(v) : 0L;

    public bool GetBool(string field) =>
        _values.TryGetValue(field, out var v) && v is bool b && b;

    public string? GetString(string field) =>
        _values.TryGetValue(field, out var v) ? v as string : null;

    public ScriptValue? GetScript(string field) => _values.GetValueOrDefault(field) as ScriptValue;

    public ISet<string>? GetSet(string field) => _values.GetValueOrDefault(field) as ISet<string>;

    public DbRecord? GetObject(string field) => _values.GetValueOrDefault(field) as DbRecord;

    public IList<DbRecord>? GetList(string field) => _values.GetValueOrDefault(field) as IList<DbRecord>;

    public LevelList? GetLevel(string field) => _values.GetValueOrDefault(field) as LevelList;

    /// <summary>Sets a value without marking dirty or raising change notifications (used by readers).</summary>
    public void SetRaw(string field, object? value) => _values[field] = value;

    /// <summary>Sets a value, marks the record dirty, and notifies bindings.</summary>
    public void Set(string field, object? value)
    {
        _values[field] = value;
        Extras.Remove(field); // a schema field edited by the user wins over any preserved passthrough value
        IsDirty = true;
        OnPropertyChanged(field);
        OnPropertyChanged(string.Empty);
    }

    public void Remove(string field)
    {
        if (_values.Remove(field))
        {
            IsDirty = true;
            OnPropertyChanged(field);
            OnPropertyChanged(string.Empty);
        }
    }

    public DbRecord DeepClone()
    {
        var clone = new DbRecord(Schema) { Origin = Origin };
        foreach (var (k, v) in _values)
            clone._values[k] = CloneValue(v);
        foreach (var (k, v) in Extras)
            clone.Extras[k] = v;
        clone.AttachNestedOwners();
        return clone;
    }

    /// <summary>Sets <see cref="Owner"/> on all directly-nested records so their edits bubble dirtiness here.
    /// Call after populating a record's nested Object/ObjectList values.</summary>
    public void AttachNestedOwners()
    {
        foreach (var value in _values.Values)
        {
            switch (value)
            {
                case DbRecord nested:
                    nested.Owner = this;
                    nested.AttachNestedOwners();
                    break;
                case IList<DbRecord> list:
                    foreach (var item in list)
                    {
                        item.Owner = this;
                        item.AttachNestedOwners();
                    }
                    break;
            }
        }
    }

    private static object? CloneValue(object? value) => value switch
    {
        DbRecord r => r.DeepClone(),
        IList<DbRecord> list => list.Select(x => x.DeepClone()).ToList(),
        ISet<string> set => new HashSet<string>(set, StringComparer.Ordinal),
        LevelList lvl => lvl.Clone(),
        IList<object> scalars => scalars.ToList(),
        _ => value, // value types, string, ScriptValue (immutable)
    };
}
