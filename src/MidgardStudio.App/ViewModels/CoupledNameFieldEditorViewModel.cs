using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Common;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// The Name and AegisName fields, coupled with an inline auto-fill button. On the Name field the button
/// generates the aegis name (Title_Case_With_Underscores); on the AegisName field it fills the display
/// name (Title Case, no underscores). The two editors reference each other so the sibling box refreshes.
/// </summary>
public sealed class CoupledNameFieldEditorViewModel : FieldEditorViewModel
{
    private readonly string _siblingField;
    private readonly bool _toAegis; // true on the Name field (sibling = aegis, gets underscores)

    public CoupledNameFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c,
        string siblingField, bool toAegis) : base(r, f, c)
    {
        _siblingField = siblingField;
        _toAegis = toAegis;
        SyncCommand = new RelayCommand(Sync, () => IsEditable);
    }

    /// <summary>The other half of the pair (set by the record editor after both are built).</summary>
    public CoupledNameFieldEditorViewModel? Sibling { get; set; }

    public string Value
    {
        get => Record.GetString(FieldName) ?? string.Empty;
        set { if (value != Value) { Commit(value); OnPropertyChanged(); } }
    }

    public ICommand SyncCommand { get; }

    public string SyncTooltip => _toAegis
        ? "Generate the aegis name from this (Title_Case_With_Underscores)"
        : "Fill the display name from this (Title Case, spaces)";

    private void Sync()
    {
        if (!IsEditable) return;
        string source = Value;
        if (string.IsNullOrWhiteSpace(source)) return;

        string result = _toAegis ? NameFormat.ToAegis(source) : NameFormat.ToDisplay(source);
        Stack.Execute(new SetFieldCommand(Record, _siblingField, result));
        Sibling?.Refresh();
        RaiseChanged();
    }

    public void Refresh() => OnPropertyChanged(nameof(Value));
}
