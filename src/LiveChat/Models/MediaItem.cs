namespace LiveChat.Models;

public enum MediaKind
{
    /// <summary>Image fixe (PNG, JPEG, WebP...).</summary>
    Image,

    /// <summary>GIF animé.</summary>
    AnimatedImage,

    /// <summary>Vidéo lue jusqu'au bout (dans la limite de durée).</summary>
    Video,

    /// <summary>Vidéo courte sans son jouée en boucle (GIF Tenor/Giphy).</summary>
    LoopingVideo,

    /// <summary>Message texte sans média.</summary>
    Text,

    /// <summary>Pièce jointe non affichable (on affiche seulement son nom).</summary>
    File,
}

/// <summary>Élément prêt à être affiché dans la fenêtre d'incrustation.</summary>
public sealed record MediaItem(MediaKind Kind, string Author)
{
    public string? Url { get; init; }

    public string? AvatarUrl { get; init; }

    /// <summary>Légende affichée par-dessus le média.</summary>
    public string? Caption { get; init; }

    /// <summary>Contenu d'un message texte (<see cref="MediaKind.Text"/>).</summary>
    public string? Text { get; init; }

    public string? FileName { get; init; }

    /// <summary>Le média doit être flouté.</summary>
    public bool IsSpoiler { get; init; }
}
