using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MidgardStudio.App.Common;

/// <summary>
/// A <see cref="Track"/> that guarantees a minimum thumb length. WPF's default Track sizes the thumb
/// strictly proportionally (viewport / extent) and ignores the thumb's MinHeight when arranging, so on
/// a list with thousands of rows the thumb shrinks to ~0px. This re-implements the arrange (and the
/// drag→value mapping) with a hard minimum, so the thumb stays grabbable on huge lists.
/// </summary>
public sealed class FixedThumbTrack : Track
{
    public static readonly DependencyProperty MinThumbLengthProperty = DependencyProperty.Register(
        nameof(MinThumbLength), typeof(double), typeof(FixedThumbTrack),
        new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinThumbLength
    {
        get => (double)GetValue(MinThumbLengthProperty);
        set => SetValue(MinThumbLengthProperty, value);
    }

    private double _trackLength;
    private double _thumbLength;

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        double trackLength = vertical ? finalSize.Height : finalSize.Width;
        double range = Math.Max(0, Maximum - Minimum);
        double viewport = ViewportSize;

        double thumbLength;
        if (!double.IsNaN(viewport) && viewport > 0)
        {
            double extent = range + viewport;
            thumbLength = extent > 0 ? trackLength * viewport / extent : trackLength;
        }
        else
        {
            thumbLength = vertical ? (Thumb?.DesiredSize.Height ?? 0) : (Thumb?.DesiredSize.Width ?? 0);
        }

        double minThumb = Math.Min(MinThumbLength, trackLength);
        thumbLength = Math.Max(minThumb, Math.Min(thumbLength, trackLength));
        if (double.IsNaN(thumbLength) || thumbLength < 0) thumbLength = 0;

        double frac = range > 0 ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        double freeSpace = Math.Max(0, trackLength - thumbLength);
        double offset = frac * freeSpace;

        _trackLength = trackLength;
        _thumbLength = thumbLength;

        if (vertical)
        {
            double w = finalSize.Width;
            DecreaseRepeatButton?.Arrange(new Rect(0, 0, w, offset));
            Thumb?.Arrange(new Rect(0, offset, w, thumbLength));
            IncreaseRepeatButton?.Arrange(new Rect(0, offset + thumbLength, w, Math.Max(0, trackLength - offset - thumbLength)));
        }
        else
        {
            double h = finalSize.Height;
            DecreaseRepeatButton?.Arrange(new Rect(0, 0, offset, h));
            Thumb?.Arrange(new Rect(offset, 0, thumbLength, h));
            IncreaseRepeatButton?.Arrange(new Rect(offset + thumbLength, 0, Math.Max(0, trackLength - offset - thumbLength), h));
        }

        return finalSize;
    }

    /// <summary>Maps a thumb-drag distance to a value delta using the enforced thumb size, so dragging
    /// tracks the cursor correctly even with the minimum thumb.</summary>
    public override double ValueFromDistance(double horizontal, double vertical)
    {
        double range = Math.Max(0, Maximum - Minimum);
        double freeSpace = Math.Max(1e-6, _trackLength - _thumbLength);
        double density = range / freeSpace;
        double delta = Orientation == Orientation.Vertical ? vertical : horizontal;
        return density * delta;
    }
}
