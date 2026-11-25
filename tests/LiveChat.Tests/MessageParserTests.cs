using LiveChat.Models;
using LiveChat.Services;

namespace LiveChat.Tests;

public class MessageParserTests
{
    private static readonly ParserOptions Defaults = new(ShowMessageText: true, ShowTextOnlyMessages: false, BlurSpoilers: true);

    private static IncomingMessage Message(
        string content = "",
        AttachmentInfo[]? attachments = null,
        EmbedInfo[]? embeds = null) =>
        new(1, "Alice", "https://cdn.example/avatar.png", content, attachments ?? [], embeds ?? []);

    [Theory]
    [InlineData("image/png", "photo.png", MediaKind.Image)]
    [InlineData(null, "photo.JPG", MediaKind.Image)]
    [InlineData("image/webp", "sticker", MediaKind.Image)]
    [InlineData("image/gif", "anim.gif", MediaKind.AnimatedImage)]
    [InlineData(null, "anim.gif", MediaKind.AnimatedImage)]
    [InlineData("video/mp4", "clip.mp4", MediaKind.Video)]
    [InlineData(null, "clip.webm", MediaKind.Video)]
    [InlineData("application/pdf", "doc.pdf", MediaKind.File)]
    [InlineData(null, "archive", MediaKind.File)]
    public void Classify_UsesMimeTypeThenExtension(string? contentType, string fileName, MediaKind expected) =>
        Assert.Equal(expected, MessageParser.Classify(contentType, fileName));

    [Fact]
    public void Parse_TextWithoutMedia_IsIgnoredByDefault()
    {
        var items = MessageParser.Parse(Message("Salut tout le monde"), Defaults);

        Assert.Empty(items);
    }

    [Fact]
    public void Parse_TextWithoutMedia_IsShownWhenOptionEnabled()
    {
        var options = Defaults with { ShowTextOnlyMessages = true };

        var item = Assert.Single(MessageParser.Parse(Message("Salut **tout** le monde"), options));

        Assert.Equal(MediaKind.Text, item.Kind);
        Assert.Equal("Salut tout le monde", item.Text);
        Assert.Equal("Alice", item.Author);
    }

    [Fact]
    public void Parse_Attachments_CaptionOnlyOnFirstItem()
    {
        var message = Message("Regardez ça https://example.com/page", [
            new AttachmentInfo("https://cdn/a.png", "a.png", "image/png"),
            new AttachmentInfo("https://cdn/b.mp4", "b.mp4", "video/mp4"),
        ]);

        var items = MessageParser.Parse(message, Defaults);

        Assert.Equal(2, items.Count);
        Assert.Equal(MediaKind.Image, items[0].Kind);
        Assert.Equal("Regardez ça", items[0].Caption);
        Assert.Equal(MediaKind.Video, items[1].Kind);
        Assert.Null(items[1].Caption);
    }

    [Fact]
    public void Parse_CaptionDisabled_NoCaption()
    {
        var message = Message("Texte", [new AttachmentInfo("https://cdn/a.png", "a.png", "image/png")]);

        var item = Assert.Single(MessageParser.Parse(message, Defaults with { ShowMessageText = false }));

        Assert.Null(item.Caption);
    }

    [Fact]
    public void Parse_SpoilerAttachment_IsBlurredOnlyWhenOptionEnabled()
    {
        var message = Message(attachments: [new AttachmentInfo("https://cdn/s.png", "SPOILER_s.png", "image/png")]);

        Assert.True(Assert.Single(MessageParser.Parse(message, Defaults)).IsSpoiler);
        Assert.False(Assert.Single(MessageParser.Parse(message, Defaults with { BlurSpoilers = false })).IsSpoiler);
    }

    [Fact]
    public void Parse_TenorGifv_BecomesLoopingVideo()
    {
        var embed = new EmbedInfo("Gifv", "https://tenor.com/view/x", null, "https://media.tenor.com/x.png", "https://media.tenor.com/x.mp4", HasProvider: true);

        var item = Assert.Single(MessageParser.Parse(Message("https://tenor.com/view/x", embeds: [embed]), Defaults));

        Assert.Equal(MediaKind.LoopingVideo, item.Kind);
        Assert.Equal("https://media.tenor.com/x.mp4", item.Url);
        Assert.Null(item.Caption); // Le message ne contenait qu'un lien.
    }

    [Fact]
    public void Parse_DirectImageLink_UsesThumbnail()
    {
        var embed = new EmbedInfo("Image", "https://i.example/cat.gif", null, "https://proxy/cat.gif", null, HasProvider: false);

        var item = Assert.Single(MessageParser.Parse(Message(embeds: [embed]), Defaults));

        Assert.Equal(MediaKind.AnimatedImage, item.Kind);
        Assert.Equal("https://proxy/cat.gif", item.Url);
    }

    [Fact]
    public void Parse_YoutubeVideoEmbed_IsIgnored()
    {
        var embed = new EmbedInfo("Video", "https://youtube.com/watch?v=1", null, "https://i.ytimg.com/x.jpg", "https://www.youtube.com/embed/1", HasProvider: true);

        Assert.Empty(MessageParser.Parse(Message(embeds: [embed]), Defaults));
    }

