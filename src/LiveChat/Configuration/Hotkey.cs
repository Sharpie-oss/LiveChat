using System.Text.Json.Serialization;
using System.Windows.Input;

namespace LiveChat.Configuration;

/// <summary>Raccourci clavier global (touche + modificateurs).</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    /// <summary>
    /// Un raccourci global sans modificateur n'est accepté que pour les touches rarement utilisées
    /// en saisie (F1-F24, Pause, Arrêt défil, Inser) afin de ne pas « voler » la frappe au clavier.
    /// </summary>
    [JsonIgnore] // Propriété calculée : elle n'a pas sa place dans settings.json.
    public bool IsValidGlobalHotkey
    {
        get
        {
            if (Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            {
                return false;
            }

            if ((Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            {
                return true;
            }

            return Key is (>= Key.F1 and <= Key.F24) or Key.Pause or Key.Scroll or Key.Insert;
        }
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Maj");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Pavé num. {key - Key.NumPad0}",
        Key.Pause => "Pause",
        Key.Scroll => "Arrêt défil",
        Key.Insert => "Inser",
        Key.Delete => "Suppr",
        Key.Home => "Début",
        Key.End => "Fin",
        Key.PageUp => "Page préc.",
        Key.PageDown => "Page suiv.",
        Key.Space => "Espace",
        Key.Return => "Entrée",
        Key.Escape => "Échap",
        Key.Back => "Retour arrière",
        Key.Up => "Haut",
        Key.Down => "Bas",
        Key.Left => "Gauche",
        Key.Right => "Droite",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.Add => "Pavé num. +",
        Key.Subtract => "Pavé num. -",
        Key.Multiply => "Pavé num. *",
        Key.Divide => "Pavé num. /",
        _ => key.ToString(),
    };
}
