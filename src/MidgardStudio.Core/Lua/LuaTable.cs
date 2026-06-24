namespace MidgardStudio.Core.Lua;

/// <summary>
/// A parsed Lua table value. Holds positional items (the array part) plus keyed entries: integer keys
/// (<c>[123] = ...</c>), name keys (<c>foo = ...</c>), and bracketed-expression keys captured raw
/// (<c>[ACCESSORY_IDs.X] = ...</c>). Values are string, double, bool, null, or a nested LuaTable.
/// </summary>
public sealed class LuaTable
{
    public List<object?> Array { get; } = new();

    public Dictionary<long, object?> IntKeys { get; } = new();

    public Dictionary<string, object?> NameKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Entries keyed by a bracketed expression, in source order: (rawKeyText, value).</summary>
    public List<(string Key, object? Value)> ExprKeys { get; } = new();

    public string? GetString(string name) => NameKeys.GetValueOrDefault(name) as string;

    public double? GetNumber(string name) => NameKeys.GetValueOrDefault(name) as double?;

    public int GetInt(string name) => NameKeys.GetValueOrDefault(name) is double d ? (int)d : 0;

    public bool GetBool(string name) => NameKeys.GetValueOrDefault(name) is bool b && b;

    public LuaTable? GetTable(string name) => NameKeys.GetValueOrDefault(name) as LuaTable;

    public IReadOnlyList<string> GetStringArray(string name) =>
        GetTable(name)?.Array.Select(x => x?.ToString() ?? string.Empty).ToList() ?? new List<string>();
}
