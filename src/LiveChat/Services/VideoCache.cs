namespace LiveChat.Services;

/// <summary>
/// Fichiers vidéo temporaires. Le lecteur de WPF (MediaElement) refuse les URL qui contiennent une
/// query string (« Seuls les URI à en-tête pack du site d'origine sont pris en charge pour les médias »),
/// or Discord signe toujours ses liens (?ex=…&amp;is=…&amp;hm=…) : les vidéos sont donc téléchargées puis lues en local.
/// </summary>
public static class VideoCache
{
    /// <summary>Limite de taille d'une vidéo téléchargée (Discord autorise jusqu'à 500 Mo avec Nitro).</summary>
    public const long MaxVideoBytes = 500L * 1024 * 1024;

    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv", ".flv",
    };

    public static string Directory { get; } = Path.Combine(Path.GetTempPath(), "LiveChat", "videos");

    /// <summary>Nom de fichier temporaire unique, avec l'extension de lecture (le lecteur choisit le décodeur d'après elle).</summary>
    public static string CreateTempPath(string url)
    {
        System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, Guid.NewGuid().ToString("N") + GetPlaybackExtension(url));
    }

    /// <summary>Extension vidéo de l'URL (sans la query string) ; .mp4 par défaut.</summary>
    public static string GetExtension(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var extension = Path.GetExtension(path);
        return KnownExtensions.Contains(extension) ? extension.ToLowerInvariant() : ".mp4";
    }

    /// <summary>
    /// Extension du fichier local à lire. Le lecteur de WPF (moteur de Windows Media Player) refuse les fichiers
    /// .webm, même avec les « Extensions de vidéo web » du Microsoft Store (erreur 0xC00D0FEA). Le WebM est un
    /// sous-ensemble du Matroska : nommé .mkv, le même fichier est lu par le lecteur Matroska de Windows (VP8, VP9, Opus).
    /// </summary>
    public static string GetPlaybackExtension(string url)
    {
        var extension = GetExtension(url);
        return extension == ".webm" ? ".mkv" : extension;
    }

    /// <summary>Vrai pour un fichier créé par ce cache (jamais pour un fichier de l'utilisateur).</summary>
    public static bool IsTemporary(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // Encore ouvert par le lecteur : supprimé au prochain nettoyage.
        }
    }

    /// <summary>
    /// Supprime les vidéos temporaires laissées par une session précédente (fermeture brutale...).
    /// Les fichiers récents sont ignorés : ils peuvent appartenir à une autre instance en cours.
    /// </summary>
    public static void CleanupStaleFiles()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        var limit = DateTime.UtcNow.AddMinutes(-10);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (File.GetLastWriteTimeUtc(file) < limit)
            {
                TryDelete(file);
            }
        }
    }
}
