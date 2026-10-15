using System.IO;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Knows where the desktop lives and what is on it, and watches it.
///
/// Pads hold desktop items in place — files never leave the Desktop folder —
/// so this service reports what is there and tells padx when something
/// appears, disappears or is renamed (by Explorer, an installer, a save…).
/// </summary>
public sealed class DesktopIconService : IDisposable
{
    /// <summary>The hidden folder older versions moved pad items into; emptied once on upgrade.</summary>
    public const string StoreFolderName = ".Paddock";

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    private readonly List<FileSystemWatcher> _watchers = new();
    private System.Timers.Timer? _debounce;
    private Action? _onChanged;
    private Action<string, string>? _onRenamed;

    public DesktopIconService()
    {
        DesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CommonDesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        StoreRoot = Path.Combine(DesktopRoot, StoreFolderName);
    }

    /// <summary>The current user's Desktop folder.</summary>
    public string DesktopRoot { get; }

    /// <summary>The all-users Desktop folder.</summary>
    public string CommonDesktopRoot { get; }

    /// <summary>The old pad store (Desktop\.Paddock).</summary>
    public string StoreRoot { get; }

    public IconStore CreateIconStore() => new(DesktopRoot, StoreRoot);

    /// <summary>
    /// Top-level items on the user's desktop — files and folders, minus shell
    /// bookkeeping files, hidden items and the old pad store.
    /// </summary>
    public IReadOnlyList<string> GetDesktopItems() => List(DesktopRoot);

    /// <summary>Items on the all-users desktop (shortcuts installed "for everyone", and the odd folder).</summary>
    public IReadOnlyList<string> GetCommonDesktopItems()
        => string.IsNullOrEmpty(CommonDesktopRoot) ? [] : List(CommonDesktopRoot);

    private IReadOnlyList<string> List(string folder)
    {
        var items = new List<string>();

        if (!Directory.Exists(folder))
            return items;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                if (!IsHiddenOrIgnored(entry))
                    items.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot list the desktop folder '{folder}': {ex.Message}");
        }

        return items;
    }

    /// <summary>True for an item in either Desktop folder (not in a subfolder).</summary>
    public bool IsDesktopItem(string path)
    {
        var parent = Path.GetDirectoryName(path);
        return parent is not null
               && (string.Equals(parent, DesktopRoot, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(parent, CommonDesktopRoot, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Watches both Desktop folders. <paramref name="onRenamed"/> gets every
    /// rename (old path, new path) straight away; <paramref name="onChanged"/>
    /// is raised once a burst of changes has settled. Both arrive on a
    /// background thread — marshal to the UI yourself.
    /// </summary>
    public void WatchDesktop(Action onChanged, Action<string, string> onRenamed)
    {
        _onChanged = onChanged;
        _onRenamed = onRenamed;

        _debounce = new System.Timers.Timer(350) { AutoReset = false };
        _debounce.Elapsed += (_, _) => _onChanged?.Invoke();

        foreach (var folder in new[] { DesktopRoot, CommonDesktopRoot }.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                };

                watcher.Created += OnEvent;
                watcher.Deleted += OnEvent;
                watcher.Changed += OnEvent;
                watcher.Renamed += OnRenamedEvent;
                watcher.Error += (_, e) => Log.Warn($"Desktop watcher: {e.GetException().Message}");
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Log.Error($"Cannot watch '{folder}' for changes.", ex);
            }
        }
    }

    /// <summary>
    /// Tells the shell the desktop's contents changed, so it redraws promptly.
    /// </summary>
    public void NotifyShellOfDesktopChange()
    {
        var pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(DesktopRoot);
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_UPDATEDIR,
                NativeMethods.SHCNF_PATHW,
                pathPtr,
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"SHChangeNotify failed: {ex.Message}");
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
                System.Runtime.InteropServices.Marshal.FreeHGlobal(pathPtr);
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _debounce?.Dispose();
        _debounce = null;
        _onChanged = null;
        _onRenamed = null;
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (IsNoise(e.Name))
            return;

        // Collapse bursts of events (a multi-file copy) into one refresh.
        _debounce?.Stop();
        _debounce?.Start();
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        if (!IsNoise(e.Name))
            _onRenamed?.Invoke(e.OldFullPath, e.FullPath);

        OnEvent(sender, e);
    }

    private static bool IsNoise(string? name)
        => name is null
           || IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase)
           || string.Equals(name, StoreFolderName, StringComparison.OrdinalIgnoreCase);

    private bool IsHiddenOrIgnored(string path)
    {
        var name = Path.GetFileName(path);

        if (IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return true;

        if (string.Equals(name, StoreFolderName, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot read attributes of '{path}': {ex.Message}");
            return true;
        }
    }
}
