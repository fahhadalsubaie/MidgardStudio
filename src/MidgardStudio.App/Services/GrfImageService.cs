using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MidgardStudio.App.Common;
using MidgardStudio.Grf;

namespace MidgardStudio.App.Services;

/// <summary>Resolves client asset images (item icon / illustration / sprite animation) from the layered GRFs.</summary>
public sealed class GrfImageService
{
    private readonly GrfService _grf;

    // Decoding a GRF image (decompress + bitmap build) is expensive and was repeated on every selection /
    // resource-name commit. Cache the frozen ImageSource keyed by GRF path and drop it whenever the configured
    // sources change. Frozen images are safe to share/cross-thread. The cache is bounded by BOTH an entry count
    // and an approximate byte budget — a flat count cap let a few large sprites/illustrations grow it to GBs.
    private const int CacheCap = 2048;
    private const long ByteBudget = 128L * 1024 * 1024; // ~128 MB of decoded pixels
    private readonly Dictionary<string, (ImageSource? Img, long Bytes)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();
    private readonly object _lock = new();
    private long _bytes;

    public GrfImageService(GrfService grf)
    {
        _grf = grf;
        _grf.SourcesChanged += ClearCache;
    }

    private void ClearCache()
    {
        lock (_lock) { _cache.Clear(); _order.Clear(); _bytes = 0; }
    }

    private ImageSource? Cached(string path, Func<ImageSource?> decode)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var hit)) return hit.Img;
            var img = decode();
            long size = Estimate(img);
            _cache[path] = (img, size);
            _bytes += size;
            _order.Enqueue(path);
            // Evict oldest until both bounds hold. The just-added entry is evicted last, so an image larger
            // than the whole budget degrades to "not cached" rather than evicting everything else first.
            while (_order.Count > 0 && (_cache.Count > CacheCap || _bytes > ByteBudget))
            {
                if (_cache.Remove(_order.Dequeue(), out var ev)) _bytes -= ev.Bytes;
            }
            return img;
        }
    }

    /// <summary>Approximate decoded size in bytes of a bitmap (pixels × bytes-per-pixel); small const otherwise.</summary>
    private static long Estimate(ImageSource? img) =>
        img is BitmapSource b ? (long)b.PixelWidth * b.PixelHeight * Math.Max(1, (b.Format.BitsPerPixel + 7) / 8) : 4096;

    public ImageSource? ItemIcon(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName)) return null;
        var path = GrfAssetPaths.ItemIcon(resourceName!);
        return Cached(path, () => GrfImaging.ToImageSource(_grf.GetImage(path)));
    }

    public ImageSource? ItemCollection(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName)) return null;
        var path = GrfAssetPaths.ItemCollection(resourceName!);
        return Cached(path, () => GrfImaging.ToImageSource(_grf.GetImage(path)));
    }

    public ImageSource? MonsterSprite(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return null;
        var path = GrfAssetPaths.MonsterSprite(spriteName!);
        return Cached(path, () => GrfImaging.ToImageSource(_grf.GetImage(path)));
    }

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
