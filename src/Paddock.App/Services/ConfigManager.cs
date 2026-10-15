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
/// Loading drops the current pads and rebuilds the saved ones (same ids,
/// places, colours, settings), each pointing at its items again. No file
/// moves — items are on the desktop all along. Items saved in a config but
/// since deleted are skipped; items the config never knew about stay on the
/// desktop.
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
            config.Settings.IconOpenMode = current.IconOpenMode;
            config.Settings.Language = current.Language;
            config.Settings.SearchHotkey = current.SearchHotkey;
            config.Settings.SearchBoxAlwaysVisible = current.SearchBoxAlwaysVisible;
            config.Settings.SearchBoxPosition = current.SearchBoxPosition;
            config.Settings.SearchBoxStyle = current.SearchBoxStyle;
            config.Settings.SearchBoxColor = current.SearchBoxColor;
            config.Settings.SearchBoxOpacity = current.SearchBoxOpacity;
            config.Settings.SearchGlassOpacity = current.SearchGlassOpacity;
            config.Settings.SearchOffersWeb = current.SearchOffersWeb;

            var corral = config.Settings.Corrals.TryGetValue(config.Settings.ActiveCorral, out var c) ? c : new CorralModel();
            var savedPads = corral.Paddocks.ToList();
            corral.Paddocks.Clear();

            _app.Settings.Replace(config.Settings);

            foreach (var saved in savedPads)
            {
                var pad = Manager.RestorePaddock(saved);

                foreach (var icon in config.Icons.GetValueOrDefault(saved.Id) ?? [])
                {
                    if (!PutBack(pad.Id, icon))
                        missing++;
                }
            }
        }

        Log.Info($"Config '{name}' loaded; {missing} saved item(s) not found.");
        return missing;
    }

    /// <summary>
    /// Puts a saved item in its pad — by its path, and for configs saved
    /// while items still lived in Desktop\.Paddock, by its name on the desktop.
    /// </summary>
    private bool PutBack(string padId, ConfigIcon icon)
    {
        if (icon.SystemClsid is not null)
        {
            return SystemDesktopIcons.FromTarget("::" + icon.SystemClsid) is { } definition
                   && SystemIconShortcuts.Ensure(definition) is { } link
                   && Manager.AddIconToPaddock(padId, link) is not null;
        }

        var byName = new[]
        {
            Path.Combine(_app.DesktopIconService.DesktopRoot, icon.FileName),
            Path.Combine(_app.DesktopIconService.CommonDesktopRoot, icon.FileName),
        };

        var found = IconStore.ItemExists(icon.Path) ? icon.Path
            : _app.IconStore.IsInLegacyStore(icon.Path) ? byName.FirstOrDefault(IconStore.ItemExists)
            : null;

        return found is not null && Manager.AddIconToPaddock(padId, found) is not null;
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
    /// Used when padx is uninstalled: no pads left. Every item already is on
    /// the desktop, so once Windows shows its icons again nothing is missing.
    /// </summary>
    public void RestoreEverything()
    {
        using (Manager.DeferSave())
            EmptyAllPads();
    }

    /// <summary>Every pad gone; their items were on the desktop all along.</summary>
    private void EmptyAllPads()
    {
        foreach (var pad in Manager.GetPaddocks().ToList())
            Manager.DeletePaddock(pad.Id);
    }
}
