namespace LiveChat.Tests;

/// <summary>Le journal est statique : ces tests ne tournent pas en parallèle avec les autres.</summary>
[CollectionDefinition("Log", DisableParallelization = true)]
public sealed class LogCollection;

[Collection("Log")]
public sealed class LogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LiveChat.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Log.Clock = () => DateTime.Now;
        Log.Initialize(null);
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Write_RotatesToANewFileAtMidnight()
    {
        var now = new DateTime(2026, 11, 25, 23, 59, 58);
        Log.Clock = () => now;
        Log.Initialize(_directory);

        Log.Info("avant minuit");
        now = now.AddSeconds(5);
        Log.Info("après minuit");

        Assert.Contains("avant minuit", File.ReadAllText(Path.Combine(_directory, "livechat-20261125.log")));
        var next = File.ReadAllText(Path.Combine(_directory, "livechat-20261126.log"));
        Assert.Contains("après minuit", next);
        Assert.DoesNotContain("avant minuit", next);
    }

    [Fact]
    public void Rotation_PurgesFilesOlderThanAWeek()
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "livechat-20261110.log");
        var recent = Path.Combine(_directory, "livechat-20261122.log");
        File.WriteAllText(old, "ancien");
        File.WriteAllText(recent, "récent");
        File.SetLastWriteTime(old, new DateTime(2026, 11, 10, 12, 0, 0));
        File.SetLastWriteTime(recent, new DateTime(2026, 11, 22, 12, 0, 0));

        var now = new DateTime(2026, 11, 12, 12, 0, 0);
        Log.Clock = () => now;
        Log.Initialize(_directory);
        Assert.True(File.Exists(old)); // Deux jours seulement au lancement.

        now = new DateTime(2026, 11, 25, 9, 0, 0); // LiveChat est resté ouvert près de deux semaines.
        Log.Info("nouvelle journée");

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
