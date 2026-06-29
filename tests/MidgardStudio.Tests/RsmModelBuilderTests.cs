using System.Text;
using MidgardStudio.Core.Workspace;
using MidgardStudio.Grf;
using OpenTK.Mathematics;

namespace MidgardStudio.Tests;

// Real-data smoke test: bakes RSM models straight from data.grf and checks the geometry is sane
// (stride intact, whole triangles, finite non-degenerate bbox that actually contains the verts,
// unit-length normals). Skips when the GRF isn't present (CI without repo data).
public class RsmModelBuilderTests
{
    static RsmModelBuilderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static string? FindGrf()
    {
        foreach (var name in new[] { "data.grf", "official_data.grf" })
        {
            var p = Path.Combine(WorkspaceConfigService.DefaultRepoRoot, "grfs", name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public void Builds_sane_geometry_from_real_rsm_models()
    {
        var grf = FindGrf();
        if (grf is null) return;

        using var svc = new GrfService();
        svc.SetDisplayCodepage(1252);
        svc.OpenBrowseSource(grf);

        var rsms = svc.BrowseEntries()
            .Where(e => e.EndsWith(".rsm", StringComparison.OrdinalIgnoreCase) ||
                        e.EndsWith(".rsm2", StringComparison.OrdinalIgnoreCase))
            .Take(40).ToList();
        if (rsms.Count == 0) return;

        int built = 0;
        foreach (var path in rsms)
        {
            var geo = svc.BuildModel(path);
            if (geo is null) continue; // unreadable entry — skip, don't fail the whole sweep
            built++;

            Assert.NotEmpty(geo.AllSubmeshes);
            Assert.True(geo.TotalVertices > 0, $"{path}: no vertices");
            Assert.True(geo.Radius > 0 && float.IsFinite(geo.Radius), $"{path}: bad radius {geo.Radius}");
            for (int i = 0; i < 3; i++)
            {
                Assert.True(float.IsFinite(geo.Min[i]) && float.IsFinite(geo.Max[i]), $"{path}: non-finite bbox");
                Assert.True(geo.Max[i] >= geo.Min[i], $"{path}: inverted bbox axis {i}");
            }

            // verts are local-space; normals (also local) are unit-length, geometry is whole triangles
            foreach (var s in geo.AllSubmeshes)
            {
                Assert.Equal(0, s.Vertices.Length % ModelSubmesh.Stride); // interleaved stride intact
                Assert.True(s.VertexCount % 3 == 0, $"{path}: vertices are not whole triangles");
                for (int o = 0; o < s.Vertices.Length; o += ModelSubmesh.Stride)
                {
                    float nx = s.Vertices[o + 3], ny = s.Vertices[o + 4], nz = s.Vertices[o + 5];
                    Assert.InRange(MathF.Sqrt(nx * nx + ny * ny + nz * nz), 0.9f, 1.1f);
                }
            }

            // bbox bounds the WORLD-transformed (rest-pose) verts — local verts must map inside it
            var rest = RsmPose.ComputeWorldMatrices(
                geo.Meshes.Select(m => m.Transform).ToList(), geo.Version, geo.AnimationLength, 0.0);
            for (int mi = 0; mi < geo.Meshes.Count; mi++)
                foreach (var s in geo.Meshes[mi].Submeshes)
                    for (int o = 0; o < s.Vertices.Length; o += ModelSubmesh.Stride)
                    {
                        var p = Vector3.TransformPosition(new Vector3(s.Vertices[o], s.Vertices[o + 1], s.Vertices[o + 2]), rest[mi]);
                        Assert.InRange(p.X, geo.Min[0] - 1f, geo.Max[0] + 1f);
                        Assert.InRange(p.Y, geo.Min[1] - 1f, geo.Max[1] + 1f);
                        Assert.InRange(p.Z, geo.Min[2] - 1f, geo.Max[2] + 1f);
                    }
        }

        Assert.True(built > 0, "no RSM models could be built from the GRF");
    }

    [Fact]
    public void Animation_moves_a_mesh_over_time()
    {
        var grf = FindGrf();
        if (grf is null) return;

        using var svc = new GrfService();
        svc.SetDisplayCodepage(1252);
        svc.OpenBrowseSource(grf);

        // find an animated model (keyframes present) — they're rarer, so scan a wide slice
        ModelGeometry? animated = null;
        int scanned = 0;
        foreach (var e in svc.BrowseEntries())
        {
            if (!e.EndsWith(".rsm", StringComparison.OrdinalIgnoreCase) &&
                !e.EndsWith(".rsm2", StringComparison.OrdinalIgnoreCase)) continue;
            if (scanned++ >= 1500) break;
            var g = svc.BuildModel(e);
            if (g is { IsAnimated: true } && g.AnimationLength > 0) { animated = g; break; }
        }
        if (animated is null) return; // GRF without animated models — skip

        var transforms = animated.Meshes.Select(m => m.Transform).ToList();
        Matrix4[] a = RsmPose.ComputeWorldMatrices(transforms, animated.Version, animated.AnimationLength, 0.0);
        Matrix4[] b = RsmPose.ComputeWorldMatrices(transforms, animated.Version, animated.AnimationLength, animated.AnimationLength * 0.4);

        Assert.Equal(transforms.Count, a.Length);
        foreach (var m in a) Assert.True(IsFinite(m), "rest-pose matrix not finite");
        foreach (var m in b) Assert.True(IsFinite(m), "animated matrix not finite");

        // at least one mesh must actually move between the two times
        bool moved = false;
        for (int i = 0; i < a.Length && !moved; i++) moved = Differs(a[i], b[i]);
        Assert.True(moved, "animation produced no movement between t=0 and t=0.4·len");
    }

    private static bool IsFinite(Matrix4 m)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (!float.IsFinite(m[r, c])) return false;
        return true;
    }

    private static bool Differs(Matrix4 a, Matrix4 b)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (MathF.Abs(a[r, c] - b[r, c]) > 1e-4f) return true;
        return false;
    }
}
