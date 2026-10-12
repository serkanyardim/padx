using Microsoft.Win32;

namespace Paddock.Shell;

/// <summary>
/// Manages the "Run on Windows startup" setting via the registry.
/// </summary>
public class StartupManager
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "padx";

    /// <summary>The entry name used before the app was renamed; removed so it never starts twice.</summary>
    private const string LegacyAppName = "Paddock";

    /// <summary>
    /// Returns true if Paddock is configured to start with Windows.
    /// </summary>
    public bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
        return key?.GetValue(AppName) is not null;
    }

    /// <summary>
    /// Enables or disables starting Paddock on Windows startup.
    /// </summary>
    public void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
        if (key is null) return;

        key.DeleteValue(LegacyAppName, throwOnMissingValue: false);

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? string.Empty;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
