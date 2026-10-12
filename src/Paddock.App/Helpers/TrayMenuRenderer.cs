using System.Drawing;
using System.Windows.Forms;

namespace Paddock.App.Helpers;

/// <summary>
/// Dark, flat renderer for the tray menu so it matches the "digital" look of
/// the WPF menus and the settings window (same tokens as Digital.xaml).
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Panel = Color.FromArgb(0x23, 0x2C, 0x38);
    private static readonly Color Line = Color.FromArgb(0x3A, 0x47, 0x57);
    private static readonly Color Text = Color.FromArgb(0xE8, 0xEE, 0xF4);
    private static readonly Color TextDim = Color.FromArgb(0x8E, 0x9B, 0xAA);
    private static readonly Color Highlight = Color.FromArgb(0x1E, 0x4A, 0x5A);

    public TrayMenuRenderer()
        : base(new DigitalColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : TextDim;
        base.OnRenderItemText(e);
    }

    /// <summary>Submenu arrows default to black, which vanishes on the dark menu.</summary>
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Selected == true ? Text : TextDim;
        base.OnRenderArrow(e);
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

        // An item whose submenu is open is drawn "pressed" — without these it
        // falls back to the system's pale blue, and white text disappears on it.
        public override Color MenuItemPressedGradientBegin => Highlight;
        public override Color MenuItemPressedGradientMiddle => Highlight;
        public override Color MenuItemPressedGradientEnd => Highlight;
        public override Color ButtonSelectedHighlight => Highlight;
        public override Color ButtonPressedHighlight => Highlight;
        public override Color CheckBackground => Highlight;
        public override Color CheckSelectedBackground => Highlight;
        public override Color CheckPressedBackground => Highlight;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Panel;
    }
}
