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
        ClampAllToMonitors();

        foreach (var model in _app.PaddockManager.GetPaddocks().ToList())
        {
            CreateWindow(model);
        }

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

        _app.DesktopIconService.WatchStore(OnStoreChanged);

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
                 $"at {ScreenHelper.DipScale:0.##}x scale. Store: {_app.IconStore.StoreRoot}");

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
            title: "New Pad",
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
        _windows.Remove(window);
        var paddockId = window.ViewModel.Id;

        window.Close();

        // Deleting the paddock moves its items back onto the desktop.
        _app.PaddockManager.DeletePaddock(paddockId);
        _app.DesktopIconService.NotifyShellOfDesktopChange();
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

    /// <summary>Puts a pad away; its contents stay in it.</summary>
    internal void HidePaddock(PaddockWindow window)
    {
        if (_app.PaddockManager.GetPaddock(window.ViewModel.Id) is not { } model)
            return;

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

            // A pad put away with its × stays away until "Show Hidden Pads".
            if (visible && IsHiddenPad(window))
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
    /// The re-pin timer only earns its keep while something is on screen; with
    /// every paddock hidden it would just be waking the UI thread.
    /// </summary>
    private void UpdateZOrderTimer()
    {
        var needed = _windows.Any(w => w.IsVisible);

        if (needed && !_zOrderTimer.IsEnabled)
            _zOrderTimer.Start();
        else if (!needed && _zOrderTimer.IsEnabled)
            _zOrderTimer.Stop();
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
    /// Moves every item in every paddock back to the desktop. The escape hatch:
    /// after this, nothing of the user's is left inside Paddock's folders.
    /// </summary>
    internal int RestoreAllIconsToDesktop()
    {
        var manager = _app.PaddockManager;
        var restored = 0;
        var systemIconsShown = false;

        using (manager.DeferSave())
        {
            foreach (var paddock in manager.GetPaddocks().ToList())
            {
                foreach (var icon in paddock.Icons.ToList())
                {
                    // A shortcut standing in for a built-in icon (Recycle Bin…):
                    // the real icon comes back instead of the shortcut.
                    if (Paddock.Shell.SystemDesktopIcons.FromTarget(
                            Paddock.Shell.ShortcutReader.Describe(icon.DesktopPath).Target) is { } systemIcon)
                    {
                        manager.RemoveIconFromPaddock(paddock.Id, icon.DesktopPath);
                        TryDelete(icon.DesktopPath);
                        Paddock.Shell.SystemDesktopIcons.Show(systemIcon);
                        systemIconsShown = true;
                        restored++;
                        continue;
                    }

                    if (manager.EjectIcon(paddock.Id, icon.DesktopPath) is not null)
                        restored++;
                }
            }

            // Every pad is empty now; empty pads are just clutter.
            foreach (var window in _windows.Where(IsEmpty).ToList())
                RemovePaddock(window);
        }

        _app.DesktopIconService.NotifyShellOfDesktopChange();

        if (systemIconsShown)
            Paddock.Shell.SystemDesktopIcons.RefreshDesktop();

        // Lay the returned icons out by kind, as "Sort by → Item type" would.
        // Explorer needs a moment to pick up the new files first.
        _app.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(800);
            Paddock.Shell.DesktopArranger.SortByItemType();
        });

        return restored;
    }

    private bool IsEmpty(PaddockWindow window)
        => _app.PaddockManager.GetPaddock(window.ViewModel.Id)?.Icons.Count == 0;

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

        if (show && !model.Hidden && (!_paddocksHidden || viewModel.ExcludeFromQuickHide))
            window.Show();

        return window;
    }

    private void PinAllToDesktop()
    {
        foreach (var window in _windows)
        {
            if (window.IsVisible)
                window.PinToDesktop();
        }
    }

    /// <summary>
    /// Keeps every paddock fully on a monitor — the layout is saved in screen
    /// coordinates, so a resolution change or an unplugged display could
    /// otherwise leave one parked off-screen.
    /// </summary>
    private void ClampAllToMonitors()
    {
        var models = _app.PaddockManager.GetPaddocks();
        if (models.Count == 0)
            return;

        var moved = false;

        foreach (var model in models)
        {
            var before = (model.X, model.Y, model.Width, model.Height);
            _app.LayoutEngine.ClampToBounds(model, ScreenHelper.GetWorkAreaFor(model));

            if (before != (model.X, model.Y, model.Width, model.Height))
                moved = true;
        }

        if (moved)
            _app.PaddockManager.SaveLayout();
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

    private void OnStoreChanged()
    {
        // Raised on a watcher thread — hop to the UI before touching windows.
        _app.Dispatcher.InvokeAsync(RefreshFromDisk);
    }
}
