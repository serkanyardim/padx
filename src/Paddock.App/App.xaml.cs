using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Windows;
using Point = System.Windows.Point;
using Paddock.App.Services;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;
using static Paddock.App.Helpers.Lang;

namespace Paddock.App;

public partial class App : Application
{
    /// <summary>Per-user name, so Paddock still runs once per session on a shared machine.</summary>
    private const string SingleInstanceMutexName = @"Local\Paddock.SingleInstance";

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private HotkeyService? _hotkeys;
    private System.Threading.Mutex? _instanceMutex;
    private Views.SettingsWindow? _settingsWindow;
    private UiWatchdog? _watchdog;

    /// <summary>Smaller than this (DIPs) and a Ctrl + drag is treated as a slip, not a pad.</summary>
    private const double MinDrawnPadSize = 48;

    private DesktopDragService? _desktopDrag;
    private Views.DrawFeedbackWindow? _drawFeedback;
    private Point _drawStart;

    public SettingsService Settings { get; private set; } = null!;
    public PaddockManager PaddockManager { get; private set; } = null!;
    public LayoutEngine LayoutEngine { get; private set; } = null!;
    public DesktopIconService DesktopIconService { get; private set; } = null!;
    public IconStore IconStore { get; private set; } = null!;
    public IconExtractor IconExtractor { get; private set; } = null!;
    public ShellHookService ShellHookService { get; private set; } = null!;
    public StartupManager StartupManager { get; private set; } = null!;

    /// <summary>Owns the live paddock windows.</summary>
    internal PaddockHost? Host { get; private set; }

