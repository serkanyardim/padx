using System.Drawing;
using System.Windows.Forms;

namespace Paddock.App.Helpers;

/// <summary>
/// Dark, flat renderer for the tray menu so it matches the "digital" look of
/// the WPF menus and the settings window (same tokens as Digital.xaml).
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Panel = Color.FromArgb(0x10, 0x15, 0x1C);
    private static readonly Color Line = Color.FromArgb(0x2B, 0x35, 0x42);
    private static readonly Color Text = Color.FromArgb(0xD9, 0xE1, 0xEA);
    private static readonly Color Highlight = Color.FromArgb(0x10, 0x2C, 0x36);

    public TrayMenuRenderer()
        : base(new DigitalColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Line);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private sealed class DigitalColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemSelectedGradientBegin => Highlight;
        public override Color MenuItemSelectedGradientEnd => Highlight;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Panel;
    }
}
