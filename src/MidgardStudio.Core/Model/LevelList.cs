using System.Globalization;
using System.Text;

namespace MidgardStudio.Core.Model;

/// <summary>One per-level entry of a <see cref="LevelList"/> (skill level → value).</summary>
public readonly record struct LevelEntry(int Level, int Value);

/// <summary>
/// A skill_db "dual-typed" numeric value: either a single scalar that applies to every level
/// (<c>Range: 5</c>) or a per-level array (<c>Range: [{Level: 1, Size: 3}, …]</c>). rAthena accepts
/// both forms for many fields, so the editor preserves whichever the data uses and round-trips it
/// unchanged. The per-level inner value key (Size/Time/Amount/…) is supplied by the field schema.
/// </summary>
public sealed class LevelList
{
    /// <summary>The scalar value, when the field is a single number (null when per-level or empty).</summary>
    public int? Scalar { get; set; }

    /// <summary>The per-level entries, when the field is an array (empty when scalar).</summary>
    public List<LevelEntry> Levels { get; } = new();

    public bool IsPerLevel => Levels.Count > 0;

    public bool IsEmpty => Scalar is null && Levels.Count == 0;

    public static LevelList FromScalar(int value) => new() { Scalar = value };

    public LevelList Clone()
    {
        var clone = new LevelList { Scalar = Scalar };
        clone.Levels.AddRange(Levels);
        return clone;
    }

    /// <summary>"5" → scalar 5; "1:3, 2:5" → per-level; empty/blank → empty.</summary>
    public static LevelList Parse(string? text)
    {
        var result = new LevelList();
        if (string.IsNullOrWhiteSpace(text)) return result;

        text = text.Trim();
        if (!text.Contains(':') && !text.Contains(','))
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                result.Scalar = s;
            return result;
        }

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':', 2);
            if (pair.Length == 2
                && int.TryParse(pair[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl)
                && int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                result.Levels.Add(new LevelEntry(lvl, val));
        }
        return result;
    }

    /// <summary>scalar → "5"; per-level → "1:3, 2:5"; empty → "".</summary>
    public string ToCompactString()
    {
        if (Scalar is { } s) return s.ToString(CultureInfo.InvariantCulture);
        if (Levels.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < Levels.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Levels[i].Level).Append(':').Append(Levels[i].Value);
        }
        return sb.ToString();
    }
}
