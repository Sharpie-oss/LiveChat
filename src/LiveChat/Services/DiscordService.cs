using System.Net;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using LiveChat.Models;

namespace LiveChat.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,

    /// <summary>Problème temporaire ou de configuration du salon ; Discord.Net continue de se reconnecter.</summary>
    Warning,

    /// <summary>Erreur bloquante qui demande une action de l'utilisateur (token, intents...).</summary>
    Error,
}

public sealed record ConnectionStatus(ConnectionState State, string Message);

public enum RemoteCommand
{
    Skip,
    Stop,
}

/// <summary>
/// Connexion au gateway Discord. Les événements sont déclenchés depuis les threads de Discord.Net :
/// l'abonné doit les renvoyer sur le thread de l'interface (sans bloquer).
/// </summary>
public sealed class DiscordService : IAsyncDisposable
{
    private const int DisallowedIntentsCloseCode = 4014;
    private const int AuthenticationFailedCloseCode = 4004;
    private const int MaxTrackedMessages = 256;
    private static readonly TimeSpan MaxEmbedDelay = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Lock _embedGate = new();
    private readonly HashSet<ulong> _embedHandled = [];
    private readonly Queue<ulong> _embedOrder = new();

    private DiscordSocketClient? _client;
    private CancellationTokenSource? _connectCts;
    private ulong _channelId;
    private volatile bool _commandsRegistered;

    public event Action<ConnectionStatus>? StatusChanged;

    public event Action<IncomingMessage>? MessageReceived;

    public event Action<RemoteCommand>? CommandReceived;

    public ConnectionStatus Status { get; private set; } = new(ConnectionState.Disconnected, "Non connecté");

    /// <summary>Nom lisible du salon surveillé (#salon · Serveur), connu une fois connecté.</summary>
    public string? ChannelName { get; private set; }

    /// <summary>Quand la réception est en pause, les messages et commandes /caption sont ignorés.</summary>
    public bool IsPaused { get; set; }

    /// <summary>(Re)démarre la connexion. Les erreurs réseau sont retentées automatiquement.</summary>
    public Task StartAsync(string token, ulong channelId)
    {
        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource();
        return RunExclusiveAsync(() => ConnectLoopAsync(token, channelId, cts.Token));
    }

    public Task StopAsync() => StopCoreAsync(resetStatus: true);

