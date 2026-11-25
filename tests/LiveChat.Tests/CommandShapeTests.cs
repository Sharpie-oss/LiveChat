using Discord;
using LiveChat.Services;

namespace LiveChat.Tests;

public class CommandShapeTests
{
    private static SlashCommandProperties Skip(GuildPermission? permission = GuildPermission.ManageMessages)
    {
        var builder = new SlashCommandBuilder().WithName("skip").WithDescription("Passe le média en cours d'affichage");
        if (permission is { } p)
        {
            builder.WithDefaultMemberPermissions(p);
        }

        return builder.Build();
    }

    private static SlashCommandProperties Caption(int maxLength) => new SlashCommandBuilder()
        .WithName("caption").WithDescription("Légende")
        .AddOption("texte", ApplicationCommandOptionType.String, "Texte", isRequired: true, maxLength: maxLength)
        .AddOption("media", ApplicationCommandOptionType.Attachment, "Média", isRequired: true)
        .Build();

    private static string Signature(params SlashCommandProperties[] commands) =>
        CommandShape.Signature(commands.Select(CommandShape.From));

    [Fact]
    public void Signature_IgnoresOrder() =>
        Assert.Equal(Signature(Skip(), Caption(200)), Signature(Caption(200), Skip()));

    [Fact]
    public void Signature_ChangesWithPermissionsOrOptions()
    {
        var reference = Signature(Skip(), Caption(200));

        Assert.NotEqual(reference, Signature(Skip(permission: null), Caption(200)));
        Assert.NotEqual(reference, Signature(Skip(), Caption(100)));
        Assert.NotEqual(reference, Signature(Skip()));
    }
}
