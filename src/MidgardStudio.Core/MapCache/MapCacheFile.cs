using System.IO.Compression;
using System.Text;

namespace MidgardStudio.Core.MapCache;

/// <summary>One map in a map_cache.dat: name + dimensions + the zlib-compressed cell-type bytes.</summary>
public sealed class MapCacheEntry
{
    public string Name { get; set; } = string.Empty;
    public int Xs { get; set; }
    public int Ys { get; set; }

    /// <summary>Zlib-compressed cell data. Uncompressed length = Xs*Ys, one type byte per cell.</summary>
    public byte[] CompressedCells { get; set; } = System.Array.Empty<byte>();

    public int Cells => Xs * Ys;
}

/// <summary>
/// rAthena's <c>map_cache.dat</c> binary format. Header: file_size(uint32) + map_count(uint16) +
/// reserved(uint16). Then per map: name[12] (ANSI, null-padded, lowercase), xs(uint16), ys(uint16),
/// len(int32), data[len] (zlib). file_size = 8 + Σlen + count*20. Little-endian throughout.
/// </summary>
public sealed class MapCacheFile
{
    public List<MapCacheEntry> Maps { get; set; } = new();

    public static MapCacheFile Read(byte[] bytes)
    {
        var file = new MapCacheFile();
        if (bytes.Length < 8) return file;

        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        r.ReadUInt32();                 // file_size (recomputed on write)
        int mapCount = r.ReadUInt16();
        r.ReadUInt16();                 // reserved

        for (int i = 0; i < mapCount && ms.Position + 20 <= ms.Length; i++)
        {
            string name = Encoding.Latin1.GetString(r.ReadBytes(12)).TrimEnd('\0').ToLowerInvariant();
            int xs = r.ReadUInt16();
            int ys = r.ReadUInt16();
            int len = r.ReadInt32();
            if (len < 0 || ms.Position + len > ms.Length) break;
            file.Maps.Add(new MapCacheEntry { Name = name, Xs = xs, Ys = ys, CompressedCells = r.ReadBytes(len) });
        }
        return file;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        uint fileSize = (uint)(8 + Maps.Sum(m => m.CompressedCells.Length) + Maps.Count * 20);
        w.Write(fileSize);
        w.Write((ushort)Maps.Count);
        w.Write((ushort)0);

        foreach (var m in Maps)
        {
            var name12 = new byte[12];
            byte[] nameBytes = Encoding.Latin1.GetBytes(m.Name ?? string.Empty);
            System.Array.Copy(nameBytes, name12, System.Math.Min(12, nameBytes.Length));
            w.Write(name12);
            w.Write((ushort)m.Xs);
            w.Write((ushort)m.Ys);
            w.Write(m.CompressedCells.Length);
            w.Write(m.CompressedCells);
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Builds a cache entry from a client <c>.gat</c> (walk-cell geometry). Stores the raw cell
    /// type per cell; water-height cells are not computed (use the standalone tool for water maps).</summary>
    public static MapCacheEntry FromGat(string mapName, byte[] gat)
    {
        if (gat.Length < 14 || gat[0] != (byte)'G' || gat[1] != (byte)'R' || gat[2] != (byte)'A' || gat[3] != (byte)'T')
            throw new InvalidDataException("Not a valid .gat file (missing GRAT header).");

        int width = System.BitConverter.ToInt32(gat, 6);
        int height = System.BitConverter.ToInt32(gat, 10);
        if (width <= 0 || height <= 0 || 14L + (long)width * height * 20 > gat.Length)
            throw new InvalidDataException("Unexpected .gat dimensions.");

        int n = width * height;
        var cells = new byte[n];
        for (int i = 0; i < n; i++)
            cells[i] = gat[14 + i * 20 + 16]; // low byte of the int32 cell type (little-endian)

        string name = (mapName ?? string.Empty).ToLowerInvariant();
        if (name.Length > 11) name = name[..11];

        return new MapCacheEntry { Name = name, Xs = width, Ys = height, CompressedCells = CompressZlib(cells) };
    }

    public static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public static byte[] DecompressZlib(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}
