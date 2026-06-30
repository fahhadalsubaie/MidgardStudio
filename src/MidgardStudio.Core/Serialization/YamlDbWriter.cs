using System.Collections;
using System.Globalization;
using System.Text;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MidgardStudio.Core.Serialization;

/// <summary>
/// Writes a <see cref="DbFile"/> as a rAthena-style YAML document (Header + Body, no Footer —
/// import files have none). Fields are emitted in schema order, defaults/empties are omitted,
/// scripts use literal block style, and bool-maps emit only their true keys. Output is idempotent:
/// reading it back and rewriting yields identical text.
/// </summary>
public sealed class YamlDbWriter
{
    private static readonly EmitterSettings Settings =
        EmitterSettings.Default.WithBestIndent(2).WithBestWidth(int.MaxValue).WithIndentedSequences()
            .WithNewLine("\n"); // LF to match rAthena's base files (avoid CRLF diff churn)

    public string WriteToString(DbSchema schema, DbFile file)
    {
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
            Write(writer, schema, file);
        return sb.ToString();
    }

    /// <summary>Serializes a set of records as a complete import document (Header + Body) — used by the
    /// list "Copy YAML" action so the clipboard text can be pasted straight into an import file.</summary>
    public string WriteToString(DbSchema schema, IEnumerable<DbRecord> records)
    {
        var file = new DbFile { HeaderType = schema.HeaderType, HeaderVersion = schema.HeaderVersion };
        file.Records.AddRange(records);
        return WriteToString(schema, file);
    }

