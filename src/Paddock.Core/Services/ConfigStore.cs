using System.Text.Json;
using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>One icon in a saved config: enough to find the item again and put it back.</summary>
/// <param name="FileName">The item's file name; it is matched on the desktop when the config is loaded.</param>
/// <param name="Managed">True when Paddock moved it into the pad; false for an item only referenced where it lives.</param>
/// <param name="Path">Where it was when saved (used for referenced items).</param>
/// <param name="SystemClsid">For a built-in desktop icon (This PC…), its shell id; the shortcut is recreated.</param>
public sealed record ConfigIcon(string FileName, bool Managed, string Path, string? SystemClsid = null);

/// <summary>A saved config as listed: name, when, and the main screen resolution it was made for.</summary>
public sealed record ConfigInfo(string Name, DateTime SavedAt, string? Resolution);

/// <summary>A named snapshot of everything: settings, appearance, pads and what is in them.</summary>
public sealed class SavedConfig
{
    public string Name { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }

    /// <summary>Main screen resolution when it was saved, e.g. "2560x1440". Null for older configs.</summary>
    public string? Resolution { get; set; }

    public AppSettings Settings { get; set; } = new();

    /// <summary>Pad id → its icons, in order.</summary>
    public Dictionary<string, List<ConfigIcon>> Icons { get; set; } = new();
}

/// <summary>Saved configs as JSON files in <c>%LocalAppData%\padx\configs</c>.</summary>
public class ConfigStore
{
    public ConfigStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>The suggested name for a new config: config-yyyyMMdd-HHmm.</summary>
    public static string DefaultName(DateTime now) => $"config-{now:yyyyMMdd-HHmm}";

    /// <summary>Error message for an unusable name, or null.</summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Give the config a name.";

        if (name.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A name can't contain any of these: \\ / : * ? \" < > |";

        return null;
    }

    public bool Exists(string name) => File.Exists(PathOf(name));

    /// <summary>Saved configs, newest first, with the resolution each was saved at.</summary>
    public IReadOnlyList<ConfigInfo> List()
    {
        if (!Directory.Exists(Folder))
            return [];

        return Directory.EnumerateFiles(Folder, "*.json")
            .Select(f => new ConfigInfo(System.IO.Path.GetFileNameWithoutExtension(f), File.GetLastWriteTime(f), ReadResolution(f)))
            .OrderByDescending(c => c.SavedAt)
            .ToList();
    }

    /// <summary>Reads just the resolution field, without loading the whole config.</summary>
    private static string? ReadResolution(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("resolution", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Pretty form of a resolution key: "2560x1440" → "2560×1440".</summary>
    public static string Display(string? resolution) => resolution?.Replace('x', '×') ?? "—";

    public void Save(SavedConfig config)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(config, SettingsService.SerializerOptions);
        File.WriteAllText(PathOf(config.Name), json);
    }

    public void Delete(string name)
    {
        var path = PathOf(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    public SavedConfig? Load(string name)
    {
        try
        {
            var path = PathOf(name);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SavedConfig>(File.ReadAllText(path), SettingsService.SerializerOptions)
                : null;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read config '{name}'.", ex);
            return null;
        }
    }

    private string PathOf(string name) => System.IO.Path.Combine(Folder, name.Trim() + ".json");
}
