using System.Runtime.InteropServices;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Keeps Paddock's windows at "desktop level": above the wallpaper and the
/// desktop icons, but below every ordinary application window.
///
/// The trick is <em>not</em> to reparent into the shell's WorkerW — a window
/// living there is drawn behind the desktop icons and never receives mouse
/// input. Instead each paddock stays a normal top-level window whose z-order is
/// pinned directly above the shell's desktop window (Progman). Windows will
/// happily raise other applications above it, but nothing can raise it above
/// them, so paddocks behave like part of the desktop.
///
/// Two pieces are needed:
/// <list type="number">
/// <item><see cref="SendToDesktopLevel"/> once, when the window is created.</item>
/// <item><see cref="EnforceDesktopZOrder"/> from the window's WM_WINDOWPOSCHANGING
/// handler, so activation and focus changes cannot lift it out of place.</item>
/// </list>
/// </summary>
public class ShellHookService
{
    /// <summary>The message to forward to <see cref="EnforceDesktopZOrder"/>.</summary>
    public const int WindowPosChangingMessage = NativeMethods.WM_WINDOWPOSCHANGING;

    /// <summary>
    /// Minimum gap between attempts to find the icon view again, in ms.
    /// WM_WINDOWPOSCHANGING can arrive dozens of times a second while a paddock
    /// is dragged, and when the view genuinely cannot be found (desktop icons
    /// turned off, say) an unthrottled search would run on every one of them.
    /// </summary>
    private const long ResolveThrottleMs = 1000;

    private IntPtr _desktopWindow;
    private IntPtr _loggedAnchor;
    private long _lastResolveTicks;

    /// <summary>
    /// The top-level window that hosts the desktop icons — normally Progman,
    /// but on machines where the wallpaper is animated or slideshowed the icon
    /// view lives in a WorkerW that sits above Progman. Anchoring to the wrong
    /// one would leave paddocks hidden behind the desktop, so we look for the
    /// window actually containing SHELLDLL_DefView.
    ///
    /// The result is cached but re-validated on every call, which also covers
    /// Explorer restarting or switching wallpaper modes.
    /// </summary>
    public IntPtr GetDesktopWindow()
    {
        if (HostsDesktopIcons(_desktopWindow))
            return _desktopWindow;

        var now = Environment.TickCount64;
        if (_desktopWindow != IntPtr.Zero && now - _lastResolveTicks < ResolveThrottleMs)
            return _desktopWindow;

        _lastResolveTicks = now;
        _desktopWindow = ResolveDesktopWindow();

        // Worth a line in the log — but only when it actually changes: if
        // paddocks ever end up invisible, the window we chose to sit on top of
        // is the first thing to check.
        if (_desktopWindow != _loggedAnchor)
        {
            _loggedAnchor = _desktopWindow;
            Log.Info($"Desktop z-order anchor: {NativeMethods.GetWindowClassName(_desktopWindow)} " +
                     $"(0x{_desktopWindow.ToInt64():X}).");
        }

        return _desktopWindow;
    }

    /// <summary>
    /// Marks the window as a tool window (no Alt-Tab entry, no taskbar button)
    /// and drops it into the z-order slot just above the desktop.
    /// Safe to call repeatedly.
    /// </summary>
    public void SendToDesktopLevel(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        // WPF adds WS_EX_APPWINDOW for ShowInTaskbar, and APPWINDOW beats
        // TOOLWINDOW: left in place, every pad gets its own taskbar button.
        var exStyle = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
        var wanted = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~(long)NativeMethods.WS_EX_APPWINDOW;
        if (wanted != exStyle)
        {
            NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE, (IntPtr)wanted);
        }

        NativeMethods.SetWindowPos(
            windowHandle,
            GetZOrderAnchor(windowHandle),
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Rewrites the pending WINDOWPOS so the window lands just above the
    /// desktop, whatever z-order the system was about to give it. Call this
    /// with the lParam of WM_WINDOWPOSCHANGING and let the message continue
    /// to normal processing afterwards.
    /// </summary>
    /// <returns>True when the pending position was adjusted.</returns>
    public bool EnforceDesktopZOrder(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return false;

        var position = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);

        // A plain move or resize leaves the z-order alone, so there is nothing
        // to correct — and forcing one on every drag step would only cause
        // needless z-order churn. Activation, by contrast, arrives with
        // SWP_NOZORDER cleared and HWND_TOP requested: that is what we override.
        if ((position.flags & NativeMethods.SWP_NOZORDER) != 0)
            return false;

        var anchor = GetZOrderAnchor(position.hwnd);
        if (position.hwndInsertAfter == anchor)
            return false;

        position.hwndInsertAfter = anchor;

        Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
        return true;
    }

    /// <summary>
    /// The window our paddocks sit directly on top of. Falls back to the bottom
    /// of the z-order when the shell's desktop window cannot be found, which has
    /// the same practical effect (the desktop is kept below normal windows).
    /// </summary>
    private IntPtr GetZOrderAnchor(IntPtr ownWindowHandle)
    {
        var desktop = GetDesktopWindow();
        return desktop != IntPtr.Zero && desktop != ownWindowHandle
            ? desktop
            : NativeMethods.HWND_BOTTOM;
    }

    /// <summary>
    /// Shows or hides Explorer's desktop icon view (the icons only — the
    /// wallpaper and our pads stay). Used to hide the shuffle while Auto
    /// Organize moves files around. Always pair a hide with a show.
    /// </summary>
    public void SetDesktopIconsVisible(bool visible)
    {
        var desktop = GetDesktopWindow();
        var defView = NativeMethods.FindWindowExW(desktop, IntPtr.Zero, "SHELLDLL_DefView", null);
        var listView = NativeMethods.FindWindowExW(defView, IntPtr.Zero, "SysListView32", null);

        if (listView != IntPtr.Zero)
            NativeMethods.ShowWindow(listView, visible ? NativeMethods.SW_SHOWNA : NativeMethods.SW_HIDE);
    }

    private static bool HostsDesktopIcons(IntPtr windowHandle)
        => windowHandle != IntPtr.Zero
           && NativeMethods.IsWindow(windowHandle)
           && NativeMethods.FindWindowExW(windowHandle, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

    private static IntPtr ResolveDesktopWindow()
    {
        var progman = NativeMethods.GetShellWindow();
        if (progman == IntPtr.Zero)
            progman = NativeMethods.FindWindowW("Progman", null);

        if (HostsDesktopIcons(progman))
            return progman;

        // Walk the top-level WorkerW windows looking for the one holding the
        // desktop icon view.
        var worker = IntPtr.Zero;
        while ((worker = NativeMethods.FindWindowExW(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            if (HostsDesktopIcons(worker))
                return worker;
        }

        // No icon view at all (icons hidden, or an unusual shell) — Progman is
        // still the right thing to sit on top of.
        return progman;
    }
}
