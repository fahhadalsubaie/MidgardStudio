namespace MidgardStudio.Core.Lua;

/// <summary>
/// Reads and appends to the headgear sprite tables: accessoryid.lub (ACCESSORY_IDs constants) and
/// accname.lub (AccNameTable mapping constant -&gt; sprite file). Robe tables share the same shape.
/// </summary>
public static class AccessoryTables
{
    /// <summary>Parses ACCESSORY_IDs: constant name -> numeric id.</summary>
    public static Dictionary<string, int> ReadConstants(string accessoryIdText, string tableName = "ACCESSORY_IDs")
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var table = new LuaTableParser(accessoryIdText).ParseNamedTable(tableName);
        if (table is null) return result;
        foreach (var (name, value) in table.NameKeys)
            if (value is double d) result[name] = (int)d;
        return result;
    }

    /// <summary>Parses AccNameTable: constant name (from [ACCESSORY_IDs.X]) -> sprite file name.</summary>
    public static Dictionary<string, string> ReadNames(string accNameText, string tableName = "AccNameTable")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var table = new LuaTableParser(accNameText).ParseNamedTable(tableName);
        if (table is null) return result;
        foreach (var (key, value) in table.ExprKeys)
        {
            int dot = key.LastIndexOf('.');
            string name = dot >= 0 ? key[(dot + 1)..] : key;
            if (value is string s) result[name] = s;
        }
        return result;
    }

    public static int NextFreeId(IReadOnlyDictionary<string, int> constants) =>
        constants.Count == 0 ? 1 : constants.Values.Max() + 1;

    /// <summary>Appends a constant <c>NAME = id,</c> inside the named table.</summary>
    public static string AppendConstant(string text, string tableName, string constantName, int id) =>
        InsertBeforeTableClose(text, tableName, $"\t{constantName} = {id},");

    /// <summary>Appends a <c>[ACCESSORY_IDs.NAME] = "sprite",</c> mapping inside the named table. The sprite is
    /// user-typed free text, so it's escaped through the shared quoter (symmetric with the reader — audit sweep).</summary>
    public static string AppendName(string text, string tableName, string idsTableName, string constantName, string sprite) =>
        InsertBeforeTableClose(text, tableName, $"\t[{idsTableName}.{constantName}] = {LuaString.Quote(sprite)},");

    /// <summary>Upserts a <c>[idsTable.NAME] = "sprite"</c> mapping: replaces the string value in place if
    /// the key already exists in the table, else appends. Repeated <see cref="AppendName"/> for the same
    /// key grew a stale duplicate line on every re-register (audit #7); this updates the one line instead,
    /// leaving every other byte (comments, other entries, trailing comma) untouched.</summary>
    public static string SetOrAppendName(string text, string tableName, string idsTableName, string constantName, string sprite)
    {
        int open = LuaScan.FindTableOpen(text, tableName);
        int close = open < 0 ? -1 : LuaScan.FindMatchingBrace(text, open);
        if (open < 0 || close < 0)
            return AppendName(text, tableName, idsTableName, constantName, sprite); // fail-loud via AppendName

        string keyToken = $"[{idsTableName}.{constantName}]";
        int keyAt = text.IndexOf(keyToken, open, close - open, StringComparison.Ordinal);
        if (keyAt < 0) return AppendName(text, tableName, idsTableName, constantName, sprite); // new key

        int eq = text.IndexOf('=', keyAt + keyToken.Length);
        int q1 = eq < 0 || eq > close ? -1 : text.IndexOf('"', eq + 1);
        int q2 = q1 < 0 || q1 > close ? -1 : text.IndexOf('"', q1 + 1);
        if (q2 < 0 || q2 > close) // value isn't a simple quoted string — don't risk a wrong splice, append
            return AppendName(text, tableName, idsTableName, constantName, sprite);

        return text[..q1] + LuaString.Quote(sprite) + text[(q2 + 1)..]; // replace the whole quoted token, escaped (audit sweep)
    }

    private static string InsertBeforeTableClose(string text, string tableName, string line)
    {
        // Reuse the shared string- AND comment-aware scanner so a brace inside a Lua comment can't
        // mis-locate the table's open/close (the old local scanners ignored comments).
        // These tables (SKID / ACCESSORY_IDs / AccNameTable / npc tables) are always present in a valid
        // base file, so a missing table means a malformed/incompatible file. Fail LOUD — the save path
        // keeps the edit in memory and rolls the transaction back — rather than silently returning the
        // file unchanged (which committed clean while dropping the edit, and could orphan references).
        int open = LuaScan.FindTableOpen(text, tableName);
        if (open < 0)
            throw new InvalidDataException(
                $"Couldn't find the '{tableName}' table in the client Lua file, so your change was NOT saved and the file was left untouched. " +
                "The file may be malformed or missing that table — open it and check, then save again.");

        int close = LuaScan.FindMatchingBrace(text, open);
        if (close < 0)
            throw new InvalidDataException(
                $"Couldn't find the end of the '{tableName}' table in the client Lua file, so your change was NOT saved and the file was left untouched. " +
                "The file may have a mismatched brace — open it and check, then save again.");

        // Add a field separator if the last entry lacks one — see LuaScan.SeparatorBeforeNewEntry (the rule
        // that bit v1.0.1). Place it right after the last real value (NOT after a trailing comment — audit #16),
        // then the new line just before the close brace, leaving any trailing comment/whitespace untouched.
        string sep = LuaScan.SeparatorBeforeNewEntry(text, open, close);
        int p = LuaScan.LastValueEnd(text, open, close); // index just past the last value (comment-aware)
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        return text[..p] + sep + text[p..close] + line + nl + text[close..];
    }
}
