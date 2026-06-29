using OpenTK.Mathematics;

namespace MidgardStudio.Grf;

/// <summary>
/// Computes per-mesh world matrices for an RSM model at a given animation time, including keyframe
/// interpolation (rotation slerp, position/scale lerp) and the global Y-flip (RSM is Y-down, GL Y-up).
/// Ported from GRFEditor's RsmFormat CalcMatrix1/CalcMatrix2 (row-vector / OpenTK convention).
/// Both the builder (rest pose, frame 0) and the viewer (per render frame) call this.
/// </summary>
public static class RsmPose
{
    /// <param name="frame">Animation time in the RSM frame unit; looped over <paramref name="animationLength"/>.</param>
    public static Matrix4[] ComputeWorldMatrices(IReadOnlyList<MeshTransform> meshes, double version, int animationLength, double frame)
    {
        Matrix4 flip = Matrix4.CreateScale(1f, -1f, 1f);
        var outMats = new Matrix4[meshes.Count];

        if (version >= 2.2)
        {
            var memo = new Matrix4?[meshes.Count];
            for (int i = 0; i < meshes.Count; i++)
                outMats[i] = AccRsm2(meshes, i, animationLength, frame, memo) * flip;
        }
        else
        {
            for (int i = 0; i < meshes.Count; i++)
                outMats[i] = M2Rsm1(meshes[i]) * M1Rsm1(meshes[i], animationLength, frame) * flip;
        }
        return outMats;
    }

    // ---- RSM1 (< 2.2): each mesh placed independently; GlobalPosition is absolute ----

    private static Matrix4 M1Rsm1(MeshTransform m, int animLength, double frame)
    {
        Matrix4 r = Matrix4.CreateTranslation(V(m.GlobalPosition)); // T(GlobalPosition)
        if (m.RotationFrames.Length == 0)
        {
            if (MathF.Abs(m.GlobalRotationAngle) > 0.01f)
                r = Matrix4.CreateFromAxisAngle(SafeAxis(m.GlobalRotationAxis), m.GlobalRotationAngle) * r;
        }
        else
        {
            r = Matrix4.CreateFromQuaternion(SlerpRot(m.RotationFrames, animLength, frame)) * r;
        }
        return ScaleRows(r, V(m.GlobalScale));
    }

    private static Matrix4 M2Rsm1(MeshTransform m) =>
        ToM4(m.Transformation) * Matrix4.CreateTranslation(V(m.LocalPosition));

    // ---- RSM2 (>= 2.2): accumulate Matrix1 up the parent chain ----

    private static Matrix4 AccRsm2(IReadOnlyList<MeshTransform> meshes, int i, int animLength, double frame, Matrix4?[] memo)
    {
        if (memo[i] is { } cached) return cached;
        Matrix4 m1 = M1Rsm2(meshes, i, animLength, frame);
        int p = meshes[i].ParentIndex;
        Matrix4 result = p >= 0 && p < meshes.Count && p != i ? m1 * AccRsm2(meshes, p, animLength, frame, memo) : m1;
        memo[i] = result;
        return result;
    }

    private static Matrix4 M1Rsm2(IReadOnlyList<MeshTransform> meshes, int i, int animLength, double frame)
    {
        var m = meshes[i];
        Matrix4 r = Matrix4.Identity;

        if (m.ScaleFrames.Length > 0) r = ScaleRows(r, LerpScale(m.ScaleFrames, animLength, frame));

        if (m.RotationFrames.Length > 0)
        {
            r *= Matrix4.CreateFromQuaternion(SlerpRot(m.RotationFrames, animLength, frame));
        }
        else
        {
            r *= ToM4(m.Transformation);
            if (m.ParentIndex >= 0 && m.ParentIndex < meshes.Count)
                r *= SafeInvert(ToM4(meshes[m.ParentIndex].Transformation));
        }

        Vector3 pos;
        if (m.PositionFrames.Length > 0)
            pos = LerpPos(m.PositionFrames, animLength, frame);
        else if (m.ParentIndex >= 0 && m.ParentIndex < meshes.Count)
            pos = Vector3.TransformNormal(V(m.LocalPosition) - V(meshes[m.ParentIndex].LocalPosition),
                                          SafeInvert(ToM4(meshes[m.ParentIndex].Transformation)));
        else
            pos = V(m.LocalPosition);

        return r * Matrix4.CreateTranslation(pos);
    }

