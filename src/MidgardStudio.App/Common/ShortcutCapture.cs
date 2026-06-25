using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MidgardStudio.App.Common;

/// <summary>
/// Turns a <see cref="TextBox"/> into a shortcut recorder: instead of typing, the user presses the key
/// combination and it's captured as a gesture string (e.g. <c>Ctrl+Shift+F</c>). Supports any number of
/// modifiers (Ctrl / Shift / Alt). Esc cancels; Backspace / Delete clears the binding. The captured
/// string is pushed back through the TextBox's <c>Text</c> binding.
/// </summary>
public static class ShortcutCapture
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ShortcutCapture), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if (e.NewValue is true)
        {
            tb.IsReadOnly = true;
            tb.IsReadOnlyCaretVisible = false;
            tb.PreviewKeyDown += OnPreviewKeyDown;
            tb.GotKeyboardFocus += OnGotFocus;
        }
        else
        {
            tb.PreviewKeyDown -= OnPreviewKeyDown;
            tb.GotKeyboardFocus -= OnGotFocus;
        }
    }

    private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // When Alt is held WPF reports the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        e.Handled = true;

        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }

        if ((key == Key.Back || key == Key.Delete) && mods == ModifierKeys.None)
        {
            Commit(tb, string.Empty); // clear / disable this shortcut
            return;
        }

        // A lone modifier isn't a shortcut yet — wait for the real key.
        if (IsModifier(key)) return;

        // Letters/digits/punctuation need at least one modifier; function keys (F1–F24) stand alone.
        bool standalone = key is >= Key.F1 and <= Key.F24;
        if (mods == ModifierKeys.None && !standalone) return;

        Commit(tb, Format(mods, key));
    }

    private static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private static string Format(ModifierKeys mods, Key key)
    {
        var sb = new StringBuilder();
        if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        sb.Append(key.ToString());
        return sb.ToString();
    }

    private static void Commit(TextBox tb, string gesture)
    {
        tb.Text = gesture;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        tb.SelectAll();
    }
}
