using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class ConfigurationWizardView : UserControl
{
    private Point _dragStart;

    public ConfigurationWizardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Esc closes the configuration window. Handled at the tunneling (Preview) stage on the view root so it
    /// fires reliably even when a focused child (a TextBox, the profiles list, etc.) would otherwise swallow
    /// the key — more robust than a bubbling KeyBinding on the host overlay.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ConfigurationWizardViewModel vm && vm.CloseCommand.CanExecute(null))
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ----- GRF/data path drag-to-reorder (the layering order is the priority; the bottom entry wins) -----

    private void GrfList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(null);

    private void GrfList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var src = e.OriginalSource as DependencyObject;
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(src) is not null) return; // not the remove button
        if (FindAncestor<ListBoxItem>(src) is not { DataContext: string path } item) return;

        DragDrop.DoDragDrop(item, new DataObject("GrfPath", path), DragDropEffects.Move);
    }

    private void GrfList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("GrfPath") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GrfList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not ConfigurationWizardViewModel vm) return;
        if (e.Data.GetData("GrfPath") is not string sourcePath) return;
        int from = vm.GrfPaths.IndexOf(sourcePath);
        if (from < 0) return;

        // Drop onto a row -> move there; drop in the empty area below -> move to the end (lowest, highest priority).
        int to = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: string targetPath }
            ? vm.GrfPaths.IndexOf(targetPath)
            : vm.GrfPaths.Count - 1;
        vm.MoveGrf(from, to);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }
}
