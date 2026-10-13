using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Paddock.App.Helpers;
using Paddock.App.ViewModels;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Views;

/// <summary>
/// The visible paddock: title strip, icon grid, and all of the mouse handling
/// for moving, resizing and filling it. Geometry changes are applied to the
/// hosting <see cref="PaddockWindow"/>, since each paddock is its own window.
/// </summary>
public partial class PaddockControl : UserControl
{
    /// <summary>Minimum movement before a mouse-down turns into an icon drag.</summary>
    private const double MinDragDistance = 5;

    private System.ComponentModel.PropertyChangedEventHandler? _vmPropertyChangedHandler;

    // Window-drag state
    private bool _isDraggingWindow;
    private Point _dragGrabOffset;

    // Icon-drag state
    private Point _iconDragStart;
    private bool _iconDragStarted;

    public PaddockControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        BuildColorMenu();
    }

    /// <summary>
    /// Scrolls an icon into view and blinks it a few times, then leaves it lit
    /// until <paramref name="duration"/> is up — the desktop search's "Find place".
    /// </summary>
    public async void Spotlight(string path, TimeSpan duration)
    {
        var icon = ViewModel.Icons.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (icon is null)
            return;

        // The pad may have just been expanded or shown; let it lay out first.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        (IconItems.ItemContainerGenerator.ContainerFromItem(icon) as FrameworkElement)?.BringIntoView();

        const int blink = 260;
        for (var i = 0; i < 5; i++)
        {
            icon.IsSpotlit = i % 2 == 0;
            await Task.Delay(blink);
        }

        icon.IsSpotlit = true;
        await Task.Delay(Math.Max(0, (int)duration.TotalMilliseconds - 5 * blink));
        icon.IsSpotlit = false;
    }

    /// <summary>Glass materials first, then "Default", then one swatch item per palette colour.</summary>
    private void BuildColorMenu()
    {
        foreach (var colour in PadPalette.Materials)
            AddSwatchItem(colour);

        ColorMenu.Items.Add(new Separator());
        ColorMenu.Items.Add(new MenuItem { Header = "Default", Tag = null });
        ColorMenu.Items.Add(new Separator());

        foreach (var colour in PadPalette.PadColors)
            AddSwatchItem(colour);

        foreach (var item in ColorMenu.Items.OfType<MenuItem>())
            item.Click += (_, _) => ViewModel.SetColor(item.Tag as string);
    }

    private void AddSwatchItem(PaletteColor colour)
    {
        ColorMenu.Items.Add(new MenuItem
        {
            Header = colour.Name,
            Tag = colour.Hex,
            Icon = new System.Windows.Shapes.Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = colour.Brush,
                Stroke = (System.Windows.Media.Brush)FindResource("D.LineStrong"),
                StrokeThickness = 1,
            },
        });
    }

    /// <summary>Ticks the pad's current colour each time the menu opens.</summary>
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var current = ViewModel.CustomColor;

        foreach (var item in ColorMenu.Items.OfType<MenuItem>())
            item.IsChecked = string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase);
    }

    private PaddockViewModel ViewModel => (PaddockViewModel)DataContext;

    private PaddockWindow? HostWindow => Window.GetWindow(this) as PaddockWindow;

    private static App CurrentApp => (App)Application.Current;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PaddockViewModel oldVm && _vmPropertyChangedHandler is not null)
            oldVm.PropertyChanged -= _vmPropertyChangedHandler;

        if (e.NewValue is PaddockViewModel vm)
        {
            UpdateTitleVisibility(vm.TitleVisibility);
            _vmPropertyChangedHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(PaddockViewModel.IsRenaming))
                    UpdateRenameState(vm.IsRenaming);
                else if (args.PropertyName == nameof(PaddockViewModel.TitleVisibility))
                    UpdateTitleVisibility(vm.TitleVisibility);
            };
            vm.PropertyChanged += _vmPropertyChangedHandler;
        }
    }

    // --- Title ---

    private void UpdateTitleVisibility(TitleVisibility mode)
    {
        TitleText.Visibility = mode == TitleVisibility.Hidden
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (mode == TitleVisibility.Always)
            TitleText.Opacity = 1.0;
    }

    private void UpdateRenameState(bool isRenaming)
    {
        if (isRenaming)
        {
            TitleText.Visibility = Visibility.Collapsed;
            RenameBox.Visibility = Visibility.Visible;

            // Deferred: when rename is started from the context menu, the menu is
            // still closing and would take focus straight back off the text box.
            Dispatcher.InvokeAsync(
                () =>
                {
                    // The window has to be active for the box to take keystrokes.
                    HostWindow?.Activate();
                    RenameBox.Focus();
                    Keyboard.Focus(RenameBox);
                    RenameBox.SelectAll();
                },
                DispatcherPriority.Input);
        }
        else
        {
            RenameBox.Visibility = Visibility.Collapsed;
            UpdateTitleVisibility(ViewModel.TitleVisibility);
        }
    }

    // --- Moving the paddock ---

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ViewModel.ToggleRollUp();
            e.Handled = true;
            return;
        }

        if (ViewModel.IsLocked || ViewModel.IsRenaming)
            return;

        var window = HostWindow;
        if (window is null)
            return;

        var cursor = ScreenHelper.GetCursorPosition();
        _dragGrabOffset = new Point(cursor.X - window.Left, cursor.Y - window.Top);
        _isDraggingWindow = true;
        TitleArea.CaptureMouse();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingWindow)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndWindowDrag();
            return;
        }

        var window = HostWindow;
        if (window is null)
            return;

        var cursor = ScreenHelper.GetCursorPosition();
        var targetX = cursor.X - _dragGrabOffset.X;
        var targetY = cursor.Y - _dragGrabOffset.Y;

        var settings = CurrentApp.Settings.Settings;
        if (settings.SnapEnabled)
        {
            // Line up with the other visible pads and the edges of the monitor
            // it is being dragged onto, as drawn on screen (rolled-up pads are short).
            var bounds = ScreenHelper.GetWorkAreaFor(targetX, targetY, window.ActualWidth, window.ActualHeight);
            var others = CurrentApp.Host?.Windows
                .Where(w => w != window && w.IsVisible)
                .Select(w => new PlacedRect(w.Left, w.Top, w.ActualWidth, w.ActualHeight))
                .ToList() ?? [];

            var snapped = SnapEngine.Snap(
                new PlacedRect(targetX, targetY, window.ActualWidth, window.ActualHeight),
                others, bounds, settings.IntelligentSpacing);

            targetX = snapped.X;
            targetY = snapped.Y;
            Guides.Show(snapped.Guides);
        }

        window.Left = targetX;
        window.Top = targetY;
    }

    /// <summary>Alignment guides, shared by all pads (only one is dragged at a time).</summary>
    private static readonly SnapGuideOverlay Guides = new();

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingWindow)
            EndWindowDrag();
    }

    private void EndWindowDrag()
    {
        _isDraggingWindow = false;
        TitleArea.ReleaseMouseCapture();
        Guides.Hide();

        var window = HostWindow;
        if (window is not null)
        {
            ViewModel.UpdatePosition(window.Left, window.Top);
            CurrentApp.Host?.KeepOutOfSearchStrip(window);
        }
    }

    // --- Resizing ---

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        var window = HostWindow;
        if (window is null)
            return;

        var bounds = ScreenHelper.GetWorkAreaFor(window.Left, window.Top, window.Width, window.Height);

        var maxWidth = Math.Max(window.MinWidth, bounds.Right - window.Left);
        var maxHeight = Math.Max(window.MinHeight, bounds.Bottom - window.Top);

        window.Width = Math.Clamp(window.Width + e.HorizontalChange, window.MinWidth, maxWidth);
        window.Height = Math.Clamp(window.Height + e.VerticalChange, window.MinHeight, maxHeight);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel.IsLocked)
            return;

        var window = HostWindow;
        if (window is not null)
            ViewModel.UpdateSize(window.Width, window.Height);
    }

    // --- Rename ---

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelRename();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRenaming)
            ViewModel.CommitRename();
    }

    // --- Close ---

    /// <summary>The × hides the pad; deleting is in the menu.</summary>
    private void CloseButton_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.Hide();
        e.Handled = true;
    }

    private void ContextMenu_Hide(object sender, RoutedEventArgs e) => ViewModel.Hide();

    // --- Dropping items in ---

    private void Paddock_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAccept(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Paddock_DragEnter(object sender, DragEventArgs e)
    {
        if (CanAccept(e))
            DropHighlight.Visibility = Visibility.Visible;
    }

    private void Paddock_DragLeave(object sender, DragEventArgs e)
        => DropHighlight.Visibility = Visibility.Collapsed;

    private static bool CanAccept(DragEventArgs e)
        => e.Data.GetDataPresent(typeof(IconDragData))
           || e.Data.GetDataPresent(DataFormats.FileDrop);

    private void Paddock_Drop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;

        var sourcePaddockId = ViewModel.HandleDrop(e);

        if (sourcePaddockId is not null)
            CurrentApp.Host?.RefreshPaddock(sourcePaddockId);

        CurrentApp.DesktopIconService.NotifyShellOfDesktopChange();

        // We moved the files ourselves, so tell the drag source there is nothing
        // left for it to clean up. Reporting Move here would invite Explorer to
        // delete a source path we have already emptied.
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    // --- Icons ---

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        // StackPanel has no MouseDoubleClick event, so detect it via ClickCount.
        if (e.ClickCount == 2 && !OpensOnSingleClick && element.DataContext is IconViewModel icon)
        {
            icon.Launch();
            _iconDragStarted = false;
            e.Handled = true;
            return;
        }

        _iconDragStart = e.GetPosition(element);
        _iconDragStarted = false;
        _iconPressed = element;
    }

    /// <summary>Single-click mode: a press and release on the same icon, without a drag, opens it.</summary>
    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _iconPressed;
        _iconPressed = null;

        if (!OpensOnSingleClick || _iconDragStarted || pressed != sender)
            return;

        if (sender is FrameworkElement { DataContext: IconViewModel icon })
        {
            icon.Launch();
            e.Handled = true;
        }
    }

    /// <summary>The icon the left button went down on, until it comes up again.</summary>
    private object? _iconPressed;

    private static bool OpensOnSingleClick
        => string.Equals(CurrentApp.Settings.Settings.IconOpenMode, "single", StringComparison.OrdinalIgnoreCase);

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _iconDragStarted = false;
            return;
        }

        if (_iconDragStarted)
            return;

        if (sender is not FrameworkElement element || element.DataContext is not IconViewModel icon)
            return;

        // Require a little movement first, so double-click-to-launch still works.
        var delta = e.GetPosition(element) - _iconDragStart;
        if (Math.Abs(delta.X) < MinDragDistance && Math.Abs(delta.Y) < MinDragDistance)
            return;

        _iconDragStarted = true;

        var data = new DataObject();
        data.SetData(typeof(IconDragData), new IconDragData
        {
            SourcePaddockId = ViewModel.Id,
            DesktopPath = icon.FullPath
        });

        // Also offer the plain file, so the item can be dropped into Explorer
        // or any other application that accepts files.
        data.SetData(DataFormats.FileDrop, new[] { icon.FullPath });

        DragDrop.DoDragDrop(element, data, DragDropEffects.Move | DragDropEffects.Copy);

        _iconDragStarted = false;
        HandleIconDragFinished(icon);
    }

    /// <summary>
    /// After a drag ends: if the item was not dropped on a paddock, it belongs
    /// back on the desktop. That also cleans up the entry when another
    /// application (Explorer, say) moved the file itself.
    /// </summary>
    private void HandleIconDragFinished(IconViewModel icon)
    {
        var droppedOnPaddock = CurrentApp.Host?.FindWindowAt(ScreenHelper.GetCursorPosition()) is not null;
        if (droppedOnPaddock)
            return;

        ViewModel.EjectIcon(icon.FullPath);
        CurrentApp.DesktopIconService.NotifyShellOfDesktopChange();
    }

    private void Icon_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IconViewModel icon } element)
            return;

        // Handled here so the paddock's own context menu does not also open.
        e.Handled = true;

        var window = Window.GetWindow(this);
        var handle = window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
        var screenPoint = element.PointToScreen(e.GetPosition(element));

        var result = ShellContextMenu.Show(icon.FullPath, handle, (int)screenPoint.X, (int)screenPoint.Y);

        if (result == ShellMenuResult.Rename)
            RenameIcon(icon);

        // The menu may have deleted or renamed the item.
        CurrentApp.Host?.RefreshFromDisk();
    }

    /// <summary>
    /// Asks for a new name. Shortcut extensions stay hidden (Explorer never
    /// shows them); for other files only the part before the extension is
    /// pre-selected.
    /// </summary>
    private void RenameIcon(IconViewModel icon)
    {
        var fileName = System.IO.Path.GetFileName(icon.FullPath);
        var extension = System.IO.Path.GetExtension(fileName);
        var hideExtension = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
        var isFolder = System.IO.Directory.Exists(icon.FullPath);

        var shown = hideExtension ? System.IO.Path.GetFileNameWithoutExtension(fileName) : fileName;
        var stemLength = isFolder || hideExtension ? shown.Length : System.IO.Path.GetFileNameWithoutExtension(fileName).Length;

        while (true)
        {
            var answer = DigitalDialog.Prompt("Rename", "New name:", shown, stemLength, "RENAME");
            if (answer is null)
                return;

            var newName = hideExtension ? answer.Trim() + extension : answer.Trim();
            var error = CurrentApp.PaddockManager.RenameIcon(ViewModel.Id, icon.FullPath, newName);

            if (error is null)
            {
                ViewModel.RefreshIcons();
                return;
            }

            DigitalDialog.Inform("Can't rename", error);
            shown = answer;
        }
    }

    // --- Context menu ---

    private void ContextMenu_Rename(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginRenameTitle();
    }

    /// <summary>
    /// Deletes the pad. Nothing is lost — its items go back to the desktop —
    /// but rebuilding a full pad is tedious, so a pad with icons asks first.
    /// An empty one simply goes.
    /// </summary>
    private void ContextMenu_Remove(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.IconCount;

        if (count > 0 && !DigitalDialog.Confirm("Delete pad",
                Lang.F("Delete \"{0}\"? Its {1} icon(s) go back to the desktop.", ViewModel.Title, count), "DELETE"))
        {
            return;
        }

        ViewModel.Remove();
    }

    private void ContextMenu_ToggleRollUp(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleRollUp();
    }

    /// <summary>
    /// Moves a whole category of desktop items into this paddock. The category
    /// comes from the menu item's Tag, and the count is always confirmed first —
    /// this moves real files.
    /// </summary>
    private void ContextMenu_FillFromDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }
            || !Enum.TryParse<DesktopItemCategory>(tag, out var category))
        {
            return;
        }

        var items = DesktopItemFilter.Filter(CurrentApp.DesktopIconService.GetDesktopItems(), category);
        var description = Lang.T(DesktopItemFilter.Describe(category));

        if (items.Count == 0)
        {
            DigitalDialog.Inform("Fill from Desktop", Lang.F("There are no {0} on the desktop to collect.", description));
            return;
        }

        if (!DigitalDialog.Confirm("Fill from Desktop",
                Lang.F("Move {0} {1} into \"{2}\"?", items.Count, description, ViewModel.Title) + "\n\n" +
                Lang.T("They will be moved off the desktop into this pad's folder, and " +
                       "go back to the desktop if you drag them out or remove the pad."),
                "MOVE"))
        {
            return;
        }

        ViewModel.AddIcons(items);
        CurrentApp.DesktopIconService.NotifyShellOfDesktopChange();
    }

    private void TitleVisibility_Always(object sender, RoutedEventArgs e)
        => ViewModel.TitleVisibility = TitleVisibility.Always;

    private void TitleVisibility_Hover(object sender, RoutedEventArgs e)
        => ViewModel.TitleVisibility = TitleVisibility.Hover;

    private void TitleVisibility_Hidden(object sender, RoutedEventArgs e)
        => ViewModel.TitleVisibility = TitleVisibility.Hidden;

    private void Sort_Name(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.Name;

    private void Sort_Date(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.DateModified;

    private void Sort_Type(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.FileType;

    private void Sort_Size(object sender, RoutedEventArgs e) => ViewModel.SortBy = SortField.FileSize;

    private void Sort_ToggleDirection(object sender, RoutedEventArgs e)
        => ViewModel.SortAscending = !ViewModel.SortAscending;
}
