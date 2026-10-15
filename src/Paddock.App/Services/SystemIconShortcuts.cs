using System.IO;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// The built-in desktop icons (This PC, your user folder, Network, Control
/// Panel, Recycle Bin) are no files, so padx stands each in with a shortcut,
/// kept in its own data folder. With padx drawing the desktop, they show — and
/// go into pads — like any other item. Which ones show follows Windows' own
/// setting (Personalise ▸ Themes ▸ Desktop icon settings).
/// </summary>
internal static class SystemIconShortcuts
{
    public static string Folder => Path.Combine(AppPaths.DataFolder, "system");

    /// <summary>The shortcut file standing in for a built-in icon (not created here).</summary>
    public static string PathFor(SystemDesktopIcons.Definition icon)
    {
        var name = string.Concat(SystemDesktopIcons.DisplayName(icon).Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(Folder, name + ".lnk");
    }

    /// <summary>The shortcut for a built-in icon, written if it is not there yet; null if that fails.</summary>
    public static string? Ensure(SystemDesktopIcons.Definition icon)
    {
        var path = PathFor(icon);
        if (File.Exists(path))
            return path;

        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not create '{Folder}': {ex.Message}");
            return null;
        }

        return SystemDesktopIcons.CreateShortcut(icon, path) ? path : null;
    }

    /// <summary>Shortcuts for the built-in icons Windows would show, in desktop order (Recycle Bin last).</summary>
    public static IReadOnlyList<string> Visible()
        => SystemDesktopIcons.Visible().Select(Ensure).OfType<string>().ToList();

    /// <summary>
    /// For the upgrade: the stand-in for a built-in icon whose old generated
    /// shortcut had this file name ("This PC.lnk"), or null.
    /// </summary>
    public static string? ByName(string fileName)
        => SystemDesktopIcons.All
            .FirstOrDefault(icon => string.Equals(Path.GetFileName(PathFor(icon)), fileName, StringComparison.OrdinalIgnoreCase))
            is { } match ? Ensure(match) : null;

    /// <summary>
    /// For the upgrade: when <paramref name="path"/> is one of the shortcuts the
    /// old version generated, the built-in icon is switched back on in Windows
    /// (the old version hid it) and the path of its new stand-in is returned.
    /// </summary>
    public static string? ReplaceGenerated(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            || SystemDesktopIcons.FromTarget(ShortcutReader.Describe(path).Target) is not { } icon)
        {
            return null;
        }

        SystemDesktopIcons.Show(icon);
        return Ensure(icon);
    }
}
