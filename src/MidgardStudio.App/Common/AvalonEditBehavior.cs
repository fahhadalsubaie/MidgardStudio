using System;
using System.Windows;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

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

    // ----- Syntax highlighting picked from a bound file extension -----

    /// <summary>Bind a file extension (e.g. ".lua") to drive the editor's syntax highlighting.</summary>
    public static readonly DependencyProperty HighlightExtensionProperty =
        DependencyProperty.RegisterAttached(
            "HighlightExtension", typeof(string), typeof(AvalonEditBehavior),
            new PropertyMetadata(null, OnHighlightExtensionChanged));

    public static string? GetHighlightExtension(DependencyObject o) => (string?)o.GetValue(HighlightExtensionProperty);

    public static void SetHighlightExtension(DependencyObject o, string? value) => o.SetValue(HighlightExtensionProperty, value);

    private static void OnHighlightExtensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor) editor.SyntaxHighlighting = ResolveHighlighting(e.NewValue as string);
    }

    private static IHighlightingDefinition? ResolveHighlighting(string? ext)
    {
        ext = ext?.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return null;
        if (ext is ".lua" or ".lub") return LuaDefinition.Value;
        try
        {
            // Json/js share the JavaScript grammar; everything else falls back to AvalonEdit's by-extension map.
            return ext is ".json" or ".js"
                ? HighlightingManager.Instance.GetDefinition("JavaScript")
                : HighlightingManager.Instance.GetDefinitionByExtension(ext);
        }
        catch { return null; }
    }

    private static readonly Lazy<IHighlightingDefinition?> LuaDefinition = new(LoadLua);

    private static IHighlightingDefinition? LoadLua()
    {
        try
        {
            var asm = typeof(AvalonEditBehavior).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("Lua.xshd", StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = XmlReader.Create(stream);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch { return null; }
    }
}
