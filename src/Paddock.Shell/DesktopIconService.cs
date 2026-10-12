using System.IO;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Knows where the desktop lives and where paddocks keep their items.
///
/// Paddock stores the items it holds in a hidden folder on the desktop itself
/// (<c>%USERPROFILE%\Desktop\.Paddock\&lt;paddock-id&gt;</c>). Moving a file in
/// there is what makes its icon leave the desktop, because the shell only draws
/// the top-level entries of the Desktop folder. Keeping the folder on the
/// desktop — rather than off in AppData — means the user's files never travel
/// far, and stay recoverable even without the app.
/// </summary>
public sealed class DesktopIconService : IDisposable
{
    /// <summary>Name of the hidden folder holding one subfolder per paddock.</summary>
    public const string StoreFolderName = ".Paddock";

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private Action? _onStoreChanged;

    public DesktopIconService()
    {
        DesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CommonDesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        StoreRoot = Path.Combine(DesktopRoot, StoreFolderName);
    }

    /// <summary>The current user's Desktop folder.</summary>
    public string DesktopRoot { get; }

    /// <summary>The all-users Desktop folder (read-only for our purposes).</summary>
    public string CommonDesktopRoot { get; }

    /// <summary>Parent of the per-paddock folders.</summary>
    public string StoreRoot { get; }

    /// <summary>
    /// Creates the store folder (hidden, so it does not itself appear as a
    /// desktop icon) and returns a store bound to it.
    /// </summary>
    public IconStore CreateIconStore()
    {
        EnsureHiddenStoreRoot();
        return new IconStore(DesktopRoot, StoreRoot);
    }

    /// <summary>
    /// Movable top-level items on the desktop — files and folders, minus shell
    /// bookkeeping files and Paddock's own store folder.
    ///
    /// Items on the all-users desktop are deliberately left out: moving them
    /// would change what every account on the machine sees (and usually needs
    /// admin rights), so they stay where they are. They can still be dragged
    /// into a paddock by hand, where they are referenced rather than moved.
    /// </summary>
    public IReadOnlyList<string> GetDesktopItems()
    {
        var items = new List<string>();

        if (!Directory.Exists(DesktopRoot))
            return items;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(DesktopRoot))
            {
                if (IsHiddenOrIgnored(entry))
                    continue;

                items.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot list the desktop folder '{DesktopRoot}': {ex.Message}");
        }

        return items;
    }

    /// <summary>Folder in the store where all-users desktop items wait between elevated moves.</summary>
    public const string PublicStagingFolder = "_public";

    /// <summary>
    /// Files on the all-users desktop (shortcuts installed "for everyone").
    /// Moving these needs administrator rights — see <see cref="ElevatedMover"/>.
    /// </summary>
    public IReadOnlyList<string> GetCommonDesktopItems()
    {
        var items = new List<string>();

        if (string.IsNullOrEmpty(CommonDesktopRoot) || !Directory.Exists(CommonDesktopRoot))
            return items;

        try
        {
            foreach (var entry in Directory.EnumerateFiles(CommonDesktopRoot))
            {
                if (!IsHiddenOrIgnored(entry))
                    items.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot list the all-users desktop '{CommonDesktopRoot}': {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Watches the store for changes made outside the app (files dropped into a
    /// paddock folder from Explorer, items deleted, and so on). The callback is
    /// debounced and raised on a background thread — marshal to the UI yourself.
    /// </summary>
    public void WatchStore(Action onChanged)
    {
        _onStoreChanged = onChanged;

        try
        {
            EnsureHiddenStoreRoot();

            _debounce = new System.Timers.Timer(400) { AutoReset = false };
            _debounce.Elapsed += (_, _) => _onStoreChanged?.Invoke();

            _watcher = new FileSystemWatcher(StoreRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnStoreEvent;
            _watcher.Deleted += OnStoreEvent;
            _watcher.Renamed += OnStoreEvent;
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot watch '{StoreRoot}' for changes.", ex);
        }
    }

    /// <summary>
    /// Tells the shell a folder's contents changed, so the desktop redraws
    /// promptly after items are moved in or out.
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
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnStoreEvent;
            _watcher.Deleted -= OnStoreEvent;
            _watcher.Renamed -= OnStoreEvent;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounce?.Dispose();
        _debounce = null;
        _onStoreChanged = null;
    }

    private void OnStoreEvent(object sender, FileSystemEventArgs e)
    {
        // Collapse bursts of events (a multi-file move) into one refresh.
        if (_debounce is null)
            return;

        _debounce.Stop();
        _debounce.Start();
    }

    private void EnsureHiddenStoreRoot()
    {
        try
        {
            // Hidden + System: Explorer treats it as a protected OS item, so it
            // stays out of sight even when "show hidden items" is switched on.
            var info = Directory.CreateDirectory(StoreRoot);
            const FileAttributes wanted = FileAttributes.Hidden | FileAttributes.System;
            if ((info.Attributes & wanted) != wanted)
                info.Attributes |= wanted;
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot prepare the paddock store at '{StoreRoot}'.", ex);
        }
    }

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
