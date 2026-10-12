namespace Paddock.Core.Services;

/// <summary>
/// Where Paddock keeps its own files. One place to look, and one place to
/// change, for the folder the user is pointed at when something needs
/// inspecting.
/// </summary>
public static class AppPaths
{
    /// <summary><c>%AppData%/Paddock</c> — settings and log live here.</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Paddock");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string LogFile => Path.Combine(DataFolder, "paddock.log");

    /// <summary>Saved configs (Config ▸ Save Config), one JSON file each.</summary>
    public static string ConfigsFolder => Path.Combine(DataFolder, "configs");
}
