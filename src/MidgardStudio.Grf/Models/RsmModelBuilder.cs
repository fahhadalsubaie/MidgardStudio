using System.Linq;
using GRF.FileFormats.RsmFormat;
using OpenTK.Mathematics;
using Tk = GRF.Graphics;

namespace MidgardStudio.Grf;

/// <summary>
/// Bakes a parsed <see cref="Rsm"/> into <see cref="ModelGeometry"/>: per-mesh local-space vertices
/// grouped by texture, ShadeType-aware normals, plus each node's transform + keyframes. World placement
/// and animation are deferred to <see cref="RsmPose"/> (so node animation just recomputes matrices, not
/// geometry). Ported from GRFEditor's RSM renderer.
/// </summary>
public static class RsmModelBuilder
{
    public static ModelGeometry Build(Rsm rsm, Func<string, ModelTexture?>? textureLoader = null)
    {
        var indexOf = new Dictionary<Mesh, int>();
        var nameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < rsm.Meshes.Count; i++)
        {
            indexOf[rsm.Meshes[i]] = i;
            nameToIndex.TryAdd(rsm.Meshes[i].Name ?? string.Empty, i);
        }

        var meshes = new List<ModelMesh>(rsm.Meshes.Count);
        for (int i = 0; i < rsm.Meshes.Count; i++)
            meshes.Add(BuildMesh(rsm.Meshes[i], i, rsm, indexOf, nameToIndex, textureLoader));

        // Rest-pose world matrices (frame 0) → measure the bounding box for camera framing.
        var transforms = meshes.Select(mm => mm.Transform).ToList();
        Matrix4[] rest = RsmPose.ComputeWorldMatrices(transforms, rsm.Version, rsm.AnimationLength, 0.0);

        var bbox = new BBox();
        for (int i = 0; i < meshes.Count; i++)
            foreach (var sm in meshes[i].Submeshes)
                for (int o = 0; o < sm.Vertices.Length; o += ModelSubmesh.Stride)
                    bbox.Add(Vector3.TransformPosition(new Vector3(sm.Vertices[o], sm.Vertices[o + 1], sm.Vertices[o + 2]), rest[i]));

        Vector3 min = bbox.Any ? bbox.Min : Vector3.Zero;
        Vector3 max = bbox.Any ? bbox.Max : Vector3.Zero;
        Vector3 center = (min + max) * 0.5f;
        float radius = bbox.Any ? (max - center).Length : 1f;
        if (radius < 1e-3f) radius = 1f;

