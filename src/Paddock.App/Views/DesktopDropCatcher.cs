using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Paddock.App.Views;

/// <summary>
/// While an icon is dragged out of a pad, the bare desktop catches the drop.
/// Left to Explorer, a desktop file dropped on the desktop would be "moved"
/// onto itself ("the source and destination file names are the same").
///
/// It is a see-through window over all screens that exists only during the
/// drag, sits at desktop level just below the pads, and accepts the drop
/// without doing anything: the item leaves its pad and the Desktop pad picks
/// it up. Apps and other pads above it still take their drops as usual.
/// </summary>
internal static class DesktopDropCatcher
{
    private static Window? _window;

    private static App CurrentApp => (App)Application.Current;

    public static void Begin()
    {
        _window ??= Create();
        _window.Left = SystemParameters.VirtualScreenLeft;
        _window.Top = SystemParameters.VirtualScreenTop;
        _window.Width = SystemParameters.VirtualScreenWidth;
        _window.Height = SystemParameters.VirtualScreenHeight;
        _window.Show();

        var handle = new WindowInteropHelper(_window).Handle;
        CurrentApp.ShellHookService.SendToDesktopLevel(handle);

        // Pinned after it, the pads end up above it and keep their drops.
        CurrentApp.Host?.PinPads();
    }

    public static void End() => _window?.Hide();

    private static Window Create()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            // Not quite clear: a fully transparent pixel would let the drop through to Explorer.
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
            AllowDrop = true,
        };

        window.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };

        window.Drop += (_, e) =>
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };

        return window;
    }
}
