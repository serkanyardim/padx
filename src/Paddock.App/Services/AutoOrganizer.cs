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
/// Every file move is written to a ledger first, so REVERT can put each item
/// back exactly where it was (in its old pad, or on the desktop) and rebuild
/// the old pads with their original ids, positions and settings.
/// </summary>
internal sealed class AutoOrganizer
{
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan RevealStagger = TimeSpan.FromMilliseconds(70);

    /// <summary>Colour per category for <see cref="AutoColorMode.ByCategory"/>: dark, distinct hues.</summary>
    private static readonly Dictionary<AutoCategory, string> CategoryColors = new()
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

    /// <summary>
    /// One item Auto Organize handles. <see cref="Path"/> always holds where the
    /// item is right now, so an undo can find it at any point of a run.
    /// </summary>
    private sealed class Item(string path, string? originPadId)
    {
        public string Path { get; set; } = path;

        /// <summary>The pad it was in before the run; null when it was on the desktop.</summary>
        public string? OriginPadId { get; } = originPadId;

        public bool WasManaged { get; init; } = true;

        /// <summary>Came from the all-users desktop: moving it back needs elevation.</summary>
        public bool FromPublic { get; init; }

        /// <summary>A shortcut we generated for a built-in desktop icon (Recycle Bin, …).</summary>
        public SystemDesktopIcons.Definition? SystemIcon { get; init; }

        /// <summary>Set once the built-in icon has been hidden, so undo can restore the setting.</summary>
        public SystemDesktopIcons.Taken? HiddenIcon { get; set; }

        public AutoCategory? CategoryOverride { get; init; }
    }

    private const string SystemShortcutFolder = "_system";

    /// <summary>Everything decided before a single file moves.</summary>
    private sealed record Plan(List<(AutoCategory Category, List<Item> Items, PlacedRect Rect, string? Color)> Pads);

    private sealed record PendingRun(List<PaddockModel> RemovedPads, List<Item> Items, List<string> CreatedPadIds);

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

        var pads = Manager.GetPaddocks().ToList();
        var unlocked = pads.Where(p => !p.Locked).ToList();
        var desktopItems = _app.DesktopIconService.GetDesktopItems();
        var publicItems = _app.Settings.Settings.AutoMode.IncludeAllUsersDesktop
            ? _app.DesktopIconService.GetCommonDesktopItems()
            : [];
        var padItems = unlocked.Sum(p => p.Icons.Count);

        if (desktopItems.Count + publicItems.Count + padItems == 0)
        {
            DigitalDialog.Inform("Nothing to organize",
                pads.Count > 0
                    ? "No suitable icons found: the desktop is empty and every icon is already in a locked pad."
                    : "No suitable icons found: the desktop is empty.");
            return;
        }

        if (unlocked.Count > 0 && !DigitalDialog.Confirm("Auto organize",
                $"{unlocked.Count} unlocked pad(s) will be taken apart and rebuilt by category, together with " +
                $"{desktopItems.Count + publicItems.Count} item(s) from the desktop.\n\n" +
                "Locked pads stay exactly as they are. You can revert afterwards." +
                (publicItems.Count > 0
                    ? $"\n\n{publicItems.Count} shortcut(s) are on the all-users desktop; Windows will ask for administrator permission to move them."
                    : string.Empty),
                "ORGANIZE"))
        {
            return;
        }

