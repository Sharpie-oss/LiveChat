using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveChat.Services;

/// <summary>Image d'une animation, déjà composée (taille de l'animation entière).</summary>
public sealed record AnimationFrame(BitmapSource Image, TimeSpan Delay);

/// <summary>
/// Décodeur de PNG animés (APNG) : WPF n'affiche que leur première image, or c'est le format de la plupart des
/// stickers Discord. Chaque image est reconstruite en PNG autonome, décodée par WPF, puis composée selon la
/// spécification APNG (fusion SOURCE/OVER, effacement NONE/BACKGROUND/PREVIOUS).
/// </summary>
public static class Apng
{
    /// <summary>Au-delà (largeur × hauteur × images), on garde l'image fixe pour ménager la mémoire (~200 Mo).</summary>
    public const long MaxTotalPixels = 50_000_000;

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Vrai si le fichier est un PNG animé (bloc acTL avant les données d'image).</summary>
    public static bool IsAnimated(byte[] data)
    {
        foreach (var chunk in Chunks(data))
        {
            if (chunk.Type is "acTL")
            {
                return true;
            }

            if (chunk.Type is "IDAT")
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Images composées de l'animation, ou null si ce n'est pas un APNG exploitable.</summary>
    public static IReadOnlyList<AnimationFrame>? Decode(byte[] data)
    {
        byte[]? header = null;
        var sharedChunks = new List<Chunk>();
        var frames = new List<FrameInfo>();
        FrameInfo? current = null;
        var animated = false;

        foreach (var chunk in Chunks(data))
        {
            switch (chunk.Type)
            {
                case "IHDR":
                    header = chunk.Data;
                    break;
                case "acTL":
                    animated = true;
                    break;
                case "fcTL" when chunk.Data.Length >= 26:
                    current = FrameInfo.Parse(chunk.Data);
                    frames.Add(current);
                    break;
                case "IDAT":
                    current?.Data.Add(chunk.Data); // Sans fcTL avant, l'image par défaut ne fait pas partie de l'animation.
                    break;
                case "fdAT" when chunk.Data.Length > 4:
                    current?.Data.Add(chunk.Data[4..]); // Les 4 premiers octets sont un numéro de séquence.
                    break;
                case "IEND":
                    break;
                default:
                    if (frames.Count == 0)
                    {
                        sharedChunks.Add(chunk); // PLTE, tRNS, gAMA, sRGB, iCCP... communs à toutes les images.
                    }

                    break;
            }
        }

        if (!animated || header is null || frames.Count < 2 || frames.Any(f => f.Data.Count == 0))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header);
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        if (width <= 0 || height <= 0 || (long)width * height * frames.Count > MaxTotalPixels)
        {
            return null;
        }

        var stride = width * 4;
        var canvas = new byte[stride * height]; // Pbgra32, transparent au départ.
        var result = new List<AnimationFrame>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0
                || frame.X + frame.Width > width || frame.Y + frame.Height > height)
            {
                return null;
            }

            var pixels = DecodeFrame(header, sharedChunks, frame);
            var dispose = i == 0 && frame.Dispose == DisposeOp.Previous ? DisposeOp.Background : frame.Dispose;
            var saved = dispose == DisposeOp.Previous ? (byte[])canvas.Clone() : null;

            Blend(canvas, stride, pixels, frame);
            var snapshot = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, canvas, stride);
            snapshot.Freeze();
            result.Add(new AnimationFrame(snapshot, frame.Delay));

            if (dispose == DisposeOp.Background)
            {
                Clear(canvas, stride, frame);
            }
            else if (saved is not null)
            {
                canvas = saved;
            }
        }

