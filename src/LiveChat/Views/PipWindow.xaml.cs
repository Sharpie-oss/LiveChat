using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveChat.Configuration;
using LiveChat.Models;
using LiveChat.Services;
using WpfAnimatedGif;

namespace LiveChat.Views;

/// <summary>
/// Fenêtre d'incrustation : affiche les médias un par un depuis la file d'attente.
/// Chaque affichage reçoit un identifiant ; les minuteurs et événements d'un affichage terminé
/// sont ignorés (corrige les « fantômes » de la v1 après un STOP).
/// </summary>
public partial class PipWindow : Window
{
    private const long MaxImageBytes = 50L * 1024 * 1024;
    private const int MaxDecodeWidth = 1920;
    private static readonly TimeSpan VideoOpenTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StallMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FileCardDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ErrorCardDuration = TimeSpan.FromSeconds(4);

    private enum PlaybackMode
    {
        None,
        Timed,
        Video,
    }

    private readonly MediaQueue _queue = new();
    private readonly DispatcherTimer _ticker;
    private readonly DispatcherTimer _toastTimer;

    private MediaItem? _current;
    private int _presentationId;
    private PlaybackMode _mode;
    private DateTime _startedAt;
    private TimeSpan _duration;
    private MediaElement? _video;
    private string? _tempVideoPath;
    private DateTime? _videoOpenedAt;
    private bool _looping;
    private CancellationTokenSource? _loadCts;

    private double _volume = 0.5;
    private bool _keepVisible;
    private bool _paused;
    private bool _updatingVolumeUi;
    private TimeSpan _imageDuration = TimeSpan.FromSeconds(6);
    private TimeSpan _maxVideoDuration = TimeSpan.FromSeconds(60);
    private ConnectionStatus _status = new(ConnectionState.Disconnected, "Non connecté");

    public PipWindow()
    {
        InitializeComponent();

        _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _ticker.Tick += (_, _) => OnTick();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
        };

        SourceInitialized += (_, _) => HideFromAltTab();
        PreviewMouseWheel += OnPreviewMouseWheel;
        MediaPanel.PreviewMouseLeftButtonUp += (_, _) => RevealSpoiler();
        LocationChanged += (_, _) => RaisePlacementChanged();
        SizeChanged += (_, _) => RaisePlacementChanged();

