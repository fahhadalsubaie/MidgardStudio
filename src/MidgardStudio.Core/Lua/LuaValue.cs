using System.Globalization;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Re-serializes a parsed Lua value (the kind <see cref="LuaTableParser"/> produces — string, double, bool,
/// null, or nested <see cref="LuaTable"/>) back to Lua source text. Used to round-trip per-entry fields the
/// editor does not model, so editing an entry re-emits them instead of dropping them (audit #3). It is
/// value-faithful (no data lost); nested-table formatting may differ from the original spacing.
/// </summary>
public static class LuaValue
{
    public static string Serialize(object? v) => v switch
    {
        null => "nil",
        bool b => b ? "true" : "false",
        string s => LuaString.Quote(s),
        double d => Number(d),
        LuaTable t => Table(t),
        _ => LuaString.Quote(v.ToString() ?? string.Empty),
    };

    private static string Number(double d) =>
        d == Math.Truncate(d) && !double.IsInfinity(d) && Math.Abs(d) < 9.2e18
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("R", CultureInfo.InvariantCulture);

    private static string Table(LuaTable t)
    {
        var parts = new List<string>();
        foreach (var item in t.Array) parts.Add(Serialize(item));
        foreach (var (k, val) in t.NameKeys) parts.Add($"{k} = {Serialize(val)}");
        foreach (var (k, val) in t.IntKeys) parts.Add($"[{k}] = {Serialize(val)}");
        foreach (var (k, val) in t.ExprKeys) parts.Add($"[{k}] = {Serialize(val)}");
        return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
    }
}