    /// <summary>Serializes a single record as a bare YAML mapping (no Header/Body wrapper), using the
    /// record's own schema. Used to copy a nested sub-record (e.g. an item-group entry) to the clipboard.</summary>
    public string WriteRecord(DbRecord record)
    {
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            var emitter = new Emitter(writer, Settings);
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart());
            EmitRecord(emitter, record, record.Schema);
            emitter.Emit(new DocumentEnd(isImplicit: true));
            emitter.Emit(new StreamEnd());
        }
        return sb.ToString();
    }

    public void WriteFile(string path, DbSchema schema, DbFile file)
    {
        var canonical = WriteToString(schema, file);
        // Preserve a hand-documented import file's comments (banner, field docs, commented-out examples) by
        // merging the regenerated Body into the existing text rather than overwriting it wholesale. No-op for
        // app-generated files (no banner, real Body) and for a first write (no existing file). See YamlBodyMerge.
        string? existing = null;
        if (File.Exists(path))
        {
            // The file EXISTS, so it has content (banner, comments, a Footer chaining sub-DBs) that a clean
            // regenerate would drop. If we can't read it, abort the save rather than silently overwrite it
            // with a comment-/Footer-less rewrite (audit #9). When File.Exists is false there's nothing to lose.
            try { existing = File.ReadAllText(path, new UTF8Encoding(false)); }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Couldn't read the existing '{Path.GetFileName(path)}' to merge your change safely, so nothing " +
                    "was written and the file is untouched. It may be open/locked in another program — close it and save again.", ex);
            }
        }
        var text = YamlBodyMerge.Merge(existing, canonical);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text); // rAthena YAML is UTF-8, no BOM
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        // Back up the existing import file before overwriting (upgrade-safe, recoverable).
        if (File.Exists(path))
        {
            var backupDir = Path.Combine(dir, ".midgard-backup");
            Directory.CreateDirectory(backupDir);
            File.Copy(path, Path.Combine(backupDir, Path.GetFileName(path) + ".bak"), overwrite: true);
        }

        // Crash-safe: write to a temp file then atomically replace.
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    public void Write(TextWriter writer, DbSchema schema, DbFile file)
    {
        var emitter = new Emitter(writer, Settings);
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        emitter.Emit(new MappingStart());

        // Header
        Key(emitter, "Header");
        emitter.Emit(new MappingStart());
        Key(emitter, "Type");
        Scalar(emitter, string.IsNullOrEmpty(file.HeaderType) ? schema.HeaderType : file.HeaderType);
        Key(emitter, "Version");
        Scalar(emitter, (file.HeaderVersion == 0 ? schema.HeaderVersion : file.HeaderVersion)
            .ToString(CultureInfo.InvariantCulture));
        emitter.Emit(new MappingEnd());

        // Body
        Key(emitter, "Body");
        emitter.Emit(BeginSequence());
        foreach (var record in file.Records)
            EmitRecord(emitter, record, schema);
        emitter.Emit(new SequenceEnd());

        emitter.Emit(new MappingEnd());
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());
    }

    private static void EmitRecord(IEmitter emitter, DbRecord record, DbSchema schema)
    {
        emitter.Emit(new MappingStart());

        foreach (var field in schema.Fields)
        {
            if (!record.Has(field.Name)) continue;
            var value = record.Get(field.Name);
            if (IsOmitted(field, value)) continue;

            Key(emitter, field.Name);
            EmitValue(emitter, field, value!);
        }

        foreach (var (key, value) in record.Extras)
        {
            Key(emitter, key);
            EmitGeneric(emitter, value);
        }

        emitter.Emit(new MappingEnd());
    }

    private static void EmitValue(IEmitter emitter, FieldSchema field, object value)
    {
        switch (field.Kind)
        {
            case FieldKind.Int:
            case FieldKind.Long:
                Scalar(emitter, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0");
                break;
            case FieldKind.Bool:
                Scalar(emitter, value is bool b && b ? "true" : "false");
                break;
            case FieldKind.String:
            case FieldKind.Enum:
            case FieldKind.Reference:
                Scalar(emitter, value.ToString() ?? string.Empty);
                break;
            case FieldKind.Script:
                Literal(emitter, (value as ScriptValue)?.Text ?? string.Empty);
                break;
            case FieldKind.Flags:
            case FieldKind.BoolMap:
                EmitBoolMap(emitter, field, (ISet<string>)value);
                break;
            case FieldKind.Object:
                EmitRecord(emitter, (DbRecord)value, field.ObjectSchema!);
                break;
            case FieldKind.ObjectList:
                emitter.Emit(BeginSequence());
                foreach (var child in (IEnumerable<DbRecord>)value)
                    EmitRecord(emitter, child, field.ObjectSchema!);
                emitter.Emit(new SequenceEnd());
                break;
            case FieldKind.ScalarList:
                emitter.Emit(BeginSequence());
                foreach (var item in (IEnumerable)value)
                    Scalar(emitter, Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
                emitter.Emit(new SequenceEnd());
                break;
            case FieldKind.LevelInt:
                EmitLevel(emitter, field, (LevelList)value);
                break;
        }
    }

    private static void EmitLevel(IEmitter emitter, FieldSchema field, LevelList level)
    {
        if (level.Scalar is { } s)
        {
            Scalar(emitter, s.ToString(CultureInfo.InvariantCulture));
            return;
        }
        emitter.Emit(BeginSequence());
        foreach (var entry in level.Levels)
        {
            emitter.Emit(new MappingStart());
            Key(emitter, "Level");
            Scalar(emitter, entry.Level.ToString(CultureInfo.InvariantCulture));
            Key(emitter, field.LevelValueKey);
            Scalar(emitter, entry.Value.ToString(CultureInfo.InvariantCulture));
            emitter.Emit(new MappingEnd());
        }
        emitter.Emit(new SequenceEnd());
    }

    private static void EmitBoolMap(IEmitter emitter, FieldSchema field, ISet<string> set)
    {
        emitter.Emit(new MappingStart());
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in field.Enum?.Values ?? Array.Empty<string>())
        {
            if (set.Contains(key))
            {
                Key(emitter, key);
                Scalar(emitter, "true");
                emitted.Add(key);
            }
        }
        foreach (var key in set)
        {
            if (emitted.Add(key))
            {
                Key(emitter, key);
                Scalar(emitter, "true");
            }
        }
        // Exclusions (token: false) for the "all-except" pattern. Emitted AFTER the includes — the universal
        // "All" is already first via the enum-order pass above, which is the order rAthena applies.
        if (set is BoolMap bm)
        {
            foreach (var key in field.Enum?.Values ?? Array.Empty<string>())
                if (bm.Excluded.Contains(key) && emitted.Add(key)) { Key(emitter, key); Scalar(emitter, "false"); }
            foreach (var key in bm.Excluded)
                if (emitted.Add(key)) { Key(emitter, key); Scalar(emitter, "false"); }
        }
        emitter.Emit(new MappingEnd());
    }

    private static void EmitGeneric(IEmitter emitter, object? value)
    {
        switch (value)
        {
            case null:
                Scalar(emitter, string.Empty);
                break;
            case string s:
                if (s.Contains('\n')) Literal(emitter, s); // keep an unknown multi-line/script field as a | block (audit #10)
                else Scalar(emitter, s);
                break;
            case IDictionary<string, object?> map:
                emitter.Emit(new MappingStart());
                foreach (var (k, v) in map)
                {
                    Key(emitter, k);
                    EmitGeneric(emitter, v);
                }
                emitter.Emit(new MappingEnd());
                break;
            case IEnumerable seq:
                emitter.Emit(BeginSequence());
                foreach (var item in seq)
                    EmitGeneric(emitter, item);
                emitter.Emit(new SequenceEnd());
                break;
            default:
                Scalar(emitter, value.ToString() ?? string.Empty);
                break;
        }
    }

    internal static bool IsOmitted(FieldSchema field, object? value)
    {
        if (value is null) return true;
        switch (field.Kind)
        {
            case FieldKind.Int:
                return Convert.ToInt32(value) == (field.Default is int di ? di : 0);
            case FieldKind.Long:
                return Convert.ToInt64(value) == (field.Default is long dl ? dl : 0L);
            case FieldKind.Bool:
                return (value is bool b && b) == (field.Default is bool dbv && dbv);
            case FieldKind.String:
            case FieldKind.Enum:
            case FieldKind.Reference:
                var s = value as string;
                if (string.IsNullOrEmpty(s)) return true;
                return field.Default is string ds && string.Equals(s, ds, StringComparison.Ordinal);
            case FieldKind.Script:
                return value is not ScriptValue sv || sv.IsEmpty;
            case FieldKind.Flags:
            case FieldKind.BoolMap:
                if (value is not ISet<string> set) return true;
                if (set is BoolMap bm && bm.Excluded.Count > 0) return false; // keep "all-except" maps
                return set.Count == 0;
            case FieldKind.Object:
                return value is not DbRecord rec || field.ObjectSchema is null || !RecordHasContent(rec, field.ObjectSchema);
            case FieldKind.ObjectList:
            case FieldKind.ScalarList:
                return value is not ICollection col || col.Count == 0;
            case FieldKind.LevelInt:
                return value is not LevelList ll || (ll.Levels.Count == 0 && ll.Scalar is null or 0);
            default:
                return false;
        }
    }

    private static bool RecordHasContent(DbRecord record, DbSchema schema)
    {
        foreach (var field in schema.Fields)
        {
            if (record.Has(field.Name) && !IsOmitted(field, record.Get(field.Name)))
                return true;
        }
        return record.Extras.Count > 0;
    }

    private static SequenceStart BeginSequence() =>
        new(AnchorName.Empty, TagName.Empty, isImplicit: true, SequenceStyle.Block);

    private static void Key(IEmitter emitter, string key) => emitter.Emit(new Scalar(key));

    private static void Scalar(IEmitter emitter, string value) => emitter.Emit(new Scalar(value));

    private static void Literal(IEmitter emitter, string value) =>
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, ScalarStyle.Literal, isPlainImplicit: true, isQuotedImplicit: false));
}
