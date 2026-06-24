using System.Text;

namespace MidgardStudio.Core.Lua;

/// <summary>
/// Reads/writes client lua/lub text with a fixed codepage (default Windows-1252) and normalizes line
/// endings to CRLF on write. The "Korean-looking" resource names are 1252 bytes preserved verbatim.
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
        string crlf = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        try
        {
            return Write.GetBytes(crlf);
        }
        catch (EncoderFallbackException ex)
        {
            string ch = ex.CharUnknown != '\0'
                ? $"'{ex.CharUnknown}' (U+{(int)ex.CharUnknown:X4})"
                : "an unsupported symbol";
            throw new InvalidDataException(
                $"This text contains {ch}, which can't be saved to a Windows-1252 client file — your edit was NOT written. " +
                "Client text must use Western (Latin-1) characters; remove the emoji or non-Western character (e.g. Korean, Chinese, Cyrillic) and save again.",
                ex);
        }
    }

    public void WriteText(string path, string text) => File.WriteAllBytes(path, EncodeText(text));
}
