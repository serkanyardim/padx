using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Paddock.Core.Services;

namespace Paddock.App.Views;

/// <summary>
/// The thin accent lines shown while dragging a pad, PowerPoint-style, at
/// whatever it is lining up with. Each line is a one-pixel, click-through
/// window — far cheaper than a full-screen overlay.
/// </summary>
internal sealed class SnapGuideOverlay
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>How far a guide runs past the two pads it connects.</summary>
    private const double Overshoot = 10;

    private readonly List<Window> _lines = new();

    public void Show(IReadOnlyList<SnapGuide> guides)
    {
        while (_lines.Count < guides.Count)
            _lines.Add(CreateLine());

        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (i >= guides.Count)
            {
                line.Hide();
                continue;
            }

            var g = guides[i];
            if (g.Vertical)
            {
                line.Left = g.Position - 0.5;
                line.Top = g.Start - Overshoot;
                line.Width = 1;
                line.Height = g.End - g.Start + 2 * Overshoot;
            }
            else
            {
                line.Left = g.Start - Overshoot;
                line.Top = g.Position - 0.5;
                line.Width = g.End - g.Start + 2 * Overshoot;
                line.Height = 1;
            }

            if (!line.IsVisible)
                line.Show();
        }
    }

    public void Hide()
    {
        foreach (var line in _lines)
            line.Hide();
    }

    private static Window CreateLine()
    {
        var accent = (Color)Application.Current.FindResource("D.AccentColor");
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, accent.R, accent.G, accent.B)),
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Width = 1,
            Height = 1,
            MinWidth = 1,
            MinHeight = 1,
        };

        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        return window;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
