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
        [AutoCategory.Other] = "#12161C",
    };

    private readonly App _app;
    private bool _busy;
    private PendingRun? _pending;
    private OrganizeBar? _bar;

    /// <summary>One file that Auto Organize moved: where it came from, where it is now.</summary>
    private sealed record Move(string? OriginPadId, string CurrentPath);

    private sealed record PendingRun(List<PaddockModel> RemovedPads, List<Move> Moves, List<string> CreatedPadIds);

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
        var padItems = unlocked.Sum(p => p.Icons.Count);

        if (desktopItems.Count + padItems == 0)
        {
            DigitalDialog.Inform("Nothing to organize",
                pads.Count > 0
                    ? "No suitable icons found: the desktop is empty and every icon is already in a locked pad."
                    : "No suitable icons found: the desktop is empty.");
            return;
        }

        if (unlocked.Count > 0 && !DigitalDialog.Confirm("Auto organize",
                $"{unlocked.Count} unlocked pad(s) will be taken apart and rebuilt by category, together with " +
                $"{desktopItems.Count} item(s) from the desktop.\n\n" +
                "Locked pads stay exactly as they are. You can revert afterwards.",
                "ORGANIZE"))
        {
            return;
        }

        _busy = true;
        try
        {
            await OrganizeAsync(unlocked, desktopItems);
        }
        catch (Exception ex)
        {
            Log.Error("Auto organize failed.", ex);
            DigitalDialog.Inform("Auto organize failed", "Something went wrong; see paddock.log. Your files are safe on the desktop or in pads.");
        }
        finally
        {
            _app.ShellHookService.SetDesktopIconsVisible(true);
            _busy = false;
        }
    }

    private async Task OrganizeAsync(List<PaddockModel> unlocked, IReadOnlyList<string> desktopItems)
    {
        var settings = _app.Settings.Settings;
        var auto = settings.AutoMode;
        var animate = auto.Animate;

        if (Host.PaddocksHidden)
            Host.SetPaddocksVisible(true);

        var snapshot = unlocked.Select(Clone).ToList();
        var oldWindows = unlocked.Select(p => Host.FindWindow(p.Id)).OfType<PaddockWindow>().ToList();

        // 1. Clear the stage: desktop icons and the pads being rebuilt disappear.
        _app.ShellHookService.SetDesktopIconsVisible(false);
        if (animate)
            await FadeAsync(oldWindows, 0, FadeOut);

        // 2. Collect every item, remembering where it came from.
        var items = new List<(string Path, string? Origin)>();
        items.AddRange(desktopItems.Select(p => (p, (string?)null)));

        var moves = new List<Move>();
        var created = new List<PaddockWindow>();

        using (Manager.DeferSave())
        {
            foreach (var pad in unlocked)
            {
                foreach (var icon in pad.Icons.ToList())
                {
                    if (icon.Managed)
                    {
                        if (Manager.EjectIcon(pad.Id, icon.DesktopPath) is { } onDesktop)
                            items.Add((onDesktop, pad.Id));
                    }
                    else
                    {
                        Manager.RemoveIconFromPaddock(pad.Id, icon.DesktopPath);
                        items.Add((icon.DesktopPath, pad.Id));
                    }
                }

                if (Host.FindWindow(pad.Id) is { } window)
                    Host.RemovePaddock(window);
            }

            // 3. Categorise; tiny categories fold into "Other".
            var groups = items
                .GroupBy(i => IconCategorizer.Categorize(ShortcutReader.Describe(i.Path)))
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var small in groups.Where(g => g.Key != AutoCategory.Other && g.Value.Count < auto.MinItemsPerPad).ToList())
            {
                if (!groups.TryGetValue(AutoCategory.Other, out var other))
                    groups[AutoCategory.Other] = other = new();

                other.AddRange(small.Value);
                groups.Remove(small.Key);
            }

            var ordered = groups.OrderBy(g => g.Key).ToList();

            // 4. Size each pad to its icons and pack them around the locked pads.
            var metrics = CurrentMetrics(settings.Appearance);
            var sizes = ordered
                .Select(g => AutoLayoutPlanner.Measure(g.Value.Count, auto.MaxIconsPerRow, metrics))
                .Select(m => (m.Width, m.Height))
                .ToList();

            var workArea = SystemParameters.WorkArea;
            var obstacles = Manager.GetPaddocks()
                .Select(p => new PlacedRect(p.X, p.Y, p.Width, p.IsRolledUp ? ViewModels.PaddockViewModel.TitleBarHeight : p.Height))
                .ToList();

            var rects = AutoLayoutPlanner.Place(
                sizes,
                new LayoutBounds(workArea.Left, workArea.Top, workArea.Width, workArea.Height),
                obstacles,
                Math.Max(8, settings.IntelligentSpacing),
                auto.StartCorner);

            var colors = PickColors(ordered.Select(g => g.Key).ToList(), auto.ColorMode);

            // 5. Build the pads and move the items in.
            for (var i = 0; i < ordered.Count; i++)
            {
                var (category, groupItems) = (ordered[i].Key, ordered[i].Value);
                var rect = rects[i];

                var model = Manager.CreatePaddock(IconCategorizer.Title(category), rect.X, rect.Y, rect.Width, rect.Height);
                model.TitleVisibility = TitleVisibility.Always;
                model.Locked = auto.LockCreatedPads;
                model.Style.Color = colors[i];

                foreach (var (path, origin) in groupItems)
                {
                    if (Manager.AddIconToPaddock(model.Id, path) is { } entry)
                        moves.Add(new Move(origin, entry.DesktopPath));
                }

                created.Add(Host.AddWindow(model, show: false));
            }
        }

        _app.DesktopIconService.NotifyShellOfDesktopChange();

        // 6. Reveal.
        await RevealAsync(created, animate);
        _app.ShellHookService.SetDesktopIconsVisible(true);

        var total = created.Sum(w => w.ViewModel.Icons.Count);
        Log.Info($"Auto organize: {total} item(s) into {created.Count} pad(s); {snapshot.Count} pad(s) rebuilt.");

        _pending = new PendingRun(snapshot, moves, created.Select(w => w.ViewModel.Id).ToList());
        ShowBar($"{created.Count} pads · {total} icons");
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
            var animate = _app.Settings.Settings.AutoMode.Animate;
            var createdWindows = run.CreatedPadIds.Select(Host.FindWindow).OfType<PaddockWindow>().ToList();

            _app.ShellHookService.SetDesktopIconsVisible(false);
            if (animate)
                await FadeAsync(createdWindows, 0, FadeOut);

            var restoredWindows = new List<PaddockWindow>();

            using (Manager.DeferSave())
            {
                var restored = run.RemovedPads.Select(Manager.RestorePaddock).ToList();

                foreach (var move in run.Moves)
                {
                    var holder = run.CreatedPadIds
                        .Select(Manager.GetPaddock)
                        .FirstOrDefault(p => p?.Icons.Any(i => PathEquals(i.DesktopPath, move.CurrentPath)) == true);

                    // Moved again since (or deleted): leave it where the user put it.
                    if (holder is null)
                        continue;

                    if (move.OriginPadId is null)
                        Manager.EjectIcon(holder.Id, move.CurrentPath);
                    else
                        Manager.MoveIconBetweenPaddocks(holder.Id, move.OriginPadId, move.CurrentPath);
                }

                // Anything left (added after the run) goes safely to the desktop with its pad.
                foreach (var window in createdWindows)
                    Host.RemovePaddock(window);

                foreach (var model in restored)
                    restoredWindows.Add(Host.AddWindow(model, show: false));
            }

            _app.DesktopIconService.NotifyShellOfDesktopChange();
            await RevealAsync(restoredWindows, animate);
            Log.Info($"Auto organize reverted: {run.Moves.Count} item(s), {restoredWindows.Count} pad(s) restored.");
        }
        catch (Exception ex)
        {
            Log.Error("Reverting auto organize failed.", ex);
        }
        finally
        {
            _app.ShellHookService.SetDesktopIconsVisible(true);
            _busy = false;
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
        AutoColorMode.ByCategory => categories.Select(c => (string?)CategoryColors[c]).ToList(),
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
