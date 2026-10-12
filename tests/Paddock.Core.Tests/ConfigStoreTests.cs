using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"paddock_configs_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    [Fact]
    public void DefaultName_IsConfigDashDateDashTime()
        => Assert.Equal("config-20261013-1320", ConfigStore.DefaultName(new DateTime(2026, 10, 13, 13, 20, 45)));

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("work:layout", false)]
    [InlineData("office layout", true)]
    [InlineData("config-20261013-1320", true)]
    public void Validate_RejectsEmptyAndInvalidNames(string name, bool ok)
        => Assert.Equal(ok, ConfigStore.Validate(name) is null);

    [Fact]
    public void SaveThenLoad_RoundTripsSettingsPadsAndIcons()
    {
        var store = new ConfigStore(_folder);
        var settings = new AppSettings { Appearance = new PadAppearance { PadColor = "#123456", CornerRadius = 9 } };
        settings.Corrals["default"].Paddocks.Add(new PaddockModel { Id = "pad1", Title = "System", X = 12, Y = 34, Locked = true });

        store.Save(new SavedConfig
        {
            Name = "work",
            SavedAt = DateTime.Now,
            Settings = settings,
            Icons = new() { ["pad1"] = [new ConfigIcon("bin.lnk", true, @"C:\x\bin.lnk", "{645FF040-5081-101B-9F08-00AA002F954E}")] },
        });

        var loaded = store.Load("work")!;

        Assert.Equal("#123456", loaded.Settings.Appearance.PadColor);
        Assert.Equal(9, loaded.Settings.Appearance.CornerRadius);
        var pad = Assert.Single(loaded.Settings.Corrals["default"].Paddocks);
        Assert.Equal(("pad1", "System", 12.0, 34.0, true), (pad.Id, pad.Title, pad.X, pad.Y, pad.Locked));
        var icon = Assert.Single(loaded.Icons["pad1"]);
        Assert.Equal("{645FF040-5081-101B-9F08-00AA002F954E}", icon.SystemClsid);
    }

    [Fact]
    public void HiddenPads_StayHiddenThroughSaveAndLoad()
    {
        var store = new ConfigStore(_folder);
        var settings = new AppSettings();
        settings.Corrals["default"].Paddocks.Add(new PaddockModel { Id = "shown", Title = "Docs" });
        settings.Corrals["default"].Paddocks.Add(new PaddockModel { Id = "away", Title = "Games", Hidden = true });

        store.Save(new SavedConfig { Name = "with-hidden", Settings = settings });
        var loaded = store.Load("with-hidden")!.Settings.Corrals["default"].Paddocks;

        Assert.False(loaded.Single(p => p.Id == "shown").Hidden);
        Assert.True(loaded.Single(p => p.Id == "away").Hidden);

        // Loading rebuilds pads with RestorePaddock — it must carry the flag over.
        var manager = new PaddockManager(new SettingsService(Path.Combine(_folder, "settings.json")));
        var rebuilt = manager.RestorePaddock(loaded.Single(p => p.Id == "away"));
        Assert.True(rebuilt.Hidden);
    }

    [Fact]
    public void List_IsNewestFirst()
    {
        var store = new ConfigStore(_folder);
        store.Save(new SavedConfig { Name = "older" });
        File.SetLastWriteTime(Path.Combine(_folder, "older.json"), DateTime.Now.AddHours(-1));
        store.Save(new SavedConfig { Name = "newer" });

        Assert.Equal(["newer", "older"], store.List().Select(c => c.Name));
    }

    [Fact]
    public void Load_ReturnsNullForMissingOrCorruptConfigs()
    {
        var store = new ConfigStore(_folder);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "broken.json"), "{ not json");

        Assert.Null(store.Load("nope"));
        Assert.Null(store.Load("broken"));
    }
}