    internal AutoOrganizer? AutoOrganizer { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Elevated helper run: do the file moves and leave. No UI, no mutex.
        if (e.Args.Length == 2 && e.Args[0] == ElevatedMover.Argument)
        {
            Shutdown(ElevatedMover.Execute(e.Args[1]));
            return;
        }

        InstallCrashHandlers();

        // Run by the uninstaller: every icon back on the desktop, no pads, then exit.
        if (e.Args.Length >= 1 && e.Args[0] == "--restore-all")
        {
            RestoreAllAndExit();
            return;
        }

        // Screenshot mode: a throwaway desktop in %TEMP%, rendered to PNG, then exit.
        if (e.Args.Length >= 1 && e.Args[0] == "--screenshots")
        {
            RunShowcase(e.Args.Length > 1 ? e.Args[1] : "screenshots");
            return;
        }

        _watchdog = new UiWatchdog(Dispatcher);
        Log.Info($"padx starting (v{typeof(App).Assembly.GetName().Version}).");

        if (!ClaimSingleInstance())
        {
            // A second copy would draw a duplicate set of paddocks and fight the
            // first one over settings.json.
            Log.Info("Another instance is already running — exiting.");
            Shutdown();
            return;
        }

        // Services
        Settings = new SettingsService();
        DesktopIconService = new DesktopIconService();
        IconStore = DesktopIconService.CreateIconStore();
        PaddockManager = new PaddockManager(Settings, IconStore);
        LayoutEngine = new LayoutEngine();
        IconExtractor = new IconExtractor();
        ShellHookService = new ShellHookService();
        StartupManager = new StartupManager();

        ApplyTheme(Settings.Settings.GlobalTheme);
        Helpers.Lang.Load(Settings.Settings.Language);
        Helpers.Lang.Install();

        // Sync the startup registry entry with the persisted setting
        StartupManager.SetStartupEnabled(Settings.Settings.StartWithWindows);

        // Save layout when Windows is shutting down or the user logs off
        SessionEnding += (_, _) => PaddockManager.SaveLayout();

        InitializeTrayIcon();

        // If a previous run died mid Auto Organize, the desktop icons could still be hidden.
        ShellHookService.SetDesktopIconsVisible(true);

        // The main screen's ★ default config, if it has one, replaces the last
        // session's layout before any pad is drawn.
        Configs = new ConfigManager(this);
        Configs.MigrateStartupConfig();

        _mainResolution = Helpers.ScreenHelper.MainResolution();
        if (Configs.DefaultFor(_mainResolution) is { } startup)
        {
            Log.Info($"Main screen {_mainResolution}: loading its default config '{startup}'.");
            Configs.Apply(startup);
            ApplyLoadedSettings();
        }

        // …and again whenever the main screen changes resolution (a laptop
        // undocked, a monitor swapped). Display events come in bursts, so wait
        // for them to settle.
        _resolutionCheck = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _resolutionCheck.Tick += (_, _) => { _resolutionCheck.Stop(); OnMainScreenMaybeChanged(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Before the pads are placed: an always-visible search box keeps its strip free.
        ApplySearchBox(fitPads: false);

        Host = new PaddockHost(this);
        Host.Pinned += () => _search?.PinToDesktop();
        Host.Start();
        AutoOrganizer = new AutoOrganizer(this);
        AutoOrganizer.RecoverStagedItems();

        InitializeHotkeys();
        UpdateDesktopDrawing();

        if (Host.PaddockCount == 0)
            ShowNoPaddocksHint();
    }

    /// <summary>
    /// Headless cleanup for the uninstaller. Runs only when padx itself is not
    /// running (the installer closes it first), so the two never fight over
    /// the files.
    /// </summary>
    private void RestoreAllAndExit()
    {
        try
        {
            if (!ClaimSingleInstance())
            {
                Log.Warn("--restore-all skipped: padx is still running.");
                return;
            }

            Settings = new SettingsService();
            DesktopIconService = new DesktopIconService();
            IconStore = DesktopIconService.CreateIconStore();
            PaddockManager = new PaddockManager(Settings, IconStore);
            Configs = new ConfigManager(this);

            Configs.RestoreEverything();
            new StartupManager().SetStartupEnabled(false);
            Log.Info("--restore-all: every icon is back on the desktop.");
        }
        catch (Exception ex)
        {
            Log.Error("--restore-all failed.", ex);
        }
        finally
        {
            Shutdown();
        }
    }

    /// <summary>
    /// Points every service at a fresh temp folder (its own desktop, store and
    /// settings), renders the showcase images, and quits. Nothing real is read
    /// or written — no tray icon, hotkeys, hooks or pad windows either.
    /// </summary>
    private async void RunShowcase(string outputFolder)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"padx-showcase-{Guid.NewGuid():N}");
        var desktop = Path.Combine(sandbox, "Desktop");
        Directory.CreateDirectory(desktop);

        try
        {
            Settings = new SettingsService(Path.Combine(sandbox, "settings.json"));
            IconStore = new IconStore(desktop, Path.Combine(desktop, DesktopIconService.StoreFolderName));
            PaddockManager = new PaddockManager(Settings, IconStore);
            IconExtractor = new IconExtractor();
            StartupManager = new StartupManager();
            Configs = new ConfigManager(this);

            await ShowcaseRenderer.RenderAsync(this, desktop, Path.GetFullPath(outputFolder));
        }
        catch (Exception ex)
        {
            Log.Error("Rendering the showcase screenshots failed.", ex);
        }
        finally
        {
            IconExtractor?.Dispose();
            try { Directory.Delete(sandbox, recursive: true); } catch { }
            Shutdown();
        }
    }

    /// <summary>
    /// Takes the single-instance lock, or reports that someone else holds it.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        try
        {
            _instanceMutex = new System.Threading.Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);

