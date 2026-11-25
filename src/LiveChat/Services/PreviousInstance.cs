using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LiveChat.Services;

/// <summary>
/// Ancienne instance qui ne se ferme pas après une mise à jour. Mesuré en 2.0.1 → 2.0.2 : en se fermant, la v2.0.1
/// charge pour la première fois une dépendance (WpfAnimatedGif) dans son exécutable, déjà remplacé, et reste bloquée.
/// La nouvelle version ne pouvait alors pas démarrer. Depuis la v2.0.2, les dépendances sont préchargées avant le
/// remplacement ; ce filet de sécurité couvre les mises à jour depuis les versions plus anciennes.
/// </summary>
public static class PreviousInstance
{
    private const int NimDelete = 2;

    /// <summary>
    /// Termine les autres processus LiveChat lancés depuis <paramref name="appDirectory"/> (le dossier de
    /// l'application mise à jour), sans toucher à une installation située ailleurs.
    /// </summary>
    /// <returns>Vrai si au moins un processus a été terminé.</returns>
    public static bool TerminateStuck(string appDirectory)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        var terminated = false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(UpdateService.ExecutableName)))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId || !IsInDirectory(process, directory))
                {
                    continue;
                }

                try
                {
                    Log.Warn($"L'ancienne instance (processus {process.Id}) ne s'est pas fermée après la mise à jour : arrêt forcé.");
                    RemoveTrayIcons(process.Id);
                    process.Kill();
                    process.WaitForExit(5000);
                    terminated = true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    Log.Warn($"Impossible d'arrêter l'ancienne instance (processus {process.Id}).", ex);
                }
            }
        }

        return terminated;
    }

    private static bool IsInDirectory(Process process, string directory)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path is not null && string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false; // Processus d'un autre utilisateur ou déjà terminé.
        }
    }

    /// <summary>
    /// Retire l'icône de notification d'un processus que l'on va terminer : sinon elle reste affichée
    /// (icône « fantôme ») jusqu'à ce que la souris passe dessus. L'icône de WinForms a l'identifiant 1.
    /// </summary>
    private static void RemoveTrayIcons(int processId)
    {
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner == processId)
            {
                var className = new StringBuilder(64);
                GetClassName(handle, className, className.Capacity);
                if (className.ToString().StartsWith("WindowsForms10.Window", StringComparison.Ordinal))
                {
                    var data = new NotifyIconData { Size = Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1 };
                    Shell_NotifyIcon(NimDelete, ref data);
                }
            }

            return true;
        }, IntPtr.Zero);
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public int State;
        public int StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
}
