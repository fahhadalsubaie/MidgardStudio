using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Edits the client-side item text (itemInfo) for the selected item: display/resource/description
/// names, slot count, ClassNum (= server View) and costume flag, with a live GRF icon preview.
/// Slot count and ClassNum are seeded from the server Slots/View. Edits route through the undo stack.
/// </summary>
public sealed partial class ClientTextViewModel : ObservableObject
{
    private readonly DbRecord _server;
    private readonly ClientItemService _client;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService? _sprite;
    private readonly EditCommandStack _stack;
    private readonly AppSettingsService _settings;
    private readonly Func<string, string?>? _skill;
    private readonly Func<IReadOnlyList<IconItemRow>>? _iconRows;
    private readonly ItemInfoEntry _entry;

    // True when 'server' is a synthetic stand-in for an item whose server record was deleted (a client-only
    // "orphan"). Server-driven actions (autocomplete, Slots/View mirroring) have no real data to read, so they're
    // disabled to avoid zeroing the client entry's real values.
    private readonly bool _serverIsDetached;

    public ClientTextViewModel(DbRecord server, ClientItemService client, GrfImageService images, EditCommandStack stack,
        AppSettingsService settings, Func<string, string?>? skillResolver = null, SpriteLinkService? sprite = null,
        Func<IReadOnlyList<IconItemRow>>? iconRows = null, bool serverIsDetached = false)
    {
        _server = server;
        _client = client;
        _images = images;
        _sprite = sprite;
        _stack = stack;
        _settings = settings;
        _skill = skillResolver;
        _iconRows = iconRows;
        _serverIsDetached = serverIsDetached;

        int id = server.GetInt("Id");

        var locations = server.GetSet("Locations");
        IsHeadgear = locations is not null && locations.Any(l =>
            l.StartsWith("Head_", StringComparison.Ordinal) || l.StartsWith("Costume_Head", StringComparison.Ordinal));
        _entry = client.GetOrCreate(id);

        if (!client.Has(id))
        {
            // Seed a fresh client entry from the server record.
            _entry.IdentifiedDisplayName = server.GetString("Name") ?? string.Empty;
            _entry.SlotCount = server.GetInt("Slots");
            _entry.ClassNum = server.GetInt("View");
        }

        IsOfficialId = client.IsOfficial(id);
        TargetLabel = client.TargetFor(id) == ItemInfoTarget.Override
            ? "Overrides official client entry (tbl_override)"
            : "New custom client entry (tbl_custom)";

        RefreshIcon();
    }

    public bool IsOfficialId { get; }

    public string TargetLabel { get; }

    public bool IsHeadgear { get; }

    public bool CanLinkSprite => IsHeadgear && _sprite is { IsAvailable: true };

    [ObservableProperty]
    private ImageSource? _iconPreview;

    /// <summary>The in-game item-detail illustration (data\texture\…\collection\&lt;resource&gt;.bmp); falls back to the icon.</summary>
    [ObservableProperty]
    private ImageSource? _collectionPreview;

    [ObservableProperty]
    private string _spriteName = string.Empty;

    [ObservableProperty]
    private string? _spriteLinkMessage;

    /// <summary>Opens the icon picker (copy an existing item's icon or browse a GRF/loose source) and sets the
    /// identified resource name from the choice. Same picker the Item Forge uses.</summary>
    [RelayCommand]
    private void BrowseIcon()
    {
        var picker = new IconPickerViewModel(_images, _iconRows?.Invoke() ?? System.Array.Empty<IconItemRow>());
        var dlg = new Views.IconPickerDialog(picker) { Owner = System.Windows.Application.Current.MainWindow };
        bool? ok = dlg.ShowDialog();
        _images.CloseIconSource(); // release the picker's single-source reader + thumbnail cache
        if (ok == true && !string.IsNullOrWhiteSpace(picker.Result))
            IdentifiedResourceName = picker.Result!.Trim();
    }

    /// <summary>Opens the headgear sprite picker (animated previews) and sets the sprite base name; the user then
    /// clicks Link sprite. Same picker the Item Forge uses.</summary>
    [RelayCommand]
    private void BrowseSprite()
    {
        var picker = new SpritePickerViewModel(_images);
        var dlg = new Views.SpritePickerDialog(picker) { Owner = System.Windows.Application.Current.MainWindow };
        bool? ok = dlg.ShowDialog();
        _images.CloseIconSource(); // release the picker's single-source reader
        if (ok == true && !string.IsNullOrWhiteSpace(picker.Result))
            SpriteName = picker.Result!.Trim();
    }

