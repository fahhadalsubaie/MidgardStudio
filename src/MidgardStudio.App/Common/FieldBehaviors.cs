using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MidgardStudio.App.Common;

/// <summary>Attached behaviors for the schema-generated field editors.</summary>
public static class FieldBehaviors
{
    /// <summary>Opens a <see cref="ComboBox"/>'s dropdown on a click anywhere in the field (not just the chevron).
    /// Our custom combo template makes the content area hit-test-transparent, so without this only the arrow
    /// opened it. A non-editable combo toggles open/closed; an editable (autocomplete) combo just opens (so the
    /// text caret still works for typing). The combo's transparent background makes the tunneling
    /// PreviewMouseLeftButtonDown fire for clicks anywhere in its bounds.</summary>
    public static readonly DependencyProperty OpenOnClickProperty =
        DependencyProperty.RegisterAttached(
            "OpenOnClick", typeof(bool), typeof(FieldBehaviors), new PropertyMetadata(false, OnOpenOnClickChanged));

    public static void SetOpenOnClick(DependencyObject d, bool value) => d.SetValue(OpenOnClickProperty, value);

    public static bool GetOpenOnClick(DependencyObject d) => (bool)d.GetValue(OpenOnClickProperty);

    private static void OnOpenOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo) return;
        combo.PreviewMouseLeftButtonDown -= OnComboMouseDown;
        if (e.NewValue is true) combo.PreviewMouseLeftButtonDown += OnComboMouseDown;
    }

    private static void OnComboMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo || !combo.IsEnabled || combo.IsDropDownOpen) return;

        // Defer the open to AFTER this click's mouse-up completes. Opening synchronously on mouse-down lets the
        // popup's capture treat the upcoming mouse-up as an outside click and instantly re-close — the classic
        // "combo opens then snaps shut" bug. We only ever open; WPF closes it natively (select / click-out / Esc).
        // The click still reaches the editable textbox underneath, so typing/caret placement keep working.
        combo.Dispatcher.BeginInvoke(
            new System.Action(() => { if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true; }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Runs the bound command when the element loses keyboard focus. The reference field's editable
    /// combo uses this to commit its live query text to the record once (on blur) instead of per keystroke —
    /// so the dropdown can filter live (Text bound PropertyChanged) without spamming the undo stack.</summary>
    public static readonly DependencyProperty CommitOnLostFocusProperty =
        DependencyProperty.RegisterAttached(
            "CommitOnLostFocus", typeof(ICommand), typeof(FieldBehaviors), new PropertyMetadata(null, OnChanged));

    public static void SetCommitOnLostFocus(DependencyObject d, ICommand? value) => d.SetValue(CommitOnLostFocusProperty, value);

    public static ICommand? GetCommitOnLostFocus(DependencyObject d) => (ICommand?)d.GetValue(CommitOnLostFocusProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.LostKeyboardFocus -= OnLostFocus;
        if (e.NewValue is ICommand) fe.LostKeyboardFocus += OnLostFocus;
    }

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement fe && GetCommitOnLostFocus(fe) is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
    }
}
