namespace MidgardStudio.Core.Lua;

/// <summary>
/// Shared Lua string serialization. The quoter MUST be the exact inverse of <see cref="LuaTableParser"/>'s
/// string reader: the reader decodes <c>\n \t \r \\ \"</c> and preserves any other escape verbatim, so the
/// writer re-escapes backslash + quote AND the control chars <c>\n \t \r</c>. Backslash is replaced FIRST so
/// the escapes it adds are not themselves doubled. Before this was shared, two copies escaped only <c>\ "</c>,
/// which wrote a real newline/tab into the string literal (invalid Lua) and dropped Windows-path backslashes
/// (audit #2 / #5 / #12).
/// </summary>
public static class LuaString
{
    public static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t") + "\"";
}
