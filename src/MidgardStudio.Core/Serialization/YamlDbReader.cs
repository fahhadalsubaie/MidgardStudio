using System.Globalization;
using System.Text;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MidgardStudio.Core.Serialization;

/// <summary>
/// Reads a rAthena YAML database file into <see cref="DbFile"/> using YamlDotNet's streaming event
/// parser (not the DOM, which rejects the duplicate keys present in some official files). Each Body
/// entry is coerced into a schema-described <see cref="DbRecord"/>; unknown keys go to
/// <see cref="DbRecord.Extras"/>. Duplicate keys resolve last-wins.
/// </summary>
public sealed class YamlDbReader
{
    public DbFile ReadFile(string path, DbSchema schema, RecordOrigin origin = RecordOrigin.Base)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader, schema, origin);
    }

    public DbFile Read(string yaml, DbSchema schema, RecordOrigin origin = RecordOrigin.Base)
    {
        using var reader = new StringReader(yaml);
        return Read(reader, schema, origin);
    }

    public DbFile Read(TextReader reader, DbSchema schema, RecordOrigin origin = RecordOrigin.Base)
    {
        var file = new DbFile { HeaderType = schema.HeaderType, HeaderVersion = schema.HeaderVersion };

        var parser = new Parser(reader);
        parser.Consume<StreamStart>();
        if (!parser.TryConsume<DocumentStart>(out _))
            return file;

        if (ReadGeneric(parser) is not Dictionary<string, object?> root)
            return file;

        if (root.TryGetValue("Header", out var h) && h is Dictionary<string, object?> header)
        {
            if (header.TryGetValue("Type", out var t) && t is string ts && !string.IsNullOrEmpty(ts))
                file.HeaderType = ts;
            if (header.TryGetValue("Version", out var v) && int.TryParse(v as string, out var ver))
                file.HeaderVersion = ver;
        }

        if (root.TryGetValue("Body", out var b) && b is List<object?> body)
        {
            foreach (var entry in body)
            {
                if (entry is Dictionary<string, object?> dict)
                {
                    var record = CoerceRecord(dict, schema, origin);
                    record.AttachNestedOwners();
                    file.Records.Add(record);
                }
            }
        }

        return file;
    }

    /// <summary>Materializes the next YAML node as a generic tree: string | Dictionary | List.</summary>
    private static object? ReadGeneric(IParser parser)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
            return scalar.Value;

        if (parser.TryConsume<MappingStart>(out _))
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value ?? string.Empty;
                map[key] = ReadGeneric(parser); // last-wins on duplicate keys
            }
            return map;
        }

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var list = new List<object?>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                list.Add(ReadGeneric(parser));
            return list;
        }

        // Anchors/aliases/other — skip a single event to keep progressing.
        parser.MoveNext();
        return null;
    }

    private static DbRecord CoerceRecord(Dictionary<string, object?> dict, DbSchema schema, RecordOrigin origin)
    {
        var record = new DbRecord(schema) { Origin = origin };
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            if (dict.TryGetValue(field.Name, out var raw) && raw is not null && TryCoerce(raw, field, out var value))
            {
                record.SetRaw(field.Name, value);
                consumed.Add(field.Name);
            }
            // A value whose YAML shape doesn't match the field kind (e.g. a per-level array on a scalar
            // field) is left unconsumed and preserved verbatim in Extras below, so it round-trips.
        }

        foreach (var (key, value) in dict)
        {
            if (!consumed.Contains(key))
                record.Extras[key] = value;
        }

        record.IsDirty = false;
        return record;
    }

    /// <summary>Coerces a raw YAML value into the typed model value, returning false when the value's
    /// shape doesn't match the field kind (so the caller preserves it verbatim instead of losing it).</summary>
    private static bool TryCoerce(object raw, FieldSchema field, out object? value)
    {
        value = null;
        switch (field.Kind)
        {
            case FieldKind.Int:
                if (raw is not string) return false;
                value = ParseInt((string)raw); return true;
            case FieldKind.Long:
                if (raw is not string) return false;
                value = long.TryParse((string)raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L; return true;
            case FieldKind.Bool:
                if (raw is not string) return false;
                value = ParseBool((string)raw); return true;
            case FieldKind.String:
            case FieldKind.Enum:
            case FieldKind.Reference:
                if (raw is not string) return false;
                value = (string)raw; return true;
            case FieldKind.Script:
                if (raw is not string) return false;
                value = new ScriptValue((string)raw); return true;
            case FieldKind.Flags:
            case FieldKind.BoolMap:
                if (raw is not Dictionary<string, object?>) return false;
                value = ReadBoolSet(raw); return true;
            case FieldKind.Object:
                if (raw is not Dictionary<string, object?> m || field.ObjectSchema is null) return false;
                value = CoerceRecord(m, field.ObjectSchema, RecordOrigin.Base); return true;
            case FieldKind.ObjectList:
                if (raw is not List<object?> || field.ObjectSchema is null) return false;
                value = ReadObjectList(raw, field); return true;
            case FieldKind.ScalarList:
                if (raw is not List<object?> sl) return false;
                value = new List<object?>(sl); return true;
            case FieldKind.LevelInt:
                if (raw is not string && raw is not List<object?>) return false;
                value = ReadLevel(raw, field); return true;
            default:
                if (raw is not string) return false;
                value = (string)raw; return true;
        }
    }

    private static LevelList ReadLevel(object raw, FieldSchema field)
    {
        var list = new LevelList();
        if (raw is string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) list.Scalar = v;
        }
        else if (raw is List<object?> seq)
        {
            foreach (var item in seq)
            {
                if (item is Dictionary<string, object?> m
                    && m.TryGetValue("Level", out var lo) && int.TryParse(lo as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl)
                    && m.TryGetValue(field.LevelValueKey, out var vo) && int.TryParse(vo as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                    list.Levels.Add(new LevelEntry(lvl, val));
            }
        }
        return list;
    }

    private static HashSet<string> ReadBoolSet(object raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (raw is Dictionary<string, object?> map)
        {
            foreach (var (key, value) in map)
            {
                if (ParseBool(value as string))
                    set.Add(key);
            }
        }
        return set;
    }

    private static List<DbRecord> ReadObjectList(object raw, FieldSchema field)
    {
        var list = new List<DbRecord>();
        if (raw is List<object?> seq && field.ObjectSchema is not null)
        {
            foreach (var item in seq)
            {
                if (item is Dictionary<string, object?> m)
                    list.Add(CoerceRecord(m, field.ObjectSchema, RecordOrigin.Base));
            }
        }
        return list;
    }

    private static int ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static bool ParseBool(string? s) =>
        s is not null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");
}
