using Discord;

namespace LiveChat.Services;

/// <summary>Option d'une commande slash, réduite à ce que LiveChat définit.</summary>
public sealed record CommandOptionShape(string Name, string Description, int Type, bool Required, int? MaxLength);

/// <summary>
/// Forme d'une commande slash (nom, description, permissions, options) : permet de comparer les commandes déjà
/// enregistrées sur le serveur à celles de cette version, sans dépendre des objets de Discord.Net.
/// </summary>
public sealed record CommandShape(string Name, string Description, ulong Permissions, IReadOnlyList<CommandOptionShape> Options)
{
    public static CommandShape From(SlashCommandProperties command) => new(
        command.Name.Value,
        command.Description.Value,
        command.DefaultMemberPermissions.IsSpecified ? (ulong)command.DefaultMemberPermissions.Value : 0,
        (command.Options.IsSpecified ? command.Options.Value : [])
            .Select(o => new CommandOptionShape(o.Name, o.Description, (int)o.Type, o.IsRequired ?? false, o.MaxLength))
            .ToList());

    public static CommandShape From(IApplicationCommand command) => new(
        command.Name,
        command.Description,
        command.DefaultMemberPermissions.RawValue,
        command.Options
            .Select(o => new CommandOptionShape(o.Name, o.Description, (int)o.Type, o.IsRequired ?? false, o.MaxLength))
            .ToList());

    /// <summary>Représentation canonique d'un ensemble de commandes (indépendante de l'ordre).</summary>
    public static string Signature(IEnumerable<CommandShape> commands) => string.Join("\n", commands
        .OrderBy(c => c.Name, StringComparer.Ordinal)
        .Select(c => $"{c.Name}|{c.Description}|{c.Permissions}|" + string.Join(";", c.Options
            .Select(o => $"{o.Name}:{o.Description}:{o.Type}:{o.Required}:{o.MaxLength}"))));
}
