namespace Paddock.Core.Services;

/// <summary>
/// Knows where desktop items live.
///
/// Pads hold their items <em>in place</em>: a pad only remembers paths, and the
/// files stay in the Desktop folder, so every path keeps working (shortcuts,
/// the Run box, links in documents) and Explorer shows the Desktop as it is.
/// padx hides Windows' own desktop icons and draws the desktop itself.
///
/// Before that, padx moved pad items into a hidden folder on the desktop
/// (<see cref="LegacyStoreRoot"/>, ".Paddock"). <see cref="ReturnToDesktop"/>
/// brings such items back, once, when upgrading.
///
/// Both roots are injected, so this type performs no Windows-specific calls and
/// is unit-testable against temp directories.
/// </summary>
public class IconStore
{
    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    public IconStore(string desktopRoot, string legacyStoreRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyStoreRoot);

        DesktopRoot = Normalize(desktopRoot);
        LegacyStoreRoot = Normalize(legacyStoreRoot);
    }

    /// <summary>The user's Desktop folder.</summary>
    public string DesktopRoot { get; }

    /// <summary>The hidden folder older versions moved pad items into (Desktop\.Paddock).</summary>
    public string LegacyStoreRoot { get; }

    /// <summary>True when the path is a direct child of the desktop folder.</summary>
    public bool IsOnDesktop(string path)
    {
        var parent = GetParentDirectory(path);
        return parent is not null && PathEquals(parent, DesktopRoot);
    }

    /// <summary>True when the path lives inside the old pad store.</summary>
    public bool IsInLegacyStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var full = Normalize(path);
        return full.StartsWith(LegacyStoreRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moves an item from the old pad store back onto the desktop, under a
    /// free name (" (2)" … when the name is taken). Returns its new path, or
    /// null when it could not be moved — it is then left where it was.
    /// </summary>
    public string? ReturnToDesktop(string path)
    {
        if (!ItemExists(path))
            return null;

        var source = Normalize(path);

        try
        {
            Directory.CreateDirectory(DesktopRoot);
            var target = GetAvailablePath(DesktopRoot, Path.GetFileName(source));
            MoveItem(source, target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not move '{source}' back to the desktop.", ex);
            return null;
        }
    }

    /// <summary>
    /// Everything still in the old store: the contents of each pad folder (not
    /// the pad folders themselves), and any stray file at its top.
    /// </summary>
    public IReadOnlyList<string> ListLegacyItems()
    {
        if (!Directory.Exists(LegacyStoreRoot))
            return [];

        try
        {
            return Directory.EnumerateDirectories(LegacyStoreRoot)
                .SelectMany(Directory.EnumerateFileSystemEntries)
                .Concat(Directory.EnumerateFiles(LegacyStoreRoot))
                .Where(p => !IsIgnored(p))
                .Select(Normalize)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not list the old pad store: {ex.Message}");
            return [];
        }
    }

    /// <summary>Deletes the old store once nothing of the user's is left in it.</summary>
    public void RemoveLegacyStoreIfEmpty()
    {
        if (!Directory.Exists(LegacyStoreRoot) || ListLegacyItems().Count > 0)
            return;

        try
        {
            // Hidden + system: clear the attributes so the delete is allowed everywhere.
            foreach (var entry in Directory.EnumerateFileSystemEntries(LegacyStoreRoot, "*", SearchOption.AllDirectories).Append(LegacyStoreRoot))
                File.SetAttributes(entry, FileAttributes.Normal | (Directory.Exists(entry) ? FileAttributes.Directory : 0));

            Directory.Delete(LegacyStoreRoot, recursive: true);
            Log.Info("The old pad store is empty and has been removed.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove the old pad store: {ex.Message}");
        }
    }

    /// <summary>True when the item still exists on disk.</summary>
    public static bool ItemExists(string path)
        => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    private static bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);
        return IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static void MoveItem(string source, string target)
    {
        if (Directory.Exists(source))
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }

    /// <summary>
    /// Returns a free path in <paramref name="folder"/> for the given file
    /// name, appending " (2)", " (3)" … rather than ever overwriting.
    /// </summary>
    private static string GetAvailablePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!ItemExists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!ItemExists(candidate))
                return candidate;
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static string? GetParentDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetDirectoryName(Normalize(path));
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot resolve the parent of '{path}': {ex.Message}");
            return null;
        }
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
