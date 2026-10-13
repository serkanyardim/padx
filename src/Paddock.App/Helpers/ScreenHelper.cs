using System.Windows;
using Microsoft.Win32;
using Paddock.Core.Models;
using Screen = System.Windows.Forms.Screen;

namespace Paddock.App.Helpers;

/// <summary>
/// Bridges Win32 screen geometry (physical pixels) and WPF window geometry
/// (device-independent pixels).
///
/// Paddock windows are positioned with <see cref="Window.Left"/> /
/// <see cref="Window.Top"/>, which are DIPs measured from the primary monitor's
/// top-left corner — so every monitor rectangle we compare against has to be in
/// the same units.
///
/// The scale factor is derived by comparing what Win32 reports for the primary
/// monitor against what WPF reports, rather than assumed: that stays correct
/// whether the process ends up system-DPI-aware (WPF's default) or unaware.
///
/// Monitor geometry is cached because dragging a paddock asks for it on every
/// mouse move, and enumerating monitors is a Win32 round trip. The cache is
/// dropped whenever the display configuration changes.
/// </summary>
internal static class ScreenHelper
{
    private static readonly object Gate = new();

    private static IReadOnlyList<LayoutBounds>? _workAreas;
    private static double? _dipScale;
    private static double _reservedTop;
    private static double _reservedBottom;

    static ScreenHelper()
    {
        SystemEvents.DisplaySettingsChanged += (_, _) => Invalidate();
        SystemEvents.UserPreferenceChanged += (_, _) => Invalidate();
    }

    /// <summary>Physical pixels per device-independent pixel.</summary>
    internal static double DipScale
    {
        get
        {
            lock (Gate)
            {
                return _dipScale ??= MeasureDipScale();
            }
        }
    }

    /// <summary>Forgets cached monitor geometry; next use re-reads it.</summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _workAreas = null;
            _dipScale = null;
        }
    }

    /// <summary>
    /// Keeps a strip at the top or bottom of the main screen free of pads (the
    /// always-visible search box sits there), the way the taskbar's strip is:
    /// every work area handed out leaves it out. Zero for none.
    /// </summary>
    internal static void ReserveMainScreenStrip(double top, double bottom)
    {
        lock (Gate)
        {
            _reservedTop = top;
            _reservedBottom = bottom;
            _workAreas = null;
        }
    }

    /// <summary>The reserved strip on the main screen, in DIPs, or null when there is none.</summary>
    internal static LayoutBounds? ReservedStrip
    {
        get
        {
            double top, bottom;
            lock (Gate)
            {
                top = _reservedTop;
                bottom = _reservedBottom;
            }

            if (top <= 0 && bottom <= 0)
                return null;

            var full = GetFullWorkArea(underCursor: false);
            return top > 0
                ? new LayoutBounds(full.Left, full.Top, full.Width, top)
                : new LayoutBounds(full.Left, full.Bottom - bottom, full.Width, bottom);
        }
    }

    /// <summary>
    /// A monitor's whole work area in DIPs, ignoring any reserved strip: the
    /// one under the mouse, or the main screen.
    /// </summary>
    internal static LayoutBounds GetFullWorkArea(bool underCursor)
    {
        var screen = underCursor
            ? Screen.FromPoint(System.Windows.Forms.Control.MousePosition)
            : Screen.PrimaryScreen;

        if (screen is null)
            return new LayoutBounds(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        var scale = DipScale;
        var area = screen.WorkingArea;
        return new LayoutBounds(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
    }

    /// <summary>
    /// The main screen's resolution in real pixels, as a config key: "2560x1440".
    /// With several monitors it is the one Windows calls "main display".
    /// </summary>
    internal static string MainResolution()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? System.Drawing.Rectangle.Empty;
        return $"{bounds.Width}x{bounds.Height}";
    }

    /// <summary>Current mouse position in DIPs, valid across all monitors.</summary>
    internal static Point GetCursorPosition()
    {
        var position = System.Windows.Forms.Control.MousePosition;
        var scale = DipScale;
        return new Point(position.X / scale, position.Y / scale);
    }

    /// <summary>The union of all monitors, in DIPs.</summary>
    internal static LayoutBounds VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Work areas (screen minus taskbar) of every monitor, in DIPs.
    /// </summary>
    internal static IReadOnlyList<LayoutBounds> GetWorkAreas()
    {
        lock (Gate)
        {
            return _workAreas ??= MeasureWorkAreas();
        }
    }

    /// <summary>
    /// The work area a paddock belongs to: the monitor holding its centre, or
    /// failing that the one it overlaps most, or the primary monitor.
    /// </summary>
    internal static LayoutBounds GetWorkAreaFor(double x, double y, double width, double height)
    {
        var areas = GetWorkAreas();
        var centreX = x + width / 2;
        var centreY = y + height / 2;

        foreach (var area in areas)
        {
            if (area.Contains(centreX, centreY))
                return area;
        }

        var best = areas[0];
        var bestOverlap = 0.0;

        foreach (var area in areas)
        {
            var overlapWidth = Math.Min(x + width, area.Right) - Math.Max(x, area.Left);
            var overlapHeight = Math.Min(y + height, area.Bottom) - Math.Max(y, area.Top);
            if (overlapWidth <= 0 || overlapHeight <= 0)
                continue;

            var overlap = overlapWidth * overlapHeight;
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = area;
            }
        }

        return best;
    }

    /// <summary>The work area of the monitor the given paddock sits on.</summary>
    internal static LayoutBounds GetWorkAreaFor(PaddockModel paddock)
        => GetWorkAreaFor(paddock.X, paddock.Y, paddock.Width, paddock.Height);

    private static double MeasureDipScale()
    {
        var primary = Screen.PrimaryScreen;
        var logicalWidth = SystemParameters.PrimaryScreenWidth;

        if (primary is null || logicalWidth <= 0)
            return 1.0;

        var scale = primary.Bounds.Width / logicalWidth;
        return scale > 0 ? scale : 1.0;
    }

    private static IReadOnlyList<LayoutBounds> MeasureWorkAreas()
    {
        var scale = _dipScale ??= MeasureDipScale();
        var areas = new List<LayoutBounds>();

        foreach (var screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            var top = screen.Primary ? _reservedTop : 0;
            var bottom = screen.Primary ? _reservedBottom : 0;
            areas.Add(new LayoutBounds(
                area.X / scale,
                area.Y / scale + top,
                area.Width / scale,
                area.Height / scale - top - bottom));
        }

        if (areas.Count == 0)
        {
            areas.Add(new LayoutBounds(
                0,
                0,
                SystemParameters.PrimaryScreenWidth,
                SystemParameters.PrimaryScreenHeight));
        }

        return areas;
    }
}
