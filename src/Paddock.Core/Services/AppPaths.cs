namespace Paddock.Core.Services;

/// <summary>
/// Where padx keeps its own files. One place to look, and one place to
/// change, for the folder the user is pointed at when something needs
/// inspecting.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// <c>%LocalAppData%\padx</c> — settings, configs, log. Local rather than
    /// roaming: a layout belongs to this PC's screens.
    /// </summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "padx");

    /// <summary>Where versions up to 2.2 kept it: <c>%AppData%\Paddock</c>.</summary>
    private static string OldDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Paddock");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string LogFile => Path.Combine(DataFolder, "paddock.log");

    /// <summary>Saved configs, one JSON file each.</summary>
    public static string ConfigsFolder => Path.Combine(DataFolder, "configs");

    /// <summary>
    /// Moves the old data folder to <see cref="DataFolder"/>, once. Call it
    /// first thing, before anything is read or logged.
    /// </summary>
    public static void MoveOldDataFolder()
        => MoveOldDataFolder(OldDataFolder, DataFolder);

    /// <summary>
    /// Moves <paramref name="from"/>'s contents into <paramref name="to"/>,
    /// unless <paramref name="to"/> already has settings. The new folder may
    /// exist already without them (a log line written first), so it is merged
    /// into rather than skipped; what is already there is kept.
    /// </summary>
    public static void MoveOldDataFolder(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from) || File.Exists(Path.Combine(to, "settings.json")))
                return;

            MergeInto(from, to);

            if (!Directory.EnumerateFileSystemEntries(from, "*", SearchOption.AllDirectories).Any(File.Exists))
                Directory.Delete(from, recursive: true);

            Log.Info($"Data folder moved from '{from}' to '{to}'.");
        }
        catch (Exception ex)
        {
            // Not fatal: padx starts with what it finds in the new place.
            Log.Warn($"Could not move the old data folder: {ex.Message}");
        }
    }

    private static void MergeInto(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Move(file, target);
        }

        foreach (var folder in Directory.EnumerateDirectories(from))
            MergeInto(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}
