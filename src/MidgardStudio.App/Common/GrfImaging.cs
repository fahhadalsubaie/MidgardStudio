using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GRF.Image;

namespace MidgardStudio.App.Common;

/// <summary>Converts a decoded <see cref="GrfImage"/> into a WPF <see cref="ImageSource"/>.</summary>
public static class GrfImaging
{
    public static ImageSource? ToImageSource(GrfImage? image)
    {
        if (image is null || image.Pixels is null || image.Pixels.Length == 0) return null;

        try
        {
            switch (image.GrfImageType)
            {
                case GrfImageType.Bgra32:
                    return Frozen(image.Width, image.Height, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
                case GrfImageType.Bgr32:
                    return Frozen(image.Width, image.Height, PixelFormats.Bgr32, null, image.Pixels, image.Width * 4);
                case GrfImageType.Bgr24:
                    return Frozen(image.Width, image.Height, PixelFormats.Bgr24, null, image.Pixels, image.Width * 3);
                case GrfImageType.Indexed8:
                    // Convert to Bgra32 (stride = width*4, always 4-byte aligned). WPF's Indexed8
                    // BitmapSource.Create rejects a non-4-aligned stride, which silently broke any image
                    // whose width isn't a multiple of 4 (e.g. 75-wide collection illustrations).
                    return IndexedToBgra32(image.Width, image.Height, image.Pixels, image.Palette);
                default:
                    return FromEncodedBytes(image.Pixels); // NotEvaluated*: raw png/jpg/bmp/tga bytes
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource Frozen(int w, int h, PixelFormat fmt, BitmapPalette? palette, byte[] pixels, int stride)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, fmt, palette, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Expands an 8-bit palettized image into a 32-bit BGRA buffer (palette index 0 = transparent,
    /// per RO convention). Produces a 4-byte-aligned stride that WPF accepts for any width.</summary>
    private static ImageSource IndexedToBgra32(int w, int h, byte[] indices, byte[]? palette)
    {
        var bgra = new byte[w * h * 4];
        bool hasPalette = palette is not null && palette.Length >= 256 * 4;
        int count = Math.Min(indices.Length, w * h);
        for (int i = 0; i < count; i++)
        {
            int idx = indices[i];
            byte r = 0, g = 0, b = 0;
            if (hasPalette)
            {
                r = palette![idx * 4 + 0];
                g = palette[idx * 4 + 1];
                b = palette[idx * 4 + 2];
            }
            int o = i * 4;
            bgra[o + 0] = b;
            bgra[o + 1] = g;
            bgra[o + 2] = r;
            bgra[o + 3] = (byte)(idx == 0 ? 0 : 255); // RO: palette index 0 is the transparent background
        }
        return Frozen(w, h, PixelFormats.Bgra32, null, bgra, w * 4);
    }

    private static ImageSource? FromEncodedBytes(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
