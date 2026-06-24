using System.Windows;
using System.Windows.Controls;
using MidgardStudio.App.ViewModels;

namespace MidgardStudio.App.Common;

/// <summary>
/// Picks the *full* (popup) editor template for a wide field VM — the chips / nested form / sub-grid /
/// script editor — as opposed to the compact one-line row shown inline. Keyed templates live in
/// FieldTemplates.xaml (merged app-wide), so they resolve from the application resources.
/// </summary>
public sealed class FullEditorTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        string? key = item switch
        {
            BoolMapFieldEditorViewModel => "BoolMapFullTemplate",
            ObjectFieldEditorViewModel => "ObjectFullTemplate",
            ObjectListFieldEditorViewModel => "ObjectListFullTemplate",
            ScriptFieldEditorViewModel => "ScriptFullTemplate",
            _ => null,
        };
        return key is not null && Application.Current.TryFindResource(key) is DataTemplate template ? template : null;
    }
}
