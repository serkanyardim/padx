namespace Paddock.Core.Models;

/// <summary>How Auto Organize lays out and styles the pads it creates.</summary>
public class AutoModeSettings
{
    public const int MinPerRow = 2;
    public const int MaxPerRow = 12;

    /// <summary>Most icons side by side in one pad before it wraps to a new row.</summary>
    public int MaxIconsPerRow { get; set; } = 6;

    /// <summary>Categories with fewer items than this are folded into "Other".</summary>
    public int MinItemsPerPad { get; set; } = 2;

    public AutoColorMode ColorMode { get; set; } = AutoColorMode.ByCategory;

    /// <summary>Where the first pad goes; the rest flow down, then across.</summary>
    public AutoStartCorner StartCorner { get; set; } = AutoStartCorner.TopLeft;

    /// <summary>Lock the created pads so a later Auto Organize leaves them alone.</summary>
    public bool LockCreatedPads { get; set; }

    /// <summary>
    /// Also organize shortcuts on the all-users desktop. Needs administrator
    /// permission (one prompt per run) and removes them for other accounts too.
    /// </summary>
    public bool IncludeAllUsersDesktop { get; set; } = true;

    /// <summary>Hide, then reveal pads one by one, instead of switching instantly.</summary>
    public bool Animate { get; set; } = true;

    public void Normalize()
    {
        MaxIconsPerRow = Math.Clamp(MaxIconsPerRow, MinPerRow, MaxPerRow);
        MinItemsPerPad = Math.Clamp(MinItemsPerPad, 1, 10);
    }
}

public enum AutoColorMode
{
    /// <summary>Every pad uses the default pad colour (which may be glass).</summary>
    Default,

    /// <summary>Each category always gets the same colour.</summary>
    ByCategory,

    /// <summary>A different random colour each run.</summary>
    Random
}

public enum AutoStartCorner
{
    TopLeft,
    TopRight
}
