using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Workspace;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Root view model for the application shell: database navigation, the Renewal/Pre-Renewal mode
/// toggle, the hosted content (editable workspace or placeholder), and global save/undo/redo.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly IWorkspaceConfigService _configService;
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private readonly ReferenceResolver _references;
    private readonly GrfBrowserViewModel _grfBrowser;
    private readonly ValidationViewModel _validation;
    private readonly WorkspaceValidator _validator;
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService _sprite;
    private readonly MobSpriteService _mobSprite;
    private readonly DropService _drops;
    private readonly BackupService _backups;
    private readonly MapCacheService _mapCache;
    private readonly AppSettingsService _appSettings;
    private readonly ConfigurationWizardViewModel _wizard;
    private readonly OnboardingViewModel _onboarding;
    private readonly Dictionary<string, DbWorkspaceViewModel> _workspaces = new(StringComparer.Ordinal);
    private readonly System.Windows.Threading.DispatcherTimer _intervalSaveTimer = new();
    private readonly System.Windows.Threading.DispatcherTimer _editSaveTimer = new();
    private ClientItemsViewModel? _clientItemsVm;
    private ComboEditorViewModel? _comboVm;
    private BackupManagerViewModel? _backupVm;
    private ForgeViewModel? _forgeVm;
    private SettingsViewModel? _settingsVm;
    private MapCacheEditorViewModel? _mapCacheVm;
    private bool _suppressModeReload;

    public ObservableCollection<DbSectionViewModel> Sections { get; } = new();

    [ObservableProperty]
    private DbSectionViewModel? _selectedSection;

    [ObservableProperty]
    private bool _isRenewal = true;

    [ObservableProperty]
    private object? _currentContent;

    /// <summary>The active editable DB workspace (null on Client Items / GRF / Validation / Backup),
    /// surfaced so the top bar can host its record actions (YAML / Port / Create override).</summary>
    [ObservableProperty]
    private DbWorkspaceViewModel? _activeWorkspace;

    /// <summary>The active Item Combos editor (null elsewhere) — lets the top bar host its New/Override actions.</summary>
    [ObservableProperty]
    private ComboEditorViewModel? _activeCombo;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private bool _showWizard;

    [ObservableProperty]
    private bool _showOnboarding;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    public ObservableCollection<PaletteResultViewModel> PaletteResults { get; } = new();

    /// <summary>Saved profiles for the File ▸ Switch profile submenu (active one flagged).</summary>
    public ObservableCollection<ProfileMenuItemViewModel> Profiles { get; } = new();

    /// <summary>The first-run / profile-management screen, shown over the shell when <see cref="ShowWizard"/>.</summary>
    public ConfigurationWizardViewModel Wizard => _wizard;

    /// <summary>The one-time onboarding tour, shown over everything on first run (<see cref="ShowOnboarding"/>).</summary>
    public OnboardingViewModel Onboarding => _onboarding;

    public string ModeLabel => IsRenewal ? "Renewal" : "Pre-Renewal";

    /// <summary>Name of the active profile, shown in the menu-bar status strip.</summary>
    public string ActiveProfileName => _session.Config.Name;

    public ShellViewModel(IWorkspaceConfigService config, SchemaRegistry schemas, WorkspaceSession session,
        ReferenceResolver references, GrfBrowserViewModel grfBrowser, ClientItemService clientItems,
        GrfImageService images, SpriteLinkService sprite, MobSpriteService mobSprite, ValidationViewModel validation,
        WorkspaceValidator validator,
        DropService drops, BackupService backups, MapCacheService mapCache, AppSettingsService appSettings,
        ConfigurationWizardViewModel wizard, OnboardingViewModel onboarding)
    {
        _configService = config;
        _schemas = schemas;
        _session = session;
        _references = references;
        _grfBrowser = grfBrowser;
        _validation = validation;
        _validator = validator;
        _clientItems = clientItems;
        _images = images;
        _sprite = sprite;
        _mobSprite = mobSprite;
        _drops = drops;
        _backups = backups;
        _mapCache = mapCache;
        _appSettings = appSettings;
        _wizard = wizard;
        _onboarding = onboarding;
        _onboarding.Completed += () =>
        {
            // The tour runs exactly once: record that it's been seen, then reveal whatever sits beneath it
            // (the configuration wizard on a first run, or the loaded workspace).
            _appSettings.Settings.HasSeenOnboarding = true;
            _appSettings.Save();
            ShowOnboarding = false;
        };

        _intervalSaveTimer.Interval = TimeSpan.FromSeconds(60); // safe default; ApplySaveMode overrides it
        _intervalSaveTimer.Tick += (_, _) => { if (IsModified) DoSave(createBackup: false, interactive: false); };
        _editSaveTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _editSaveTimer.Tick += (_, _) => { _editSaveTimer.Stop(); if (IsModified) DoSave(createBackup: false, interactive: false); };
        _wizard.OpenRequested += cfg => ApplyProfile(cfg);
        _wizard.CloseRequested += () =>
        {
            // Closing returns to the workspace already loaded behind the overlay. On a genuine first run there
            // is nothing valid to return to, so closing exits the app — but confirm first so an accidental Esc
            // or ✕ during setup can't quit the whole app without warning.
            var profile = _configService.ActiveProfile;
            if (profile is not null && profile.Paths.AllExist())
                ShowWizard = false;
            else if (Views.ConfirmDialog.Show("Quit Midgard Studio?",
                         "No workspace is configured yet, so closing setup will exit Midgard Studio.", yes: "Quit"))
                Exit();
        };
        _validation.Navigate = NavigateToIssue;

        _session.Commands.Changed += OnCommandsChanged;

        var active = config.ActiveProfile;
        if (active is not null && active.Paths.AllExist())
        {
            // A valid profile is saved — go straight into it; the wizard stays one click away.
            ApplyProfile(active, persist: false);
        }
        else
        {
            // First run (or the saved paths are gone): present the Configuration Wizard.
            ShowWizard = true;
            BuildSections((active ?? config.Load()).Paths);
        }

        RefreshProfiles();
        ApplySaveMode();

        // Onboarding runs once, the first time the app is ever opened, layered above the wizard/workspace.
        ShowOnboarding = !_appSettings.Settings.HasSeenOnboarding;
    }

    /// <summary>Rebuilds the Switch-profile submenu from disk and flags the active profile.</summary>
    private void RefreshProfiles()
    {
        Profiles.Clear();
        string activeName = _session.Config.Name;
        foreach (var cfg in _configService.GetProfiles())
            Profiles.Add(new ProfileMenuItemViewModel(cfg,
                string.Equals(cfg.Name, activeName, StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(ActiveProfileName));
    }

    private void OnCommandsChanged()
    {
        IsModified = _session.Commands.IsModified;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged(); // grey the Save button out when there's nothing to write

        // Auto-save-on-edit: debounce so a burst of edits results in a single write.
        if (_appSettings.Settings.SaveMode == SaveMode.OnEdit && IsModified)
        {
            _editSaveTimer.Stop();
            _editSaveTimer.Start();
        }
    }

    /// <summary>Re-arms the auto-save timers to match the current save-mode preference.</summary>
    private void ApplySaveMode()
    {
        _intervalSaveTimer.Stop();
        _editSaveTimer.Stop();

        if (_appSettings.Settings.SaveMode == SaveMode.Interval)
        {
            _intervalSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, _appSettings.Settings.SaveIntervalSeconds));
            _intervalSaveTimer.Start();
        }
    }

    private void BuildSections(WorkspacePaths p)
    {
        string db = p.ServerDbRoot;
        string re(string file) => Path.Combine(db, "re", file);

        Sections.Add(new("items", "Items", SymbolRegular.BoxMultiple24,
            "Server item database (item_db): stats, bonuses, restrictions and drop sources.",
            re("item_db_equip.yml"), schemaId: "item_db"));
        Sections.Add(new("client_items", "Client Items", SymbolRegular.Image24,
            "Client item info (itemInfo.lua / itemInfo_C.lua): names, descriptions, slots, view and sprite.",
            string.Empty));
        Sections.Add(new("mobs", "Mobs", SymbolRegular.Bug24,
            "Monster database (mob_db) with drops, modes and client sprite registration.",
            re("mob_db.yml"), schemaId: "mob_db"));
        Sections.Add(new("mob_avail", "Mob Sprite Reuse", SymbolRegular.ArrowSwap24,
            "Reuse another mob's (or a job) sprite for a mob (mob_avail).",
            Path.Combine(db, "import", "mob_avail.yml"), schemaId: "mob_avail"));
        Sections.Add(new("pets", "Pets", SymbolRegular.Heart24,
            "Pet database (pet_db): taming, intimacy, evolution and bonus scripts.",
            re("pet_db.yml"), schemaId: "pet_db"));
        Sections.Add(new("combos", "Item Combos", SymbolRegular.Link24,
            "Item set combos (item_combos) and their bonus scripts.",
            re("item_combos.yml"), schemaId: "item_combos"));
        Sections.Add(new("groups", "Item Groups", SymbolRegular.Folder24,
            "Item group pools (item_group_db) with sub-groups and rates.",
            re("item_group_db.yml"), schemaId: "item_group_db"));
        Sections.Add(new("skills", "Skills", SymbolRegular.Flash24,
            "Skill database (skill_db).",
            re("skill_db.yml"), schemaId: "skill_db"));
        Sections.Add(new("achievements", "Achievements", SymbolRegular.Trophy24,
            "Achievement database (achievement_db): targets, dependencies and rewards.",
            re("achievement_db.yml"), schemaId: "achievement_db"));
        Sections.Add(new("abra", "Class Change", SymbolRegular.Sparkle24,
            "Abracadabra / Hocus-Pocus random skill table (abra_db).",
            Path.Combine(db, "abra_db.yml"), schemaId: "abra_db"));
        Sections.Add(new("summon", "Summon Groups", SymbolRegular.Star24,
            "Summon group pools (mob_summon): Bloody Branch, Dead Branch, Class Change, Poring Box, ...",
            re("mob_summon.yml"), schemaId: "mob_summon"));
        Sections.Add(new("grf", "GRF Browser", SymbolRegular.FolderZip24,
            "Browse client GRF archives: preview icons/sprites and read lua files.",
            "data\\luafiles514\\lua files"));
        Sections.Add(new("validation", "Validation", SymbolRegular.Checkmark24,
            "Cross-file consistency checks for your custom and overridden entries.",
            string.Empty));
    }

    partial void OnSelectedSectionChanged(DbSectionViewModel? value) => RebuildContent();

    private void RebuildContent()
    {
        var section = SelectedSection;
        ActiveWorkspace = null; // cleared for non-DB screens; set below when a workspace is shown
        ActiveCombo = null;     // set below only on the Item Combos screen

        if (section?.Key == "grf")
        {
            _grfBrowser.RefreshFromConfig();
            CurrentContent = _grfBrowser;
            return;
        }

        if (section?.Key == "validation")
        {
            CurrentContent = _validation;
            return;
        }

        if (section?.Key == "client_items")
        {
            _clientItemsVm ??= new ClientItemsViewModel(_session, _clientItems, _images, _sprite, _appSettings,
                _schemas.Get("item_db")!, NavigateTo, SyncItemsWorkspace);
            CurrentContent = _clientItemsVm;
            _ = _clientItemsVm.EnsureLoadedAsync();
            return;
        }

        if (section?.Key == "combos")
        {
            _comboVm ??= new ComboEditorViewModel(_session, _schemas.Get("item_combos")!, _drops);
            ActiveCombo = _comboVm;
            CurrentContent = _comboVm;
            _ = _comboVm.EnsureLoadedAsync();
            return;
        }

        if (section?.SchemaId is { } id && _schemas.Get(id) is { } schema)
        {
            if (!_workspaces.TryGetValue(id, out var workspace))
            {
                workspace = new DbWorkspaceViewModel(_session, schema, _references, _clientItems, _images,
                    _mobSprite, _drops, NavigateTo);
                _workspaces[id] = workspace;
            }

            ActiveWorkspace = workspace;
            CurrentContent = workspace;
            _ = workspace.EnsureLoadedAsync();
        }
        else
        {
            CurrentContent = section;
        }
    }

    partial void OnIsRenewalChanged(bool value)
    {
        if (_suppressModeReload) return;
        _session.SetMode(value ? ServerMode.Renewal : ServerMode.PreRenewal);
        // Mode sets are cached, so rebuilding the active workspace against the new mode is instant.
        DisposeWorkspaces();
        RebuildContent();
        OnPropertyChanged(nameof(ModeLabel));
    }

    /// <summary>Applies a workspace profile: swaps the session's data, reconfigures GRF, rebuilds the
    /// navigation against the new paths, and leaves the wizard.</summary>
    private void ApplyProfile(WorkspaceConfig cfg, bool persist = true)
    {
        // Switching profiles reloads a different server and clears the undo stack — confirm first if
        // there are unsaved edits (only happens when re-opening the wizard mid-session).
        if (persist && _session.Commands.IsModified)
        {
            if (!Views.ConfirmDialog.Show("Unsaved changes",
                    "You have unsaved changes. Switching profiles will discard them. Continue?",
                    yes: "Discard & switch")) return;
        }

        if (persist)
        {
            cfg.LastOpenedUtc = DateTime.UtcNow;
            _configService.UpsertProfile(cfg);
            _configService.SetActiveProfile(cfg.Name);
        }

        DisposeWorkspaces();
        _session.ApplyProfile(cfg);
        _grfBrowser.RefreshFromConfig();

        _suppressModeReload = true;
        IsRenewal = cfg.DefaultMode == ServerMode.Renewal;
        _suppressModeReload = false;
        OnPropertyChanged(nameof(ModeLabel));

        Sections.Clear();
        BuildSections(cfg.Paths);
        ShowWizard = false;
        SelectedSection = Sections.Count > 0 ? Sections[0] : null;
        RefreshProfiles();
    }

    [RelayCommand]
    private void SwitchProfile(ProfileMenuItemViewModel? item)
    {
        if (item is null || item.IsActive) return;
        ApplyProfile(item.Config);
    }

    [RelayCommand]
    private void GoSection(string? key)
    {
        var section = Sections.FirstOrDefault(s => s.Key == key);
        if (section is not null) SelectedSection = section;
    }

    [RelayCommand]
    private void RevealImportFolder() =>
        OpenInExplorer(Path.Combine(_session.Paths.ServerDbRoot, "import"));

    [RelayCommand]
    private void OpenLuaFolder() => OpenInExplorer(_session.Paths.LuaFilesRoot);

    [RelayCommand]
    private void OpenDocs()
    {
        try
        {
            // Open the project repository (docs + releases) in the default browser.
            Process.Start(new ProcessStartInfo("https://github.com/fahhadalsubaie/MidgardStudio") { UseShellExecute = true });
        }
        catch { /* shell-open failure is non-fatal */ }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { /* shell-open failure is non-fatal */ }
    }

    [RelayCommand]
    private void ReloadData()
    {
        if (_session.Commands.IsModified)
        {
            if (!Views.ConfirmDialog.Show("Reload from disk",
                    "Reload all data from disk? Unsaved changes will be discarded.",
                    yes: "Discard & reload")) return;
        }

        _session.ApplyProfile(_session.Config); // clears caches + undo so the next read re-loads from disk
        DisposeWorkspaces();
        RebuildContent();
    }

    [RelayCommand]
    private void EnableRenewal() => IsRenewal = true;

    [RelayCommand]
    private void EnablePreRenewal() => IsRenewal = false;

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    [RelayCommand]
    private void About() => Views.AboutDialog.ShowAbout();

    [RelayCommand]
    private void OpenWizard()
    {
        _wizard.Refresh();
        ShowWizard = true;
    }

    private void DisposeWorkspaces()
    {
        foreach (var workspace in _workspaces.Values) workspace.Dispose();
        _workspaces.Clear();
        _clientItemsVm?.Dispose();
        _clientItemsVm = null; // rebuilt against the new mode/profile on next visit
        _comboVm?.Dispose();
        _comboVm = null;
        _forgeVm = null;
        _mapCacheVm = null;
    }

    /// <summary>Re-syncs the server Items list with the overlay (used after Client Items mutates the shared
    /// item data, so both lists stay consistent).</summary>
    private void SyncItemsWorkspace()
    {
        if (_workspaces.TryGetValue("item_db", out var ws)) ws.List?.SyncWithOverlay();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => DoSave(createBackup: true, showSummary: true, gated: true);

    /// <summary>True when there are server or client edits not yet written to disk. Client dirtiness is a
    /// content comparison (<see cref="ClientItemService.IsDirty"/>), so undoing a client edit back to
    /// baseline correctly clears it instead of leaving Save lit.</summary>
    public bool HasUnsavedChanges => _session.Commands.IsModified || _clientItems.IsDirty;

    /// <summary>Gate for the Save command — disabled (greyed out) when nothing has changed.</summary>
    private bool CanSave() => HasUnsavedChanges;

    /// <summary>Writes pending changes. Returns false if the save failed (data is kept in the editor).
    /// Manual saves take a dated backup; auto-saves don't (to avoid spam). When <paramref name="showSummary"/>
    /// is set, a centered dialog reports what was written.</summary>
    private bool DoSave(bool createBackup, bool showSummary = false, bool gated = false, bool interactive = true)
    {
        // Capture what's about to change (id + import path) so the backup/summary can label themselves;
        // the dirty flags are cleared by SaveAll, so this must run first.
        var saveTargets = _session.DirtySaveTargets();
        bool clientDirty = _clientItems.IsDirty;
        if (saveTargets.Count == 0 && !clientDirty)
        {
            IsModified = _session.Commands.IsModified;
            SaveCommand.NotifyCanExecuteChanged();
            return true;
        }

        // Only a manual save is gated. Auto-save timers and exit-save pass gated:false and are never blocked.
        if (gated && !RunSaveGate(saveTargets, clientDirty))
            return false;

        try
        {
            _session.SaveAll();      // server import YAML (only modified DBs are rewritten)
            _clientItems.Save();     // client itemInfo_C.lua (spliced in place — never overwrites functions)
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Save failed");
            // Never report a failed/partial save as clean: keep whatever is still dirty so the user can retry.
            IsModified = _session.Commands.IsModified || _clientItems.IsDirty;
            SaveCommand.NotifyCanExecuteChanged();
            // Background auto-save (interval/on-edit) must not pop a modal every tick when a file is locked
            // (e.g. a running server) — it just logs and leaves the Save button lit. Only interactive saves alert.
            if (interactive)
                Views.ConfirmDialog.Alert("Save failed",
                    "Your changes were NOT fully saved and are still here in the editor:\n\n" + ex.Message +
                    "\n\nClose anything that might be using these files (for example a running server), then save again.");
            return false;
        }

        // OR client dirtiness in so a half-failed save can never be shown as a clean state.
        IsModified = _session.Commands.IsModified || _clientItems.IsDirty;
        SaveCommand.NotifyCanExecuteChanged();

        // The list of files this save wrote, with friendly labels — reused for the backup note + summary.
        var written = saveTargets
            .Select(t => new Views.SaveSummaryDialog.SavedFile(_schemas.Get(t.Id)?.DisplayName ?? t.Id, t.ImportFilePath))
            .ToList();
        if (clientDirty) written.Add(new("Client items", _clientItems.SaveTargetPath));

        // Snapshot the freshly-saved state into a dated, self-describing backup (manual saves only).
        if (createBackup)
        {
            var names = written.Select(w => w.Label).ToList();
            _backups.CreateBackup("Save · " + string.Join(", ", names),
                $"Automatic backup taken after saving: {string.Join(", ", names)}.");
            _backupVm?.RefreshCommand.Execute(null);
        }

        RefreshProfiles();       // keep the active-profile flag fresh

        if (showSummary)
        {
            string summary = written.Count == 1
                ? "1 file was written to disk."
                : $"{written.Count} files were written to disk.";
            Views.SaveSummaryDialog.Show(summary, written);
        }

        return true;
    }

    /// <summary>Saves for an exiting window. Returns true if it's safe to close (saved or nothing pending).
    /// No summary dialog here — the window is on its way out, and exit-save is never gated.</summary>
    public bool SaveForExit() => DoSave(createBackup: true, showSummary: false);

    /// <summary>The soft/hard save gate. If the databases about to be written contain Error-level validation
    /// issues, prompts (soft gate) or blocks (hard gate). Returns true to proceed with the save. Validation
    /// failures themselves never block a save.</summary>
    private bool RunSaveGate(IReadOnlyList<(string Id, string ImportFilePath)> saveTargets, bool clientDirty)
    {
        var settings = _appSettings.Settings;
        if (!settings.ValidateOnSave || settings.SaveGate == ValidationGateMode.Advisory)
            return true;

        var dbIds = saveTargets.Select(t => t.Id).ToList();
        if (clientDirty && !dbIds.Contains("item_db")) dbIds.Add("item_db");
        if (dbIds.Count == 0) return true;

        MidgardStudio.Core.Validation.ValidationReport report;
        try { report = _validator.ValidateDatabases(dbIds); }
        catch { return true; }

        if (!report.HasErrors) return true;

        string message = BuildGateMessage(report);
        if (settings.SaveGate == ValidationGateMode.HardGate)
        {
            Views.ConfirmDialog.Alert("Validation errors",
                message + "\n\nFix these Error-level issues before saving (hard gate is enabled).");
            return false;
        }

        return Views.ConfirmDialog.Show("Validation errors", message, yes: "Save anyway", no: "Fix first");
    }

    private static string BuildGateMessage(MidgardStudio.Core.Validation.ValidationReport report)
    {
        var errors = report.Issues
            .Where(i => i.Severity == MidgardStudio.Core.Validation.ValidationSeverity.Error)
            .ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{errors.Count} error(s) were found in the entries you're about to save:");
        sb.AppendLine();
        foreach (var e in errors.Take(10))
            sb.AppendLine($"•  [{e.DbId} #{e.Key}]  {e.Message}");
        if (errors.Count > 10) sb.AppendLine($"…and {errors.Count - 10} more.");
        return sb.ToString();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _settingsVm ??= new SettingsViewModel(_appSettings, OnSettingsChanged);
        ActiveWorkspace = null;
        CurrentContent = _settingsVm;
    }

    private void OnSettingsChanged()
    {
        ApplySaveMode();
        ShortcutsChanged?.Invoke();
    }

    /// <summary>Raised when the user edits a keyboard shortcut (the window rebuilds its input bindings).</summary>
    public event Action? ShortcutsChanged;

    private ICommand? CommandFor(string key) => key switch
    {
        "Save" => SaveCommand,
        "Undo" => UndoCommand,
        "Redo" => RedoCommand,
        "NewEntry" => NewEntryCommand,
        "Duplicate" => DuplicateEntryCommand,
        "CopyYaml" => CopyEntryYamlCommand,
        "Forge" => OpenForgeCommand,
        "FindInList" => FocusListSearchCommand,
        "FindEverywhere" => FindEverywhereCommand,
        "QuickOpen" => OpenPaletteCommand,
        "Reload" => ReloadDataCommand,
        "Validate" => OpenValidationCommand,
        "ToggleMode" => ToggleModeCommand,
        "BackupManager" => OpenBackupManagerCommand,
        "Settings" => OpenSettingsCommand,
        "Configuration" => OpenWizardCommand,
        _ => null,
    };

    /// <summary>Duplicates the active workspace's selected entry (global shortcut). No-op off a DB screen.</summary>
    [RelayCommand]
    private void DuplicateEntry() => ActiveWorkspace?.DuplicateCommand.Execute(null);

    /// <summary>Copies the active workspace's selected entry/entries as YAML (global shortcut).</summary>
    [RelayCommand]
    private void CopyEntryYaml() => ActiveWorkspace?.CopyEntryCommand.Execute(null);

    /// <summary>Jumps to the cross-file validation screen and runs a check.</summary>
    [RelayCommand]
    private void OpenValidation()
    {
        GoSection("validation");
        _validation.RunCommand.Execute(null);
    }

    /// <summary>Flips the active ruleset between Renewal and Pre-Renewal.</summary>
    [RelayCommand]
    private void ToggleMode() => IsRenewal = !IsRenewal;

    /// <summary>Raised when the user presses the "find in list" shortcut so the window can focus the search box.</summary>
    public event Action? FocusSearchRequested;

    [RelayCommand]
    private void NewEntry() => ActiveWorkspace?.AddCustomCommand.Execute(null);

    [RelayCommand]
    private void FocusListSearch() => FocusSearchRequested?.Invoke();

    /// <summary>Builds the window's key bindings from the configured (or default) shortcut gestures.</summary>
    public IEnumerable<KeyBinding> BuildInputBindings()
    {
        var converter = new KeyGestureConverter();
        foreach (var (key, _, def) in AppSettingsService.ShortcutDefs)
        {
            if (CommandFor(key) is not { } command) continue;
            string gesture = _appSettings.Settings.Shortcuts.TryGetValue(key, out var g) && !string.IsNullOrWhiteSpace(g) ? g : def;
            KeyGesture? kg = null;
            try { kg = converter.ConvertFromString(gesture) as KeyGesture; } catch { /* invalid gesture — skip */ }
            if (kg is not null) yield return new KeyBinding(command, kg.Key, kg.Modifiers);
        }
    }

    [RelayCommand]
    private void OpenForge()
    {
        _forgeVm ??= new ForgeViewModel(_session, _schemas, _clientItems, _images, _sprite, NavigateTo);
        ActiveWorkspace = null;
        CurrentContent = _forgeVm;
    }

    [RelayCommand]
    private void OpenMapCacheEditor()
    {
        _mapCacheVm ??= new MapCacheEditorViewModel(_mapCache);
        ActiveWorkspace = null;
        CurrentContent = _mapCacheVm;
    }

    [RelayCommand]
    private void OpenBackupManager()
    {
        _backupVm ??= new BackupManagerViewModel(_backups, ReloadAfterRestore);
        _backupVm.RefreshCommand.Execute(null);
        ActiveWorkspace = null;
        CurrentContent = _backupVm;
    }

    /// <summary>After a backup restore: reset caches + undo so restored files are re-read on next visit.</summary>
    private void ReloadAfterRestore()
    {
        _session.ApplyProfile(_session.Config);
        DisposeWorkspaces();
        IsModified = _session.Commands.IsModified;
    }

    [RelayCommand]
    private void OpenPalette()
    {
        PaletteQuery = string.Empty;
        PaletteResults.Clear();
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    /// <summary>Opens the palette as a global search, loading every database so results cover all of them.</summary>
    [RelayCommand]
    private void FindEverywhere()
    {
        PaletteQuery = string.Empty;
        PaletteResults.Clear();
        IsPaletteOpen = true;
        _ = EnsureAllDatabasesLoadedAsync();
    }

    /// <summary>Creates + loads every database workspace so the palette can search all of them; results
    /// stream in as each finishes loading, and everything is cached for instant re-use afterwards.</summary>
    private async Task EnsureAllDatabasesLoadedAsync()
    {
        foreach (var section in Sections.ToList())
        {
            if (section.SchemaId is not { } id || _schemas.Get(id) is not { } schema) continue;
            if (!_workspaces.TryGetValue(id, out var workspace))
            {
                workspace = new DbWorkspaceViewModel(_session, schema, _references, _clientItems, _images,
                    _mobSprite, _drops, NavigateTo);
                _workspaces[id] = workspace;
            }
            // One malformed database must not stop the rest from being indexed for search.
            try { await workspace.EnsureLoadedAsync(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "Failed to preload {Id} for search", id); continue; }
            if (IsPaletteOpen && !string.IsNullOrWhiteSpace(PaletteQuery))
                OnPaletteQueryChanged(PaletteQuery); // re-search now that this DB is available
        }
    }

    partial void OnPaletteQueryChanged(string value)
    {
        PaletteResults.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;

        int budget = 40;
        foreach (var workspace in _workspaces.Values)
        {
            foreach (var (key, display) in workspace.SearchRows(value, budget))
            {
                PaletteResults.Add(new PaletteResultViewModel(workspace.SchemaId, workspace.Title, key, display));
                if (--budget <= 0) return;
            }
        }
    }

    /// <summary>Switches to a section (by schema id or section key) and selects a record there.</summary>
    public void NavigateTo(string target, RecordKey key)
    {
        var section = Sections.FirstOrDefault(s => s.SchemaId == target)
                      ?? Sections.FirstOrDefault(s => s.Key == target);
        if (section is null) return;
        SelectedSection = section; // RebuildContent creates + loads the target workspace
        if (section.SchemaId is { } id && _workspaces.TryGetValue(id, out var workspace))
            workspace.SelectRow(key);
        else if (section.Key == "client_items")
            _clientItemsVm?.SelectRow(key);
    }

    /// <summary>Navigates to the record behind a validation issue, resolving the int/string key from the schema.</summary>
    private void NavigateToIssue(string dbId, string key)
    {
        var schema = _schemas.Get(dbId);
        if (schema is null) return;
        RecordKey rk = schema.Key is MidgardStudio.Core.Schema.IntKeyStrategy && long.TryParse(key, out var n)
            ? RecordKey.Of(n)
            : RecordKey.Of(key);
        NavigateTo(dbId, rk);
    }

    [RelayCommand]
    private void ActivatePaletteResult(PaletteResultViewModel? result)
    {
        if (result is null) return;
        IsPaletteOpen = false;

        var section = Sections.FirstOrDefault(s => s.SchemaId == result.SchemaId);
        if (section is null) return;
        SelectedSection = section;
        if (_workspaces.TryGetValue(result.SchemaId, out var workspace))
            workspace.SelectRow(result.Key);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _session.Commands.Undo();

    private bool CanUndo() => _session.Commands.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _session.Commands.Redo();

    private bool CanRedo() => _session.Commands.CanRedo;
}

/// <summary>One row in the File ▸ Switch profile submenu.</summary>
public sealed class ProfileMenuItemViewModel
{
    public ProfileMenuItemViewModel(WorkspaceConfig config, bool isActive)
    {
        Config = config;
        IsActive = isActive;
    }

    public WorkspaceConfig Config { get; }
    public string Name => Config.Name;
    public bool IsActive { get; }
    public string ModeText => Config.DefaultMode == ServerMode.Renewal ? "Renewal" : "Pre-Renewal";
}