            if (isFirst)
                return true;

            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }
        catch (Exception ex)
        {
            // Better to run than to refuse over a lock we could not take.
            Log.Warn($"Could not check for another instance: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// A tray app has no window to show an error in, so an unhandled exception
    /// would simply make Paddock vanish. Anything that escapes a UI callback is
    /// logged and swallowed — a failed drop or menu click should never take the
    /// whole app (and the user's paddocks) down with it.
    /// </summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception on the UI thread.", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                Log.Error("Unhandled exception (terminating).", exception);
            else
                Log.Error($"Unhandled non-exception fault: {args.ExceptionObject}");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>
    /// Swaps the active theme dictionary. Called at startup and whenever the
    /// setting changes, so the choice in Settings actually takes effect.
    /// </summary>
    internal void ApplyTheme(string theme)
    {
        var path = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? "Resources/Themes/Light.xaml"
            : "Resources/Themes/Dark.xaml";

        var dictionary = new ResourceDictionary { Source = new Uri(path, UriKind.Relative) };

        // Only the theme is swapped; the shared styles stay merged.
        var merged = Resources.MergedDictionaries;
        var current = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Themes/") == true);
        if (current is not null)
            merged.Remove(current);

        merged.Insert(0, dictionary);
    }

    /// <summary>
    /// With no paddocks there is nothing on screen at all, so point the user at
    /// the tray menu rather than leaving them wondering whether it started.
    /// </summary>
    private void ShowNoPaddocksHint()
    {
        _trayIcon?.ShowBalloonTip(
            7000,
            T("padx is running"),
            T("Right-click the tray icon and choose \"New Pad\", then drag an area on the desktop."),
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = T("padx — desktop, organized"),
            Icon = CreateTrayIcon(),
        };

        int iconSize = Helpers.TrayMenuIcons.CurrentSize();
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            ImageScalingSize = new System.Drawing.Size(iconSize, iconSize),
            Renderer = new Helpers.TrayMenuRenderer(),
            Font = new Font("Segoe UI", 9.5f),
            ShowImageMargin = true,
        };
        menu.Items.Add(T("New Pad"), Helpers.TrayMenuIcons.NewPad(iconSize), (_, _) => CreateNewPaddock());
        menu.Items.Add(T("Auto Organize"), Helpers.TrayMenuIcons.AutoOrganize(iconSize), (_, _) => RunAutoOrganize());
        menu.Items.Add(T("Arrange Pads"), Helpers.TrayMenuIcons.ArrangePads(iconSize), (_, _) => AutoOrganizer?.ArrangePads());
        menu.Items.Add(BuildConfigMenu(iconSize));
        menu.Items.Add(BuildPadStyleMenu(iconSize));

        // Only there when something is hidden, and says how many.
        var showHidden = new System.Windows.Forms.ToolStripMenuItem(T("Show Hidden Pads"), Helpers.TrayMenuIcons.ShowHidden(iconSize));
        showHidden.Click += (_, _) => ShowHiddenPads();
        menu.Items.Add(showHidden);
        menu.Opening += (_, _) =>
        {
            var hidden = Host?.HiddenPadCount ?? 0;
            showHidden.Visible = hidden > 0;
            showHidden.Text = F("Show Hidden Pads ({0})", hidden);
        };
        menu.Items.Add(T("Settings"), Helpers.TrayMenuIcons.Settings(iconSize), (_, _) => OpenSettings());
        menu.Items.Add(T("About..."), Helpers.TrayMenuIcons.About(iconSize), (_, _) => ShowAbout());
        menu.Items.Add("-");
        menu.Items.Add(T("Exit"), Helpers.TrayMenuIcons.Exit(iconSize), (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => TogglePaddocks();
    }

    /// <summary>Config ▸ Save, Load (opens the configs window), folder, Reset.</summary>
    private System.Windows.Forms.ToolStripMenuItem BuildConfigMenu(int iconSize)
    {
        var config = new System.Windows.Forms.ToolStripMenuItem(T("Config"), Helpers.TrayMenuIcons.Config(iconSize));

        config.DropDownItems.Add(T("Configs... (save, load, defaults)"), null, (_, _) => OpenConfigs());
        config.DropDownItems.Add("-");
        config.DropDownItems.Add(T("Open Configs Folder"), null, (_, _) => OpenFolder(AppPaths.ConfigsFolder));
        config.DropDownItems.Add("-");
        config.DropDownItems.Add(T("Reset Config - Delete All Pads"), null, (_, _) => RestoreAllIcons());

        // The submenu gets the same look as the main menu.
        config.DropDown.Renderer = new Helpers.TrayMenuRenderer();
        return config;
    }

    /// <summary>Pad Style ▸ Custom / One Color / Glass — ticked to show the current one.</summary>
    private System.Windows.Forms.ToolStripMenuItem BuildPadStyleMenu(int iconSize)
    {
        var style = new System.Windows.Forms.ToolStripMenuItem(T("Pad Style"), Helpers.TrayMenuIcons.PadStyle(iconSize));
        var options = new (string Text, PadStyleMode Mode)[]
        {
            ("Custom (each pad its own color)", PadStyleMode.Custom),
            ("One Color (all pads the default color)", PadStyleMode.Color),
            ("Glass (all pads)", PadStyleMode.Glass),
        };

        foreach (var (text, mode) in options)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(T(text)) { Tag = mode };
            item.Click += (_, _) => SetPadStyle(mode);
            style.DropDownItems.Add(item);
        }

        style.DropDownOpening += (_, _) =>
        {
            foreach (System.Windows.Forms.ToolStripMenuItem item in style.DropDownItems)
                item.Checked = (PadStyleMode)item.Tag! == Settings.Settings.Appearance.PadStyle;
        };

        style.DropDown.Renderer = new Helpers.TrayMenuRenderer();
        return style;
    }

    private Views.ConfigsWindow? _configsWindow;

    /// <summary>Shows the config manager, or brings the open one to the front.</summary>
    internal void OpenConfigs()
    {
        if (_configsWindow is null)
        {
            _configsWindow = new Views.ConfigsWindow();
            _configsWindow.Closed += (_, _) => _configsWindow = null;
            _configsWindow.Show();
        }

        _configsWindow.Activate();
    }

    internal ConfigManager Configs { get; private set; } = null!;

    /// <summary>
    /// Loads a config. Returns how many saved items were missing, or null if it
    /// could not be read (or was cancelled). <paramref name="quiet"/> leaves the
    /// reporting to the caller (the config manager shows it in its status line).
    /// </summary>
    internal int? LoadConfig(string name, bool confirm = true, bool quiet = false)
    {
        if (Host is null)
            return null;

        if (confirm && !Views.DigitalDialog.Confirm("Load config",
                F("Load \"{0}\"? Your pads and settings are replaced by the saved ones; " +
                "items are moved back into the pads they were saved in.", name), "LOAD"))
        {
            return null;
        }

        AutoOrganizer?.Accept();
        _settingsWindow?.Close();

        ShellHookService.SetDesktopIconsVisible(false);
        int? missing;
        try
        {
            Host.CloseAllWindows();
            using (Activity.Begin($"loading config '{name}'"))
                missing = Configs.Apply(name);
            ApplyLoadedSettings();
            Host.OpenAllWindows();
        }
        finally
        {
            ShellHookService.SetDesktopIconsVisible(true);
        }

        if (!quiet && missing is null)
            Views.DigitalDialog.Inform("Load config", F("\"{0}\" could not be read.", name));
        else if (!quiet && missing > 0)
            Views.DigitalDialog.Inform("Load config", F("Loaded. {0} saved item(s) are no longer on this PC and were skipped.", missing));

        return missing;
    }

    /// <summary>Re-applies everything that reads settings once (hotkeys, Ctrl+drag, startup entry).</summary>
    private void ApplyLoadedSettings()
    {
        RegisterHotkeys();
        UpdateDesktopDrawing();
        ApplySearchBox(fitPads: true);
        StartupManager.SetStartupEnabled(Settings.Settings.StartWithWindows);
    }

    private Views.SearchWindow? _search;
    private string? _searchShape;

    /// <summary>
    /// Sets the desktop search up as the settings say: an always-visible box at
    /// the top or bottom of the main screen (its strip kept free of pads), or
    /// a popup made when the shortcut is first pressed.
    /// </summary>
    internal void ApplySearchBox(bool fitPads)
    {
        var settings = Settings.Settings;
        string? dock = !settings.SearchBoxAlwaysVisible ? null
            : string.Equals(settings.SearchBoxPosition, "bottom", StringComparison.OrdinalIgnoreCase) ? "bottom" : "top";

        Helpers.ScreenHelper.ReserveMainScreenStrip(
            dock == "top" ? Views.SearchWindow.StripHeight : 0,
            dock == "bottom" ? Views.SearchWindow.StripHeight : 0);

        // Where it lives and glass-or-colour need a new window; the colour does not.
        var glass = string.Equals(settings.SearchBoxStyle, "glass", StringComparison.OrdinalIgnoreCase);
        var shape = $"{dock}|{glass}";

        if (shape != _searchShape)
        {
            _search?.Close();
            _search = null;
            _searchShape = shape;

            if (dock is not null)
            {
                _search = new Views.SearchWindow(dock, glass);
                _search.ShowDocked();
            }
        }

        _search?.ApplyLook();

        if (fitPads)
            Host?.FitPadsToScreens();
    }

    /// <summary>The search shortcut: opens the popup, or brings the docked box forward.</summary>
    private void OpenSearch()
    {
        _search ??= new Views.SearchWindow(dockedAt: null,
            glass: string.Equals(Settings.Settings.SearchBoxStyle, "glass", StringComparison.OrdinalIgnoreCase));
        _search.Summon();
    }

    private void InitializeHotkeys()
    {
        _hotkeys = new HotkeyService();
        RegisterHotkeys();
    }

    /// <summary>
    /// (Re)registers every global shortcut from settings. Pads live behind other
    /// windows, so these are the way to reach Paddock from anywhere. Returns the
    /// shortcuts that could not be registered (typically: another app owns them).
    /// </summary>
    internal IReadOnlyList<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        if (_hotkeys is null)
            return failed;

        _hotkeys.UnregisterAll();
        _search?.RefreshHint();

        var settings = Settings.Settings;
        var bindings = new List<(string Hotkey, Action Action)>
        {
            (settings.NewPadHotkey, CreateNewPaddock),
            (settings.RollUpAllHotkey, () => Host?.ToggleRollUpAll()),
            (settings.ShowHiddenHotkey, ShowHiddenPads),
            (settings.SettingsHotkey, OpenSettings),
            (settings.SearchHotkey, OpenSearch),
        };

        if (settings.QuickHideEnabled)
            bindings.Insert(0, (settings.QuickHideHotkey, TogglePaddocks));

        foreach (var (hotkey, action) in bindings)
        {
            if (string.IsNullOrWhiteSpace(hotkey))
                continue;

            if (!_hotkeys.Register(hotkey, action))
                failed.Add(hotkey);
        }

        return failed;
    }

    /// <summary>Releases all shortcuts while one is being recorded in Settings.</summary>
    internal void SuspendHotkeys() => _hotkeys?.UnregisterAll();

    internal void ResumeHotkeys() => RegisterHotkeys();

    /// <summary>Starts or stops the Ctrl + drag gesture to match the setting.</summary>
    internal void UpdateDesktopDrawing()
    {
        if (Settings.Settings.CtrlDragCreatesPad)
        {
            if (_desktopDrag is not null)
                return;

            _desktopDrag = new DesktopDragService();
            _desktopDrag.DrawStarted += (x, y) => Dispatcher.InvokeAsync(() => BeginDesktopDraw(x, y));
            _desktopDrag.DrawMoved += (x, y) => Dispatcher.InvokeAsync(() => UpdateDesktopDraw(x, y));
            _desktopDrag.DrawCompleted += (x1, y1, x2, y2) => Dispatcher.InvokeAsync(() => CompleteDesktopDraw(x2, y2));
            _desktopDrag.DrawCancelled += () => Dispatcher.InvokeAsync(EndDesktopDraw);
            _desktopDrag.Start();
        }
        else
        {
            _desktopDrag?.Dispose();
            _desktopDrag = null;
            EndDesktopDraw();
        }
    }

    private static Point ToDips(int x, int y)
    {
        var scale = Helpers.ScreenHelper.DipScale;
        return new Point(x / scale, y / scale);
    }

    private void BeginDesktopDraw(int x, int y)
    {
        EndDesktopDraw();
        _drawStart = ToDips(x, y);
        _drawFeedback = new Views.DrawFeedbackWindow(Settings.Settings.Appearance.CornerRadius);
        _drawFeedback.Span(_drawStart, _drawStart);
        _drawFeedback.Show();
    }

    private void UpdateDesktopDraw(int x, int y) => _drawFeedback?.Span(_drawStart, ToDips(x, y));

    /// <summary>Turns the drawn rectangle into a pad, unless it was too small to mean one.</summary>
    private void CompleteDesktopDraw(int x, int y)
    {
        if (_drawFeedback is null)
            return;

        var area = new Rect(_drawStart, ToDips(x, y));
        EndDesktopDraw();

        if (area.Width >= MinDrawnPadSize && area.Height >= MinDrawnPadSize)
            Host?.CreatePaddock(area);
    }

    private void EndDesktopDraw()
    {
        _drawFeedback?.Close();
        _drawFeedback = null;
    }

    private void CreateNewPaddock() => Host?.CreatePaddockInteractive();

    private string _mainResolution = string.Empty;
    private System.Windows.Threading.DispatcherTimer? _resolutionCheck;

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(() => { _resolutionCheck?.Stop(); _resolutionCheck?.Start(); });

    /// <summary>
    /// The main screen now has a different resolution: load the ★ default made
    /// for it. Without one, nothing is loaded — the pads that no longer fit
    /// have already been moved to free space by the host.
    /// </summary>
    private void OnMainScreenMaybeChanged()
    {
        var resolution = Helpers.ScreenHelper.MainResolution();
        if (resolution == _mainResolution)
            return;

        Log.Info($"Main screen changed: {_mainResolution} → {resolution}.");
        _mainResolution = resolution;

        if (Configs.DefaultFor(resolution) is not { } name)
            return;

        LoadConfig(name, confirm: false);
        _trayIcon?.ShowBalloonTip(4000, "padx",
            F("Screen is now {0} — loaded its default config \"{1}\".", ConfigStore.Display(resolution), name),
            System.Windows.Forms.ToolTipIcon.Info);
    }

    /// <summary>Switches every pad between their own colours, one colour, or glass.</summary>
    internal void SetPadStyle(PadStyleMode mode)
    {
        Settings.Settings.Appearance.PadStyle = mode;
        Settings.Save();
        Host?.ApplyAppearance();
        _settingsWindow?.RefreshFromSettings();
    }

    /// <summary>Menu and hotkey: bring back pads hidden with ×. Does nothing when none are hidden.</summary>
    private void ShowHiddenPads() => Host?.ShowHiddenPads();

    internal void RunAutoOrganize() => _ = AutoOrganizer?.RunAsync();

    private void TogglePaddocks() => Host?.ToggleQuickHide();

    internal void RestoreAllIcons()
    {
        if (Host is null)
            return;

        if (!Views.DigitalDialog.Confirm("Restore all icons",
                "Every item goes back onto the desktop, sorted by item type. Built-in icons " +
                "(Recycle Bin, your user folder…) reappear as themselves, and the emptied pads are removed.",
                "RESTORE"))
        {
            return;
        }

        int restored;
        using (Activity.Begin("restoring all icons to the desktop"))
            restored = Host.RestoreAllIconsToDesktop();

        Views.DigitalDialog.Inform("Restore all icons",
            restored == 0
                ? "There was nothing to restore."
                : F("Moved {0} item(s) back to the desktop.", restored));
    }

    /// <summary>Shows the settings window, or brings the open one to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new Views.SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;

        _settingsWindow.Activate();
    }

    /// <summary>Opens the folder holding settings.json and paddock.log.</summary>
    internal static void OpenSettingsFolder() => OpenFolder(AppPaths.DataFolder);

    private static void OpenFolder(string folder)
    {
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{folder}'.", ex);
        }
    }

    private Views.AboutWindow? _aboutWindow;

    private void ShowAbout()
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = new Views.AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
            _aboutWindow.Show();
        }

        _aboutWindow.Activate();
    }

    /// <summary>
    /// Starts a fresh padx and quits this one (to switch language). The
    /// single-instance lock is let go first so the new one can take it.
    /// </summary>
    internal void Restart()
    {
        PaddockManager.SaveLayout();

        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not restart padx.", ex);
        }

        Shutdown();
    }

    private void ExitApplication()
    {
        PaddockManager.SaveLayout();
        Shutdown();
    }

    /// <summary>
    /// Generates a 32x32 tray icon at runtime — a stylised fence/paddock glyph
    /// so the app is recognisable in the system tray without shipping a separate .ico.
    /// </summary>
    private static Icon CreateTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Rounded-rect background (#0A84FF accent blue)
        using var bgBrush = new SolidBrush(Color.FromArgb(10, 132, 255));
        using var bgPath = RoundedRect(new Rectangle(0, 0, size, size), 7);
        g.FillPath(bgBrush, bgPath);

        // Draw three vertical fence posts
        using var postPen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int[] postXs = [9, 16, 23];
        foreach (int px in postXs)
        {
            g.DrawLine(postPen, px, 8, px, 24);
        }

        // Draw two horizontal rails
        using var railPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(railPen, 6, 13, 26, 13);
        g.DrawLine(railPen, 6, 19, 26, 19);

        // The HICON behind this is owned by the process for its whole lifetime.
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PaddockManager?.SaveLayout();

        _hotkeys?.Dispose();
        _search?.Close();
        _desktopDrag?.Dispose();
        _watchdog?.Dispose();
        Host?.Dispose();
        DesktopIconService?.Dispose();
        IconExtractor?.Dispose();

        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        Log.Info("padx exited.");

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }
}