        return result;
    }

    private static byte[] DecodeFrame(byte[] header, List<Chunk> sharedChunks, FrameInfo frame)
    {
        // PNG autonome : IHDR aux dimensions de l'image, blocs communs, données de l'image, IEND.
        var frameHeader = (byte[])header.Clone();
        BinaryPrimitives.WriteInt32BigEndian(frameHeader, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(frameHeader.AsSpan(4), frame.Height);

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", frameHeader);
        foreach (var chunk in sharedChunks)
        {
            WriteChunk(png, chunk.Type, chunk.Data);
        }

        WriteChunk(png, "IDAT", frame.Data.SelectMany(d => d).ToArray());
        WriteChunk(png, "IEND", []);
        png.Position = 0;

        var decoded = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Pbgra32, null, 0);
        var pixels = new byte[frame.Width * frame.Height * 4];
        converted.CopyPixels(pixels, frame.Width * 4, 0);
        return pixels;
    }

    private static void Blend(byte[] canvas, int stride, byte[] pixels, FrameInfo frame)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            var target = (frame.Y + y) * stride + frame.X * 4;
            var source = y * frame.Width * 4;
            if (frame.Blend == BlendOp.Source)
            {
                Buffer.BlockCopy(pixels, source, canvas, target, frame.Width * 4);
                continue;
            }

            for (var x = 0; x < frame.Width * 4; x += 4)
            {
                // Pixels prémultipliés : résultat = source + destination × (1 − alpha source).
                var alpha = pixels[source + x + 3];
                var keep = 255 - alpha;
                for (var c = 0; c < 4; c++)
                {
                    canvas[target + x + c] = (byte)(pixels[source + x + c] + (canvas[target + x + c] * keep + 127) / 255);
                }
            }
        }
    }

    private static void Clear(byte[] canvas, int stride, FrameInfo frame)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            Array.Clear(canvas, (frame.Y + y) * stride + frame.X * 4, frame.Width * 4);
        }
    }

    private sealed record Chunk(string Type, byte[] Data);

    private static IEnumerable<Chunk> Chunks(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
        {
            yield break;
        }

        var offset = 8;
        while (offset + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
            if (length < 0 || offset + 12L + length > data.Length)
            {
                yield break;
            }

            var type = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
            yield return new Chunk(type, data.AsSpan(offset + 8, length).ToArray());
            offset += 12 + length;
        }
    }

    internal static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(typeBytes, data));
        stream.Write(buffer);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private enum DisposeOp : byte
    {
        None = 0,
        Background = 1,
        Previous = 2,
    }

    private enum BlendOp : byte
    {
        Source = 0,
        Over = 1,
    }

    private sealed class FrameInfo
    {
        public int Width { get; private init; }

        public int Height { get; private init; }

        public int X { get; private init; }

        public int Y { get; private init; }

        public TimeSpan Delay { get; private init; }

        public DisposeOp Dispose { get; private init; }

        public BlendOp Blend { get; private init; }

        public List<byte[]> Data { get; } = [];

        public static FrameInfo Parse(byte[] fcTL)
        {
            var span = fcTL.AsSpan();
            var numerator = BinaryPrimitives.ReadUInt16BigEndian(span[20..]);
            var denominator = BinaryPrimitives.ReadUInt16BigEndian(span[22..]);
            var milliseconds = numerator * 1000.0 / (denominator == 0 ? 100 : denominator);
            return new FrameInfo
            {
                Width = BinaryPrimitives.ReadInt32BigEndian(span[4..]),
                Height = BinaryPrimitives.ReadInt32BigEndian(span[8..]),
                X = BinaryPrimitives.ReadInt32BigEndian(span[12..]),
                Y = BinaryPrimitives.ReadInt32BigEndian(span[16..]),

                // Comme les navigateurs pour les GIF : un délai inférieur à 20 ms compte pour 100 ms.
                Delay = TimeSpan.FromMilliseconds(milliseconds < 20 ? 100 : milliseconds),
                Dispose = (DisposeOp)Math.Min(span[24], (byte)2),
                Blend = span[25] == 1 ? BlendOp.Over : BlendOp.Source,
            };
        }
    }
}
