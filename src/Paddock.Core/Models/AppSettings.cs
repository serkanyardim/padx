namespace Paddock.Core.Models;

public class AppSettings
{
    public int Version { get; set; } = 1;
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// Old single startup config; replaced by <see cref="ResolutionDefaults"/>
    /// and migrated into it on first start.
    /// </summary>
    public string? StartupConfig { get; set; }

    /// <summary>
    /// Main screen resolution ("2560x1440") → the config to load for it, at
    /// startup and whenever the main screen changes to that resolution.
    /// </summary>
    public Dictionary<string, string> ResolutionDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// "auto" (Turkish on a Turkish Windows, English otherwise), "en", or a
    /// language file in lang/ such as "tr".
    /// </summary>
    public string Language { get; set; } = "auto";

    public string GlobalTheme { get; set; } = "dark";
    public bool QuickHideEnabled { get; set; } = true;
    public string QuickHideHotkey { get; set; } = "Ctrl+F12";

    /// <summary>Global shortcut that starts drawing a new pad. Empty disables it.</summary>
    public string NewPadHotkey { get; set; } = "Ctrl+Alt+N";

    /// <summary>Global shortcut that rolls every pad up, or expands them all again.</summary>
    public string RollUpAllHotkey { get; set; } = "Ctrl+Alt+R";

    /// <summary>Global shortcut that brings back pads hidden with their × button.</summary>
    public string ShowHiddenHotkey { get; set; } = "Ctrl+Alt+H";

    /// <summary>Global shortcut that opens the settings window.</summary>
    public string SettingsHotkey { get; set; } = "Ctrl+Alt+P";

    /// <summary>Global shortcut that opens the desktop search.</summary>
    public string SearchHotkey { get; set; } = "Alt+Q";

    /// <summary>
    /// Keeps the search box on the desktop all the time, instead of only when
    /// its shortcut is pressed. Pads stay out of the strip it sits in.
    /// </summary>
    public bool SearchBoxAlwaysVisible { get; set; }

    /// <summary>Where the always-visible search box sits: "top" or "bottom" of the main screen.</summary>
    public string SearchBoxPosition { get; set; } = "top";

    /// <summary>"color" (<see cref="SearchBoxColor"/> at <see cref="SearchBoxOpacity"/>) or "glass" (frosted).</summary>
    public string SearchBoxStyle { get; set; } = "color";

    public string SearchBoxColor { get; set; } = "#0B0E13";

    /// <summary>0.3 – 1.</summary>
    public double SearchBoxOpacity { get; set; } = 1.0;

    /// <summary>
    /// How dark the glass is tinted (anthracite), 0 – 0.9. That is the box in
    /// use; at rest a docked box is tinted half as much.
    /// </summary>
    public double SearchGlassOpacity { get; set; } = 0.55;

    /// <summary>Ends the results with "Search Google for …", on the user's country's Google.</summary>
    public bool SearchOffersWeb { get; set; } = true;

    /// <summary>Ctrl + drag on empty desktop draws a new pad.</summary>
    public bool CtrlDragCreatesPad { get; set; } = true;

    public bool SnapEnabled { get; set; } = true;
    public double IntelligentSpacing { get; set; } = 12;
    public PadAppearance Appearance { get; set; } = new();
    public AutoModeSettings AutoMode { get; set; } = new();
    public string RollUpExpandMode { get; set; } = "click";

    /// <summary>How an icon in a pad is opened: "double" (click twice, like the desktop) or "single".</summary>
    public string IconOpenMode { get; set; } = "double";
    public string ActiveCorral { get; set; } = "default";
    public Dictionary<string, CorralModel> Corrals { get; set; } = new()
    {
        ["default"] = new CorralModel()
    };
    public List<AutoSortRule> AutoSortRules { get; set; } = new();
}
