using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// The client-side item editor (separate from the Server Items section): the same item list, but the
/// detail panel edits the client itemInfo (display/resource/description names, slots, ClassNum, costume,
/// headgear sprite + icon) read from itemInfo.lua / itemInfo_C.lua.
/// </summary>
public sealed partial class ClientItemsViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly ClientItemService _clientItems;
    private readonly GrfImageService _images;
    private readonly SpriteLinkService _sprite;
    private readonly DbSchema _itemSchema;
    private OverlayTable? _overlay;

    public ClientItemsViewModel(WorkspaceSession session, ClientItemService clientItems, GrfImageService images,
        SpriteLinkService sprite, DbSchema itemSchema)
    {
        _session = session;
        _clientItems = clientItems;
        _images = images;
        _sprite = sprite;
        _itemSchema = itemSchema;
    }

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private DbListViewModel? _list;

    [ObservableProperty]
    private ClientTextViewModel? _editor;

    public bool HasEditor => Editor is not null;

    partial void OnEditorChanged(ClientTextViewModel? value) => OnPropertyChanged(nameof(HasEditor));

    public async Task EnsureLoadedAsync()
    {
        if (_overlay is not null) return;

        IsLoading = true;
        var modeSet = await Task.Run(() => _session.GetModeSet(_itemSchema));
        _overlay = modeSet.For(_session.Mode);

        var list = new DbListViewModel(_overlay,
            key => _images.ItemIcon(_clientItems.GetOrCreate((int)key.AsInt).IdentifiedResourceName));
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DbListViewModel.SelectedRow))
                Editor = list.SelectedRow is { } row
                    ? new ClientTextViewModel(row.Record, _clientItems, _images, _session.Commands, _sprite)
                    : null;
        };

        List = list;
        IsLoading = false;
        list.SelectedRow = list.Rows.FirstOrDefault();
        if (_pendingSelect is { } sel) { list.SelectByKey(sel); _pendingSelect = null; }
    }

    private RecordKey? _pendingSelect;

    public void SelectRow(RecordKey key)
    {
        if (List is not null) List.SelectByKey(key);
        else _pendingSelect = key; // applied once loaded (navigation from Items)
    }
}
