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
            Assert.False(File.Exists(Path.Combine(target, UpdateService.BackupManifestName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FormatReleaseNotes_RendersChangelogMarkdownAsPlainText()
    {
        const string markdown = "### Nouveautés\r\n\r\n- **Stickers** : affichés (`PNG`, GIF).\r\n- Voir [la release](https://github.com/o/r).\r\n\r\n\r\n\r\n### Corrections de bugs\r\n\r\n* **WebM** : lu.";

        Assert.Equal(
            "NOUVEAUTÉS\n\n• Stickers : affichés (PNG, GIF).\n• Voir la release.\n\nCORRECTIONS DE BUGS\n\n• WebM : lu.",
            UpdateService.FormatReleaseNotes(markdown));
    }

    [Fact]
    public void PreloadAssemblies_LoadsLazyDependenciesQuickly()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        UpdateService.PreloadAssemblies();

        // Utilisée seulement pour les GIF : c'est elle qui manquait à la fermeture pendant la mise à jour.
        Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "WpfAnimatedGif");
        // Garde-fou contre un blocage seulement : 0,5 s en local, mais jusqu'à 10 s sur les machines partagées
        // de la CI GitHub (un seuil de 5 s y faisait échouer le test de façon aléatoire).
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Préchargement anormalement long : {stopwatch.Elapsed}.");
    }

    [Fact]
    public void CleanupPreviousUpdate_KeepsUnrelatedOldFiles()
    {
        // LiveChat.exe lancé depuis Téléchargements : les autres fichiers .old ne lui appartiennent pas.
        var downloads = Path.Combine(Path.GetTempPath(), "LiveChat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(downloads, "Photos"));
        try
        {
            File.WriteAllText(Path.Combine(downloads, "LiveChat.exe"), "v2");
            File.WriteAllText(Path.Combine(downloads, "LiveChat.exe.old"), "v1"); // Laissé par la mise à jour 2.0.1.
            File.WriteAllText(Path.Combine(downloads, "rapport.docx.old"), "à garder");
            File.WriteAllText(Path.Combine(downloads, "Photos", "vacances.jpg.old"), "à garder");

            UpdateService.CleanupPreviousUpdate(downloads);

            Assert.False(File.Exists(Path.Combine(downloads, "LiveChat.exe.old")));
            Assert.True(File.Exists(Path.Combine(downloads, "rapport.docx.old")));
            Assert.True(File.Exists(Path.Combine(downloads, "Photos", "vacances.jpg.old")));
        }
        finally
        {
            Directory.Delete(downloads, recursive: true);
        }
    }

    [Fact]
    public void CleanupPreviousUpdate_IgnoresManifestEntriesOutsideTheAppFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "LiveChat.Tests", Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        try
        {
            File.WriteAllText(Path.Combine(root, "ailleurs.old"), "à garder");
            File.WriteAllText(Path.Combine(app, "lib.dll.old"), "sauvegarde");
            File.WriteAllLines(Path.Combine(app, UpdateService.BackupManifestName), [@"..\ailleurs.old", "lib.dll.old", "settings.json"]);

            UpdateService.CleanupPreviousUpdate(app);

            Assert.True(File.Exists(Path.Combine(root, "ailleurs.old")));
            Assert.False(File.Exists(Path.Combine(app, "lib.dll.old")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
