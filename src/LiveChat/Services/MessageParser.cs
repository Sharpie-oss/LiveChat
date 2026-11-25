using System.Text.RegularExpressions;
using LiveChat.Models;

namespace LiveChat.Services;

/// <param name="ShowMessageText">Utiliser le texte du message comme légende du premier média.</param>
/// <param name="ShowTextOnlyMessages">Afficher aussi les messages sans média.</param>
/// <param name="BlurSpoilers">Flouter les médias spoiler et masquer le texte entre ||barres||.</param>
public sealed record ParserOptions(bool ShowMessageText, bool ShowTextOnlyMessages, bool BlurSpoilers);

/// <summary>Transforme un message Discord en éléments affichables.</summary>
public static partial class MessageParser
{
    public const int MaxCaptionLength = 200;
    public const int MaxTextLength = 500;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".webp", ".tif", ".tiff",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv", ".flv",
    };

    public static IReadOnlyList<MediaItem> Parse(IncomingMessage message, ParserOptions options)
    {
        var items = new List<MediaItem>();
        var linksAreSpoilers = options.BlurSpoilers && SpoilerRegex().Matches(message.Content)
            .Any(m => m.Value.Contains("http", StringComparison.OrdinalIgnoreCase));

        if (!message.EmbedsOnly)
        {
            foreach (var attachment in message.Attachments)
            {
                items.Add(new MediaItem(Classify(attachment.ContentType, attachment.FileName), message.Author)
                {
                    Url = attachment.Url,
                    FileName = attachment.FileName,
                    AvatarUrl = message.AvatarUrl,
                    IsSpoiler = options.BlurSpoilers && attachment.IsSpoiler,
                });
            }

            foreach (var sticker in message.Stickers)
            {
                items.Add(new MediaItem(sticker.IsAnimated ? MediaKind.AnimatedImage : MediaKind.Image, message.Author)
                {
                    Url = sticker.Url,
                    FileName = sticker.Name,
                    AvatarUrl = message.AvatarUrl,
                });
            }
        }

        foreach (var embed in message.Embeds)
        {
            if (FromEmbed(embed, message, linksAreSpoilers) is { } item)
            {
                items.Add(item);
            }
        }

        // Une même URL peut apparaître à la fois en pièce jointe et en embed.
        items = items.DistinctBy(i => i.Url).ToList();

        if (items.Count > 0)
        {
            var caption = BuildCaption(message, options);
            if (caption is not null)
            {
                items[0] = items[0] with { Caption = caption };
            }

            return items;
        }

        if (!message.EmbedsOnly && options.ShowTextOnlyMessages)
        {
            var text = Clean(message.Content, options.BlurSpoilers, MaxTextLength);
            if (text is not null)
            {
                return [new MediaItem(MediaKind.Text, message.Author) { Text = text, AvatarUrl = message.AvatarUrl }];
            }
        }

        return [];
    }

    /// <summary>
    /// Résumé d'un message pour le journal (sans son texte) : « Message de Alice : 1 pièce jointe (photo.png),
    /// 1 aperçu (video, ignoré) → 1 média ». Null pour un message sans média ni aperçu.
    /// </summary>
    public static string? Describe(IncomingMessage message, IReadOnlyList<MediaItem> items)
    {
        var parts = new List<string>();
        if (!message.EmbedsOnly && message.Attachments.Count > 0)
        {
            parts.Add($"{Plural(message.Attachments.Count, "pièce jointe", "pièces jointes")} ({string.Join(", ", message.Attachments.Select(a => a.FileName))})");
        }

        if (message.Embeds.Count > 0)
        {
            parts.Add($"{Plural(message.Embeds.Count, "aperçu", "aperçus")} ({string.Join(", ", message.Embeds.Select(e => e.Type.ToLowerInvariant()))})");
        }

        if (!message.EmbedsOnly && message.Stickers.Count > 0)
        {
            parts.Add(Plural(message.Stickers.Count, "sticker", "stickers"));
        }

        if (parts.Count == 0 && items.Count == 0)
        {
            return null;
        }

        var origin = message.EmbedsOnly ? " (aperçu ajouté après coup)" : "";
        var content = parts.Count > 0 ? string.Join(", ", parts) : "texte seul";
        return $"Message de {message.Author}{origin} : {content} → {Plural(items.Count, "média affiché", "médias affichés")}.";
    }

    private static string Plural(int count, string singular, string plural) => $"{count} {(count > 1 ? plural : singular)}";

    /// <summary>Détermine le type d'un fichier d'après son type MIME, puis son extension.</summary>
    public static MediaKind Classify(string? contentType, string? fileName)
    {
        var extension = ExtensionOf(fileName);
        var mime = contentType ?? "";

        if (mime.StartsWith("image/gif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            return MediaKind.AnimatedImage;
        }

        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || ImageExtensions.Contains(extension))
        {
            return MediaKind.Image;
        }

        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || VideoExtensions.Contains(extension))
        {
            return MediaKind.Video;
        }

        return MediaKind.File;
    }

    /// <summary>Nettoie un texte Discord pour l'affichage : liens, markdown, emojis personnalisés, spoilers.</summary>
    public static string? Clean(string? content, bool hideSpoilers, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = SpoilerRegex().Replace(content, m => hideSpoilers ? " ▒▒▒ " : m.Groups["inner"].Value);
        text = CustomEmojiRegex().Replace(text, ":${name}:");
        text = MaskedLinkRegex().Replace(text, "${label}"); // [texte](https://…) : on garde le texte.
        text = RawMentionRegex().Replace(text, " "); // <@123>, <#123>, <t:…> (texte d'un message transféré).
        text = UrlRegex().Replace(text, " ");
        text = HeadingRegex().Replace(text, "");
        text = QuoteRegex().Replace(text, "");
        text = MarkdownRegex().Replace(text, "");
        text = ItalicRegex().Replace(text, "${text}");
        text = WhitespaceRegex().Replace(text, " ").Trim();

        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }

    private static string? BuildCaption(IncomingMessage message, ParserOptions options)
    {
        if (message.IsExplicitCaption)
        {
            return Clean(message.Content, hideSpoilers: false, MaxCaptionLength);
        }

        // Pour un aperçu de lien arrivé après coup, le texte a déjà été affiché seul si l'option est active.
        var show = options.ShowMessageText && !(message.EmbedsOnly && options.ShowTextOnlyMessages);
        return show ? Clean(message.Content, options.BlurSpoilers, MaxCaptionLength) : null;
    }

    private static MediaItem? FromEmbed(EmbedInfo embed, IncomingMessage message, bool isSpoiler)
    {
        string? url;
        MediaKind kind;

        switch (embed.Type.ToLowerInvariant())
        {
            case "gifv":
                // GIF Tenor/Giphy : Discord fournit une petite vidéo MP4 à jouer en boucle.
                if (embed.VideoUrl is not null)
                {
                    url = embed.VideoUrl;
                    kind = MediaKind.LoopingVideo;
                }
                else
                {
                    url = embed.ImageUrl ?? embed.ThumbnailUrl;
                    kind = MediaKind.AnimatedImage;
                }

                break;

            case "image":
                // Lien direct vers une image : l'URL est dans la miniature.
                url = embed.ImageUrl ?? embed.ThumbnailUrl ?? embed.Url;
                kind = ExtensionOf(url).Equals(".gif", StringComparison.OrdinalIgnoreCase) ? MediaKind.AnimatedImage : MediaKind.Image;
                break;

            case "video":
                // Seuls les liens directs vers un fichier vidéo sont lisibles (pas YouTube, Twitch...).
                if (embed.HasProvider || embed.VideoUrl is null || !VideoExtensions.Contains(ExtensionOf(embed.VideoUrl)))
                {
                    return null;
                }

                url = embed.VideoUrl;
                kind = MediaKind.Video;
                break;

            default:
                // Embeds « riches » (réseaux sociaux, articles) : on prend l'image principale s'il y en a une.
                url = embed.ImageUrl;
                kind = ExtensionOf(url).Equals(".gif", StringComparison.OrdinalIgnoreCase) ? MediaKind.AnimatedImage : MediaKind.Image;
                break;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new MediaItem(kind, message.Author)
        {
            Url = url,
            AvatarUrl = message.AvatarUrl,
            IsSpoiler = isSpoiler,
        };
    }

    private static string ExtensionOf(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return "";
        }

        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return Path.GetExtension(uri.AbsolutePath);
        }

        try
        {
            return Path.GetExtension(pathOrUrl);
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    [GeneratedRegex(@"\|\|(?<inner>.+?)\|\|", RegexOptions.Singleline)]
    private static partial Regex SpoilerRegex();

    [GeneratedRegex(@"<a?:(?<name>\w+):\d+>")]
    private static partial Regex CustomEmojiRegex();

    [GeneratedRegex(@"<?https?://[^\s>]+>?", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^\s*(?:#{1,3}|-#)\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\*\*|__|~~|`+")]
    private static partial Regex MarkdownRegex();

    [GeneratedRegex(@"\[(?<label>[^\]\n]+)\]\(\s*<?https?://[^)\s>]+>?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex MaskedLinkRegex();

    [GeneratedRegex(@"<(?:@[!&]?\d+|#\d+|t:\d+(?::[tTdDfFR])?)>")]
    private static partial Regex RawMentionRegex();

    [GeneratedRegex(@"^\s*>{1,3}\s?", RegexOptions.Multiline)]
    private static partial Regex QuoteRegex();

    /// <summary>*italique* ou _italique_, sans toucher à 2*3*4 ni à nom_de_fichier.</summary>
    [GeneratedRegex(@"(?<![\w*])\*(?=\S)(?<text>[^*\n]+?)(?<=\S)\*(?![\w*])|(?<!\w)_(?=\S)(?<text>[^_\n]+?)(?<=\S)_(?!\w)")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
