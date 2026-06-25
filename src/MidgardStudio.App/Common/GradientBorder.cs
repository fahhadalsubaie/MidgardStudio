using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MidgardStudio.App.Common;

/// <summary>
/// When <c>Enabled</c> is true, paints a control's Background with the logo gradient rotating around its
/// centre — a glow that keeps circling the border (à la reactbits StarBorder); when false it reverts to
/// solid black. Applied to a thin outer "ring" border (an inner black border covers the centre) so only the
/// edge shows. Built in code as a local value so it survives styling, and the rotation never animates a
/// frozen resource.
/// </summary>
public static class GradientBorder
{
    private static readonly Color C1 = (Color)ColorConverter.ConvertFromString("#8D2DF2")!;
    private static readonly Color C2 = (Color)ColorConverter.ConvertFromString("#D916FB")!;
    private static readonly Color C3 = (Color)ColorConverter.ConvertFromString("#B497CF")!;

    private static readonly Brush Idle = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GradientBorder), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control c) return;
        c.Background = e.NewValue is true ? CreateRotating() : Idle;
    }

    private static Brush CreateRotating()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            SpreadMethod = GradientSpreadMethod.Reflect,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(C1, 0.00));
        brush.GradientStops.Add(new GradientStop(C2, 0.25));
        brush.GradientStops.Add(new GradientStop(C3, 0.50));
        brush.GradientStops.Add(new GradientStop(C2, 0.75));
        brush.GradientStops.Add(new GradientStop(C1, 1.00));

        var rotate = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360,
            new Duration(TimeSpan.FromSeconds(3))) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
