using System.Windows.Controls;
using System.Windows.Input;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class GrfBrowserView : UserControl
{
    public GrfBrowserView()
    {
        InitializeComponent();

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
    private void OnHeaderClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Content: string column } && DataContext is GrfBrowserViewModel vm)
            vm.SortBy(column);
    }
}
