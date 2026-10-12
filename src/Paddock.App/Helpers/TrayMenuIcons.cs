using System.Drawing;
using System.Drawing.Drawing2D;

namespace Paddock.App.Helpers;

/// <summary>
/// Colourful round glyphs for the tray menu, drawn at runtime so they stay sharp
/// at any DPI without shipping image files. Glyphs are designed on a 16-unit grid
/// and scaled to <paramref name="size"/> pixels.
/// </summary>
internal static class TrayMenuIcons
{
    private static readonly Color Red = Color.FromArgb(255, 69, 58);
    private static readonly Color Blue = Color.FromArgb(10, 132, 255);
    private static readonly Color Green = Color.FromArgb(48, 209, 88);
    private static readonly Color Gray = Color.FromArgb(142, 142, 147);

    /// <summary>Menu icon edge in device pixels for the current system DPI.</summary>
    public static int CurrentSize()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(16 * g.DpiX / 96f);
    }

    public static Bitmap NewPad(int size) => Draw(size, Red, g =>
    {
        using var pen = WhitePen(2f);
        g.DrawLine(pen, 8f, 4.5f, 8f, 11.5f);
        g.DrawLine(pen, 4.5f, 8f, 11.5f, 8f);
    });

    public static Bitmap Settings(int size) => Draw(size, Blue, g =>
    {
        using var white = new SolidBrush(Color.White);
        using var hole = new SolidBrush(Blue);

        // Eight teeth around a solid hub, then punch the axle hole back out.
        for (int i = 0; i < 8; i++)
        {
            var state = g.Save();
            g.TranslateTransform(8f, 8f);
            g.RotateTransform(i * 45f);
            g.FillRectangle(white, -1.1f, -6f, 2.2f, 3f);
            g.Restore(state);
        }
        g.FillEllipse(white, 3.8f, 3.8f, 8.4f, 8.4f);
        g.FillEllipse(hole, 6.2f, 6.2f, 3.6f, 3.6f);
    });

    public static Bitmap About(int size) => Draw(size, Green, g =>
    {
        using var white = new SolidBrush(Color.White);
        using var pen = WhitePen(2f);
        g.FillEllipse(white, 6.9f, 3.4f, 2.2f, 2.2f);
        g.DrawLine(pen, 8f, 7.4f, 8f, 11.8f);
    });

    public static Bitmap Exit(int size) => Draw(size, Gray, g =>
    {
        // Power symbol: an arc open at the top with a stroke through the gap.
        using var pen = WhitePen(1.7f);
        g.DrawArc(pen, 4.2f, 4.6f, 7.6f, 7.6f, -50f, 280f);
        g.DrawLine(pen, 8f, 3.6f, 8f, 8f);
    });

    private static Pen WhitePen(float width) =>
        new(Color.White, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };

    private static Bitmap Draw(int size, Color background, Action<Graphics> glyph)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.ScaleTransform(size / 16f, size / 16f);

        using (var bg = new SolidBrush(background))
            g.FillEllipse(bg, 0.5f, 0.5f, 15f, 15f);

        glyph(g);
        return bmp;
    }
}
