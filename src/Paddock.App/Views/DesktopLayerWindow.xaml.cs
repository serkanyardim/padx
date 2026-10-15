using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Paddock.App.Helpers;
using Paddock.App.Services;
using Paddock.App.ViewModels;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Views;

/// <summary>
/// padx's desktop. Windows' own desktop icons are hidden while padx runs;
/// this full-screen, see-through window at the bottom of everything draws
/// the items that are in no pad, in a grid around the pads — the files are
/// the real ones in the Desktop folder.
///
/// Everything between the icons is fully transparent, so clicks there go
/// through to the real desktop: its right-click menu (New, View, Personalise…),
/// dropping files onto it, Ctrl + drag for a new pad.
/// </summary>
public partial class DesktopLayerWindow : Window
{
    /// <summary>The "pad id" of icons dragged from the desktop.</summary>
    public const string SourceId = "desktop";

    private const double MinDragDistance = 5;
    private const double CellMargin = 6;

    private readonly ObservableCollection<LayerIcon> _icons = new();
    private readonly DispatcherTimer _refreshTimer;
    private Point _dragStart;
    private bool _dragStarted;
    private LayerIcon? _pressed;
    private int _raised;

    public DesktopLayerWindow()
    {
        InitializeComponent();
        Items.ItemsSource = _icons;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Refresh(); };

