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
    /// Lays pads out as a tidy table: every row's pads share the same top
    /// edge, every column the same left edge and width, and all gaps are
    /// equal. A pad narrower than its column is widened to it and re-flows
    /// its icons into the extra space (so it gets shorter, never emptier).
    ///
    /// Each row is led by its tallest pads; where a short pad leaves room
    /// below it in the row, the shortest remaining pads are stacked into that
    /// space — so a small System pad next to a tall Apps pad does not leave a
    /// hole. The first pad (<paramref name="keepFirst"/>) stays in the top-left
    /// cell. The table uses the fewest columns that fit on screen.
    /// </summary>
    /// <returns>One rectangle per input count, in input order, or null if no table fits.</returns>
    public static List<PlacedRect>? PlanGrid(
        IReadOnlyList<int> counts,
        int maxPerRow,
        PadMetrics m,
        LayoutBounds area,
        double gap,
        AutoStartCorner corner,
        bool keepFirst)
    {
        var n = counts.Count;
        if (n == 0)
            return [];

        maxPerRow = Math.Max(1, maxPerRow);

        var order = Enumerable.Range(0, n).ToList();
        var rest = order.Skip(keepFirst ? 1 : 0)
            .OrderByDescending(i => Measure(counts[i], maxPerRow, m).Height)
            .ThenBy(i => i);
        order = (keepFirst ? [0] : new List<int>()).Concat(rest).ToList();

        var usableWidth = area.Width - 2 * gap;
        var usableHeight = area.Height - 2 * gap;

        for (var columns = 1; columns <= n; columns++)
        {
            var grid = Arrange(order, columns, i => Measure(counts[i], maxPerRow, m).Height, gap);
            var table = FitWidth(grid, columns, counts, maxPerRow, m, gap, usableWidth);

            if (table.TotalWidth <= usableWidth && table.TotalHeight <= usableHeight)
                return Position(table, area, gap, corner, n);
        }

        return null;
    }

    /// <summary>Rows → cells (one per column) → the pads stacked in that cell, top to bottom.</summary>
    private sealed record Table(
        List<List<List<int>>> Grid, double[] ColumnWidths, double[] RowHeights,
        Dictionary<int, double> PadHeights, double TotalWidth, double TotalHeight);

    /// <summary>
    /// Fills rows from the front of the order (tallest first), and tops up the
    /// space under each short pad with the shortest pads from the back.
    /// </summary>
    private static List<List<List<int>>> Arrange(List<int> order, int columns, Func<int, double> height, double gap)
    {
        var queue = new LinkedList<int>(order);
        var rows = new List<List<List<int>>>();

        while (queue.Count > 0)
        {
            var row = new List<List<int>>();
            while (row.Count < columns && queue.Count > 0)
            {
                row.Add([queue.First!.Value]);
                queue.RemoveFirst();
            }

            var rowHeight = row.Max(cell => height(cell[0]));

            foreach (var cell in row)
            {
                var free = rowHeight - height(cell[0]);
                while (queue.Count > 0 && height(queue.Last!.Value) + gap <= free)
                {
                    free -= height(queue.Last.Value) + gap;
                    cell.Add(queue.Last.Value);
                    queue.RemoveLast();
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Sizes the columns to their widest pad; if the table is then too wide
    /// for the screen, narrows columns one icon at a time, each time the one
    /// whose pads grow taller the least.
    /// </summary>
    private static Table FitWidth(
        List<List<List<int>>> grid, int columns, IReadOnlyList<int> counts, int maxPerRow, PadMetrics m, double gap, double usableWidth)
    {
        var cells = new int[columns];
        foreach (var row in grid)
        {
            for (var c = 0; c < row.Count; c++)
            {
                foreach (var pad in row[c])
                    cells[c] = Math.Max(cells[c], Math.Clamp(counts[pad], 1, maxPerRow));
            }
        }

        while (Layout(grid, counts, cells, m, gap).TotalWidth > usableWidth)
        {
            var best = -1;
            var bestHeight = double.MaxValue;

            for (var c = 0; c < columns; c++)
            {
                if (cells[c] <= 1)
                    continue;

                cells[c]--;
                var height = Layout(grid, counts, cells, m, gap).TotalHeight;
                cells[c]++;

                if (height < bestHeight)
                {
                    bestHeight = height;
                    best = c;
                }
            }

            if (best < 0)
                break;

            cells[best]--;
        }

        return Layout(grid, counts, cells, m, gap);
    }

    private static Table Layout(List<List<List<int>>> grid, IReadOnlyList<int> counts, int[] cells, PadMetrics m, double gap)
    {
        var widths = cells.Select(c => Math.Max(m.MinWidth, Math.Ceiling(Math.Max(1, c) * m.CellWidth + m.ChromeWidth))).ToArray();
        var padHeights = new Dictionary<int, double>();
        var rowHeights = new double[grid.Count];

        for (var r = 0; r < grid.Count; r++)
        {
            for (var c = 0; c < grid[r].Count; c++)
            {
                // Every pad in the cell re-flows to the column's width.
                var stack = 0.0;
                foreach (var pad in grid[r][c])
                {
                    var iconRows = Math.Max(1, (int)Math.Ceiling(counts[pad] / (double)Math.Max(1, cells[c])));
                    padHeights[pad] = Math.Ceiling(iconRows * m.CellHeight + m.ChromeHeight);
                    stack += padHeights[pad] + (stack > 0 ? gap : 0);
                }

                rowHeights[r] = Math.Max(rowHeights[r], stack);
            }
        }

        var totalWidth = widths.Sum() + (cells.Length - 1) * gap;
        var totalHeight = rowHeights.Sum() + (grid.Count - 1) * gap;
        return new Table(grid, widths, rowHeights, padHeights, totalWidth, totalHeight);
    }

    private static List<PlacedRect> Position(Table table, LayoutBounds area, double gap, AutoStartCorner corner, int count)
    {
        var result = new PlacedRect[count];

        // From the right, the columns are mirrored so column 0 hugs the right edge.
        var columnX = new double[table.ColumnWidths.Length];
        var x = corner == AutoStartCorner.TopRight ? area.Right - gap : area.Left + gap;
        for (var c = 0; c < columnX.Length; c++)
        {
            if (corner == AutoStartCorner.TopRight)
            {
                x -= table.ColumnWidths[c];
                columnX[c] = x;
                x -= gap;
            }
            else
            {
                columnX[c] = x;
                x += table.ColumnWidths[c] + gap;
            }
        }

        var rowTop = area.Top + gap;
        for (var r = 0; r < table.Grid.Count; r++)
        {
            for (var c = 0; c < table.Grid[r].Count; c++)
            {
                var y = rowTop;
                foreach (var pad in table.Grid[r][c])
                {
                    result[pad] = new PlacedRect(columnX[c], y, table.ColumnWidths[c], table.PadHeights[pad]);
                    y += table.PadHeights[pad] + gap;
                }
            }

            rowTop += table.RowHeights[r] + gap;
        }

        return result.ToList();
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
