using System;
using System.Collections.Generic;
using System.Linq;

namespace MidgardStudio.App.Common;

/// <summary>
/// Converts between a display name and an aegis (server id) name: Title Case both ways, underscores for
/// the aegis ("Crown of Valor" ⇄ "Crown_Of_Valor"). The first letter of each word is capitalised; the
/// rest of each word is preserved (so embedded caps like "HP" survive).
/// </summary>
public static class NameFormat
{
    public static string ToAegis(string display) => string.Join("_", Words(display).Select(Capitalize));

    public static string ToDisplay(string aegis) =>
        string.Join(" ", Words((aegis ?? string.Empty).Replace('_', ' ')).Select(Capitalize));

    private static IEnumerable<string> Words(string s) =>
        (s ?? string.Empty).Split(new[] { ' ', '\t', '_' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Capitalize(string w) =>
        w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..];
}
