using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using Paddock.App.Helpers;
using Paddock.App.ViewModels;
using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.App.Views;

/// <summary>
/// Search-as-you-type over the desktop: every item in every pad (hidden ones
/// too) and the items still on the desktop. Enter runs the selected one,
/// Ctrl+Enter or right-click → Find place shows where it is.
///
/// Two ways to live: a popup that the shortcut opens over everything and that
/// goes away when it loses focus, or a box docked at the top or bottom of the
/// main screen, sitting at desktop level like the pads until it is clicked or
/// its shortcut is pressed.
/// </summary>
public partial class SearchWindow : Window
{
    /// <summary>Height of the box itself, and the gap kept around a docked one (DIPs).</summary>
    internal const double BoxHeight = 46;
    internal const double DockGap = 10;

    /// <summary>The strip a docked box takes from the main screen.</summary>
    internal const double StripHeight = BoxHeight + 2 * DockGap + 2;

    /// <summary>More than this many matches and the list stops; refine the search instead.</summary>
    private const int MaxResults = 60;

    private readonly bool _docked;
    private readonly bool _atBottom;
    private List<SearchItem> _items = [];
    private Dictionary<string, IconViewModel> _icons = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hiddenPads = [];

    private readonly bool _glass;

    /// <param name="dockedAt">"top" or "bottom" for an always-visible box, null for the popup.</param>
    /// <param name="glass">Frosted glass instead of a colour.</param>
    public SearchWindow(string? dockedAt, bool glass)
    {
        InitializeComponent();

        _docked = dockedAt is not null;
        _atBottom = dockedAt == "bottom";
        _glass = glass;
        Topmost = !_docked;

        if (_glass)
            BecomeGlass();

        ApplyLook();

        if (_atBottom)
        {
            // Results open upwards; "no matches" and the divider sit next to the box.
            DockPanel.SetDock(Box, Dock.Bottom);
            ResultsArea.Children.Remove(NoMatches);
            ResultsArea.Children.Remove(Divider);
            ResultsArea.Children.Add(NoMatches);
            ResultsArea.Children.Add(Divider);
        }

        RefreshHint();

        Activated += (_, _) => OnActivated();
        Deactivated += (_, _) => OnDeactivated();
        SizeChanged += (_, _) => KeepAnchored();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private static App CurrentApp => (App)Application.Current;

    /// <summary>Anthracite: the glass's tint.</summary>
    private static readonly Color GlassTint = Color.FromRgb(0x26, 0x2A, 0x31);

    private bool _inUse;

    /// <summary>
    /// The colour and opacity from settings; for glass, its tint — full
    /// strength in use, half at rest, so the resting box is clear glass.
    /// </summary>
    public void ApplyLook()
    {
        var settings = CurrentApp.Settings.Settings;

        if (!_glass)
        {
            Card.Background = PadVisuals.Solid(settings.SearchBoxColor, Math.Clamp(settings.SearchBoxOpacity, 0.3, 1), Color.FromRgb(0x0B, 0x0E, 0x13));
            return;
        }

        var tint = Math.Clamp(settings.SearchGlassOpacity, 0, 0.9) * (_inUse || !_docked ? 1 : 0.5);
        Card.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(tint * 255), GlassTint.R, GlassTint.G, GlassTint.B));
    }

    /// <summary>
    /// See-through frosted glass: an ordinary (not layered) window whose whole
    /// area is "glass frame", blurred behind like the glass pads, tinted by
    /// <see cref="ApplyLook"/>. Being a normal window it also gets Windows'
    /// rounded corners and shadow.
    /// </summary>
    private void BecomeGlass()
    {
        AllowsTransparency = false;
        Width -= Card.Margin.Left + Card.Margin.Right;
        Card.Margin = new Thickness(0);
        Card.Effect = null;
        Card.Background = Brushes.Transparent;
        Card.BorderThickness = new Thickness(0);
        Card.CornerRadius = new CornerRadius(0);

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(-1),
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target })
                target.BackgroundColor = Colors.Transparent;

            Dwm.Set(handle, Dwm.UseImmersiveDarkMode, 1);
            Dwm.Set(handle, Dwm.WindowCornerPreference, Dwm.RoundCorners);

            // Plain blur, not acrylic: acrylic comes out dark and solid, like metal.
            if (Paddock.Shell.GlassEffect.IsBlurAvailable)
                Paddock.Shell.GlassEffect.Apply(handle, GlassBlur.Light);
        };
    }

    private static class Dwm
    {
        internal const int UseImmersiveDarkMode = 20;
        internal const int WindowCornerPreference = 33;
        internal const int BorderColor = 34;

        /// <summary>DWMWA_COLOR_DEFAULT: Windows' own edge.</summary>
        internal const int DefaultBorder = unchecked((int)0xFFFFFFFF);
        internal const int RoundCorners = 2;

        internal static void Set(IntPtr hwnd, int attribute, int value)
            => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>Shows the current shortcut at the end of the box.</summary>
    public void RefreshHint() => HotkeyHint.Text = CurrentApp.Settings.Settings.SearchHotkey.ToUpperInvariant();

    /// <summary>Opens the search ready to type: the popup appears, a docked box comes to the front.</summary>
    public void Summon()
    {
        Position();
        Topmost = true;

        if (!IsVisible)
            Show();

        Activate();
        NativeFocus();
    }

    /// <summary>Shows a docked box at desktop level (it is summoned later).</summary>
    public void ShowDocked()
    {
        Position();
        Show();
        ShowInUse(false);
        PinToDesktop();
    }

    /// <summary>Keeps a docked box at desktop level unless it is in use.</summary>
    public void PinToDesktop()
    {
        if (!_docked || IsActive)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            CurrentApp.ShellHookService.SendToDesktopLevel(handle);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        // Tool window: no taskbar button, no Alt+Tab entry.
        if (_docked)
            CurrentApp.ShellHookService.SendToDesktopLevel(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Letting go of Alt (after Alt+Q) would otherwise open the window's
        // system-menu mode and swallow the next key.
        if (msg == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_KEYMENU)
        {
            handled = true;
            return IntPtr.Zero;
        }

        // A docked box not in use stays at desktop level, like the pads.
        if (msg == Paddock.Shell.ShellHookService.WindowPosChangingMessage && _docked && !Topmost)
            CurrentApp.ShellHookService.EnforceDesktopZOrder(lParam);

        return IntPtr.Zero;
    }

    // --- Placement ---

    private double _anchorBottom;

    /// <summary>
    /// The popup: a little below the top of the screen the mouse is on. Docked:
    /// centred in the strip kept for it at the top or bottom of the main screen.
    /// </summary>
    private void Position()
    {
        var area = ScreenHelper.GetFullWorkArea(underCursor: !_docked);
        var card = Card.Margin;
        Left = area.Left + (area.Width - Width) / 2;

        if (!_docked)
        {
            Top = area.Top + Math.Max(60, area.Height * 0.12) - card.Top;
        }
        else if (!_atBottom)
        {
            Top = area.Top + DockGap - card.Top;
        }
        else
        {
            _anchorBottom = area.Bottom - DockGap + card.Bottom;
            KeepAnchored();
        }
    }

    /// <summary>A box at the bottom grows upwards as results appear.</summary>
    private void KeepAnchored()
    {
        if (_atBottom && _anchorBottom > 0)
            Top = _anchorBottom - ActualHeight;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(() =>
        {
            if (_docked)
                Position();
        });

    // --- Focus ---

    private void OnActivated()
    {
        Topmost = true;
        ShowInUse(true);
        LoadItems();
        Search();
        NativeFocus();
    }

    private void OnDeactivated()
    {
        QueryBox.Text = string.Empty;

        if (!_docked)
        {
            Hide();
            return;
        }

        Topmost = false;
        ShowInUse(false);
        PinToDesktop();
    }

    private DropShadowEffect? _shadow;

    /// <summary>
    /// A docked box at rest is quiet — faint edge, no shadow — so it sits on
    /// the desktop like part of it. In use it lights up: accent edge, shadow.
    /// (The popup is only ever seen in use.)
    /// </summary>
    private void ShowInUse(bool inUse)
    {
        _inUse = inUse;
        ApplyLook();

        var lit = inUse || !_docked;
        var accent = (Color)FindResource("D.AccentColor");

        if (_glass)
        {
            // Windows draws the glass window's edge; colour it.
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                Dwm.Set(handle, Dwm.BorderColor, lit ? accent.R | accent.G << 8 | accent.B << 16 : Dwm.DefaultBorder);
            return;
        }

        _shadow ??= Card.Effect as DropShadowEffect;
        Card.Effect = lit ? _shadow : null;
        Card.BorderBrush = lit
            ? new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B))
            : (Brush)FindResource("D.Line");
    }

    /// <summary>Puts the caret in the box, also when the window was just brought forward.</summary>
    private void NativeFocus()
    {
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.CaretIndex = QueryBox.Text.Length;
    }

    /// <summary>
    /// Leaves the search: the popup goes away, a docked box returns to the
    /// desktop. <paramref name="handFocusBack"/> is false when something just
    /// launched should get the focus instead.
    /// </summary>
    private void Dismiss(bool handFocusBack = true)
    {
        if (!_docked)
        {
            Hide();
            return;
        }

        QueryBox.Text = string.Empty;
        Topmost = false;
        ShowInUse(false);

        // Hand the focus to the desktop so the box counts as left.
        if (handFocusBack)
            NativeMethods.SetForegroundWindow(CurrentApp.ShellHookService.GetDesktopWindow());

        PinToDesktop();
    }

    // --- Items ---

    /// <summary>
    /// A fresh snapshot of what can be found. Pad items reuse the pads' own
    /// icons (already loaded); desktop items are read from the desktop folders.
    /// </summary>
    private void LoadItems()
    {
        var app = CurrentApp;
        var items = new List<SearchItem>();
        var icons = new Dictionary<string, IconViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var window in app.Host?.Windows ?? [])
        {
            var pad = window.ViewModel;
            foreach (var icon in pad.Icons)
            {
                if (icons.TryAdd(icon.FullPath, icon))
                    items.Add(new SearchItem(icon.FullPath, icon.DisplayName, pad.Id, pad.Title));
            }
        }

        // What padx's desktop shows: everything in no pad, built-in icons included.
        foreach (var icon in app.DesktopLayer?.Icons ?? [])
        {
            if (icons.TryAdd(icon.FullPath, icon))
                items.Add(new SearchItem(icon.FullPath, icon.DisplayName, null, DesktopSearch.DesktopGroup));
        }

        _items = items;
        _icons = icons;
        _hiddenPads = app.PaddockManager.GetPaddocks().Where(p => p.Hidden).Select(p => p.Id).ToHashSet();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Search();
    }

    private void Search()
    {
        var query = QueryBox.Text;
        if (query.Trim().Length == 0)
        {
            Results.ItemsSource = null;
            ResultsArea.Visibility = Visibility.Collapsed;
            return;
        }

        var found = DesktopSearch.Find(_items, query)
            .Take(MaxResults)
            .Select(item => new SearchResult(item, _icons[item.Path], item.PadId is not null && _hiddenPads.Contains(item.PadId)))
            .ToList();

        var anyOnDesktop = found.Count > 0;

        // Last of all: the same words on the web.
        if (CurrentApp.Settings.Settings.SearchOffersWeb)
            found.Add(SearchResult.Web(query.Trim(), Country));

        // A box at the bottom reads upwards: best match next to it, Google on top.
        if (_atBottom)
            found.Reverse();

        Results.ItemsSource = found;
        Results.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatches.Visibility = anyOnDesktop ? Visibility.Collapsed : Visibility.Visible;
        ResultsArea.Visibility = Visibility.Visible;

        if (found.Count > 0)
        {
            // The row next to the box starts selected.
            Results.SelectedIndex = _atBottom ? found.Count - 1 : 0;
            Results.ScrollIntoView(Results.SelectedItem);
        }
    }

    private SearchResult? Selected => Results.SelectedItem as SearchResult;

    // --- Keys ---

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                break;

            case Key.Down:
                Move(+1);
                break;

            case Key.Up:
                Move(-1);
                break;

            case Key.PageDown:
                Move(+8);
                break;

            case Key.PageUp:
                Move(-8);
                break;

            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                FindPlace(Selected);
                break;

            case Key.Enter:
                Run(Selected);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Moves the selection; the caret stays in the box so typing carries on.</summary>
    private void Move(int by)
    {
        if (Results.Items.Count == 0)
            return;

        var index = Math.Clamp(Results.SelectedIndex + by, 0, Results.Items.Count - 1);
        Results.SelectedIndex = index;
        Results.ScrollIntoView(Results.SelectedItem);
        NativeFocus();
    }

    // --- Mouse ---

    /// <summary>Results are like links: one click opens them.</summary>
    private void Results_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource) is { } result)
        {
            e.Handled = true;
            Run(result);
        }
    }

    /// <summary>Right-click selects the row under the mouse before its menu opens.</summary>
    private void Results_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The web row has no menu: it can only be run.
        if (ItemUnder(e.OriginalSource) is { IsWeb: false } result)
            Results.SelectedItem = result;
        else
            e.Handled = true;
    }

    private static SearchResult? ItemUnder(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = ParentOf(element))
        {
            if (element is ListBoxItem { DataContext: SearchResult result })
                return result;
        }

        return null;
    }

    /// <summary>
    /// Up the tree from anything that can be clicked — including text runs,
    /// which are not visuals and have only a logical parent.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject element)
        => element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private void Menu_Run(object sender, RoutedEventArgs e) => Run(Selected);

    private void Menu_FindPlace(object sender, RoutedEventArgs e) => FindPlace(Selected);

    // --- Actions ---

    private void Run(SearchResult? result)
    {
        if (result is null)
            return;

        // We hold the foreground right now; pass it on to whatever starts.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        if (result.WebUrl is { } url)
        {
            Log.Info("Search: opening a Google search in the default browser.");
            OpenInBrowser(url);
        }
        else
        {
            Log.Info($"Search: running '{result.Name}'.");
            result.Icon!.Launch();
        }

        Dismiss(handFocusBack: false);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the browser.", ex);
        }
    }

    /// <summary>
    /// The country set in Windows (Settings → Time &amp; language → Region),
    /// e.g. "TR"; failing that, the one in the regional format.
    /// </summary>
    internal static string? Country => _country ??= ReadCountry();

    private static string? _country;

    private static string? ReadCountry()
    {
        try
        {
            var buffer = new System.Text.StringBuilder(16);
            if (NativeMethods.GetUserDefaultGeoName(buffer, buffer.Capacity) > 0 && buffer.Length == 2)
                return buffer.ToString();
        }
        catch (Exception)
        {
            // Older Windows: fall through.
        }

        try
        {
            return System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows where an item is: its pad, or the desktop, comes forward for a
    /// moment with the item blinking.
    /// </summary>
    private void FindPlace(SearchResult? result)
    {
        if (result is null || result.IsWeb)
            return;

        Dismiss();

        if (result.Item.PadId is { } padId)
            CurrentApp.Host?.Spotlight(padId, result.Item.Path);
        else
            CurrentApp.DesktopLayer?.Spotlight(result.Item.Path);
    }

    private static class NativeMethods
    {
        internal const int ASFW_ANY = -1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool AllowSetForegroundWindow(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern int GetUserDefaultGeoName(System.Text.StringBuilder geoName, int geoNameCount);
    }
}

/// <summary>One row of results: "Google Chrome [INTERNET]", or the closing "Search Google for …".</summary>
public sealed class SearchResult(SearchItem item, IconViewModel? icon, bool padHidden, string? webUrl = null)
{
    /// <summary>The row that searches the web for <paramref name="query"/> on the country's Google.</summary>
    public static SearchResult Web(string query, string? country)
    {
        var domain = WebSearch.GoogleDomain(country);
        var item = new SearchItem(string.Empty, Lang.F("Search Google for “{0}”", query), null, domain);
        return new SearchResult(item, null, false, WebSearch.GoogleUrl(query, country));
    }

    public SearchItem Item { get; } = item;

    /// <summary>The item's icon (and its Launch); null for the web row.</summary>
    public IconViewModel? Icon { get; } = icon;

    public string? WebUrl { get; } = webUrl;

    public bool IsWeb => WebUrl is not null;

    public string Name => Item.Name;

    public string Label => $"[{Item.Group.ToUpperInvariant()}]";

    /// <summary>Said of an item in a pad that is put away; Find place brings the pad back.</summary>
    public string Note => padHidden ? "hidden" : string.Empty;

    public Visibility ItemVisibility => IsWeb ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WebVisibility => IsWeb ? Visibility.Visible : Visibility.Collapsed;
}
