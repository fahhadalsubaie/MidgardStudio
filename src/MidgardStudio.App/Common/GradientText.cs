using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MidgardStudio.App.Common;

/// <summary>
/// Attaches an animated, logo-coloured gradient to a <see cref="TextBlock"/>'s foreground — the fill is
/// on the glyphs themselves (no borders). The brush is built fresh per element in code, so the animation
/// never targets a frozen resource. (A gradient brush placed in a Style setter gets sealed/frozen when the
/// style is applied, which makes a Storyboard throw "Cannot animate '…' on an immutable object instance".)
/// </summary>
public static class GradientText
{
    // Logo palette (purple → magenta → lavender). Kept light enough to read on the dark theme.
    private static readonly Color C1 = (Color)ColorConverter.ConvertFromString("#8D2DF2")!;
    private static readonly Color C2 = (Color)ColorConverter.ConvertFromString("#D916FB")!;
    private static readonly Color C3 = (Color)ColorConverter.ConvertFromString("#B497CF")!;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GradientText), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb && e.NewValue is true) Apply(tb);
    }

    private static void Apply(TextBlock tb)
    {
        // Symmetric stop pattern (starts and ends on the same colour) + Repeat spread, so sliding the
        // RelativeTransform from 0→1 loops seamlessly with no visible seam.
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(C1, 0.00));
        brush.GradientStops.Add(new GradientStop(C2, 0.25));
        brush.GradientStops.Add(new GradientStop(C3, 0.50));
        brush.GradientStops.Add(new GradientStop(C2, 0.75));
        brush.GradientStops.Add(new GradientStop(C1, 1.00));

        var slide = new TranslateTransform(0, 0);
        brush.RelativeTransform = slide;
        tb.Foreground = brush;

        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromSeconds(5))) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
