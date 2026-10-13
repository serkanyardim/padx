using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Paddock.App.ViewModels;
using Paddock.App.Views;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// <c>padx.exe --screenshots &lt;folder&gt;</c>: builds a throwaway desktop with
/// generic, anonymous items in a temp folder, organizes it with the real
/// categorizer and layout planner, and renders it — plus the Settings and
/// About windows — to PNG. The user's own desktop, pads and settings are
/// never touched, so the images are safe to publish.
/// </summary>
internal static class ShowcaseRenderer
{
    private const double Scale = 1.5;
    private static readonly Size DesktopSize = new(1600, 900);

    /// <summary>Real programs to link to, if installed: icons only, the targets are never shown.</summary>
    private static readonly (string Name, string Path)[] Programs =
    [
        ("Google Chrome", @"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
        ("Microsoft Edge", @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
        ("Brave", @"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe"),
        ("Vivaldi", @"%LOCALAPPDATA%\Vivaldi\Application\vivaldi.exe"),
        ("Visual Studio Code", @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe"),
        ("GitHub Desktop", @"%LOCALAPPDATA%\GitHubDesktop\GitHubDesktop.exe"),
        ("Notepad++", @"%ProgramFiles%\Notepad++\notepad++.exe"),
        ("PowerShell", @"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"),
        ("Command Prompt", @"%SystemRoot%\System32\cmd.exe"),
        ("Remote Desktop", @"%SystemRoot%\System32\mstsc.exe"),
        ("WinSCP", @"%ProgramFiles(x86)%\WinSCP\WinSCP.exe"),
        ("VMware Workstation", @"%ProgramFiles(x86)%\VMware\VMware Workstation\vmware.exe"),
        ("VLC media player", @"%ProgramFiles%\VideoLAN\VLC\vlc.exe"),
        ("Inkscape", @"%ProgramFiles%\Inkscape\bin\inkscape.exe"),
        ("Calculator", @"%SystemRoot%\System32\calc.exe"),
        ("Task Manager", @"%SystemRoot%\System32\Taskmgr.exe"),
        ("7-Zip", @"%ProgramFiles%\7-Zip\7zFM.exe"),
        ("Notepad", @"%SystemRoot%\System32\notepad.exe"),
    ];

    private static readonly string[] Folders =
        ["Projects", "Invoices", "Photos 2026", "Clients", "Design Assets", "Archive", "Music", "Travel"];

    private static readonly string[] Files =
    [
        "Project Plan.docx", "Budget 2026.xlsx", "Quarterly Report.pdf", "Meeting Notes.txt", "Roadmap.pptx",
        "Contacts.csv", "Invoice 0142.pdf", "Holiday.jpg", "Logo.png", "Product Demo.mp4", "Podcast Intro.mp3",
        "Banner.png", "Backup.zip", "Setup Tool.msi",
    ];

    /// <param name="desktop">The sandbox desktop folder the app's services were pointed at.</param>
    public static async Task RenderAsync(App app, string desktop, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);

        var items = CreateDesktopItems(desktop);
        var pads = Organize(app, items);

        // Category colours on a soft wallpaper, then the same desktop in glass.
        await RenderDesktopAsync(app, pads, Path.Combine(outputFolder, "desktop.png"));

        app.Settings.Settings.Appearance.PadColor = PadMaterials.Glass;
        foreach (var pad in pads)
            pad.Style.Color = null;
        await RenderDesktopAsync(app, pads, Path.Combine(outputFolder, "desktop-glass.png"));

        app.Settings.Settings.Appearance.PadColor = new PadAppearance().PadColor;
        await RenderWindowAsync(new SettingsWindow(), w => SelectTab(w, 2), Path.Combine(outputFolder, "settings-appearance.png"));
        await RenderWindowAsync(new SettingsWindow(), w => SelectTab(w, 3), Path.Combine(outputFolder, "settings-automode.png"));
        await RenderWindowAsync(new AboutWindow(), null, Path.Combine(outputFolder, "about.png"));

        Log.Info($"Showcase screenshots written to {outputFolder}.");
    }

    // --- The fake desktop ---

    private static List<string> CreateDesktopItems(string desktop)
    {
        Directory.CreateDirectory(desktop);
        var items = new List<string>();

        foreach (var folder in Folders)
        {
            var path = Path.Combine(desktop, folder);
            Directory.CreateDirectory(path);
            items.Add(path);
        }

        foreach (var file in Files)
        {
            var path = Path.Combine(desktop, file);
            File.WriteAllBytes(path, []);
            items.Add(path);
        }

        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        foreach (var (name, rawTarget) in Programs)
        {
            var target = Environment.ExpandEnvironmentVariables(rawTarget);
            if (!File.Exists(target))
                continue;

            var link = Path.Combine(desktop, name + ".lnk");
            dynamic shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [link])!;
            shortcut.TargetPath = target;
            shortcut.Save();
            items.Add(link);
        }

        // Built-in icons, under neutral names.
        foreach (var (icon, name) in new[] { (0, "This PC"), (1, "Alex"), (2, "Network"), (4, "Recycle Bin") })
        {
            var link = Path.Combine(desktop, name + ".lnk");
            if (SystemDesktopIcons.CreateShortcut(SystemDesktopIcons.All[icon], link))
                items.Add(link);
        }

        return items;
    }

    /// <summary>The same categorise → size → table steps Auto Organize uses.</summary>
    private static List<PaddockModel> Organize(App app, List<string> items)
    {
        var groups = items
            .GroupBy(p => IconCategorizer.Categorize(ShortcutReader.Describe(p)))
            .OrderBy(g => g.Key)
            .ToList();

        var metrics = AutoOrganizer.CurrentMetrics(app.Settings.Settings.Appearance);
        var rects = AutoLayoutPlanner.PlanGrid(
            groups.Select(g => g.Count()).ToList(), 6, metrics,
            new LayoutBounds(0, 0, DesktopSize.Width, DesktopSize.Height), 14, AutoStartCorner.TopLeft,
            keepFirst: groups[0].Key == AutoCategory.System)!;

        var pads = new List<PaddockModel>();
        for (var i = 0; i < groups.Count; i++)
        {
            var r = rects[i];
            var pad = app.PaddockManager.CreatePaddock(IconCategorizer.Title(groups[i].Key), r.X, r.Y, r.Width, r.Height);
            pad.TitleVisibility = TitleVisibility.Always;
            pad.Style.Color = AutoOrganizer.CategoryColors.GetValueOrDefault(groups[i].Key);

            var ordered = groups[i].Key == AutoCategory.System
                ? groups[i].OrderBy(p => SystemDesktopIcons.Rank(ShortcutReader.Describe(p).Target))
                : groups[i].AsEnumerable();

            if (groups[i].Key == AutoCategory.System)
                pad.SortBy = SortField.Manual;

            foreach (var item in ordered)
                app.PaddockManager.AddIconToPaddock(pad.Id, item);

            pads.Add(pad);
        }

        return pads;
    }

    // --- Rendering ---

    private static async Task RenderDesktopAsync(App app, List<PaddockModel> pads, string file)
    {
        var canvas = new Canvas { Width = DesktopSize.Width, Height = DesktopSize.Height, ClipToBounds = true };
        canvas.Children.Add(Wallpaper());

        var models = new List<PaddockViewModel>();
        foreach (var pad in pads)
        {
            var vm = new PaddockViewModel(pad, app.PaddockManager, () => app.Settings.Settings.Appearance);
            models.Add(vm);

            var control = new PaddockControl { DataContext = vm, Width = pad.Width, Height = pad.Height };
            Canvas.SetLeft(control, pad.X);
            Canvas.SetTop(control, pad.Y);
            canvas.Children.Add(control);
        }

        var window = OffscreenWindow(canvas);
        window.Show();

        // Icons arrive from the shell asynchronously; give them a moment.
        for (var waited = 0; waited < 8000 && models.SelectMany(m => m.Icons).Any(i => i.IconImage is null); waited += 100)
            await Task.Delay(100);

        await Task.Delay(300);
        Save(canvas, file);
        window.Close();
    }

    private static async Task RenderWindowAsync(Window window, Action<Window>? prepare, string file)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = 0;
        window.ShowActivated = false;
        window.Show();

        prepare?.Invoke(window);
        await Task.Delay(600);

        // The window itself, so its own background is in the picture.
        Save(window, file);
        window.Close();
    }

