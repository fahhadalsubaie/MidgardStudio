using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// The schema-generated detail form for one record. Renders fields grouped into sections, routes
/// edits through the undo stack (creating an import override on first edit of a core record), and
/// keeps a live YAML preview of exactly what will be written to the import layer.
/// </summary>
public sealed partial class RecordEditorViewModel : ObservableObject
{
    private readonly OverlayTable _table;
    private readonly EditCommandStack _stack;
    private readonly IReferenceResolver? _references;
    private readonly ScriptCommandCatalog? _catalog;
    private readonly Core.Workspace.ServerMode _mode;
    private RecordKey _key;

    public RecordEditorViewModel(OverlayTable table, EditCommandStack stack,
        IReferenceResolver? references = null, ScriptCommandCatalog? catalog = null,
        Core.Workspace.ServerMode mode = Core.Workspace.ServerMode.Renewal)
    {
        _table = table;
        _stack = stack;
        _references = references;
        _catalog = catalog;
        _mode = mode;
    }

    public ObservableCollection<FieldGroupViewModel> Groups { get; } = new();

    /// <summary>Groups split across two side-by-side cards (balanced by field count) so the form fits without scrolling.</summary>
    public ObservableCollection<FieldGroupViewModel> LeftColumn { get; } = new();
    public ObservableCollection<FieldGroupViewModel> RightColumn { get; } = new();

    [ObservableProperty]
    private bool _hasRecord;

    [ObservableProperty]
    private bool _isEditable;

    [ObservableProperty]
    private string _origin = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _yamlPreview = string.Empty;

    /// <summary>Raised when the record's effective content changes (so the list row can refresh).</summary>
    public event Action? RecordChanged;

    /// <summary>Raised when a base record is overridden (so dependent cards can unlock for editing).</summary>
    public event Action? OverrideCreated;

    public void Load(RecordKey key)
    {
        _key = key;
        Build();
    }

    public void Clear()
    {
        Groups.Clear();
        HasRecord = false;
        Title = string.Empty;
        YamlPreview = string.Empty;
    }

    [RelayCommand]
    private void CreateOverride()
    {
        if (_table.OriginOf(_key) != RecordOrigin.Base) return;

        // Route through the undo stack so it's undoable AND marks the session modified (changes indicator
        // + Save light up). The clone is captured so redo re-adds the same instance — keeping any later
        // field-edit commands (which reference that record) valid across undo/redo.
        DbRecord? clone = null;
        _stack.Execute(new ListMutateCommand("Create override",
            () => { if (clone is null) clone = _table.BeginOverride(_key); else _table.AddCustom(clone); },
            () => _table.RevertToCore(_key)));

        Build();
        RecordChanged?.Invoke();
        OverrideCreated?.Invoke();
    }

    private void Build()
    {
        Groups.Clear();
        LeftColumn.Clear();
        RightColumn.Clear();

        var record = _table.GetEffective(_key);
        if (record is null)
        {
            HasRecord = false;
            return;
        }

        HasRecord = true;
        var origin = _table.OriginOf(_key);
        IsEditable = origin != RecordOrigin.Base;
        Origin = origin.ToString();
        Title = $"{record.GetString("AegisName")}  ·  #{_key}";

        var context = new FieldEditorContext
        {
            Stack = _stack,
            IsEditable = IsEditable,
            Mode = _mode,
            References = _references,
            ScriptCatalog = _catalog,
        };

        var schema = record.Schema;
        bool couple = schema.Field("Name") is not null && schema.Field("AegisName") is not null;
        CoupledNameFieldEditorViewModel? nameVm = null, aegisVm = null;

        foreach (var group in schema.Fields
                     .Where(f => !f.HideInForm)
                     .Where(f => InActiveSystem(f))
                     .Where(f => f.IsApplicable?.Invoke(record) ?? true)
                     .GroupBy(f => f.Group ?? "General"))
        {
            var fields = new List<FieldEditorViewModel>();
            foreach (var field in group)
            {
                FieldEditorViewModel vm;
                if (couple && field.Name == "Name")
                    vm = nameVm = new CoupledNameFieldEditorViewModel(record, field, context, "AegisName", toAegis: true);
                else if (couple && field.Name == "AegisName")
                    vm = aegisVm = new CoupledNameFieldEditorViewModel(record, field, context, "Name", toAegis: false);
                else
                    vm = FieldEditorFactory.Create(record, field, context);

                vm.Changed += OnFieldChanged;
                fields.Add(vm);
            }
            Groups.Add(new FieldGroupViewModel(group.Key, fields));
        }

        if (nameVm is not null && aegisVm is not null)
        {
            nameVm.Sibling = aegisVm;
            aegisVm.Sibling = nameVm;
        }

        // Balance groups across two columns by field count.
        int left = 0, right = 0;
        foreach (var g in Groups)
        {
            if (left <= right) { LeftColumn.Add(g); left += g.Fields.Count; }
            else { RightColumn.Add(g); right += g.Fields.Count; }
        }

        UpdatePreview(record);
    }

    /// <summary>
    /// Hides fields that belong to the other ruleset (e.g. Gradable/MagicAttack are Renewal-only, so
    /// they don't appear in a Pre-Renewal profile). The value is preserved in the record either way.
    /// </summary>
    private bool InActiveSystem(Core.Schema.FieldSchema f) => f.Renewal switch
    {
        Core.Schema.RenewalScope.RenewalOnly => _mode == Core.Workspace.ServerMode.Renewal,
        Core.Schema.RenewalScope.PreRenewalOnly => _mode == Core.Workspace.ServerMode.PreRenewal,
        _ => true,
    };

    private void OnFieldChanged()
    {
        var record = _table.GetEffective(_key);
        if (record is not null) UpdatePreview(record);
        RecordChanged?.Invoke();
    }

    private void UpdatePreview(DbRecord record)
    {
        var file = new DbFile { HeaderType = record.Schema.HeaderType, HeaderVersion = record.Schema.HeaderVersion };
        file.Records.Add(record);
        YamlPreview = new YamlDbWriter().WriteToString(record.Schema, file);
    }
}
