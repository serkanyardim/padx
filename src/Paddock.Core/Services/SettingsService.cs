using System.Text.Json;
using System.Text.Json.Serialization;
using Paddock.Core.Models;

namespace Paddock.Core.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _lock = new();
    private readonly string _settingsPath;
    private AppSettings _settings;

    private int _deferDepth;
    private bool _saveDeferred;

    public AppSettings Settings => _settings;

    /// <summary>The same JSON shape as settings.json, for saved configs.</summary>
    public static JsonSerializerOptions SerializerOptions => JsonOptions;

    /// <summary>Swaps in a whole set of settings (loading a saved config) and saves them.</summary>
    public void Replace(AppSettings settings)
    {
        settings.Appearance ??= new PadAppearance();
        settings.Appearance.Normalize();
        settings.AutoMode ??= new AutoModeSettings();
        settings.AutoMode.Normalize();

        lock (_lock)
        {
            _settings = settings;
        }

        Save();
    }

    public SettingsService()
        : this(AppPaths.SettingsFile)
    {
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _settings = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (loaded is not null)
            {
                loaded.Appearance ??= new PadAppearance();
                loaded.Appearance.Normalize();
                loaded.AutoMode ??= new AutoModeSettings();
                loaded.AutoMode.Normalize();
                return loaded;
            }

            Log.Warn($"settings.json deserialized to null; starting from defaults ({_settingsPath}).");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read '{_settingsPath}'; starting from defaults.", ex);
        }

        // Never silently discard a layout we failed to parse — keep it aside so
        // it can be inspected or recovered by hand.
        PreserveUnreadableSettings();
        return new AppSettings();
    }

    private void PreserveUnreadableSettings()
    {
        try
        {
            var backup = _settingsPath + ".corrupt";
            File.Copy(_settingsPath, backup, overwrite: true);
            Log.Warn($"Previous settings kept at '{backup}'.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not preserve the unreadable settings file.", ex);
        }
    }

    /// <summary>
    /// Suppresses writes until the returned scope is disposed, then writes once
    /// if anything asked to be saved. Bulk work — collecting a whole desktop
    /// into a paddock, say — would otherwise rewrite settings.json per item.
    /// Scopes may be nested.
    /// </summary>
    public IDisposable DeferSave()
    {
        lock (_lock)
        {
            _deferDepth++;
        }

        return new DeferScope(this);
    }

    public void Save()
    {
        lock (_lock)
        {
            if (_deferDepth > 0)
            {
                _saveDeferred = true;
                return;
            }

            WriteToDisk();
        }
    }

    public CorralModel GetActiveCorral()
    {
        if (_settings.Corrals.TryGetValue(_settings.ActiveCorral, out var corral))
            return corral;

        var defaultCorral = new CorralModel();
        _settings.Corrals[_settings.ActiveCorral] = defaultCorral;
        return defaultCorral;
    }

    /// <summary>Must be called while holding <see cref="_lock"/>.</summary>
    private void WriteToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Atomic settings write failed ({ex.Message}); trying a direct write.");

            try
            {
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception fallbackEx)
            {
                Log.Error("Could not save settings; will retry on the next change.", fallbackEx);
            }
        }
    }

    private void EndDefer()
    {
        lock (_lock)
        {
            if (_deferDepth > 0)
                _deferDepth--;

            if (_deferDepth > 0 || !_saveDeferred)
                return;

            _saveDeferred = false;
            WriteToDisk();
        }
    }

    private sealed class DeferScope(SettingsService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.EndDefer();
        }
    }
}
