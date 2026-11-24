using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using LiveChat.Configuration;

namespace LiveChat.Services;

/// <summary>
/// Raccourcis clavier globaux (RegisterHotKey) : ils fonctionnent même quand une autre application
/// (jeu, logiciel de stream...) a le focus, contrairement à la v1.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public GlobalHotkeyManager()
    {
        // Fenêtre « message-only », invisible, dédiée à la réception de WM_HOTKEY.
        _source = new HwndSource(new HwndSourceParameters("LiveChat.Hotkeys")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <returns><c>false</c> si le raccourci est déjà utilisé par une autre application.</returns>
    public bool Register(Hotkey hotkey, Action callback)
    {
        var id = _nextId++;
        var modifiers = ToNativeModifiers(hotkey.Modifiers) | ModNoRepeat;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);

        if (!RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
        {
            Log.Warn($"Raccourci {hotkey} indisponible (erreur Win32 {Marshal.GetLastWin32Error()}).");
            return false;
        }

        _handlers[id] = callback;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _handlers.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _handlers.TryGetValue(wParam.ToInt32(), out var callback))
        {
            handled = true;
            callback();
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= ModWin;
        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
