using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Paddock.App.Helpers;
using Paddock.App.ViewModels;
using Paddock.App.Views;
using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.App.Services;

/// <summary>
/// Owns the live paddock windows: creates them from the saved layout, keeps them
/// pinned at desktop level, and keeps them in step with what is on disk.
/// </summary>
internal sealed class PaddockHost : IDisposable
{
    /// <summary>
    /// How often the desktop-level z-order is re-asserted. WM_WINDOWPOSCHANGING
    /// covers our own windows, but this also recovers if Explorer restarts (the
    /// desktop window we anchor to gets a new handle) or another app reshuffles
    /// the z-order behind our back.
    /// </summary>
    private static readonly TimeSpan ZOrderInterval = TimeSpan.FromSeconds(3);

    private readonly App _app;
    private readonly List<PaddockWindow> _windows = new();
    private readonly DispatcherTimer _zOrderTimer;

    private bool _paddocksHidden;
    private bool _disposed;

    internal PaddockHost(App app)
    {
        _app = app;

        _zOrderTimer = new DispatcherTimer { Interval = ZOrderInterval };
        _zOrderTimer.Tick += (_, _) => PinAllToDesktop();
    }

    internal int PaddockCount => _windows.Count;

    internal IReadOnlyList<PaddockWindow> Windows => _windows;

    /// <summary>Closes every pad window, leaving the layout itself alone (for loading a config).</summary>
    internal void CloseAllWindows()
    {
        foreach (var window in _windows.ToList())
            window.Close();

        _windows.Clear();
        UpdateZOrderTimer();
    }

    /// <summary>Opens a window for every pad in the layout.</summary>
    internal void OpenAllWindows()
    {
        // A config from before the Desktop pad has none; it gets one.
        EnsureDesktopPad();
        SyncDesktopPad(announce: false);

        foreach (var model in _app.PaddockManager.GetPaddocks().ToList())
        {
            var window = CreateWindow(model);
            window.PinToDesktop();
        }

        UpdateZOrderTimer();
    }

    /// <summary>
    /// Creates the window for a pad model that is already in the layout.
    /// With <paramref name="show"/> false the caller reveals it (Auto Organize
    /// fades pads in one by one).
    /// </summary>
    internal PaddockWindow AddWindow(PaddockModel model, bool show)
    {
        var window = CreateWindow(model, show);
        UpdateZOrderTimer();
        return window;
    }

