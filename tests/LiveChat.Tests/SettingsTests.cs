using System.Windows.Input;
using LiveChat.Configuration;

namespace LiveChat.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LiveChat.Tests", Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private string LegacyPath => Path.Combine(_directory, "config.json");

    [Fact]
    public void Load_WithoutFile_ReturnsDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.False(settings.IsConfigured);
        Assert.Equal(0.5, settings.Volume);
        Assert.Equal(new Hotkey(ModifierKeys.None, Key.Pause), settings.StopHotkey);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAndEncryptsToken()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings
        {
            BotToken = "MTIzNDU2Nzg5.secret-token",
            ChannelId = 123456789012345678,
            Volume = 0.8,
            SkipHotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.N),
            Window = new WindowPlacement(10, 20, 400, 300),
        };

        store.Save(settings);
        var json = File.ReadAllText(SettingsPath);
        var loaded = store.Load();

        Assert.DoesNotContain("secret-token", json);
        Assert.Contains("\"Alt, Control\"", json); // Énumérations lisibles dans le fichier.
        Assert.Equal("MTIzNDU2Nzg5.secret-token", loaded.BotToken);
        Assert.Equal(123456789012345678UL, loaded.ChannelId);
        Assert.Equal(0.8, loaded.Volume);
        Assert.Equal(settings.SkipHotkey, loaded.SkipHotkey);
        Assert.Equal(settings.Window, loaded.Window);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Load_MigratesLegacyPlainTextConfig()
    {
        File.WriteAllText(LegacyPath, """{ "BotToken": "legacy-token", "ChannelId": 987654321098765432 }""");

        var settings = new SettingsStore(SettingsPath, LegacyPath).Load();

        Assert.Equal("legacy-token", settings.BotToken);
        Assert.Equal(987654321098765432UL, settings.ChannelId);
        Assert.False(File.Exists(LegacyPath)); // Le token en clair est supprimé.
        Assert.DoesNotContain("legacy-token", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Load_CorruptedFile_FallsBackToDefaultsAndKeepsBackup()
    {
        File.WriteAllText(SettingsPath, "{ pas du json");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.False(settings.IsConfigured);
        Assert.True(File.Exists(SettingsPath + ".corrompu"));
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var settings = new AppSettings
        {
            Volume = 4,
            ImageDurationSeconds = 0,
            MaxVideoDurationSeconds = 100_000,
            MaxQueueLength = -3,
            StopHotkey = new Hotkey(ModifierKeys.None, Key.A),
            SkipHotkey = new Hotkey(ModifierKeys.None, Key.Pause),
            Window = new WindowPlacement(double.NaN, 0, 300, 200),
        };

        settings.Normalize();

        Assert.Equal(1, settings.Volume);
        Assert.Equal(AppSettings.MinImageDuration, settings.ImageDurationSeconds);
        Assert.Equal(AppSettings.MaxVideoDuration, settings.MaxVideoDurationSeconds);
        Assert.Equal(AppSettings.MinQueueLength, settings.MaxQueueLength);
        Assert.Null(settings.StopHotkey); // Une lettre seule volerait la frappe au clavier.
        Assert.NotNull(settings.SkipHotkey);
        Assert.Null(settings.Window);
    }

    [Theory]
    [InlineData("123456789012345678", 123456789012345678UL)]
    [InlineData("  123456789012345678 ", 123456789012345678UL)]
    [InlineData("https://discord.com/channels/111111111111111111/222222222222222222", 222222222222222222UL)]
    [InlineData("https://ptb.discord.com/channels/111111111111111111/333333333333333333/444444444444444444", 333333333333333333UL)]
    public void TryParseChannelId_AcceptsIdsAndLinks(string input, ulong expected)
    {
        Assert.True(SettingsValidation.TryParseChannelId(input, out var channelId));
        Assert.Equal(expected, channelId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("général")]
    [InlineData("12345")]
    [InlineData("123456789012345678901234")]
    [InlineData("-123456789012345678")]
    public void TryParseChannelId_RejectsInvalidInput(string input) =>
        Assert.False(SettingsValidation.TryParseChannelId(input, out _));

    [Theory]
    [InlineData("  abc.def.ghi  ", "abc.def.ghi")]
    [InlineData("\"abc.def.ghi\"", "abc.def.ghi")]
    [InlineData("Bot abc.def.ghi", "abc.def.ghi")]
    public void NormalizeToken_StripsNoise(string input, string expected) =>
        Assert.Equal(expected, SettingsValidation.NormalizeToken(input));

    [Theory]
    [InlineData(ModifierKeys.None, Key.Pause, true)]
    [InlineData(ModifierKeys.None, Key.F9, true)]
    [InlineData(ModifierKeys.None, Key.S, false)]
    [InlineData(ModifierKeys.Shift, Key.S, false)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.S, true)]
    [InlineData(ModifierKeys.Alt, Key.LeftCtrl, false)]
    public void Hotkey_IsValidGlobalHotkey(ModifierKeys modifiers, Key key, bool expected) =>
        Assert.Equal(expected, new Hotkey(modifiers, key).IsValidGlobalHotkey);

    [Fact]
    public void Hotkey_ToString_IsReadable()
    {
        Assert.Equal("Ctrl + Alt + Maj + S", new Hotkey(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.S).ToString());
        Assert.Equal("Pause", new Hotkey(ModifierKeys.None, Key.Pause).ToString());
        Assert.Equal("Ctrl + 5", new Hotkey(ModifierKeys.Control, Key.D5).ToString());
    }
}
