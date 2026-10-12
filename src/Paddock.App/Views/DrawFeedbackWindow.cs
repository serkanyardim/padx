using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Paddock.App.Helpers;

namespace Paddock.App.Views;

/// <summary>
/// The rectangle shown while a pad is drawn with Ctrl + drag on the desktop.
/// It is purely visual: click-through, never activated, and sized to the
/// rectangle itself rather than covering the screen.
/// </summary>
internal sealed class DrawFeedbackWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly TextBlock _size;

    public DrawFeedbackWindow(double cornerRadius)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        var accent = (Color)Application.Current.FindResource("D.AccentColor");

        _size = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("D.Mono"),
            FontSize = 10,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        Content = new Border
        {
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(accent),
            Background = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(cornerRadius),
            Child = _size,
        };
    }

    /// <summary>Spans the two corners (DIPs), in whichever direction the user drags.</summary>
    public void Span(Point a, Point b)
    {
        Left = Math.Min(a.X, b.X);
        Top = Math.Min(a.Y, b.Y);
        Width = Math.Max(1, Math.Abs(b.X - a.X));
        Height = Math.Max(1, Math.Abs(b.Y - a.Y));

        var scale = ScreenHelper.DipScale;
        _size.Text = $"{Width * scale:0} × {Height * scale:0}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Click-through and never focusable, so the desktop keeps the mouse.
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
