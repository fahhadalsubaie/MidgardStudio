using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MidgardStudio.App.Common;

/// <summary>
/// Attached behavior: right-clicking a list row selects it first, so a list-level ContextMenu can act
/// on <c>SelectedItem</c> (the row the user actually clicked). Avoids per-item ContextMenu styles,
/// which can't resolve the themed ListViewItem style from inside a merged ResourceDictionary.
/// </summary>
public static class ListBehaviors
{
    public static readonly DependencyProperty SelectOnRightClickProperty = DependencyProperty.RegisterAttached(
        "SelectOnRightClick", typeof(bool), typeof(ListBehaviors), new PropertyMetadata(false, OnChanged));

    public static void SetSelectOnRightClick(DependencyObject o, bool value) => o.SetValue(SelectOnRightClickProperty, value);

    public static bool GetSelectOnRightClick(DependencyObject o) => (bool)o.GetValue(SelectOnRightClickProperty);

    private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ListBox list) return;
        if (e.NewValue is true)
            list.PreviewMouseRightButtonDown += OnRightDown;
        else
            list.PreviewMouseRightButtonDown -= OnRightDown;
    }

    private static void OnRightDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null and not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListBoxItem item)
        {
            item.IsSelected = true;     // right-clicking a row selects it
            item.Focus();
        }
        else if (sender is System.Windows.Controls.Primitives.Selector selector)
        {
            selector.SelectedItem = null; // right-clicking empty space clears selection
        }
    }
}
