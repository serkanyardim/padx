using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Reads what a desktop item points at, for categorising it: the target of a
/// .lnk (without resolving it — no network, no "searching for target"
/// dialogs), or the URL of a .url file.
/// </summary>
public static class ShortcutReader
{
    private const uint SLGP_RAWPATH = 0x4;
    private const int MaxPath = 1024;

    /// <summary>Describes an item for <see cref="IconCategorizer"/>.</summary>
    public static DesktopItemInfo Describe(string path)
    {
        if (Directory.Exists(path))
            return new DesktopItemInfo(path, true);

        var extension = Path.GetExtension(path);
        string? target = null;

        if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            target = ReadLinkTarget(path);
        else if (string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(extension, ".website", StringComparison.OrdinalIgnoreCase))
            target = ReadUrl(path);

        var targetIsFolder = false;
        if (!string.IsNullOrEmpty(target) && !target.Contains("://", StringComparison.Ordinal))
        {
            // Only a local check; a UNC target is judged by its name alone.
            try
            {
                targetIsFolder = !target.StartsWith(@"\\", StringComparison.Ordinal) && Directory.Exists(target);
            }
            catch (Exception)
            {
            }
        }

        return new DesktopItemInfo(path, false, target, targetIsFolder);
    }

    private static string? ReadLinkTarget(string path)
    {
        object? link = null;

        try
        {
            link = new CShellLink();
            ((IPersistFile)link).Load(path, 0);

            var buffer = new StringBuilder(MaxPath);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, IntPtr.Zero, SLGP_RAWPATH);

            var target = Environment.ExpandEnvironmentVariables(buffer.ToString());
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read shortcut '{path}': {ex.Message}");
            return null;
        }
        finally
        {
            if (link is not null)
                Marshal.FinalReleaseComObject(link);
        }
    }

    private static string? ReadUrl(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..].Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read '{path}': {ex.Message}");
        }

        return null;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    /// <summary>Only the slots up to GetPath are needed; the order must match the vtable.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    }
}