        _busy = true;
        try
        {
            await OrganizeAsync(unlocked, desktopItems, publicItems);
        }
        finally
        {
            _app.ShellHookService.SetDesktopIconsVisible(true);
            _busy = false;
        }
    }

    private async Task OrganizeAsync(List<PaddockModel> unlocked, IReadOnlyList<string> desktopItems, IReadOnlyList<string> publicItems)
    {
        var animate = _app.Settings.Settings.AutoMode.Animate;

        var items = desktopItems.Select(p => new Item(p, null)).ToList();
        items.AddRange(publicItems.Select(p => new Item(p, null) { FromPublic = true }));
        foreach (var pad in unlocked)
            items.AddRange(pad.Icons.Select(i => new Item(i.DesktopPath, pad.Id) { WasManaged = i.Managed }));

        // All-users shortcuts first go (elevated) into a staging folder we own;
        // whatever the user declines simply stays out of the run.
        if (items.Any(i => i.FromPublic))
            await StagePublicItemsAsync(items);

        if (_app.Settings.Settings.AutoMode.IncludeSystemIcons)
            items.AddRange(CreateSystemIconShortcuts());

        // Decide everything first. If anything here fails, nothing has moved yet.
        Plan plan;
        try
        {
            plan = MakePlan(items, unlocked);
        }
        catch (Exception ex)
        {
            Log.Error("Auto organize could not plan the layout; nothing was changed.", ex);
            DeleteSystemShortcuts();
            DigitalDialog.Inform("Auto organize failed", "Could not work out a layout, so nothing was changed. Details are in the log (Settings ▸ General ▸ Open).");
            return;
        }

        if (Host.PaddocksHidden)
            Host.SetPaddocksVisible(true);

        var snapshot = unlocked.Select(Clone).ToList();
        var oldWindows = unlocked.Select(p => Host.FindWindow(p.Id)).OfType<PaddockWindow>().ToList();
        var createdIds = new List<string>();

        // Clear the stage: desktop icons and the pads being rebuilt disappear.
        _app.ShellHookService.SetDesktopIconsVisible(false);
        if (animate)
            await FadeAsync(oldWindows, 0, FadeOut);

        try
        {
            Execute(plan, unlocked, createdIds);
        }
        catch (Exception ex)
        {
            // Half-done: put everything back as it was.
            Log.Error("Auto organize failed while moving items; rolling back.", ex);
            await UndoAsync(new PendingRun(snapshot, items, createdIds), animate);
            DigitalDialog.Inform("Auto organize failed",
                "Something went wrong while moving items, so everything was put back the way it was. Details are in the log (Settings ▸ General ▸ Open).");
            return;
        }

        _app.DesktopIconService.NotifyShellOfDesktopChange();

        var created = createdIds.Select(Host.FindWindow).OfType<PaddockWindow>().ToList();
        await RevealAsync(created, animate);
        _app.ShellHookService.SetDesktopIconsVisible(true);

        var total = created.Sum(w => w.ViewModel.Icons.Count);
        Log.Info($"Auto organize: {total} item(s) into {created.Count} pad(s); {snapshot.Count} pad(s) rebuilt.");

        _pending = new PendingRun(snapshot, items, createdIds);
        ShowBar($"{created.Count} pads · {total} icons");
    }

    /// <summary>
    /// Moves the all-users shortcuts into the store's staging folder with one
    /// UAC prompt, updating their paths; any that did not move are dropped
    /// from the run.
    /// </summary>
    private async Task StagePublicItemsAsync(List<Item> items)
    {
        var store = _app.IconStore;
        var staging = store.EnsurePaddockFolder(DesktopIconService.PublicStagingFolder);
        var publicItems = items.Where(i => i.FromPublic).ToList();

        var requests = publicItems
            .Select(i => new ElevatedMover.MoveRequest(i.Path, System.IO.Path.Combine(staging, System.IO.Path.GetFileName(i.Path))))
            .ToList();

        // The elevated process is waited on off the UI thread.
        var done = await Task.Run(() => ElevatedMover.Run(requests)) ?? [];
        var movedTo = done.ToDictionary(d => d.From, d => d.To, StringComparer.OrdinalIgnoreCase);

        foreach (var item in publicItems)
        {
            if (movedTo.TryGetValue(item.Path, out var staged))
                item.Path = staged;
            else
                items.Remove(item);
        }

        Log.Info($"All-users desktop: {movedTo.Count} of {publicItems.Count} shortcut(s) taken in.");
    }

    /// <summary>
    /// Sends staged all-users shortcuts back to the all-users desktop (one UAC
    /// prompt). If that is declined they go to the user's own desktop instead,
    /// so nothing is ever left stranded in the store.
    /// </summary>
    private async Task ReturnStagedItemsAsync()
    {
        var store = _app.IconStore;
        var staged = store.ListPaddockItems(DesktopIconService.PublicStagingFolder);
        if (staged.Count == 0)
            return;

        var common = _app.DesktopIconService.CommonDesktopRoot;
        var requests = staged
            .Select(p => new ElevatedMover.MoveRequest(p, System.IO.Path.Combine(common, System.IO.Path.GetFileName(p))))
            .ToList();

        var done = await Task.Run(() => ElevatedMover.Run(requests));

        foreach (var leftover in store.ListPaddockItems(DesktopIconService.PublicStagingFolder))
            store.MoveToDesktop(leftover);

        if (done is null)
            Log.Info($"All-users shortcuts could not go back without permission; {staged.Count} put on your own desktop.");
    }

    /// <summary>
    /// After a crash mid-run, staged all-users shortcuts would sit unseen in
    /// the store. Put them on the user's desktop at startup (no prompt).
    /// </summary>
    public void RecoverStagedItems()
    {
        foreach (var item in _app.IconStore.ListPaddockItems(DesktopIconService.PublicStagingFolder))
            _app.IconStore.MoveToDesktop(item);

        DeleteSystemShortcuts();
    }

    /// <summary>
    /// Shortcuts for the built-in icons showing on the desktop, written to a
    /// staging folder in the store. Hiding the real icons waits for
    /// <see cref="Execute"/>, so a failed plan changes nothing.
    /// </summary>
    private List<Item> CreateSystemIconShortcuts()
    {
        var folder = _app.IconStore.EnsurePaddockFolder(SystemShortcutFolder);
        var items = new List<Item>();

        foreach (var icon in SystemDesktopIcons.Visible())
        {
            var name = string.Concat(SystemDesktopIcons.DisplayName(icon).Split(System.IO.Path.GetInvalidFileNameChars()));
            var link = System.IO.Path.Combine(folder, name + ".lnk");

            if (SystemDesktopIcons.CreateShortcut(icon, link))
            {
                items.Add(new Item(link, null)
                {
                    SystemIcon = icon,
                    CategoryOverride = AutoCategory.System,
                });
            }
        }

        return items;
    }

    /// <summary>Removes generated shortcuts that never made it into a pad.</summary>
    private void DeleteSystemShortcuts()
    {
        foreach (var leftover in _app.IconStore.ListPaddockItems(SystemShortcutFolder))
            TryDelete(leftover);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete '{path}': {ex.Message}");
        }
    }

    /// <summary>Categories, pad sizes, positions and colours — no side effects.</summary>
    private Plan MakePlan(List<Item> items, List<PaddockModel> unlocked)
    {
        var settings = _app.Settings.Settings;
        var auto = settings.AutoMode;

        // Categorise; tiny categories fold into "Other".
        var groups = items
            .GroupBy(i => i.CategoryOverride ?? IconCategorizer.Categorize(ShortcutReader.Describe(i.Path)))
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

    /// <summary>Takes the old pads apart and builds the planned ones, keeping every item's path current.</summary>
    private void Execute(Plan plan, List<PaddockModel> unlocked, List<string> createdIds)
    {
        var auto = _app.Settings.Settings.AutoMode;
        var byOrigin = plan.Pads.SelectMany(p => p.Items).Where(i => i.OriginPadId is not null).ToList();

        // Built-in desktop icons give way to the shortcuts that replace them.
        foreach (var item in plan.Pads.SelectMany(p => p.Items).Where(i => i.SystemIcon is not null))
            item.HiddenIcon = SystemDesktopIcons.Hide(item.SystemIcon!);

        if (plan.Pads.Any(p => p.Items.Any(i => i.HiddenIcon is not null)))
            SystemDesktopIcons.RefreshDesktop();

        using (Manager.DeferSave())
        {
            foreach (var pad in unlocked)
            {
                foreach (var item in byOrigin.Where(i => i.OriginPadId == pad.Id))
                {
                    if (item.WasManaged)
                    {
                        if (Manager.EjectIcon(pad.Id, item.Path) is { } onDesktop)
                            item.Path = onDesktop;
                    }
                    else
                    {
                        Manager.RemoveIconFromPaddock(pad.Id, item.Path);
                    }
                }

                if (Host.FindWindow(pad.Id) is { } window)
                    Host.RemovePaddock(window);
            }

            foreach (var (category, items, rect, color) in plan.Pads)
            {
                var model = Manager.CreatePaddock(IconCategorizer.Title(category), rect.X, rect.Y, rect.Width, rect.Height);
                createdIds.Add(model.Id);
                model.TitleVisibility = TitleVisibility.Always;
                model.Locked = auto.LockCreatedPads;
                model.Style.Color = color;

                // System icons keep a fixed order (This PC first … Recycle Bin last).
                var ordered = items;
                if (category == AutoCategory.System)
                {
                    model.SortBy = SortField.Manual;
                    ordered = items
                        .OrderBy(i => SystemDesktopIcons.Rank(
                            i.SystemIcon is { } icon ? "::" + icon.Clsid : ShortcutReader.Describe(i.Path).Target))
                        .ToList();
                }

                foreach (var item in ordered)
                {
                    if (Manager.AddIconToPaddock(model.Id, item.Path) is { } entry)
                        item.Path = entry.DesktopPath;
                }

                Host.AddWindow(model, show: false);
            }
        }
    }

    /// <summary>Keeps the new layout.</summary>
    public void Accept()
    {
        _pending = null;
        CloseBar();
    }

    /// <summary>Puts every moved item back and rebuilds the pads that were taken apart.</summary>
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
            await UndoAsync(run, _app.Settings.Settings.AutoMode.Animate);
        }
        finally
        {
            _app.ShellHookService.SetDesktopIconsVisible(true);
            _busy = false;
        }
    }

    /// <summary>
    /// Returns every item to where it started and rebuilds the removed pads.
    /// Works from any point of a run — finished, or failed halfway — because
    /// each item's current path is always up to date.
    /// </summary>
    private async Task UndoAsync(PendingRun run, bool animate)
    {
        try
        {
            var createdWindows = run.CreatedPadIds.Select(Host.FindWindow).OfType<PaddockWindow>().ToList();

            _app.ShellHookService.SetDesktopIconsVisible(false);
            if (animate)
                await FadeAsync(createdWindows, 0, FadeOut);

            var restoredWindows = new List<PaddockWindow>();

            using (Manager.DeferSave())
            {
                var restored = run.RemovedPads
                    .Where(p => Manager.GetPaddock(p.Id) is null)
                    .Select(Manager.RestorePaddock)
                    .ToList();

                foreach (var item in run.Items)
                {
                    var holder = run.CreatedPadIds
                        .Select(Manager.GetPaddock)
                        .FirstOrDefault(p => p?.Icons.Any(i => PathEquals(i.DesktopPath, item.Path)) == true);

                    if (item.SystemIcon is not null)
                    {
                        // Our generated shortcut goes; the real icon comes back.
                        if (holder is not null)
                            Manager.RemoveIconFromPaddock(holder.Id, item.Path);

                        TryDelete(item.Path);

                        if (item.HiddenIcon is not null)
                            SystemDesktopIcons.Restore(item.HiddenIcon);
                    }
                    else if (item.FromPublic)
                    {
                        // Back to staging; the elevated step below returns them.
                        if (holder is not null && _app.IconStore.MoveIntoPaddock(item.Path, DesktopIconService.PublicStagingFolder) is not null)
                            Manager.RemoveIconFromPaddock(holder.Id, item.Path);
                    }
                    else if (item.OriginPadId is null)
                    {
                        if (holder is not null)
                            Manager.EjectIcon(holder.Id, item.Path);
                    }
                    else if (holder is not null)
                    {
                        Manager.MoveIconBetweenPaddocks(holder.Id, item.OriginPadId, item.Path);
                    }
                    else if (Manager.GetPaddock(item.OriginPadId) is { } origin
                             && !origin.Icons.Any(i => PathEquals(i.DesktopPath, item.Path)))
                    {
                        // Taken out of its pad but not placed yet (a run that failed halfway).
                        Manager.AddIconToPaddock(item.OriginPadId, item.Path);
                    }
                }

                // Anything left (added after the run) goes safely to the desktop with its pad.
                foreach (var window in createdWindows)
                    Host.RemovePaddock(window);

                foreach (var model in restored)
                    restoredWindows.Add(Host.AddWindow(model, show: false));
            }

            if (run.Items.Any(i => i.FromPublic))
                await ReturnStagedItemsAsync();

            if (run.Items.Any(i => i.HiddenIcon is not null))
                SystemDesktopIcons.RefreshDesktop();

            // Pads that were faded out but never removed come back too.
            foreach (var window in Host.Windows.Where(w => w.Opacity < 1 && !restoredWindows.Contains(w)))
                window.Opacity = 1;

            _app.DesktopIconService.NotifyShellOfDesktopChange();
            await RevealAsync(restoredWindows.Where(w => !Host.IsHiddenPad(w)).ToList(), animate);
            Log.Info($"Auto organize undone: {run.Items.Count} item(s), {restoredWindows.Count} pad(s) restored.");
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
    private static PadMetrics CurrentMetrics(PadAppearance a)
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
