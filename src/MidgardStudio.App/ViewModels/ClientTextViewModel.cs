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
    private readonly ItemInfoEntry _entry;

    public ClientTextViewModel(DbRecord server, ClientItemService client, GrfImageService images, EditCommandStack stack, SpriteLinkService? sprite = null)
    {
        _server = server;
        _client = client;
        _images = images;
        _sprite = sprite;
        _stack = stack;

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

    [RelayCommand]
    private void LinkSprite()
    {
        if (_sprite is null || string.IsNullOrWhiteSpace(SpriteName)) return;
        try
        {
            string aegis = _server.GetString("AegisName") ?? ("item" + _server.GetInt("Id"));
            var result = _sprite.LinkAccessory(aegis, SpriteName.Trim());
            ClassNum = result.ViewId;
            if (_server.Origin != RecordOrigin.Base)
                _stack.Execute(new SetFieldCommand(_server, "View", result.ViewId));

            SpriteLinkMessage = $"Linked '{result.Sprite}' as {result.ConstantName} → View {result.ViewId}. "
                + (_server.Origin == RecordOrigin.Base ? $"Override the item and set View = {result.ViewId}." : "Server View set.");
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
