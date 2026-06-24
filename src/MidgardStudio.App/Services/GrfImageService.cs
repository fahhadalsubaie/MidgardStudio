using System.IO;
using System.Windows.Media;
using MidgardStudio.App.Common;
using MidgardStudio.Grf;

namespace MidgardStudio.App.Services;

/// <summary>Resolves client asset images (item icon / illustration / sprite animation) from the layered GRFs.</summary>
public sealed class GrfImageService
{
    private readonly GrfService _grf;

    public GrfImageService(GrfService grf) => _grf = grf;

    public ImageSource? ItemIcon(string? resourceName) =>
        string.IsNullOrWhiteSpace(resourceName) ? null : GrfImaging.ToImageSource(_grf.GetImage(GrfAssetPaths.ItemIcon(resourceName!)));

    public ImageSource? ItemCollection(string? resourceName) =>
        string.IsNullOrWhiteSpace(resourceName) ? null : GrfImaging.ToImageSource(_grf.GetImage(GrfAssetPaths.ItemCollection(resourceName!)));

    public ImageSource? MonsterSprite(string? spriteName) =>
        string.IsNullOrWhiteSpace(spriteName) ? null : GrfImaging.ToImageSource(_grf.GetImage(GrfAssetPaths.MonsterSprite(spriteName!)));

    /// <summary>Decodes the monster's .spr/.act from the GRF into an animatable sequence of frames.</summary>
    public SpriteAnimation? MonsterAnimation(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return null;
        string sprPath = GrfAssetPaths.MonsterSprite(spriteName!);
        byte[]? spr = _grf.GetData(sprPath);
        if (spr is null) return null;
        byte[]? act = _grf.GetData(Path.ChangeExtension(sprPath, ".act"));
        return SpriteRenderer.Build(spr, act);
    }
}
