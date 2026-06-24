using System.Windows;
using System.Windows.Controls;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class MapCacheEditorView : UserControl
{
    public MapCacheEditorView()
    {
        InitializeComponent();
        MapList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if (DataContext is not MapCacheEditorViewModel vm) return;

        string? key = header.Column == MapColumn ? "Map" : header.Column == OriginColumn ? "Origin" : null;
        if (key is null) return;

        vm.ToggleSort(key);
        MapColumn.Header = vm.HeaderText("Map");
        OriginColumn.Header = vm.HeaderText("Origin");
    }
}
