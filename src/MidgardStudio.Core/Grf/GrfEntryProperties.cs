using System.Globalization;

namespace MidgardStudio.Core.Grf;

/// <summary>Raw metadata for one GRF entry, read by the Grf layer and formatted for display here.</summary>
public sealed record GrfEntryInfo(long SizeDecompressed, long SizeCompressed, long Offset, int Flags);

/// <summary>Formats a <see cref="GrfEntryInfo"/> into labelled rows for the properties view (no GRF dependency).</summary>
public static class GrfEntryProperties
{
    public static IReadOnlyList<(string Label, string Value)> Format(GrfEntryInfo e) => new[]
    {
        ("Size", HumanSize(e.SizeDecompressed)),
        ("Compressed", HumanSize(e.SizeCompressed)),
        ("Ratio", Ratio(e.SizeCompressed, e.SizeDecompressed)),
        ("Offset", "0x" + e.Offset.ToString("X", CultureInfo.InvariantCulture)),
        ("Flags", DescribeFlags(e.Flags)),
    };

    /// <summary>Compressed/decompressed as a percentage (smaller = better compression).</summary>
    public static string Ratio(long compressed, long decompressed)
        => decompressed <= 0 ? "—" : ((double)compressed / decompressed * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Decodes the GRF EntryType bitfield into a human list (matches GRF.Core.EntryType).</summary>
    public static string DescribeFlags(int flags)
    {
        var parts = new List<string>();
        if ((flags & (1 << 0)) != 0) parts.Add("File");
        if ((flags & (1 << 1)) != 0) parts.Add("Header-encrypted");
        if ((flags & (1 << 2)) != 0) parts.Add("Data-encrypted");
        if ((flags & (1 << 7)) != 0) parts.Add("Gravity-encrypted");
        if ((flags & (1 << 10)) != 0) parts.Add("LZMA");
        if ((flags & (1 << 11)) != 0) parts.Add("Raw");
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.0} {units[u]}";
    }
}
