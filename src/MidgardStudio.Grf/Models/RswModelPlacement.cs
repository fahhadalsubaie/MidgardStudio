using System.Linq;
using GRF.FileFormats.GndFormat;
using GRF.FileFormats.RswFormat;
using GRF.FileFormats.RswFormat.RswObjects;
using OpenTK.Mathematics;

namespace MidgardStudio.Grf;

/// <summary>
/// Places RSW model objects onto the GND terrain: groups objects by model (built once each), and computes
/// a per-instance world matrix that aligns the model with the terrain. Ported from GRFEditor's ModelRenderer
/// (CalculateCachedMatrix): global Scale(1,1,-1) → Translate(5·W+X, −Y, −10−5·H+Z) → rot Z(−),X(−),Y(+) →
/// Scale(X,−Y,Z) → v-split flip/centre. Composed with the model's un-flipped rest pose in the viewer.
/// </summary>
public static class RswModelPlacement
{
    public static List<MapModelInstance> Build(Rsw rsw, Gnd gnd, Func<string, ModelGeometry?> modelBuilder)
    {
        var result = new List<MapModelInstance>();
        var groups = rsw.Objects
            .Where(o => o.Type == RswObjectType.Model)
            .Cast<Model>()
            .GroupBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            if (string.IsNullOrEmpty(g.Key)) continue;
            var geo = modelBuilder("data\\model\\" + g.Key.Replace('/', '\\'));
            if (geo is null || geo.TotalVertices == 0) continue;

            var transforms = g.Select(m => Flatten(Placement(gnd, geo, m))).ToList();
            if (transforms.Count > 0)
                result.Add(new MapModelInstance { Model = geo, Transforms = transforms });
        }
        return result;
    }

    private static Matrix4 Placement(Gnd gnd, ModelGeometry geo, Model obj)
    {
        int w = gnd.Header.Width, h = gnd.Header.Height;
        var p = obj.Position; var r = obj.Rotation; var s = obj.Scale;

        // GLHelper ops are pre-multiply (op * m); build in the same order.
        Matrix4 m = Matrix4.CreateScale(1f, 1f, -1f);
        m = Matrix4.CreateTranslation(5f * w + p.X, -p.Y, -10f - 5f * h + p.Z) * m;
        m = Matrix4.CreateFromAxisAngle(Vector3.UnitZ, -Rad(r.Z)) * m;
        m = Matrix4.CreateFromAxisAngle(Vector3.UnitX, -Rad(r.X)) * m;
        m = Matrix4.CreateFromAxisAngle(Vector3.UnitY, Rad(r.Y)) * m;
        m = Matrix4.CreateScale(s.X, -s.Y, s.Z) * m;
        if (geo.Version < 2.2)
            m = Matrix4.CreateTranslation(-geo.Center[0], geo.Min[1], -geo.Center[2]) * m;
        else
            m = Matrix4.CreateScale(1f, -1f, 1f) * m;
        return m;
    }

    private static float Rad(float deg) => deg * (MathF.PI / 180f);

    private static float[] Flatten(Matrix4 m) => new[]
    {
        m.Row0.X, m.Row0.Y, m.Row0.Z, m.Row0.W,
        m.Row1.X, m.Row1.Y, m.Row1.Z, m.Row1.W,
        m.Row2.X, m.Row2.Y, m.Row2.Z, m.Row2.W,
        m.Row3.X, m.Row3.Y, m.Row3.Z, m.Row3.W,
    };
}
