using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;

namespace MidgardStudio.App.Common;

/// <summary>A decoded sprite animation: composited WPF frames + the per-frame interval (0 = static).</summary>
public sealed record SpriteAnimation(IReadOnlyList<ImageSource> Frames, int IntervalMs);

/// <summary>
/// Decodes a Ragnarok .spr/.act pair (read from a GRF) into a list of composited, animatable WPF frames.
/// Layers are placed at their .act offsets onto a transparent canvas sized to the action's bounds.
/// Read-only: it never writes back to the GRF. Falls back to the first static sprite frame on any error.
/// </summary>
public static class SpriteRenderer
{
    public static SpriteAnimation? Build(byte[]? sprBytes, byte[]? actBytes)
    {
        if (sprBytes is null || sprBytes.Length == 0) return null;

        if (actBytes is { Length: > 0 })
        {
            try
            {
                var act = new Act(actBytes, sprBytes);
                if (act.NumberOfActions > 0 && (act.Sprite?.Images?.Count ?? 0) > 0)
                {
                    var anim = Composite(act);
                    if (anim is not null) return anim;
                }
            }
            catch { /* fall through to a static frame */ }
        }

        return StaticFirstFrame(sprBytes);
    }

    private static SpriteAnimation? StaticFirstFrame(byte[] sprBytes)
    {
        try
        {
            var images = new Spr(sprBytes).Images;
            if (images is { Count: > 0 } && GrfImaging.ToImageSource(images[0]) is { } img)
                return new SpriteAnimation(new[] { img }, 0);
        }
        catch { /* unreadable sprite */ }
        return null;
    }

    private static SpriteAnimation? Composite(Act act)
    {
        var spr = act.Sprite!;
        var action = act[0];

        // Bounds across every layer of every frame in the action, centred on the .act origin.
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        bool any = false;
        foreach (var frame in action.Frames)
            foreach (var layer in frame.Layers)
            {
                var gi = SafeImage(layer, spr);
                if (gi is null) continue;
                double w = gi.Width, h = gi.Height;
                double l = layer.OffsetX - w / 2.0, t = layer.OffsetY - h / 2.0;
                double r = layer.OffsetX + w / 2.0, b = layer.OffsetY + h / 2.0;
                if (!any) { minX = l; minY = t; maxX = r; maxY = b; any = true; }
                else { minX = Math.Min(minX, l); minY = Math.Min(minY, t); maxX = Math.Max(maxX, r); maxY = Math.Max(maxY, b); }
            }

        if (!any) return null;

        const int pad = 2;
        int cw = Math.Clamp((int)Math.Ceiling(maxX - minX) + pad * 2, 1, 1024);
        int ch = Math.Clamp((int)Math.Ceiling(maxY - minY) + pad * 2, 1, 1024);
        double cx = -minX + pad, cy = -minY + pad;

        var frames = new List<ImageSource>(action.NumberOfFrames);
        foreach (var frame in action.Frames)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                foreach (var layer in frame.Layers)
                {
                    var gi = SafeImage(layer, spr);
                    if (gi is null || GrfImaging.ToImageSource(gi) is not { } src) continue;
                    double w = gi.Width, h = gi.Height;
                    double x = cx + layer.OffsetX - w / 2.0;
                    double y = cy + layer.OffsetY - h / 2.0;
                    bool mirror = layer.Mirror;
                    if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, cx + layer.OffsetX, cy + layer.OffsetY));
                    dc.DrawImage(src, new Rect(x, y, w, h));
                    if (mirror) dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            frames.Add(rtb);
        }

        if (frames.Count == 0) return null;
        int interval = Math.Clamp((int)(action.AnimationSpeed * 25f), 40, 1000);
        return new SpriteAnimation(frames, frames.Count > 1 ? interval : 0);
    }

    private static GRF.Image.GrfImage? SafeImage(Layer layer, Spr spr)
    {
        try { return layer.GetImage(spr); }
        catch { return null; }
    }
}