    /// <summary>Arrête le client ; après une erreur bloquante on conserve le message d'erreur affiché.</summary>
    private Task StopCoreAsync(bool resetStatus)
    {
        _connectCts?.Cancel();
        return RunExclusiveAsync(async () =>
        {
            await DisposeClientAsync();
            if (resetStatus)
            {
                SetStatus(ConnectionState.Disconnected, "Non connecté");
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }

    private async Task RunExclusiveAsync(Func<Task> action)
    {
        await _lifecycle.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task ConnectLoopAsync(string token, ulong channelId, CancellationToken cancellationToken)
    {
        await DisposeClientAsync();
        _channelId = channelId;
        _commandsRegistered = false;
        ChannelName = null;

        var retryDelay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = CreateClient();
            _client = client;
            try
            {
                SetStatus(ConnectionState.Connecting, "Connexion à Discord…");
                await client.LoginAsync(TokenType.Bot, token);

                // LoginAsync ne contacte plus Discord : sans cette vérification, un token refusé n'apparaîtrait
                // qu'au gateway, que Discord.Net retenterait alors indéfiniment.
                var application = await client.Rest.GetApplicationInfoAsync();
                Log.Info($"Token accepté pour l'application « {application.Name} ».");

                await client.StartAsync();
                return; // Discord.Net gère ensuite lui-même les reconnexions.
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Unauthorized)
            {
                await DisposeClientAsync();
                SetStatus(ConnectionState.Error, "Token refusé par Discord : vérifiez le Bot Token dans les paramètres.");
                return;
            }
            catch (ArgumentException ex)
            {
                Log.Warn("Token au format invalide.", ex);
                await DisposeClientAsync();
                SetStatus(ConnectionState.Error, "Le Bot Token n'a pas un format valide.");
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warn("Connexion à Discord impossible.", ex);
                await DisposeClientAsync();
                SetStatus(ConnectionState.Warning, $"Connexion impossible, nouvel essai dans {retryDelay.TotalSeconds:0} s…");
                try
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
            catch (Exception)
            {
                await DisposeClientAsync();
                return;
            }
        }
    }

    private DiscordSocketClient CreateClient()
    {
        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            // MessageContent est un intent privilégié : il doit être activé dans le portail développeur.
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Info,
            MessageCacheSize = 0,
            AlwaysDownloadUsers = false,
        });

        client.Log += OnLog;
        client.Ready += () => OnReady(client);
        client.Connected += () => OnConnected(client);
        client.Disconnected += ex => OnDisconnected(client, ex);
        client.MessageReceived += message => OnMessageReceived(client, message);
        client.MessageUpdated += (_, message, channel) => OnMessageUpdated(client, message, channel);
        client.SlashCommandExecuted += command => OnSlashCommandAsync(client, command);
        return client;
    }

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        try
        {
            await client.StopAsync();
            await client.LogoutAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Erreur lors de la déconnexion de Discord.", ex);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private bool IsCurrent(DiscordSocketClient client) => ReferenceEquals(client, _client);

    private void SetStatus(ConnectionState state, string message)
    {
        Status = new ConnectionStatus(state, message);
        Log.Info($"Statut Discord : {state} — {message}");
        StatusChanged?.Invoke(Status);
    }

    private static Task OnLog(LogMessage message)
    {
        var text = $"[Discord] {message.Source}: {message.Message}";
        switch (message.Severity)
        {
            case LogSeverity.Critical or LogSeverity.Error:
                Log.Error(text, message.Exception);
                break;
            case LogSeverity.Warning:
                Log.Warn(text, message.Exception);
                break;
            case LogSeverity.Info:
                Log.Info(text);
                break;
        }

        return Task.CompletedTask;
    }

    private Task OnReady(DiscordSocketClient client)
    {
        if (!IsCurrent(client))
        {
            return Task.CompletedTask;
        }

        if (UpdateChannelStatus(client) is { } guild && !_commandsRegistered)
        {
            // Hors du handler pour ne pas bloquer le gateway.
            _ = Task.Run(() => RegisterCommandsAsync(client, guild));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Après une coupure réseau, Discord.Net reprend la session (RESUME) sans redéclencher Ready : sans ce gestionnaire,
    /// le statut restait indéfiniment sur « Connexion perdue, reconnexion en cours… » alors que tout refonctionnait.
    /// À la première connexion, le salon n'est pas encore en cache : Ready s'en charge.
    /// </summary>
    private Task OnConnected(DiscordSocketClient client)
    {
        if (IsCurrent(client) && client.GetChannel(_channelId) is not null)
        {
            UpdateChannelStatus(client);
        }

        return Task.CompletedTask;
    }

    /// <summary>Met à jour le statut d'après le salon surveillé ; renvoie son serveur s'il est valide.</summary>
    private SocketGuild? UpdateChannelStatus(DiscordSocketClient client)
    {
        switch (client.GetChannel(_channelId))
        {
            case null:
                ChannelName = null;
                SetStatus(ConnectionState.Warning,
                    $"Salon {_channelId} introuvable : vérifiez l'ID et que le bot a bien été invité sur le serveur.");
                return null;

            case SocketGuildChannel channel and IMessageChannel:
                ChannelName = $"#{channel.Name} · {channel.Guild.Name}";
                if (!channel.Guild.CurrentUser.GetPermissions(channel).ViewChannel)
                {
                    SetStatus(ConnectionState.Warning, $"Le bot n'a pas la permission de voir {ChannelName}.");
                }
                else
                {
                    SetStatus(ConnectionState.Connected, $"Connecté à {ChannelName}");
                }

                return channel.Guild;

            default:
                ChannelName = null;
                SetStatus(ConnectionState.Warning, "L'ID configuré ne correspond pas à un salon textuel de serveur.");
                return null;
        }
    }

    private Task OnDisconnected(DiscordSocketClient client, Exception? exception)
    {
        if (!IsCurrent(client))
        {
            return Task.CompletedTask;
        }

        if (FindException<HttpException>(exception) is { HttpCode: HttpStatusCode.Unauthorized })
        {
            SetStatus(ConnectionState.Error, "Token refusé par Discord : vérifiez le Bot Token dans les paramètres.");
            _ = Task.Run(() => StopCoreAsync(resetStatus: false));
            return Task.CompletedTask;
        }

        switch (FindException<WebSocketClosedException>(exception)?.CloseCode)
        {
            case DisallowedIntentsCloseCode:
                SetStatus(ConnectionState.Error,
                    "Discord refuse l'intent « Message Content » : activez-le dans le portail développeur (onglet Bot).");
                _ = Task.Run(() => StopCoreAsync(resetStatus: false));
                break;

            case AuthenticationFailedCloseCode:
                SetStatus(ConnectionState.Error, "Token refusé par Discord : vérifiez le Bot Token dans les paramètres.");
                _ = Task.Run(() => StopCoreAsync(resetStatus: false));
                break;

            default:
                if (Status.State != ConnectionState.Error)
                {
                    SetStatus(ConnectionState.Warning, "Connexion perdue, reconnexion en cours…");
                }

                break;
        }

        return Task.CompletedTask;
    }

    private Task OnMessageReceived(DiscordSocketClient client, SocketMessage message)
    {
        if (!IsCurrent(client) || IsPaused || message.Channel.Id != _channelId
            || message is not SocketUserMessage || message.Author.IsBot)
        {
            return Task.CompletedTask;
        }

        if (message.Embeds.Count > 0)
        {
            MarkEmbedsHandled(message.Id);
        }

        MessageReceived?.Invoke(ToIncoming(message, embedsOnly: false));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Discord ajoute souvent les aperçus de liens (images, GIF Tenor) quelques instants après l'envoi,
    /// via une mise à jour du message : on les récupère ici, une seule fois par message.
    /// </summary>
    private Task OnMessageUpdated(DiscordSocketClient client, SocketMessage message, ISocketMessageChannel channel)
    {
        if (!IsCurrent(client) || IsPaused || channel.Id != _channelId || message.Author.IsBot
            || message.Embeds.Count == 0 || message.EditedTimestamp is not null
            || DateTimeOffset.UtcNow - message.Timestamp > MaxEmbedDelay)
        {
            return Task.CompletedTask;
        }

        if (MarkEmbedsHandled(message.Id))
        {
            MessageReceived?.Invoke(ToIncoming(message, embedsOnly: true));
        }

        return Task.CompletedTask;
    }

    private static T? FindException<T>(Exception? exception)
        where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <returns><c>false</c> si les embeds de ce message ont déjà été traités.</returns>
    private bool MarkEmbedsHandled(ulong messageId)
    {
        lock (_embedGate)
        {
            if (!_embedHandled.Add(messageId))
            {
                return false;
            }

            _embedOrder.Enqueue(messageId);
            if (_embedOrder.Count > MaxTrackedMessages)
            {
                _embedHandled.Remove(_embedOrder.Dequeue());
            }

            return true;
        }
    }

    private async Task OnSlashCommandAsync(DiscordSocketClient client, SocketSlashCommand command)
    {
        if (!IsCurrent(client))
        {
            return;
        }

        try
        {
            if (command.ChannelId != _channelId)
            {
                await command.RespondAsync("❌ Cette commande n'est utilisable que dans le salon LiveChat.", ephemeral: true);
                return;
            }

            switch (command.CommandName)
            {
                case "caption":
                    await HandleCaptionAsync(command);
                    break;
                case "skip":
                    CommandReceived?.Invoke(RemoteCommand.Skip);
                    await command.RespondAsync("⏭️ Média passé.", ephemeral: true);
                    break;
                case "stop":
                    CommandReceived?.Invoke(RemoteCommand.Stop);
                    await command.RespondAsync("⏹️ Affichage arrêté et file d'attente vidée.", ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Erreur lors du traitement de /{command.CommandName}.", ex);
        }
    }

    private async Task HandleCaptionAsync(SocketSlashCommand command)
    {
        if (IsPaused)
        {
            await command.RespondAsync("⏸️ LiveChat est actuellement en pause.", ephemeral: true);
            return;
        }

        var caption = command.Data.Options.FirstOrDefault(o => o.Name == "texte")?.Value as string ?? "";
        if (command.Data.Options.FirstOrDefault(o => o.Name == "media")?.Value is not IAttachment attachment)
        {
            await command.RespondAsync("❌ Aucun média fourni.", ephemeral: true);
            return;
        }

        var kind = MessageParser.Classify(attachment.ContentType, attachment.Filename);
        if (kind is not (MediaKind.Image or MediaKind.AnimatedImage or MediaKind.Video))
        {
            await command.RespondAsync("❌ Seules les images, les GIF et les vidéos peuvent être affichés.", ephemeral: true);
            return;
        }

        var user = command.User;
        var message = new IncomingMessage(
            command.Id,
            DisplayName(user),
            AvatarUrl(user),
            caption,
            [new AttachmentInfo(attachment.Url, attachment.Filename, attachment.ContentType)],
            [])
        {
            IsExplicitCaption = true,
        };

        MessageReceived?.Invoke(message);
        await command.RespondAsync("✅ Média ajouté à la file LiveChat !", ephemeral: true);
    }

    private async Task RegisterCommandsAsync(DiscordSocketClient client, SocketGuild guild)
    {
        try
        {
            var commands = new ApplicationCommandProperties[]
            {
                new SlashCommandBuilder()
                    .WithName("caption")
                    .WithDescription("Affiche une image ou une vidéo avec une légende dans LiveChat")
                    .AddOption("texte", ApplicationCommandOptionType.String, "Texte de la légende", isRequired: true, maxLength: MessageParser.MaxCaptionLength)
                    .AddOption("media", ApplicationCommandOptionType.Attachment, "Image, GIF ou vidéo à afficher", isRequired: true)
                    .Build(),
                new SlashCommandBuilder()
                    .WithName("skip")
                    .WithDescription("Passe le média en cours d'affichage")
                    .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
                    .Build(),
                new SlashCommandBuilder()
                    .WithName("stop")
                    .WithDescription("Arrête l'affichage et vide la file d'attente")
                    .WithDefaultMemberPermissions(GuildPermission.ManageMessages)
                    .Build(),
            };

            // Commandes de serveur : disponibles immédiatement, et « bulk overwrite » est idempotent.
            await guild.BulkOverwriteApplicationCommandAsync(commands);

            // La v1 enregistrait /caption en commande globale à chaque connexion : on la supprime.
            foreach (var legacy in (await client.GetGlobalApplicationCommandsAsync()).Where(c => c.Name == "caption"))
            {
                await legacy.DeleteAsync();
            }

            _commandsRegistered = true;
            Log.Info($"Commandes slash enregistrées sur le serveur {guild.Name}.");
        }
        catch (Exception ex)
        {
            Log.Warn("Impossible d'enregistrer les commandes slash (le bot a-t-il le scope applications.commands ?).", ex);
        }
    }

    private static IncomingMessage ToIncoming(SocketMessage message, bool embedsOnly)
    {
        var sources = WithForwardedMessages(message);

        var attachments = sources
            .SelectMany(m => m.Attachments)
            .Select(a => new AttachmentInfo(a.Url, a.Filename, a.ContentType))
            .ToList();

        var embeds = sources
            .SelectMany(m => m.Embeds)
            .Select(e => new EmbedInfo(
                e.Type.ToString(),
                e.Url,
                e.Image?.ProxyUrl ?? e.Image?.Url,
                e.Thumbnail?.ProxyUrl ?? e.Thumbnail?.Url,
                e.Video?.Url,
                e.Provider.HasValue))
            .ToList();

        var stickers = sources
            .SelectMany(m => m.Stickers ?? [])
            .Select(ToSticker)
            .OfType<StickerInfo>()
            .ToList();

        // Message transféré sans commentaire : le texte d'origine sert de légende.
        var content = message.CleanContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            content = sources.Skip(1).Select(m => m.Content).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        }

        return new IncomingMessage(
            message.Id,
            DisplayName(message.Author),
            AvatarUrl(message.Author),
            content ?? "",
            attachments,
            embeds)
        {
            EmbedsOnly = embedsOnly,
            Stickers = stickers,
        };
    }

    /// <summary>
    /// Le message et, s'il s'agit d'un transfert (bouton « Transférer » de Discord), le message d'origine :
    /// ses médias ne sont pas dans les pièces jointes du transfert mais dans un instantané.
    /// </summary>
    private static List<IMessage> WithForwardedMessages(SocketMessage message)
    {
        var sources = new List<IMessage> { message };
        try
        {
            if (message is SocketUserMessage { ForwardedMessages.Count: > 0 } forward)
            {
                sources.AddRange(forward.ForwardedMessages.Select(f => f.Message).Where(m => m is not null));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Lecture du message transféré impossible.", ex);
        }

        return sources;
    }

    /// <summary>
    /// URL d'un sticker affichable. Discord sert les stickers GIF uniquement depuis media.discordapp.net ;
    /// les stickers Lottie (animations vectorielles JSON) ne peuvent pas être affichés.
    /// </summary>
    private static StickerInfo? ToSticker(IStickerItem sticker) => sticker.Format switch
    {
        StickerFormatType.Png or StickerFormatType.Apng
            => new StickerInfo($"https://media.discordapp.net/stickers/{sticker.Id}.png", sticker.Name, IsAnimated: false),
        StickerFormatType.Gif
            => new StickerInfo($"https://media.discordapp.net/stickers/{sticker.Id}.gif", sticker.Name, IsAnimated: true),
        _ => null,
    };

    private static string DisplayName(IUser user) =>
        (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username;

    private static string AvatarUrl(IUser user) =>
        (user as IGuildUser)?.GetDisplayAvatarUrl(size: 64) ?? user.GetAvatarUrl(size: 64) ?? user.GetDefaultAvatarUrl();
}
