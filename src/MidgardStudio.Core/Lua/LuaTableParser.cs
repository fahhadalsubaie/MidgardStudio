using System.Globalization;
using System.Text;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// A small recursive-descent parser for the Lua data tables RO clients use (itemInfo, accessoryid,
/// accname, ...). Handles strings, numbers, booleans/nil, nested tables, line/block comments, and the
/// three key forms (positional, name = , [expr] = ). It is not a full Lua interpreter — just enough to
/// round-trip these data files.
/// </summary>
public sealed class LuaTableParser
{
    private readonly string _s;
    private int _i;

    public LuaTableParser(string text)
    {
        _s = text;
        _i = 0;
    }

    /// <summary>Finds <c>name = { ... }</c> at top level and parses the table; null if not present.</summary>
    public LuaTable? ParseNamedTable(string name)
    {
        int idx = FindAssignment(name);
        if (idx < 0) return null;
        _i = idx;
        SkipTrivia();
        if (Peek() != '{') return null;
        return ParseTable();
    }

    private int FindAssignment(string name)
    {
        // Find `name` followed (after whitespace) by '=' then (after whitespace) '{', not inside a string/comment.
        for (int i = 0; i < _s.Length; i++)
        {
            if (!MatchIdentifierAt(i, name)) continue;
            int j = i + name.Length;
            int eq = SkipWsFrom(j);
            if (eq < _s.Length && _s[eq] == '=')
            {
                int brace = SkipWsFrom(eq + 1);
                if (brace < _s.Length && _s[brace] == '{')
                    return brace;
            }
        }
        return -1;
    }

    private bool MatchIdentifierAt(int i, string name)
    {
        if (i + name.Length > _s.Length) return false;
        if (string.CompareOrdinal(_s, i, name, 0, name.Length) != 0) return false;
        if (i > 0 && (char.IsLetterOrDigit(_s[i - 1]) || _s[i - 1] == '_')) return false; // not a sub-token
        int after = i + name.Length;
        if (after < _s.Length && (char.IsLetterOrDigit(_s[after]) || _s[after] == '_')) return false;
        return true;
    }

    private int SkipWsFrom(int i)
    {
        while (i < _s.Length && char.IsWhiteSpace(_s[i])) i++;
        return i;
    }

    private LuaTable ParseTable()
    {
        var table = new LuaTable();
        Expect('{');
        SkipTrivia();
        while (Peek() != '}' && !Eof)
        {
            int loopStart = _i;
            char c = Peek();
            if (c == '[')
            {
                // [expr] = value  — expr is a number or an identifier expression
                Next(); // [
                SkipTrivia();
                string rawKey = ReadUntilCloseBracket().Trim();
                SkipTrivia();
                Expect('=');
                SkipTrivia();
                object? value = ParseValue();
                if (long.TryParse(rawKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ik))
                    table.IntKeys[ik] = value;
                else
                    table.ExprKeys.Add((rawKey, value));
            }
            else if (c == '"' || c == '\'' || c == '{' || c == '-' || char.IsDigit(c))
            {
                table.Array.Add(ParseValue()); // positional
            }
            else
            {
                // name = value   (or a bare keyword value like true/false/nil used positionally)
                string ident = ReadIdentifier();
                SkipTrivia();
                if (Peek() == '=')
                {
                    Next();
                    SkipTrivia();
                    table.NameKeys[ident] = ParseValue();
                }
                else
                {
                    table.Array.Add(KeywordValue(ident));
                }
            }

            SkipTrivia();
            if (Peek() == ',' || Peek() == ';') { Next(); SkipTrivia(); }

            // Guarantee forward progress: a stray non-identifier char would otherwise leave _i unchanged
            // and spin the loop forever on a malformed/partially-written client file.
            if (_i == loopStart) Next();
        }
        Expect('}');
        return table;
    }

    private object? ParseValue()
    {
        SkipTrivia();
        char c = Peek();
        if (c == '{') return ParseTable();
        if (c == '"' || c == '\'') return ReadString();
        if (c == '-' || char.IsDigit(c)) return ReadNumber();

        string ident = ReadIdentifier();
        return KeywordValue(ident);
    }

    private static object? KeywordValue(string ident) => ident switch
    {
        "true" => true,
        "false" => false,
        "nil" => null,
        _ => ident, // an identifier/expression used as a value
    };

    private string ReadString()
    {
        char quote = Next();
        int start = _i;

        // Fast path: most strings (color codes, plain names) have no escapes — return a single substring.
        while (!Eof)
        {
            char c = Peek();
            if (c == '\\') break;       // contains an escape -> fall through to the rebuild path
            if (c == quote) { string s = _s.Substring(start, _i - start); Next(); return s; }
            Next();
        }
        if (Eof) return _s.Substring(start, _i - start); // unterminated string at EOF

        // Slow path: rebuild with escapes resolved, keeping the un-escaped prefix already scanned.
        var sb = new StringBuilder();
        sb.Append(_s, start, _i - start);
        while (!Eof)
        {
            char c = Next();
            if (c == '\\')
            {
                if (Eof) break; // trailing backslash with no escaped char — don't read past EOF
                char e = Next();
                // Decode the escapes we understand; for ANY other escape keep BOTH chars verbatim so a
                // Windows path like "C:\Program Files\Gravity" round-trips (the writer re-escapes the
                // backslash). Dropping the backslash here silently corrupted such strings (audit #5).
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': sb.Append('\\'); break; // \\ -> one backslash
                    case '"': sb.Append('"'); break;   // \" -> quote
                    default: sb.Append('\\'); sb.Append(e); break; // unknown escape preserved verbatim
                }
            }
            else if (c == quote)
            {
                break;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private double ReadNumber()
    {
        int start = _i;
        if (Peek() == '-') Next();
        while (!Eof && (char.IsDigit(Peek()) || Peek() == '.' || Peek() == 'e' || Peek() == 'E' || Peek() == 'x' || Peek() == 'X' || (Peek() >= 'a' && Peek() <= 'f') || (Peek() >= 'A' && Peek() <= 'F')))
            Next();
        // Parse straight off the source span — the client files are dense with numbers, so a discarded
        // substring per number is the dominant parse allocation.
        ReadOnlySpan<char> num = _s.AsSpan(start, _i - start);
        if (num.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(num[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return hex;
        return double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private string ReadIdentifier()
    {
        int start = _i;
        while (!Eof && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '.')) Next();
        return _s.Substring(start, _i - start);
    }

    private string ReadUntilCloseBracket()
    {
        int start = _i;
        while (!Eof && Peek() != ']') Next();
        string s = _s.Substring(start, _i - start);
        if (Peek() == ']') Next();
        return s;
    }

    private void SkipTrivia()
    {
        while (!Eof)
        {
            char c = Peek();
            if (char.IsWhiteSpace(c)) { Next(); continue; }
            if (c == '-' && _i + 1 < _s.Length && _s[_i + 1] == '-')
            {
                _i += 2;
                if (Peek() == '[' && _i + 1 < _s.Length && _s[_i + 1] == '[')
                {
                    _i += 2;
                    int end = _s.IndexOf("]]", _i, StringComparison.Ordinal);
                    _i = end < 0 ? _s.Length : end + 2;
                }
                else
                {
                    while (!Eof && Peek() != '\n') Next();
                }
                continue;
            }
            break;
        }
    }

    private bool Eof => _i >= _s.Length;
    private char Peek() => _i < _s.Length ? _s[_i] : '\0';
    private char Next() => _i < _s.Length ? _s[_i++] : '\0'; // bounds-safe: never reads past EOF
    private void Expect(char c) { if (Peek() == c) Next(); }
}
