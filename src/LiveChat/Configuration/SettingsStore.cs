using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveChat.Configuration;

/// <summary>
/// Lecture/écriture de settings.json. Le token Discord est chiffré avec DPAPI (portée utilisateur),
/// l'écriture passe par un fichier temporaire pour ne jamais laisser un fichier à moitié écrit.
/// </summary>
public sealed class SettingsStore(string settingsPath, string? legacyConfigPath = null)
{
    private static readonly byte[] Entropy = "LiveChat/discord-token/v2"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string SettingsPath { get; } = settingsPath;

    public AppSettings Load()
    {
        AppSettings? settings = null;

        if (File.Exists(SettingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                Log.Warn($"Paramètres illisibles ({SettingsPath}), valeurs par défaut utilisées.", ex);
                BackupCorruptedFile();
            }
        }
        else if (TryMigrateLegacyConfig(out var migrated))
        {
            settings = migrated;
        }

        settings ??= new AppSettings();
        if (string.IsNullOrEmpty(settings.BotToken))
        {
            settings.BotToken = Unprotect(settings.ProtectedBotToken);
        }

        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.ProtectedBotToken = Protect(settings.BotToken);

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
        File.Move(temporary, SettingsPath, overwrite: true);
    }

    internal static string? Protect(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    internal static string Unprotect(string? protectedToken)
    {
        if (string.IsNullOrEmpty(protectedToken))
        {
            return "";
        }

        try
        {
            var clear = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Fichier copié depuis un autre compte Windows ou altéré : il faudra ressaisir le token.
            Log.Warn("Le token enregistré ne peut pas être déchiffré ; il devra être ressaisi.", ex);
            return "";
        }
    }

    /// <summary>Reprend l'ancien config.json (v1, token en clair) puis le supprime.</summary>
    private bool TryMigrateLegacyConfig(out AppSettings? settings)
    {
        settings = null;
        if (legacyConfigPath is null || !File.Exists(legacyConfigPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyConfigPath));
            var root = document.RootElement;
            var migrated = new AppSettings();

            if (root.TryGetProperty("BotToken", out var token) && token.ValueKind == JsonValueKind.String)
            {
                migrated.BotToken = token.GetString() ?? "";
            }

            if (root.TryGetProperty("ChannelId", out var channel) && channel.TryGetUInt64(out var channelId))
            {
                migrated.ChannelId = channelId;
            }

            Save(migrated);
            settings = migrated;
            File.Delete(legacyConfigPath);
            Log.Info("Ancienne configuration (config.json) migrée ; le token est désormais chiffré.");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Impossible de migrer l'ancien config.json.", ex);
            return settings is not null;
        }
    }

    private void BackupCorruptedFile()
    {
        try
        {
            File.Copy(SettingsPath, SettingsPath + ".corrompu", overwrite: true);
        }
        catch (IOException)
        {
        }
    }
}