    [RelayCommand]
    private void LinkSprite()
    {
        if (_sprite is null || string.IsNullOrWhiteSpace(SpriteName)) return;
        try
        {
            string sprite = SpriteName.Trim();

            // Already in accname/accessoryid? Reuse that View id — registering a duplicate under a fresh id leaves
            // the item invisible in-game (same rule as the Item Forge). Only a genuinely new sprite registers.
            if (_sprite.FindViewForSprite(sprite) is { } existingView)
            {
                if (_server.Origin != RecordOrigin.Base)
                    _stack.Execute(new SetFieldCommand(_server, "View", existingView));
                ClassNum = existingView;
                SpriteLinkMessage = $"'{sprite}' is already registered — reusing View {existingView}; no new entry written. "
                    + (_server.Origin == RecordOrigin.Base ? $"Override the item and set View = {existingView}." : "Server View set.");
                return;
            }

            string aegis = _server.GetString("AegisName") ?? ("item" + _server.GetInt("Id"));
            var planned = _sprite.PlanAccessory(aegis, sprite);
            // Queue the registration + the View edit as one undo step (written on Save).
            using (_stack.BeginBatch("Link accessory sprite"))
            {
                _stack.Execute(new ListMutateCommand("Link accessory sprite",
                    () => _sprite.AddPending(planned), () => _sprite.RemovePending(planned)));
                if (_server.Origin != RecordOrigin.Base)
                    _stack.Execute(new SetFieldCommand(_server, "View", planned.Id));
            }
            ClassNum = planned.Id;

            SpriteLinkMessage = $"Queued '{planned.Sprite}' as {planned.ConstantName} → View {planned.Id}; written on Save. "
                + (_server.Origin == RecordOrigin.Base ? $"Override the item and set View = {planned.Id}." : "Server View set.");
        }
        catch (Exception ex)
        {
            SpriteLinkMessage = "Link failed: " + ex.Message;
        }
    }

    /// <summary>Copies the identified display name + description over to the unidentified side.</summary>
    [RelayCommand]
    private void CopyToUnidentified()
    {
        using (_stack.BeginBatch("Copy identified → unidentified"))
        {
            UnidentifiedDisplayName = IdentifiedDisplayName;
            UnidentifiedDescription = IdentifiedDescription;
        }
    }

    /// <summary>Copies the unidentified display name + description over to the identified side.</summary>
    [RelayCommand]
    private void CopyToIdentified()
    {
        using (_stack.BeginBatch("Copy unidentified → identified"))
        {
            IdentifiedDisplayName = UnidentifiedDisplayName;
            IdentifiedDescription = UnidentifiedDescription;
        }
    }

    /// <summary>
    /// Autocomplete: fills the identified display name + description (and slots/ClassNum) from the server
    /// item record, type-aware and script-driven (heal amounts, weapon element, stat bonuses) per the
    /// user's Autocomplete settings. For an official item with existing client text it restores the
    /// canonical official name/description. Also applies the default unidentified description. One undo step.
    /// </summary>
    /// <summary>Autocomplete regenerates client text FROM the server record, so it's unavailable for an orphaned
    /// client entry (no server item) — running it would zero the real Slots/View.</summary>
    public bool CanAutocomplete => !_serverIsDetached;

    [RelayCommand(CanExecute = nameof(CanAutocomplete))]
    private void Autocomplete()
    {
        if (_serverIsDetached) return; // no server item to autocomplete from — never touch the client entry's real values
        var cfg = _settings.Settings.Autocomplete;
        var gen = new ItemAutocomplete(cfg, _skill);

        // Always regenerate the property block from server data, preserving any leading lore/flavor lines.
        string newName = gen.DisplayName(_server);
        var newDesc = gen.BuildDescription(_server, _entry.IdentifiedDescription);

        using (_stack.BeginBatch("Autocomplete client text from server"))
        {
            if (cfg.OverwriteExisting || string.IsNullOrWhiteSpace(IdentifiedDisplayName))
                IdentifiedDisplayName = newName;
            if (cfg.OverwriteExisting || string.IsNullOrWhiteSpace(IdentifiedDescription))
                IdentifiedDescription = string.Join(Environment.NewLine, newDesc);

            // Slots / ClassNum always mirror the server (cross-file invariant).
            SlotCount = _server.GetInt("Slots");
            ClassNum = _server.GetInt("View");

            // Default unidentified description.
            if (cfg.DefaultUnidentifiedDescription.Length > 0 &&
                (cfg.OverwriteExisting || string.IsNullOrWhiteSpace(UnidentifiedDescription)))
                UnidentifiedDescription = cfg.DefaultUnidentifiedDescription;
        }
    }

