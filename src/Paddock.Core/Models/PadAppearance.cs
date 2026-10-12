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
        BorderThickness = Math.Clamp(Math.Round(BorderThickness), 0, MaxBorderThickness);
        CornerRadius = Math.Clamp(CornerRadius, 0, 24);
        TitleFontSize = Math.Clamp(TitleFontSize, 8, 20);
        IconSize = Math.Clamp(IconSize, 24, 64);
    }
}

public enum TitleAlignment
{
    Left,
    Center,
    Right
}
