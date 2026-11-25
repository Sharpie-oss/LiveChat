using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveChat.Services;

/// <summary>
/// Images animées que WPF affiche figées : PNG animés (APNG, décodés par <see cref="Apng"/>) et WebP animés
/// (décodés par le codec WebP de Windows, qui fournit chaque image déjà composée avec sa durée).
/// </summary>
public static class AnimatedImages
{
    /// <summary>Images de l'animation, ou null pour une image fixe ou un fichier non exploitable.</summary>
    public static IReadOnlyList<AnimationFrame>? TryDecode(byte[] data)
    {
        if (Apng.IsAnimated(data))
        {
            return Apng.Decode(data);
        }

        return IsWebP(data) ? DecodeWebP(data) : null;
    }

    internal static bool IsWebP(byte[] data) =>
        data.Length >= 16 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    private static IReadOnlyList<AnimationFrame>? DecodeWebP(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count < 2)
            {
                return null;
            }

            var width = decoder.Frames[0].PixelWidth;
            var height = decoder.Frames[0].PixelHeight;
            if ((long)width * height * decoder.Frames.Count > Apng.MaxTotalPixels)
            {
                return null; // Trop lourd en mémoire : image fixe.
            }

            var frames = new List<AnimationFrame>(decoder.Frames.Count);
            var pixels = new byte[width * height * 4];
            foreach (var frame in decoder.Frames)
            {
                if (frame.PixelWidth != width || frame.PixelHeight != height)
                {
                    return null; // Le codec de Windows compose les images : une taille différente est inattendue.
                }

                new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0).CopyPixels(pixels, width * 4, 0);
                var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
                image.Freeze();

                var milliseconds = frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/ANMF/FrameDuration") is { } value
                    ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
                    : 0;

                // Comme les navigateurs : une durée inférieure à 20 ms compte pour 100 ms.
                frames.Add(new AnimationFrame(image, TimeSpan.FromMilliseconds(milliseconds < 20 ? 100 : milliseconds)));
            }

            return frames;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // Codec WebP absent ou fichier abîmé : l'image fixe sera tentée.
        }
    }
}
