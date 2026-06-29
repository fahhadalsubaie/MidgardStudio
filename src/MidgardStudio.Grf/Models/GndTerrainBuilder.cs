using System.Linq;
using GRF.FileFormats.GndFormat;
using OpenTK.Mathematics;

namespace MidgardStudio.Grf;

/// <summary>
/// Bakes a GND ground into <see cref="MapGeometry"/> terrain: heightmap top surfaces + front/side cliff walls,
/// grouped by texture, with UVs from each tile and the baked lightmap shadow folded into per-vertex colour.
/// Ported from GRFEditor's GndRenderer (positions: X=10·x, Y=−height, Z=10·(H−y); bottom corners at +10).
/// </summary>
public static class GndTerrainBuilder
{
    private const float Cell = 10f; // GRFEditor's fixed cell size

    public static MapGeometry Build(Gnd gnd, Func<string, ModelTexture?>? textureLoader = null)
    {
        int w = gnd.Header.Width, h = gnd.Header.Height;
        int lw = gnd.LightmapWidth <= 0 ? 8 : gnd.LightmapWidth;
        int lh = gnd.LightmapHeight <= 0 ? 8 : gnd.LightmapHeight;
        var groups = new Dictionary<int, List<float>>();
        var bbox = new BBox();

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = gnd[x, y];
            if (c is null) continue;
            float z0 = Cell * h - Cell * y; // north edge z; south edge = z0 + Cell

            // ---- TOP (ground) ----
            if (c.TileUp >= 0 && c.TileUp < gnd.Tiles.Count)
            {
                var t = gnd.Tiles[c.TileUp];
                Vector3 nw = new(Cell * x, -H(c, 2), z0);
                Vector3 ne = new(Cell * x + Cell, -H(c, 3), z0);
                Vector3 sw = new(Cell * x, -H(c, 0), z0 + Cell);
                Vector3 se = new(Cell * x + Cell, -H(c, 1), z0 + Cell);
                var (cNW, cNE, cSW, cSE) = (Shade(gnd, t, lw, lh, 2), Shade(gnd, t, lw, lh, 3), Shade(gnd, t, lw, lh, 0), Shade(gnd, t, lw, lh, 1));
                var list = Group(groups, t.TextureIndex);
                // triangles (se,ne,nw) + (se,nw,sw)
                Push(list, se, UV(t, 1), cSE, bbox); Push(list, ne, UV(t, 3), cNE, bbox); Push(list, nw, UV(t, 2), cNW, bbox);
                Push(list, se, UV(t, 1), cSE, bbox); Push(list, nw, UV(t, 2), cNW, bbox); Push(list, sw, UV(t, 0), cSW, bbox);
            }

            // ---- FRONT wall (south, to cell y+1) ----
            var nbF = gnd[x, y + 1];
            if (c.TileFront >= 0 && c.TileFront < gnd.Tiles.Count && nbF is not null)
            {
                var t = gnd.Tiles[c.TileFront];
                Vector3 v1 = new(Cell * x, -H(c, 2), z0);          // top-left   uv[0]
                Vector3 v2 = new(Cell * x + Cell, -H(c, 3), z0);   // top-right  uv[1]
                Vector3 v3 = new(Cell * x, -H(nbF, 0), z0);        // bottom-left uv[2]
                Vector3 v4 = new(Cell * x + Cell, -H(nbF, 1), z0); // bottom-right uv[3]
                Vector3 col = Shade(gnd, t, lw, lh, 0);
                var list = Group(groups, t.TextureIndex);
                Push(list, v1, UV(t, 0), col, bbox); Push(list, v2, UV(t, 1), col, bbox); Push(list, v3, UV(t, 2), col, bbox);
                Push(list, v3, UV(t, 2), col, bbox); Push(list, v2, UV(t, 1), col, bbox); Push(list, v4, UV(t, 3), col, bbox);
            }

            // ---- SIDE wall (east, to cell x+1) ----
            var nbS = gnd[x + 1, y];
            if (c.TileSide >= 0 && c.TileSide < gnd.Tiles.Count && nbS is not null)
            {
                var t = gnd.Tiles[c.TileSide];
                Vector3 v1 = new(Cell * x + Cell, -H(c, 1), z0 + Cell); // uv[1]
                Vector3 v2 = new(Cell * x + Cell, -H(c, 3), z0);        // uv[0]
                Vector3 v3 = new(Cell * x + Cell, -H(nbS, 0), z0 + Cell); // uv[3]
                Vector3 v4 = new(Cell * x + Cell, -H(nbS, 2), z0);       // uv[2]
                Vector3 col = Shade(gnd, t, lw, lh, 0);
                var list = Group(groups, t.TextureIndex);
                Push(list, v1, UV(t, 1), col, bbox); Push(list, v3, UV(t, 3), col, bbox); Push(list, v4, UV(t, 2), col, bbox);
                Push(list, v4, UV(t, 2), col, bbox); Push(list, v2, UV(t, 0), col, bbox); Push(list, v1, UV(t, 1), col, bbox);
            }
        }

