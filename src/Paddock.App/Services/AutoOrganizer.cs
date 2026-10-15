using System.Text.Json;
using System.Windows;
using System.Windows.Media.Animation;
using Paddock.App.Helpers;
using Paddock.App.Views;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// Auto Organize: gathers everything on the desktop plus the contents of every
/// unlocked pad, sorts it into categories, and lays out one snugly-sized pad
/// per category around the locked pads.
///
/// No file moves — pads only point at items, which stay where they are — so
/// REVERT simply rebuilds the old pads, with their ids, places, settings and
/// contents, from a snapshot.
/// </summary>
internal sealed class AutoOrganizer
{
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan RevealStagger = TimeSpan.FromMilliseconds(70);

    /// <summary>Colour per category for <see cref="AutoColorMode.ByCategory"/>: dark, distinct hues.</summary>
    /// <summary>Colour per category for <see cref="AutoColorMode.ByCategory"/>: dark, distinct hues.</summary>
    internal static readonly Dictionary<AutoCategory, string> CategoryColors = new()
    {
        [AutoCategory.System] = "#1E2A3A",
        [AutoCategory.Folders] = "#4A3B12",
        [AutoCategory.Documents] = "#10233F",
        [AutoCategory.Media] = "#2E1A3D",
        [AutoCategory.Office] = "#12355B",
        [AutoCategory.Browsers] = "#0E3B43",
        [AutoCategory.Links] = "#0B3A4A",
        [AutoCategory.Communication] = "#2A1F55",
        [AutoCategory.Development] = "#133A2A",
        [AutoCategory.Remote] = "#1F3A3A",
        [AutoCategory.Design] = "#4A2511",
        [AutoCategory.Games] = "#3D1520",
        [AutoCategory.Utilities] = "#2B2F36",
        [AutoCategory.Apps] = "#1B2433",
        [AutoCategory.Archives] = "#3A3A1A",
        [AutoCategory.Installers] = "#2A2F3A",
        [AutoCategory.Other] = "#12161C",
    };

    private readonly App _app;
    private bool _busy;
    private PendingRun? _pending;
    private OrganizeBar? _bar;

    /// <summary>One item Auto Organize sorts.</summary>
    private sealed class Item(string path)
    {
        public string Path { get; } = path;
    }

    /// <summary>Everything decided before anything changes.</summary>
    private sealed record Plan(List<(AutoCategory Category, List<Item> Items, PlacedRect Rect, string? Color)> Pads);

    /// <summary>What Revert needs: the pads as they were, and the ones the run made.</summary>
    private sealed record PendingRun(List<PaddockModel> RemovedPads, List<string> CreatedPadIds);

    public AutoOrganizer(App app)
    {
        _app = app;
    }

    private PaddockHost Host => _app.Host!;

    private PaddockManager Manager => _app.PaddockManager;

