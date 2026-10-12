using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Moves items on and off the all-users desktop (C:\Users\Public\Desktop),
/// which needs administrator rights. The app relaunches itself elevated with
/// a job file — one UAC prompt per batch — and the elevated copy does nothing
/// but the moves.
///
/// The elevated side trusts nothing in the job: every move must go between a
/// direct child of the all-users desktop and a folder inside a ".Paddock"
/// store, and only files are moved. So even a tampered job file cannot make
/// it touch anything else.
/// </summary>
public static class ElevatedMover
{
    /// <summary>Command-line switch that makes Paddock.exe run a move job and exit.</summary>
    public const string Argument = "--paddock-elevated-move";

    private const int ErrorCancelled = 1223;

    public sealed record MoveRequest(string From, string To);

    private sealed record Job(List<MoveRequest> Moves);

    private sealed record JobResult(List<MoveRequest> Done);

    /// <summary>
    /// Runs the moves elevated and waits. Returns the moves that happened
    /// (with their final destination), or null when the user declined UAC.
    /// </summary>
    public static List<MoveRequest>? Run(IReadOnlyList<MoveRequest> moves)
    {
        if (moves.Count == 0)
            return [];

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paddock");
        Directory.CreateDirectory(folder);

        var jobPath = Path.Combine(folder, $"elevate-{Guid.NewGuid():N}.json");
        var resultPath = jobPath + ".result";

        try
        {
            File.WriteAllText(jobPath, JsonSerializer.Serialize(new Job(moves.ToList())));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"{Argument} \"{jobPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            process?.WaitForExit();

            if (!File.Exists(resultPath))
                return [];

            return JsonSerializer.Deserialize<JobResult>(File.ReadAllText(resultPath))?.Done ?? [];
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            Log.Info("Administrator permission was declined; all-users desktop items were left alone.");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Elevated move failed.", ex);
            return [];
        }
        finally
        {
            TryDelete(jobPath);
            TryDelete(resultPath);
        }
    }

    /// <summary>Entry point of the elevated process. Returns the exit code.</summary>
    public static int Execute(string jobPath)
    {
        var done = new List<MoveRequest>();

        try
        {
            var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(jobPath));
            var commonDesktop = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

            foreach (var move in job?.Moves ?? [])
            {
                var from = Normalize(move.From);
                var to = Normalize(move.To);

                var allowed = File.Exists(from)
                              && ((IsChildOf(from, commonDesktop) && IsInStore(to))
                                  || (IsInStore(from) && IsChildOf(to, commonDesktop)));

                if (!allowed)
                {
                    Log.Warn($"Elevated move refused: '{from}' -> '{to}'.");
                    continue;
                }

                try
                {
                    var target = Available(to);

                    // Copy + delete rather than move, so the file picks up the
                    // destination folder's permissions (and works across drives).
                    File.Copy(from, target);
                    File.Delete(from);
                    done.Add(new MoveRequest(move.From, target));
                }
                catch (Exception ex)
                {
                    Log.Error($"Elevated move of '{from}' failed.", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not run the elevated move job.", ex);
        }

        try
        {
            File.WriteAllText(jobPath + ".result", JsonSerializer.Serialize(new JobResult(done)));
        }
        catch (Exception ex)
        {
            Log.Error("Could not report the elevated move result.", ex);
        }

        return 0;
    }

    private static bool IsChildOf(string path, string folder)
        => string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);

    /// <summary>Inside &lt;something&gt;\.Paddock\&lt;folder&gt;\ — a pad folder or the staging folder.</summary>
    private static bool IsInStore(string path)
    {
        var folder = Path.GetDirectoryName(path);
        var store = folder is null ? null : Path.GetDirectoryName(folder);
        return store is not null
               && string.Equals(Path.GetFileName(store), DesktopIconService.StoreFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Available(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
