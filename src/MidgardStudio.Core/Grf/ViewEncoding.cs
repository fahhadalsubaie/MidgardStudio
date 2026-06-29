using System.Text;

namespace MidgardStudio.Core.Grf;

/// <summary>One entry in the GRF Browser's view-encoding selector.</summary>
public sealed record EncodingChoice(int CodePage, string Display);

/// <summary>
/// View-only text encoding for the GRF Browser. The vendored GRF library decodes every entry NAME with a
/// single global codepage (pinned to 1252 app-wide, because icon/sprite lookups build 1252 paths). This
/// service lets the browser show file <b>content</b> and <b>display names</b> in any codepage WITHOUT touching
/// that global: content is decoded straight from the raw bytes, and a display name is re-projected from its
/// 1252 form via a byte-preserving round-trip — the original 1252 string stays the lookup key.
/// </summary>
public static class ViewEncoding
{
    public const int DefaultCodePage = 1252;

    static ViewEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The fixed codepages offered in the selector (a "Custom codepage…" entry is added by the UI).</summary>
    public static IReadOnlyList<EncodingChoice> Choices { get; } = new[]
    {
        new EncodingChoice(1252,  "Western / EN (1252)"),
        new EncodingChoice(949,   "Korean (949)"),
        new EncodingChoice(932,   "Japanese (932)"),
        new EncodingChoice(936,   "Chinese (936)"),
        new EncodingChoice(1251,  "Cyrillic (1251)"),
        new EncodingChoice(65001, "Unicode (UTF-8)"),
    };

    /// <summary>Decodes raw file bytes as text in <paramref name="codePage"/> (falls back to 1252 if unknown).</summary>
    public static string Decode(byte[] data, int codePage) => Resolve(codePage).GetString(data);

    /// <summary>
    /// Re-projects a name already decoded under <paramref name="fromCodePage"/> (default 1252, which is
    /// byte-preserving in .NET) into <paramref name="toCodePage"/> for display. Returns the input unchanged when
    /// the codepages match or the round-trip throws, so it can never break a name.
    /// </summary>
    public static string Reproject(string name, int toCodePage, int fromCodePage = DefaultCodePage)
    {
        if (toCodePage == fromCodePage || string.IsNullOrEmpty(name)) return name;
        try { return Resolve(toCodePage).GetString(Resolve(fromCodePage).GetBytes(name)); }
        catch { return name; }
    }

    /// <summary>True if the runtime has an encoding for <paramref name="codePage"/> (validates a custom entry).</summary>
    public static bool IsKnown(int codePage)
    {
        try { _ = Encoding.GetEncoding(codePage); return true; }
        catch { return false; }
    }

    private static Encoding Resolve(int codePage)
    {
        try { return Encoding.GetEncoding(codePage); }
        catch { return Encoding.GetEncoding(DefaultCodePage); }
    }
}
