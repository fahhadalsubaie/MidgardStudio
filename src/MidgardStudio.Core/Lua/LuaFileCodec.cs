using System.Text;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Reads/writes client lua/lub text with a fixed codepage (default Windows-1252) and preserves the file's
/// dominant line ending on write (CRLF for new/empty files). The "Korean-looking" resource names are 1252
/// bytes preserved verbatim.
/// Reads are lenient; writes THROW on any character that cannot be represented in the client codepage
/// (e.g. an em-dash, curly quotes, ellipsis or emoji pasted into an item name) instead of silently
/// substituting '?' and corrupting the user's text.
/// </summary>
public sealed class LuaFileCodec
{
    static LuaFileCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LuaFileCodec(int codepage = 1252) => Codepage = codepage;

    public int Codepage { get; }

    /// <summary>Lenient decode (every byte in a single-byte codepage maps to a char).</summary>
    private Encoding Read => Encoding.GetEncoding(Codepage);

    /// <summary>Strict encode: throws <see cref="EncoderFallbackException"/> on a non-representable char.</summary>
    private Encoding Write =>
        Encoding.GetEncoding(Codepage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

    public string ReadText(string path) => Read.GetString(File.ReadAllBytes(path));

    public byte[] EncodeText(string text)
    {
        // Preserve the file's existing dominant line ending instead of forcing CRLF, so a one-entry edit to an
        // LF-only or mixed file doesn't rewrite every line (audit #11/#14). The spliced text already carries
        // the original bytes verbatim, so its dominant style IS the file's. CRLF stays the tie-break + the
        // empty/new-file default (the app's own templates are CRLF).
        int crlf = CountSubstring(text, "\r\n");
        int loneLf = -crlf;
        foreach (var c in text) if (c == '\n') loneLf++;
        string nl = loneLf > crlf ? "\n" : "\r\n";
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", nl);
        try
        {
            return Write.GetBytes(normalized);
        }
        catch (EncoderFallbackException ex)
        {
            string ch = ex.CharUnknown != '\0'
                ? $"'{ex.CharUnknown}' (U+{(int)ex.CharUnknown:X4})"
                : "an unsupported symbol";
            string codec = $"{Write.EncodingName} (codepage {Codepage})";
            throw new InvalidDataException(
                $"This text contains {ch}, which can't be saved to this client's {codec} encoding — your edit was NOT written. " +
                "Remove that character (or switch the profile's Display Encoding to one that supports it) and save again.",
                ex);
        }
    }

    /// <summary>Atomic write (temp + Replace/Move) so a crash mid-write can't truncate the live file.</summary>
    public void WriteText(string path, string text)
    {
        var bytes = EncodeText(text);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ } throw; }
    }

    private static int CountSubstring(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, System.StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }
}