    // ---- keyframe interpolation ----

    private static Quaternion SlerpRot(RotKey[] f, int animLength, double frame)
    {
        var (p, n, t) = Locate(f.Length, idx => f[idx].Frame, animLength, frame);
        Quaternion qp = p < 0 ? Quaternion.Identity : Q(f[p]);
        Quaternion qn = n >= f.Length ? Q(f[f.Length - 1]) : Q(f[n]);
        return Quaternion.Slerp(qp, qn, t);
    }

    private static Vector3 LerpPos(PosKey[] f, int animLength, double frame)
    {
        var (p, n, t) = Locate(f.Length, idx => f[idx].Frame, animLength, frame);
        Vector3 vp = p < 0 ? new Vector3(f[0].X, f[0].Y, f[0].Z) : new Vector3(f[p].X, f[p].Y, f[p].Z);
        Vector3 vn = n >= f.Length ? new Vector3(f[^1].X, f[^1].Y, f[^1].Z) : new Vector3(f[n].X, f[n].Y, f[n].Z);
        return vp + (vn - vp) * t;
    }

    private static Vector3 LerpScale(ScaleKey[] f, int animLength, double frame)
    {
        var (p, n, t) = Locate(f.Length, idx => f[idx].Frame, animLength, frame);
        Vector3 vp = p < 0 ? Vector3.One : new Vector3(f[p].X, f[p].Y, f[p].Z);
        Vector3 vn = n >= f.Length ? new Vector3(f[^1].X, f[^1].Y, f[^1].Z) : new Vector3(f[n].X, f[n].Y, f[n].Z);
        return vp + (vn - vp) * t;
    }

    // prev/next keyframe indices around the (looped) frame, plus the [0,1] blend factor.
    private static (int Prev, int Next, float T) Locate(int count, Func<int, int> frameAt, int animLength, double frame)
    {
        if (count == 0) return (-1, 0, 0f);
        float af = animLength > 0 ? (float)(((frame % animLength) + animLength) % animLength) : 0f;
        int prev = -1;
        for (int i = 0; i < count; i++) { if (frameAt(i) <= af) prev = i; else break; }
        int next = prev + 1;
        float prevTick = prev < 0 ? 0f : frameAt(prev);
        float nextTick = next >= count ? animLength : frameAt(next);
        float denom = nextTick - prevTick;
        float t = denom > 1e-4f ? (af - prevTick) / denom : 0f;
        return (prev, next, Math.Clamp(t, 0f, 1f));
    }

    // ---- helpers ----

    private static Vector3 V(float[] a) => new(a[0], a[1], a[2]);
    private static Quaternion Q(RotKey k) => new(k.X, k.Y, k.Z, k.W);
    private static Vector3 SafeAxis(float[] a)
    {
        var v = V(a);
        return v.LengthSquared > 1e-8f ? Vector3.Normalize(v) : Vector3.UnitZ;
    }

    private static Matrix4 ToM4(float[] m) => new(
        m[0], m[1], m[2], 0f,
        m[3], m[4], m[5], 0f,
        m[6], m[7], m[8], 0f,
        0f, 0f, 0f, 1f);

    // GLHelper.Scale: row-scale == diag(s) * m (pre-multiply).
    private static Matrix4 ScaleRows(Matrix4 m, Vector3 s)
    {
        m.Row0 *= s.X; m.Row1 *= s.Y; m.Row2 *= s.Z;
        return m;
    }

    private static Matrix4 SafeInvert(Matrix4 m)
    {
        try { return Matrix4.Invert(m); }
        catch { return Matrix4.Identity; }
    }
}
