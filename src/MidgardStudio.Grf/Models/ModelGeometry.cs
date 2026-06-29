using System.Linq;

namespace MidgardStudio.Grf;

/// <summary>
/// GL-ready geometry baked from an RSM model: a list of meshes, each holding local-space vertices
/// grouped by texture plus the transform/keyframe data needed to place and animate it. Vertices stay
/// in mesh-local space (no world bake) so <see cref="RsmPose"/> can compute per-mesh world matrices at
/// any animation time. Plain data only — no GRF or GL types leak out.
/// </summary>
public sealed class ModelGeometry
{
    public IReadOnlyList<ModelMesh> Meshes { get; init; } = Array.Empty<ModelMesh>();

    /// <summary>Bounding-box centre (x,y,z) of the rest pose in world space — the camera orbits this.</summary>
    public float[] Center { get; init; } = new float[3];

    /// <summary>Bounding-sphere radius (half the box diagonal) — used to frame the model on load.</summary>
    public float Radius { get; init; }

    public float[] Min { get; init; } = new float[3];
    public float[] Max { get; init; } = new float[3];

    public int ShadeType { get; init; }
    public double Version { get; init; }

    /// <summary>Animation length in the RSM frame unit (keyframe <c>Frame</c> values run 0..this).</summary>
    public int AnimationLength { get; init; }
    public float Fps { get; init; }

    /// <summary>True when any mesh carries rotation/position/scale keyframes.</summary>
    public bool IsAnimated { get; init; }

    public IEnumerable<ModelSubmesh> AllSubmeshes => Meshes.SelectMany(m => m.Submeshes);

    public int TotalVertices
    {
        get { int n = 0; foreach (var m in Meshes) foreach (var s in m.Submeshes) n += s.VertexCount; return n; }
    }

    /// <summary>
    /// Free the CPU-side vertex/texture pixel arrays once they've been uploaded to GPU buffers/textures.
    /// They're never read again (the viewer re-uploads only from a freshly built geometry), so keeping
    /// them alive in this singleton-held DTO is what lets the 3D preview pile memory into the heap.
    /// Transforms/keyframes (animation needs them) and bbox scalars (camera needs them) are kept.
    /// </summary>
    public void ReleaseCpuData()
    {
        foreach (var mesh in Meshes)
            foreach (var sm in mesh.Submeshes)
            {
                sm.Vertices = Array.Empty<float>();
                if (sm.Texture is { } t) t.Bgra = Array.Empty<byte>();
            }
    }
}

/// <summary>One RSM node: its texture-grouped local geometry plus its transform/keyframes.</summary>
public sealed class ModelMesh
{
    public IReadOnlyList<ModelSubmesh> Submeshes { get; init; } = Array.Empty<ModelSubmesh>();
    public MeshTransform Transform { get; init; } = new();
}

/// <summary>A node's static transform and animation keyframes — consumed by <see cref="RsmPose"/>.</summary>
public sealed class MeshTransform
{
    /// <summary>Index into <see cref="ModelGeometry.Meshes"/> of the parent node, or -1 for a root.</summary>
    public int ParentIndex { get; init; } = -1;

    /// <summary>The 3×3 rotation/scale matrix, row-major (9 floats).</summary>
    public float[] Transformation { get; init; } = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
    public float[] LocalPosition { get; init; } = new float[3];
    public float[] GlobalPosition { get; init; } = new float[3];

    // RSM1 static base rotation/scale (usually identity).
    public float GlobalRotationAngle { get; init; }
    public float[] GlobalRotationAxis { get; init; } = { 0, 0, 1 };
    public float[] GlobalScale { get; init; } = { 1, 1, 1 };

    public RotKey[] RotationFrames { get; init; } = Array.Empty<RotKey>();
    public PosKey[] PositionFrames { get; init; } = Array.Empty<PosKey>();
    public ScaleKey[] ScaleFrames { get; init; } = Array.Empty<ScaleKey>();

    public bool HasKeyframes => RotationFrames.Length > 0 || PositionFrames.Length > 0 || ScaleFrames.Length > 0;
}

public readonly record struct RotKey(int Frame, float X, float Y, float Z, float W);
public readonly record struct PosKey(int Frame, float X, float Y, float Z);
public readonly record struct ScaleKey(int Frame, float X, float Y, float Z);

/// <summary>One texture group within a mesh: interleaved local-space vertices, 8 floats each — pos(3) normal(3) uv(2).</summary>
public sealed class ModelSubmesh
{
    public const int Stride = 8; // floats per vertex: pos.xyz, normal.xyz, uv.xy

    /// <summary>Raw RSM texture name for this group.</summary>
    public string TextureName { get; init; } = string.Empty;

    /// <summary>Decoded texture pixels (BGRA, magenta-keyed), or null when it couldn't be loaded (render solid).</summary>
    public ModelTexture? Texture { get; init; }

    /// <summary>Interleaved vertex data, length is a multiple of <see cref="Stride"/>.
    /// Settable so the viewer can drop it after GPU upload — see <see cref="ModelGeometry.ReleaseCpuData"/>.</summary>
    public float[] Vertices { get; set; } = Array.Empty<float>();

    /// <summary>Vertices.Length / Stride. Reads 0 after <see cref="ModelGeometry.ReleaseCpuData"/>; the GL
    /// viewer captures its draw count at upload time, so the released array never affects rendering.</summary>
    public int VertexCount => Vertices.Length / Stride;
}

/// <summary>A decoded model texture: top-row-first BGRA pixels ready for GL upload.</summary>
public sealed class ModelTexture
{
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>BGRA, length = Width*Height*4. Magenta (RO color key) is mapped to alpha 0.
    /// Settable so the viewer can drop it after GPU upload — see <see cref="ModelGeometry.ReleaseCpuData"/>.</summary>
    public byte[] Bgra { get; set; } = Array.Empty<byte>();
}
