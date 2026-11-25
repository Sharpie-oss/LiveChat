using System.Windows.Media.Imaging;

namespace LiveChat.Services;

/// <summary>Durées d'affichage des médias animés.</summary>
public static class MediaTiming
{
    /// <summary>
    /// Durée d'affichage d'un média animé (GIF, GIF Tenor) : la durée configurée pour les images, ou une boucle
    /// complète si elle est plus longue, sans dépasser la durée maximale des vidéos.
    /// </summary>
    public static TimeSpan DisplayDuration(TimeSpan animationLength, TimeSpan imageDuration, TimeSpan maxDuration)
    {
        var target = animationLength > imageDuration ? animationLength : imageDuration;
        return target < maxDuration ? target : maxDuration;
    }

    /// <summary>
    /// Durée d'une boucle d'un GIF animé, d'après les délais de ses images. Comme dans les navigateurs, un délai
    /// inférieur à 20 ms compte pour 100 ms. Renvoie zéro si le fichier n'est pas un GIF lisible.
    /// </summary>
    public static TimeSpan ReadGifLength(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var hundredths = 0L;
            foreach (var frame in decoder.Frames)
            {
                var delay = frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is ushort value ? value : 0;
                hundredths += delay < 2 ? 10 : delay;
            }

            return decoder.Frames.Count > 1 ? TimeSpan.FromMilliseconds(hundredths * 10) : TimeSpan.Zero;
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return TimeSpan.Zero;
        }
    }
}
