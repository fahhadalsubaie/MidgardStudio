using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MidgardStudio.App.Common;

/// <summary>
/// A floating "ghost" of the element being dragged that follows the cursor — a frozen snapshot, drop-shadowed,
/// slightly lifted and tilted. Gives DoDragDrop a real drag affordance instead of the default no-feedback
/// cursor. Added to the window's <see cref="AdornerLayer"/>; position is fed from DragOver. A frozen bitmap
/// (not a live VisualBrush) is used so dimming the source card doesn't dim the ghost too.
/// </summary>
public sealed class DragAdorner : Adorner
{
    private readonly Image _ghost;
    private readonly Point _grab; // offset within the dragged element where the user grabbed it
    private Point _position;

    public DragAdorner(UIElement adornedElement, ImageSource snapshot, Size size, Point grab) : base(adornedElement)
    {
        _grab = grab;
        IsHitTestVisible = false;
        _ghost = new Image
        {
            Source = snapshot,
            Width = size.Width,
            Height = size.Height,
            IsHitTestVisible = false,
            Opacity = 0.9,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.55, Color = Colors.Black },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1.04, 1.04), new RotateTransform(-2.5) },
            },
        };
        AddVisualChild(_ghost);
    }

    /// <summary>Move the ghost so the grabbed point sits under the cursor (coords relative to the adorned element).</summary>
    public void SetPosition(Point cursor)
    {
        _position = cursor;
        (Parent as AdornerLayer)?.Update(AdornedElement);
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _ghost;

    protected override Size MeasureOverride(Size constraint)
    {
        _ghost.Measure(constraint);
        return _ghost.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _ghost.Arrange(new Rect(new Point(_position.X - _grab.X, _position.Y - _grab.Y),
            new Size(_ghost.Width, _ghost.Height)));
        return finalSize;
    }
}
