using System.Windows;
using ICSharpCode.AvalonEdit;

namespace MidgardStudio.App.Common;

/// <summary>
/// Attached property that makes AvalonEdit's <see cref="TextEditor.Text"/> bindable. Pushes to the
/// source on lost-focus (so script edits become one undo step, not one per keystroke).
/// </summary>
public static class AvalonEditBehavior
{
    private static bool _updating;

    public static readonly DependencyProperty BindableTextProperty =
        DependencyProperty.RegisterAttached(
            "BindableText", typeof(string), typeof(AvalonEditBehavior),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindableTextChanged));

    public static string GetBindableText(DependencyObject o) => (string)o.GetValue(BindableTextProperty);

    public static void SetBindableText(DependencyObject o, string value) => o.SetValue(BindableTextProperty, value);

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(AvalonEditBehavior), new PropertyMetadata(false));

    private static void OnBindableTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor) return;
        Hook(editor);

        if (_updating) return;
        var text = e.NewValue as string ?? string.Empty;
        if (editor.Text != text)
            editor.Text = text;
    }

    private static void Hook(TextEditor editor)
    {
        if ((bool)editor.GetValue(HookedProperty)) return;
        editor.SetValue(HookedProperty, true);

        editor.LostKeyboardFocus += (_, _) =>
        {
            _updating = true;
            SetBindableText(editor, editor.Text);
            _updating = false;
        };
    }
}
