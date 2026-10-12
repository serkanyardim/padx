using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Paddock.Core.Models;

namespace Paddock.App.Helpers;

/// <summary>A named colour offered in pickers and the pad colour menu.</summary>
public sealed record PaletteColor(string Name, string Hex)
{
    public Brush Brush { get; } = PadVisuals.Solid(Hex, 1, Colors.Transparent);
}

/// <summary>Colours offered for pads, plus a few extras for edges and text.</summary>
public static class PadPalette
{
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

    /// <summary>Normalises any colour string WPF understands to #RRGGBB, or null.</summary>
    public static string? Normalize(string? value)
        => TryParse(value, out var c) ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;

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
        var c = Parse(padHex, Colors.Black);
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return luminance > 0.55 ? DarkText : LightText;
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
