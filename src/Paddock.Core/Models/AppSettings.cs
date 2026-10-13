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

    /// <summary>Ctrl + drag on empty desktop draws a new pad.</summary>
    public bool CtrlDragCreatesPad { get; set; } = true;

    public bool SnapEnabled { get; set; } = true;
    public double IntelligentSpacing { get; set; } = 12;
    public PadAppearance Appearance { get; set; } = new();
    public AutoModeSettings AutoMode { get; set; } = new();
    public string RollUpExpandMode { get; set; } = "click";
    public string ActiveCorral { get; set; } = "default";
    public Dictionary<string, CorralModel> Corrals { get; set; } = new()
    {
        ["default"] = new CorralModel()
    };
    public List<AutoSortRule> AutoSortRules { get; set; } = new();
}
