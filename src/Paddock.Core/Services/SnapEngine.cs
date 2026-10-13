using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>A guide line to draw while snapping: vertical (at X) or horizontal (at Y), spanning Start..End.</summary>
public readonly record struct SnapGuide(bool Vertical, double Position, double Start, double End);

/// <summary>Where a dragged pad should go, and which alignment guides to show.</summary>
public sealed record SnapResult(double X, double Y, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// PowerPoint-style snapping for a pad being dragged. On each axis it looks
/// at every target the pad could line up with and takes the nearest one in
/// reach:
/// <list type="bullet">
/// <item>alignment with another pad — left/right/centre, top/bottom/middle;</item>
/// <item>sitting next to another pad at the spacing gap;</item>
/// <item>the screen edges at the spacing gap.</item>
/// </list>
/// Alignments come with a guide line spanning both pads, so you can see what
/// you lined up with.
/// </summary>
public static class SnapEngine
{
    public const double SnapDistance = 10;

    private sealed record Candidate(double Delta, SnapGuide? Guide);

    public static SnapResult Snap(PlacedRect moving, IReadOnlyList<PlacedRect> others, LayoutBounds bounds, double gap)
    {
        var xCandidates = new List<Candidate>();
        var yCandidates = new List<Candidate>();

        // Screen edges (no guide; the edge itself is the cue).
        xCandidates.Add(new(bounds.Left + gap - moving.X, null));
        xCandidates.Add(new(bounds.Right - gap - moving.Right, null));
        yCandidates.Add(new(bounds.Top + gap - moving.Y, null));
        yCandidates.Add(new(bounds.Bottom - gap - moving.Bottom, null));

        foreach (var other in others)
        {
            var spanY = (Math.Min(moving.Y, other.Y), Math.Max(moving.Bottom, other.Bottom));
            var spanX = (Math.Min(moving.X, other.X), Math.Max(moving.Right, other.Right));

            // Vertical alignment lines (snap X): left, right, centre.
            AddAligned(xCandidates, other.X, moving.X, spanY, vertical: true);
            AddAligned(xCandidates, other.Right, moving.Right, spanY, vertical: true);
            AddAligned(xCandidates, other.X + other.Width / 2, moving.X + moving.Width / 2, spanY, vertical: true);

            // Horizontal alignment lines (snap Y): top, bottom, middle.
            AddAligned(yCandidates, other.Y, moving.Y, spanX, vertical: false);
            AddAligned(yCandidates, other.Bottom, moving.Bottom, spanX, vertical: false);
            AddAligned(yCandidates, other.Y + other.Height / 2, moving.Y + moving.Height / 2, spanX, vertical: false);

            // Side by side at the spacing gap.
            xCandidates.Add(new(other.Right + gap - moving.X, null));
            xCandidates.Add(new(other.X - gap - moving.Right, null));
            yCandidates.Add(new(other.Bottom + gap - moving.Y, null));
            yCandidates.Add(new(other.Y - gap - moving.Bottom, null));
        }

        var dx = Nearest(xCandidates);
        var dy = Nearest(yCandidates);

        var x = moving.X + (dx?.Delta ?? 0);
        var y = moving.Y + (dy?.Delta ?? 0);

        // Guides for every alignment that holds at the final position, not just the
        // one that won: snapping a top often lines up a bottom too.
        var guides = new List<SnapGuide>();
        if (dx is not null)
            guides.AddRange(xCandidates.Where(c => c.Guide is not null && Math.Abs(c.Delta - dx.Delta) < 0.5).Select(c => Shift(c.Guide!.Value, y - moving.Y)));
        if (dy is not null)
            guides.AddRange(yCandidates.Where(c => c.Guide is not null && Math.Abs(c.Delta - dy.Delta) < 0.5).Select(c => Shift(c.Guide!.Value, x - moving.X)));

        return new SnapResult(x, y, guides.Distinct().ToList());
    }

    /// <summary>
    /// A guide spans both pads along the other axis; it was measured before the
    /// snap on that axis moved the pad, so slide its span along with it.
    /// </summary>
    private static SnapGuide Shift(SnapGuide guide, double moved) => moved == 0
        ? guide
        : guide with { Start = guide.Start + Math.Min(0, moved), End = guide.End + Math.Max(0, moved) };

    private static void AddAligned(List<Candidate> candidates, double target, double current, (double Min, double Max) span, bool vertical)
        => candidates.Add(new(target - current, new SnapGuide(vertical, target, span.Min, span.Max)));

    /// <summary>
    /// Lining up with another pad is what people are after, so alignments get
    /// a head start over the gap and screen-edge snaps that happen to be a
    /// couple of pixels closer.
    /// </summary>
    private const double AlignmentPreference = 4;

    private static Candidate? Nearest(List<Candidate> candidates)
    {
        Candidate? best = null;
        var bestScore = double.MaxValue;

        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.Delta) > SnapDistance)
                continue;

            var score = Math.Abs(candidate.Delta) - (candidate.Guide is null ? 0 : AlignmentPreference);
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }
}
