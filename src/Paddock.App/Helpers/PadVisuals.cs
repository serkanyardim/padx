using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Paddock.Core.Models;

namespace Paddock.App.Helpers;

/// <summary>A named colour offered in pickers and the pad colour menu.</summary>
public sealed record PaletteColor(string Name, string Hex)
{
    public Brush Brush { get; } = PadVisuals.SwatchBrush(Hex);
}

/// <summary>Colours offered for pads, plus a few extras for edges and text.</summary>
public static class PadPalette
{
    /// <summary>Glass materials, offered above the flat colours.</summary>
    public static IReadOnlyList<PaletteColor> Materials { get; } =
    [
        new("Glass", PadMaterials.Glass),
        new("Frost", PadMaterials.Frost),
    ];

    public static IReadOnlyList<PaletteColor> PadColors { get; } =
    [
        new("Carbon", "#12161C"),
        new("Graphite", "#2B2F36"),
        new("Navy", "#10233F"),
        new("Teal", "#0E3B43"),
        new("Forest", "#133A2A"),
        new("Olive", "#3A3A1A"),
        new("Plum", "#2E1A3D"),
        new("Wine", "#3D1520"),
        new("Rust", "#4A2511"),
        new("Cyan", "#0097B2"),
        new("Lime", "#5E8C2E"),
        new("Amber", "#B07A00"),
        new("Red", "#B3261E"),
        new("Blue", "#1565C0"),
        new("Violet", "#6A1B9A"),
        new("Snow", "#E8ECF1"),
    ];

    /// <summary>Pad colours plus neutrals and accents, for edges, title bars and text.</summary>
    public static IReadOnlyList<PaletteColor> AllColors { get; } =
    [
        .. PadColors,
        new("Black", "#000000"),
        new("Steel", "#2A3442"),
        new("Gray", "#8B949E"),
        new("White", "#FFFFFF"),
        new("Ice", "#7FE7FF"),
        new("Neon", "#00D1FF"),
        new("Mint", "#4DFFB8"),
        new("Gold", "#FFC940"),
    ];
}

/// <summary>Turns <see cref="PadAppearance"/> values into WPF brushes, fonts and alignments.</summary>
public static class PadVisuals
{
    public static IReadOnlyList<string> TitleFonts { get; } = ["Cascadia Mono", "Bahnschrift", "Segoe UI Variable"];

    public static bool TryParse(string? value, out Color colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color parsed)
            {
                colour = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }

    public static Color Parse(string? value, Color fallback)
        => TryParse(value, out var colour) ? colour : fallback;

    /// <summary>Normalises a material name, or any colour WPF understands to #RRGGBB; null if neither.</summary>
    public static string? Normalize(string? value)
        => PadMaterials.Normalize(value)
           ?? (TryParse(value, out var c) ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null);

    public static Brush Solid(string? hex, double opacity, Color fallback)
    {
        var c = Parse(hex, fallback);
        var alpha = (byte)Math.Clamp(opacity * c.A, 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush DarkText = Solid("#14181F", 1, Colors.Black);
    private static readonly Brush LightText = Solid("#E8EDF3", 1, Colors.White);

    /// <summary>Icon label colour that stays readable on the given pad colour.</summary>
    public static Brush LabelBrushFor(string? padHex)
    {
        if (PadMaterials.IsGlass(padHex))
            return IsFrost(padHex) ? DarkText : LightText;

        var c = Parse(padHex, Colors.Black);
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return luminance > 0.55 ? DarkText : LightText;
    }

    // --- Glass ---

    private static bool IsFrost(string? material)
        => string.Equals(material, PadMaterials.Frost, StringComparison.OrdinalIgnoreCase);

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Color White(double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), 255, 255, 255);

    /// <summary>
    /// The colour laid over the blur. Dark glass is smoked; frost is milky.
    /// A slight top-to-bottom deepening keeps it from looking like flat paint.
    /// </summary>
    public static Brush GlassFill(string material, double tint)
    {
        var (r, g, b) = IsFrost(material) ? ((byte)0xF2, (byte)0xF6, (byte)0xFA) : ((byte)0x08, (byte)0x0D, (byte)0x14);
        var top = (byte)Math.Clamp(tint * 0.85 * 255, 0, 255);
        var bottom = (byte)Math.Clamp(tint * 255, 0, 255);

        return Freeze(new LinearGradientBrush(
            Color.FromArgb(top, r, g, b), Color.FromArgb(bottom, r, g, b), 90));
    }

    /// <summary>Diagonal reflection across the upper part of the glass.</summary>
    public static Brush GlassSheen(string material, double shine)
    {
        var s = shine * (IsFrost(material) ? 1.2 : 1.0);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.55, 1) };
        brush.GradientStops.Add(new GradientStop(White(0.30 * s), 0));
        brush.GradientStops.Add(new GradientStop(White(0.10 * s), 0.38));
        brush.GradientStops.Add(new GradientStop(White(0), 0.52));
        brush.GradientStops.Add(new GradientStop(White(0.05 * s), 1));
        return Freeze(brush);
    }

    /// <summary>Edge that catches the light at the top and fades towards the bottom.</summary>
    public static Brush GlassEdge(string material, double shine)
    {
        var s = Math.Max(0.15, shine) * (IsFrost(material) ? 1.3 : 1.0);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(White(0.75 * s), 0));
        brush.GradientStops.Add(new GradientStop(White(0.22 * s), 0.35));
        brush.GradientStops.Add(new GradientStop(White(0.10 * s), 1));
        return Freeze(brush);
    }

    /// <summary>Small sample for menus and pickers: a flat colour, or a glassy gradient.</summary>
    public static Brush SwatchBrush(string value)
    {
        if (!PadMaterials.IsGlass(value))
            return Solid(value, 1, Colors.Transparent);

        var brush = IsFrost(value)
            ? new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xAE, 0xC0, 0xD2), 45)
            : new LinearGradientBrush(Color.FromRgb(0x7F, 0xA6, 0xC4), Color.FromRgb(0x14, 0x1E, 0x2A), 45);
        return Freeze(brush);
    }

    public static FontFamily Font(string? name)
        => new($"{(string.IsNullOrWhiteSpace(name) ? "Segoe UI Variable" : name)}, Segoe UI");

    public static HorizontalAlignment Align(TitleAlignment alignment) => alignment switch
    {
        TitleAlignment.Center => HorizontalAlignment.Center,
        TitleAlignment.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };
}

/// <summary>
/// Binds a radio button to one value of a property: checked when the property
/// equals ConverterParameter, and writes that value back when checked.
/// Works for enums, strings and numbers.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(
            System.Convert.ToString(value, CultureInfo.InvariantCulture),
            System.Convert.ToString(parameter, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
            return Binding.DoNothing;

        var text = System.Convert.ToString(parameter, CultureInfo.InvariantCulture)!;

        if (targetType.IsEnum)
            return Enum.Parse(targetType, text, ignoreCase: true);

        return System.Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
    }
}
