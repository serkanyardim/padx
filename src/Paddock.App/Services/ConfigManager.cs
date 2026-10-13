using System.IO;
using System.Text.Json;
using Paddock.Core.Models;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// Saves and loads named configs: every setting, every pad, and which item
/// sits in which pad.
///
/// Loading puts everything back on the desktop first, then rebuilds the saved
/// pads (same ids, places, colours, settings) and moves each item in by its
/// file name. Items saved in a config but since deleted are skipped; items
/// the config never knew about stay on the desktop.
/// </summary>
internal sealed class ConfigManager
{
    private readonly App _app;

    public ConfigManager(App app)
    {
        _app = app;
        Store = new ConfigStore(AppPaths.ConfigsFolder);
    }

    public ConfigStore Store { get; }

    private PaddockManager Manager => _app.PaddockManager;

    /// <summary>
    /// Snapshots the layout on the UI thread (cheap), then reads the shortcuts
    /// and writes the file in the background, so the UI never waits on disk.
    /// </summary>
    public async Task SaveAsync(string name)
    {
        // A deep copy, so later changes do not leak into the saved file.
        var settings = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(_app.Settings.Settings, SettingsService.SerializerOptions),
            SettingsService.SerializerOptions)!;

        var pads = Manager.GetPaddocks()
            .Select(p => (p.Id, Icons: PaddockManager.GetSortedIcons(p).Select(i => (i.DesktopPath, i.Managed)).ToList()))
            .ToList();

        using var _ = Activity.Begin($"saving config '{name}'");

        var icons = await Task.Run(() => pads.ToDictionary(
            p => p.Id,
            p => p.Icons.Select(i => new ConfigIcon(
                    Path.GetFileName(i.DesktopPath),
                    i.Managed,
                    i.DesktopPath,
                    SystemDesktopIcons.FromTarget(ShortcutReader.Describe(i.DesktopPath).Target)?.Clsid))
                .ToList()));

        var config = new SavedConfig
        {
            Name = name.Trim(),
            SavedAt = DateTime.Now,
            Resolution = Helpers.ScreenHelper.MainResolution(),
            Settings = settings,
            Icons = icons,
        };
        await Task.Run(() => Store.Save(config));

