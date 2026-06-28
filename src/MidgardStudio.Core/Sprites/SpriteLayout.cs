using System;
using System.Collections.Generic;

namespace MidgardStudio.Core.Sprites;

/// <summary>One sprite layer's placement box: its centre offset (.act OffsetX/Y) and image size.</summary>
public readonly record struct SpriteBox(double OffsetX, double OffsetY, double Width, double Height);

/// <summary>The composited canvas: pixel size + where the .act origin sits inside it.</summary>
public readonly record struct SpriteCanvas(int Width, int Height, double OriginX, double OriginY);

/// <summary>Top-left placement of a layer on the canvas, plus the centre to mirror about.</summary>
public readonly record struct LayerPlacement(double X, double Y, double Width, double Height, double MirrorCenterX, double MirrorCenterY);

/// <summary>
/// Pure sprite-composition geometry, lifted out of the WPF renderer so the bounds / clamp / offset math
/// (which has real corner cases) is unit-testable. The App renderer feeds it boxes extracted from the
/// .act/.spr and renders the returned placements — no WPF here.
/// </summary>
public static class SpriteLayout
{
    /// <summary>Canvas bounding every box (each centred on its offset), padded and clamped to 1..1024px.
    /// Null when there are no boxes.</summary>
    public static SpriteCanvas? Bounds(IEnumerable<SpriteBox> boxes, int pad = 2)
    {
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        bool any = false;
        foreach (var b in boxes)
        {
            double l = b.OffsetX - b.Width / 2.0, t = b.OffsetY - b.Height / 2.0;
            double r = b.OffsetX + b.Width / 2.0, btm = b.OffsetY + b.Height / 2.0;
            if (!any) { minX = l; minY = t; maxX = r; maxY = btm; any = true; }
            else { minX = Math.Min(minX, l); minY = Math.Min(minY, t); maxX = Math.Max(maxX, r); maxY = Math.Max(maxY, btm); }
        }
        if (!any) return null;

        int cw = Math.Clamp((int)Math.Ceiling(maxX - minX) + pad * 2, 1, 1024);
        int ch = Math.Clamp((int)Math.Ceiling(maxY - minY) + pad * 2, 1, 1024);
        return new SpriteCanvas(cw, ch, -minX + pad, -minY + pad);
    }

    /// <summary>Top-left placement of <paramref name="box"/> on <paramref name="canvas"/>.</summary>
    public static LayerPlacement Place(SpriteBox box, SpriteCanvas canvas) =>
        new(canvas.OriginX + box.OffsetX - box.Width / 2.0, canvas.OriginY + box.OffsetY - box.Height / 2.0,
            box.Width, box.Height, canvas.OriginX + box.OffsetX, canvas.OriginY + box.OffsetY);

    /// <summary>Per-frame interval in ms (40..1000), or 0 for a single frame.</summary>
    public static int IntervalMs(float animationSpeed, int frameCount) =>
        frameCount > 1 ? Math.Clamp((int)(animationSpeed * 25f), 40, 1000) : 0;
}
