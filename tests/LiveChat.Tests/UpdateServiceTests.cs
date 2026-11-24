using System.Text.Json;
using LiveChat.Services;

namespace LiveChat.Tests;

public class UpdateServiceTests
{
    private static readonly Version Current = new(2, 0, 0);

    private static JsonElement Release(string tag, bool draft = false, bool prerelease = false, bool withChecksum = true, string assetName = UpdateService.AssetName)
    {
        var assets = new List<object>
        {
            new { name = assetName, browser_download_url = $"https://github.com/o/r/releases/download/{tag}/{assetName}" },
        };
        if (withChecksum)
        {
            assets.Add(new { name = assetName + ".sha256", browser_download_url = $"https://github.com/o/r/releases/download/{tag}/{assetName}.sha256" });
        }

        var json = JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            body = "Notes",
            html_url = $"https://github.com/o/r/releases/tag/{tag}",
            assets,
        });
        return JsonDocument.Parse(json).RootElement;
    }

    [Theory]
    [InlineData("v2.1.0", 2, 1, 0)]
    [InlineData("2.1", 2, 1, 0)]
    [InlineData("V3", 3, 0, 0)]
    [InlineData("v2.1.0-beta.1", 2, 1, 0)]
    [InlineData("2.0.7+build", 2, 0, 7)]
    public void TryParseVersion_AcceptsCommonTagFormats(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void TryParseVersion_RejectsGarbage(string? tag) =>
        Assert.False(UpdateService.TryParseVersion(tag, out _));

    [Fact]
    public void ParseRelease_NewerVersion_ReturnsAssetAndChecksum()
    {
        var update = UpdateService.ParseRelease(Release("v2.1.0"), Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 1, 0), update.Version);
        Assert.EndsWith("/" + UpdateService.AssetName, update.AssetUrl.AbsoluteUri);
        Assert.NotNull(update.ChecksumUrl);
        Assert.Equal("Notes", update.ReleaseNotes);
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("v1.9.9")]
    public void ParseRelease_SameOrOlderVersion_ReturnsNull(string tag) =>
        Assert.Null(UpdateService.ParseRelease(Release(tag), Current));

    [Fact]
    public void ParseRelease_DraftOrPrerelease_ReturnsNull()
    {
        Assert.Null(UpdateService.ParseRelease(Release("v3.0.0", draft: true), Current));
        Assert.Null(UpdateService.ParseRelease(Release("v3.0.0", prerelease: true), Current));
    }

    [Fact]
    public void ParseRelease_WithoutBuildAsset_ReturnsNull() =>
        // La v1 téléchargeait l'archive du code source (« zipball ») : on exige désormais l'archive compilée.
        Assert.Null(UpdateService.ParseRelease(Release("v3.0.0", assetName: "Source.zip"), Current));

    [Fact]
    public void ParseRelease_WithoutChecksum_StillReturnsUpdate()
    {
        var update = UpdateService.ParseRelease(Release("v3.0.0", withChecksum: false), Current);

        Assert.NotNull(update);
        Assert.Null(update.ChecksumUrl);
    }

    [Theory]
    [InlineData("A3F1C2D4E5B6A7980112233445566778899AABBCCDDEEFF00112233445566778  LiveChat-win-x64.zip\n")]
    [InlineData("a3f1c2d4e5b6a7980112233445566778899aabbccddeeff00112233445566778")]
    public void ParseChecksum_ReadsSha256SumFormat(string content) =>
        Assert.Equal("a3f1c2d4e5b6a7980112233445566778899aabbccddeeff00112233445566778", UpdateService.ParseChecksum(content));

    [Theory]
    [InlineData("")]
    [InlineData("pas-un-hash  fichier.zip")]
    public void ParseChecksum_RejectsInvalidContent(string content) =>
        Assert.Null(UpdateService.ParseChecksum(content));

    [Fact]
    public void ReplaceFiles_ReplacesAndKeepsBackups_ThenCleanupRemovesThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "LiveChat.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "new");
        var target = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(source, "LiveChat.exe"), "v2");
            File.WriteAllText(Path.Combine(source, "sub", "data.txt"), "nouveau");
            File.WriteAllText(Path.Combine(target, "LiveChat.exe"), "v1");
            File.WriteAllText(Path.Combine(target, "keep.txt"), "inchangé");

            UpdateService.ReplaceFiles(source, target);

            Assert.Equal("v2", File.ReadAllText(Path.Combine(target, "LiveChat.exe")));
            Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "LiveChat.exe.old")));
            Assert.Equal("nouveau", File.ReadAllText(Path.Combine(target, "sub", "data.txt")));
            Assert.Equal("inchangé", File.ReadAllText(Path.Combine(target, "keep.txt")));

            UpdateService.CleanupPreviousUpdate(target);

            Assert.False(File.Exists(Path.Combine(target, "LiveChat.exe.old")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
