using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LiveChat.Configuration;
using LiveChat.Models;
using LiveChat.Services;
using LiveChat.Tray;
using LiveChat.Views;
using Forms = System.Windows.Forms;

namespace LiveChat;

/// <summary>
/// Point d'entrée : instance unique, chargement des paramètres, puis orchestration entre Discord,
/// la fenêtre d'incrustation, l'icône de notification, les raccourcis et les mises à jour.
/// </summary>
public partial class App : Application
{
    private const string InstanceId = "LiveChat-8B1F6C2E-4D7A-4C55-9E0B-2F3A6D9C1E47";

    /// <summary>Un dossier de données distinct (LIVECHAT_DATA_DIR) correspond à une instance distincte.</summary>
    private static readonly string InstanceKey = AppPaths.IsDataDirectoryOverridden
        ? $"{InstanceId}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDirectory.ToUpperInvariant())))[..16]}"
        : InstanceId;

    /// <summary>LiveChat reste souvent ouvert des jours entiers (démarrage avec Windows) : on revérifie régulièrement.</summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateService _updates = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = UpdateCheckInterval };

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;

    private SettingsStore _store = null!;
    private AppSettings _settings = null!;
    private DiscordService _discord = null!;
    private PipWindow _pip = null!;
    private TrayIcon _tray = null!;
    private GlobalHotkeyManager _hotkeys = null!;

    private UpdateInfo? _pendingUpdate;
    private bool _paused;
    private bool _settingsOpen;
    private bool _hasConnectedOnce;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Initialize();
        RegisterExceptionHandlers();

        var updated = e.Args.Contains("--updated", StringComparer.OrdinalIgnoreCase);
        if (!AcquireSingleInstance(waitForPreviousInstance: updated))
        {
            Shutdown();
            return;
        }

        Log.Info($"LiveChat {UpdateService.CurrentVersion.ToString(3)} démarré ({AppPaths.AppDirectory}).");
        UpdateService.CleanupPreviousUpdate();
        VideoCache.CleanupStaleFiles();

        _store = new SettingsStore(AppPaths.SettingsFile, AppPaths.LegacyConfigFile);
        _settings = _store.Load();
        _saveTimer.Tick += (_, _) => SaveNow();

        CreatePipWindow();
        CreateTrayIcon();
        CreateDiscordService();
        _hotkeys = new GlobalHotkeyManager();
        RegisterHotkeys();

        EnqueueCommandLinePreviews(e.Args);

        if (!_settings.IsConfigured)
        {
            if (!ShowSettings(firstRun: true))
            {
                ExitApplication();
                return;
            }
        }
        else
        {
            StartDiscord();
        }

        if (_settings.StartWithWindows)
        {
            StartupRegistration.Apply(true); // Met à jour le chemin si l'application a été déplacée.
        }

        if (updated)
        {
            _tray.Notify("LiveChat a été mis à jour", $"Version {UpdateService.CurrentVersion.ToString(3)} installée.");
        }

        if (_settings.CheckForUpdates)
        {
            _ = CheckForUpdatesAsync(interactive: false, delay: TimeSpan.FromSeconds(5));
        }

