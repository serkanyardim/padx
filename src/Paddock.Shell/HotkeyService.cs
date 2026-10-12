using System.Windows.Input;
using System.Windows.Interop;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Registers system-wide hotkeys against a hidden message-only window, so they
/// work no matter which application has focus.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly HwndSource _source;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService()
    {
        // A zero-sized window with no WS_VISIBLE — it exists only to receive
        // WM_HOTKEY on the UI thread.
        _source = new HwndSource(new HwndSourceParameters("Paddock.Hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        });

        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a hotkey written as "Ctrl+Shift+F12".
    /// Returns false when the string cannot be parsed or the combination is
    /// already taken by another application.
    /// </summary>
    public bool Register(string hotkey, Action onPressed)
    {
        if (_disposed || !TryParse(hotkey, out var modifiers, out var virtualKey))
            return false;

        var id = _nextId++;

        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            Log.Warn($"Hotkey '{hotkey}' is unavailable — another application already owns it.");
            return false;
        }

        _handlers[id] = onPressed;
        return true;
    }

    /// <summary>Releases every registered hotkey, e.g. before re-registering a changed set.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _handlers.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    /// <summary>Parses "Ctrl+Alt+F12" into Win32 modifier flags and a virtual key.</summary>
    public static bool IsValid(string hotkey) => TryParse(hotkey, out _, out _);

    internal static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key))
            return false;

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }
}
