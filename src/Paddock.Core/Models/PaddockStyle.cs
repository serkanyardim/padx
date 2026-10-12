namespace Paddock.Core.Models;

/// <summary>
/// Special pad "colours" that are materials rather than flat colours: real
/// blur-behind with a tint, sheen and lit edge. Stored in place of a #RRGGBB.
/// </summary>
public static class PadMaterials
{
    /// <summary>Dark smoked glass.</summary>
    public const string Glass = "glass";

    /// <summary>Light frosted glass.</summary>
    public const string Frost = "frost";

    public static bool IsGlass(string? value)
        => string.Equals(value, Glass, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Frost, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the canonical material name, or null if the value is not one.</summary>
    public static string? Normalize(string? value)
        => string.Equals(value?.Trim(), Glass, StringComparison.OrdinalIgnoreCase) ? Glass
         : string.Equals(value?.Trim(), Frost, StringComparison.OrdinalIgnoreCase) ? Frost
         : null;
}

/// <summary>
/// Per-pad styling. Deliberately just a colour: opacity, edges and the title bar
/// are global (<see cref="PadAppearance"/>).
/// </summary>
public class PaddockStyle
{
    /// <summary>
    /// Pad colour as #RRGGBB or a <see cref="PadMaterials"/> name, or null to
    /// follow <see cref="PadAppearance.PadColor"/>.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>The colour this pad is actually drawn with.</summary>
    public string EffectiveColor(PadAppearance appearance)
        => string.IsNullOrWhiteSpace(Color) ? appearance.PadColor : Color;
}