        var subs = new List<MapSubmesh>(groups.Count);
        foreach (var kv in groups)
        {
            string tex = kv.Key >= 0 && kv.Key < gnd.Textures.Count ? gnd.Textures[kv.Key] : string.Empty;
            var pix = tex.Length > 0 ? textureLoader?.Invoke(tex) : null;
            subs.Add(new MapSubmesh { TextureName = tex, Texture = pix, Vertices = kv.Value.ToArray() });
        }

        Vector3 min = bbox.Any ? bbox.Min : Vector3.Zero;
        Vector3 max = bbox.Any ? bbox.Max : Vector3.Zero;
        Vector3 center = (min + max) * 0.5f;
        float radius = bbox.Any ? (max - center).Length : 1f;
        if (radius < 1e-3f) radius = 1f;

        return new MapGeometry
        {
            Terrain = subs,
            Center = new[] { center.X, center.Y, center.Z },
            Radius = radius,
            Min = new[] { min.X, min.Y, min.Z },
            Max = new[] { max.X, max.Y, max.Z },
        };
    }

    // corner height: 0=BottomLeft 1=BottomRight 2=TopLeft 3=TopRight
    private static float H(Cube c, int i) => i switch
    {
        0 => c.BottomLeft, 1 => c.BottomRight, 2 => c.TopLeft, _ => c.TopRight,
    };

    private static Vector2 UV(Tile t, int i) => new(t.TexCoords[i].X, t.TexCoords[i].Y);

    private static List<float> Group(Dictionary<int, List<float>> groups, int tex)
    {
        if (!groups.TryGetValue(tex, out var list)) groups[tex] = list = new List<float>(1024);
        return list;
    }

    private static void Push(List<float> list, Vector3 p, Vector2 uv, Vector3 col, BBox bb)
    {
        list.Add(p.X); list.Add(p.Y); list.Add(p.Z);
        list.Add(uv.X); list.Add(uv.Y);
        list.Add(col.X); list.Add(col.Y); list.Add(col.Z);
        bb.Add(p);
    }

    // tile tint × baked lightmap shadow at the given corner (0=BL 1=BR 2=TL 3=TR)
    private static Vector3 Shade(Gnd gnd, Tile t, int lw, int lh, int corner)
    {
        float r = t.TileColor.R / 255f, g = t.TileColor.G / 255f, b = t.TileColor.B / 255f;
        float shadow = LightmapShadow(gnd, t.LightmapIndex, lw, lh, corner);
        return new Vector3(r * shadow, g * shadow, b * shadow);
    }

    private static float LightmapShadow(Gnd gnd, int lightmapIndex, int lw, int lh, int corner)
    {
        if (lightmapIndex < 0 || lightmapIndex >= gnd.Lightmaps.Count) return 1f;
        var d = gnd.Lightmaps[lightmapIndex];
        int per = lw * lh;
        if (d is null || d.Length < per) return 1f;
        // map the tile's UV corner to a lightmap grid corner (shadow/alpha channel = first `per` bytes)
        int col = corner is 0 or 2 ? 0 : lw - 1;        // BL/TL = left, BR/TR = right
        int row = corner is 0 or 1 ? 0 : lh - 1;        // BL/BR = bottom, TL/TR = top
        int idx = row * lw + col;
        if (idx < 0 || idx >= per) idx = 0;
        return d[idx] / 255f;
    }

    private sealed class BBox
    {
        public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
        public bool Any;
        public void Add(Vector3 v) { Min = Vector3.ComponentMin(Min, v); Max = Vector3.ComponentMax(Max, v); Any = true; }
    }
}
