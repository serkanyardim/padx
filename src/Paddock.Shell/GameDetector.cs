using System.IO;

namespace Paddock.Shell;

/// <summary>
/// Recognises games that no keyword list could know, by what sits next to the
/// executable: every Unity game ships UnityPlayer.dll, every Unreal game an
/// "Engine" folder, Steam games the Steam API. Cheap — a few file checks.
/// </summary>
public static class GameDetector
{
    private static readonly string[] MarkerFiles =
    [
        "UnityPlayer.dll", "UnityCrashHandler64.exe", "UnityCrashHandler32.exe", "GameAssembly.dll",
        "steam_api64.dll", "steam_api.dll", "EOSSDK-Win64-Shipping.dll", "galaxy64.dll",
    ];

    public static bool LooksLikeGame(string exePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(folder))
                return false;

            if (MarkerFiles.Any(f => File.Exists(Path.Combine(folder, f))))
                return true;

            // Unreal: Game.exe next to Engine\ (and Game\Binaries\Win64 inside).
            if (Directory.Exists(Path.Combine(folder, "Engine", "Binaries")))
                return true;

            // Unity: Game.exe next to Game_Data\.
            var name = Path.GetFileNameWithoutExtension(exePath);
            return Directory.Exists(Path.Combine(folder, name + "_Data", "Managed"))
                   || Directory.Exists(Path.Combine(folder, name + "_Data", "il2cpp_data"));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
