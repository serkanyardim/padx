using System.Windows.Automation;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Lets the user draw a new pad straight onto the desktop with Ctrl + drag.
///
/// The desktop belongs to Explorer, so the only way to see the gesture is a
/// low-level mouse hook. When Ctrl + left button goes down on the desktop's
/// icon view, the press is held back from Explorer while we check what is
/// under the cursor:
/// <list type="bullet">
/// <item>an icon — the press is replayed unchanged, so Ctrl + drag still copies
/// files and Ctrl + click still toggles selection exactly as Windows intends;</item>
/// <item>empty desktop — we own the drag: Explorer never sees it (no rubber
/// band), and the rectangle is reported through the events below.</item>
/// </list>
/// The hook runs on its own thread so a slow UI thread can never stall the
/// mouse. All events are raised on background threads, in physical pixels.
/// </summary>
public sealed class DesktopDragService : IDisposable
{
    /// <summary>Tags the input we replay so the hook lets it through.</summary>
    private static readonly UIntPtr ReplayMarker = new(0x50414444); // "PADD"

    /// <summary>How long the icon check may take before we give the press back to Explorer.</summary>
    private static readonly TimeSpan HitTestTimeout = TimeSpan.FromMilliseconds(600);

    private readonly object _gate = new();
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    private State _state;
    private NativeMethods.POINT _start;
    private NativeMethods.POINT _current;
    private bool _releasedWhilePending;

    private enum State { Idle, Pending, Drawing }

    public DesktopDragService()
    {
        _proc = HookProc;
    }

    /// <summary>A drag on empty desktop began at this point.</summary>
    public event Action<int, int>? DrawStarted;

    /// <summary>The cursor moved while drawing.</summary>
    public event Action<int, int>? DrawMoved;

    /// <summary>The button was released: start point, end point.</summary>
    public event Action<int, int, int, int>? DrawCompleted;

    /// <summary>Escape was pressed mid-drag.</summary>
    public event Action? DrawCancelled;

    public bool IsRunning => _thread is not null;

    public void Start()
    {
        if (_thread is not null)
            return;

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Paddock desktop drag hook" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        if (_thread is null)
            return;

        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;

        lock (_gate)
            _state = State.Idle;
    }

    public void Dispose() => Stop();

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        ready.Set();

        if (_hook == IntPtr.Zero)
        {
            Log.Warn("Could not install the desktop mouse hook; Ctrl+drag pads are unavailable.");
            return;
        }

        // A low-level hook is called through this thread's message loop.
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            if (info.dwExtraInfo != ReplayMarker && Handle((int)wParam, info.pt))
                return new IntPtr(1); // swallow: Explorer never sees it
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Returns true when the message must be withheld from the system.</summary>
    private bool Handle(int message, NativeMethods.POINT pt)
    {
        Action? raise = null;
        var swallow = false;

        lock (_gate)
        {
            switch (message)
            {
                case NativeMethods.WM_LBUTTONDOWN when _state == State.Idle && OnlyCtrlHeld() && IsDesktopIconView(pt):
                    _state = State.Pending;
                    _start = _current = pt;
                    _releasedWhilePending = false;
                    ThreadPool.QueueUserWorkItem(_ => Resolve());
                    swallow = true;
                    break;

                case NativeMethods.WM_MOUSEMOVE when _state == State.Pending:
                    _current = pt;
                    break;

                case NativeMethods.WM_MOUSEMOVE when _state == State.Drawing:
                    _current = pt;
                    if (IsDown(NativeMethods.VK_ESCAPE))
                    {
                        _state = State.Idle;
                        raise = DrawCancelled;
                    }
                    else
                    {
                        raise = () => DrawMoved?.Invoke(pt.X, pt.Y);
                    }
                    break;

                case NativeMethods.WM_LBUTTONUP when _state == State.Pending:
                    _releasedWhilePending = true;
                    swallow = true;
                    break;

                case NativeMethods.WM_LBUTTONUP when _state == State.Drawing:
                    _state = State.Idle;
                    var start = _start;
                    raise = () => DrawCompleted?.Invoke(start.X, start.Y, pt.X, pt.Y);
                    swallow = true;
                    break;
            }
        }

        // Never block the hook: listeners get the event on the thread pool.
        if (raise is not null)
            ThreadPool.QueueUserWorkItem(_ => raise());

        return swallow;
    }

    /// <summary>Decides, off the hook thread, whether the held-back press landed on an icon.</summary>
    private void Resolve()
    {
        NativeMethods.POINT start;
        lock (_gate)
            start = _start;

        var onIcon = IsOverIcon(start);

        NativeMethods.POINT current;
        bool released;

        lock (_gate)
        {
            if (_state != State.Pending)
                return;

            current = _current;
            released = _releasedWhilePending;

            // A plain Ctrl+click on empty desktop: nothing to draw.
            _state = onIcon || released ? State.Idle : State.Drawing;
        }

        if (onIcon)
        {
            Replay(start, current, released);
            return;
        }

        if (!released)
        {
            DrawStarted?.Invoke(start.X, start.Y);
            DrawMoved?.Invoke(current.X, current.Y);
        }
    }

    /// <summary>Gives Explorer the press it was denied, where it originally happened.</summary>
    private static void Replay(NativeMethods.POINT start, NativeMethods.POINT current, bool released)
    {
        NativeMethods.SetCursorPos(start.X, start.Y);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, ReplayMarker);

        if (released)
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, ReplayMarker);

        NativeMethods.SetCursorPos(current.X, current.Y);
    }

    /// <summary>
    /// Asks UI Automation what is under the point. Anything other than a clear
    /// "empty desktop" — including a slow or failed answer — counts as an icon,
    /// so in doubt Windows' own behaviour wins.
    /// </summary>
    private static bool IsOverIcon(NativeMethods.POINT pt)
    {
        var lookup = Task.Run(() =>
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            return element?.Current.ControlType == ControlType.ListItem;
        });

        try
        {
            return !lookup.Wait(HitTestTimeout) || lookup.Result;
        }
        catch (Exception ex)
        {
            Log.Warn($"Desktop hit test failed ({ex.GetBaseException().Message}); passing the click through.");
            return true;
        }
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool OnlyCtrlHeld()
        => IsDown(NativeMethods.VK_CONTROL)
           && !IsDown(NativeMethods.VK_SHIFT)
           && !IsDown(NativeMethods.VK_MENU)
           && !IsDown(NativeMethods.VK_LWIN)
           && !IsDown(NativeMethods.VK_RWIN);

    /// <summary>True when the point is over the bare desktop (Explorer's icon view).</summary>
    private static bool IsDesktopIconView(NativeMethods.POINT pt)
    {
        var window = NativeMethods.WindowFromPoint(pt);
        if (window == IntPtr.Zero)
            return false;

        var cls = NativeMethods.GetWindowClassName(window);

        if (cls == "SysListView32")
            return NativeMethods.GetWindowClassName(NativeMethods.GetParent(window)) == "SHELLDLL_DefView";

        return cls == "SHELLDLL_DefView";
    }
}
