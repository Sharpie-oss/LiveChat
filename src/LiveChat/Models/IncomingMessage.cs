namespace LiveChat.Models;

/// <summary>Pièce jointe d'un message, indépendante de Discord.Net (facilite les tests).</summary>
public sealed record AttachmentInfo(string Url, string FileName, string? ContentType)
{
    public bool IsSpoiler => FileName.StartsWith("SPOILER_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Embed (aperçu de lien, GIF Tenor...) d'un message.</summary>
/// <param name="Type">Type Discord : gifv, image, video, rich, article, link...</param>
/// <param name="HasProvider">Vrai pour les sites comme YouTube dont la vidéo n'est pas un fichier lisible.</param>
public sealed record EmbedInfo(
    string Type,
    string? Url,
    string? ImageUrl,
    string? ThumbnailUrl,
    string? VideoUrl,
    bool HasProvider);

/// <summary>Message reçu dans le salon surveillé (ou via la commande /caption).</summary>
public sealed record IncomingMessage(
    ulong Id,
    string Author,
    string? AvatarUrl,
    string Content,
    IReadOnlyList<AttachmentInfo> Attachments,
    IReadOnlyList<EmbedInfo> Embeds)
{
    /// <summary>Le contenu est une légende explicite (commande /caption) : toujours affichée.</summary>
    public bool IsExplicitCaption { get; init; }

    /// <summary>
    /// Embeds ajoutés après coup par Discord (aperçus de liens) : on ignore alors les pièces jointes,
    /// déjà traitées à la réception du message.
    /// </summary>
    public bool EmbedsOnly { get; init; }
}
