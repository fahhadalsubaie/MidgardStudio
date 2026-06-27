using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Validation;
using MidgardStudio.Core.Validation.Validators;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Runs the workspace-wide validator (server correctness + cross-references + cross-file client/GRF
/// rules) on a background thread and presents the findings: filterable by severity, scoped to custom
/// entries or a full scan, with one-click quick-fixes and jump-to-record navigation.
/// </summary>
public sealed partial class ValidationViewModel : ObservableObject
{
    private readonly WorkspaceValidator _validator;
    private readonly WorkspaceSession _session;
    private readonly ClientSkillService _clientSkills;
    private List<ValidationIssue> _all = new();
    private bool _hasRun;
    private CancellationTokenSource? _refreshCts;

    public ValidationViewModel(WorkspaceValidator validator, WorkspaceSession session, ClientSkillService clientSkills)
    {
        _validator = validator;
        _session = session;
        _clientSkills = clientSkills;
    }

    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    /// <summary>Set by the shell so "Go to" / double-click jumps to the offending record (dbId, key).</summary>
    public Action<string, string>? Navigate { get; set; }

    /// <summary>Raised after a quick-fix runs so the shell can refresh the Save / modified indicator.</summary>
    public Action? FixApplied { get; set; }

    [ObservableProperty]
    private string _summary = "Run a check to validate your custom entries across the server and client files.";

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>When true, validate every record (including the read-only base data), not just customs.</summary>
    [ObservableProperty]
    private bool _fullScan;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _showWarnings = true;

    [ObservableProperty]
    private bool _showInfos = true;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _infoCount;

    private bool _runQueued;

    [RelayCommand]
    private async Task Run()
    {
        // Coalesce: if a (re-)validation is requested while one is already running, run once more after it
        // finishes instead of dropping it — otherwise an undo that lands mid-scan would leave the panel showing
        // stale, pre-undo results until the next manual run.
        if (IsRunning) { _runQueued = true; return; }
        IsRunning = true;
        try
        {
            do
            {
                _runQueued = false;
                Summary = "Validating…";
                var scope = FullScan ? ValidationScope.FullScan : ValidationScope.CustomOnly;
                var report = await Task.Run(() => _validator.Validate(scope));
                _all = report.Issues
                    .OrderByDescending(i => i.Severity)
                    .ThenBy(i => i.DbId, StringComparer.Ordinal)
                    .ToList();
                ErrorCount = report.ErrorCount;
                WarningCount = report.WarningCount;
                InfoCount = report.InfoCount;
                ApplyFilter();
                _hasRun = true;
            }
            while (_runQueued); // a refresh arrived while validating — re-scan against the latest model
        }
        finally
        {
            IsRunning = false;
        }

        // Re-sync the Save / modified indicator from the real dirty sources once this run settles. Every
        // command already fires OnCommandsChanged, but a quick-fix's re-validation (and the debounced
        // re-validation after an undo) complete asynchronously — this guarantees the indicator reflects the
        // settled truth and is never left stale by an async-completion race.
        FixApplied?.Invoke();
    }

    /// <summary>Re-runs validation shortly after the data changes underneath the panel (e.g. an undo/redo
    /// of a quick-fix), coalescing a burst of changes into one re-scan so the counts + list stay live.
    /// No-op until the panel has been run at least once.</summary>
    public async void RefreshAfterChange()
    {
        if (!_hasRun) return;
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        try
        {
            await Task.Delay(200, cts.Token); // debounce Ctrl+Z spam
            if (!cts.IsCancellationRequested) await Run();
        }
        catch (TaskCanceledException) { /* superseded by a newer change */ }
    }

    partial void OnShowErrorsChanged(bool value) => ApplyFilter();
    partial void OnShowWarningsChanged(bool value) => ApplyFilter();
    partial void OnShowInfosChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Issues.Clear();
        foreach (var issue in _all)
        {
            bool show = issue.Severity switch
            {
                ValidationSeverity.Error => ShowErrors,
                ValidationSeverity.Warning => ShowWarnings,
                _ => ShowInfos,
            };
            if (show) Issues.Add(issue);
        }

        Summary = _all.Count == 0
            ? "No issues found in your custom/overridden entries."
            : $"{ErrorCount} error(s), {WarningCount} warning(s), {InfoCount} info — showing {Issues.Count}.";
    }

    [RelayCommand]
    private async Task ApplyFix(ValidationIssue? issue)
    {
        if (issue?.Fix is not { } fix) return;

        // Route the fix through the undo stack (when it's reversible) so it gets undo/redo and lights the
        // Save indicator just like a manual edit. Fixes that aren't reversible (e.g. registering a sprite to
        // disk) run directly.
        if (fix.Revert is { } revert)
            _session.Commands.Execute(new ListMutateCommand("Quick fix: " + fix.Title,
                () => { fix.Apply(); NotifyServices(issue); },
                () => { revert(); NotifyServices(issue); }));
        else
        {
            fix.Apply();
            NotifyServices(issue);
        }

        FixApplied?.Invoke();
        await RunCommand.ExecuteAsync(null); // re-validate so the list reflects the fix
    }

    /// <summary>Core client-skill fixes mutate the POCO directly; poke the service so its dirty signature updates.</summary>
    private void NotifyServices(ValidationIssue issue)
    {
        if (issue.DbId == ClientSkillValidator.DbId && !string.IsNullOrEmpty(issue.Key))
            _clientSkills.NotifyEdited(issue.Key);
    }

    [RelayCommand]
    private void Open(ValidationIssue? issue)
    {
        if (issue is not null) Navigate?.Invoke(issue.DbId, issue.Key);
    }
}
