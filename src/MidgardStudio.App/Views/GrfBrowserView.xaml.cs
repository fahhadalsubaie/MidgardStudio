using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MidgardStudio.App.ViewModels;
using MidgardStudio.Core;

namespace MidgardStudio.App.Views;

public partial class GrfBrowserView : UserControl
{
    private static readonly string LayoutFile = Path.Combine(AppPaths.RoamingDir, "grfbrowser-layout.txt");

    public GrfBrowserView()
    {
        InitializeComponent();

        Loaded += (_, _) => RestoreColumns();
        Unloaded += (_, _) => SaveColumns();

        Tree.SelectedItemChanged += (_, e) =>
        {
            if (DataContext is GrfBrowserViewModel vm)
                vm.SelectNode(e.NewValue as GrfNode);
        };

        Files.SelectionChanged += (_, _) =>
        {
            if (DataContext is GrfBrowserViewModel vm)
                vm.SelectItem(Files.SelectedItem as GrfItem);
        };
        Files.MouseDoubleClick += (_, _) =>
        {
            if (DataContext is GrfBrowserViewModel vm)
                vm.Open(Files.SelectedItem as GrfItem);
        };
        Files.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is GrfBrowserViewModel vm)
                vm.Open(Files.SelectedItem as GrfItem);
        };
    }

    /// <summary>Click a column header to sort the file table by that column.</summary>
    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Content: string column } && DataContext is GrfBrowserViewModel vm)
            vm.SortBy(column);
    }

    private void OnSearchActivate(object sender, MouseButtonEventArgs e) => ActivateSearch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ActivateSearch();
    }

    private void ActivateSearch()
    {
        if (DataContext is GrfBrowserViewModel vm && SearchList.SelectedItem is SearchHit hit)
            vm.OpenSearchHit(hit);
    }

    // ----- Resizable panes: remember the two left column widths across sessions -----

    private void RestoreColumns()
    {
        try
        {
            if (!File.Exists(LayoutFile)) return;
            var parts = File.ReadAllText(LayoutFile).Split(',');
            if (parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w0)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w1))
            {
                if (w0 is > 80 and < 2000) Col0.Width = new GridLength(w0);
                if (w1 is > 80 and < 2000) Col1.Width = new GridLength(w1);
            }
        }
        catch { /* layout is a convenience; ignore a bad/locked file */ }
    }

    private void SaveColumns()
    {
        try
        {
            if (Col0.ActualWidth < 1) return; // never laid out
            Directory.CreateDirectory(AppPaths.RoamingDir);
            File.WriteAllText(LayoutFile,
                string.Format(CultureInfo.InvariantCulture, "{0},{1}", Col0.ActualWidth, Col1.ActualWidth));
        }
        catch { /* best-effort */ }
    }
}
