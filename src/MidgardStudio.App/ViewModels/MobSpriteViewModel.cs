using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Common;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Model;

namespace MidgardStudio.App.ViewModels;

/// <summary>
/// Client sprite registration for the selected monster: writes the npcidentity.lub + jobname.lub
/// mapping (JT_&lt;NAME&gt; = mob id, sprite name) and previews the sprite from the GRF.
/// </summary>
public sealed partial class MobSpriteViewModel : ObservableObject
{
    private readonly DbRecord _server;
    private readonly MobSpriteService _service;
    private readonly GrfImageService _images;

    public MobSpriteViewModel(DbRecord server, MobSpriteService service, GrfImageService images)
    {
        _server = server;
        _service = service;
        _images = images;

        _spriteName = server.GetString("AegisName") ?? string.Empty; // sprite name defaults to AegisName
        RegisteredConstant = service.IsAvailable ? service.FindConstantForMob(server.GetInt("Id")) : null;
        RefreshPreview();
    }

    public bool CanRegister => _service.IsAvailable;

    public string? RegisteredConstant { get; }

    [ObservableProperty]
    private string _spriteName;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ImageSource? _spritePreview;

    /// <summary>Decoded, animatable sprite frames for the current sprite name (null when not in the GRF).</summary>
    [ObservableProperty]
    private SpriteAnimation? _animation;

    public bool HasAnimation => Animation is not null;

    partial void OnAnimationChanged(SpriteAnimation? value) => OnPropertyChanged(nameof(HasAnimation));

    [RelayCommand]
    private void Register()
    {
        if (!_service.IsAvailable || string.IsNullOrWhiteSpace(SpriteName)) return;
        try
        {
            string aegis = _server.GetString("AegisName") ?? ("mob" + _server.GetInt("Id"));
            var result = _service.RegisterMob(_server.GetInt("Id"), aegis, SpriteName.Trim());
            StatusMessage = result.AlreadyRegistered
                ? $"Updated jobname: {result.ConstantName} → '{result.Sprite}'."
                : $"Registered {result.ConstantName} = {_server.GetInt("Id")} → sprite '{result.Sprite}'. npcidentity.lub & jobname.lub updated.";
            RefreshPreview();
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed: " + ex.Message;
        }
    }

    partial void OnSpriteNameChanged(string value) => RefreshPreview();

    private void RefreshPreview()
    {
        Animation = _images.MonsterAnimation(SpriteName);
        SpritePreview = Animation is null ? _images.MonsterSprite(SpriteName) : null;
    }
}