        return new ModelGeometry
        {
            Meshes = meshes,
            Center = new[] { center.X, center.Y, center.Z },
            Radius = radius,
            Min = new[] { min.X, min.Y, min.Z },
            Max = new[] { max.X, max.Y, max.Z },
            ShadeType = rsm.ShadeType,
            Version = rsm.Version,
            AnimationLength = rsm.AnimationLength,
            Fps = rsm.FrameRatePerSecond,
            IsAnimated = meshes.Any(mm => mm.Transform.HasKeyframes),
        };
    }

    private static ModelMesh BuildMesh(Mesh mesh, int selfIndex, Rsm rsm, IReadOnlyDictionary<Mesh, int> indexOf,
                                       IReadOnlyDictionary<string, int> nameToIndex, Func<string, ModelTexture?>? loader)
    {
        var groups = new Dictionary<int, List<float>>(); // global texture index -> interleaved local verts
        int vn = mesh.Vertices.Count, fc = mesh.Faces.Count;

        if (vn > 0 && fc > 0)
        {
            // local-space vertices + flat face normals
            var faceN = new Vector3[fc];
            for (int fi = 0; fi < fc; fi++)
            {
                var f = mesh.Faces[fi];
                Vector3 a = LV(mesh, f.VertexIds[0]), b = LV(mesh, f.VertexIds[1]), c = LV(mesh, f.VertexIds[2]);
                Vector3 nrm = Vector3.Cross(b - a, c - a);
                faceN[fi] = nrm.LengthSquared > 1e-10f ? Vector3.Normalize(nrm) : Vector3.UnitY;
            }

            // per-face-vertex normals — flat for ShadeType 0/1, smoothgroup-averaged otherwise (2 by group)
            var vnorm = new Vector3[fc * 3];
            int shade = rsm.ShadeType;
            if (shade is 0 or 1)
            {
                for (int fi = 0; fi < fc; fi++)
                    for (int ii = 0; ii < 3; ii++) vnorm[fi * 3 + ii] = faceN[fi];
            }
            else
            {
                var acc = new Dictionary<(int Group, int Vid), Vector3>();
                for (int fi = 0; fi < fc; fi++)
                {
                    var f = mesh.Faces[fi];
                    for (int ii = 0; ii < 3; ii++)
                    {
                        var key = (shade == 2 ? f.SmoothGroup[ii] : 0, (int)f.VertexIds[ii]);
                        acc[key] = (acc.TryGetValue(key, out var s) ? s : Vector3.Zero) + faceN[fi];
                    }
                }
                for (int fi = 0; fi < fc; fi++)
                {
                    var f = mesh.Faces[fi];
                    for (int ii = 0; ii < 3; ii++)
                    {
                        var s = acc[(shade == 2 ? f.SmoothGroup[ii] : 0, (int)f.VertexIds[ii])];
                        vnorm[fi * 3 + ii] = s.LengthSquared > 1e-10f ? Vector3.Normalize(s) : faceN[fi];
                    }
                }
            }

            int tvCount = mesh.TextureVertices.Count;
            for (int fi = 0; fi < fc; fi++)
            {
                var f = mesh.Faces[fi];
                int gtex = f.TextureId < mesh.TextureIndexes.Count ? mesh.TextureIndexes[f.TextureId] : 0;
                if (!groups.TryGetValue(gtex, out var list)) groups[gtex] = list = new List<float>(fc * 3 * ModelSubmesh.Stride);
                for (int ii = 0; ii < 3; ii++)
                {
                    Vector3 p = LV(mesh, f.VertexIds[ii]);
                    Vector3 nr = vnorm[fi * 3 + ii];
                    int tvi = f.TextureVertexIds[ii];
                    float u = 0f, v = 0f;
                    if (tvi < tvCount) { var tv = mesh.TextureVertices[tvi]; u = tv.U; v = tv.V; }
                    list.Add(p.X); list.Add(p.Y); list.Add(p.Z);
                    list.Add(nr.X); list.Add(nr.Y); list.Add(nr.Z);
                    list.Add(u); list.Add(v);
                }
            }
        }

        var subs = new List<ModelSubmesh>(groups.Count);
        foreach (var kv in groups)
        {
            string tex = kv.Key >= 0 && kv.Key < rsm.Textures.Count ? rsm.Textures[kv.Key] : string.Empty;
            var pix = tex.Length > 0 ? loader?.Invoke(tex) : null;
            subs.Add(new ModelSubmesh { TextureName = tex, Texture = pix, Vertices = kv.Value.ToArray() });
        }

        return new ModelMesh { Submeshes = subs, Transform = BuildTransform(mesh, selfIndex, indexOf, nameToIndex) };
    }

    private static MeshTransform BuildTransform(Mesh mesh, int selfIndex, IReadOnlyDictionary<Mesh, int> indexOf,
                                                IReadOnlyDictionary<string, int> nameToIndex)
    {
        var t = mesh.TransformationMatrix;
        int parent = -1;
        if (mesh.Parent is not null && indexOf.TryGetValue(mesh.Parent, out int pi)) parent = pi;
        else if (!string.IsNullOrEmpty(mesh.ParentName) && nameToIndex.TryGetValue(mesh.ParentName, out int ni) && ni != selfIndex) parent = ni;

        return new MeshTransform
        {
            ParentIndex = parent,
            Transformation = new[]
            {
                t.Row0.X, t.Row0.Y, t.Row0.Z,
                t.Row1.X, t.Row1.Y, t.Row1.Z,
                t.Row2.X, t.Row2.Y, t.Row2.Z,
            },
            LocalPosition = new[] { mesh.LocalPosition.X, mesh.LocalPosition.Y, mesh.LocalPosition.Z },
            GlobalPosition = new[] { mesh.GlobalPosition.X, mesh.GlobalPosition.Y, mesh.GlobalPosition.Z },
            GlobalRotationAngle = mesh.GlobalRotationAngle,
            GlobalRotationAxis = new[] { mesh.GlobalRotationAxis.X, mesh.GlobalRotationAxis.Y, mesh.GlobalRotationAxis.Z },
            GlobalScale = new[] { mesh.GlobalScale.X, mesh.GlobalScale.Y, mesh.GlobalScale.Z },
            RotationFrames = mesh.RotationKeyFrames
                .Select(k => new RotKey(k.Frame, k.Quaternion.X, k.Quaternion.Y, k.Quaternion.Z, k.Quaternion.W)).ToArray(),
            PositionFrames = mesh.PosKeyFrames
                .Select(k => new PosKey(k.Frame, k.Position.X, k.Position.Y, k.Position.Z)).ToArray(),
            ScaleFrames = mesh.ScaleKeyFrames
                .Select(k => new ScaleKey(k.Frame, k.Scale.X, k.Scale.Y, k.Scale.Z)).ToArray(),
        };
    }

    // A corrupt/hostile .rsm can carry a face vertex index past the mesh's vertex count (the parser doesn't
    // clamp it); treat an out-of-range index as the origin instead of throwing, so a bad model degrades to a
    // skewed preview rather than aborting the (try/caught) build.
    private static Vector3 LV(Mesh mesh, int i)
    {
        if ((uint)i >= (uint)mesh.Vertices.Count) return Vector3.Zero;
        var v = mesh.Vertices[i];
        return new Vector3(v.X, v.Y, v.Z);
    }

    private sealed class BBox
    {
        public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
        public bool Any;
        public void Add(Vector3 v) { Min = Vector3.ComponentMin(Min, v); Max = Vector3.ComponentMax(Max, v); Any = true; }
    }
}
