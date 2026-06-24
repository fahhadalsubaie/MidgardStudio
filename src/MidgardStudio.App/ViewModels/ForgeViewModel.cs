using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Common;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.App.ViewModels;

/// <summary>One equip-location choice in the Forge.</summary>
public sealed record ForgeLocation(string Key, string Label);

/// <summary>A live readiness check shown in the Forge checklist.</summary>
public sealed partial class ForgeCheck : ObservableObject
{
    public ForgeCheck(string text) => _text = text;
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _state = "info"; // ok | warn | info
}

/// <summary>
/// The Item Forge: one guided flow that creates a complete custom item across the server item_db,
/// the client itemInfo, and (for headgear) the accessory sprite registration — keeping the cross-file
/// invariants (View==ClassNum, Slots==slotCount) correct by construction. It never writes to a GRF;
/// the "Export data folder" action lays out the client lua files in a GRF-mirrored tree for manual packing.
/// </summary>
public sealed partial class ForgeViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService _sprite;
    private readonly Action<string, RecordKey> _navigate;

    private static readonly HashSet<string> HeadgearKeys = new(StringComparer.Ordinal)
    {
        "Head_Top", "Head_Mid", "Head_Low",
        "Costume_Head_Top", "Costume_Head_Mid", "Costume_Head_Low",
    };

    public ForgeViewModel(WorkspaceSession session, SchemaRegistry schemas, ClientItemService clientItems,
        GrfImageService images, SpriteLinkService sprite, Action<string, RecordKey> navigate)
    {
        _session = session;
        _schemas = schemas;
        _clientItems = clientItems;
        _images = images;
        _sprite = sprite;
        _navigate = navigate;

        Locations = new List<ForgeLocation> { new("", "None (not equippable)") }
            .Concat(ItemEnums.Locations.Values.Select(v => new ForgeLocation(v, ItemEnums.Locations.Label(v))))
            .ToList();
        _selectedLocation = Locations[0];

        Refresh();
    }

    // ---- inputs ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _aegisName = string.Empty;
    private bool _aegisEdited;
    private string _autoAegis = string.Empty;
    [ObservableProperty] private string _type = "Etc";
    [ObservableProperty] private int _slots;
    [ObservableProperty] private ForgeLocation _selectedLocation;
    [ObservableProperty] private string _iconResource = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _spriteName = string.Empty;
    [ObservableProperty] private bool _costume;

    // server stats / pricing / requirements (written only when non-default)
    [ObservableProperty] private int _buy;
    [ObservableProperty] private int _sell;
    [ObservableProperty] private int _weight;
    [ObservableProperty] private int _attack;
    [ObservableProperty] private int _magicAttack;
    [ObservableProperty] private int _defense;
    [ObservableProperty] private int _range;
    [ObservableProperty] private int _weaponLevel;
    [ObservableProperty] private int _equipLevelMin;
    [ObservableProperty] private bool _refineable;
    [ObservableProperty] private string _script = string.Empty;

    // ---- outputs ----
    [ObservableProperty] private ImageSource? _iconImage;
    [ObservableProperty] private ImageSource? _collectionImage;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int? _forgedId;

    // ---- wizard step (1 = server side, 2 = client side) ----
    [ObservableProperty] private int _step = 1;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public string StepLabel => Step == 1 ? "Step 1 of 2 · Server side" : "Step 2 of 2 · Client side";

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(StepLabel));
    }

    [RelayCommand]
    private void Next()
    {
        if (string.IsNullOrWhiteSpace(Name)) { StatusMessage = "Give the item a display name first."; return; }

        // Smart default: derive the icon resource from the sprite or aegis name so the user rarely types it.
        if (string.IsNullOrWhiteSpace(IconResource))
            IconResource = !string.IsNullOrWhiteSpace(SpriteName) ? SpriteName.Trim().ToLowerInvariant()
                         : !string.IsNullOrWhiteSpace(AegisName) ? AegisName.Trim().ToLowerInvariant()
                         : string.Empty;

        Step = 2;
    }

    [RelayCommand]
    private void Back() => Step = 1;

    public IReadOnlyList<string> Types => ItemEnums.Type.Values;
    public IReadOnlyList<ForgeLocation> Locations { get; }
    public ObservableCollection<ForgeCheck> Checklist { get; } = new();

    public bool IsEquip => !string.IsNullOrEmpty(SelectedLocation.Key);
    public bool IsHeadgear => HeadgearKeys.Contains(SelectedLocation.Key);

    // Auto-derive AegisName from Name until the user types their own.
    partial void OnNameChanged(string value)
    {
        if (!_aegisEdited) { _autoAegis = NameFormat.ToAegis(value); AegisName = _autoAegis; }
        Refresh();
    }

    partial void OnAegisNameChanged(string value)
    {
        if (value != _autoAegis) _aegisEdited = true;
        Refresh();
    }

    /// <summary>Inline auto-fill: generate the aegis name from the display name.</summary>
    [RelayCommand]
    private void FillAegisFromName()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        _aegisEdited = true;
        AegisName = NameFormat.ToAegis(Name);
    }

    /// <summary>Inline auto-fill: generate the display name from the aegis name.</summary>
    [RelayCommand]
    private void FillNameFromAegis()
    {
        if (string.IsNullOrWhiteSpace(AegisName)) return;
        Name = NameFormat.ToDisplay(AegisName);
    }

    partial void OnTypeChanged(string value) => Refresh();
    partial void OnSlotsChanged(int value) => Refresh();
    partial void OnSelectedLocationChanged(ForgeLocation value)
    {
        OnPropertyChanged(nameof(IsEquip));
        OnPropertyChanged(nameof(IsHeadgear));
        Refresh();
    }
    partial void OnSpriteNameChanged(string value) => Refresh();

    partial void OnIconResourceChanged(string value)
    {
        IconImage = string.IsNullOrWhiteSpace(value) ? null : _images.ItemIcon(value);
        CollectionImage = string.IsNullOrWhiteSpace(value) ? null : _images.ItemCollection(value);
        Refresh();
    }

    private void Refresh()
    {
        Checklist.Clear();
        Add("Display name", string.IsNullOrWhiteSpace(Name) ? "warn" : "ok",
            string.IsNullOrWhiteSpace(Name) ? "Display name is required" : $"\"{Name}\"");
        Add("Aegis name", string.IsNullOrWhiteSpace(AegisName) ? "warn" : "ok", AegisName);

        if (IsEquip)
            Add("Equip slot", "ok", SelectedLocation.Label);
        else if (Type is "Armor" or "Weapon")
            Add("Equip slot", "warn", "An equippable type usually needs a location");

        if (string.IsNullOrWhiteSpace(IconResource))
            Add("Icon resource", "warn", "No inventory icon set — item shows a blank icon");
        else
            Add("Icon resource", IconImage is null ? "info" : "ok",
                IconImage is null ? "Not found in the configured GRF (you can add it later)" : "Found in GRF");

        if (IsHeadgear)
        {
            if (string.IsNullOrWhiteSpace(SpriteName))
                Add("Headgear sprite", "info", "Set a sprite name to auto-register the View id");
            else if (!_sprite.IsAvailable)
                Add("Headgear sprite", "warn", "accessoryid.lub / accname.lub not found in the lua-files folder");
            else
                Add("Headgear sprite", "ok", $"Will register \"{SpriteName}\" and allocate a View id");
        }

        OnPropertyChanged(nameof(IsEquip));
        OnPropertyChanged(nameof(IsHeadgear));

        void Add(string label, string state, string detail) =>
            Checklist.Add(new ForgeCheck($"{label} — {detail}") { State = state });
    }

    [RelayCommand]
    private void Forge()
    {
        if (string.IsNullOrWhiteSpace(Name)) { StatusMessage = "Give the item a display name first."; return; }
        if (_schemas.Get("item_db") is not { } schema) { StatusMessage = "Item database is not available."; return; }

        var overlay = _session.GetActiveOverlay(schema);
        int id = NextFreeId(overlay);
        string aegis = string.IsNullOrWhiteSpace(AegisName) ? $"Custom_{id}" : AegisName.Trim();

        var record = new DbRecord(schema);
        record.SetRaw("Id", id);
        record.SetRaw("AegisName", aegis);
        record.SetRaw("Name", Name.Trim());
        record.SetRaw("Type", Type);
        if (Slots > 0) record.SetRaw("Slots", Slots);
        if (IsEquip) record.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { SelectedLocation.Key });

        if (Buy > 0) record.SetRaw("Buy", Buy);
        if (Sell > 0) record.SetRaw("Sell", Sell);
        if (Weight > 0) record.SetRaw("Weight", Weight);
        if (Attack > 0) record.SetRaw("Attack", Attack);
        if (MagicAttack > 0) record.SetRaw("MagicAttack", MagicAttack);
        if (Defense > 0) record.SetRaw("Defense", Defense);
        if (Range > 0) record.SetRaw("Range", Range);
        if (WeaponLevel > 0) record.SetRaw("WeaponLevel", WeaponLevel);
        if (EquipLevelMin > 0) record.SetRaw("EquipLevelMin", EquipLevelMin);
        if (Refineable) record.SetRaw("Refineable", true);
        if (!string.IsNullOrWhiteSpace(Script)) record.SetRaw("Script", new Core.Model.ScriptValue(Script.Trim()));

        int view = 0;
        string spriteNote = string.Empty;
        if (IsHeadgear && !string.IsNullOrWhiteSpace(SpriteName) && _sprite.IsAvailable)
        {
            try
            {
                var link = _sprite.LinkAccessory(aegis, SpriteName.Trim());
                view = link.ViewId;
                record.SetRaw("View", view);
                spriteNote = $"  Registered sprite as {link.ConstantName} (View {view}).";
            }
            catch (Exception ex)
            {
                spriteNote = "  Sprite registration failed: " + ex.Message;
            }
        }

        _session.Commands.Execute(new AddRecordCommand(overlay, record));

        // Client itemInfo — synced so View==ClassNum and Slots==slotCount by construction.
        var entry = _clientItems.GetOrCreate(id);
        entry.IdentifiedDisplayName = Name.Trim();
        entry.IdentifiedResourceName = IconResource.Trim();
        entry.IdentifiedDescription = SplitLines(Description);
        entry.SlotCount = Slots;
        entry.ClassNum = view;
        entry.Costume = Costume;
        _clientItems.Upsert(entry);

        ForgedId = id;
        StatusMessage = $"Forged item #{id} \"{Name.Trim()}\".{spriteNote}  Review it in Server Items, then Save to write the files (a backup is taken automatically).";
        _navigate("item_db", RecordKey.Of(id));
    }

    [RelayCommand]
    private void Reset()
    {
        Name = AegisName = IconResource = Description = SpriteName = Script = string.Empty;
        Type = "Etc"; Slots = 0; Costume = false; SelectedLocation = Locations[0];
        Buy = Sell = Weight = Attack = MagicAttack = Defense = Range = WeaponLevel = EquipLevelMin = 0;
        Refineable = false;
        _aegisEdited = false; ForgedId = null; StatusMessage = string.Empty; Step = 1;
    }

    /// <summary>Exports the client lua data files into a GRF-mirrored "data\..." folder for manual packing.
    /// Never touches a GRF — it only copies loose files into the correct in-archive layout.</summary>
    [RelayCommand]
    private void ExportDataFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder to export the client data tree into",
        };
        if (picker.ShowDialog() != true) return;

        try
        {
            string root = picker.FolderName;
            string dataInfo = Path.Combine(root, "data", "luafiles514", "lua files", "datainfo");
            Directory.CreateDirectory(dataInfo);

            string srcDir = Path.Combine(_session.Paths.LuaFilesRoot, "datainfo");
            int copied = 0;
            foreach (var file in new[] { "accessoryid.lub", "accname.lub", "accname_eng.lub" })
            {
                string src = Path.Combine(srcDir, file);
                if (File.Exists(src)) { File.Copy(src, Path.Combine(dataInfo, file), true); copied++; }
            }

            // Pre-create the texture/sprite folders (Korean RO names) so the user just drops assets in.
            string ui = "유저인터페이스";   // 유저인터페이스 (user interface)
            string acc = "악세사리";                     // 악세사리 (accessory)
            string female = "여";                                    // 여
            string male = "남";                                      // 남
            Directory.CreateDirectory(Path.Combine(root, "data", "texture", ui, "item"));
            Directory.CreateDirectory(Path.Combine(root, "data", "texture", ui, "collection"));
            Directory.CreateDirectory(Path.Combine(root, "data", "sprite", acc, female));
            Directory.CreateDirectory(Path.Combine(root, "data", "sprite", acc, male));

            string icon = string.IsNullOrWhiteSpace(IconResource) ? "<resourceName>" : IconResource.Trim();
            string spr = string.IsNullOrWhiteSpace(SpriteName) ? "<spriteName>" : SpriteName.Trim();
            File.WriteAllText(Path.Combine(root, "READ ME - pack into your GRF.txt"),
                "Midgard Studio — client data export\r\n" +
                "===================================\r\n\r\n" +
                "Pack the 'data' folder in this directory into your client GRF with GRF Editor.\r\n" +
                "Midgard Studio never edits your GRF directly — you pack these loose files yourself.\r\n\r\n" +
                $"Copied {copied} lua data file(s) into:\r\n" +
                "  data\\luafiles514\\lua files\\datainfo\\\r\n\r\n" +
                "Drop your own art files into these (already-created) folders:\r\n" +
                $"  Inventory icon:   data\\texture\\유저인터페이스\\item\\{icon}.bmp\r\n" +
                $"  Collection art:   data\\texture\\유저인터페이스\\collection\\{icon}.bmp\r\n" +
                $"  Headgear sprite:  data\\sprite\\악세사리\\여\\여_{spr}.spr  (+ .act)   [female]\r\n" +
                $"                    data\\sprite\\악세사리\\남\\남_{spr}.spr  (+ .act)   [male]\r\n");

            StatusMessage = $"Exported {copied} data file(s) + folder layout. Pack the 'data' folder into your GRF.";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
            }
            catch { /* shell-open is non-fatal */ }
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed: " + ex.Message;
        }
    }

    private int NextFreeId(MidgardStudio.Core.Overlay.OverlayTable overlay)
    {
        const int start = 30000;
        var used = new HashSet<int>();
        foreach (var r in overlay.Effective()) used.Add(r.GetInt("Id"));
        int id = start;
        while (used.Contains(id)) id++;
        return id;
    }

    private static List<string> SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : text.Replace("\r\n", "\n").Split('\n').ToList();
}
