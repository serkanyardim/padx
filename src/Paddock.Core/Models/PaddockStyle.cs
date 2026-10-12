namespace Paddock.Core.Models;

/// <summary>
/// Per-pad styling. Deliberately just a colour: opacity, edges and the title bar
/// are global (<see cref="PadAppearance"/>).
/// </summary>
public class PaddockStyle
{
    /// <summary>Pad colour as #RRGGBB, or null to follow <see cref="PadAppearance.PadColor"/>.</summary>
    public string? Color { get; set; }

    /// <summary>The colour this pad is actually drawn with.</summary>
    public string EffectiveColor(PadAppearance appearance)
        => string.IsNullOrWhiteSpace(Color) ? appearance.PadColor : Color;
}