    public async Task RunAsync()
    {
        if (_busy || _app.Host is null)
            return;

        // A new run implicitly keeps the previous result.
        Accept();

        // Rebuilt: every unlocked pad, plus — locked or not — hidden pads (out of
        // sight, so their contents get sorted again) and empty pads (removed).
        // Everything is in the revert snapshot, so Revert brings them all back.
        var pads = Manager.GetPaddocks().ToList();
        var rebuild = pads.Where(p => !p.Locked || p.Hidden || p.Icons.Count == 0).ToList();
        var hidden = rebuild.Count(p => p.Hidden);
        var empty = rebuild.Count(p => p.Icons.Count == 0);
        var loose = LooseItems();
        var padItems = rebuild.Sum(p => p.Icons.Count);

        if (loose.Count + padItems == 0 && rebuild.Count == 0)
        {
            DigitalDialog.Inform("Nothing to organize",
                pads.Count > 0
                    ? "No suitable icons found: the desktop is empty and every icon is already in a locked pad."
                    : "No suitable icons found: the desktop is empty.");
            return;
        }

        var details = new List<string>();
        if (hidden > 0) details.Add(Lang.F("{0} hidden", hidden));
        if (empty > 0) details.Add(Lang.F("{0} empty, which will be removed", empty));
        var padsLine = rebuild.Count == 0
            ? string.Empty
            : Lang.F("{0} pad(s){1} will be taken apart and rebuilt by category, together with ",
                rebuild.Count, details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty);

        if (rebuild.Count > 0 && !DigitalDialog.Confirm("Auto organize",
                padsLine +
                Lang.F("{0} item(s) from the desktop.", loose.Count) + "\n\n" +
                Lang.T("Locked pads that are visible and not empty stay exactly as they are. You can revert afterwards."),
                "ORGANIZE"))
        {
            return;
        }

        _busy = true;
        try
        {
            await OrganizeAsync(rebuild, loose);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// What is on the desktop and in no pad — as padx's desktop shows it —
    /// minus what Auto Mode leaves out (all-users shortcuts, built-in icons).
    /// </summary>
    private List<string> LooseItems()
    {
        var auto = _app.Settings.Settings.AutoMode;
        var common = _app.DesktopIconService.CommonDesktopRoot;

        return (_app.DesktopLayer?.Icons ?? [])
            .Select(i => i.FullPath)
            .Where(p => auto.IncludeAllUsersDesktop || !string.Equals(System.IO.Path.GetDirectoryName(p), common, StringComparison.OrdinalIgnoreCase))
            .Where(p => auto.IncludeSystemIcons || !p.StartsWith(SystemIconShortcuts.Folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task OrganizeAsync(List<PaddockModel> unlocked, List<string> loose)
    {
        var animate = _app.Settings.Settings.AutoMode.Animate;

        var items = loose.Select(p => new Item(p)).ToList();
        foreach (var pad in unlocked)
            items.AddRange(pad.Icons.Select(i => new Item(i.DesktopPath)));

        // Decide everything first. If anything here fails, nothing has changed.
        Plan plan;
        try
        {
            // Reading every shortcut (targets, version info, installer lookups)
            // happens off the UI thread, so a slow disk or share never freezes it.
            Dictionary<Item, AutoCategory> categories;
            using (Activity.Begin("auto organize: reading shortcuts"))
            {
                categories = await Task.Run(() => items.ToDictionary(
                    i => i,
                    i => IconCategorizer.Categorize(ShortcutReader.Describe(i.Path))));
            }

            plan = MakePlan(items, categories, unlocked);
        }
        catch (Exception ex)
        {
            Log.Error("Auto organize could not plan the layout; nothing was changed.", ex);
            DigitalDialog.Inform("Auto organize failed", "Could not work out a layout, so nothing was changed. Details are in the log (Settings ▸ General ▸ Open).");
            return;
        }

        if (Host.PaddocksHidden)
            Host.SetPaddocksVisible(true);

        var snapshot = unlocked.Select(Clone).ToList();
        var oldWindows = unlocked.Select(p => Host.FindWindow(p.Id)).OfType<PaddockWindow>().ToList();
        var createdIds = new List<string>();

        if (animate)
            await FadeAsync(oldWindows, 0, FadeOut);

        try
        {
            using (Activity.Begin("auto organize: building pads"))
                Execute(plan, unlocked, createdIds);
        }
        catch (Exception ex)
        {
            // Half-done: put everything back as it was.
            Log.Error("Auto organize failed while building pads; rolling back.", ex);
            await UndoAsync(new PendingRun(snapshot, createdIds), animate);
            DigitalDialog.Inform("Auto organize failed",
                "Something went wrong while moving items, so everything was put back the way it was. Details are in the log (Settings ▸ General ▸ Open).");
            return;
        }

        var created = createdIds.Select(Host.FindWindow).OfType<PaddockWindow>().ToList();
        await RevealAsync(created, animate);

        var total = created.Sum(w => w.ViewModel.Icons.Count);
        Log.Info($"Auto organize: {total} item(s) into {created.Count} pad(s); {snapshot.Count} pad(s) rebuilt.");

        _pending = new PendingRun(snapshot, createdIds);
        ShowBar(Lang.F("{0} pads · {1} icons", created.Count, total));
    }


    /// <summary>Categories, pad sizes, positions and colours — no side effects.</summary>
    private Plan MakePlan(List<Item> items, Dictionary<Item, AutoCategory> categories, List<PaddockModel> unlocked)
    {
        var settings = _app.Settings.Settings;
        var auto = settings.AutoMode;

        // Group by category; tiny categories fold into "Other".
        var groups = items
            .GroupBy(i => categories[i])
            .ToDictionary(g => g.Key, g => g.ToList());

        // "System" always gets its own pad, even for a lone Recycle Bin.
        foreach (var small in groups.Where(g => g.Key is not AutoCategory.Other and not AutoCategory.System
                                                && g.Value.Count < auto.MinItemsPerPad).ToList())
        {
            if (!groups.TryGetValue(AutoCategory.Other, out var other))
                groups[AutoCategory.Other] = other = new();

            other.AddRange(small.Value);
            groups.Remove(small.Key);
        }

        var ordered = groups.OrderBy(g => g.Key).ToList();

        // Size each pad to its icons and pack them around the pads that stay.
        var metrics = CurrentMetrics(settings.Appearance);
        var sizes = ordered
            .Select(g => AutoLayoutPlanner.Measure(g.Value.Count, auto.MaxIconsPerRow, metrics))
            .Select(m => (m.Width, m.Height))
            .ToList();

        var rebuilt = unlocked.Select(p => p.Id).ToHashSet();
        var obstacles = Manager.GetPaddocks()
            .Where(p => !rebuilt.Contains(p.Id))
            .Select(p => new PlacedRect(p.X, p.Y, p.Width, p.IsRolledUp ? ViewModels.PaddockViewModel.TitleBarHeight : p.Height))
            .ToList();

        var workArea = SystemParameters.WorkArea;
        var bounds = new LayoutBounds(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        var gap = Math.Max(8, settings.IntelligentSpacing);
        var hasSystem = ordered.Count > 0 && ordered[0].Key == AutoCategory.System;
        var counts = ordered.Select(g => g.Value.Count).ToList();

        // Preferred: a tidy table (aligned tops, aligned columns, even gaps).
        var rects = PlanTable(counts, sizes, metrics, bounds, gap, hasSystem, auto);

        // Fallback when the table cannot fit, or would cover a locked pad.
        if (rects is null || rects.Any(r => obstacles.Any(o => r.Intersects(o, gap / 2))))
            rects = PlanFreeform(sizes, bounds, obstacles, gap, hasSystem, auto.StartCorner);

        var colors = PickColors(ordered.Select(g => g.Key).ToList(), auto.ColorMode);

        return new Plan(ordered.Select((g, i) => (g.Key, g.Value, rects[i], colors[i])).ToList());
    }

    /// <summary>
    /// The table layout. The System pad always sits top-left: as the table's
    /// first cell when filling from the left, or on its own in the corner
    /// with the table built from the right.
    /// </summary>
    private static List<PlacedRect>? PlanTable(
        List<int> counts, List<(double Width, double Height)> sizes, PadMetrics metrics,
        LayoutBounds bounds, double gap, bool hasSystem, AutoModeSettings auto)
    {
        if (!hasSystem || auto.StartCorner == AutoStartCorner.TopLeft)
            return AutoLayoutPlanner.PlanGrid(counts, auto.MaxIconsPerRow, metrics, bounds, gap, auto.StartCorner, keepFirst: hasSystem);

        var system = new PlacedRect(bounds.Left + gap, bounds.Top + gap, sizes[0].Width, sizes[0].Height);
        var rest = AutoLayoutPlanner.PlanGrid(counts.Skip(1).ToList(), auto.MaxIconsPerRow, metrics, bounds, gap, AutoStartCorner.TopRight, keepFirst: false);

        if (rest is null || rest.Any(r => r.Intersects(system, gap / 2)))
            return null;

        return [system, .. rest];
    }

    /// <summary>The older first-free-spot packing; it can flow around locked pads.</summary>
    private static List<PlacedRect> PlanFreeform(
        List<(double Width, double Height)> sizes, LayoutBounds bounds, List<PlacedRect> obstacles,
        double gap, bool hasSystem, AutoStartCorner corner)
    {
        var rects = new List<PlacedRect>();
        var remaining = sizes.ToList();

        if (hasSystem)
        {
            var system = AutoLayoutPlanner.Place([remaining[0]], bounds, obstacles, gap, AutoStartCorner.TopLeft)[0];
            rects.Add(system);
            obstacles = [.. obstacles, system];
            remaining.RemoveAt(0);
        }

        rects.AddRange(AutoLayoutPlanner.Place(remaining, bounds, obstacles, gap, corner));
        return rects;
    }

    /// <summary>Removes the old pads and builds the planned ones. Items stay where they are.</summary>
    private void Execute(Plan plan, List<PaddockModel> unlocked, List<string> createdIds)
    {
        var auto = _app.Settings.Settings.AutoMode;

        using (Manager.DeferSave())
        {
            foreach (var pad in unlocked)
            {
                if (Host.FindWindow(pad.Id) is { } window)
                    Host.RemovePaddock(window);
                else
                    Manager.DeletePaddock(pad.Id);
            }

            foreach (var (category, items, rect, color) in plan.Pads)
            {
                var model = Manager.CreatePaddock(Lang.T(IconCategorizer.Title(category)), rect.X, rect.Y, rect.Width, rect.Height);
                createdIds.Add(model.Id);
                model.TitleVisibility = TitleVisibility.Always;
                model.Locked = auto.LockCreatedPads;
                model.Style.Color = color;

                // System icons keep a fixed order (This PC first … Recycle Bin last).
                var ordered = items;
                if (category == AutoCategory.System)
                {
                    model.SortBy = SortField.Manual;
                    ordered = items.OrderBy(i => SystemDesktopIcons.Rank(ShortcutReader.Describe(i.Path).Target)).ToList();
                }

                foreach (var item in ordered)
                    Manager.AddIconToPaddock(model.Id, item.Path);

                Host.AddWindow(model, show: false);
            }
        }
    }


    /// <summary>
    /// Arrange Pads: the same tidy table as Auto Organize, applied to the pads
    /// that exist — each resized to fit its icons and moved into place. No pads
    /// are created or removed and no file moves, so there is nothing to revert.
    /// Locked pads stay put (the rest go around them); hidden pads are left alone.
    /// </summary>
    public void ArrangePads()
    {
        if (_busy || _app.Host is null)
            return;

        Accept();

        var settings = _app.Settings.Settings;
        var auto = settings.AutoMode;

        var visible = Host.Windows.Where(w => w.IsVisible)
            .Select(w => Manager.GetPaddock(w.ViewModel.Id))
            .OfType<PaddockModel>()
            .ToList();

        var movable = visible.Where(p => !p.Locked).ToList();
        if (movable.Count == 0)
        {
            DigitalDialog.Inform("Arrange pads", "There are no unlocked pads to arrange.");
            return;
        }

        // The System pad (built-in icons) keeps its top-left place.
        var system = movable.FirstOrDefault(p =>
            string.Equals(p.Title, IconCategorizer.Title(AutoCategory.System), StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Title, Lang.T(IconCategorizer.Title(AutoCategory.System)), StringComparison.OrdinalIgnoreCase));
        if (system is not null)
        {
            movable.Remove(system);
            movable.Insert(0, system);
        }

        // Every pad keeps the size it has now — its own rows and columns of
        // icons — and only moves (a rolled-up pad counts as its title strip).
        var sizes = movable
            .Select(p => (p.Width, p.IsRolledUp ? ViewModels.PaddockViewModel.TitleBarHeight : p.Height))
            .ToList();

        var obstacles = visible.Where(p => p.Locked)
            .Select(p => new PlacedRect(p.X, p.Y, p.Width, p.IsRolledUp ? ViewModels.PaddockViewModel.TitleBarHeight : p.Height))
            .ToList();

        var workArea = SystemParameters.WorkArea;
        var bounds = new LayoutBounds(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        var gap = Math.Max(8, settings.IntelligentSpacing);

        var rects = AutoLayoutPlanner.PlanGridFixedSizes(sizes, bounds, gap, auto.StartCorner, keepFirst: system is not null);
        if (rects is null || rects.Any(r => obstacles.Any(o => r.Intersects(o, gap / 2))))
            rects = PlanFreeform(sizes, bounds, obstacles, gap, system is not null, auto.StartCorner);

        using (Manager.DeferSave())
        {
            for (var i = 0; i < movable.Count; i++)
            {
                movable[i].X = rects[i].X;
                movable[i].Y = rects[i].Y;
                Manager.SaveLayout();
                Host.FindWindow(movable[i].Id)?.ApplyModelBounds();
            }
        }
        Log.Info($"Arrange pads: {movable.Count} pad(s) arranged, {obstacles.Count} locked pad(s) left in place.");
    }

    /// <summary>Keeps the new layout.</summary>
    public void Accept()
    {
        _pending = null;
        CloseBar();
    }

    /// <summary>Rebuilds the pads that were taken apart, as they were.</summary>
    public async Task RevertAsync()
    {
        var run = _pending;
        if (run is null || _busy)
            return;

        _pending = null;
        CloseBar();
        _busy = true;

        try
        {
            using (Activity.Begin("auto organize: reverting"))
                await UndoAsync(run, _app.Settings.Settings.AutoMode.Animate);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Removes the pads the run made and brings back the ones it removed —
    /// same ids, places, settings, and the same items in the same order.
    /// Works from any point of a run, finished or failed halfway.
    /// </summary>
    private async Task UndoAsync(PendingRun run, bool animate)
    {
        try
        {
            var createdWindows = run.CreatedPadIds.Select(Host.FindWindow).OfType<PaddockWindow>().ToList();

            if (animate)
                await FadeAsync(createdWindows, 0, FadeOut);

            var restoredWindows = new List<PaddockWindow>();

            using (Manager.DeferSave())
            {
                foreach (var window in createdWindows)
                    Host.RemovePaddock(window);

                foreach (var snapshot in run.RemovedPads)
                {
                    // A pad that failed to go away comes back in one piece, without duplicates.
                    if (Host.FindWindow(snapshot.Id) is { } leftover)
                        Host.RemovePaddock(leftover);

                    var model = Manager.RestorePaddock(snapshot);
                    foreach (var icon in snapshot.Icons)
                        Manager.AddIconToPaddock(model.Id, icon.DesktopPath);

                    restoredWindows.Add(Host.AddWindow(model, show: false));
                }
            }

            // Pads that were faded out but never removed come back too.
            foreach (var window in Host.Windows.Where(w => w.Opacity < 1 && !restoredWindows.Contains(w)))
                window.Opacity = 1;

            await RevealAsync(restoredWindows.Where(w => !Host.IsHiddenPad(w)).ToList(), animate);
            Log.Info($"Auto organize undone: {restoredWindows.Count} pad(s) restored.");
        }
        catch (Exception ex)
        {
            Log.Error("Undoing auto organize failed; items are on the desktop or in pads.", ex);
        }
    }


    /// <summary>
    /// Grid geometry matching PaddockControl.xaml: a cell is the icon plus its
    /// padding and 2px margin on each side; the grid sits inside the edge, the
    /// 36px title strip and the scroll viewer's margins (6,36,4,6).
    /// </summary>
    internal static PadMetrics CurrentMetrics(PadAppearance a)
    {
        var edge = Math.Max(1, a.BorderThickness);
        var cellWidth = a.IconSize + 32 + 4;
        var cellHeight = (a.ShowIconLabels ? a.IconSize + 40 : a.IconSize + 14) + 4;

        // +2 of slack so rounding never wraps the last column or adds a scrollbar.
        return new PadMetrics(cellWidth, cellHeight, 2 * edge + 6 + 4 + 2, 2 * edge + 36 + 6 + 2, 140);
    }

    private static List<string?> PickColors(List<AutoCategory> categories, AutoColorMode mode) => mode switch
    {
        AutoColorMode.ByCategory => categories.Select(c => CategoryColors.GetValueOrDefault(c, CategoryColors[AutoCategory.Other])).Select(c => (string?)c).ToList(),
        AutoColorMode.Random => RandomColors(categories.Count),
        AutoColorMode.Glass => categories.Select(_ => (string?)PadMaterials.Glass).ToList(),
        _ => categories.Select(_ => (string?)null).ToList(),
    };

    /// <summary>Random palette colours, without repeats until the palette runs out. Snow is too bright for a pad full of icons.</summary>
    private static List<string?> RandomColors(int count)
    {
        var pool = PadPalette.PadColors.Where(c => c.Name != "Snow").Select(c => c.Hex).ToList();
        var result = new List<string?>(count);

        while (result.Count < count)
        {
            var shuffled = pool.OrderBy(_ => Random.Shared.Next()).ToList();
            result.AddRange(shuffled.Take(count - result.Count));
        }

        return result;
    }

    private static async Task RevealAsync(List<PaddockWindow> windows, bool animate)
    {
        foreach (var window in windows)
        {
            if (animate)
                window.Opacity = 0;

            window.Show();
            window.PinToDesktop();

            if (animate)
            {
                Animate(window, 1, FadeIn);
                await Task.Delay(RevealStagger);
            }
        }

        if (animate)
            await Task.Delay(FadeIn);
    }

    private static async Task FadeAsync(IEnumerable<PaddockWindow> windows, double to, TimeSpan duration)
    {
        foreach (var window in windows)
            Animate(window, to, duration);

        await Task.Delay(duration);
    }

    private static void Animate(Window window, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation(to, duration) { EasingFunction = new QuadraticEase() };

        // Hand the final value back to the property so later fades start from it.
        animation.Completed += (_, _) =>
        {
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = to;
        };

        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void ShowBar(string summary)
    {
        CloseBar();
        _bar = new OrganizeBar(summary, Accept, () => _ = RevertAsync());
        _bar.Show();
    }

    private void CloseBar()
    {
        _bar?.Close();
        _bar = null;
    }

    private static PaddockModel Clone(PaddockModel model)
        => JsonSerializer.Deserialize<PaddockModel>(JsonSerializer.Serialize(model))!;

    private static bool PathEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
