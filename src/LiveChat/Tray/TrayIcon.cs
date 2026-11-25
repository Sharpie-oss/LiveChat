using Forms = System.Windows.Forms;

namespace LiveChat.Tray;

/// <summary>Icône et menu de la zone de notification.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _updateItem;
    private Action? _balloonAction;
    private string? _pendingUpdateVersion;

    public TrayIcon(System.Drawing.Icon icon)
    {
        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false };

        _statusItem = new Forms.ToolStripMenuItem("Non connecté") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(Item("Afficher / masquer la fenêtre", () => ToggleWindowRequested?.Invoke(), bold: true));
        menu.Items.Add(Item("Passer le média en cours", () => SkipRequested?.Invoke()));
        menu.Items.Add(Item("Rejouer le dernier média", () => ReplayRequested?.Invoke()));
        menu.Items.Add(Item("STOP : arrêter et vider la file", () => StopRequested?.Invoke()));

        _pauseItem = new Forms.ToolStripMenuItem("Mettre en pause la réception") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => PauseToggled?.Invoke(_pauseItem.Checked);
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("Tester l'affichage", () => TestRequested?.Invoke()));
        menu.Items.Add(Item("Paramètres…", () => SettingsRequested?.Invoke()));

        _updateItem = Item("Rechercher des mises à jour", () =>
        {
            if (_pendingUpdateVersion is null)
            {
                CheckUpdatesRequested?.Invoke();
            }
            else
            {
                InstallUpdateRequested?.Invoke();
            }
        });
        menu.Items.Add(_updateItem);
        menu.Items.Add(Item("Ouvrir le dossier des journaux", () => OpenLogsRequested?.Invoke()));

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("Quitter LiveChat", () => ExitRequested?.Invoke()));

        _icon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "LiveChat",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ToggleWindowRequested?.Invoke();
            }
        };
        _icon.BalloonTipClicked += (_, _) => _balloonAction?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
    }

    public event Action? ToggleWindowRequested;

    public event Action? SkipRequested;

    public event Action? ReplayRequested;

    public event Action? StopRequested;

    public event Action<bool>? PauseToggled;

    public event Action? TestRequested;

    public event Action? SettingsRequested;

    public event Action? CheckUpdatesRequested;

    public event Action? InstallUpdateRequested;

    public event Action? OpenLogsRequested;

    public event Action? ExitRequested;

    public void SetStatus(string status)
    {
        _statusItem.Text = status;
        var tooltip = $"LiveChat — {status}";
        _icon.Text = tooltip.Length <= 127 ? tooltip : tooltip[..126] + "…";
    }

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    public void SetUpdateAvailable(string? version)
    {
        _pendingUpdateVersion = version;
        _updateItem.Text = version is null ? "Rechercher des mises à jour" : $"Installer la mise à jour {version}…";

        // Recherche toutes les 6 heures : on ne recrée la police (objet GDI) que si le style change.
        var style = version is null ? System.Drawing.FontStyle.Regular : System.Drawing.FontStyle.Bold;
        if (_updateItem.Font.Style != style)
        {
            var previous = _updateItem.Font;
            _updateItem.Font = new System.Drawing.Font(previous, style);
            if (previous.Style == System.Drawing.FontStyle.Bold)
            {
                previous.Dispose(); // Seule la police grasse a été créée par nous (l'autre est celle du menu).
            }
        }
    }

    /// <summary>Affiche une notification Windows ; <paramref name="onClick"/> est appelé si l'utilisateur clique dessus.</summary>
    public void Notify(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(5000, title, text, icon);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private static Forms.ToolStripMenuItem Item(string text, Action onClick, bool bold = false)
    {
        var item = new Forms.ToolStripMenuItem(text);
        if (bold)
        {
            item.Font = new System.Drawing.Font(item.Font, System.Drawing.FontStyle.Bold);
        }

        item.Click += (_, _) => onClick();
        return item;
    }
}
