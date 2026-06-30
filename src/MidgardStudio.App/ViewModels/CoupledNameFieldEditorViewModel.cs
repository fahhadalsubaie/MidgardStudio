using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Common;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// The Name and AegisName fields, coupled with an inline copy button. The button copies the OTHER field's
/// value into this one verbatim (Name's button pulls in the aegis name; the aegis name's button pulls in the
/// display name) — a plain copy, not a transform. The two editors reference each other so the sibling refreshes.
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
        set { var v = Normalize(value); if (v != Value) { Commit(v); OnPropertyChanged(); } }
    }

    public ICommand SyncCommand { get; }

    public string SyncTooltip => _toAegis
        ? "Copy the aegis name into here"     // on the Name field; sibling = AegisName
        : "Copy the display name into here";  // on the AegisName field; sibling = Name

    /// <summary>The aegis name is a server identifier and must never contain spaces — they become underscores.
    /// The display name is left as typed.</summary>
    private string Normalize(string s) => _toAegis ? s : s.Replace(' ', '_');

    /// <summary>Copies the sibling field's value into THIS field, converting between the two conventions:
    /// aegis → display name turns underscores into spaces; display name → aegis turns spaces into underscores.</summary>
    private void Sync()
    {
        if (!IsEditable) return;
        string other = Record.GetString(_siblingField) ?? string.Empty;
        string converted = _toAegis ? other.Replace('_', ' ') : other.Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(converted) || converted == Value) return;

        Commit(converted);
        OnPropertyChanged(nameof(Value));
        RaiseChanged();
    }

    public void Refresh() => OnPropertyChanged(nameof(Value));
}
