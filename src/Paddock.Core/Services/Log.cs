using System.Diagnostics;

namespace Paddock.Core.Services;

/// <summary>
/// Small append-only log at <c>%LocalAppData%/padx/paddock.log</c>.
///
/// Paddock has no console and no window of its own most of the time, and
/// <see cref="Debug"/> output is compiled out of Release builds — so without
/// this there is no way to find out why something did not work on a user's
/// machine. Logging never throws: a broken log must not break the app.
/// </summary>
public static class Log
{
    private const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();
    private static bool _rotationChecked;

    private static readonly string FilePath = AppPaths.LogFile;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception exception)
        => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        Debug.WriteLine($"[{level}] {message}");

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                RotateIfNeeded();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
            // Logging is best-effort by design.
        }
    }

    /// <summary>
    /// Keeps one previous log around and starts fresh past the cap. Checked once
    /// per run: a single session can overshoot the cap a little, which is a fair
    /// trade for not stat-ing the file on every line.
    /// </summary>
    private static void RotateIfNeeded()
    {
        if (_rotationChecked)
            return;

        _rotationChecked = true;

        try
        {
            var file = new FileInfo(FilePath);
            if (!file.Exists || file.Length < MaxBytes)
                return;

            var previous = FilePath + ".old";
            if (File.Exists(previous))
                File.Delete(previous);

            File.Move(FilePath, previous);
        }
        catch
        {
            // Ignore — worst case the log keeps growing slowly.
        }
    }
}
