using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Common;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.App.ViewModels;

/// <summary>A live readiness check shown in the Forge studio's right rail.</summary>
public sealed partial class ForgeCheck : ObservableObject
{
    public ForgeCheck(string text) => _text = text;
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _state = "info"; // ok | warn | info
}

/// <summary>A nav-rail section entry (a jump target into the editor). Driven by the hosted editor's groups,
/// so a section only appears when it has applicable fields for the current Type.</summary>
public sealed partial class ForgeSection : ObservableObject
{
    public ForgeSection(string title) => Title = title;
    public string Title { get; }
    [ObservableProperty] private bool _isActive;
}

/// <summary>
/// The Item Forge — a full-width "studio" for creating a complete custom item across the server item_db, the
/// client itemInfo, and (for headgear) the sprite registration. The server side is hosted by the SAME
/// schema-driven field-editor stack the main editor uses, so it inherits correct enum tokens, Type-conditional
/// fields (IsApplicable), the script/bonus generator, and live validation — and, because inapplicable fields
/// are never shown, never sets a field rAthena would drop. The right rail is a live preview + a "what will be
/// written" transparency panel. It never writes to a GRF.
/// </summary>
public sealed partial class ForgeViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService _sprite;
    private readonly IReferenceResolver _references;
    private readonly IReferenceIndex _referenceIndex;
    private readonly AppSettingsService _appSettings;
    private readonly Action<string, RecordKey> _navigate;

    // Description auto-derive (mirrors the Name→Aegis pattern): keep the client Description generated from the
    // draft until the user types their own, then leave it alone.
    private bool _descEdited;
    private string _autoDesc = string.Empty;

    private static readonly HashSet<string> HeadgearKeys = new(StringComparer.Ordinal)
    {
        "Head_Top", "Head_Mid", "Head_Low",
        "Costume_Head_Top", "Costume_Head_Mid", "Costume_Head_Low",
    };

    // The draft lives in its own scratch overlay + undo stack, so editing it doesn't dirty the real session
    // until the user clicks Forge (which commits it as one atomic, undoable add on the real stack).
    private DbSchema _itemSchema = null!;
    private EditCommandStack _draftStack = null!;
    private DbRecord _draft = null!;

    public ForgeViewModel(WorkspaceSession session, SchemaRegistry schemas, ClientItemService clientItems,
        GrfImageService images, SpriteLinkService sprite, IReferenceResolver references, IReferenceIndex referenceIndex,
        AppSettingsService appSettings, Action<string, RecordKey> navigate)
    {
        _session = session;
        _schemas = schemas;
        _clientItems = clientItems;
        _images = images;
        _sprite = sprite;
        _references = references;
        _referenceIndex = referenceIndex;
        _appSettings = appSettings;
        _navigate = navigate;

        NewDraft();
    }

    /// <summary>The hosted schema-driven server editor (its Groups drive the center fields + the nav sections,
    /// its YamlPreview drives the safety panel, and it maps live validation onto the fields).</summary>
    [ObservableProperty] private RecordEditorViewModel _serverEditor = null!;

    public ObservableCollection<ForgeSection> Sections { get; } = new();
    public ObservableCollection<ForgeCheck> Checklist { get; } = new();

    public int PreviewId => _draft.GetInt("Id");
    public bool HasDescription => Description.Trim().Length > 0;

    // ---- Item ID: auto-allocated, changed via the "Change ID" dialog; a duplicate id can never be forged ----
    public string ItemIdInput => _draft.GetInt("Id").ToString();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ForgeCommand))]
    private bool _idConflict;
    [ObservableProperty] private string _idStatus = string.Empty;

    /// <summary>Forging is blocked unless the item has a name AND a unique, positive id — the duplicate-id guard
    /// can't be bypassed (the command itself won't execute).</summary>
    private bool CanForge => HasDisplayName && !IdConflict;

    // ---- client appearance (the itemInfo side — not part of item_db) ----
    [ObservableProperty] private string _iconResource = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _spriteName = string.Empty;
    [ObservableProperty] private bool _costume;

    // ---- preview / outputs ----
    [ObservableProperty] private ImageSource? _iconImage;
    [ObservableProperty] private ImageSource? _collectionImage;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _justForged;
    [ObservableProperty] private int? _forgedId;

    // ---- derived (preview + safety), refreshed whenever the draft or client side changes ----
    public string DisplayName => (_draft.GetString("Name") ?? string.Empty).Trim();
    public bool HasDisplayName => !string.IsNullOrWhiteSpace(DisplayName);
    public string AegisName => (_draft.GetString("AegisName") ?? string.Empty).Trim();
    public int SlotCount => _draft.GetInt("Slots");
    public bool IsEquip => (_draft.GetSet("Locations")?.Count ?? 0) > 0;
    public bool IsHeadgear => _draft.GetSet("Locations")?.Any(HeadgearKeys.Contains) ?? false;
    public bool WillRegisterSprite => IsHeadgear && !string.IsNullOrWhiteSpace(SpriteName) && _sprite.IsAvailable;

    public string ServerYamlPreview => ServerEditor.YamlPreview;
    public string ClientLuaPreview => ItemInfoWriter.FormatEntry(CurrentClientEntry());
    public string WriteTargets =>
        "import/item_db.yml  ·  itemInfo_C.lua" + (WillRegisterSprite ? "  ·  datainfo/accessoryid.lub" : string.Empty);

    /// <summary>Begins a fresh draft item (a new scratch overlay + record + hosted editor).</summary>
    private void NewDraft()
    {
        if (_schemas.Get("item_db") is not { } schema)
        {
            StatusMessage = "Item database is not available.";
            return;
        }
        _itemSchema = schema;

        int id = NextFreeId();
        _draft = new DbRecord(schema);
        _draft.SetRaw("Id", id);
        _draft.SetRaw("AegisName", string.Empty);
        _draft.SetRaw("Name", string.Empty);
        _draft.SetRaw("Type", "Etc");

        var scratch = new OverlayTable(schema, new DbLayer(), new DbLayer(), schema.Layout.ImportFile);
        scratch.AddCustom(_draft); // NewCustom origin -> editable in the hosted editor

        _draftStack = new EditCommandStack();
        var editor = new RecordEditorViewModel(scratch, _draftStack, _references, _session.ScriptCatalog,
            _session.Mode, _session.Validation, _referenceIndex, clearStaleValues: true);
        editor.RecordChanged += OnDraftChanged;
        editor.Load(_draft.Key);
        ServerEditor = editor;

        IconResource = Description = SpriteName = string.Empty;
        Costume = false;
        IconImage = CollectionImage = null;
        _descEdited = false;
        _autoDesc = string.Empty;

        ValidateId();
        OnPropertyChanged(nameof(ItemIdInput));
        RebuildSections();
        RefreshChecklist();
        AutoFillDescription();
        RaiseDerived();
    }

    /// <summary>Fired by the hosted editor on any field edit — refresh nav sections (a Type change may
    /// reveal/hide a whole group), the readiness checklist, and the preview/safety bindings.</summary>
    private void OnDraftChanged()
    {
        JustForged = false; // editing dismisses the post-forge banner
        RebuildSections();
        RefreshChecklist();
        AutoFillDescription();
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(HasDisplayName));
        OnPropertyChanged(nameof(AegisName));
        OnPropertyChanged(nameof(PreviewId));
        OnPropertyChanged(nameof(SlotCount));
        OnPropertyChanged(nameof(IsEquip));
        OnPropertyChanged(nameof(IsHeadgear));
        OnPropertyChanged(nameof(WillRegisterSprite));
        OnPropertyChanged(nameof(ServerYamlPreview));
        OnPropertyChanged(nameof(ClientLuaPreview));
        OnPropertyChanged(nameof(WriteTargets));
        ForgeCommand.NotifyCanExecuteChanged(); // HasDisplayName may have flipped
    }

    /// <summary>Nav sections = the editor's applicable server groups (in schema order), plus the client
    /// "Appearance" section. A group with no applicable fields for the current Type simply isn't present.</summary>
    private void RebuildSections()
    {
        var wanted = ServerEditor.Groups.Select(g => g.Title).Append("Appearance").ToList();
        // Reconcile in place so the nav doesn't flicker on every keystroke.
        if (!Sections.Select(s => s.Title).SequenceEqual(wanted))
        {
            Sections.Clear();
            foreach (var title in wanted) Sections.Add(new ForgeSection(title));
        }
    }

    partial void OnSpriteNameChanged(string value) { RefreshChecklist(); RaiseDerived(); }
    partial void OnCostumeChanged(bool value) => OnPropertyChanged(nameof(ClientLuaPreview));
    partial void OnDescriptionChanged(string value)
    {
        if (value != _autoDesc) _descEdited = true; // the user typed their own — stop auto-filling
        OnPropertyChanged(nameof(ClientLuaPreview));
        OnPropertyChanged(nameof(HasDescription));
    }

    /// <summary>Regenerates the client Description from the draft (same generator the Client Items editor uses)
    /// until the user edits it by hand — so a forged item gets an authentic auto-written tooltip out of the box.</summary>
    private void AutoFillDescription()
    {
        if (_descEdited) return;
        var text = GenerateDescription();
        if (text == _autoDesc) return;
        _autoDesc = text;
        Description = text; // OnDescriptionChanged sees value == _autoDesc, so it stays "not edited"
    }

    private string GenerateDescription()
    {
        // A forged item always gets a complete tooltip: the category line (labelled "Type") and Weight are
        // forced on regardless of the user's global toggles. Clone so we never mutate the shared settings.
        var cfg = _appSettings.Settings.Autocomplete.Clone();
        cfg.IncludeClass = true;
        cfg.IncludeWeight = true;
        cfg.IncludeJobs = true;
        cfg.AlwaysShowWeight = true;       // show "Weight: 0" even at 0
        cfg.Labels["Class"] = "Type";      // the category line reads "Type:" (value is the Type or its SubType)
        cfg.Labels["Jobs"] = "Class";      // the who-can-use line reads "Class:" (e.g. "Transcendent Archer")

        var lines = new ItemAutocomplete(cfg).IdentifiedDescription(_draft);
        if (BuildTransferRestrictions() is { } restrictions)
            lines.Add(cfg.UseColors ? $"Restrictions:^FF0000 {restrictions}^000000" : $"Restrictions: {restrictions}");
        return string.Join("\n", lines);
    }

    partial void OnIconResourceChanged(string value)
    {
        IconImage = string.IsNullOrWhiteSpace(value) ? null : _images.ItemIcon(value);
        CollectionImage = string.IsNullOrWhiteSpace(value) ? null : _images.ItemCollection(value);
        RefreshChecklist();
        OnPropertyChanged(nameof(ClientLuaPreview));
    }

    private void RefreshChecklist()
    {
        Checklist.Clear();
        Add("Item ID", IdConflict ? "warn" : "ok", IdStatus);
        Add("Display name", HasDisplayName ? "ok" : "warn", HasDisplayName ? $"“{DisplayName}”" : "Required");
        Add("Aegis name", string.IsNullOrWhiteSpace(AegisName) ? "warn" : "ok", AegisName);

        if (IsEquip)
            Add("Equip Location", "ok", string.Join(", ", _draft.GetSet("Locations")!.Select(ItemEnums.Locations.Label)));
        else if (_draft.GetString("Type") is "Armor" or "Weapon" or "ShadowGear")
            Add("Equip Location", "warn", "An equippable type needs a location");

        Add("Icon resource", string.IsNullOrWhiteSpace(IconResource) ? "warn" : IconImage is null ? "info" : "ok",
            string.IsNullOrWhiteSpace(IconResource) ? "Blank icon in-game"
            : IconImage is null ? "Not found in the GRF (you can add it later)" : "Found in GRF");

        if (IsHeadgear)
        {
            if (string.IsNullOrWhiteSpace(SpriteName))
                Add("Headgear sprite", "info", "Set a sprite name to auto-allocate the View id");
            else if (!_sprite.IsAvailable)
                Add("Headgear sprite", "warn", "accessoryid.lub / accname.lub not found");
            else
                Add("Headgear sprite", "ok", $"Will register “{SpriteName}” and allocate a View");
        }

        if (ServerEditor.HasRecordIssues)
            Add("Validation", "warn", ServerEditor.RecordIssuesText.Replace("\n", "; "));

        void Add(string label, string state, string detail) =>
            Checklist.Add(new ForgeCheck($"{label} — {detail}") { State = state });
    }

    /// <summary>The item's transfer flags as a comma-joined phrase ("No Drop, No Trade, …"), appended to the
    /// auto description as a red <c>Restrictions:</c> line. Null when the item has no transfer restrictions.</summary>
    private string? BuildTransferRestrictions()
    {
        if (_draft.GetObject("Trade") is not { } t) return null;
        var parts = new List<string>();
        if (t.GetBool("NoDrop")) parts.Add("No Drop");
        if (t.GetBool("NoTrade")) parts.Add("No Trade");
        if (t.GetBool("NoSell")) parts.Add("No Sell");
        if (t.GetBool("NoStorage")) parts.Add("No Storage");
        if (t.GetBool("NoCart")) parts.Add("No Cart");
        if (t.GetBool("NoMail")) parts.Add("No Mail");
        if (t.GetBool("NoAuction")) parts.Add("No Auction");
        if (t.GetBool("NoGuildStorage")) parts.Add("No Guild Storage");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    [RelayCommand(CanExecute = nameof(CanForge))]
    private void Forge()
    {
        if (!HasDisplayName) { StatusMessage = "Give the item a display name first."; return; }

        // Re-check uniqueness against the live overlay in case another item landed since the draft opened —
        // a duplicate id is never written (hard gate; CanForge also keeps the button disabled).
        ValidateId();
        if (IdConflict) { StatusMessage = IdStatus; return; }

        var overlay = _session.GetActiveOverlay(_itemSchema);
        int id = _draft.GetInt("Id"); // the auto-allocated or user-entered id, validated above
        string aegis = string.IsNullOrWhiteSpace(AegisName) ? $"Custom_{id}" : AegisName;
        _draft.SetRaw("AegisName", aegis);

        // View: headgear auto-allocates a sprite View; any other equip uses the View the user set in the form.
        int view = _draft.GetInt("View");
        string spriteNote = string.Empty;
        Core.Sprites.PendingRegistration? plannedSprite = null;
        if (WillRegisterSprite)
        {
            try
            {
                plannedSprite = _sprite.PlanAccessory(aegis, SpriteName.Trim());
                view = plannedSprite.Id;
                _draft.SetRaw("View", view);
                spriteNote = $"  Sprite queued as {plannedSprite.ConstantName} (View {view}); written on Save.";
            }
            catch (Exception ex)
            {
                plannedSprite = null;
                spriteNote = "  Sprite registration failed: " + ex.Message;
            }
        }

        // Client itemInfo — synced so View==ClassNum and Slots==slotCount by construction. Built BEFORE the
        // batch so its insert can join the SAME undo step as the record add: undoing a forge then drops the
        // client text too, instead of orphaning it and leaving the Save button stuck lit.
        var entry = _clientItems.GetOrCreate(id);
        string name = DisplayName;
        var desc = SplitLines(Description);
        entry.IdentifiedDisplayName = name;
        entry.IdentifiedResourceName = IconResource.Trim();
        entry.IdentifiedDescription = desc;
        if (string.IsNullOrEmpty(entry.UnidentifiedDisplayName)) entry.UnidentifiedDisplayName = name;
        if (string.IsNullOrEmpty(entry.UnidentifiedResourceName)) entry.UnidentifiedResourceName = IconResource.Trim();
        if (entry.UnidentifiedDescription.Count == 0) entry.UnidentifiedDescription = desc;
        entry.SlotCount = _draft.GetInt("Slots");
        entry.ClassNum = view;
        entry.Costume = Costume;

        // One undo step on the REAL stack: the record add, its sprite registration, and the client text all
        // commit and revert together.
        using (_session.Commands.BeginBatch("Forge item"))
        {
            _session.Commands.Execute(new AddRecordCommand(overlay, _draft));
            if (plannedSprite is { } ps)
                _session.Commands.Execute(new ListMutateCommand("Link accessory sprite",
                    () => _sprite.AddPending(ps), () => _sprite.RemovePending(ps)));
            _session.Commands.Execute(_clientItems.SeedClientTextCommand(entry));
        }

        ForgedId = id;
        StatusMessage = $"Forged item #{id} “{name}”.{spriteNote}";
        NewDraft();        // fresh studio for the next item
        JustForged = true; // success banner (cleared on the next edit / Dismiss)
    }

    /// <summary>Opens the just-forged item in Server Items so the user can review it before saving.</summary>
    [RelayCommand]
    private void OpenForged()
    {
        if (ForgedId is int id) _navigate("item_db", RecordKey.Of(id));
    }

    /// <summary>Opens the icon picker: copy an existing item's icon (search by id/name) or browse the icons in
    /// one chosen GRF/loose source. Sets <see cref="IconResource"/> from the chosen resource.</summary>
    [RelayCommand]
    private void BrowseIcon()
    {
        var rows = new List<IconItemRow>();
        if (_schemas.Get("item_db") is { } schema)
        {
            foreach (var r in _session.GetActiveOverlay(schema).Effective())
            {
                int id = r.GetInt("Id");
                var res = _clientItems.ResourceOf(id);
                if (string.IsNullOrWhiteSpace(res)) continue; // only items that actually have an icon to copy
                var name = r.GetString("Name");
                rows.Add(new IconItemRow(_images, id,
                    string.IsNullOrWhiteSpace(name) ? (r.GetString("AegisName") ?? string.Empty) : name!, res!));
            }
        }

        var picker = new IconPickerViewModel(_images, rows);
        var dlg = new MidgardStudio.App.Views.IconPickerDialog(picker)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        bool? ok = dlg.ShowDialog();
        _images.CloseIconSource(); // release the picker's single-source reader + thumbnail cache
        if (ok == true && !string.IsNullOrWhiteSpace(picker.Result))
            IconResource = picker.Result!.Trim();
    }

    /// <summary>Opens the sprite picker: browse the accessory headgear sprite base names in one chosen
    /// GRF/loose source. Sets <see cref="SpriteName"/> from the chosen base name.</summary>
    [RelayCommand]
    private void BrowseSprite()
    {
        var picker = new SpritePickerViewModel(_images);
        var dlg = new MidgardStudio.App.Views.SpritePickerDialog(picker)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        bool? ok = dlg.ShowDialog();
        _images.CloseIconSource(); // release the picker's single-source reader
        if (ok == true && !string.IsNullOrWhiteSpace(picker.Result))
            SpriteName = picker.Result!.Trim();
    }

    [RelayCommand]
    private void Dismiss() => JustForged = false;

    [RelayCommand]
    private void Reset()
    {
        NewDraft();
        JustForged = false;
        ForgedId = null;
        StatusMessage = string.Empty;
    }

    /// <summary>Builds the itemInfo entry that WOULD be written, from the current draft + client fields — used
    /// for the live "what will be written" client preview (read-only; never committed).</summary>
    private ItemInfoEntry CurrentClientEntry()
    {
        string name = DisplayName;
        string icon = IconResource.Trim();
        var desc = SplitLines(Description);
        return new ItemInfoEntry
        {
            Id = _draft.GetInt("Id"),
            IdentifiedDisplayName = name,
            IdentifiedResourceName = icon,
            IdentifiedDescription = desc,
            UnidentifiedDisplayName = name,
            UnidentifiedResourceName = icon,
            UnidentifiedDescription = desc,
            SlotCount = _draft.GetInt("Slots"),
            ClassNum = _draft.GetInt("View"),
            Costume = Costume,
        };
    }

    /// <summary>Opens the "Change ID" dialog (validates uniqueness; a duplicate can't be committed).</summary>
    [RelayCommand]
    private void ChangeId()
    {
        // Precompute the server id→where map once so the dialog's per-keystroke validation is O(1).
        var serverWhere = BuildServerWhere();
        var vm = new ChangeIdViewModel(_draft.GetInt("Id"),
            id => IdStatusFor(id, serverWhere, _clientItems.Exists), NextFreeId);
        var dlg = new MidgardStudio.App.Views.ChangeIdDialog(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() == true && vm.Result is int id)
        {
            _draft.SetRaw("Id", id);
            ValidateId();
            OnPropertyChanged(nameof(ItemIdInput));
            OnPropertyChanged(nameof(PreviewId));
            RefreshChecklist();
        }
    }

    /// <summary>Validates the draft's id against BOTH sides (active-mode server item_db + client item info).</summary>
    private void ValidateId()
    {
        var (ok, status) = IdStatusFor(_draft.GetInt("Id"), BuildServerWhere(), _clientItems.Exists);
        IdConflict = !ok;
        IdStatus = status;
    }

    /// <summary>The availability verdict for an id and, when taken, where it's in use. Checks the active
    /// overlay (so it respects the chosen Renewal/Pre-renewal system) AND the client item info — an id can be
    /// on one side but not the other, and the reserved/system ids (Emperium, gemstones, …) are official items
    /// already present in the base item_db, so the server scan covers them too.</summary>
    private static (bool ok, string status) IdStatusFor(int id, IReadOnlyDictionary<int, string> serverWhere, Func<int, bool> clientHas)
    {
        if (id <= 0) return (false, "Enter a positive Item ID.");
        serverWhere.TryGetValue(id, out var where);
        bool client = clientHas(id);
        if (where is not null && client) return (false, $"#{id} is taken — {where}, and the client item info.");
        if (where is not null) return (false, $"#{id} is taken — {where}.");
        if (client) return (false, $"#{id} is taken — it exists in the client item info (no server entry yet).");
        return (true, $"#{id} is available.");
    }

    /// <summary>Maps every used server id to a human "where" string (official vs your import + the item name),
    /// from the active-mode overlay. The draft lives in its own scratch overlay, so every match is a real one.</summary>
    private Dictionary<int, string> BuildServerWhere()
    {
        var map = new Dictionary<int, string>();
        if (_schemas.Get("item_db") is { } schema)
            foreach (var r in _session.GetActiveOverlay(schema).Effective())
            {
                int rid = r.GetInt("Id");
                if (map.ContainsKey(rid)) continue;
                string nm = (r.GetString("Name") ?? r.GetString("AegisName") ?? "unnamed").Trim();
                map[rid] = r.Origin == RecordOrigin.Base ? $"an official item ({nm})" : $"your import item ({nm})";
            }
        return map;
    }

    private int NextFreeId()
    {
        var used = BuildServerWhere();
        int id = 30000;
        while (used.ContainsKey(id) || _clientItems.Exists(id)) id++; // skip ids taken on either side
        return id;
    }

    private static List<string> SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : text.Replace("\r\n", "\n").Split('\n').ToList();
}