        UpdateVolumeUi();
        UpdateStatusUi();
    }

    public event Action<double>? VolumeChanged;

    public event Action<bool>? KeepVisibleChanged;

    public event Action<WindowPlacement>? PlacementChanged;

    /// <summary>Autorise la fermeture réelle (sinon la fenêtre est seulement masquée).</summary>
    public bool AllowClose { get; set; }

    public int QueueCount => _queue.Count;

    public bool IsPresenting => _current is not null;

    public WindowPlacement Placement => new(Left, Top, Width, Height);

    public void ApplySettings(AppSettings settings)
    {
        _imageDuration = TimeSpan.FromSeconds(settings.ImageDurationSeconds);
        _maxVideoDuration = TimeSpan.FromSeconds(settings.MaxVideoDurationSeconds);
        _queue.Capacity = settings.MaxQueueLength;
        SetVolume(settings.Volume, raiseEvent: false);
        SetKeepVisible(settings.KeepWindowVisible, raiseEvent: false);
    }

    /// <summary>Restaure la position enregistrée, ou place la fenêtre en bas à droite de l'écran principal.</summary>
    public void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is { } p && IsReachable(p))
        {
            Width = Math.Max(MinWidth, p.Width);
            Height = Math.Max(MinHeight, p.Height);
            Left = p.Left;
            Top = p.Top;
            return;
        }

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
    }

    public void Enqueue(IReadOnlyList<MediaItem> items)
    {
        var dropped = _queue.Enqueue(items);
        if (dropped > 0)
        {
            Log.Warn($"File d'attente pleine : {dropped} média(s) ignoré(s).");
        }

        UpdateQueueBadge();
        ShowNext();
    }

    /// <summary>Passe au média suivant.</summary>
    public void Skip()
    {
        if (_current is not null)
        {
            Finish(_presentationId);
        }
    }

    /// <summary>Arrête le média en cours et vide la file d'attente.</summary>
    public void StopAll()
    {
        _queue.Clear();
        if (_current is not null)
        {
            TearDown();
            _current = null;
        }

        EnterIdle();
    }

    public void SetStatus(ConnectionStatus status)
    {
        _status = status;
        UpdateStatusUi();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        UpdateStatusUi();
    }

    public void ShowWindow()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
    }

    public void HideWindow()
    {
        VolumePopup.IsOpen = false;
        Hide();
    }

    public void ToggleWindow()
    {
        if (IsVisible)
        {
            HideWindow();
        }
        else
        {
            ShowWindow();
        }
    }

    public void SetVolume(double volume, bool raiseEvent)
    {
        _volume = Math.Clamp(Math.Round(volume, 2), 0, 1);
        if (_video is not null)
        {
            _video.Volume = _volume;
        }

        UpdateVolumeUi();
        if (raiseEvent)
        {
            VolumeChanged?.Invoke(_volume);
        }
    }

    public void SetKeepVisible(bool keepVisible, bool raiseEvent)
    {
        _keepVisible = keepVisible;
        PinToggle.IsChecked = keepVisible;
        PinToggle.Content = keepVisible ? "" : "";

        if (keepVisible)
        {
            ShowWindow();
        }
        else if (_current is null && IsVisible && raiseEvent)
        {
            HideWindow();
        }

        if (raiseEvent)
        {
            KeepVisibleChanged?.Invoke(keepVisible);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }

        TearDown();
        base.OnClosing(e);
    }

    // ───────────────────────────── Affichage ─────────────────────────────

    private void ShowNext()
    {
        if (_current is not null)
        {
            return;
        }

        if (_queue.TryDequeue(out var item))
        {
            _ = PresentAsync(item);
        }
        else
        {
            EnterIdle();
        }
    }

    private async Task PresentAsync(MediaItem item)
    {
        _current = item;
        var id = ++_presentationId;

        UpdateQueueBadge();
        SkipButton.IsEnabled = true;
        IdlePanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Visible;
        AuthorText.Text = item.Author;
        SetAvatar(item.AvatarUrl);
        CaptionText.Text = item.Caption ?? "";
        CaptionBorder.Visibility = string.IsNullOrWhiteSpace(item.Caption) ? Visibility.Collapsed : Visibility.Visible;
        SpoilerBadge.Visibility = item.IsSpoiler ? Visibility.Visible : Visibility.Collapsed;
        SetProgress(0, visible: false);

        if (!IsVisible)
        {
            Show();
        }

        try
        {
            switch (item.Kind)
            {
                case MediaKind.Image:
                case MediaKind.AnimatedImage:
                    var cts = _loadCts = new CancellationTokenSource();
                    var image = await CreateImageAsync(item, cts.Token);
                    if (id != _presentationId)
                    {
                        return;
                    }

                    MediaHost.Children.Add(image);
                    StartTimed(_imageDuration);
                    break;

                case MediaKind.Video:
                case MediaKind.LoopingVideo:
                    var videoCts = _loadCts = new CancellationTokenSource();
                    var source = await PrepareVideoAsync(item, id, videoCts.Token);
                    if (id != _presentationId)
                    {
                        // Passé pendant le téléchargement : on ne garde pas le fichier temporaire.
                        if (VideoCache.IsTemporary(source))
                        {
                            VideoCache.TryDelete(source);
                        }

                        return;
                    }

                    StartVideo(item, id, source);
                    break;

                case MediaKind.Text:
                    MediaHost.Children.Add(CreateTextBlock(item.Text ?? ""));
                    StartTimed(ReadingTime(item.Text ?? ""));
                    break;

                default:
                    MediaHost.Children.Add(CreateNotice("", item.FileName ?? "Fichier"));
                    StartTimed(FileCardDuration);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Une annulation volontaire (Passer / STOP) a déjà changé l'identifiant d'affichage.
            if (id == _presentationId)
            {
                Log.Warn($"Affichage impossible de {item.Url ?? item.Kind.ToString()}.", ex);
                ShowError(id, ex is InvalidDataException
                    ? "Média trop volumineux pour être affiché."
                    : "Impossible de charger ce média (lien expiré ou inaccessible ?).");
            }
        }
    }

    /// <summary>Remplace le média par un court message d'erreur, au lieu de le sauter sans explication.</summary>
    private void ShowError(int id, string message)
    {
        if (id != _presentationId)
        {
            return;
        }

        _ticker.Stop();
        if (_video is not null)
        {
            _video.Stop();
            _video.Close();
            _video.Source = null;
            _video = null;
        }

        MediaHost.Children.Clear();
        CaptionBorder.Visibility = Visibility.Collapsed;
        SpoilerBadge.Visibility = Visibility.Collapsed;
        MediaHost.Children.Add(CreateNotice("", message));
        StartTimed(ErrorCardDuration);
    }

    private void StartTimed(TimeSpan duration)
    {
        _mode = PlaybackMode.Timed;
        _duration = duration;
        _startedAt = DateTime.UtcNow;
        SetProgress(0, visible: true);
        _ticker.Start();
    }

    /// <summary>
    /// Renvoie le fichier local à lire : les vidéos distantes sont d'abord téléchargées (voir <see cref="VideoCache"/>),
    /// avec un indicateur de chargement et la progression du téléchargement.
    /// </summary>
    private async Task<string> PrepareVideoAsync(MediaItem item, int id, CancellationToken cancellationToken)
    {
        var uri = new Uri(item.Url!, UriKind.Absolute);
        if (uri.IsFile)
        {
            return uri.LocalPath;
        }

        var loading = CreateLoadingText();
        MediaHost.Children.Add(loading);
        SetProgress(0, visible: true);

        var path = VideoCache.CreateTempPath(item.Url!);
        _tempVideoPath = path;
        var progress = new Progress<double>(value =>
        {
            if (id == _presentationId)
            {
                SetProgress(value, visible: true);
                loading.Text = $"Chargement de la vidéo… {value:P0}";
            }
        });

        await HttpClients.DownloadToFileAsync(item.Url!, path, VideoCache.MaxVideoBytes, progress, cancellationToken);
        MediaHost.Children.Remove(loading);
        return path;
    }

    private TextBlock CreateLoadingText() => new()
    {
        Text = "Chargement de la vidéo…",
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(24),
        Foreground = (Brush)FindResource("MutedTextBrush"),
    };

    private void StartVideo(MediaItem item, int id, string localPath)
    {
        _looping = item.Kind == MediaKind.LoopingVideo;
        _videoOpenedAt = null;

        var video = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            Stretch = Stretch.Uniform,
            Volume = _volume,
            IsMuted = _looping, // Les GIF Tenor/Giphy n'ont pas de son.
            ScrubbingEnabled = false,
        };

        video.MediaOpened += (_, _) =>
        {
            if (id == _presentationId)
            {
                _videoOpenedAt = DateTime.UtcNow;
            }
        };
        video.MediaEnded += (_, _) => OnVideoEnded(video, id);
        video.MediaFailed += (_, e) =>
        {
            if (id == _presentationId)
            {
                Log.Warn($"Lecture impossible de {item.Url}.", e.ErrorException);
                var format = VideoCache.GetExtension(item.Url!).TrimStart('.').ToUpperInvariant();
                ShowError(id, $"Vidéo illisible : le format {format} n'est pas pris en charge par le lecteur de Windows.");
            }
        };
        video.Loaded += (_, _) =>
        {
            if (id == _presentationId)
            {
                video.Play();
            }
        };

        ApplySpoiler(video, item);
        video.Source = new Uri(localPath, UriKind.Absolute);
        _video = video;
        MediaHost.Children.Add(video);

        _mode = PlaybackMode.Video;
        _startedAt = DateTime.UtcNow;
        SetProgress(0, visible: true);
        _ticker.Start();
    }

    private void OnVideoEnded(MediaElement video, int id)
    {
        if (id != _presentationId)
        {
            return;
        }

        if (_looping && DateTime.UtcNow - (_videoOpenedAt ?? _startedAt) < LoopTarget())
        {
            video.Position = TimeSpan.Zero;
            video.Play();
            return;
        }

        Finish(id);
    }

    /// <summary>Durée d'affichage d'un GIF vidéo : au moins une boucle complète.</summary>
    private TimeSpan LoopTarget()
    {
        var natural = _video?.NaturalDuration is { HasTimeSpan: true } d ? d.TimeSpan : TimeSpan.Zero;
        var target = natural > _imageDuration ? natural : _imageDuration;
        return target < _maxVideoDuration ? target : _maxVideoDuration;
    }

    private void OnTick()
    {
        var id = _presentationId;
        var now = DateTime.UtcNow;

        switch (_mode)
        {
            case PlaybackMode.Timed:
            {
                var fraction = (now - _startedAt) / _duration;
                SetProgress(fraction, visible: true);
                if (fraction >= 1)
                {
                    Finish(id);
                }

                break;
            }

            case PlaybackMode.Video when _video is not null:
            {
                if (_videoOpenedAt is null)
                {
                    if (now - _startedAt > VideoOpenTimeout)
                    {
                        Log.Warn($"La vidéo ne s'ouvre pas ({_current?.Url}), passage au média suivant.");
                        Finish(id);
                    }

                    return;
                }

                var played = now - _videoOpenedAt.Value;
                if (_looping)
                {
                    var target = LoopTarget();
                    SetProgress(played / target, visible: true);
                    if (played >= target)
                    {
                        Finish(id);
                    }

                    return;
                }

                var limit = _video.NaturalDuration.HasTimeSpan && _video.NaturalDuration.TimeSpan < _maxVideoDuration
                    ? _video.NaturalDuration.TimeSpan
                    : _maxVideoDuration;
                SetProgress(limit > TimeSpan.Zero ? _video.Position / limit : 0, visible: true);

                // Durée maximale atteinte, ou flux bloqué (la position n'avance plus).
                if (_video.Position >= _maxVideoDuration || played > limit + StallMargin)
                {
                    Finish(id);
                }

                break;
            }
        }
    }

    private void Finish(int id)
    {
        if (id != _presentationId || _current is null)
        {
            return;
        }

        TearDown();
        _current = null;
        ShowNext();
    }

    private void TearDown()
    {
        _presentationId++;
        _ticker.Stop();
        _mode = PlaybackMode.None;
        _loadCts?.Cancel();
        _loadCts = null;

        if (_video is not null)
        {
            _video.Stop();
            _video.Close();
            _video.Source = null;
            _video = null;
        }

        if (_tempVideoPath is { } tempVideo)
        {
            _tempVideoPath = null;
            _ = DeleteTempVideoAsync(tempVideo);
        }

        foreach (var image in MediaHost.Children.OfType<Image>())
        {
            ImageBehavior.SetAnimatedSource(image, null);
        }

        MediaHost.Children.Clear();
        CaptionBorder.Visibility = Visibility.Collapsed;
        SpoilerBadge.Visibility = Visibility.Collapsed;
        SetProgress(0, visible: false);
    }

    /// <summary>Le lecteur libère le fichier de façon asynchrone : on réessaie quelques secondes plus tard.</summary>
    private static async Task DeleteTempVideoAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (VideoCache.TryDelete(path))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private void EnterIdle()
    {
        _mode = PlaybackMode.None;
        _ticker.Stop();
        MediaPanel.Visibility = Visibility.Collapsed;
        IdlePanel.Visibility = Visibility.Visible;
        SkipButton.IsEnabled = false;
        UpdateQueueBadge();
        SetProgress(0, visible: false);

        if (!_keepVisible && IsVisible)
        {
            HideWindow();
        }
    }

    // ───────────────────────────── Création des éléments ─────────────────────────────

    private static async Task<FrameworkElement> CreateImageAsync(MediaItem item, CancellationToken cancellationToken)
    {
        var data = await HttpClients.GetBytesAsync(item.Url!, MaxImageBytes, cancellationToken);
        var image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        if (item.Kind == MediaKind.AnimatedImage)
        {
            // WPF n'anime pas les GIF nativement (ils restaient figés dans la v1).
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = new MemoryStream(data, writable: false);
            source.EndInit();
            ImageBehavior.SetRepeatBehavior(image, RepeatBehavior.Forever);
            ImageBehavior.SetAnimatedSource(image, source);
        }
        else
        {
            // Décodage hors du thread de l'interface pour ne pas la figer sur les grosses images.
            image.Source = await Task.Run(() => DecodeStill(data), cancellationToken);
        }

        ApplySpoiler(image, item);
        return image;
    }

    private static BitmapSource DecodeStill(byte[] data)
    {
        int width;
        using (var probe = new MemoryStream(data, writable: false))
        {
            width = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).PixelWidth;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(data, writable: false);
        if (width > MaxDecodeWidth)
        {
            bitmap.DecodePixelWidth = MaxDecodeWidth; // Limite la mémoire utilisée par les très grandes images.
        }

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private TextBlock CreateTextBlock(string text) => new()
    {
        Text = text,
        FontSize = text.Length > 220 ? 14 : text.Length > 100 ? 15.5 : 18,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.WordEllipsis,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8),
        Foreground = (Brush)FindResource("TextBrush"),
    };

    /// <summary>Petit message centré avec une icône (pièce jointe non affichable, erreur de lecture...).</summary>
    private StackPanel CreateNotice(string glyph, string text)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 12, 24, 12),
        };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("MutedTextBrush"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 0),
            MaxWidth = 340,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = (Brush)FindResource("MutedTextBrush"),
        });
        return panel;
    }

    private TimeSpan ReadingTime(string text)
    {
        // ~15 caractères par seconde, jamais moins que la durée d'affichage configurée.
        var seconds = Math.Clamp(2 + text.Length / 15.0, _imageDuration.TotalSeconds, 20);
        return TimeSpan.FromSeconds(seconds);
    }

    private static void ApplySpoiler(UIElement element, MediaItem item)
    {
        if (item.IsSpoiler)
        {
            element.Effect = new BlurEffect { Radius = 48, KernelType = KernelType.Gaussian };
        }
    }

    /// <summary>Un clic sur un média flouté le dévoile.</summary>
    private void RevealSpoiler()
    {
        if (_current is not { IsSpoiler: true })
        {
            return;
        }

        foreach (UIElement child in MediaHost.Children)
        {
            child.Effect = null;
        }

        SpoilerBadge.Visibility = Visibility.Collapsed;
    }

    private void SetAvatar(string? url)
    {
        AvatarEllipse.Fill = (Brush)FindResource("InputBrush");
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.DecodePixelWidth = 48;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            AvatarEllipse.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Log.Warn("Avatar non chargé.", ex);
        }
    }

    // ───────────────────────────── Interface ─────────────────────────────

    private void SetProgress(double fraction, bool visible)
    {
        ProgressScale.ScaleX = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        ProgressFill.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    private void UpdateQueueBadge()
    {
        QueueBadge.Visibility = _queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueText.Text = $"+{_queue.Count}";
    }

    private void UpdateStatusUi()
    {
        var brushKey = _paused
            ? "WarningBrush"
            : _status.State switch
            {
                ConnectionState.Connected => "SuccessBrush",
                ConnectionState.Connecting or ConnectionState.Warning => "WarningBrush",
                ConnectionState.Error => "DangerBrush",
                _ => "IdleBrush",
            };

        StatusDot.Fill = (Brush)FindResource(brushKey);
        StatusDot.ToolTip = _status.Message;
        TitleText.Text = _paused ? "LiveChat · en pause" : "LiveChat";

        if (_paused)
        {
            IdleIcon.Text = "";
            IdleText.Text = "La réception des médias est en pause.";
        }
        else if (_status.State == ConnectionState.Connected)
        {
            IdleIcon.Text = "";
            IdleText.Text = $"En attente de médias…\n{_status.Message}";
        }
        else
        {
            IdleIcon.Text = _status.State is ConnectionState.Error or ConnectionState.Warning ? "" : "";
            IdleText.Text = _status.Message;
        }
    }

    private void UpdateVolumeUi()
    {
        _updatingVolumeUi = true;
        VolumeSlider.Value = _volume * 100;
        _updatingVolumeUi = false;

        VolumeText.Text = $"{_volume * 100:0} %";
        var glyph = _volume <= 0 ? "" : "";
        VolumeButton.Content = glyph;
        VolumeIcon.Text = glyph;
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.BeginAnimation(OpacityProperty, null);
        Toast.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RaisePlacementChanged()
    {
        if (IsLoaded && WindowState == WindowState.Normal)
        {
            PlacementChanged?.Invoke(Placement);
        }
    }

    private static bool IsReachable(WindowPlacement placement)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(placement.Left + 40, placement.Top + 8, Math.Max(1, placement.Width - 80), 28);
        return screen.IntersectsWith(titleBar);
    }

    // ───────────────────────────── Événements ─────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Bouton relâché avant le début du déplacement.
        }
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e) => VolumePopup.IsOpen = !VolumePopup.IsOpen;

    private void SkipButton_Click(object sender, RoutedEventArgs e) => Skip();

    private void PinToggle_Click(object sender, RoutedEventArgs e) => SetKeepVisible(PinToggle.IsChecked == true, raiseEvent: true);

    private void HideButton_Click(object sender, RoutedEventArgs e) => HideWindow();

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingVolumeUi && VolumeText is not null)
        {
            SetVolume(e.NewValue / 100, raiseEvent: true);
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetVolume(_volume + (e.Delta > 0 ? 0.05 : -0.05), raiseEvent: true);
        ShowToast($"Volume : {_volume * 100:0} %");
        e.Handled = true;
    }

    // ───────────────────────────── Win32 ─────────────────────────────

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    /// <summary>Retire la fenêtre de la liste Alt+Tab (comportement attendu d'une incrustation).</summary>
    private void HideFromAltTab()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