        _updateTimer.Tick += (_, _) =>
        {
            if (_settings.CheckForUpdates)
            {
                _ = CheckForUpdatesAsync(interactive: false, delay: TimeSpan.Zero);
            }
        };
        _updateTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex non possédé (seconde instance).
            }

            _instanceMutex.Dispose();
        }

        Log.Info("LiveChat arrêté.");
        base.OnExit(e);
    }

    // ───────────────────────────── Initialisation ─────────────────────────────

    private void CreatePipWindow()
    {
        _pip = new PipWindow();
        _pip.RestorePlacement(_settings.Window);
        _pip.ApplySettings(_settings);
        _pip.SetPaused(_paused);

        _pip.VolumeChanged += volume =>
        {
            _settings.Volume = volume;
            ScheduleSave();
        };
        _pip.KeepVisibleChanged += keepVisible =>
        {
            _settings.KeepWindowVisible = keepVisible;
            ScheduleSave();
        };
        _pip.PlacementChanged += placement =>
        {
            _settings.Window = placement;
            ScheduleSave();
        };
    }

    private void CreateTrayIcon()
    {
        _tray = new TrayIcon(LoadTrayIcon());
        _tray.ToggleWindowRequested += () => _pip.ToggleWindow();
        _tray.SkipRequested += () => _pip.Skip();
        _tray.ReplayRequested += ReplayLast;
        _tray.StopRequested += () => _pip.StopAll();
        _tray.PauseToggled += SetPaused;
        _tray.TestRequested += () => _pip.Enqueue([TestCard.Create()]);
        _tray.SettingsRequested += () => ShowSettings(firstRun: false);
        _tray.CheckUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true, delay: TimeSpan.Zero);
        _tray.InstallUpdateRequested += ShowUpdateWindow;
        _tray.OpenLogsRequested += OpenLogFolder;
        _tray.ExitRequested += ExitApplication;
    }

    private void CreateDiscordService()
    {
        _discord = new DiscordService();

        // Les événements arrivent sur les threads de Discord.Net : InvokeAsync ne bloque pas le gateway
        // (la v1 utilisait Dispatcher.Invoke, bloquant).
        _discord.StatusChanged += status => Dispatcher.InvokeAsync(() => OnDiscordStatus(status));
        _discord.MessageReceived += message => Dispatcher.InvokeAsync(() => OnDiscordMessage(message));
        _discord.CommandReceived += command => Dispatcher.InvokeAsync(() =>
        {
            if (command == RemoteCommand.Skip)
            {
                _pip.Skip();
            }
            else
            {
                _pip.StopAll();
            }
        });
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/LiveChat.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    // ───────────────────────────── Discord ─────────────────────────────

    private void StartDiscord()
    {
        _hasConnectedOnce = false;
        _ = _discord.StartAsync(_settings.BotToken, _settings.ChannelId);
    }

    private void OnDiscordStatus(ConnectionStatus status)
    {
        _pip.SetStatus(status);
        _tray.SetStatus(status.Message);

        switch (status.State)
        {
            case ConnectionState.Connected:
                _hasConnectedOnce = true;
                break;

            case ConnectionState.Error when !_settingsOpen:
                if (!_hasConnectedOnce)
                {
                    // Erreur dès la connexion (token, intents) : on ouvre directement les paramètres.
                    ShowSettings(firstRun: false, status.Message);
                }
                else
                {
                    _tray.Notify("LiveChat — action requise", status.Message, Forms.ToolTipIcon.Warning,
                        () => ShowSettings(firstRun: false, status.Message));
                }

                break;
        }
    }

    private void OnDiscordMessage(IncomingMessage message)
    {
        if (_paused)
        {
            return;
        }

        var options = new ParserOptions(_settings.ShowMessageText, _settings.ShowTextOnlyMessages, _settings.BlurSpoilers);
        var items = MessageParser.Parse(message, options);
        if (MessageParser.Describe(message, items) is { } summary)
        {
            Log.Info(summary); // Pour comprendre pourquoi un média ne s'est pas affiché.
        }

        if (items.Count > 0)
        {
            _pip.Enqueue(items);
        }
    }

    private void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        _discord.IsPaused = paused;
        _pip.SetPaused(paused);
        _tray.SetPaused(paused);
        if (paused)
        {
            _pip.StopAll();
        }

        Log.Info(paused ? "Réception mise en pause." : "Réception reprise.");
    }

    // ───────────────────────────── Paramètres ─────────────────────────────

    /// <returns>Vrai si les paramètres ont été enregistrés.</returns>
    private bool ShowSettings(bool firstRun, string? message = null)
    {
        if (_settingsOpen)
        {
            Windows.OfType<SettingsWindow>().FirstOrDefault()?.Activate();
            return false;
        }

        _settingsOpen = true;
        _hotkeys.UnregisterAll(); // Permet de capturer une combinaison déjà utilisée par LiveChat.
        try
        {
            var window = new SettingsWindow(_settings, firstRun, message);
            if (window.ShowDialog() != true || window.Result is not { } updated)
            {
                return false;
            }

            var reconnect = updated.BotToken != _settings.BotToken
                || updated.ChannelId != _settings.ChannelId
                || _discord.Status.State is ConnectionState.Error or ConnectionState.Disconnected;
            var startupChanged = updated.StartWithWindows != _settings.StartWithWindows;

            updated.Window = _pip.Placement;
            _settings = updated;
            SaveNow();
            _pip.ApplySettings(_settings);

            if (startupChanged)
            {
                StartupRegistration.Apply(_settings.StartWithWindows);
            }

            if (reconnect)
            {
                StartDiscord();
            }

            return true;
        }
        finally
        {
            _settingsOpen = false;
            RegisterHotkeys();
        }
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failures = new List<string>();

        if (_settings.StopHotkey is { } stop && !_hotkeys.Register(stop, () => _pip.StopAll()))
        {
            failures.Add($"STOP ({stop})");
        }

        if (_settings.SkipHotkey is { } skip && !_hotkeys.Register(skip, () => _pip.Skip()))
        {
            failures.Add($"Passer ({skip})");
        }

        if (_settings.ReplayHotkey is { } replay && !_hotkeys.Register(replay, ReplayLast))
        {
            failures.Add($"Rejouer ({replay})");
        }

        if (failures.Count > 0)
        {
            _tray.Notify("Raccourci indisponible",
                $"{string.Join(", ", failures)} : déjà utilisé par une autre application. Choisissez-en un autre dans les paramètres.",
                Forms.ToolTipIcon.Warning, () => ShowSettings(firstRun: false));
        }
    }

    private void ReplayLast()
    {
        if (!_pip.Replay())
        {
            _tray.Notify("Rien à rejouer", "Aucun média n'a encore été affiché depuis le lancement de LiveChat.");
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Impossible d'enregistrer les paramètres.", ex);
        }
    }

    // ───────────────────────────── Mises à jour ─────────────────────────────

    private async Task CheckForUpdatesAsync(bool interactive, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            var update = await _updates.CheckAsync();
            if (update is null)
            {
                if (interactive)
                {
                    _tray.Notify("LiveChat est à jour", $"Vous utilisez la dernière version ({UpdateService.CurrentVersion.ToString(3)}).");
                }

                return;
            }

            // Vérification périodique : on ne renotifie pas une version déjà signalée.
            var alreadyNotified = _pendingUpdate?.Version == update.Version;
            _pendingUpdate = update;
            _tray.SetUpdateAvailable(update.Version.ToString(3));
            if (alreadyNotified && !interactive)
            {
                return;
            }

            Log.Info($"Mise à jour disponible : {update.TagName}.");

            if (interactive)
            {
                ShowUpdateWindow();
            }
            else
            {
                _tray.Notify("Mise à jour disponible",
                    $"LiveChat {update.Version.ToString(3)} est disponible. Cliquez ici pour l'installer.",
                    onClick: ShowUpdateWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Vérification des mises à jour impossible.", ex);
            if (interactive)
            {
                _tray.Notify("Vérification impossible", "Impossible de contacter GitHub. Réessayez plus tard.", Forms.ToolTipIcon.Warning);
            }
        }
    }

    private void ShowUpdateWindow()
    {
        if (_pendingUpdate is null || Windows.OfType<UpdateWindow>().Any())
        {
            return;
        }

        var window = new UpdateWindow(_pendingUpdate, _updates);
        if (window.ShowDialog() == true && window.Installed)
        {
            ExitApplication();
        }
    }

    // ───────────────────────────── Divers ─────────────────────────────

    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _updateTimer.Stop();
        if (_pip is not null)
        {
            if (_settings is not null)
            {
                _settings.Window = _pip.Placement;
            }

            _pip.AllowClose = true;
            _pip.Close();
        }

        if (_store is not null && _settings is not null)
        {
            SaveNow();
        }

        _hotkeys?.Dispose();
        _tray?.Dispose();

        if (_discord is not null)
        {
            // Déconnexion propre, sans bloquer plus de 3 secondes.
            Task.Run(() => _discord.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
        }

        Shutdown();
    }

    private bool AcquireSingleInstance(bool waitForPreviousInstance)
    {
        _instanceMutex = new Mutex(false, $@"Local\{InstanceKey}");
        bool acquired;
        try
        {
            // Après une mise à jour, l'ancienne instance est peut-être encore en train de se fermer.
            acquired = _instanceMutex.WaitOne(waitForPreviousInstance ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        // L'ancienne version ne s'est pas fermée (bloquée pendant sa fermeture) : on l'arrête pour pouvoir démarrer.
        if (!acquired && waitForPreviousInstance && PreviousInstance.TerminateStuck(AppPaths.AppDirectory))
        {
            try
            {
                acquired = _instanceMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
        }

        if (!acquired)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;

            // Demande à l'instance déjà lancée d'afficher sa fenêtre.
            if (EventWaitHandle.TryOpenExisting($@"Local\{InstanceKey}-activate", out var existing))
            {
                existing.Set();
                existing.Dispose();
            }

            return false;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceKey}-activate");
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
            Dispatcher.InvokeAsync(() =>
            {
                _pip.ShowWindow();
                _tray.Notify("LiveChat est déjà lancé", "Retrouvez-le dans la zone de notification, près de l'horloge.");
            }), null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Erreur inattendue dans l'interface.", e.Exception);
            e.Handled = true; // Ne pas faire planter l'incrustation pour une erreur ponctuelle.
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Erreur fatale.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Erreur dans une tâche en arrière-plan.", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// --test : affiche la carte de test ; --preview &lt;fichier|url&gt; (répétable) : affiche un média local
    /// ou distant, pratique pour vérifier qu'un format est bien lu par Windows.
    /// </summary>
    private void EnqueueCommandLinePreviews(string[] args)
    {
        var items = new List<MediaItem>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(TestCard.Create());
            }
            else if (args[i].Equals("--preview", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var target = args[++i];
                if (TestCard.FromPathOrUrl(target) is { } item)
                {
                    items.Add(item);
                }
                else
                {
                    Log.Warn($"Aperçu impossible : « {target} » n'est pas une image ou une vidéo accessible.");
                }
            }
        }

        if (items.Count > 0)
        {
            _pip.Enqueue(items);
        }
    }
}
