using MidgardStudio.Core.Sprites;

namespace MidgardStudio.Tests;

/// <summary>
/// The pure sprite-composition geometry, previously trapped inside the App WPF renderer. Corner cases
/// (empty action, clamp to 1..1024, mirror centre, interval clamp) are now testable without WPF.
/// </summary>
public class SpriteLayoutTests
{
    [Fact]
    public void Bounds_sizes_the_canvas_around_a_single_centered_layer()
    {
        var canvas = SpriteLayout.Bounds(new[] { new SpriteBox(0, 0, 20, 30) });

        Assert.NotNull(canvas);
        Assert.Equal(24, canvas!.Value.Width);   // 20 + pad*2
        Assert.Equal(34, canvas.Value.Height);   // 30 + pad*2
        Assert.Equal(12, canvas.Value.OriginX);  // -minX + pad = 10 + 2
        Assert.Equal(17, canvas.Value.OriginY);  // 15 + 2
    }

    [Fact]
    public void Bounds_is_null_for_an_empty_action() => Assert.Null(SpriteLayout.Bounds(Array.Empty<SpriteBox>()));

    [Fact]
    public void Bounds_clamps_an_oversized_layer_to_1024()
    {
        var canvas = SpriteLayout.Bounds(new[] { new SpriteBox(0, 0, 5000, 10) });
        Assert.Equal(1024, canvas!.Value.Width);
    }

    [Fact]
    public void Place_offsets_the_layer_and_reports_the_mirror_centre()
    {
        var canvas = SpriteLayout.Bounds(new[] { new SpriteBox(0, 0, 20, 30) })!.Value;

        var p = SpriteLayout.Place(new SpriteBox(0, 0, 20, 30), canvas);

        Assert.Equal(2, p.X);             // originX(12) - w/2(10)
        Assert.Equal(2, p.Y);             // originY(17) - h/2(15)
        Assert.Equal(12, p.MirrorCenterX);
        Assert.Equal(17, p.MirrorCenterY);
    }

    [Theory]
    [InlineData(4f, 3, 100)]   // 4*25 = 100, in range
    [InlineData(1f, 2, 40)]    // 25 -> clamped up to 40
    [InlineData(100f, 2, 1000)]// 2500 -> clamped down to 1000
    [InlineData(4f, 1, 0)]     // single frame -> static
    public void IntervalMs_clamps_and_zeroes_single_frames(float speed, int frames, int expected) =>
        Assert.Equal(expected, SpriteLayout.IntervalMs(speed, frames));
}