        CoverAllScreens();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        CurrentApp.PaddockManager.LayoutChanged += ScheduleRefresh;
    }

    private static App CurrentApp => (App)Application.Current;

    /// <summary>What the desktop shows now (for the search).</summary>
    public IReadOnlyList<IconViewModel> Icons => _icons.Select(i => i.Icon).ToList();

    /// <summary>Redraws soon; many changes in a row cost one redraw.</summary>
    public void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // --- Contents and layout ---

    /// <summary>
    /// Every desktop item in no pad — built-in icons first (This PC … Recycle
    /// Bin), then folders, then files, by name — laid out down each column,
    /// then across, skipping the cells the pads cover.
    /// </summary>
    public void Refresh()
    {
        var app = CurrentApp;
        var manager = app.PaddockManager;

        var items = SystemIconShortcuts.Visible()
            .Concat(app.DesktopIconService.GetDesktopItems()
                .Concat(app.DesktopIconService.GetCommonDesktopItems())
                .OrderBy(p => Directory.Exists(p) ? 0 : 1)
                .ThenBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase))
            .Where(p => !manager.IsInAnyPad(p))
            .ToList();

        var size = Math.Max(16, app.Settings.Settings.Appearance.IconSize);
        var cellWidth = size + 44;
        var cellHeight = size + 50;
        var cells = FreeCells(cellWidth, cellHeight).GetEnumerator();

        var known = _icons.ToDictionary(i => i.Icon.FullPath, StringComparer.OrdinalIgnoreCase);
        var wanted = new List<LayerIcon>();

        foreach (var path in items)
        {
            if (!cells.MoveNext())
            {
                Log.Warn($"Desktop: no room left for {items.Count - wanted.Count} item(s).");
                break;
            }

            var icon = known.TryGetValue(path, out var existing)
                ? existing
                : new LayerIcon(new IconViewModel(new IconEntry { DesktopPath = path }));

            icon.Place(cells.Current.X - Left, cells.Current.Y - Top, cellWidth - 4, cellHeight - 4, size);
            wanted.Add(icon);
        }

        // Fewest collection changes: icons already shown keep their images.
        var keep = wanted.ToHashSet();
        for (var i = _icons.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_icons[i]))
                _icons.RemoveAt(i);
        }

        foreach (var icon in wanted.Where(w => !_icons.Contains(w)))
            _icons.Add(icon);
    }

    /// <summary>Grid cells (screen DIPs), main screen first, that no visible pad covers.</summary>
    private static IEnumerable<Point> FreeCells(double width, double height)
    {
        var pads = CurrentApp.Host?.Windows
            .Where(w => w.IsVisible)
            .Select(w => new Rect(w.Left - 2, w.Top - 2, w.ActualWidth + 4, w.ActualHeight + 4))
            .ToList() ?? [];

        var areas = ScreenHelper.GetWorkAreas()
            .OrderBy(a => a.Contains(1, 1) ? 0 : 1)
            .ThenBy(a => a.Left);

        foreach (var area in areas)
        {
            for (var x = area.Left + CellMargin; x + width <= area.Right; x += width)
            {
                for (var y = area.Top + CellMargin; y + height <= area.Bottom; y += height)
                {
                    var cell = new Rect(x, y, width, height);
                    if (!pads.Any(p => p.IntersectsWith(cell)))
                        yield return new Point(x, y);
                }
            }
        }
    }

    private void CoverAllScreens()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(() =>
        {
            CoverAllScreens();
            ScheduleRefresh();
        });

    // --- Desktop level ---

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        // Never takes the focus (so it never jumps above the pads), and no taskbar button.
        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000;
        SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        CurrentApp.ShellHookService.SendToDesktopLevel(handle);
    }

    /// <summary>Back to the bottom: just above the wallpaper, below the pads (pin it before them).</summary>
    public void PinToDesktop()
    {
        if (_raised > 0)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            CurrentApp.ShellHookService.SendToDesktopLevel(handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ShellHookService.WindowPosChangingMessage && _raised == 0)
            CurrentApp.ShellHookService.EnforceDesktopZOrder(lParam);

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CurrentApp.PaddockManager.LayoutChanged -= ScheduleRefresh;
        base.OnClosed(e);
    }

    // --- Find place ---

    /// <summary>Lifts the desktop above every window for a moment and blinks one of its icons.</summary>
    public async void Spotlight(string path)
    {
        var icon = _icons.FirstOrDefault(i => string.Equals(i.Icon.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (icon is null)
            return;

        _raised++;
        Topmost = true;

        const int blink = 260;
        for (var i = 0; i < 5; i++)
        {
            icon.Icon.IsSpotlit = i % 2 == 0;
            await Task.Delay(blink);
        }

        icon.Icon.IsSpotlit = true;
        await Task.Delay(3000 - 5 * blink);
        icon.Icon.IsSpotlit = false;

        if (--_raised > 0)
            return;

        Topmost = false;
        PinToDesktop();
    }

    // --- Mouse ---

    private static bool OpensOnSingleClick
        => string.Equals(CurrentApp.Settings.Settings.IconOpenMode, "single", StringComparison.OrdinalIgnoreCase);

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LayerIcon icon } element)
            return;

        if (e.ClickCount == 2 && !OpensOnSingleClick)
        {
            icon.Icon.Launch();
            _pressed = null;
            e.Handled = true;
            return;
        }

        _pressed = icon;
        _dragStart = e.GetPosition(element);
        _dragStarted = false;
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _pressed;
        _pressed = null;

        if (OpensOnSingleClick && !_dragStarted && sender is FrameworkElement { DataContext: LayerIcon icon } && icon == pressed)
        {
            icon.Icon.Launch();
            e.Handled = true;
        }
    }

    /// <summary>Dragging an icon: into a pad, into a folder in Explorer, anywhere files go.</summary>
    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStarted || _pressed is null)
            return;

        if (sender is not FrameworkElement { DataContext: LayerIcon icon } element || icon != _pressed)
            return;

        var delta = e.GetPosition(element) - _dragStart;
        if (Math.Abs(delta.X) < MinDragDistance && Math.Abs(delta.Y) < MinDragDistance)
            return;

        _dragStarted = true;

        var data = new DataObject();
        data.SetData(typeof(IconDragData), new IconDragData { SourcePaddockId = SourceId, DesktopPath = icon.Icon.FullPath });
        data.SetData(DataFormats.FileDrop, new[] { icon.Icon.FullPath });

        DragDrop.DoDragDrop(element, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);

        _pressed = null;
        ScheduleRefresh();
    }

    /// <summary>Windows' own menu for the item (Open, Send to, WinRAR…, Rename, Delete).</summary>
    private void Icon_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LayerIcon icon } element)
            return;

        e.Handled = true;

        var handle = new WindowInteropHelper(this).Handle;
        var point = element.PointToScreen(e.GetPosition(element));

        if (ShellContextMenu.Show(icon.Icon.FullPath, handle, (int)point.X, (int)point.Y) == ShellMenuResult.Rename)
            Rename(icon.Icon.FullPath);

        ScheduleRefresh();
    }

    /// <summary>Renames the real file; the name keeps its extension out of the way, as on the desktop.</summary>
    private static void Rename(string path)
    {
        var fileName = Path.GetFileName(path);
        var isShortcut = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
        var shown = isShortcut ? Path.GetFileNameWithoutExtension(path) : fileName;
        var stem = Directory.Exists(path) || isShortcut ? shown.Length : Path.GetFileNameWithoutExtension(fileName).Length;

        while (DigitalDialog.Prompt("Rename", "New name:", shown, stem, "RENAME") is { } answer)
        {
            var newName = isShortcut ? answer.Trim() + Path.GetExtension(path) : answer.Trim();
            if (newName == fileName)
                return;

            var error = RenameItem(path, newName);
            if (error is null)
                return;

            DigitalDialog.Inform("Can't rename", error);
            shown = answer;
        }
    }

    private static string? RenameItem(string path, string newName)
    {
        if (newName.Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Lang.T("A name can't be empty or contain any of these: \\ / : * ? \" < > |");

        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase) && IconStore.ItemExists(target))
            return Lang.F("There is already an item called \"{0}\".", newName);

        try
        {
            if (Directory.Exists(path))
                Directory.Move(path, target);
            else
                File.Move(path, target);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not rename '{path}'.", ex);
            return Lang.F("Windows would not rename it: {0}", ex.Message);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

/// <summary>One icon on padx's desktop, and where it sits.</summary>
public sealed class LayerIcon(IconViewModel icon) : INotifyPropertyChanged
{
    public IconViewModel Icon { get; } = icon;

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }
    public double IconSize { get; private set; }

    public void Place(double x, double y, double width, double height, double iconSize)
    {
        Set(ref x, X, v => X = v, nameof(X));
        Set(ref y, Y, v => Y = v, nameof(Y));
        Set(ref width, Width, v => Width = v, nameof(Width));
        Set(ref height, Height, v => Height = v, nameof(Height));
        Set(ref iconSize, IconSize, v => IconSize = v, nameof(IconSize));
    }

    private void Set(ref double value, double current, Action<double> assign, string name)
    {
        if (value.Equals(current))
            return;

        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
