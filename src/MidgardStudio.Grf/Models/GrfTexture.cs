using GRF.Image;

namespace MidgardStudio.Grf;

/// <summary>Normalizes a decoded <see cref="GrfImage"/> (bmp/tga/indexed/…) into top-row-first BGRA pixels
/// for GL upload, mapping RO's magenta color key to transparent.</summary>
internal static class GrfTexture
{
    public static ModelTexture? ToBgra(GrfImage image)
    {
        try
        {
            image.Convert(GrfImageType.Bgra32); // decode/normalize any source format to 32-bit BGRA
            if (image.Pixels is null || image.Width <= 0 || image.Height <= 0) return null;

            int w = image.Width, h = image.Height, need = w * h * 4;
            if (image.Pixels.Length < need) return null;

            var bgra = new byte[need];
            Array.Copy(image.Pixels, bgra, need);
            ApplyMagentaKey(bgra);
            return new ModelTexture { Width = w, Height = h, Bgra = bgra };
        }
        catch { return null; }
    }

    // RO textures use pure magenta (B255 G0 R255) as the transparent color key.
    private static void ApplyMagentaKey(byte[] bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
            if (bgra[i] >= 248 && bgra[i + 1] <= 8 && bgra[i + 2] >= 248)
                bgra[i + 3] = 0;
    }
}
