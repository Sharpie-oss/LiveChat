using LiveChat.Services;

namespace LiveChat.Tests;

public class MediaTimingTests
{
    private static readonly TimeSpan ImageDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    /// <summary>GIF de 1×1 pixel dont chaque image a le délai indiqué (en centièmes de seconde).</summary>
    private static byte[] Gif(params ushort[] delays)
    {
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());
        bytes.AddRange([0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00]);
        foreach (var delay in delays)
        {
            bytes.AddRange([0x21, 0xF9, 0x04, 0x00, (byte)(delay & 0xFF), (byte)(delay >> 8), 0x00, 0x00]);
            bytes.AddRange([0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
            bytes.AddRange([0x02, 0x02, 0x44, 0x01, 0x00]);
        }

        bytes.Add(0x3B);
        return [.. bytes];
    }

    [Theory]
    [InlineData(2, 6)] // GIF court : il boucle pendant la durée configurée.
    [InlineData(10, 10)] // GIF long : affiché jusqu'au bout.
    [InlineData(90, 60)] // Mais jamais au-delà de la durée maximale des vidéos.
    [InlineData(0, 6)] // Image fixe.
    public void DisplayDuration_ShowsAtLeastOneFullLoop(int animationSeconds, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds),
            MediaTiming.DisplayDuration(TimeSpan.FromSeconds(animationSeconds), ImageDuration, MaxDuration));

    [Fact]
    public void ReadGifLength_SumsFrameDelays() =>
        Assert.Equal(TimeSpan.FromSeconds(2), MediaTiming.ReadGifLength(Gif(50, 150)));

    [Fact]
    public void ReadGifLength_TreatsTinyDelaysAsTenthOfSecond() =>
        // Comme les navigateurs : 0 ou 10 ms compte pour 100 ms.
        Assert.Equal(TimeSpan.FromMilliseconds(300), MediaTiming.ReadGifLength(Gif(0, 1, 10)));

    [Fact]
    public void ReadGifLength_SingleFrame_IsZero() =>
        Assert.Equal(TimeSpan.Zero, MediaTiming.ReadGifLength(Gif(500)));

    [Fact]
    public void ReadGifLength_NotAGif_IsZero() =>
        Assert.Equal(TimeSpan.Zero, MediaTiming.ReadGifLength("pas une image"u8.ToArray()));
}
