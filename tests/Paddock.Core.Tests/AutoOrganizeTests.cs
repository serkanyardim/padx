using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class IconCategorizerTests
{
    private static AutoCategory Shortcut(string name, string? target, bool targetIsFolder = false)
        => IconCategorizer.Categorize(new DesktopItemInfo($@"C:\Users\u\Desktop\{name}.lnk", false, target, targetIsFolder));

    private static AutoCategory File(string fileName)
        => IconCategorizer.Categorize(new DesktopItemInfo($@"C:\Users\u\Desktop\{fileName}", false));

    [Theory]
    [InlineData("holiday.jpg", AutoCategory.Media)]
    [InlineData("clip.mp4", AutoCategory.Media)]
    [InlineData("report.pdf", AutoCategory.Documents)]
    [InlineData("budget.xlsx", AutoCategory.Documents)]
    [InlineData("backup.zip", AutoCategory.Archives)]
    [InlineData("site-plan.dwg", AutoCategory.Design)]
    [InlineData("original.bin", AutoCategory.Other)]
    public void Files_GoByExtension(string fileName, AutoCategory expected)
        => Assert.Equal(expected, File(fileName));

    [Fact]
    public void Folder_IsFolders()
        => Assert.Equal(AutoCategory.Folders,
            IconCategorizer.Categorize(new DesktopItemInfo(@"C:\Users\u\Desktop\Projects", true)));

    [Fact]
    public void ShortcutToFolder_IsFolders()
        => Assert.Equal(AutoCategory.Folders, Shortcut("Projects", @"D:\Work\Projects", targetIsFolder: true));

    [Fact]
    public void Shortcut_IsJudgedByTargetExe_NotItsName()
        => Assert.Equal(AutoCategory.Browsers,
            Shortcut("Work", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));

    [Theory]
    [InlineData("putty", @"C:\Program Files\PuTTY\putty.exe", AutoCategory.Remote)]
    [InlineData("DWGSeePro2027", @"C:\Program Files\AutoDWG\DWGSee Pro 2027\dwgsee.exe", AutoCategory.Design)]
    [InlineData("Visual Studio Code", @"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe", AutoCategory.Development)]
    [InlineData("Microsoft Teams", @"C:\Program Files\WindowsApps\MSTeams_1\ms-teams.exe", AutoCategory.Communication)]
    [InlineData("Steam", @"C:\Program Files (x86)\Steam\steam.exe", AutoCategory.Games)]
    [InlineData("VLC media player", @"C:\Program Files\VideoLAN\VLC\vlc.exe", AutoCategory.Media)]
    [InlineData("7-Zip File Manager", @"C:\Program Files\7-Zip\7zFM.exe", AutoCategory.Utilities)]
    public void KnownApps_LandInTheirCategory(string name, string target, AutoCategory expected)
        => Assert.Equal(expected, Shortcut(name, target));

    [Fact]
    public void ShortcutWithoutReadableTarget_FallsBackToItsName()
        => Assert.Equal(AutoCategory.Office, Shortcut("Excel", null));

    [Fact]
    public void ShortcutToDocument_IsDocuments()
        => Assert.Equal(AutoCategory.Documents, Shortcut("Q3 report", @"D:\Reports\q3.pdf"));

    [Theory]
    [InlineData("DWGSeePro2027.msi")]
    [InlineData("Autodesk_DWG_TrueView_2027_setup_webinstall.exe")]
    public void Installers_GetTheirOwnPad(string fileName)
        => Assert.Equal(AutoCategory.Installers, File(fileName));

    [Fact]
    public void CodeFiles_AreDevTools()
        => Assert.Equal(AutoCategory.Development, File("mailreportozet.py"));

    [Fact]
    public void ShortcutToUncheckableFolder_IsFolders()
        => Assert.Equal(AutoCategory.Folders, Shortcut("UT Şablonları", @"M:\dys\04. Şablonlar\UT Şablonları"));

    [Fact]
    public void ShortcutToShellObject_IsSystem_WhateverItsName()
        => Assert.Equal(AutoCategory.System, Shortcut("bin", "::{645FF040-5081-101B-9F08-00AA002F954E}"));

    [Fact]
    public void UnknownProgramInAGameEngineFolder_IsGames()
        => Assert.Equal(AutoCategory.Games, IconCategorizer.Categorize(
            new DesktopItemInfo(@"C:\Users\u\Desktop\Last Epoch.lnk", false, @"D:\fun\Last Epoch\Last Epoch.exe", LooksLikeGame: true)));

    [Fact]
    public void ProgramDetails_DecideWhenNameAndPathSayNothing()
        => Assert.Equal(AutoCategory.Utilities, IconCategorizer.Categorize(
            new DesktopItemInfo(@"C:\Users\u\Desktop\LRS.lnk", false, @"C:\Program Files\X\winpe\ifsui.exe",
                Details: "Lazesoft Recovery Suite | Lazesoft")));

    [Theory]
    [InlineData("Google Play Games", @"C:\Program Files\Google\Play Games\Bootstrapper.exe", AutoCategory.Games)]
    [InlineData("sVB IDE", @"C:\Windows\Installer\{004F0DE1}\_7F93.exe", AutoCategory.Development)]
    [InlineData("Arduino IDE", @"C:\Program Files\Arduino IDE\Arduino IDE.exe", AutoCategory.Development)]
    [InlineData("VMware Workstation Pro", @"C:\Program Files (x86)\VMware\VMware Workstation\vmware.exe", AutoCategory.Remote)]
    [InlineData("PDQ Inventory", @"C:\Program Files (x86)\Admin Arsenal\PDQ Inventory\PDQInventoryConsole.exe", AutoCategory.Remote)]
    [InlineData("QCAD", @"C:\Program Files\QCAD\qcad.exe", AutoCategory.Design)]
    [InlineData("Google Earth Pro", @"C:\Program Files\Google\Google Earth Pro\client\googleearth.exe", AutoCategory.Design)]
    [InlineData("RevPDF", @"C:\Program Files\RevPDF\RevPDF.exe", AutoCategory.Office)]
    [InlineData("Xtreme Download Manager", @"C:\Program Files (x86)\XDM\xdm-app.exe", AutoCategory.Browsers)]
    [InlineData("MiniTool Partition Wizard", @"C:\Program Files\MiniTool Partition Wizard 13\partitionwizard.exe", AutoCategory.Utilities)]
    public void FormerlyUncategorisedApps_NowFindTheirPad(string name, string target, AutoCategory expected)
        => Assert.Equal(expected, Shortcut(name, target));

    [Fact]
    public void SystemCategory_SortsFirst()
    {
        Assert.Equal(AutoCategory.System, Enum.GetValues<AutoCategory>().Min());
        Assert.Equal("System", IconCategorizer.Title(AutoCategory.System));
    }

    [Fact]
    public void UnknownApp_IsApps()
        => Assert.Equal(AutoCategory.Apps, Shortcut("Acme Tool", @"C:\Program Files\Acme\acmetool.exe"));

    [Fact]
    public void ShortWords_MustMatchWholeWords()
        => Assert.Equal(AutoCategory.Apps, Shortcut("Barcode Reader", @"C:\Tools\barcodereader.exe"));

    [Fact]
    public void UrlShortcut_IsLinks_ButSteamUrlIsGames()
    {
        Assert.Equal(AutoCategory.Links,
            IconCategorizer.Categorize(new DesktopItemInfo(@"C:\Users\u\Desktop\Wiki.url", false, "https://wiki.example.com")));
        Assert.Equal(AutoCategory.Games,
            IconCategorizer.Categorize(new DesktopItemInfo(@"C:\Users\u\Desktop\Portal 2.url", false, "steam://rungameid/620")));
    }
}

