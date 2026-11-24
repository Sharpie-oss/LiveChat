using System.Text.Json.Serialization;
using System.Windows.Input;

namespace LiveChat.Configuration;

/// <summary>Position et taille de la fenêtre d'incrustation.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height);

/// <summary>Paramètres persistés de l'application.</summary>
public sealed class AppSettings
{
    public const int MinImageDuration = 2;
    public const int MaxImageDuration = 60;
    public const int MinVideoDuration = 5;
    public const int MaxVideoDuration = 600;
    public const int MinQueueLength = 1;
    public const int MaxQueueLengthLimit = 100;

    /// <summary>Token du bot, en clair uniquement en mémoire (jamais sérialisé).</summary>
    [JsonIgnore]
    public string BotToken { get; set; } = "";

    /// <summary>Token chiffré avec DPAPI (lisible uniquement par l'utilisateur Windows courant).</summary>
    public string? ProtectedBotToken { get; set; }

    public ulong ChannelId { get; set; }

    /// <summary>Volume des vidéos, entre 0 et 1.</summary>
    public double Volume { get; set; } = 0.5;

    /// <summary>Durée d'affichage des images, GIF et messages texte.</summary>
    public int ImageDurationSeconds { get; set; } = 6;

    /// <summary>Au-delà de cette durée, une vidéo est coupée pour passer au média suivant.</summary>
    public int MaxVideoDurationSeconds { get; set; } = 60;

    /// <summary>Nombre maximum de médias en attente (protection anti-spam).</summary>
    public int MaxQueueLength { get; set; } = 20;

    /// <summary>Garde la fenêtre affichée même quand aucun média n'est en cours.</summary>
    public bool KeepWindowVisible { get; set; }

    /// <summary>Affiche le texte du message en légende du média.</summary>
    public bool ShowMessageText { get; set; } = true;

    /// <summary>Affiche aussi les messages qui ne contiennent que du texte.</summary>
    public bool ShowTextOnlyMessages { get; set; }

    /// <summary>Floute les médias marqués « spoiler ».</summary>
    public bool BlurSpoilers { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public Hotkey? StopHotkey { get; set; } = new(ModifierKeys.None, Key.Pause);

    public Hotkey? SkipHotkey { get; set; }

    public WindowPlacement? Window { get; set; }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && ChannelId != 0;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Ramène les valeurs dans des bornes raisonnables (fichier modifié à la main, etc.).</summary>
    public void Normalize()
    {
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0.5;
        ImageDurationSeconds = Math.Clamp(ImageDurationSeconds, MinImageDuration, MaxImageDuration);
        MaxVideoDurationSeconds = Math.Clamp(MaxVideoDurationSeconds, MinVideoDuration, MaxVideoDuration);
        MaxQueueLength = Math.Clamp(MaxQueueLength, MinQueueLength, MaxQueueLengthLimit);

        if (StopHotkey is { IsValidGlobalHotkey: false })
        {
            StopHotkey = null;
        }

        if (SkipHotkey is { IsValidGlobalHotkey: false } || SkipHotkey == StopHotkey)
        {
            SkipHotkey = null;
        }

        if (Window is { } w && !(w.Width > 0 && w.Height > 0 && double.IsFinite(w.Left) && double.IsFinite(w.Top)))
        {
            Window = null;
        }
    }
}
