using System.Buffers.Binary;
using System.Text;

namespace LiveChat.Services;

/// <summary>
/// Convertit un fichier Ogg Opus (format des messages vocaux Discord) en Matroska, sans réencoder le son.
/// Le lecteur de WPF refuse le conteneur Ogg, mais lit l'Opus dans un fichier Matroska (.mkv), comme pour le WebM.
/// </summary>
public static class OggOpus
{
    private const int SampleRate = 48000; // Les horodatages Opus sont toujours en 48 kHz.
    private const int MaxClusterMilliseconds = 30000; // Horodatage relatif d'un bloc : entier 16 bits signé.

    /// <summary>Vrai pour une extension de fichier Ogg (.ogg, .oga, .opus).</summary>
    public static bool IsOggExtension(string extension) =>
        extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".oga", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".opus", StringComparison.OrdinalIgnoreCase);

    /// <summary>Écrit <paramref name="destination"/> (Matroska) ; faux si la source n'est pas un Ogg Opus lisible.</summary>
    public static bool TryRemux(string source, string destination)
    {
        try
        {
            var matroska = Remux(File.ReadAllBytes(source));
            if (matroska is null)
            {
                return false;
            }

            File.WriteAllBytes(destination, matroska);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Log.Warn("Conversion Ogg Opus impossible.", ex);
            return false;
        }
    }

    /// <summary>Fichier Matroska équivalent, ou null si ce n'est pas un Ogg Opus (Vorbis, fichier abîmé...).</summary>
    internal static byte[]? Remux(byte[] ogg)
    {
        var packets = ReadPackets(ogg);
        if (packets.Count < 3 || packets[0].Length < 19 || !packets[0].AsSpan(0, 8).SequenceEqual("OpusHead"u8))
        {
            return null;
        }

        var head = packets[0];
        var channels = head[9];
        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10));

        // Paquets audio (après OpusHead et OpusTags), regroupés en clusters de 30 s au plus.
        var clusters = new List<byte[]>();
        var blocks = new List<byte[]>();
        long clusterStart = 0;
        long samples = 0;
        foreach (var packet in packets.Skip(2))
        {
            var milliseconds = samples * 1000 / SampleRate;
            if (milliseconds - clusterStart > MaxClusterMilliseconds)
            {
                clusters.Add(Cluster(clusterStart, blocks));
                blocks.Clear();
                clusterStart = milliseconds;
            }

            var block = new byte[packet.Length + 4];
            block[0] = 0x81; // Piste 1.
            BinaryPrimitives.WriteInt16BigEndian(block.AsSpan(1), (short)(milliseconds - clusterStart));
            block[3] = 0x80; // Image clé : chaque paquet Opus se décode seul.
            packet.CopyTo(block, 4);
            blocks.Add(Element(0xA3, block)); // SimpleBlock
            samples += PacketSamples(packet);
        }

        if (blocks.Count == 0)
        {
            return null;
        }

        clusters.Add(Cluster(clusterStart, blocks));

        byte[] header = Element(0x1A45DFA3, // EBML
            Element(0x4286, UInt(1)), Element(0x42F7, UInt(1)), Element(0x42F2, UInt(4)), Element(0x42F3, UInt(8)),
            Element(0x4282, "matroska"u8.ToArray()), Element(0x4287, UInt(4)), Element(0x4285, UInt(2)));

        byte[] info = Element(0x1549A966, // Info
            Element(0x2AD7B1, UInt(1_000_000)), // Horodatages en millisecondes.
            Element(0x4489, Float(samples * 1000.0 / SampleRate)), // Durée.
            Element(0x4D80, "LiveChat"u8.ToArray()),
            Element(0x5741, "LiveChat"u8.ToArray()));

        byte[] tracks = Element(0x1654AE6B, // Tracks
            Element(0xAE, // TrackEntry
                Element(0xD7, UInt(1)), Element(0x73C5, UInt(1)), Element(0x83, UInt(2)), Element(0x9C, UInt(0)),
                Element(0x86, "A_OPUS"u8.ToArray()),
                Element(0x63A2, head),
                Element(0x56AA, UInt((ulong)(preSkip * 1_000_000_000L / SampleRate))), // CodecDelay (ns)
                Element(0x56BB, UInt(80_000_000)), // SeekPreRoll : 80 ms recommandés pour Opus.
                Element(0xE1, Element(0xB5, Float(SampleRate)), Element(0x9F, UInt(channels)))));

        return [.. header, .. Element(0x18538067, [info, tracks, .. clusters])];
    }

    /// <summary>Paquets du premier flux logique, reconstitués à partir des segments des pages Ogg.</summary>
    internal static List<byte[]> ReadPackets(byte[] data)
    {
        var packets = new List<byte[]>();
        using var current = new MemoryStream();
        uint? serial = null;
        var offset = 0;
        while (offset + 27 <= data.Length && data.AsSpan(offset, 4).SequenceEqual("OggS"u8))
        {
            var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 14));
            var segments = data[offset + 26];
            var body = offset + 27 + segments;
            if (body > data.Length)
            {
                break;
            }

            var position = body;
            serial ??= pageSerial;
            for (var i = 0; i < segments; i++)
            {
                var lace = data[offset + 27 + i];
                if (position + lace > data.Length)
                {
                    return packets; // Page tronquée : on garde ce qui précède.
                }

                if (pageSerial == serial)
                {
                    current.Write(data, position, lace);
                    if (lace < 255)
                    {
                        packets.Add(current.ToArray());
                        current.SetLength(0);
                    }
                }

                position += lace;
            }

            offset = position;
        }

        return packets;
    }

    /// <summary>Durée d'un paquet Opus en échantillons à 48 kHz, d'après son octet TOC (RFC 6716, § 3.1).</summary>
    internal static int PacketSamples(byte[] packet)
    {
        if (packet.Length == 0)
        {
            return 0;
        }

        var config = packet[0] >> 3;
        var frameSamples = config switch
        {
            < 12 => new[] { 480, 960, 1920, 2880 }[config & 3], // SILK : 10, 20, 40, 60 ms.
            < 16 => (config & 1) == 0 ? 480 : 960, // Hybride : 10, 20 ms.
            _ => new[] { 120, 240, 480, 960 }[config & 3], // CELT : 2,5, 5, 10, 20 ms.
        };
        var frames = (packet[0] & 3) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length > 1 ? packet[1] & 0x3F : 0,
        };
        return frameSamples * frames;
    }

    private static byte[] Cluster(long timestamp, List<byte[]> blocks) =>
        Element(0x1F43B675, [Element(0xE7, UInt((ulong)timestamp)), .. blocks]);

    private static byte[] Element(uint id, params byte[][] children)
    {
        var length = children.Sum(c => (long)c.Length);
        using var stream = new MemoryStream();
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, id);
        var idLength = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        stream.Write(buffer[(4 - idLength)..4]);

        // Taille sur 8 octets (marqueur 0x01) : simple et valable pour toutes les tailles.
        BinaryPrimitives.WriteUInt64BigEndian(buffer, (ulong)length);
        buffer[0] = 0x01;
        stream.Write(buffer);
        foreach (var child in children)
        {
            stream.Write(child);
        }

        return stream.ToArray();
    }

    private static byte[] UInt(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        var skip = 0;
        while (skip < 7 && bytes[skip] == 0)
        {
            skip++;
        }

        return bytes[skip..];
    }

    private static byte[] Float(double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        return bytes;
    }
}
