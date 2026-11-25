using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveChat.Services;

namespace LiveChat.Tests;

public class ApngTests
{
    private sealed record TestFrame(int Width, int Height, int X, int Y, uint Bgra, ushort DelayMs, byte Dispose = 0, byte Blend = 0);

    private static readonly uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF;

    /// <summary>PNG (encodé par WPF) d'un rectangle de couleur unie, découpé en blocs.</summary>
    private static List<(string Type, byte[] Data)> EncodeSolid(int width, int height, uint bgra)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(pixels.AsSpan(i), bgra);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();

        var chunks = new List<(string, byte[])>();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            chunks.Add((System.Text.Encoding.ASCII.GetString(png, offset + 4, 4), png.AsSpan(offset + 8, length).ToArray()));
            offset += 12 + length;
        }

        return chunks;
    }

    private static byte[] BuildApng(int width, int height, params TestFrame[] frames)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = EncodeSolid(width, height, Red).First(c => c.Type == "IHDR").Data;
        Apng.WriteChunk(stream, "IHDR", header);

        var actl = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(actl, frames.Length);
        Apng.WriteChunk(stream, "acTL", actl);

        var sequence = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var f = frames[i];
            var fctl = new byte[26];
            BinaryPrimitives.WriteInt32BigEndian(fctl, sequence++);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(4), f.Width);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(8), f.Height);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(12), f.X);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(16), f.Y);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20), f.DelayMs);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22), 1000);
            fctl[24] = f.Dispose;
            fctl[25] = f.Blend;
            Apng.WriteChunk(stream, "fcTL", fctl);

            var data = EncodeSolid(f.Width, f.Height, f.Bgra).Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
            if (i == 0)
            {
                Apng.WriteChunk(stream, "IDAT", data);
            }
            else
            {
                var fdat = new byte[data.Length + 4];
                BinaryPrimitives.WriteInt32BigEndian(fdat, sequence++);
                data.CopyTo(fdat, 4);
                Apng.WriteChunk(stream, "fdAT", fdat);
            }
        }

        Apng.WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static uint Pixel(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return BinaryPrimitives.ReadUInt32LittleEndian(pixel);
    }

    [Fact]
    public void Decode_ReturnsEachFrameWithItsDelay()
    {
        var apng = BuildApng(4, 4,
            new TestFrame(4, 4, 0, 0, Red, 50),
            new TestFrame(4, 4, 0, 0, Green, 200),
            new TestFrame(4, 4, 0, 0, Blue, 5));

        var frames = Apng.Decode(apng);

        Assert.True(Apng.IsAnimated(apng));
        Assert.NotNull(frames);
        Assert.Equal([Red, Green, Blue], frames.Select(f => Pixel(f.Image, 2, 2)));
        Assert.Equal([50.0, 200.0, 100.0], frames.Select(f => f.Delay.TotalMilliseconds)); // 5 ms compte pour 100 ms.
    }

    [Fact]
    public void Decode_AppliesBlendAndDisposeOperations()
    {
        var apng = BuildApng(4, 4,
            new TestFrame(4, 4, 0, 0, Red, 100),
            new TestFrame(2, 2, 2, 2, Blue, 100, Dispose: 1), // Effacée (transparente) après affichage.
            new TestFrame(1, 1, 0, 0, Green, 100, Blend: 1));

        var frames = Apng.Decode(apng)!;

        Assert.Equal(Blue, Pixel(frames[1].Image, 3, 3));
        Assert.Equal(Red, Pixel(frames[1].Image, 0, 0));
        Assert.Equal(Green, Pixel(frames[2].Image, 0, 0));
        Assert.Equal(Red, Pixel(frames[2].Image, 1, 1));
        Assert.Equal(0u, Pixel(frames[2].Image, 3, 3)); // Zone effacée par l'image précédente.
    }

    [Fact]
    public void Decode_NeverThrowsOnCorruptedFiles()
    {
        var reference = BuildApng(8, 8,
            new TestFrame(8, 8, 0, 0, Red, 100),
            new TestFrame(4, 4, 2, 2, Green, 100, Dispose: 2, Blend: 1),
            new TestFrame(8, 8, 0, 0, Blue, 100, Dispose: 1));
        var random = new Random(42);

        for (var attempt = 0; attempt < 300; attempt++)
        {
            var corrupted = (byte[])reference.Clone();
            for (var i = 0; i < 1 + attempt % 4; i++)
            {
                corrupted[random.Next(8, corrupted.Length)] = (byte)random.Next(256);
            }

            var error = Record.Exception(() => Apng.Decode(corrupted));
            Assert.Null(error); // Au pire null : l'image fixe sera affichée à la place.
        }
    }

    [Fact]
    public void Decode_StillPng_IsNotAnimated()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)));
        using var stream = new MemoryStream();
        encoder.Save(stream);

        Assert.False(Apng.IsAnimated(stream.ToArray()));
        Assert.Null(Apng.Decode(stream.ToArray()));
        Assert.Null(Apng.Decode("pas une image"u8.ToArray()));
    }
}
