using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using LiveChat.Services;

namespace LiveChat.Views;

/// <summary>Présente une mise à jour (notes de version) puis l'installe avec une barre de progression.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly UpdateService _service;
    private bool _installing;

    public UpdateWindow(UpdateInfo update, UpdateService service)
    {
        InitializeComponent();
        _update = update;
        _service = service;

        VersionText.Text = $"LiveChat {update.Version.ToString(3)} est disponible (version installée : {UpdateService.CurrentVersion.ToString(3)}).";
        NotesText.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes) ? "Aucune note de version." : UpdateService.FormatReleaseNotes(update.ReleaseNotes);
        ReleaseLink.NavigateUri = new Uri(update.ReleasePageUrl);
    }

    /// <summary>Vrai si la mise à jour est installée et que l'application doit se fermer.</summary>
    public bool Installed { get; private set; }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installing)
        {
            return;
        }

        _installing = true;
        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = "Téléchargement…";

        var progress = new Progress<double>(value =>
        {
            ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * value;
            ProgressText.Text = value < 1 ? $"Téléchargement… {value:P0}" : "Installation…";
        });

        try
        {
            await _service.InstallAsync(_update, progress);
            Installed = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("Échec de la mise à jour.", ex);
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = ex is UnauthorizedAccessException
                ? "Impossible de remplacer les fichiers : LiveChat est peut-être installé dans un dossier protégé. Téléchargez la nouvelle version depuis GitHub."
                : $"La mise à jour a échoué : {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            _installing = false;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (!_installing)
        {
            DialogResult = false;
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Documents.Hyperlink)
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
