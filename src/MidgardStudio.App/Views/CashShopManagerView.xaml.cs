using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MidgardStudio.App.Common;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class CashShopManagerView : UserControl
{
    private Point _dragStart;
    private CashItemRowViewModel? _dragRow;
    private DragAdorner? _dragAdorner;
    private FrameworkElement? _dragSource;

    public CashShopManagerView() => InitializeComponent();

    private CashShopManagerViewModel? Vm => DataContext as CashShopManagerViewModel;

    // ---- Add box: item_db autocomplete popup + Enter-to-add ----
    private void AddBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Enter) { vm.AddItemCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CloseSuggestions(); e.Handled = true; }
    }

    private void AddBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Close the popup when focus leaves the add box — unless it moved into the suggestion list (a click on
        // a suggestion takes focus there first; PickSuggestion then closes it after the mouse-up registers).
        if (Vm is { } vm && !IsWithin(e.NewFocus as DependencyObject, SuggestList))
            vm.CloseSuggestions();
    }

    private void SuggestList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { } vm && FindDataContext<CashSuggestionViewModel>(e.OriginalSource as DependencyObject) is { } picked)
        {
            vm.PickSuggestion(picked);
            AddBox.Focus();
        }
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        for (var d = node; d is not null; d = ParentOf(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    // ---- Drag: reorder a card within the tab, or drop it on a rail tab to recategorize ----
    private void Items_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        // Don't begin a drag from the price box or remove button — let them handle the click.
        _dragRow = IsInteractive(e.OriginalSource as DependencyObject)
            ? null
            : FindDataContext<CashItemRowViewModel>(e.OriginalSource as DependencyObject);
    }

    private void Items_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragRow is null || _dragRow.IsBase) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var row = _dragRow;
        _dragRow = null;
        BeginDragGhost(row, e);
        try { DragDrop.DoDragDrop(ItemsList, row, DragDropEffects.Move); }
        finally { EndDragGhost(); }
    }

    // Spawn the floating ghost over the window and dim the real card while it's being dragged.
    private void BeginDragGhost(CashItemRowViewModel row, MouseEventArgs e)
    {
        if (ItemsList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement container) return;
        if (AdornerLayer.GetAdornerLayer(this) is not { } layer) return;
        double w = container.ActualWidth, h = container.ActualHeight;
        if (w < 1 || h < 1) return;

        var snapshot = Snapshot(container, w, h); // freeze BEFORE dimming so the ghost stays full opacity
        var grab = e.GetPosition(container);
        _dragSource = container;
        _dragSource.Opacity = 0.35;
        _dragAdorner = new DragAdorner(this, snapshot, new Size(w, h), grab);
        layer.Add(_dragAdorner);
        _dragAdorner.SetPosition(e.GetPosition(this));
    }

    private static ImageSource Snapshot(FrameworkElement element, double w, double h)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Draw the element via a VisualBrush into a fresh DrawingVisual at (0,0). RenderTargetBitmap.Render(el)
        // uses the element's OFFSET within its parent, so any card not at the list's top-left renders off the
        // top of the bitmap (only the top-left card got a ghost). Brushing at the origin fixes every card.
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, w, h));
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void EndDragGhost()
    {
        if (_dragAdorner is not null)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(_dragAdorner);
            _dragAdorner = null;
        }
        if (_dragSource is not null) { _dragSource.Opacity = 1.0; _dragSource = null; }
    }

    private void Items_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        // The ghost is the drag visual — suppress the default move/no-drop cursor swap.
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.Arrow);
        e.Handled = true;
    }

    private void Items_DragOver(object sender, DragEventArgs e)
    {
        SetMoveEffect(e);
        _dragAdorner?.SetPosition(e.GetPosition(this));
    }

    private void Items_Drop(object sender, DragEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Data.GetData(typeof(CashItemRowViewModel)) is not CashItemRowViewModel row) return;

        var targetRow = FindDataContext<CashItemRowViewModel>(e.OriginalSource as DependencyObject);
        int visualIndex = targetRow is not null ? vm.Items.IndexOf(targetRow) : vm.Items.Count;
        // The Items list shows base rows first; the custom list MoveWithinTab edits is offset by that count.
        int baseCount = vm.Items.Count(r => r.IsBase);
        vm.MoveWithinTab(row, Math.Max(0, visualIndex - baseCount));
    }

    private void TabRail_DragOver(object sender, DragEventArgs e)
    {
        SetMoveEffect(e);
        _dragAdorner?.SetPosition(e.GetPosition(this));
    }

    private void TabRail_Drop(object sender, DragEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Data.GetData(typeof(CashItemRowViewModel)) is not CashItemRowViewModel row) return;
        if (FindDataContext<CashTabViewModel>(e.OriginalSource as DependencyObject) is { } tab)
            vm.MoveItemToTab(row, tab.Tab);
    }

    private static void SetMoveEffect(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(CashItemRowViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        for (var d = source; d is not null; d = ParentOf(d))
            if (d is ButtonBase or TextBoxBase) return true;
        return false;
    }

    private static T? FindDataContext<T>(DependencyObject? source) where T : class
    {
        for (var d = source; d is not null; d = ParentOf(d))
            if (d is FrameworkElement fe && fe.DataContext is T match) return match;
        return null;
    }

    // Walks the visual tree, falling back to the logical tree for content elements (e.g. text runs) which
    // VisualTreeHelper.GetParent can't traverse.
    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
}
