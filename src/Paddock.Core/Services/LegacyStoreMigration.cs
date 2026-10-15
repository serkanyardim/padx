namespace Paddock.Core.Services;

/// <summary>
/// The one-time step from moved items to items in place: everything older
/// versions moved into Desktop\.Paddock goes back onto the desktop, and each
/// pad is pointed at the item's place there. Pads keep their contents.
/// </summary>
public static class LegacyStoreMigration
{
    /// <summary>
    /// Returns every item of the old store to the desktop and repoints the
    /// pads. <paramref name="systemShortcut"/> recognises the shortcuts padx
    /// generated for built-in icons (This PC, Recycle Bin…) and returns the
    /// path of their replacement, so those are not dumped on the desktop.
    /// An item that cannot be moved is left where it is — the pad still finds
    /// it there. Returns how many items went back to the desktop.
    /// </summary>
    public static int Run(PaddockManager manager, IconStore store,
        Func<string, string?>? systemShortcut = null, Func<string, string?>? systemShortcutByName = null)
    {
        var items = store.ListLegacyItems();
        if (items.Count == 0)
        {
            store.RemoveLegacyStoreIfEmpty();
            RepointMissing(manager, store, systemShortcutByName);
            return 0;
        }

        Log.Info($"Moving {items.Count} item(s) from the old pad store back to the desktop.");
        var returned = 0;

        using (manager.DeferSave())
        {
            foreach (var pad in manager.GetPaddocks())
            {
                foreach (var icon in pad.Icons.Where(i => store.IsInLegacyStore(i.DesktopPath)).ToList())
                {
                    if (systemShortcut?.Invoke(icon.DesktopPath) is { } replacement)
                    {
                        TryDelete(icon.DesktopPath);
                        icon.DesktopPath = replacement;
                        icon.Managed = false;
                    }
                    else if (store.ReturnToDesktop(icon.DesktopPath) is { } back)
                    {
                        icon.DesktopPath = back;
                        icon.Managed = true;
                        returned++;
                    }
                }
            }

            // Whatever no pad pointed at (left over from a crash, a deleted
            // pad…) is the user's too: back to the desktop as well.
            foreach (var leftover in store.ListLegacyItems())
            {
                if (systemShortcut?.Invoke(leftover) is not null)
                    TryDelete(leftover);
                else if (store.ReturnToDesktop(leftover) is not null)
                    returned++;
            }

            manager.SaveLayout();
        }

        store.RemoveLegacyStoreIfEmpty();
        RepointMissing(manager, store, systemShortcutByName);
        Log.Info($"{returned} item(s) are back on the desktop; pads point at them there.");
        return returned;
    }

    /// <summary>
    /// A pad still pointing into the old store at an item that is no longer
    /// there (it went back to the desktop in an earlier run, say) finds it on
    /// the desktop by name — or, for a built-in icon's shortcut, its new
    /// stand-in. Entries found nowhere are left for the normal clean-up.
    /// </summary>
    public static int RepointMissing(PaddockManager manager, IconStore store, Func<string, string?>? systemShortcutByName = null)
    {
        var repointed = 0;

        using (manager.DeferSave())
        {
            foreach (var pad in manager.GetPaddocks())
            {
                foreach (var icon in pad.Icons.Where(i => store.IsInLegacyStore(i.DesktopPath) && !IconStore.ItemExists(i.DesktopPath)).ToList())
                {
                    var name = Path.GetFileName(icon.DesktopPath);
                    var onDesktop = Path.Combine(store.DesktopRoot, name);
                    var found = systemShortcutByName?.Invoke(name) ?? (IconStore.ItemExists(onDesktop) ? onDesktop : null);

                    if (found is null || manager.IsInAnyPad(found))
                        continue;

                    icon.DesktopPath = found;
                    repointed++;
                }
            }

            if (repointed > 0)
                manager.SaveLayout();
        }

        if (repointed > 0)
            Log.Info($"{repointed} pad item(s) found on the desktop by name.");

        return repointed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete '{path}': {ex.Message}");
        }
    }
}
