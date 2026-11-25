using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media.Imaging;
using LiveChat.Services;

namespace LiveChat.Tests;

public class AnimatedImagesTests
{
    // Blocs « VP8L » (WebP sans perte) de 8×8 pixels rouges et bleus, produits par l'encodeur de Chromium.
    private static readonly byte[] RedVp8L = [0x56, 0x50, 0x38, 0x4C, 0x0F, 0x00, 0x00, 0x00, 0x2F, 0x07, 0xC0, 0x01, 0x00, 0x07, 0x10, 0xFD, 0x8F, 0xFE, 0x07, 0x22, 0xA2, 0xFF, 0x01, 0x00];
    private static readonly byte[] BlueVp8L = [0x56, 0x50, 0x38, 0x4C, 0x0F, 0x00, 0x00, 0x00, 0x2F, 0x07, 0xC0, 0x01, 0x00, 0x07, 0x10, 0xD1, 0xFF, 0xFE, 0x07, 0x22, 0xA2, 0xFF, 0x01, 0x00];

    private static byte[] Chunk(string tag, byte[] data)
    {
        var chunk = new List<byte>(System.Text.Encoding.ASCII.GetBytes(tag));
        chunk.AddRange(BitConverter.GetBytes(data.Length));
        chunk.AddRange(data);
        if (data.Length % 2 == 1)
        {
            chunk.Add(0);
        }

        return [.. chunk];
    }

    private static byte[] UInt24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];

    private static byte[] Frame(int x, int y, int durationMs, byte[] vp8l) =>
        Chunk("ANMF", [.. UInt24(x / 2), .. UInt24(y / 2), .. UInt24(7), .. UInt24(7), .. UInt24(durationMs), 0, .. vp8l]);

    /// <summary>WebP animé de 16×16 : image rouge en haut à gauche, puis image bleue en bas à droite.</summary>
    private static byte[] AnimatedWebP()
    {
        byte[] body =
        [
            .. "WEBP"u8,
            .. Chunk("VP8X", [0x02, 0, 0, 0, .. UInt24(15), .. UInt24(15)]),
            .. Chunk("ANIM", [0, 0, 0, 0, 0, 0]),
            .. Frame(0, 0, 100, RedVp8L),
            .. Frame(8, 8, 250, BlueVp8L),
        ];
        return [.. "RIFF"u8, .. BitConverter.GetBytes(body.Length), .. body];
    }

    private static uint Pixel(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return BinaryPrimitives.ReadUInt32LittleEndian(pixel);
    }

    /// <summary>Le codec WebP de Windows vient du Microsoft Store : absent sur les serveurs (CI GitHub).</summary>
    private static bool WebPCodecInstalled()
    {
        try
        {
            using var stream = new MemoryStream(AnimatedWebP());
            BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public void TryDecode_AnimatedWebP_ReturnsComposedFramesWithDurations()
    {
        if (!WebPCodecInstalled())
        {
            Assert.Null(AnimatedImages.TryDecode(AnimatedWebP())); // Sans codec : pas d'exception, image fixe.
            return;
        }

        var frames = AnimatedImages.TryDecode(AnimatedWebP());

        Assert.NotNull(frames);
        Assert.Equal(2, frames.Count);
        Assert.Equal([100.0, 250.0], frames.Select(f => f.Delay.TotalMilliseconds));
        Assert.Equal(0xFFFF0000u, Pixel(frames[1].Image, 0, 0)); // Le rouge de l'image 1 reste affiché…
        Assert.Equal(0xFF0000FFu, Pixel(frames[1].Image, 15, 15)); // …sous l'image 2, composée par Windows.
    }

    [Fact]
    public void TryDecode_StillWebP_IsNotAnimated()
    {
        byte[] body = [.. "WEBP"u8, .. RedVp8L];
        byte[] still = [.. "RIFF"u8, .. BitConverter.GetBytes(body.Length), .. body];

        Assert.True(AnimatedImages.IsWebP(still));
        Assert.Null(AnimatedImages.TryDecode(still));
    }

    [Fact]
    public void TryDecode_OtherFormats_ReturnNull()
    {
        Assert.Null(AnimatedImages.TryDecode("RIFF....WAVEfmt "u8.ToArray()));
        Assert.Null(AnimatedImages.TryDecode([]));
    }
}
