using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using LiveChat.Configuration;
using LiveChat.Services;

namespace LiveChat.Views;

/// <summary>Paramètres (et configuration initiale) : remplace la SetupWindow de la v1.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;

    public SettingsWindow(AppSettings current, bool firstRun, string? message = null)
    {
        InitializeComponent();
        _original = current;

        // Hauteur adaptée à l'écran : le contenu défile sur les petits écrans (portables 768p...).
        MaxHeight = Math.Min(760, SystemParameters.WorkArea.Height - 20);

        HeaderTitle.Text = firstRun ? "Bienvenue dans LiveChat" : "Paramètres";
        VersionText.Text = firstRun
            ? "Configurez votre bot Discord pour commencer."
            : $"Version {UpdateService.CurrentVersion.ToString(3)}";
        CancelButton.Content = firstRun ? "Quitter" : "Annuler";

        TokenHint.Text = string.IsNullOrEmpty(current.BotToken)
            ? "Le token est chiffré pour votre compte Windows et n'est jamais envoyé ailleurs qu'à Discord."
            : "Un token est déjà enregistré (chiffré). Laissez vide pour le conserver.";
        ChannelBox.Text = current.ChannelId == 0 ? "" : current.ChannelId.ToString();

        // Bornes identiques à celles des paramètres : avant, une valeur plus haute que le curseur (60 s d'image,
        // 600 s de vidéo, 100 médias) était silencieusement réduite au premier enregistrement.
        ImageDurationSlider.Minimum = AppSettings.MinImageDuration;
        ImageDurationSlider.Maximum = AppSettings.MaxImageDuration;
        VideoDurationSlider.Minimum = AppSettings.MinVideoDuration;
        VideoDurationSlider.Maximum = AppSettings.MaxVideoDuration;
        QueueSlider.Minimum = AppSettings.MinQueueLength;
        QueueSlider.Maximum = AppSettings.MaxQueueLengthLimit;

        ImageDurationSlider.Value = current.ImageDurationSeconds;
        VideoDurationSlider.Value = current.MaxVideoDurationSeconds;
        VolumeSlider.Value = Math.Round(current.Volume * 100);
        QueueSlider.Value = current.MaxQueueLength;

        ShowMessageTextBox.IsChecked = current.ShowMessageText;
        ShowTextOnlyBox.IsChecked = current.ShowTextOnlyMessages;
        BlurSpoilersBox.IsChecked = current.BlurSpoilers;
        PlayAudioBox.IsChecked = current.PlayAudio;
        KeepVisibleBox.IsChecked = current.KeepWindowVisible;
        StartWithWindowsBox.IsChecked = current.StartWithWindows;
        CheckUpdatesBox.IsChecked = current.CheckForUpdates;

        StopHotkeyBox.Hotkey = current.StopHotkey;
        SkipHotkeyBox.Hotkey = current.SkipHotkey;
        ReplayHotkeyBox.Hotkey = current.ReplayHotkey;

        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowError(message);
        }

        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(current.BotToken))
            {
                TokenBox.Focus();
            }
        };
    }

    /// <summary>Paramètres validés (null si annulé).</summary>
    public AppSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var token = SettingsValidation.NormalizeToken(TokenBox.Password);
        if (token.Length == 0)
        {
            token = _original.BotToken;
        }
        else if (!SettingsValidation.IsBotTokenFormat(token))
        {
            ShowError("Ce n'est pas un Bot Token : copiez-le dans l'onglet « Bot » (bouton Reset Token), pas le Client Secret de l'onglet OAuth2.");
            TokenBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            ShowError("Le Bot Token est requis.");
            TokenBox.Focus();
            return;
        }

        if (!SettingsValidation.TryParseChannelId(ChannelBox.Text, out var channelId))
        {
            ShowError("Salon invalide : collez l'ID (17 à 20 chiffres) ou le lien du salon.");
            ChannelBox.Focus();
            ChannelBox.SelectAll();
            return;
        }

        var hotkeys = new[] { StopHotkeyBox.Hotkey, SkipHotkeyBox.Hotkey, ReplayHotkeyBox.Hotkey }.OfType<Hotkey>().ToList();
        if (hotkeys.Count != hotkeys.Distinct().Count())
        {
            ShowError("Les raccourcis STOP, Passer et Rejouer doivent être différents.");
            return;
        }

        var settings = _original.Clone();
        settings.BotToken = token;
        settings.ChannelId = channelId;
        settings.ImageDurationSeconds = (int)ImageDurationSlider.Value;
        settings.MaxVideoDurationSeconds = (int)VideoDurationSlider.Value;
        settings.Volume = VolumeSlider.Value / 100;
        settings.MaxQueueLength = (int)QueueSlider.Value;
        settings.ShowMessageText = ShowMessageTextBox.IsChecked == true;
        settings.ShowTextOnlyMessages = ShowTextOnlyBox.IsChecked == true;
        settings.BlurSpoilers = BlurSpoilersBox.IsChecked == true;
        settings.PlayAudio = PlayAudioBox.IsChecked == true;
        settings.KeepWindowVisible = KeepVisibleBox.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
        settings.CheckForUpdates = CheckUpdatesBox.IsChecked == true;
        settings.StopHotkey = StopHotkeyBox.Hotkey;
        settings.SkipHotkey = SkipHotkeyBox.Hotkey;
        settings.ReplayHotkey = ReplayHotkeyBox.Hotkey;
        settings.Normalize();

        Result = settings;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
