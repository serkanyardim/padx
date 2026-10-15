using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// The desktop locations, and the one-time return of items from the old
/// Desktop\.Paddock store (<see cref="LegacyStoreMigration"/>).
/// </summary>
public class IconStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly string _store;
    private readonly IconStore _iconStore;

    public IconStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_store_{Guid.NewGuid():N}");
        _desktop = Path.Combine(_root, "Desktop");
        _store = Path.Combine(_desktop, ".Paddock");
        Directory.CreateDirectory(_desktop);
        _iconStore = new IconStore(_desktop, _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>A file the old version had moved into a pad's folder.</summary>
    private string MakeStoredFile(string padId, string name)
    {
        var folder = Path.Combine(_store, padId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        return path;
    }

    [Fact]
    public void IsOnDesktop_OnlyCountsDirectChildrenOfTheDesktop()
    {
        Assert.True(_iconStore.IsOnDesktop(Path.Combine(_desktop, "a.txt")));
        Assert.False(_iconStore.IsOnDesktop(Path.Combine(_desktop, "sub", "a.txt")));
        Assert.False(_iconStore.IsOnDesktop(Path.Combine(_root, "a.txt")));
    }

    [Fact]
    public void IsInLegacyStore_DetectsItemsUnderTheOldStore()
    {
        Assert.True(_iconStore.IsInLegacyStore(Path.Combine(_store, "pad1", "a.txt")));
        Assert.False(_iconStore.IsInLegacyStore(Path.Combine(_desktop, "a.txt")));
        Assert.False(_iconStore.IsInLegacyStore(_store));
    }

    [Fact]
    public void ReturnToDesktop_NeverOverwritesAnItemWithTheSameName()
    {
        File.WriteAllText(Path.Combine(_desktop, "a.txt"), "already here");
        var stored = MakeStoredFile("pad1", "a.txt");

        var back = _iconStore.ReturnToDesktop(stored);

        Assert.Equal(Path.Combine(_desktop, "a (2).txt"), back);
        Assert.Equal("already here", File.ReadAllText(Path.Combine(_desktop, "a.txt")));
        Assert.False(File.Exists(stored));
    }

    [Fact]
    public void ListLegacyItems_ListsPadContentsAndIgnoresShellMetadata()
    {
        MakeStoredFile("pad1", "a.txt");
        MakeStoredFile("pad2", "desktop.ini");
        Directory.CreateDirectory(Path.Combine(_store, "pad2", "Folder"));

        var items = _iconStore.ListLegacyItems().Select(Path.GetFileName).ToList();

        Assert.Equal(["a.txt", "Folder"], items);
    }

    [Fact]
    public void Migration_ReturnsItemsAndRepointsThePads()
    {
        var manager = new PaddockManager(new SettingsService(Path.Combine(_root, "settings.json")), _iconStore);
        var pad = manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var stored = MakeStoredFile(pad.Id, "app.lnk");
        pad.Icons.Add(new IconEntry { DesktopPath = stored, Managed = true });
        MakeStoredFile("old-pad", "orphan.txt");

        var returned = LegacyStoreMigration.Run(manager, _iconStore);

        Assert.Equal(2, returned);
        Assert.Equal(Path.Combine(_desktop, "app.lnk"), Assert.Single(pad.Icons).DesktopPath);
        Assert.True(File.Exists(Path.Combine(_desktop, "app.lnk")));
        Assert.True(File.Exists(Path.Combine(_desktop, "orphan.txt")));
        Assert.False(Directory.Exists(_store));
    }

    [Fact]
    public void Migration_ReplacesGeneratedShortcutsForBuiltInIcons()
    {
        var manager = new PaddockManager(new SettingsService(Path.Combine(_root, "settings.json")), _iconStore);
        var pad = manager.CreatePaddock("System", 0, 0, 300, 250);
        var generated = MakeStoredFile(pad.Id, "This PC.lnk");
        pad.Icons.Add(new IconEntry { DesktopPath = generated, Managed = true });
        var replacement = Path.Combine(_root, "system", "This PC.lnk");

        LegacyStoreMigration.Run(manager, _iconStore, p => p.EndsWith("This PC.lnk") ? replacement : null);

        Assert.Equal(replacement, Assert.Single(pad.Icons).DesktopPath);
        Assert.False(File.Exists(Path.Combine(_desktop, "This PC.lnk")));
        Assert.False(File.Exists(generated));
    }

    [Fact]
    public void Migration_FindsItemsAlreadyBackOnTheDesktopByName()
    {
        // An earlier run put the file back but lost track of the pad.
        var manager = new PaddockManager(new SettingsService(Path.Combine(_root, "settings.json")), _iconStore);
        var pad = manager.CreatePaddock("Docs", 0, 0, 300, 250);
        pad.Icons.Add(new IconEntry { DesktopPath = Path.Combine(_store, pad.Id, "report.docx"), Managed = true });
        pad.Icons.Add(new IconEntry { DesktopPath = Path.Combine(_store, pad.Id, "gone.txt"), Managed = true });
        File.WriteAllText(Path.Combine(_desktop, "report.docx"), "x");

        LegacyStoreMigration.Run(manager, _iconStore);

        Assert.Equal(Path.Combine(_desktop, "report.docx"), pad.Icons[0].DesktopPath);
        Assert.StartsWith(_store, pad.Icons[1].DesktopPath);
    }

    [Fact]
    public void OldDataFolder_IsMergedIntoANewFolderThatHasNoSettingsYet()
    {
        var old = Path.Combine(_root, "Roaming", "Paddock");
        var fresh = Path.Combine(_root, "Local", "padx");
        Directory.CreateDirectory(Path.Combine(old, "configs"));
        File.WriteAllText(Path.Combine(old, "settings.json"), "old settings");
        File.WriteAllText(Path.Combine(old, "configs", "home.json"), "{}");
        Directory.CreateDirectory(fresh);
        File.WriteAllText(Path.Combine(fresh, "paddock.log"), "new log");

        AppPaths.MoveOldDataFolder(old, fresh);

        Assert.Equal("old settings", File.ReadAllText(Path.Combine(fresh, "settings.json")));
        Assert.True(File.Exists(Path.Combine(fresh, "configs", "home.json")));
        Assert.Equal("new log", File.ReadAllText(Path.Combine(fresh, "paddock.log")));
    }

    [Fact]
    public void Migration_DoesNothingWithoutAnOldStore()
    {
        var manager = new PaddockManager(new SettingsService(Path.Combine(_root, "settings.json")), _iconStore);

        Assert.Equal(0, LegacyStoreMigration.Run(manager, _iconStore));
    }
}
