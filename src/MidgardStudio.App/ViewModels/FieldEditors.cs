using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Views;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.ViewModels;

/// <summary>Shared context passed to every field editor (undo stack, editability, mode, lookups).</summary>
public sealed class FieldEditorContext
{
    public required EditCommandStack Stack { get; init; }
    public bool IsEditable { get; init; }
    public ServerMode Mode { get; init; } = ServerMode.Renewal;
    public IReferenceResolver? References { get; init; }
    public ScriptCommandCatalog? ScriptCatalog { get; init; }
}

/// <summary>Base for a single editable field. Commits go through the undo stack.</summary>
public abstract class FieldEditorViewModel : ObservableObject
{
    protected readonly DbRecord Record;
    protected readonly FieldSchema Field;
    protected readonly FieldEditorContext Context;

    protected FieldEditorViewModel(DbRecord record, FieldSchema field, FieldEditorContext context)
    {
        Record = record;
        Field = field;
        Context = context;
        IsEditable = context.IsEditable
            && !field.IsKey // key stays fixed after creation
            && !(field.Renewal == RenewalScope.RenewalOnly && context.Mode == ServerMode.PreRenewal);
        OpenEditorCommand = new RelayCommand(OpenEditor);
    }

    protected EditCommandStack Stack => Context.Stack;

    public string FieldName => Field.Name;
    public string Label => Field.Label;
    public string? Description => Field.Description;
    public bool IsRenewalOnly => Field.Renewal == RenewalScope.RenewalOnly;
    public bool IsEditable { get; }

    /// <summary>Wide fields (chips, nested objects, sub-grids, scripts) collapse to a one-line summary + "…" popup.</summary>
    public bool IsWide => Field.Kind is FieldKind.Flags or FieldKind.BoolMap or FieldKind.Object
        or FieldKind.ObjectList or FieldKind.Script or FieldKind.ScalarList;

    /// <summary>One-line preview of a wide field's value, shown next to the "…" button.</summary>
    public virtual string Summary => string.Empty;

    /// <summary>Opens this field's full editor in a popup (used by wide fields' "…" button).</summary>
    public ICommand OpenEditorCommand { get; }

    private void OpenEditor()
    {
        var dialog = new FieldEditorDialog(Label, this) { Owner = System.Windows.Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    public event Action? Changed;

    protected void Commit(object? newValue)
    {
        Stack.Execute(new SetFieldCommand(Record, Field.Name, newValue));
        Changed?.Invoke();
    }

    protected void RaiseChanged() => Changed?.Invoke();

    // ---- live validation state (set by the record editor; bound by the field template) ----

    private string? _issueMessage;
    private MidgardStudio.Core.Validation.ValidationSeverity? _issueSeverity;

    /// <summary>The current validation message for this field (tooltip), or null when valid.</summary>
    public string? IssueMessage
    {
        get => _issueMessage;
        private set
        {
            if (SetProperty(ref _issueMessage, value))
            {
                OnPropertyChanged(nameof(HasIssue));
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    public bool HasIssue => _issueMessage is not null;
    public bool HasError => _issueSeverity == MidgardStudio.Core.Validation.ValidationSeverity.Error;
    public bool HasWarning => _issueSeverity == MidgardStudio.Core.Validation.ValidationSeverity.Warning;

    /// <summary>Applies (or clears) the live validation finding for this field.</summary>
    public void SetIssue(string? message, MidgardStudio.Core.Validation.ValidationSeverity? severity)
    {
        _issueSeverity = message is null ? null : severity;
        IssueMessage = message;
    }
}

public sealed class StringFieldEditorViewModel : FieldEditorViewModel
{
    public StringFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c) { }

    public string Value
    {
        get => Record.GetString(FieldName) ?? string.Empty;
        set { if (value != Value) { Commit(value); OnPropertyChanged(); } }
    }
}

public sealed class IntFieldEditorViewModel : FieldEditorViewModel
{
    public IntFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c) { }

    public int Value
    {
        get => Record.GetInt(FieldName);
        set
        {
            int clamped = Field.Clamp(value);
            if (clamped != Value) Commit(clamped);
            OnPropertyChanged(); // always notify so an out-of-range entry snaps back to the clamped value
        }
    }
}

/// <summary>Editor for a 64-bit field (e.g. mob EXP, read server-side as uint64). Backed by
/// <see cref="DbRecord.GetLong"/> so values above int32 (high-rate renewal EXP) are editable, unlike the
/// int-clamping <see cref="IntFieldEditorViewModel"/>.</summary>
public sealed class LongFieldEditorViewModel : FieldEditorViewModel
{
    public LongFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c) { }

    public long Value
    {
        get => Record.GetLong(FieldName);
        set
        {
            // Honor optional bounds (declared as int) without truncating the 64-bit payload.
            if (Field.Min is { } mn && value < mn) value = mn;
            if (Field.Max is { } mx && value > mx) value = mx;
            if (value != Value) Commit(value);
            OnPropertyChanged(); // always notify so an out-of-range entry snaps back
        }
    }
}

/// <summary>Editor for a dual-typed skill value: a single number, or a per-level list typed as
/// "level:value" pairs (e.g. <c>1:3, 2:5, 3:7</c>). Round-trips to either YAML form.</summary>
public sealed class LevelIntFieldEditorViewModel : FieldEditorViewModel
{
    public LevelIntFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c) { }

    public string Value
    {
        get => Record.GetLevel(FieldName)?.ToCompactString() ?? string.Empty;
        set
        {
            var parsed = LevelList.Parse(value);
            Commit(parsed.IsEmpty ? null : parsed);
            OnPropertyChanged();
        }
    }

    public string Hint => $"A single value, or per-level as  1:{Field.LevelValueKey}…  e.g. \"1:3, 2:5, 3:7\".";
}

public sealed class BoolFieldEditorViewModel : FieldEditorViewModel
{
    public BoolFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c) { }

