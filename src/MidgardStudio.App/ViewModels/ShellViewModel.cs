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
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService _sprite;
    private readonly MobSpriteService _mobSprite;
    private readonly DropService _drops;
    private readonly BackupService _backups;
    private readonly MapCacheService _mapCache;
    private readonly AppSettingsService _appSettings;
    private readonly ConfigurationWizardViewModel _wizard;
    private readonly Dictionary<string, DbWorkspaceViewModel> _workspaces = new(StringComparer.Ordinal);
    private readonly System.Windows.Threading.DispatcherTimer _intervalSaveTimer = new();
    private readonly System.Windows.Threading.DispatcherTimer _editSaveTimer = new();
    private ClientItemsViewModel? _clientItemsVm;
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

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private bool _showWizard;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    public ObservableCollection<PaletteResultViewModel> PaletteResults { get; } = new();

    /// <summary>Saved profiles for the File ▸ Switch profile submenu (active one flagged).</summary>
    public ObservableCollection<ProfileMenuItemViewModel> Profiles { get; } = new();

    /// <summary>The first-run / profile-management screen, shown over the shell when <see cref="ShowWizard"/>.</summary>
    public ConfigurationWizardViewModel Wizard => _wizard;

    public string ModeLabel => IsRenewal ? "Renewal" : "Pre-Renewal";

    /// <summary>Name of the active profile, shown in the menu-bar status strip.</summary>
    public string ActiveProfileName => _session.Config.Name;

    public ShellViewModel(IWorkspaceConfigService config, SchemaRegistry schemas, WorkspaceSession session,
        ReferenceResolver references, GrfBrowserViewModel grfBrowser, ClientItemService clientItems,
        GrfImageService images, SpriteLinkService sprite, MobSpriteService mobSprite, ValidationViewModel validation,
        DropService drops, BackupService backups, MapCacheService mapCache, AppSettingsService appSettings,
        ConfigurationWizardViewModel wizard)
    {
        _configService = config;
        _schemas = schemas;
        _session = session;
        _references = references;
        _grfBrowser = grfBrowser;
        _validation = validation;
        _clientItems = clientItems;
        _images = images;
        _sprite = sprite;
        _mobSprite = mobSprite;
        _drops = drops;
        _backups = backups;
        _mapCache = mapCache;
        _appSettings = appSettings;
        _wizard = wizard;

        _intervalSaveTimer.Interval = TimeSpan.FromSeconds(60); // safe default; ApplySaveMode overrides it
        _intervalSaveTimer.Tick += (_, _) => { if (IsModified) DoSave(createBackup: false); };
        _editSaveTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _editSaveTimer.Tick += (_, _) => { _editSaveTimer.Stop(); if (IsModified) DoSave(createBackup: false); };
        _wizard.OpenRequested += cfg => ApplyProfile(cfg);
        _wizard.CancelRequested += () => ShowWizard = false;

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
        Common.ScrollSoundPlayer.Enabled = _appSettings.Settings.ScrollSound;
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
            _clientItemsVm ??= new ClientItemsViewModel(_session, _clientItems, _images, _sprite, _schemas.Get("item_db")!);
            CurrentContent = _clientItemsVm;
            _ = _clientItemsVm.EnsureLoadedAsync();
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
            OpenInExplorer(Path.GetFullPath(Path.Combine(_session.Paths.ServerDbRoot, "..", "..", "docs")));
        }
        catch { /* path math can throw on an empty root — ignore */ }
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
        _clientItemsVm = null; // rebuilt against the new mode/profile on next visit
        _forgeVm = null;
        _mapCacheVm = null;
    }

    [RelayCommand]
    private void Save() => DoSave(createBackup: true);

    /// <summary>True when there are server or client edits not yet written to disk.</summary>
    public bool HasUnsavedChanges => _session.Commands.IsModified || _clientItems.IsDirty;

    /// <summary>Writes pending changes. Returns false if the save failed (data is kept in the editor).
    /// Manual saves take a dated backup; auto-saves don't (to avoid spam).</summary>
    private bool DoSave(bool createBackup)
    {
        // Capture what's about to change so the auto-backup can label itself, then write only those.
        var dirtyIds = _session.DirtyDatabaseIds();
        bool clientDirty = _clientItems.IsDirty;
        if (dirtyIds.Count == 0 && !clientDirty)
        {
            IsModified = _session.Commands.IsModified;
            return true;
        }

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
            Views.ConfirmDialog.Alert("Save failed",
                "Your changes were NOT fully saved and are still here in the editor:\n\n" + ex.Message +
                "\n\nClose anything that might be using these files (for example a running server), then save again.");
            return false;
        }

        // OR client dirtiness in so a half-failed save can never be shown as a clean state.
        IsModified = _session.Commands.IsModified || _clientItems.IsDirty;

        // Snapshot the freshly-saved state into a dated, self-describing backup (manual saves only).
        if (createBackup)
        {
            var names = dirtyIds.Select(id => _schemas.Get(id)?.DisplayName ?? id).ToList();
            if (clientDirty) names.Add("Client items");
            _backups.CreateBackup("Save · " + string.Join(", ", names),
                $"Automatic backup taken after saving: {string.Join(", ", names)}.");
            _backupVm?.RefreshCommand.Execute(null);
        }

        RefreshProfiles();       // keep the active-profile flag fresh
        return true;
    }

    /// <summary>Saves for an exiting window. Returns true if it's safe to close (saved or nothing pending).</summary>
    public bool SaveForExit() => DoSave(createBackup: true);

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
        "QuickOpen" => OpenPaletteCommand,
        "Configuration" => OpenWizardCommand,
        _ => null,
    };

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
