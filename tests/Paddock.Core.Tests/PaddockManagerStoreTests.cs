using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Covers <see cref="PaddockManager"/> when it is backed by a real
/// <see cref="IconStore"/> — the path that actually takes icons off the desktop.
/// </summary>
public class PaddockManagerStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly string _store;
    private readonly string _settingsPath;
    private readonly IconStore _iconStore;
    private readonly PaddockManager _manager;

    public PaddockManagerStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paddock_mgr_{Guid.NewGuid():N}");
        _desktop = Path.Combine(_root, "Desktop");
        _store = Path.Combine(_desktop, ".Paddock");
        Directory.CreateDirectory(_desktop);

        _settingsPath = Path.Combine(_root, "settings.json");
        _iconStore = new IconStore(_desktop, _store);
        _manager = new PaddockManager(new SettingsService(_settingsPath), _iconStore);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeDesktopFile(string name)
    {
        var path = Path.Combine(_desktop, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void AddIconToPaddock_MovesTheFileIntoThePaddockFolder()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");

        var entry = _manager.AddIconToPaddock(paddock.Id, file);

        Assert.NotNull(entry);
        Assert.True(entry.Managed);
        Assert.Equal(Path.Combine(_store, paddock.Id, "app.lnk"), entry.DesktopPath);
        Assert.True(File.Exists(entry.DesktopPath));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void RenameIcon_RenamesTheFileAndUpdatesTheEntry()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var entry = _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("old.lnk"))!;
        var oldPath = entry.DesktopPath;

        var error = _manager.RenameIcon(paddock.Id, oldPath, "new.lnk");

        Assert.Null(error);
        Assert.Equal(Path.Combine(_store, paddock.Id, "new.lnk"), entry.DesktopPath);
        Assert.True(File.Exists(entry.DesktopPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public void RenameIcon_RefusesToOverwriteOrUseInvalidNames()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var first = _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("a.txt"))!;
        _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("b.txt"));

        Assert.NotNull(_manager.RenameIcon(paddock.Id, first.DesktopPath, "b.txt"));
        Assert.NotNull(_manager.RenameIcon(paddock.Id, first.DesktopPath, "bad:name.txt"));
        Assert.True(File.Exists(first.DesktopPath));
    }

    [Fact]
    public void AddIconToPaddock_ReferencesFilesFromOutsideTheDesktopWithoutMoving()
    {
        var paddock = _manager.CreatePaddock("Docs", 0, 0, 300, 250);
        var elsewhere = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(elsewhere);
        var file = Path.Combine(elsewhere, "report.pdf");
        File.WriteAllText(file, "x");

        var entry = _manager.AddIconToPaddock(paddock.Id, file);

        Assert.NotNull(entry);
        Assert.False(entry.Managed);
        Assert.Equal(file, entry.DesktopPath);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void AddIconToPaddock_DoesNotAddTheSameStoredItemTwice()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var entry = _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("app.lnk"))!;

        var again = _manager.AddIconToPaddock(paddock.Id, entry.DesktopPath);

        Assert.Single(paddock.Icons);
        Assert.Same(entry, again);
    }

    [Fact]
    public void AddIconToPaddock_IgnoresPathsThatNoLongerExist()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);

        var entry = _manager.AddIconToPaddock(paddock.Id, Path.Combine(_desktop, "ghost.lnk"));

        Assert.Null(entry);
        Assert.Empty(paddock.Icons);
    }

    [Fact]
    public void EjectIcon_PutsTheFileBackOnTheDesktop()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var entry = _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("app.lnk"))!;

        var ejected = _manager.EjectIcon(paddock.Id, entry.DesktopPath);

        Assert.Equal(Path.Combine(_desktop, "app.lnk"), ejected);
        Assert.True(File.Exists(ejected));
        Assert.Empty(paddock.Icons);
    }

    [Fact]
    public void MoveIconBetweenPaddocks_MovesTheFileToo()
    {
        var source = _manager.CreatePaddock("Source", 0, 0, 300, 250);
        var target = _manager.CreatePaddock("Target", 400, 0, 300, 250);
        var entry = _manager.AddIconToPaddock(source.Id, MakeDesktopFile("app.lnk"))!;

        _manager.MoveIconBetweenPaddocks(source.Id, target.Id, entry.DesktopPath);

        Assert.Empty(source.Icons);
        Assert.Single(target.Icons);
        Assert.Equal(Path.Combine(_store, target.Id, "app.lnk"), target.Icons[0].DesktopPath);
        Assert.True(File.Exists(target.Icons[0].DesktopPath));
    }

    [Fact]
    public void DeletePaddock_ReturnsItsIconsToTheDesktop()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("a.lnk"));
        _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("b.lnk"));

        _manager.DeletePaddock(paddock.Id);

        Assert.True(File.Exists(Path.Combine(_desktop, "a.lnk")));
        Assert.True(File.Exists(Path.Combine(_desktop, "b.lnk")));
        Assert.False(Directory.Exists(Path.Combine(_store, paddock.Id)));
    }

    [Fact]
    public void ReconcilePaddocks_DropsEntriesWhoseFileIsGone()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var entry = _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("app.lnk"))!;
        File.Delete(entry.DesktopPath);

        var changed = _manager.ReconcilePaddocks();

        Assert.True(changed);
        Assert.Empty(paddock.Icons);
    }

    [Fact]
    public void ReconcilePaddocks_PicksUpFilesDroppedIntoThePaddockFolder()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var folder = _iconStore.EnsurePaddockFolder(paddock.Id);
        File.WriteAllText(Path.Combine(folder, "external.txt"), "x");

        var changed = _manager.ReconcilePaddocks();

        Assert.True(changed);
        Assert.Single(paddock.Icons);
        Assert.True(paddock.Icons[0].Managed);
        Assert.Equal(Path.Combine(folder, "external.txt"), paddock.Icons[0].DesktopPath);
    }

    [Fact]
    public void ReconcilePaddocks_ReportsNoChangeWhenDiskAndModelAgree()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("app.lnk"));

        Assert.False(_manager.ReconcilePaddocks());
    }

    [Fact]
    public void IconsSurviveARestart()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("app.lnk"));

        // Reload from the same settings file, as a fresh app start would.
        var reloaded = new PaddockManager(new SettingsService(_settingsPath), _iconStore);
        reloaded.ReconcilePaddocks();

        var restored = Assert.Single(reloaded.GetPaddocks());
        Assert.Equal("Apps", restored.Title);
        Assert.Single(restored.Icons);
        Assert.True(File.Exists(restored.Icons[0].DesktopPath));
    }
}
