namespace LiveChat;

/// <summary>Emplacements des fichiers de l'application.</summary>
public static class AppPaths
{
    /// <summary>
    /// Dossier des données utilisateur (%APPDATA%\LiveChat). Peut être remplacé par la variable
    /// d'environnement LIVECHAT_DATA_DIR (utile pour un usage portable ou pour les tests).
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    /// <summary>Vrai si le dossier de données a été remplacé par LIVECHAT_DATA_DIR.</summary>
    public static bool IsDataDirectoryOverridden =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIVECHAT_DATA_DIR"));

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Dossier contenant l'exécutable.</summary>
    public static string AppDirectory => AppContext.BaseDirectory;

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppDirectory, "LiveChat.exe");

    /// <summary>Ancien fichier de configuration (v1) stocké en clair à côté de l'exécutable.</summary>
    public static string LegacyConfigFile => Path.Combine(AppDirectory, "config.json");

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("LIVECHAT_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveChat");
    }
}