    private static Window OffscreenWindow(FrameworkElement content) => new()
    {
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = -20000,
        Top = 0,
        SizeToContent = SizeToContent.WidthAndHeight,
        Content = content,
    };

    private static void SelectTab(Window window, int index)
    {
        if (FindChild<TabControl>(window) is { } tabs)
            tabs.SelectedIndex = index;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindChild<T>(child) is { } deeper)
                return deeper;
        }

        return null;
    }

    private static void Save(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        var width = element.ActualWidth;
        var height = element.ActualHeight;

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>A neutral wallpaper: deep blue-teal gradient with a few soft glows.</summary>
    private static UIElement Wallpaper()
    {
        var grid = new Grid { Width = DesktopSize.Width, Height = DesktopSize.Height };
        grid.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x0E, 0x2A, 0x3E), Color.FromRgb(0x23, 0x1A, 0x40), 35),
        });

        foreach (var (x, y, size, color) in new[]
                 {
                     (1250.0, 620.0, 520.0, Color.FromArgb(0x90, 0x1F, 0x9E, 0xA8)),
                     (180.0, 760.0, 420.0, Color.FromArgb(0x80, 0x7A, 0x3F, 0xB8)),
                     (820.0, 120.0, 380.0, Color.FromArgb(0x60, 0xE0, 0x8A, 0x3C)),
                 })
        {
            var glow = new System.Windows.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                Effect = new BlurEffect { Radius = 160 },
                Margin = new Thickness(x - size / 2, y - size / 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            grid.Children.Add(glow);
        }

        return grid;
    }
}
