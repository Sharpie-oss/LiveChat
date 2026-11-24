using System.Text.RegularExpressions;

namespace LiveChat.Configuration;

/// <summary>Validation et normalisation des saisies de l'écran de paramètres.</summary>
public static partial class SettingsValidation
{
    /// <summary>
    /// Accepte un identifiant de salon (17 à 20 chiffres) ou un lien de salon Discord
    /// (https://discord.com/channels/&lt;serveur&gt;/&lt;salon&gt;).
    /// </summary>
    public static bool TryParseChannelId(string? input, out ulong channelId)
    {
        channelId = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        var link = ChannelLinkRegex().Match(text);
        if (link.Success)
        {
            text = link.Groups["channel"].Value;
        }

        return SnowflakeRegex().IsMatch(text) && ulong.TryParse(text, out channelId) && channelId != 0;
    }

    /// <summary>Nettoie un token collé : espaces, guillemets et préfixe « Bot ».</summary>
    public static string NormalizeToken(string? input)
    {
        var token = (input ?? "").Trim().Trim('"', '\'').Trim();
        if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[4..].Trim();
        }

        return token;
    }

    [GeneratedRegex(@"discord(?:app)?\.com/channels/(?:\d+|@me)/(?<channel>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelLinkRegex();

    [GeneratedRegex(@"^\d{17,20}$")]
    private static partial Regex SnowflakeRegex();
}