    public bool Value
    {
        get => Record.GetBool(FieldName);
        set { if (value != Value) { Commit(value); OnPropertyChanged(); } }
    }
}

public sealed class EnumFieldEditorViewModel : FieldEditorViewModel
{
    public EnumFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c)
    {
        // A record-dependent EnumSelector (e.g. SubType narrowed by Type) overrides the static Enum so the
        // dropdown only offers values valid for the current record. The form rebuilds when Type changes, so a
        // fresh editor (with the right options) is created.
        var src = f.EnumSelector?.Invoke(r) ?? f.Enum;
        Options = src is null
            ? Array.Empty<EnumOption>()
            : src.Values.Select(v => new EnumOption(v, src.Label(v))).ToArray();
    }

    /// <summary>The selectable values with their friendly labels (the UI shows the label, stores the value).</summary>
    public IReadOnlyList<EnumOption> Options { get; }

    public string? Value
    {
        get => Record.GetString(FieldName);
        set { if (value != Value) { Commit(value); OnPropertyChanged(); } }
    }
}

/// <summary>A selectable enum value paired with its friendly display label.</summary>
public readonly record struct EnumOption(string Value, string Label);

public sealed class ScriptFieldEditorViewModel : FieldEditorViewModel
{
    public ScriptFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c)
    {
        InsertBonusCommand = new RelayCommand(InsertBonus);
    }

    public string Value
    {
        get => Record.GetScript(FieldName)?.Text ?? string.Empty;
        set { if (value != Value) { Commit(new ScriptValue(value)); OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); } }
    }

    /// <summary>Opens the visual bonus builder and appends the generated statement to the script.</summary>
    public ICommand InsertBonusCommand { get; }

    private void InsertBonus()
    {
        if (!IsEditable) return;
        var dialog = new BonusBuilderDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            var current = Value;
            Value = string.IsNullOrWhiteSpace(current) ? dialog.Result : current.TrimEnd() + "\n" + dialog.Result;
        }
    }

    public override string Summary
    {
        get
        {
            var text = Value;
            if (string.IsNullOrWhiteSpace(text)) return "None";
            var line = text.Replace("\r", string.Empty).Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
            return line.Length > 50 ? line[..50] + "…" : line;
        }
    }

    public ScriptCommandCatalog? Catalog => Context.ScriptCatalog;
}

/// <summary>Read-only summary for any kind without a dedicated widget yet.</summary>
public sealed class SummaryFieldEditorViewModel : FieldEditorViewModel
{
    public SummaryFieldEditorViewModel(DbRecord r, FieldSchema f, FieldEditorContext c) : base(r, f, c) { }

    public override string Summary => Describe(Record.Get(FieldName));

    private static string Describe(object? value) => value switch
    {
        null => "(none)",
        ISet<string> set => set.Count == 0 ? "(none)" : string.Join(", ", set),
        IList<DbRecord> list => $"{list.Count} entr{(list.Count == 1 ? "y" : "ies")}",
        DbRecord obj => $"{obj.Values.Count} field(s)",
        IList scalars => $"{scalars.Count} item(s)",
        _ => value.ToString() ?? "(none)",
    };
}

public sealed class FieldGroupViewModel
{
    public FieldGroupViewModel(string title, IEnumerable<FieldEditorViewModel> fields)
    {
        Title = title;
        Fields = new ObservableCollection<FieldEditorViewModel>(fields);
    }

    public string Title { get; }

    public ObservableCollection<FieldEditorViewModel> Fields { get; }

    /// <summary>Scalar fields, laid out two-up in a wrap panel for density.</summary>
    public IEnumerable<FieldEditorViewModel> CompactFields => Fields.Where(f => !f.IsWide);

    /// <summary>Wide fields, stacked full-width below the compact grid.</summary>
    public IEnumerable<FieldEditorViewModel> WideFields => Fields.Where(f => f.IsWide);
}

public static class FieldEditorFactory
{
    public static FieldEditorViewModel Create(DbRecord record, FieldSchema field, FieldEditorContext ctx) => field.Kind switch
    {
        FieldKind.Int => new IntFieldEditorViewModel(record, field, ctx),
        FieldKind.Long => new LongFieldEditorViewModel(record, field, ctx),
        FieldKind.LevelInt => new LevelIntFieldEditorViewModel(record, field, ctx),
        FieldKind.Bool => new BoolFieldEditorViewModel(record, field, ctx),
        FieldKind.Enum => new EnumFieldEditorViewModel(record, field, ctx),
        FieldKind.Reference => new ReferenceFieldEditorViewModel(record, field, ctx),
        FieldKind.String => new StringFieldEditorViewModel(record, field, ctx),
        FieldKind.Script => new ScriptFieldEditorViewModel(record, field, ctx),
        FieldKind.Flags or FieldKind.BoolMap => new BoolMapFieldEditorViewModel(record, field, ctx),
        FieldKind.Object => new ObjectFieldEditorViewModel(record, field, ctx),
        FieldKind.ObjectList => new ObjectListFieldEditorViewModel(record, field, ctx),
        _ => new SummaryFieldEditorViewModel(record, field, ctx),
    };
}
