using System.Windows.Controls;
using System.Windows.Input;
using LiveChat.Configuration;

namespace LiveChat.Controls;

/// <summary>
/// Champ de capture d'un raccourci clavier : cliquer puis appuyer sur la combinaison voulue.
/// Retour arrière, Suppr ou Échap effacent le raccourci.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private const string EmptyText = "Aucun";
    private const string PromptText = "Appuyez sur une combinaison…";

    private Hotkey? _hotkey;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
        Cursor = Cursors.Hand;
        UpdateText();
    }

    public Hotkey? Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            UpdateText();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return; // Laisse la navigation au clavier fonctionner.
        }

        e.Handled = true;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete or Key.Escape)
        {
            Hotkey = null;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            Text = modifiers == ModifierKeys.None ? PromptText : new Hotkey(modifiers, Key.None).ToString().Replace("None", "…");
            return;
        }

        var candidate = new Hotkey(modifiers, key);
        if (!candidate.IsValidGlobalHotkey)
        {
            Text = "Ajoutez Ctrl ou Alt (ou utilisez F1-F24, Pause…)";
            return;
        }

        Hotkey = candidate;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = PromptText;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
    }

    private void UpdateText() => Text = _hotkey?.ToString() ?? EmptyText;
}
