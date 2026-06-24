using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MidgardStudio.Core.Scripting;

namespace MidgardStudio.App.ViewModels;

/// <summary>One selectable value for an enum-typed bonus parameter (constant + friendly label).</summary>
public sealed record BonusOption(string Value, string Label);

/// <summary>An editable value-slot for one bonus parameter in the builder.</summary>
public sealed partial class BonusParamInput : ObservableObject
{
    public BonusParamInput(BonusParam param)
    {
        Param = param;
        _value = param.Default;
        Options = param.Enum is null
            ? Array.Empty<BonusOption>()
            : param.Enum.Values.Select(v => new BonusOption(v, param.Enum.Label(v))).ToList();
    }

    public BonusParam Param { get; }
    public string Name => Param.Name;
    public bool IsEnum => Param.Kind == BonusParamKind.Enum;
    public bool IsNumber => Param.Kind == BonusParamKind.Number;
    public bool IsText => Param.Kind == BonusParamKind.Text;
    public IReadOnlyList<BonusOption> Options { get; }

    [ObservableProperty] private string _value;

    public event Action? Changed;
    partial void OnValueChanged(string value) => Changed?.Invoke();
}

/// <summary>
/// Backs the visual bonus builder: a searchable list of curated bonuses, dynamic enum-aware parameter
/// editors for the selected one, and a live preview of the exact <c>bonus…;</c> statement to insert.
/// </summary>
public sealed partial class BonusBuilderViewModel : ObservableObject
{
    public BonusBuilderViewModel()
    {
        Definitions = new ObservableCollection<BonusDefinition>(BonusCatalog.All);
        _selected = BonusCatalog.All[0];
        BuildInputs();
    }

    public ObservableCollection<BonusDefinition> Definitions { get; }
    public ObservableCollection<BonusParamInput> Inputs { get; } = new();

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private BonusDefinition? _selected;
    [ObservableProperty] private string _preview = string.Empty;

    partial void OnSearchChanged(string value)
    {
        string q = value?.Trim().ToLowerInvariant() ?? string.Empty;
        Definitions.Clear();
        foreach (var d in BonusCatalog.All)
            if (q.Length == 0 || d.Search.Contains(q)) Definitions.Add(d);
        if (Selected is null || !Definitions.Contains(Selected))
            Selected = Definitions.FirstOrDefault();
    }

    partial void OnSelectedChanged(BonusDefinition? value) => BuildInputs();

    private void BuildInputs()
    {
        foreach (var input in Inputs) input.Changed -= Recompute;
        Inputs.Clear();

        if (Selected is not null)
            foreach (var p in Selected.Params)
            {
                var input = new BonusParamInput(p);
                input.Changed += Recompute;
                Inputs.Add(input);
            }

        Recompute();
    }

    private void Recompute() =>
        Preview = Selected is null
            ? string.Empty
            : BonusCatalog.Format(Selected, Inputs.Select(i => i.Value).ToList());
}