public class AutoLayoutPlannerTests
{
    private static readonly PadMetrics Metrics = new(CellWidth: 76, CellHeight: 84, ChromeWidth: 14, ChromeHeight: 46, MinWidth: 140);
    private static readonly LayoutBounds Screen = LayoutBounds.FromSize(1920, 1040);

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(6, 6, 1)]
    [InlineData(7, 6, 2)]
    [InlineData(13, 6, 3)]
    public void Measure_WrapsAtMaxPerRow(int count, int columns, int rows)
    {
        var size = AutoLayoutPlanner.Measure(count, 6, Metrics);

        Assert.Equal(columns, size.Columns);
        Assert.Equal(rows, size.Rows);
        Assert.Equal(rows * 84 + 46, size.Height);
    }

    [Fact]
    public void Measure_FitsIconsExactly_ButNeverBelowMinWidth()
    {
        Assert.Equal(6 * 76 + 14, AutoLayoutPlanner.Measure(6, 6, Metrics).Width);
        Assert.Equal(140, AutoLayoutPlanner.Measure(1, 6, Metrics).Width);
    }

    [Fact]
    public void Place_FlowsDownTheFirstColumnFirst()
    {
        var rects = AutoLayoutPlanner.Place([(200, 300), (200, 300)], Screen, [], 12, AutoStartCorner.TopLeft);

        Assert.Equal(12, rects[0].X);
        Assert.Equal(12, rects[0].Y);
        Assert.Equal(12, rects[1].X);
        Assert.True(rects[1].Y >= rects[0].Bottom + 12);
    }

    [Fact]
    public void Place_NeverOverlapsLockedPadsOrEachOther()
    {
        var locked = new PlacedRect(0, 0, 600, 600);
        var sizes = Enumerable.Range(0, 8).Select(_ => (300.0, 250.0)).ToList();

        var rects = AutoLayoutPlanner.Place(sizes, Screen, [locked], 12, AutoStartCorner.TopLeft);

        foreach (var rect in rects)
        {
            Assert.False(rect.Intersects(locked, 0));
            Assert.True(rect.Right <= Screen.Right && rect.Bottom <= Screen.Bottom);
        }

        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].Intersects(rects[j], 0));
    }

    private static readonly int[] MixedCounts = [3, 2, 13, 2, 24, 7, 9, 19, 4, 2, 31, 2];
    private static readonly LayoutBounds WideScreen = LayoutBounds.FromSize(2560, 1400);

    [Fact]
    public void PlanGrid_AlignsTopsInRowsAndEdgesInColumns()
    {
        var rects = AutoLayoutPlanner.PlanGrid(MixedCounts, 6, Metrics, WideScreen, 12, AutoStartCorner.TopLeft, keepFirst: true)!;

        Assert.NotNull(rects);

        // Every top is shared by at least one other pad, or is the first row — i.e. rows line up.
        var tops = rects.Select(r => r.Y).Distinct().OrderBy(y => y).ToList();
        var lefts = rects.Select(r => r.X).Distinct().ToList();
        Assert.True(tops.Count < rects.Count);
        Assert.True(lefts.Count < rects.Count);

        // Pads sharing a left edge share a width.
        foreach (var column in rects.GroupBy(r => r.X))
            Assert.Single(column.Select(r => r.Width).Distinct());

        // Nothing overlaps, everything is on screen, and gaps are at least the spacing.
        for (var i = 0; i < rects.Count; i++)
        {
            Assert.True(rects[i].Right <= WideScreen.Right && rects[i].Bottom <= WideScreen.Bottom);
            for (var j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].Intersects(rects[j], 11.9));
        }
    }

    [Fact]
    public void PlanGrid_KeepsTheFirstPadInTheTopLeftCell()
    {
        var rects = AutoLayoutPlanner.PlanGrid(MixedCounts, 6, Metrics, WideScreen, 12, AutoStartCorner.TopLeft, keepFirst: true)!;

        Assert.Equal(12, rects[0].X);
        Assert.Equal(12, rects[0].Y);
    }

    [Fact]
    public void PlanGrid_WidenedPadsReflowAndNeverScroll()
    {
        var rects = AutoLayoutPlanner.PlanGrid([2, 12], 6, Metrics, LayoutBounds.FromSize(600, 2000), 12, AutoStartCorner.TopLeft, keepFirst: false)!;

        // One column (screen is narrow): the 2-icon pad is as wide as the 6-wide one, and one icon row tall.
        Assert.Equal(rects[0].Width, rects[1].Width);
        Assert.Equal(84 + 46, rects[0].Height);
        Assert.Equal(2 * 84 + 46, rects[1].Height);
    }

    [Fact]
    public void PlanGrid_FitsARealDesktopOnFullHd_ByNarrowingAColumn()
    {
        // System, Design, Media, Installers, Other, Folders, Dev, Remote, Documents, Browsers, Utilities, Apps.
        int[] counts = [3, 2, 11, 2, 2, 23, 7, 9, 19, 4, 2, 27];
        var screen = LayoutBounds.FromSize(1920, 1040);

        var rects = AutoLayoutPlanner.PlanGrid(counts, 6, Metrics, screen, 12, AutoStartCorner.TopLeft, keepFirst: true);

        Assert.NotNull(rects);
        Assert.All(rects, r => Assert.True(r.Right <= screen.Right - 12 && r.Bottom <= screen.Bottom - 12));
    }

    [Fact]
    public void PlanGrid_ReturnsNullWhenNoTableFits()
        => Assert.Null(AutoLayoutPlanner.PlanGrid(MixedCounts, 6, Metrics, LayoutBounds.FromSize(800, 600), 12, AutoStartCorner.TopLeft, keepFirst: true));

    [Fact]
    public void PlanGridFixedSizes_NeverResizesAPad()
    {
        (double, double)[] sizes = [(320, 130), (622, 298), (470, 214), (166, 130), (622, 382), (320, 130)];

        var rects = AutoLayoutPlanner.PlanGridFixedSizes(sizes, WideScreen, 12, AutoStartCorner.TopLeft, keepFirst: true)!;

        for (var i = 0; i < sizes.Length; i++)
            Assert.Equal(sizes[i], (rects[i].Width, rects[i].Height));
    }

    [Fact]
    public void PlanGridFixedSizes_AlignsLeftEdgesAndNeverOverlaps()
    {
        (double, double)[] sizes = [(320, 130), (622, 298), (470, 214), (166, 130), (622, 382), (320, 130), (470, 130)];

        var rects = AutoLayoutPlanner.PlanGridFixedSizes(sizes, LayoutBounds.FromSize(1920, 1040), 12, AutoStartCorner.TopLeft, keepFirst: true)!;

        Assert.Equal((12.0, 12.0), (rects[0].X, rects[0].Y));
        Assert.True(rects.Select(r => r.X).Distinct().Count() < rects.Count);
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].Intersects(rects[j], 11.9));
    }

    [Fact]
    public void NearestFreeSpot_MovesAPadThatFellOffAShrunkScreen_ToTheClosestGap()
    {
        // 2560x1440 → 1920x1080: a pad that sat at y=1200 must come up, but not onto its neighbour.
        var neighbour = new PlacedRect(12, 700, 600, 300);
        var lost = new PlacedRect(12, 1200, 600, 150);

        var spot = AutoLayoutPlanner.FindNearestFreeSpot(lost, Screen, [neighbour], 12)!.Value;

        Assert.True(spot.Bottom <= Screen.Bottom - 12);
        Assert.False(spot.Intersects(neighbour, 11.9));
        Assert.Equal(lost.Width, spot.Width);
    }

    [Fact]
    public void NearestFreeSpot_IsNull_WhenTheScreenIsFull()
    {
        var wall = new PlacedRect(0, 0, Screen.Width, Screen.Height);

        Assert.Null(AutoLayoutPlanner.FindNearestFreeSpot(new PlacedRect(100, 100, 300, 200), Screen, [wall], 12));
    }

    [Fact]
    public void Place_FromTopRight_StartsAtTheRightEdge()
    {
        var rects = AutoLayoutPlanner.Place([(200, 300)], Screen, [], 12, AutoStartCorner.TopRight);

        Assert.Equal(Screen.Right - 12, rects[0].Right);
    }
}
