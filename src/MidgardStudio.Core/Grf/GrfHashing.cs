using System.Security.Cryptography;

namespace MidgardStudio.Core.Grf;

/// <summary>Integrity hashes for a GRF entry's decompressed bytes — CRC32 (zlib) and MD5, lower-case hex.</summary>
public static class GrfHashing
{
    /// <summary>MD5 as 32 lower-case hex chars.</summary>
    public static string Md5(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    /// <summary>CRC32 (zlib polynomial 0xEDB88320) as 8 lower-case hex chars.</summary>
    public static string Crc32(byte[] data) => Crc32Value(data).ToString("x8");

    public static uint Crc32Value(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(crc & 1));
        }
        return ~crc;
    }
}
