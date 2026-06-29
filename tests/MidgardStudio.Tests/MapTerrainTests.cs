using System.Text;
using MidgardStudio.Core.Workspace;
using MidgardStudio.Grf;

namespace MidgardStudio.Tests;

// Real-data smoke test: bakes GND terrain straight from data.grf and checks the mesh is sane
// (stride intact, whole triangles, finite bbox that contains the verts, colours in range).
// Skips when the GRF isn't present.
public class MapTerrainTests
{
    static MapTerrainTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
    public void Builds_sane_terrain_from_real_gnd()
    {
        var grf = FindGrf();
        if (grf is null) return;

        using var svc = new GrfService();
        svc.SetDisplayCodepage(1252);
        svc.OpenBrowseSource(grf);

        MapGeometry? map = null;
        int scanned = 0;
        foreach (var e in svc.BrowseEntries())
        {
            if (!e.EndsWith(".gnd", StringComparison.OrdinalIgnoreCase)) continue;
            if (scanned++ >= 300) break;
            var m = svc.BuildMap(e);
            if (m is { TerrainVertices: > 0 }) { map = m; break; }
        }
        if (map is null) return; // GRF without maps — skip

        Assert.NotEmpty(map.Terrain);
        Assert.True(map.TerrainVertices > 0, "no terrain vertices");
        Assert.True(map.Radius > 0 && float.IsFinite(map.Radius), $"bad radius {map.Radius}");
        for (int i = 0; i < 3; i++)
        {
            Assert.True(float.IsFinite(map.Min[i]) && float.IsFinite(map.Max[i]), "non-finite bbox");
            Assert.True(map.Max[i] >= map.Min[i], $"inverted bbox axis {i}");
        }

        foreach (var s in map.Terrain)
        {
            Assert.Equal(0, s.Vertices.Length % MapSubmesh.Stride);
            Assert.True(s.VertexCount % 3 == 0, "terrain is not whole triangles");
            for (int o = 0; o < s.Vertices.Length; o += MapSubmesh.Stride)
            {
                Assert.InRange(s.Vertices[o + 0], map.Min[0] - 1f, map.Max[0] + 1f); // pos within bbox
                Assert.InRange(s.Vertices[o + 1], map.Min[1] - 1f, map.Max[1] + 1f);
                Assert.InRange(s.Vertices[o + 2], map.Min[2] - 1f, map.Max[2] + 1f);
                Assert.True(float.IsFinite(s.Vertices[o + 3]) && float.IsFinite(s.Vertices[o + 4]), "non-finite uv");
                Assert.InRange(s.Vertices[o + 5], -0.01f, 1.01f); // colour channels in [0,1]
                Assert.InRange(s.Vertices[o + 6], -0.01f, 1.01f);
                Assert.InRange(s.Vertices[o + 7], -0.01f, 1.01f);
            }
        }
    }
}
