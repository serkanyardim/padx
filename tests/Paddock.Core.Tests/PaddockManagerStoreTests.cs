using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Covers <see cref="PaddockManager"/> against real files: pads hold their
/// items in place, so no operation may move a file off the desktop.
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
    public void AddIconToPaddock_LeavesTheFileWhereItIs()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");

        var entry = _manager.AddIconToPaddock(paddock.Id, file);

        Assert.NotNull(entry);
        Assert.Equal(file, entry.DesktopPath);
        Assert.True(File.Exists(file));
        Assert.False(Directory.Exists(_store));
        Assert.True(_manager.IsInAnyPad(file));
    }

    [Fact]
    public void AddIconToPaddock_TakesTheItemOutOfAnyOtherPad()
    {
        var first = _manager.CreatePaddock("A", 0, 0, 300, 250);
        var second = _manager.CreatePaddock("B", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");

        _manager.AddIconToPaddock(first.Id, file);
        _manager.AddIconToPaddock(second.Id, file);

        Assert.Empty(first.Icons);
        Assert.Single(second.Icons);
    }

    [Fact]
    public void RenameIcon_RenamesTheRealFileAndUpdatesTheEntry()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var entry = _manager.AddIconToPaddock(paddock.Id, MakeDesktopFile("old.lnk"))!;
        var oldPath = entry.DesktopPath;

        var error = _manager.RenameIcon(paddock.Id, oldPath, "new.lnk");

        Assert.Null(error);
        Assert.Equal(Path.Combine(_desktop, "new.lnk"), entry.DesktopPath);
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
    public void AddIconToPaddock_ReferencesFilesFromOutsideTheDesktop()
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
    }

    [Fact]
    public void AddIconToPaddock_IgnoresPathsThatNoLongerExist()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);

        Assert.Null(_manager.AddIconToPaddock(paddock.Id, Path.Combine(_desktop, "gone.lnk")));
        Assert.Empty(paddock.Icons);
    }

    [Fact]
    public void EjectIcon_KeepsTheFileOnTheDesktop()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");
        _manager.AddIconToPaddock(paddock.Id, file);

        Assert.Equal(file, _manager.EjectIcon(paddock.Id, file));
        Assert.Empty(paddock.Icons);
        Assert.True(File.Exists(file));
        Assert.False(_manager.IsInAnyPad(file));
    }

    [Fact]
    public void MoveIconBetweenPaddocks_MovesOnlyTheEntry()
    {
        var source = _manager.CreatePaddock("A", 0, 0, 300, 250);
        var target = _manager.CreatePaddock("B", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");
        _manager.AddIconToPaddock(source.Id, file);

        _manager.MoveIconBetweenPaddocks(source.Id, target.Id, file);

        Assert.Empty(source.Icons);
        Assert.Equal(file, Assert.Single(target.Icons).DesktopPath);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void DeletePaddock_LeavesItsItemsOnTheDesktop()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");
        _manager.AddIconToPaddock(paddock.Id, file);

        Assert.True(_manager.DeletePaddock(paddock.Id));
        Assert.True(File.Exists(file));
        Assert.False(_manager.IsInAnyPad(file));
    }

    [Fact]
    public void RepointIcon_FollowsARenameMadeOutsidePadx()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var file = MakeDesktopFile("old.txt");
        _manager.AddIconToPaddock(paddock.Id, file);
        var renamed = Path.Combine(_desktop, "new.txt");
        File.Move(file, renamed);

        Assert.True(_manager.RepointIcon(file, renamed));
        Assert.Equal(renamed, Assert.Single(paddock.Icons).DesktopPath);
        Assert.False(_manager.RepointIcon(file, renamed));
    }

    [Fact]
    public void ReconcilePaddocks_DropsEntriesWhoseFileIsGone()
    {
        var paddock = _manager.CreatePaddock("Apps", 0, 0, 300, 250);
        var file = MakeDesktopFile("app.lnk");
        _manager.AddIconToPaddock(paddock.Id, file);
        File.Delete(file);

        Assert.True(_manager.ReconcilePaddocks());
        Assert.Empty(paddock.Icons);
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
        Assert.True(File.Exists(Assert.Single(restored.Icons).DesktopPath));
    }
}