    [Fact]
    public void Parse_DirectVideoLink_IsPlayed()
    {
        var embed = new EmbedInfo("Video", "https://cdn.example/clip.mp4", null, null, "https://cdn.example/clip.mp4", HasProvider: false);

        var item = Assert.Single(MessageParser.Parse(Message(embeds: [embed]), Defaults));

        Assert.Equal(MediaKind.Video, item.Kind);
    }

    [Fact]
    public void Parse_RichEmbedWithImage_UsesImage()
    {
        var embed = new EmbedInfo("Rich", "https://x.com/post", "https://pbs.example/photo.jpg", null, null, HasProvider: false);

        var item = Assert.Single(MessageParser.Parse(Message(embeds: [embed]), Defaults));

        Assert.Equal(MediaKind.Image, item.Kind);
    }

    [Fact]
    public void Parse_SpoileredLink_MarksEmbedAsSpoiler()
    {
        var embed = new EmbedInfo("Image", "https://i.example/a.png", null, "https://proxy/a.png", null, HasProvider: false);

        var item = Assert.Single(MessageParser.Parse(Message("||https://i.example/a.png||", embeds: [embed]), Defaults));

        Assert.True(item.IsSpoiler);
    }

    [Fact]
    public void Parse_DuplicateUrls_AreDeduplicated()
    {
        var message = Message(
            attachments: [new AttachmentInfo("https://cdn/a.png", "a.png", "image/png")],
            embeds: [new EmbedInfo("Image", null, "https://cdn/a.png", null, null, false)]);

        Assert.Single(MessageParser.Parse(message, Defaults));
    }

    [Fact]
    public void Parse_EmbedsOnlyUpdate_IgnoresAttachments()
    {
        var message = Message(
            attachments: [new AttachmentInfo("https://cdn/a.png", "a.png", "image/png")],
            embeds: [new EmbedInfo("Image", null, null, "https://proxy/b.png", null, false)]) with { EmbedsOnly = true };

        var item = Assert.Single(MessageParser.Parse(message, Defaults));

        Assert.Equal("https://proxy/b.png", item.Url);
    }

    [Fact]
    public void Parse_Stickers_AreShownAsImages()
    {
        var message = Message() with
        {
            Stickers =
            [
                new StickerInfo("https://media.discordapp.net/stickers/1.png", "Coucou", IsAnimated: false),
                new StickerInfo("https://media.discordapp.net/stickers/2.gif", "Danse", IsAnimated: true),
            ],
        };

        var items = MessageParser.Parse(message, Defaults);

        Assert.Equal(2, items.Count);
        Assert.Equal(MediaKind.Image, items[0].Kind);
        Assert.Equal("https://media.discordapp.net/stickers/1.png", items[0].Url);
        Assert.Equal(MediaKind.AnimatedImage, items[1].Kind);
        Assert.All(items, i => Assert.Equal("Alice", i.Author));
    }

    [Fact]
    public void Parse_EmbedsOnlyUpdate_IgnoresStickers()
    {
        var message = Message() with
        {
            EmbedsOnly = true,
            Stickers = [new StickerInfo("https://media.discordapp.net/stickers/1.png", "Coucou", IsAnimated: false)],
        };

        Assert.Empty(MessageParser.Parse(message, Defaults));
    }

    [Fact]
    public void Parse_ExplicitCaption_IsAlwaysShown()
    {
        var message = Message("Ma légende", [new AttachmentInfo("https://cdn/a.mp4", "a.mp4", "video/mp4")]) with { IsExplicitCaption = true };

        var item = Assert.Single(MessageParser.Parse(message, Defaults with { ShowMessageText = false }));

        Assert.Equal("Ma légende", item.Caption);
    }

    [Theory]
    [InlineData("  plusieurs   espaces \n ici ", "plusieurs espaces ici")]
    [InlineData("**gras** __souligné__ ~~barré~~ `code`", "gras souligné barré code")]
    [InlineData("# Titre\ntexte", "Titre texte")]
    [InlineData("Salut <:pepe:123456789012345678> !", "Salut :pepe: !")]
    [InlineData("voir <https://example.com> ok", "voir ok")]
    [InlineData("https://example.com", null)]
    [InlineData("   ", null)]
    public void Clean_NormalizesDiscordMarkup(string input, string? expected) =>
        Assert.Equal(expected, MessageParser.Clean(input, hideSpoilers: true, MessageParser.MaxCaptionLength));

    [Fact]
    public void Clean_HidesOrRevealsSpoilerText()
    {
        Assert.Equal("La fin : ▒▒▒", MessageParser.Clean("La fin : ||il meurt||", hideSpoilers: true, 200));
        Assert.Equal("La fin : il meurt", MessageParser.Clean("La fin : ||il meurt||", hideSpoilers: false, 200));
    }

    [Fact]
    public void Clean_TruncatesLongText()
    {
        var result = MessageParser.Clean(new string('a', 300), hideSpoilers: true, 50);

        Assert.NotNull(result);
        Assert.Equal(50, result.Length);
        Assert.EndsWith("…", result);
    }
}