    /// <summary>Copies this item's client itemInfo entry as a Lua table block to the clipboard.</summary>
    [RelayCommand]
    private void CopyLua()
    {
        var lua = ItemInfoWriter.FormatEntry(_entry);
        try { System.Windows.Clipboard.SetText(lua); } catch { /* clipboard busy */ }
    }

    public string IdentifiedDisplayName
    {
        get => _entry.IdentifiedDisplayName;
        set { var o = _entry.IdentifiedDisplayName; if (o != value) Commit("client display name", () => _entry.IdentifiedDisplayName = value, () => _entry.IdentifiedDisplayName = o, nameof(IdentifiedDisplayName)); }
    }

    public string IdentifiedResourceName
    {
        get => _entry.IdentifiedResourceName;
        set { var o = _entry.IdentifiedResourceName; if (o != value) Commit("client resource name", () => _entry.IdentifiedResourceName = value, () => _entry.IdentifiedResourceName = o, nameof(IdentifiedResourceName), refreshIcon: true); }
    }

    public string IdentifiedDescription
    {
        get => string.Join(Environment.NewLine, _entry.IdentifiedDescription);
        set { var o = _entry.IdentifiedDescription; var n = SplitLines(value); Commit("client description", () => _entry.IdentifiedDescription = n, () => _entry.IdentifiedDescription = o, nameof(IdentifiedDescription)); }
    }

    public string UnidentifiedDisplayName
    {
        get => _entry.UnidentifiedDisplayName;
        set { var o = _entry.UnidentifiedDisplayName; if (o != value) Commit("client unidentified name", () => _entry.UnidentifiedDisplayName = value, () => _entry.UnidentifiedDisplayName = o, nameof(UnidentifiedDisplayName)); }
    }

    public string UnidentifiedResourceName
    {
        get => _entry.UnidentifiedResourceName;
        set { var o = _entry.UnidentifiedResourceName; if (o != value) Commit("client unidentified resource", () => _entry.UnidentifiedResourceName = value, () => _entry.UnidentifiedResourceName = o, nameof(UnidentifiedResourceName)); }
    }

    public string UnidentifiedDescription
    {
        get => string.Join(Environment.NewLine, _entry.UnidentifiedDescription);
        set { var o = _entry.UnidentifiedDescription; var n = SplitLines(value); Commit("client unidentified description", () => _entry.UnidentifiedDescription = n, () => _entry.UnidentifiedDescription = o, nameof(UnidentifiedDescription)); }
    }

    public int SlotCount
    {
        get => _entry.SlotCount;
        set { var o = _entry.SlotCount; if (o != value) Commit("client slot count", () => _entry.SlotCount = value, () => _entry.SlotCount = o, nameof(SlotCount)); }
    }

    public int ClassNum
    {
        get => _entry.ClassNum;
        set { var o = _entry.ClassNum; if (o != value) Commit("client ClassNum", () => _entry.ClassNum = value, () => _entry.ClassNum = o, nameof(ClassNum)); }
    }

    public bool Costume
    {
        get => _entry.Costume;
        set { var o = _entry.Costume; if (o != value) Commit("client costume", () => _entry.Costume = value, () => _entry.Costume = o, nameof(Costume)); }
    }

    private void Commit(string description, Action apply, Action revert, string propertyName, bool refreshIcon = false)
    {
        _stack.Execute(new ListMutateCommand(
            "Client: " + description,
            () => { apply(); _client.Upsert(_entry); OnPropertyChanged(propertyName); if (refreshIcon) RefreshIcon(); },
            () => { revert(); _client.Upsert(_entry); OnPropertyChanged(propertyName); if (refreshIcon) RefreshIcon(); }));
    }

    private void RefreshIcon()
    {
        var res = _entry.IdentifiedResourceName;
        IconPreview = _images.ItemIcon(res);
        // The detail view in-game loads the larger "collection" illustration; show that, icon as fallback.
        CollectionPreview = _images.ItemCollection(res) ?? IconPreview;
    }

    private static List<string> SplitLines(string s) =>
        s.Replace("\r\n", "\n").Split('\n').ToList();
}
