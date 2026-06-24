using MidgardStudio.Core.Model;

namespace MidgardStudio.Core.Overlay;

/// <summary>One physical layer of records (a base bundle, or the editable import layer),
/// indexed by key with insertion order preserved for stable serialization.</summary>
public sealed class DbLayer
{
    public Dictionary<RecordKey, DbRecord> ByKey { get; } = new();

    public List<DbRecord> Records { get; } = new();

    public void Add(DbRecord record)
    {
        var key = record.Key;
        if (ByKey.TryGetValue(key, out var existing))
        {
            int idx = Records.IndexOf(existing);
            if (idx >= 0) Records[idx] = record; else Records.Add(record);
            ByKey[key] = record; // last-wins within a layer
        }
        else
        {
            ByKey[key] = record;
            Records.Add(record);
        }
    }

    public bool Remove(RecordKey key)
    {
        if (!ByKey.TryGetValue(key, out var rec)) return false;
        ByKey.Remove(key);
        Records.Remove(rec);
        return true;
    }

    public bool Contains(RecordKey key) => ByKey.ContainsKey(key);

    public DbRecord? Get(RecordKey key) => ByKey.GetValueOrDefault(key);
}