    /// <summary>Restores the saved layout and starts watching the environment.</summary>
    internal void Start()
    {
        _app.PaddockManager.ReconcilePaddocks();
        EnsureDesktopPad();
        SyncDesktopPad(announce: false);
        ClampAllToMonitors();

        foreach (var model in _app.PaddockManager.GetPaddocks().ToList())
        {
            CreateWindow(model);
        }

        // Whatever changes a pad's contents changes what is left for the Desktop pad.
        _app.PaddockManager.LayoutChanged += ScheduleDesktopSync;

        UpdateZOrderTimer();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // Win+D lifts the desktop over every app; follow it straight away.
        // Re-pin twice: Explorer finishes moving the desktop a moment later.
        _app.ShellHookService.WatchForeground(() =>
        {
            PinAllToDesktop();
            _app.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(150);
                PinAllToDesktop();
            });
        });

        _app.DesktopIconService.WatchDesktop(OnDesktopChanged, OnDesktopRenamed);

        LogStartupState();
    }

    /// <summary>
    /// One-time snapshot of what we restored and onto what. Cheap, and the first
    /// thing worth knowing when a layout comes back looking wrong.
    /// </summary>
    private void LogStartupState()
    {
        var monitors = ScreenHelper.GetWorkAreas();
        Log.Info($"{_windows.Count} paddock(s) restored across {monitors.Count} monitor(s) " +
                 $"at {ScreenHelper.DipScale:0.##}x scale. Desktop: {_app.IconStore.DesktopRoot}");

        foreach (var window in _windows)
        {
            var vm = window.ViewModel;
            Log.Info($"  \"{vm.Title}\" at {vm.X:0},{vm.Y:0} {vm.Width:0}x{vm.Height:0} " +
                     $"with {vm.Icons.Count} icon(s).");
        }
    }

    /// <summary>Asks the user to draw an area, then creates a paddock there.</summary>
    internal void CreatePaddockInteractive()
    {
        var area = DrawAreaWindow.PickArea();
        if (area is null)
            return;

        CreatePaddock(area.Value);
    }

    internal void CreatePaddock(Rect screenArea)
    {
        var model = _app.PaddockManager.CreatePaddock(
            title: Helpers.Lang.T("New Pad"),
            x: screenArea.X,
            y: screenArea.Y,
            width: screenArea.Width,
            height: screenArea.Height);

        _app.LayoutEngine.ClampToBounds(model, ScreenHelper.GetWorkAreaFor(model));
        _app.PaddockManager.SaveLayout();

        // A new paddock should be visible even if paddocks were hidden.
        if (_paddocksHidden)
            SetPaddocksVisible(true);

        var window = CreateWindow(model);
        window.PinToDesktop();
        UpdateZOrderTimer();
    }

    internal void RemovePaddock(PaddockWindow window)
    {
        // The Desktop pad is never removed; it just shows what no other pad holds.
        if (IsDesktopPad(window))
            return;

        _windows.Remove(window);
        var paddockId = window.ViewModel.Id;

        window.Close();

        // Its items never left the desktop; they simply show there again.
        _app.PaddockManager.DeletePaddock(paddockId);
        UpdateZOrderTimer();
    }

    // --- Visibility ---

    internal void ToggleQuickHide() => SetPaddocksVisible(_paddocksHidden);

    internal bool PaddocksHidden => _paddocksHidden;

    // --- Hidden pads (× button) ---

    internal bool IsHiddenPad(PaddockWindow window)
        => _app.PaddockManager.GetPaddock(window.ViewModel.Id)?.Hidden == true;

    /// <summary>How many pads are put away with their × button.</summary>
    internal int HiddenPadCount => _app.PaddockManager.GetPaddocks().Count(p => p.Hidden);

    /// <summary>
    /// The × button: puts a pad away, contents and all. An empty pad has
    /// nothing to keep, so it is simply removed — no question asked.
    /// </summary>
    internal void HidePaddock(PaddockWindow window)
    {
        if (_app.PaddockManager.GetPaddock(window.ViewModel.Id) is not { } model)
            return;

        if (model.Icons.Count == 0 && !model.IsDesktop)
        {
            RemovePaddock(window);
            return;
        }

        model.Hidden = true;
        _app.PaddockManager.SaveLayout();
        window.Hide();
        UpdateZOrderTimer();
    }

    /// <summary>Brings back every pad hidden with its × button. Returns how many came back.</summary>
    internal int ShowHiddenPads()
    {
        var hidden = _windows.Where(IsHiddenPad).ToList();
        if (hidden.Count == 0)
            return 0;

        // They would stay invisible under a quick-hide, so lift that too.
        if (_paddocksHidden)
            SetPaddocksVisible(true);

        using (_app.PaddockManager.DeferSave())
        {
            foreach (var window in hidden)
            {
                _app.PaddockManager.GetPaddock(window.ViewModel.Id)!.Hidden = false;
                _app.PaddockManager.SaveLayout();

                if (!ShouldShow(window))
                    continue;

                window.Show();
                window.PinToDesktop();
            }
        }

        UpdateZOrderTimer();
        return hidden.Count;
    }

    internal void SetPaddocksVisible(bool visible)
    {
        _paddocksHidden = !visible;

        foreach (var window in _windows)
        {
            if (!visible && window.ViewModel.ExcludeFromQuickHide)
                continue;

            // A pad put away with its × stays away until "Show Hidden Pads",
            // and an empty Desktop pad until something lands on the desktop.
            if (visible && !ShouldShow(window))
                continue;

            if (visible)
            {
                window.Show();
                window.PinToDesktop();
            }
            else
            {
                window.Hide();
            }
        }

        UpdateZOrderTimer();
    }

    /// <summary>
    /// Keeps the re-pin timer running: besides pinning the pads, it keeps
    /// Windows' own desktop icons hidden (Explorer shows them again after a
    /// restart or a refresh).
    /// </summary>
    private void UpdateZOrderTimer()
    {
        if (!_zOrderTimer.IsEnabled)
            _zOrderTimer.Start();
    }

    /// <summary>Rolls every pad up, or — if they already all are — expands them all.</summary>
    internal void ToggleRollUpAll()
    {
        var rollUp = _windows.Any(w => !w.ViewModel.IsRolledUp);

        using (_app.PaddockManager.DeferSave())
        {
            foreach (var window in _windows)
                window.ViewModel.IsRolledUp = rollUp;
        }
    }

    // --- Appearance ---

    /// <summary>Redraws every pad after the global appearance changed.</summary>
    internal void ApplyAppearance()
    {
        foreach (var window in _windows)
            window.ViewModel.ApplyAppearance();
    }

    // --- Contents ---

    /// <summary>
    /// Removes every pad but the Desktop pad. Their items never left the
    /// desktop, so they all show in the Desktop pad. Returns how many items that was.
    /// </summary>
    internal int RestoreAllIconsToDesktop()
    {
        var restored = _app.PaddockManager.GetPaddocks().Where(p => !p.IsDesktop).Sum(p => p.Icons.Count);

        using (_app.PaddockManager.DeferSave())
        {
            foreach (var window in _windows.Where(w => !IsDesktopPad(w)).ToList())
                RemovePaddock(window);
        }

        SyncDesktopPad(announce: false);
        return restored;
    }

    internal void RefreshAll()
    {
        foreach (var window in _windows)
        {
            window.ViewModel.RefreshIcons();
        }
    }

    internal void RefreshPaddock(string paddockId)
        => FindWindow(paddockId)?.ViewModel.RefreshIcons();

    /// <summary>
    /// Re-syncs every paddock with its folder, redrawing only if something
    /// actually changed — the store watcher also fires for our own moves, and
    /// rebuilding the icon lists needlessly would reset scroll positions.
    /// </summary>
    internal void RefreshFromDisk()
    {
        if (_app.PaddockManager.ReconcilePaddocks())
            RefreshAll();
    }

    // --- Desktop search ---

    /// <summary>
    /// "Find place": makes the pad holding an item visible (un-hiding,
    /// expanding it if needed), lifts it above other windows for a moment and
    /// blinks the item.
    /// </summary>
    internal void Spotlight(string paddockId, string path)
    {
        if (FindWindow(paddockId) is not { } window || _app.PaddockManager.GetPaddock(paddockId) is not { } model)
            return;

        if (_paddocksHidden)
            SetPaddocksVisible(true);

        if (model.Hidden)
        {
            model.Hidden = false;
            _app.PaddockManager.SaveLayout();
        }

        if (window.ViewModel.IsRolledUp)
            window.ViewModel.IsRolledUp = false;

        if (!window.IsVisible)
            window.Show();

        UpdateZOrderTimer();
        window.Spotlight(path);
    }

    /// <summary>
    /// A pad dropped onto the always-visible search box's strip is nudged just
    /// out of it, the way it could not be dropped onto the taskbar.
    /// </summary>
    internal void KeepOutOfSearchStrip(PaddockWindow window)
    {
        if (ScreenHelper.ReservedStrip is not { } strip)
            return;

        var pad = window.ScreenBounds;
        var overlaps = pad.Left < strip.Right && pad.Right > strip.Left && pad.Top < strip.Bottom && pad.Bottom > strip.Top;
        if (!overlaps)
            return;

        var below = strip.Top <= ScreenHelper.GetFullWorkArea(underCursor: false).Top;
        window.ViewModel.UpdatePosition(pad.Left, below ? strip.Bottom : strip.Top - window.ActualHeight);
    }

    /// <summary>Moves pads that no longer fit their screen (e.g. the search strip just appeared).</summary>
    internal void FitPadsToScreens()
    {
        ClampAllToMonitors();

        foreach (var window in _windows)
            window.ApplyModelBounds();
    }

    /// <summary>The pads are up: Windows' own desktop icons go, and stay gone while padx runs.</summary>
    internal void DesktopReady()
    {
        UpdateZOrderTimer();
        PinAllToDesktop();
    }

    /// <summary>Pins every visible pad again (they land above anything padx pinned before).</summary>
    internal void PinPads()
    {
        foreach (var window in _windows.Where(w => w.IsVisible))
            window.PinToDesktop();
    }

    /// <summary>Raised whenever the pads are pinned back to desktop level (Win+D, the timer).</summary>
    internal event Action? Pinned;

    // --- Lookup ---

    internal PaddockWindow? FindWindow(string paddockId)
        => _windows.FirstOrDefault(w => w.ViewModel.Id == paddockId);

    /// <summary>The visible paddock under a screen point (DIPs), if any.</summary>
    internal PaddockWindow? FindWindowAt(Point screenPoint)
        => _windows.FirstOrDefault(w => w.IsVisible && w.ScreenBounds.Contains(screenPoint));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _zOrderTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _app.ShellHookService.StopWatchingForeground();

        foreach (var window in _windows.ToList())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private PaddockWindow CreateWindow(PaddockModel model, bool show = true)
    {
        var viewModel = new PaddockViewModel(model, _app.PaddockManager, () => _app.Settings.Settings.Appearance);
        var window = new PaddockWindow(viewModel, _app.ShellHookService);

        viewModel.RemoveRequested += () => RemovePaddock(window);
        viewModel.HideRequested += () => HidePaddock(window);

        _windows.Add(window);

        if (show && ShouldShow(window) && (!_paddocksHidden || viewModel.ExcludeFromQuickHide))
            window.Show();

        return window;
    }

    private void PinAllToDesktop()
    {
        // While padx runs, every desktop item is in a pad — the Desktop pad
        // holds the rest — so Windows' own icons stay hidden (Explorer shows
        // them again after a restart or a refresh).
        _app.ShellHookService.SetDesktopIconsVisible(false);

        foreach (var window in _windows)
        {
            if (window.IsVisible)
                window.PinToDesktop();
        }

        Pinned?.Invoke();
    }

    /// <summary>
    /// Keeps every pad fully on a monitor — the layout is saved in screen
    /// coordinates, so a resolution change or an unplugged display could
    /// otherwise leave one parked off-screen. Only pads that no longer fit
    /// move, each to the nearest free gap; nothing is recreated or resized
    /// unless it is bigger than the screen. When the screen is full, a pad may
    /// end up overlapping another.
    /// </summary>
    private void ClampAllToMonitors()
    {
        var models = _app.PaddockManager.GetPaddocks();
        if (models.Count == 0)
            return;

        var moved = false;
        var gap = Math.Max(8, _app.Settings.Settings.IntelligentSpacing);

        // Pads that still fit stay exactly where they are, and are what the
        // others must avoid. Hidden pads take no room on screen.
        static bool Fits(PaddockModel p, LayoutBounds a)
            => p.X >= a.Left && p.Y >= a.Top && p.X + p.Width <= a.Right && p.Y + p.Height <= a.Bottom;

        var misfits = models.Where(p => !Fits(p, ScreenHelper.GetWorkAreaFor(p))).ToList();
        var taken = models
            .Where(p => !p.Hidden && !misfits.Contains(p))
            .Select(p => new PlacedRect(p.X, p.Y, p.Width, p.Height))
            .ToList();

        foreach (var model in misfits)
        {
            var area = ScreenHelper.GetWorkAreaFor(model);

            // First squeeze it inside the screen (shrinking it only if it is
            // bigger than the screen), then look for the nearest free gap.
            _app.LayoutEngine.ClampToBounds(model, area);
            var desired = new PlacedRect(model.X, model.Y, model.Width, model.Height);

            if (AutoLayoutPlanner.FindNearestFreeSpot(desired, area, taken, gap) is { } spot)
            {
                model.X = spot.X;
                model.Y = spot.Y;
            }

            // No gap left: it stays clamped, overlapping — better than off screen.
            if (!model.Hidden)
                taken.Add(new PlacedRect(model.X, model.Y, model.Width, model.Height));

            moved = true;
        }

        if (moved)
        {
            Log.Info($"Screen change: {misfits.Count} pad(s) moved back on screen.");
            _app.PaddockManager.SaveLayout();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _app.Dispatcher.InvokeAsync(() =>
        {
            ScreenHelper.Invalidate();
            ClampAllToMonitors();

            foreach (var window in _windows)
            {
                window.ApplyModelBounds();
            }

            PinAllToDesktop();
        });
    }

    /// <summary>Toggling Windows' transparency effects decides whether glass can blur.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            _app.Dispatcher.InvokeAsync(ApplyAppearance);
    }

    // --- Watching the desktop ---

    /// <summary>Renames seen since the last settle, oldest first: (old path, new path).</summary>
    private readonly List<(string Old, string New)> _renames = new();

    /// <summary>Raised on a watcher thread for every rename; handled once things settle.</summary>
    private void OnDesktopRenamed(string oldPath, string newPath)
    {
        lock (_renames)
            _renames.Add((oldPath, newPath));
    }

    /// <summary>
    /// Something on the desktop changed (raised on a watcher thread, once a
    /// burst has settled). Pads follow renames, the desktop redraws, and pads
    /// let go of items that are gone — checked again a moment later, since a
    /// program saving a file often deletes and recreates it.
    /// </summary>
    private void OnDesktopChanged()
    {
        _app.Dispatcher.InvokeAsync(async () =>
        {
            FollowRenames();
            RefreshAll();
            SyncDesktopPad(announce: true);

            await Task.Delay(800);
            RefreshFromDisk();
            SyncDesktopPad(announce: true);
        });
    }

    /// <summary>
    /// Points pads at the new names of renamed items. A rename chain
    /// (a → b → c) ends where it ends; a rename whose old name is back again
    /// was a program saving (Word renames the file, writes a new one, deletes
    /// the old) — the pad keeps the name it had.
    /// </summary>
    private void FollowRenames()
    {
        List<(string Old, string New)> renames;
        lock (_renames)
        {
            renames = _renames.ToList();
            _renames.Clear();
        }

        foreach (var (old, _) in renames)
        {
            if (IconStore.ItemExists(old))
                continue;

            var current = old;
            for (var step = 0; step < renames.Count; step++)
            {
                var next = renames.FirstOrDefault(r => string.Equals(r.Old, current, StringComparison.OrdinalIgnoreCase));
                if (next.New is null)
                    break;
                current = next.New;
            }

            if (IconStore.ItemExists(current))
            {
                _app.PaddockManager.RepointIcon(old, current);

                // A renamed item is no newcomer: the Desktop pad need not announce it.
                _renamedTo.Add(current);
            }
        }
    }

    // --- The Desktop pad ---

    /// <summary>New names from renames, not to be announced as arrivals.</summary>
    private readonly HashSet<string> _renamedTo = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer? _desktopSyncTimer;

    private static bool IsDesktopPad(PaddockWindow window) => window.ViewModel.IsDesktop;

    /// <summary>
    /// Shown unless put away with ×, and — for the Desktop pad — only while it
    /// holds something.
    /// </summary>
    private bool ShouldShow(PaddockWindow window)
        => _app.PaddockManager.GetPaddock(window.ViewModel.Id) is { } model
           && !model.Hidden
           && !(model.IsDesktop && model.Icons.Count == 0);

    /// <summary>There is always one Desktop pad; the first one sits at the top right of the main screen.</summary>
    private void EnsureDesktopPad()
    {
        var manager = _app.PaddockManager;
        if (manager.GetDesktopPad() is not null)
            return;

        var area = ScreenHelper.GetWorkAreas().FirstOrDefault(a => a.Contains(1, 1));
        if (area == default)
            area = ScreenHelper.GetWorkAreas()[0];

        var gap = Math.Max(8, _app.Settings.Settings.IntelligentSpacing);
        var size = DesktopPadSize(4);
        var pad = manager.CreatePaddock(Helpers.Lang.T("Desktop"), area.Right - size.Width - gap, area.Top + gap, size.Width, size.Height);
        pad.IsDesktop = true;
        pad.TitleVisibility = TitleVisibility.Always;
        pad.SortBy = SortField.Manual;
        manager.SaveLayout();
        Log.Info("Desktop pad created.");
    }

    /// <summary>Several pad changes in a row cost one Desktop pad update.</summary>
    private void ScheduleDesktopSync()
    {
        if (_desktopSyncTimer is null)
        {
            _desktopSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _desktopSyncTimer.Tick += (_, _) =>
            {
                _desktopSyncTimer.Stop();
                SyncDesktopPad(announce: false);
            };
        }

        _desktopSyncTimer.Stop();
        _desktopSyncTimer.Start();
    }

    /// <summary>
    /// Fills the Desktop pad with every desktop item no other pad holds, in
    /// "sort by item type" order: built-in icons (This PC … Recycle Bin),
    /// folders, then files by type and name. Empty, it goes out of sight.
    /// With <paramref name="announce"/>, newly arrived items (a download, a
    /// new file) bring it back — even after ×, at a size that fits — and blink.
    /// </summary>
    internal void SyncDesktopPad(bool announce)
    {
        var manager = _app.PaddockManager;
        if (manager.GetDesktopPad() is not { } model)
            return;

        var system = SystemIconShortcuts.Visible();
        var files = _app.DesktopIconService.GetDesktopItems()
            .Concat(_app.DesktopIconService.GetCommonDesktopItems())
            .OrderBy(p => System.IO.Directory.Exists(p) ? 0 : 1)
            .ThenBy(p => System.IO.Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => System.IO.Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase);

        var loose = system.Concat(files).Where(p => !manager.IsInAnyPad(p)).ToList();

        var wasEmpty = model.Icons.Count == 0;
        var arrived = manager.SetDesktopContents(loose)
            .Where(p => !_renamedTo.Contains(p))
            .ToList();
        _renamedTo.Clear();

        if (FindWindow(model.Id) is not { } window)
            return;

        window.ViewModel.RefreshIcons();

        if (loose.Count == 0)
        {
            window.Hide();
            return;
        }

        if (announce && arrived.Count > 0)
        {
            Log.Info($"Desktop pad: {arrived.Count} new item(s).");

            // Something new on the desktop is never left out of sight.
            if (model.Hidden)
            {
                model.Hidden = false;
                manager.SaveLayout();
            }
        }

        if (!ShouldShow(window) || (_paddocksHidden && !window.ViewModel.ExcludeFromQuickHide))
            return;

        // Coming into view, or more to show: make it fit what it holds.
        if (wasEmpty || !window.IsVisible || arrived.Count > 0)
            FitDesktopPad(model, window, grow: !wasEmpty && window.IsVisible);

        if (!window.IsVisible)
        {
            window.Show();
            window.PinToDesktop();
        }

        if (announce)
        {
            foreach (var path in arrived.Take(8))
                window.Pad.Spotlight(path, TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Sizes the Desktop pad to its icons (no bigger than most of the screen)
    /// where it last was; if that would cover another pad, it moves to the
    /// nearest free space. With <paramref name="grow"/>, it only ever grows —
    /// a size chosen by hand is kept while the icons fit in it.
    /// </summary>
    private void FitDesktopPad(PaddockModel model, PaddockWindow window, bool grow)
    {
        var area = ScreenHelper.GetWorkAreaFor(model);
        var size = DesktopPadSize(model.Icons.Count);
        var width = Math.Min(grow ? Math.Max(model.Width, size.Width) : size.Width, area.Width * 0.9);
        var height = Math.Min(grow ? Math.Max(model.Height, size.Height) : size.Height, area.Height * 0.6);

        var gap = Math.Max(8, _app.Settings.Settings.IntelligentSpacing);
        var x = Math.Clamp(model.X, area.Left, Math.Max(area.Left, area.Right - width));
        var y = Math.Clamp(model.Y, area.Top, Math.Max(area.Top, area.Bottom - height));

        var others = _windows
            .Where(w => w != window && w.IsVisible)
            .Select(w => new PlacedRect(w.Left, w.Top, w.ActualWidth, w.ActualHeight))
            .ToList();

        var wanted = new PlacedRect(x, y, width, height);
        if (others.Any(o => o.Intersects(wanted, 0))
            && AutoLayoutPlanner.FindNearestFreeSpot(wanted, area, others, gap) is { } spot)
        {
            x = spot.X;
            y = spot.Y;
        }

        model.X = x;
        model.Y = y;
        model.Width = width;
        model.Height = height;
        _app.PaddockManager.SaveLayout();
        window.ApplyModelBounds();
    }

    /// <summary>A pad that holds <paramref name="count"/> icons, the Auto Mode way (icons per row).</summary>
    private Size DesktopPadSize(int count)
    {
        var settings = _app.Settings.Settings;
        var metrics = AutoOrganizer.CurrentMetrics(settings.Appearance);
        var measured = AutoLayoutPlanner.Measure(Math.Max(1, count), Math.Max(1, settings.AutoMode.MaxIconsPerRow), metrics);
        return new Size(measured.Width, measured.Height);
    }
}
