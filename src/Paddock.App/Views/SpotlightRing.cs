using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Paddock.App.Views;

/// <summary>
/// A pulsing accent ring around a spot on screen for a few seconds — how the
/// desktop search shows where an item on the desktop itself is. Drawn above
/// every window and click-through, so it points even through open apps.
/// </summary>
internal static class SpotlightRing
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    /// <summary>Rings <paramref name="area"/> (DIPs).</summary>
    public static async void Show(Rect area)
    {
        const double pad = 6;
        var accent = (Color)Application.Current.FindResource("D.AccentColor");

        var ring = new Border
        {
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(2.5),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Left = area.Left - pad,
            Top = area.Top - pad,
            Width = area.Width + 2 * pad,
            Height = area.Height + 2 * pad,
            Content = ring,
        };

        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        window.Show();

        // Blink, like an icon found in a pad does.
        ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(260))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
        });

        await Task.Delay(Duration);
        window.Close();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
