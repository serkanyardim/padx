using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class SnapEngineTests
{
    private static readonly LayoutBounds Screen = LayoutBounds.FromSize(1920, 1040);
    private static readonly PlacedRect Office = new(10, 70, 620, 130);

    [Fact]
    public void DraggingBesideAPad_AlignsTheTops_AndShowsAGuide()
    {
        // The case from the screenshot: moved next to OFFICE, a few pixels lower.
        var moving = new PlacedRect(660, 76, 620, 210);

        var result = SnapEngine.Snap(moving, [Office], Screen, 12);

        Assert.Equal(70, result.Y);
        var guide = Assert.Single(result.Guides, g => !g.Vertical);
        Assert.Equal(70, guide.Position);
        Assert.True(guide.Start <= Office.X && guide.End >= 660 + 620);
    }

    [Fact]
    public void AlignsBottomsAndMiddlesToo()
    {
        Assert.Equal(Office.Bottom - 100, SnapEngine.Snap(new PlacedRect(700, Office.Bottom - 100 + 6, 300, 100), [Office], Screen, 12).Y);

        var middle = Office.Y + Office.Height / 2;
        Assert.Equal(middle - 40, SnapEngine.Snap(new PlacedRect(700, middle - 40 - 5, 300, 80), [Office], Screen, 12).Y);
    }

    [Fact]
    public void AlignsLeftEdges_WhenStackedBelow()
    {
        var moving = new PlacedRect(Office.X + 7, 400, 300, 100);

        var result = SnapEngine.Snap(moving, [Office], Screen, 12);

        Assert.Equal(Office.X, result.X);
        Assert.Contains(result.Guides, g => g.Vertical && g.Position == Office.X);
    }

    [Fact]
    public void SnapsToTheSpacingGap_BesideAPad()
    {
        var moving = new PlacedRect(Office.Right + 12 + 6, 500, 300, 100);

        Assert.Equal(Office.Right + 12, SnapEngine.Snap(moving, [Office], Screen, 12).X);
    }

    [Fact]
    public void PicksTheNearestTarget_NotTheLastOneChecked()
    {
        var near = new PlacedRect(1000, 300, 200, 100);
        var far = new PlacedRect(1000, 308, 200, 100);
        var moving = new PlacedRect(400, 302, 200, 100);

        Assert.Equal(300, SnapEngine.Snap(moving, [near, far], Screen, 12).Y);
    }

    [Fact]
    public void LeavesThePadAlone_WhenNothingIsInReach()
    {
        var moving = new PlacedRect(800, 500, 200, 100);

        var result = SnapEngine.Snap(moving, [Office], Screen, 12);

        Assert.Equal((800.0, 500.0), (result.X, result.Y));
        Assert.Empty(result.Guides);
    }
}
