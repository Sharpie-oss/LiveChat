using LiveChat.Models;
using LiveChat.Services;

namespace LiveChat.Tests;

public class TestCardTests
{
    [Theory]
    [InlineData("SPOILER_photo.png", true)]
    [InlineData("photo.png", false)]
    public void FromPathOrUrl_FollowsDiscordSpoilerConvention(string fileName, bool spoiler)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LiveChat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

            var item = TestCard.FromPathOrUrl(path);

            Assert.NotNull(item);
            Assert.Equal(MediaKind.Image, item.Kind);
            Assert.Equal(spoiler, item.IsSpoiler);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FromPathOrUrl_RemoteUrl_IgnoresQueryString()
    {
        var item = TestCard.FromPathOrUrl("https://cdn.discordapp.com/attachments/1/2/clip.webm?ex=1&hm=2");

        Assert.NotNull(item);
        Assert.Equal(MediaKind.Video, item.Kind);
        Assert.Equal("clip.webm", item.Caption);
    }
}
