using System.Collections.Generic;
using GRF.FileFormats.RswFormat;

namespace MidgardStudio.Grf;

/// <summary>RSW directional light: latitude/longitude → world light direction + ambient/diffuse colours.
/// Ported from GRFEditor SetupRswLight (RotX(−lat)·RotY(+long) applied to (0,1,0)).</summary>
public static class RswLighting
{
    public static (float[] Dir, float[] Ambient, float[] Diffuse) Compute(RswLight light)
    {
        float lat = light.Latitude * (MathF.PI / 180f);
        float lon = light.Longitude * (MathF.PI / 180f);
        float[] dir =
        {
            -MathF.Sin(lon) * MathF.Sin(lat),
            MathF.Cos(lat),
            -MathF.Cos(lon) * MathF.Sin(lat),
        };
        float[] amb = { light.AmbientRed, light.AmbientGreen, light.AmbientBlue };
        float[] dif = { light.DiffuseRed, light.DiffuseGreen, light.DiffuseBlue };
        return (dir, amb, dif);
    }
}

/// <summary>Builds the animated water surface: a subdivided grid over the map at the water level, plus the
/// cycling water{Type}NN.jpg frames. Wave displacement + texture cycling happen in the viewer.</summary>
public static class WaterBuilder
{
    private const string WaterDir = "¿öÅÍ"; // RO water-texture folder (Win-1252 of 워터)

    // RO water-texture folder = "워터" stored as EUC-KR, read back as Win-1252 → these four bytes/chars.
    // Built from char codes so it never depends on the source file's encoding.
    private static readonly string Folder = new(new[] { (char)0xBF, (char)0xF6, (char)0xC5, (char)0xCD });

    public static MapWater? Build(float[] min, float[] max, RswWater w, Func<string, ModelTexture?> texLoader)
    {
        var frames = new List<ModelTexture>();
        for (int f = 0; f < 32; f++)
        {
            // try the common naming variants: water0NN, water00, water0_NN
            var img = texLoader($"{Folder}\\water{w.Type}{f:00}.jpg")
                   ?? texLoader($"{Folder}\\water{w.Type}_{f:00}.jpg")
                   ?? texLoader($"{Folder}\\water{w.Type}{f}.jpg");
            if (img is not null) frames.Add(img);
        }
        if (frames.Count == 0) return null;

        float x0 = min[0], x1 = max[0], z0 = min[2], z1 = max[2];
        if (x1 - x0 < 1f || z1 - z0 < 1f) return null;

        const int n = 80;          // grid resolution (enough for smooth waves)
        const float uv = 1f / 80f; // tile the texture every ~80 world units (~8 cells)
        float y = -w.Level;
        var v = new List<float>(n * n * 6 * MapWater.Stride);
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            float ax = Lerp(x0, x1, i / (float)n), bx = Lerp(x0, x1, (i + 1) / (float)n);
            float az = Lerp(z0, z1, j / (float)n), bz = Lerp(z0, z1, (j + 1) / (float)n);
            Add(v, ax, y, az, uv); Add(v, bx, y, az, uv); Add(v, bx, y, bz, uv);
            Add(v, ax, y, az, uv); Add(v, bx, y, bz, uv); Add(v, ax, y, bz, uv);
        }
        return new MapWater
        {
            Vertices = v.ToArray(),
            WaveHeight = w.WaveHeight, WaveSpeed = w.WaveSpeed, WavePitch = w.WavePitch,
            TextureCycling = w.TextureCycling <= 0 ? 3 : w.TextureCycling,
            Frames = frames,
        };
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static void Add(List<float> v, float x, float y, float z, float uv)
    {
        v.Add(x); v.Add(y); v.Add(z); v.Add(x * uv); v.Add(z * uv);
    }
}