        Log.Info($"Config '{name}' saved: {icons.Count} pad(s), {icons.Values.Sum(l => l.Count)} item(s).");
    }

    /// <summary>
    /// Applies a saved config to the files and settings. Pad windows are not
    /// touched — the caller closes them before and opens them after.
    /// Returns how many saved items could not be found, or null if the config is unreadable.
    /// </summary>
    public int? Apply(string name)
    {
        var config = Store.Load(name);
        if (config is null)
            return null;

        var missing = 0;

        using (Manager.DeferSave())
        {
            EmptyAllPads();

            // Settings that belong to this PC rather than to a layout stay as they are.
            var current = _app.Settings.Settings;
            config.Settings.StartupConfig = current.StartupConfig;
            config.Settings.StartWithWindows = current.StartWithWindows;
            config.Settings.ResolutionDefaults = current.ResolutionDefaults;

            var corral = config.Settings.Corrals.TryGetValue(config.Settings.ActiveCorral, out var c) ? c : new CorralModel();
            var savedPads = corral.Paddocks.ToList();
            corral.Paddocks.Clear();

            _app.Settings.Replace(config.Settings);

            var onDesktop = _app.DesktopIconService.GetDesktopItems()
                .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var saved in savedPads)
            {
                var pad = Manager.RestorePaddock(saved);
                var folder = _app.IconStore.EnsurePaddockFolder(pad.Id);

                foreach (var icon in config.Icons.GetValueOrDefault(saved.Id) ?? [])
                {
                    if (!PutBack(pad.Id, folder, icon, onDesktop))
                        missing++;
                }
            }
        }

        SystemDesktopIcons.RefreshDesktop();
        _app.DesktopIconService.NotifyShellOfDesktopChange();
        Log.Info($"Config '{name}' loaded; {missing} saved item(s) not found.");
        return missing;
    }

    private bool PutBack(string padId, string folder, ConfigIcon icon, Dictionary<string, string> onDesktop)
    {
        if (icon.SystemClsid is not null)
        {
            var definition = SystemDesktopIcons.FromTarget("::" + icon.SystemClsid);
            var link = Path.Combine(folder, icon.FileName);

            if (definition is null || !SystemDesktopIcons.CreateShortcut(definition, link))
                return false;

            SystemDesktopIcons.Hide(definition);
            return Manager.AddIconToPaddock(padId, link) is not null;
        }

        if (!icon.Managed)
            return IconStore.ItemExists(icon.Path) && Manager.AddIconToPaddock(padId, icon.Path) is not null;

        if (!onDesktop.Remove(icon.FileName, out var path))
            return false;

        return Manager.AddIconToPaddock(padId, path) is not null;
    }

    // --- Defaults per main-screen resolution ---

    /// <summary>The config marked ★ for this resolution, if it still exists.</summary>
    public string? DefaultFor(string resolution)
        => _app.Settings.Settings.ResolutionDefaults.TryGetValue(resolution, out var name) && Store.Exists(name) ? name : null;

    /// <summary>Makes a config the ★ default of a resolution (replacing any previous one).</summary>
    public void SetDefault(string resolution, string name)
    {
        _app.Settings.Settings.ResolutionDefaults[resolution] = name;
        _app.Settings.Save();
    }

    public void RemoveDefault(string resolution)
    {
        _app.Settings.Settings.ResolutionDefaults.Remove(resolution);
        _app.Settings.Save();
    }

    /// <summary>Drops every ★ that points at a config (when it is deleted).</summary>
    public void ForgetDefaults(string name)
    {
        var defaults = _app.Settings.Settings.ResolutionDefaults;
        foreach (var key in defaults.Where(d => string.Equals(d.Value, name, StringComparison.OrdinalIgnoreCase)).Select(d => d.Key).ToList())
            defaults.Remove(key);

        _app.Settings.Save();
    }

    /// <summary>
    /// The old single "startup config" becomes the default of the resolution
    /// in use now, so nobody loses their setting.
    /// </summary>
    public void MigrateStartupConfig()
    {
        var settings = _app.Settings.Settings;
        if (settings.StartupConfig is not { } old)
            return;

        var resolution = Helpers.ScreenHelper.MainResolution();
        if (!settings.ResolutionDefaults.ContainsKey(resolution) && Store.Exists(old))
            settings.ResolutionDefaults[resolution] = old;

        settings.StartupConfig = null;
        _app.Settings.Save();
        Log.Info($"Startup config '{old}' is now the default for {resolution}.");
    }

    /// <summary>
    /// Used when padx is uninstalled: everything back on the desktop, no pads
    /// left, built-in icons visible again — nothing stranded in the hidden
    /// pad folders once the app is gone.
    /// </summary>
    public void RestoreEverything()
    {
        using (Manager.DeferSave())
            EmptyAllPads();

        SystemDesktopIcons.RefreshDesktop();
        _app.DesktopIconService.NotifyShellOfDesktopChange();
    }

    /// <summary>
    /// Every item back on the desktop and every pad gone; built-in icons
    /// (This PC…) come back as themselves.
    /// </summary>
    private void EmptyAllPads()
    {
        foreach (var pad in Manager.GetPaddocks().ToList())
        {
            foreach (var icon in pad.Icons.ToList())
            {
                if (SystemDesktopIcons.FromTarget(ShortcutReader.Describe(icon.DesktopPath).Target) is { } systemIcon)
                {
                    Manager.RemoveIconFromPaddock(pad.Id, icon.DesktopPath);
                    TryDelete(icon.DesktopPath);
                    SystemDesktopIcons.Show(systemIcon);
                }
                else if (icon.Managed)
                {
                    Manager.EjectIcon(pad.Id, icon.DesktopPath);
                }
                else
                {
                    Manager.RemoveIconFromPaddock(pad.Id, icon.DesktopPath);
                }
            }

            Manager.DeletePaddock(pad.Id);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete '{path}': {ex.Message}");
        }
    }
}
