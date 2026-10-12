using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>
/// Pixel geometry of a pad's icon grid, supplied by the UI so the planner can
/// size pads to fit their icons exactly.
/// </summary>
/// <param name="CellWidth">Width one icon takes, including its margins.</param>
/// <param name="CellHeight">Height one icon takes, including its margins.</param>
/// <param name="ChromeWidth">Everything around the grid horizontally (edges, padding).</param>
/// <param name="ChromeHeight">Everything around the grid vertically (title bar, edges, padding).</param>
/// <param name="MinWidth">Smallest pad the window allows.</param>
public sealed record PadMetrics(double CellWidth, double CellHeight, double ChromeWidth, double ChromeHeight, double MinWidth);

/// <summary>A rectangle in screen DIPs.</summary>
public readonly record struct PlacedRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Intersects(PlacedRect other, double gap)
        => X < other.Right + gap && other.X < Right + gap
           && Y < other.Bottom + gap && other.Y < Bottom + gap;
}

/// <summary>
/// Sizes pads to their contents and packs them onto the screen around the
/// pads that must not move (locked ones). Pure geometry — fully unit-tested.
/// </summary>
public static class AutoLayoutPlanner
{
    /// <summary>Granularity of the placement search, in DIPs.</summary>
    private const double Step = 4;

    /// <summary>
    /// The smallest pad that shows <paramref name="count"/> icons with no
    /// scrolling and no empty columns: at most <paramref name="maxPerRow"/> wide.
    /// </summary>
    public static (double Width, double Height, int Columns, int Rows) Measure(int count, int maxPerRow, PadMetrics m)
    {
        var columns = Math.Clamp(count, 1, Math.Max(1, maxPerRow));
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));

        var width = Math.Max(m.MinWidth, Math.Ceiling(columns * m.CellWidth + m.ChromeWidth));
        var height = Math.Ceiling(rows * m.CellHeight + m.ChromeHeight);
        return (width, height, columns, rows);
    }

    /// <summary>
    /// Places each size in turn at the first free spot, scanning down a column
    /// then on to the next — the way Windows itself lays out desktop icons.
    /// A pad taller than the screen is shortened (it will scroll). A pad that
    /// fits nowhere is stacked at the start corner rather than dropped.
    /// </summary>
    public static List<PlacedRect> Place(
        IReadOnlyList<(double Width, double Height)> sizes,
        LayoutBounds area,
        IReadOnlyList<PlacedRect> obstacles,
        double gap,
        AutoStartCorner corner)
    {
        var placed = new List<PlacedRect>();
        var taken = new List<PlacedRect>(obstacles);

        foreach (var (rawWidth, rawHeight) in sizes)
        {
            var width = Math.Min(rawWidth, area.Width - 2 * gap);
            var height = Math.Min(rawHeight, area.Height - 2 * gap);

            var spot = FindSpot(width, height, area, taken, gap, corner)
                       ?? new PlacedRect(
                           corner == AutoStartCorner.TopRight ? area.Right - gap - width : area.Left + gap,
                           area.Top + gap, width, height);

            placed.Add(spot);
            taken.Add(spot);
        }

        return placed;
    }

    private static PlacedRect? FindSpot(
        double width, double height, LayoutBounds area, List<PlacedRect> taken, double gap, AutoStartCorner corner)
    {
        var minX = area.Left + gap;
        var maxX = area.Right - gap - width;
        var minY = area.Top + gap;
        var maxY = area.Bottom - gap - height;

        if (maxX < minX || maxY < minY)
            return null;

        var columns = (int)Math.Floor((maxX - minX) / Step) + 1;

        for (var c = 0; c < columns; c++)
        {
            var x = corner == AutoStartCorner.TopRight ? maxX - c * Step : minX + c * Step;

            for (var y = minY; y <= maxY; y += Step)
            {
                var candidate = new PlacedRect(x, y, width, height);
                var blocker = taken.FirstOrDefault(t => candidate.Intersects(t, gap));

                if (blocker == default)
                    return candidate;

                // Jump straight past whatever is in the way.
                y = Math.Max(y, blocker.Bottom + gap - Step);
            }
        }

        return null;
    }
}
