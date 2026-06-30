using System.Collections;
using System.Runtime.CompilerServices;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Validation;

/// <summary>
/// The generic, schema-driven validator. It walks a record's fields (recursing into nested
/// Object/ObjectList values) and emits the rules that can be derived purely from <see cref="FieldSchema"/>
/// metadata: required fields, enum/flag membership, numeric bounds, string length caps, applicability,
/// renewal-mode scope, and cross-database reference resolution. Applies to every database.
/// </summary>
public sealed class SchemaDrivenValidator : IRecordValidator
{
    // Per-table whitelist of enum/flag values that appear in the read-only base data, keyed by field name
    // (including nested fields). rAthena's own data only contains values it accepts, so these are
    // authoritative — they are unioned with the schema's curated value lists (which are NOT exhaustive,
    // e.g. achievement Group / item Location aliases) to avoid flagging legitimate values.
    private static readonly ConditionalWeakTable<OverlayTable, Dictionary<string, HashSet<string>>> ObservedCache = new();

    public bool AppliesTo(string dbId) => true;

    public IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context)
    {
        var issues = new List<ValidationIssue>();
        var observed = ObservedFor(table);
        // Official/base records are authoritative and clean: don't nag about advisory mismatches on them
        // (e.g. pre-re base skills legitimately carry the renewal-only FixedCastTime, which is harmlessly ignored).
        bool isBase = record.Origin == RecordOrigin.Base;
        ValidateFields(record, record.Schema, context, record.Schema.Id, record.Key.ToString(), prefix: null, isBase, observed, issues);
        return issues;
    }

    private static void ValidateFields(
        DbRecord record, DbSchema schema, ValidationContext ctx,
        string dbId, string key, string? prefix, bool isBase, Dictionary<string, HashSet<string>> observed, List<ValidationIssue> issues)
    {
        foreach (var field in schema.Fields)
        {
            string label = prefix is null ? field.Label : $"{prefix} → {field.Label}";

            // 1. Applicability — a stray value on a non-applicable field is silently ignored by rAthena.
            if (field.IsApplicable is not null && !field.IsApplicable(record))
            {
                if (!isBase && HasNonDefaultValue(record, field))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, dbId, key, field.Name,
                        $"{label} is set but does not apply here — it will be ignored.")
                    { RuleId = "FIELD.NOT_APPLICABLE" });
                continue;
            }

            // 2. Renewal-mode scope — a renewal-only field has no effect pre-renewal and vice versa.
            bool wrongMode =
                (field.Renewal == RenewalScope.RenewalOnly && ctx.Mode == ServerMode.PreRenewal) ||
                (field.Renewal == RenewalScope.PreRenewalOnly && ctx.Mode == ServerMode.Renewal);
            if (wrongMode)
            {
                if (!isBase && HasNonDefaultValue(record, field))
                {
                    string only = field.Renewal == RenewalScope.RenewalOnly ? "renewal" : "pre-renewal";
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, dbId, key, field.Name,
                        $"{label} only exists in {only}; it is ignored in the active mode.")
                    { RuleId = "FIELD.RENEWAL_MISMATCH", Mode = ctx.Mode });
                }
                continue;
            }

            // 3. Required.
            if (field.IsRequired && IsEmptyValue(record, field))
                issues.Add(new ValidationIssue(field.RequiredSeverity, dbId, key, field.Name,
                    $"{label} is required.")
                { RuleId = "FIELD.REQUIRED" });

            // 4. Value-dependent checks.
            switch (field.Kind)
            {
                case FieldKind.Enum:
                {
                    string? v = record.GetString(field.Name);
                    if (!string.IsNullOrWhiteSpace(v) && field.Enum is { IsReference: false } && !IsKnownValue(field, v, observed))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, dbId, key, field.Name,
                            $"'{v}' is not a recognized {field.Label} value — verify the spelling (rAthena may reject it).")
                        { RuleId = "FIELD.ENUM_INVALID" });
                    break;
                }
                case FieldKind.Flags:
                case FieldKind.BoolMap:
                {
                    if (field.Enum is not null)
                    {
                        var set = record.GetSet(field.Name);
                        var excluded = record.GetBoolMap(field.Name)?.Excluded; // "all-except" tokens are spell-checked too
                        foreach (var member in (set ?? Enumerable.Empty<string>()).Concat(excluded ?? Enumerable.Empty<string>()))
                            if (!IsKnownValue(field, member, observed))
                                issues.Add(new ValidationIssue(ValidationSeverity.Warning, dbId, key, field.Name,
                                    $"'{member}' is not a recognized {field.Label} option — verify the spelling (rAthena may reject it).")
                                { RuleId = "FIELD.FLAG_INVALID" });
                    }
                    break;
                }
                case FieldKind.Reference:
                {
                    string? v = record.GetString(field.Name);
                    if (!string.IsNullOrWhiteSpace(v) && field.Enum?.ReferenceDb is { } refDb
                        && ctx.References.Knows(refDb) && !ctx.References.Contains(refDb, v))
                        issues.Add(new ValidationIssue(field.ReferenceSeverity, dbId, key, field.Name,
                            $"{label} '{v}' does not exist in {refDb}.")
                        { RuleId = "XREF.REFERENCE_MISSING" });
                    break;
                }
                case FieldKind.Int:
                case FieldKind.Long:
                {
                    if (field.Min is null && field.Max is null) break;
                    long n = record.GetLong(field.Name);
                    long clamped = n;
                    if (field.Min is { } mn && clamped < mn) clamped = mn;
                    if (field.Max is { } mx && clamped > mx) clamped = mx;
                    if (clamped != n)
                    {
                        object? oldVal = record.Get(field.Name);
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning, dbId, key, field.Name,
                            BoundsMessage(label, n, field.Min, field.Max))
                        {
                            RuleId = "FIELD.BOUNDS",
                            // No fix on read-only base data (Full Scan): a fix can't edit base in place — the
                            // user overrides the entry first, then fixes their custom copy.
                            Fix = isBase ? null : new QuickFix($"Clamp to {clamped}",
                                () => record.Set(field.Name, (int)clamped), () => record.Set(field.Name, oldVal)),
                        });
                    }
                    break;
                }
                case FieldKind.Object:
                {
                    if (record.GetObject(field.Name) is { } nested && field.ObjectSchema is { } os)
                        ValidateFields(nested, os, ctx, dbId, key, label, isBase, observed, issues);
                    break;
                }
                case FieldKind.ObjectList:
                {
                    if (record.GetList(field.Name) is { } list && field.ObjectSchema is { } os)
                        foreach (var item in list)
                            ValidateFields(item, os, ctx, dbId, key, label, isBase, observed, issues);
                    break;
                }
            }

            // 5. String length caps (AegisName/Name, etc.).
            if (field.MaxLength is { } maxLen
                && field.Kind is FieldKind.String or FieldKind.Enum or FieldKind.Reference)
            {
                string? s = record.GetString(field.Name);
                if (s is not null && s.Length > maxLen)
                    issues.Add(new ValidationIssue(field.MaxLengthSeverity, dbId, key, field.Name,
                        $"{label} is {s.Length} characters; the maximum is {maxLen}.")
                    {
                        RuleId = "FIELD.MAXLENGTH",
                        Fix = isBase ? null : new QuickFix($"Trim to {maxLen} characters",
                            () => record.Set(field.Name, s[..maxLen]), () => record.Set(field.Name, s)),
                    });
            }
        }
    }

    /// <summary>A value is "known" if it is in the schema's curated list OR appears anywhere in the
    /// authoritative base data for that field (case-insensitive).</summary>
    private static bool IsKnownValue(FieldSchema field, string value, Dictionary<string, HashSet<string>> observed)
    {
        if (field.Enum is { } e && e.Values.Count > 0 && Contains(e.Values, value)) return true;
        return observed.TryGetValue(field.Name, out var set) && set.Contains(value);
    }

    // ---- base-data observed-value index (per table, cached) ----

    private static Dictionary<string, HashSet<string>> ObservedFor(OverlayTable table) =>
        ObservedCache.GetValue(table, BuildObserved);

    private static Dictionary<string, HashSet<string>> BuildObserved(OverlayTable table)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var record in table.BaseRecords())
            CollectObserved(record, table.Schema, map);
        return map;
    }

    private static void CollectObserved(DbRecord record, DbSchema schema, Dictionary<string, HashSet<string>> map)
    {
        foreach (var field in schema.Fields)
        {
            switch (field.Kind)
            {
                case FieldKind.Enum:
                {
                    var v = record.GetString(field.Name);
                    if (!string.IsNullOrWhiteSpace(v)) Add(map, field.Name, v);
                    break;
                }
                case FieldKind.Flags:
                case FieldKind.BoolMap:
                {
                    if (record.GetSet(field.Name) is { } set)
                        foreach (var member in set) Add(map, field.Name, member);
                    if (record.GetBoolMap(field.Name)?.Excluded is { } excluded) // "all-except" tokens count too
                        foreach (var member in excluded) Add(map, field.Name, member);
                    break;
                }
                case FieldKind.Object:
                {
                    if (record.GetObject(field.Name) is { } nested && field.ObjectSchema is { } os)
                        CollectObserved(nested, os, map);
                    break;
                }
                case FieldKind.ObjectList:
                {
                    if (record.GetList(field.Name) is { } list && field.ObjectSchema is { } os)
                        foreach (var item in list)
                            CollectObserved(item, os, map);
                    break;
                }
            }
        }
    }

    private static void Add(Dictionary<string, HashSet<string>> map, string field, string value)
    {
        if (!map.TryGetValue(field, out var set))
            map[field] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.Add(value);
    }

    private static string BoundsMessage(string label, long value, int? min, int? max)
    {
        if (min is { } a && max is { } b) return $"{label} must be between {a} and {b} (was {value}).";
        if (min is { } lo) return $"{label} must be at least {lo} (was {value}).";
        return $"{label} must be at most {max} (was {value}).";
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (var v in values)
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsEmptyValue(DbRecord r, FieldSchema f) => f.Kind switch
    {
        FieldKind.String or FieldKind.Enum or FieldKind.Reference => string.IsNullOrWhiteSpace(r.GetString(f.Name)),
        FieldKind.Flags or FieldKind.BoolMap => (r.GetSet(f.Name)?.Count ?? 0) == 0,
        FieldKind.Object => r.GetObject(f.Name) is null,
        FieldKind.ObjectList => (r.GetList(f.Name)?.Count ?? 0) == 0,
        FieldKind.Script => r.GetScript(f.Name) is null or { IsEmpty: true },
        _ => !r.Has(f.Name),
    };

    private static bool HasNonDefaultValue(DbRecord r, FieldSchema f)
    {
        switch (f.Kind)
        {
            case FieldKind.Int:
            case FieldKind.Long:
                if (!r.Has(f.Name)) return false;
                long n = r.GetLong(f.Name);
                long def = f.Default is null ? 0 : Convert.ToInt64(f.Default);
                return n != def;
            case FieldKind.Bool:
                if (!r.Has(f.Name)) return false;
                bool b = r.GetBool(f.Name);
                bool bd = f.Default is bool bb && bb;
                return b != bd;
            case FieldKind.String:
            case FieldKind.Enum:
            case FieldKind.Reference:
                var s = r.GetString(f.Name);
                if (string.IsNullOrWhiteSpace(s)) return false;
                var ds = f.Default?.ToString();
                return ds is null || !string.Equals(s, ds, StringComparison.Ordinal);
            case FieldKind.Flags:
            case FieldKind.BoolMap:
                return (r.GetSet(f.Name)?.Count ?? 0) > 0;
            case FieldKind.Script:
                return r.GetScript(f.Name) is { IsEmpty: false };
            case FieldKind.Object:
                return r.GetObject(f.Name) is not null;
            case FieldKind.ObjectList:
                return (r.GetList(f.Name)?.Count ?? 0) > 0;
            case FieldKind.ScalarList:
                return r.Get(f.Name) is IList { Count: > 0 };
            case FieldKind.LevelInt:
                return r.GetLevel(f.Name) is { IsEmpty: false };
            default:
                return r.Has(f.Name);
        }
    }
}
