using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Views;

public partial class ForgeView : UserControl
{
    public ForgeView()
    {
        InitializeComponent();
        CenterScroll.GotKeyboardFocus += OnCenterFocus;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Renders the parchment preview's color-coded description (the same ^RRGGBB renderer Client Items
    /// uses) live as the auto-fill or the user changes the Description — so the preview is the real tooltip.</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ForgeViewModel oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is ForgeViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            Common.RoColorText.Render(DescPreview, vm.Description);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ForgeViewModel.Description) && sender is ForgeViewModel vm)
            Common.RoColorText.Render(DescPreview, vm.Description);
    }

    /// <summary>Nav-rail jump: scroll the clicked section into view and mark it active.</summary>
    private void NavClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ForgeSection section }) return;
        if (DataContext is not ForgeViewModel vm) return;

        SetActive(vm, section.Title);

        if (section.Title == "Appearance") { AppearanceCard.BringIntoView(); return; }

        foreach (var group in vm.ServerEditor.Groups)
            if (group.Title == section.Title)
            {
                if (GroupsHost.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement container)
                    container.BringIntoView();
                return;
            }
    }

    /// <summary>Focus-follow: when a field gains keyboard focus, highlight the nav section it lives in (so the
    /// rail moves with the user as they tab/click through Identity → Combat → Flags → Appearance).</summary>
    private void OnCenterFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not ForgeViewModel vm) return;

        var d = e.NewFocus as DependencyObject ?? e.OriginalSource as DependencyObject;
        string? title = null;
        while (d is not null)
        {
            if (ReferenceEquals(d, AppearanceCard)) { title = "Appearance"; break; }
            if (d is FrameworkElement fe && fe.DataContext is FieldGroupViewModel grp) { title = grp.Title; break; }
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : (d as FrameworkElement)?.Parent;
        }
        if (title is not null) SetActive(vm, title);
    }

    private static void SetActive(ForgeViewModel vm, string title)
    {
        foreach (var s in vm.Sections) s.IsActive = string.Equals(s.Title, title, StringComparison.Ordinal);
    }
}
