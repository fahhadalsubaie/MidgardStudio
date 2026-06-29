using System.Linq;

namespace MidgardStudio.Grf;

/// <summary>
/// GL-ready geometry for a 3D map preview: the GND ground terrain (per-texture submeshes), and — added in
/// later slices — placed RSM model instances and water. Plain data only; no GRF or GL types leak out.
/// </summary>
public sealed class MapGeometry
{
    /// <summary>Per-texture terrain submeshes (heightmap surfaces + cliff walls).</summary>
    public IReadOnlyList<MapSubmesh> Terrain { get; init; } = Array.Empty<MapSubmesh>();

    /// <summary>Placed RSM model instances (MAP-2): a built model + the world matrices it's drawn at.</summary>
    public IReadOnlyList<MapModelInstance> Models { get; init; } = Array.Empty<MapModelInstance>();

    public float[] Center { get; init; } = new float[3];
    public float Radius { get; init; }
    public float[] Min { get; init; } = new float[3];
    public float[] Max { get; init; } = new float[3];

    // RSW directional light (computed from latitude/longitude); neutral defaults for .gnd-only maps.
    public float[] LightDir { get; init; } = { 0f, 1f, 0f };
    public float[] Ambient { get; init; } = { 1f, 1f, 1f };
    public float[] Diffuse { get; init; } = { 1f, 1f, 1f };

    /// <summary>Animated water surface, or null when the map has no water.</summary>
    public MapWater? Water { get; init; }

    public int TerrainVertices
    {
        get { int n = 0; foreach (var s in Terrain) n += s.VertexCount; return n; }
    }

    /// <summary>
    /// Free the CPU-side terrain/model/water vertex + texture arrays once they've been uploaded to the GPU.
    /// A single map preview is tens to hundreds of MB of these arrays; held in the singleton VM's MapData
    /// they pile into the heap as the user browses. Bbox scalars + lighting are kept (camera/shaders need them).
    /// </summary>
    public void ReleaseCpuData()
    {
        foreach (var s in Terrain)
        {
            s.Vertices = Array.Empty<float>();
            if (s.Texture is { } t) t.Bgra = Array.Empty<byte>();
        }
        foreach (var inst in Models) inst.Model.ReleaseCpuData();
        if (Water is { } w)
        {
            w.Vertices = Array.Empty<float>();
            foreach (var f in w.Frames) f.Bgra = Array.Empty<byte>();
        }
    }
}

/// <summary>One terrain texture group: interleaved vertices, 8 floats each — pos(3) uv(2) color(3).
/// Color holds the baked lightmap shadow × tile tint.</summary>
public sealed class MapSubmesh
{
    public const int Stride = 8; // pos.xyz, uv.xy, color.rgb

    public string TextureName { get; init; } = string.Empty;
    public ModelTexture? Texture { get; init; }

    /// <summary>Interleaved vertices; settable so the viewer drops it after GPU upload (see <see cref="MapGeometry.ReleaseCpuData"/>).</summary>
    public float[] Vertices { get; set; } = Array.Empty<float>();

    public int VertexCount => Vertices.Length / Stride;
}

/// <summary>A model placed on the map: the baked geometry plus one world matrix per placement (16 floats, row-major).</summary>
public sealed class MapModelInstance
{
    public ModelGeometry Model { get; init; } = new();
    public IReadOnlyList<float[]> Transforms { get; init; } = Array.Empty<float[]>();
}

/// <summary>An animated water surface: a subdivided grid at the water level + the cycling water textures.</summary>
public sealed class MapWater
{
    public const int Stride = 5; // pos.xyz, uv.xy (already at y = -Level; the shader adds the wave offset)

    /// <summary>Interleaved vertices; settable so the viewer drops it after GPU upload (see <see cref="MapGeometry.ReleaseCpuData"/>).</summary>
    public float[] Vertices { get; set; } = Array.Empty<float>();
    public float WaveHeight { get; init; } = 1f;
    public float WaveSpeed { get; init; } = 2f;
    public float WavePitch { get; init; } = 50f;
    public int TextureCycling { get; init; } = 3;

    /// <summary>The animation frames (water{Type}00..NN); cycled by time at <see cref="TextureCycling"/>.</summary>
    public IReadOnlyList<ModelTexture> Frames { get; init; } = Array.Empty<ModelTexture>();

    public int VertexCount => Vertices.Length / Stride;
}
