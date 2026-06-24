using System.Windows;
using System.Windows.Controls;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class DbWorkspaceView : UserControl
{
    public DbWorkspaceView()
    {
        InitializeComponent();
        MasterList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        MasterList.SelectionChanged += (_, _) =>
        {
            if (MasterList.SelectedItem is { } item) MasterList.ScrollIntoView(item);
        };
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if ((DataContext as DbWorkspaceViewModel)?.List is not { } list) return;

        string? key = header.Column == IdColumn ? "Id" : header.Column == NameColumn ? "Name" : null;
        if (key is null) return;

        list.ToggleSort(key);
        IdColumn.Header = list.HeaderText("Id");
        NameColumn.Header = list.HeaderText("Name");
    }
}
