using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MidgardStudio.App.ViewModels;
using Wpf.Ui.Controls;

namespace MidgardStudio.App;

/// <summary>
/// Interaction logic for MainWindow.xaml. The shell is a WPF-UI FluentWindow with a Mica
/// backdrop; its DataContext is the <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private bool _closing;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ShortcutsChanged += ApplyShortcuts;
        _viewModel.FocusSearchRequested += FocusListSearch;
        PreviewMouseDown += (_, e) => SpawnSparks(e.GetPosition(SparkLayer));
        PaletteResultsBox.MouseDoubleClick += (_, _) =>
        {
            if (PaletteResultsBox.SelectedItem is PaletteResultViewModel result)
                _viewModel.ActivatePaletteResultCommand.Execute(result);
        };
        ApplyShortcuts();
    }

    /// <summary>Focuses the active database list's search box (the "find in list" shortcut).</summary>
    private void FocusListSearch()
    {
        if (FindByTag(this, "ListSearch") is { } box)
        {
            box.Focus();
            Keyboard.Focus(box);
            if (box is System.Windows.Controls.TextBox tb) tb.SelectAll();
        }
    }

    /// <summary>Depth-first search of the visual tree for a visible element tagged with <paramref name="tag"/>.</summary>
    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && (fe.Tag as string) == tag && fe.IsVisible)
                return fe;
            if (FindByTag(child, tag) is { } found) return found;
        }
        return null;
    }

    /// <summary>Prompts to save unsaved edits before the window closes (Save / Don't save / Cancel).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing && _viewModel.HasUnsavedChanges)
        {
            switch (Views.ConfirmDialog.AskSave("Unsaved changes",
                "You have unsaved changes. Save them before closing?"))
            {
                case Views.SavePrompt.Save:
                    if (!_viewModel.SaveForExit()) { e.Cancel = true; return; } // save failed → stay open
                    break;
                case Views.SavePrompt.Cancel:
                    e.Cancel = true; // stay open
                    return;
                // Discard → fall through and close without saving
            }
        }

        _closing = true;
        base.OnClosing(e);
    }

    /// <summary>Rebuilds the window's keyboard shortcuts from the (user-editable) settings.</summary>
    private void ApplyShortcuts()
    {
        InputBindings.Clear();
        foreach (var binding in _viewModel.BuildInputBindings())
            InputBindings.Add(binding);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsPaletteOpen) && _viewModel.IsPaletteOpen)
            Dispatcher.BeginInvoke(() => PaletteSearchBox.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Emits a short ring of spark lines from the click point (reactbits-style click spark).</summary>
    private void SpawnSparks(Point center)
    {
        const int count = 8;
        const double r0 = 5, len = 11, travel = 17;
        var color = (Color)ColorConverter.ConvertFromString("#D916FB"); // logo magenta
        var dur = new Duration(TimeSpan.FromMilliseconds(450));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        for (int i = 0; i < count; i++)
        {
            double a = 2 * Math.PI * i / count;
            double dx = Math.Cos(a), dy = Math.Sin(a);
            var line = new Line
            {
                X1 = center.X + dx * r0, Y1 = center.Y + dy * r0,
                X2 = center.X + dx * (r0 + len), Y2 = center.Y + dy * (r0 + len),
                Stroke = new SolidColorBrush(color), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            SparkLayer.Children.Add(line);

            void Animate(DependencyProperty p, double from, double to) =>
                line.BeginAnimation(p, new DoubleAnimation(from, to, dur) { EasingFunction = ease });

            Animate(Line.X1Property, center.X + dx * r0, center.X + dx * (r0 + travel));
            Animate(Line.Y1Property, center.Y + dy * r0, center.Y + dy * (r0 + travel));
            Animate(Line.X2Property, center.X + dx * (r0 + len), center.X + dx * (r0 + travel + len));
            Animate(Line.Y2Property, center.Y + dy * (r0 + len), center.Y + dy * (r0 + travel + len));

            var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
            fade.Completed += (_, _) => SparkLayer.Children.Remove(line);
            line.BeginAnimation(OpacityProperty, fade);
        }
    }
}
