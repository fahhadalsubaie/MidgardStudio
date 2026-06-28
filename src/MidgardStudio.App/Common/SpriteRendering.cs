using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using MidgardStudio.Core.Sprites;

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

        // Pure geometry (bounds / clamp / placement / interval) lives in Core.Sprites.SpriteLayout; this
        // method only extracts the boxes and renders the returned placements to WPF.
        var boxes = new List<SpriteBox>();
        foreach (var frame in action.Frames)
            foreach (var layer in frame.Layers)
            {
                var gi = SafeImage(layer, spr);
                if (gi is not null) boxes.Add(new SpriteBox(layer.OffsetX, layer.OffsetY, gi.Width, gi.Height));
            }

        if (SpriteLayout.Bounds(boxes) is not { } canvas) return null;

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
                    var p = SpriteLayout.Place(new SpriteBox(layer.OffsetX, layer.OffsetY, gi.Width, gi.Height), canvas);
                    if (layer.Mirror) dc.PushTransform(new ScaleTransform(-1, 1, p.MirrorCenterX, p.MirrorCenterY));
                    dc.DrawImage(src, new Rect(p.X, p.Y, p.Width, p.Height));
                    if (layer.Mirror) dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(canvas.Width, canvas.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            frames.Add(rtb);
        }

        if (frames.Count == 0) return null;
        return new SpriteAnimation(frames, SpriteLayout.IntervalMs(action.AnimationSpeed, frames.Count));
    }

    private static GRF.Image.GrfImage? SafeImage(Layer layer, Spr spr)
    {
        try { return layer.GetImage(spr); }
        catch { return null; }
    }
}
