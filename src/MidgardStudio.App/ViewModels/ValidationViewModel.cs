using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.App.ViewModels;

/// <summary>Runs the cross-file validator and lists the advisory issues.</summary>
public sealed partial class ValidationViewModel : ObservableObject
{
    private readonly CrossFileValidator _validator;

    public ValidationViewModel(CrossFileValidator validator) => _validator = validator;

    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    [ObservableProperty]
    private string _summary = "Run a check to validate custom entries across the server and client files.";

    [RelayCommand]
    private void Run()
    {
        Issues.Clear();
        var found = _validator.Validate();
        foreach (var issue in found) Issues.Add(issue);
        Summary = found.Count == 0
            ? "No cross-file issues found in your custom/overridden entries."
            : $"{found.Count} issue(s) found.";
    }
}
