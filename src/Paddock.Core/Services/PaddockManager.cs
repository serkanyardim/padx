using Paddock.Core.Models;

namespace Paddock.Core.Services;

public class PaddockManager
{
    private const int IconsPerRow = 4;

    private readonly SettingsService _settingsService;
    private readonly IconStore? _iconStore;

    public PaddockManager(SettingsService settingsService)
        : this(settingsService, null)
    {
    }

    /// <summary>
    /// Pads hold their items in place: the manager only keeps the lists of
    /// paths, it never moves a file. <paramref name="iconStore"/> (null in
    /// tests) knows where the desktop is.
    /// </summary>
    public PaddockManager(SettingsService settingsService, IconStore? iconStore)
    {
        _settingsService = settingsService;
        _iconStore = iconStore;
    }

    /// <summary>Where the desktop is, or <c>null</c> for in-memory use.</summary>
    public IconStore? Store => _iconStore;

    /// <summary>True when some pad (hidden ones included) holds this item.</summary>
    public bool IsInAnyPad(string path)
        => GetPaddocks().Any(p => FindEntry(p, path) is not null);

    public List<PaddockModel> GetPaddocks()
    {
        return _settingsService.GetActiveCorral().Paddocks;
    }

    public PaddockModel CreatePaddock(string title, double x, double y, double width, double height)
    {
        // No style overrides: a new pad follows the global appearance.
        var paddock = new PaddockModel
        {
            Title = title,
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };

        _settingsService.GetActiveCorral().Paddocks.Add(paddock);
        SaveLayout();

        return paddock;
    }

    /// <summary>
    /// Re-adds a previously removed pad with its original id, geometry and
    /// settings but no icons — the caller puts its items back. Used to undo an
    /// Auto Organize and to load a config.
    /// </summary>
    public PaddockModel RestorePaddock(PaddockModel snapshot)
    {
        var paddock = new PaddockModel
        {
            Id = snapshot.Id,
            Title = snapshot.Title,
            TitleVisibility = snapshot.TitleVisibility,
            X = snapshot.X,
            Y = snapshot.Y,
            Width = snapshot.Width,
            Height = snapshot.Height,
            Style = new PaddockStyle { Color = snapshot.Style.Color },
            Locked = snapshot.Locked,
            IsRolledUp = snapshot.IsRolledUp,
            ExcludeFromQuickHide = snapshot.ExcludeFromQuickHide,
            Hidden = snapshot.Hidden,
            SortBy = snapshot.SortBy,
            SortAscending = snapshot.SortAscending,
        };

        GetPaddocks().Add(paddock);
        SaveLayout();
        return paddock;
    }

    public PaddockModel? GetPaddock(string id)
    {
        return GetPaddocks().FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Deletes a paddock. Its items never left the desktop, so they simply
    /// show on it again.
    /// </summary>
    public bool DeletePaddock(string id)
    {
        var paddocks = GetPaddocks();
        var paddock = paddocks.FirstOrDefault(p => p.Id == id);
        if (paddock is null)
            return false;

        paddocks.Remove(paddock);
        SaveLayout();
        return true;
    }

    /// <summary>
    /// Adds an item to a paddock — the item stays where it is; the pad only
    /// remembers its path. An item lives in one pad at a time, so it leaves any
    /// other pad holding it. Returns the new entry, or the existing one when
    /// the item was already in this paddock.
    /// </summary>
    public IconEntry? AddIconToPaddock(string paddockId, string path)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null || string.IsNullOrWhiteSpace(path))
            return null;

        var existing = FindEntry(paddock, path);
        if (existing is not null)
            return existing;

        // A stale path (deleted behind our back) would be a phantom icon.
        if (_iconStore is not null && !IconStore.ItemExists(path))
            return null;

        foreach (var other in GetPaddocks().Where(p => p != paddock))
        {
            if (FindEntry(other, path) is { } elsewhere)
                other.Icons.Remove(elsewhere);
        }

