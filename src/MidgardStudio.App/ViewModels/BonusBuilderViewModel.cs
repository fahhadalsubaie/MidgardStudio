using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
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
    public string? Hint => Param.Hint;
    public bool HasHint => !string.IsNullOrEmpty(Param.Hint);
    public bool IsEnum => Param.Kind == BonusParamKind.Enum;
    public bool IsNumber => Param.Kind == BonusParamKind.Number;
    public bool IsText => Param.Kind == BonusParamKind.Text;
    public bool IsSkill => Param.Kind == BonusParamKind.Skill;
    public IReadOnlyList<BonusOption> Options { get; }

    [ObservableProperty] private string _value;

    public event Action? Changed;
    partial void OnValueChanged(string value) => Changed?.Invoke();
}

/// <summary>One added line in the script being assembled (the generated code + its friendly label).</summary>
public sealed record RecipeLine(string Code, string Label);

/// <summary>
/// Backs the visual <b>Script Generator</b>: browse curated effects by category, search them, configure
/// the selected one with enum-aware / percentage-friendly inputs, watch the exact <c>bonus…;</c> line
/// update live, then stack several effects into a recipe that's inserted into the item script at once.
/// </summary>
public sealed partial class BonusBuilderViewModel : ObservableObject
{
    private const string AllCategories = "All effects";
    private readonly SkillLookupService? _skillLookup;

    public BonusBuilderViewModel(SkillLookupService? skillLookup = null)
    {
        _skillLookup = skillLookup;
        Categories = new ObservableCollection<string>(new[] { AllCategories }.Concat(BonusCatalog.Categories));
        _selectedCategory = AllCategories;
        Definitions = new ObservableCollection<BonusDefinition>();
        ApplyFilter();
    }

    /// <summary>True when a skill picker is available (skill_db loaded) for skill-typed parameters.</summary>
    public bool CanPickSkill => _skillLookup is not null;

    [RelayCommand]
    private void PickSkill(BonusParamInput? input)
    {
        if (input is null || _skillLookup is null) return;
        var dlg = new Views.SkillPickerDialog(_skillLookup) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && dlg.Selected is { } s)
            input.Value = $"\"{s.Aegis}\"";
    }

    public ObservableCollection<string> Categories { get; }
    public ObservableCollection<BonusDefinition> Definitions { get; }
    public ObservableCollection<BonusParamInput> Inputs { get; } = new();
    public ObservableCollection<RecipeLine> Recipe { get; } = new();

    [ObservableProperty] private string _selectedCategory;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private BonusDefinition? _selected;
    [ObservableProperty] private string _preview = string.Empty;

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnSelectedChanged(BonusDefinition? value) => BuildInputs();

    public bool HasRecipe => Recipe.Count > 0;
    public bool HasSelection => Selected is not null;
    public string FullScript => string.Join("\n", Recipe.Select(r => r.Code));

    /// <summary>What the dialog inserts: the whole recipe, or the single configured effect if none was added.</summary>
    public string Result => HasRecipe ? FullScript : Preview;

    public string RecipeCountText => Recipe.Count switch
    {
        0 => "No effects added yet — configure one and press “Add to script”.",
        1 => "1 effect",
        var n => $"{n} effects",
    };

    private void ApplyFilter()
    {
        string q = (Search ?? string.Empty).Trim().ToLowerInvariant();
        Definitions.Clear();
        foreach (var d in BonusCatalog.All)
        {
            if (SelectedCategory != AllCategories && d.Category != SelectedCategory) continue;
            if (q.Length > 0 && !d.Search.Contains(q)) continue;
            Definitions.Add(d);
        }
        if (Selected is null || !Definitions.Contains(Selected))
            Selected = Definitions.FirstOrDefault();
    }

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

        OnPropertyChanged(nameof(HasSelection));
        Recompute();
    }

    private void Recompute() =>
        Preview = Selected is null ? string.Empty : BonusCatalog.Format(Selected, Inputs.Select(i => i.Value).ToList());

    [RelayCommand]
    private void AddEffect()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(Preview)) return;
        Recipe.Add(new RecipeLine(Preview, Selected.Display));
        RaiseRecipeChanged();
    }

    [RelayCommand]
    private void RemoveLine(RecipeLine? line)
    {
        if (line is not null && Recipe.Remove(line)) RaiseRecipeChanged();
    }

    [RelayCommand]
    private void ClearRecipe()
    {
        if (Recipe.Count == 0) return;
        Recipe.Clear();
        RaiseRecipeChanged();
    }

    private void RaiseRecipeChanged()
    {
        OnPropertyChanged(nameof(HasRecipe));
        OnPropertyChanged(nameof(FullScript));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(RecipeCountText));
    }
}
