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

    /// <summary>Closes the update popup after the user picks an action (the bound command does the rest).</summary>
    private void OnUpdateActionClicked(object sender, RoutedEventArgs e) => UpdateToggle.IsChecked = false;

    // The update pill sits in the title-bar region, which WPF-UI's ClientAreaBorder reports to the OS as
    // caption (HTCAPTION) — so a click there starts a window drag instead of reaching the pill, and
    // WindowChrome.IsHitTestVisibleInChrome is ignored by WPF-UI. We intercept WM_NCHITTEST and return
    // HTCLIENT while the cursor is over the pill, so the OS delivers a normal click that WPF then routes to it.
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int GWLP_WNDPROC = -4;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int idx, IntPtr newLong);

    private delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private WndProc? _subclass; // kept alive for the window's lifetime
    private IntPtr _oldProc;

    // WPF-UI's ClientAreaBorder owns WM_NCHITTEST and reports the whole title bar as caption (drag), ignoring
    // WindowChrome.IsHitTestVisibleInChrome — and its HwndSource hook overrides ours. So we subclass the window
    // proc itself: for NCHITTEST over the update pill we return HTCLIENT before WPF-UI ever sees the message;
    // everything else is forwarded untouched. This makes the pill's region a normal client click.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _subclass = SubclassProc;
        _oldProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
            System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_subclass));
    }

    private IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST && UpdateToggle is { IsVisible: true })
        {
            int lp = lParam.ToInt32();
            double sx = (short)(lp & 0xFFFF), sy = (short)((lp >> 16) & 0xFFFF);
            try
            {
                Point tl = UpdateToggle.PointToScreen(new Point(0, 0));
                Point br = UpdateToggle.PointToScreen(new Point(UpdateToggle.ActualWidth, UpdateToggle.ActualHeight));
                if (sx >= tl.X && sx <= br.X && sy >= tl.Y && sy <= br.Y)
                    return new IntPtr(HTCLIENT); // over the pill → client, so the click reaches the ToggleButton
            }
            catch { /* visual not ready */ }
        }
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
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