        var entry = new IconEntry
        {
            DesktopPath = path,
            Managed = _iconStore?.IsOnDesktop(path) == true,
            GridPosition = GetNextGridPosition(paddock)
        };

        paddock.Icons.Add(entry);
        SaveLayout();
        return entry;
    }

    /// <summary>Forgets an icon without touching the file on disk.</summary>
    public void RemoveIconFromPaddock(string paddockId, string path)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return;

        var icon = FindEntry(paddock, path);
        if (icon is not null)
        {
            paddock.Icons.Remove(icon);
            SaveLayout();
        }
    }

    /// <summary>
    /// Takes an item out of a paddock. A desktop item shows on the desktop
    /// again (it never left it). Returns its path, or <c>null</c> when the pad
    /// did not hold it.
    /// </summary>
    public string? EjectIcon(string paddockId, string path)
    {
        var paddock = GetPaddock(paddockId);
        var icon = paddock is null ? null : FindEntry(paddock, path);
        if (icon is null)
            return null;

        paddock!.Icons.Remove(icon);
        SaveLayout();
        return icon.DesktopPath;
    }

    /// <summary>
    /// An item was renamed or moved outside padx (Explorer, another program):
    /// every pad holding it follows. Returns true when a pad held it.
    /// </summary>
    public bool RepointIcon(string oldPath, string newPath)
    {
        var changed = false;

        foreach (var paddock in GetPaddocks())
        {
            if (FindEntry(paddock, oldPath) is { } icon)
            {
                icon.DesktopPath = newPath;
                changed = true;
            }
        }

        if (changed)
            SaveLayout();

        return changed;
    }

    /// <summary>
    /// Renames the item behind an icon in place and updates its entry.
    /// Returns an error message, or null on success.
    /// </summary>
    public string? RenameIcon(string paddockId, string path, string newFileName)
    {
        var paddock = GetPaddock(paddockId);
        var icon = paddock is null ? null : FindEntry(paddock, path);
        if (icon is null)
            return "That icon is no longer in this pad.";

        newFileName = newFileName.Trim();
        if (newFileName.Length == 0 || newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A name can't be empty or contain any of these: \\ / : * ? \" < > |";

        var folder = Path.GetDirectoryName(icon.DesktopPath);
        if (folder is null)
            return "That item can't be renamed.";

        var target = Path.Combine(folder, newFileName);
        if (string.Equals(target, icon.DesktopPath, StringComparison.Ordinal))
            return null;

        // A change of case only is fine; anything else must not overwrite.
        if (!string.Equals(target, icon.DesktopPath, StringComparison.OrdinalIgnoreCase) && IconStore.ItemExists(target))
            return $"There is already an item called \"{newFileName}\".";

        try
        {
            if (Directory.Exists(icon.DesktopPath))
                Directory.Move(icon.DesktopPath, target);
            else
                File.Move(icon.DesktopPath, target);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not rename '{icon.DesktopPath}' to '{newFileName}'.", ex);
            return $"Windows would not rename it: {ex.Message}";
        }

        icon.DesktopPath = target;
        SaveLayout();
        return null;
    }

    public void MoveIconBetweenPaddocks(string sourcePaddockId, string targetPaddockId, string path)
    {
        var source = GetPaddock(sourcePaddockId);
        var target = GetPaddock(targetPaddockId);
        if (source is null || target is null || source.Id == target.Id)
            return;

        var icon = FindEntry(source, path);
        if (icon is null)
            return;

        // Prevent duplicates in target
        if (FindEntry(target, icon.DesktopPath) is not null)
            return;

        source.Icons.Remove(icon);
        icon.GridPosition = GetNextGridPosition(target);
        target.Icons.Add(icon);

        SaveLayout();
    }

    /// <summary>
    /// Brings every paddock back in line with what is on disk: drops entries
    /// whose item is gone (deleted, or moved away while padx was not looking).
    /// No-op when the manager has no <see cref="IconStore"/>.
    /// </summary>
    public bool ReconcilePaddocks()
    {
        if (_iconStore is null)
            return false;

        var changed = false;

        foreach (var paddock in GetPaddocks())
        {
            var touched = paddock.Icons.RemoveAll(i => !IconStore.ItemExists(i.DesktopPath)) > 0;
            if (!touched)
                continue;

            ReassignGridPositions(paddock);
            changed = true;
        }

        if (changed)
            SaveLayout();

        return changed;
    }

    /// <summary>
    /// Sort icons within a paddock by the specified field and direction.
    /// </summary>
    public void SortPaddockIcons(string paddockId, SortField sortBy, bool ascending)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return;

        paddock.SortBy = sortBy;
        paddock.SortAscending = ascending;

        paddock.Icons = GetSortedIcons(paddock);
        ReassignGridPositions(paddock);
        SaveLayout();
    }

    /// <summary>
    /// Get the sorted icons list for a paddock without modifying the model.
    /// </summary>
    public static List<IconEntry> GetSortedIcons(PaddockModel paddock)
    {
        return paddock.SortBy switch
        {
            SortField.Name => paddock.SortAscending
                ? paddock.Icons.OrderBy(i => Path.GetFileNameWithoutExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList()
                : paddock.Icons.OrderByDescending(i => Path.GetFileNameWithoutExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList(),

            SortField.FileType => paddock.SortAscending
                ? paddock.Icons.OrderBy(i => Path.GetExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList()
                : paddock.Icons.OrderByDescending(i => Path.GetExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList(),

            SortField.DateModified => SortByFileDate(paddock.Icons, paddock.SortAscending),
            SortField.FileSize => SortByFileSize(paddock.Icons, paddock.SortAscending),
            _ => paddock.Icons.ToList()
        };
    }

    public void SaveLayout()
    {
        _settingsService.Save();
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Raised whenever pads change — contents, places, sizes. The desktop
    /// listens: what is in no pad shows on it, around the pads.
    /// </summary>
    public event Action? LayoutChanged;

    /// <summary>
    /// Batches the settings writes of a bulk operation into a single write.
    /// Dispose the returned scope to flush.
    /// </summary>
    public IDisposable DeferSave() => _settingsService.DeferSave();

    private static IconEntry? FindEntry(PaddockModel paddock, string path)
        => paddock.Icons.FirstOrDefault(i =>
            string.Equals(i.DesktopPath, path, StringComparison.OrdinalIgnoreCase));

    private static List<IconEntry> SortByFileDate(List<IconEntry> icons, bool ascending)
    {
        return ascending
            ? icons.OrderBy(i => GetFileDate(i.DesktopPath)).ToList()
            : icons.OrderByDescending(i => GetFileDate(i.DesktopPath)).ToList();
    }

    private static List<IconEntry> SortByFileSize(List<IconEntry> icons, bool ascending)
    {
        return ascending
            ? icons.OrderBy(i => GetFileSize(i.DesktopPath)).ToList()
            : icons.OrderByDescending(i => GetFileSize(i.DesktopPath)).ToList();
    }

    private static DateTime GetFileDate(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch { return DateTime.MinValue; }
    }

    private static long GetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static void ReassignGridPositions(PaddockModel paddock)
    {
        for (var i = 0; i < paddock.Icons.Count; i++)
        {
            paddock.Icons[i].GridPosition = new GridPosition
            {
                Row = i / IconsPerRow,
                Col = i % IconsPerRow
            };
        }
    }

    private static GridPosition GetNextGridPosition(PaddockModel paddock)
    {
        var count = paddock.Icons.Count;
        return new GridPosition
        {
            Row = count / IconsPerRow,
            Col = count % IconsPerRow
        };
    }
}
