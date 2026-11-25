using System.Buffers.Binary;
using LiveChat.Services;

namespace LiveChat.Tests;

public class OggOpusTests
{
    private static byte[] OpusHead(byte channels = 2, ushort preSkip = 312)
    {
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = channels;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), preSkip);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), 48000);
        return head;
    }

    /// <summary>Page Ogg (sans CRC : LiveChat ne la vérifie pas) contenant des segments déjà découpés.</summary>
    private static byte[] Page(uint serial, params byte[][] segments)
    {
        var page = new List<byte>("OggS"u8.ToArray()) { 0, 0 };
        page.AddRange(new byte[8]); // Position (granule).
        page.AddRange(BitConverter.GetBytes(serial));
        page.AddRange(new byte[8]); // Numéro de page et CRC.
        page.Add((byte)segments.Length);
        page.AddRange(segments.Select(s => (byte)s.Length));
        foreach (var segment in segments)
        {
            page.AddRange(segment);
        }

        return [.. page];
    }

    /// <summary>Paquet Opus fictif : configuration 31 (CELT, 20 ms), une seule image.</summary>
    private static byte[] Packet(int length) => [0xF8, .. Enumerable.Repeat((byte)0x55, length - 1)];

    private static int Count(byte[] data, byte[] pattern)
    {
        var count = 0;
        for (var i = data.AsSpan().IndexOf(pattern); i >= 0;)
        {
            count++;
            var next = data.AsSpan(i + 1).IndexOf(pattern);
            i = next < 0 ? -1 : i + 1 + next;
        }

        return count;
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, 480)] // SILK 10 ms, une image.
    [InlineData(new byte[] { 0x08 }, 960)] // SILK 20 ms, une image.
    [InlineData(new byte[] { 0xF8 }, 960)] // CELT 20 ms, une image.
    [InlineData(new byte[] { 0xF9 }, 1920)] // CELT 20 ms, deux images.
    [InlineData(new byte[] { 0xEB, 0x03 }, 720)] // CELT 5 ms (config 29), trois images (nombre dans le 2e octet).
    public void PacketSamples_ReadsTheTocByte(byte[] packet, int expected) =>
        Assert.Equal(expected, OggOpus.PacketSamples(packet));

    [Fact]
    public void ReadPackets_JoinsPacketsSpanningSeveralPages()
    {
        var big = Packet(300); // 255 + 45 : deux segments, à cheval sur deux pages.
        byte[] ogg = [.. Page(7, OpusHead()), .. Page(7, "OpusTags"u8.ToArray(), big[..255]), .. Page(7, big[255..], Packet(10))];

        var packets = OggOpus.ReadPackets(ogg);

        Assert.Equal(4, packets.Count);
        Assert.Equal(big, packets[2]);
        Assert.Equal(10, packets[3].Length);
    }

    [Fact]
    public void Remux_ProducesMatroskaWithOneBlockPerPacket()
    {
        byte[] ogg = [.. Page(7, OpusHead()), .. Page(7, "OpusTags"u8.ToArray()), .. Page(7, Packet(20), Packet(20), Packet(20))];

        var matroska = OggOpus.Remux(ogg);

        Assert.NotNull(matroska);
        Assert.Equal([0x1A, 0x45, 0xDF, 0xA3], matroska[..4]); // En-tête EBML.
        Assert.Equal(1, Count(matroska, "A_OPUS"u8.ToArray()));
        Assert.Equal(1, Count(matroska, "OpusHead"u8.ToArray())); // CodecPrivate.
        // Trois SimpleBlock : identifiant A3, taille 24 sur 8 octets, piste 1 (0x81).
        Assert.Equal(3, Count(matroska, [0xA3, 0x01, 0, 0, 0, 0, 0, 0, 0x18, 0x81]));
    }

    [Fact]
    public void Remux_RejectsVorbisAndGarbage()
    {
        byte[] vorbis = [.. Page(7, [0x01, .. "vorbis"u8.ToArray(), .. new byte[23]]), .. Page(7, Packet(10)), .. Page(7, Packet(10))];

        Assert.Null(OggOpus.Remux(vorbis));
        Assert.Null(OggOpus.Remux("pas un fichier ogg"u8.ToArray()));
    }
}
