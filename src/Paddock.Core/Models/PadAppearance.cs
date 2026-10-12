namespace Paddock.Core.Models;

/// <summary>
/// The look shared by every pad. Pads only override their colour
/// (<see cref="PaddockStyle.Color"/>); everything else comes from here so the
/// desktop stays visually consistent.
/// </summary>
public class PadAppearance
{
    public const double MaxBorderThickness = 3;

    // --- Pad surface ---
    public string PadColor { get; set; } = "#12161C";
    public double PadOpacity { get; set; } = 0.82;
    public double CornerRadius { get; set; } = 4;

    // --- Glass (pads whose colour is a PadMaterials value) ---

    /// <summary>How strongly the desktop behind a glass pad is blurred.</summary>
    public GlassBlur GlassBlur { get; set; } = GlassBlur.Strong;

    /// <summary>How much of the glass colour is laid over the blur (0 = clear).</summary>
    public double GlassTint { get; set; } = 0.35;

    /// <summary>Strength of the reflection and the lit edge (0 = none).</summary>
    public double GlassShine { get; set; } = 0.6;

    // --- Edge ---
    public string BorderColor { get; set; } = "#2A3442";
    public double BorderThickness { get; set; } = 1;

    // --- Title bar ---
    public string TitleBarColor { get; set; } = "#000000";
    public double TitleBarOpacity { get; set; } = 0.35;
    public string TitleTextColor { get; set; } = "#7FE7FF";
    public double TitleFontSize { get; set; } = 11;
    public TitleAlignment TitleAlignment { get; set; } = TitleAlignment.Left;
    public string TitleFont { get; set; } = "Cascadia Mono";
    public bool TitleUppercase { get; set; } = true;

    // --- Icons ---
    public double IconSize { get; set; } = 40;
    public bool ShowIconLabels { get; set; } = true;

    /// <summary>Clamps values that may have been hand-edited in settings.json.</summary>
    public void Normalize()
    {
        PadOpacity = Math.Clamp(PadOpacity, 0, 1);
        TitleBarOpacity = Math.Clamp(TitleBarOpacity, 0, 1);
        GlassTint = Math.Clamp(GlassTint, 0, 1);
        GlassShine = Math.Clamp(GlassShine, 0, 1);
        BorderThickness = Math.Clamp(Math.Round(BorderThickness), 0, MaxBorderThickness);
        CornerRadius = Math.Clamp(CornerRadius, 0, 24);
        TitleFontSize = Math.Clamp(TitleFontSize, 8, 20);
        IconSize = Math.Clamp(IconSize, 24, 64);
    }
}

public enum GlassBlur
{
    /// <summary>No blur: tinted clear glass.</summary>
    Off,

    /// <summary>Plain Gaussian blur.</summary>
    Light,

    /// <summary>Acrylic: heavier blur with a frosted texture.</summary>
    Strong
}

public enum TitleAlignment
{
    Left,
    Center,
    Right
}
