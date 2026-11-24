using LiveChat.Configuration;
using LiveChat.Services;

namespace LiveChat.Tests;

public class VideoCacheTests
{
    [Theory]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/clip.mp4?ex=6b075190&is=6b060010&hm=abc&", ".mp4")]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/CLIP.MOV?ex=1", ".mov")]
    [InlineData("https://media.tenor.com/abc/AAPo/funny.webm", ".webm")]
    [InlineData("https://example.com/stream?id=42", ".mp4")]
    [InlineData("https://example.com/page.html", ".mp4")]
    public void GetExtension_IgnoresQueryString(string url, string expected) =>
        Assert.Equal(expected, VideoCache.GetExtension(url));

    [Fact]
    public void CreateTempPath_IsTemporaryAndUnique()
    {
        var first = VideoCache.CreateTempPath("https://cdn/a.mp4?x=1");
        var second = VideoCache.CreateTempPath("https://cdn/a.mp4?x=1");

        Assert.NotEqual(first, second);
        Assert.EndsWith(".mp4", first);
        Assert.True(VideoCache.IsTemporary(first));
        Assert.False(VideoCache.IsTemporary(@"C:\Users\Quelqu'un\Vidéos\clip.mp4")); // Jamais un fichier de l'utilisateur.
    }

    [Fact]
    public async Task DownloadToFileAsync_RejectsNonHttpSchemes()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            HttpClients.DownloadToFileAsync("ftp://example.com/a.mp4", path, 1024, null, CancellationToken.None));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void IsBotTokenFormat_AcceptsWellFormedBotToken()
    {
        // Faux token construit à l'exécution : un littéral de ce format serait (à juste titre)
        // bloqué par la détection de secrets de GitHub.
        var botId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("123456789012345678")).TrimEnd('=');
        var fakeToken = string.Join('.', botId, "GAbCdE", new string('x', 38));

        Assert.True(SettingsValidation.IsBotTokenFormat(fakeToken));
    }

    [Theory]
    [InlineData("aBcDeFgHiJkLmNoPqRsTuVwXyZ012345")] // Client Secret OAuth2 (32 caractères).
    [InlineData("trop-court")]
    public void IsBotTokenFormat_RejectsClientSecretConfusion(string token) =>
        Assert.False(SettingsValidation.IsBotTokenFormat(token));
}
