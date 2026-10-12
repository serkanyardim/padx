using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// The desktop's built-in icons — Recycle Bin, the user's folder, This PC,
/// Network, Control Panel. They are not files, so they cannot be moved into a
/// pad. Instead a pad gets a real shortcut to the same shell object, and the
/// desktop icon is switched off with Windows' own per-user setting (the one
/// behind "Desktop icon settings"). Both steps are undone on revert.
/// </summary>
public static class SystemDesktopIcons
{
    private const string HideKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <param name="Clsid">Shell object id, e.g. {645FF040-…} for the Recycle Bin.</param>
    /// <param name="ShownByDefault">Whether Windows shows it when the setting is absent.</param>
    /// <param name="IsFolderLike">Opens like a folder (user files, This PC, Network).</param>
    public sealed record Definition(string Clsid, bool ShownByDefault, bool IsFolderLike);

    /// <summary>A system icon that was on the desktop, and what the setting held before we touched it.</summary>
    public sealed record Taken(Definition Icon, int? PreviousValue);

    public static IReadOnlyList<Definition> All { get; } =
    [
        new("{645FF040-5081-101B-9F08-00AA002F954E}", ShownByDefault: true, IsFolderLike: false),  // Recycle Bin
        new("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", ShownByDefault: false, IsFolderLike: true),  // User's Files
        new("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", ShownByDefault: false, IsFolderLike: true),  // This PC
        new("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", ShownByDefault: false, IsFolderLike: true),  // Network
        new("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", ShownByDefault: false, IsFolderLike: false), // Control Panel
    ];

    /// <summary>The built-in icons currently showing on the desktop.</summary>
    public static IReadOnlyList<Definition> Visible()
        => All.Where(d => ReadSetting(d.Clsid) is { } value ? value == 0 : d.ShownByDefault).ToList();

    /// <summary>The name the desktop shows for it (respects a rename, e.g. "bin").</summary>
    public static string DisplayName(Definition icon)
    {
        var pidl = Parse(icon.Clsid);
        if (pidl == IntPtr.Zero)
            return "Shell item";

        try
        {
            var info = new NativeMethods.SHFILEINFO();
            NativeMethods.SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_PIDL | SHGFI_DISPLAYNAME);
            return string.IsNullOrWhiteSpace(info.szDisplayName) ? "Shell item" : info.szDisplayName;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Writes a .lnk that opens the shell object, with its live icon. Returns false on failure.</summary>
    public static bool CreateShortcut(Definition icon, string linkPath)
    {
        var pidl = Parse(icon.Clsid);
        if (pidl == IntPtr.Zero)
            return false;

        object? link = null;
        try
        {
            link = new CShellLink();
            ((IShellLinkW)link).SetIDList(pidl);
            ((IPersistFile)link).Save(linkPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not create a shortcut to {icon.Clsid}: {ex.Message}");
            return false;
        }
        finally
        {
            if (link is not null)
                Marshal.FinalReleaseComObject(link);
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Hides the desktop icon, remembering the previous setting for <see cref="Restore"/>.</summary>
    public static Taken Hide(Definition icon)
    {
        var previous = ReadSetting(icon.Clsid);
        WriteSetting(icon.Clsid, 1);
        return new Taken(icon, previous);
    }

    /// <summary>Puts the setting back exactly as it was (including "not set").</summary>
    public static void Restore(Taken taken)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(HideKey);
            if (taken.PreviousValue is { } value)
                key.SetValue(taken.Icon.Clsid, value, RegistryValueKind.DWord);
            else
                key.DeleteValue(taken.Icon.Clsid, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not restore desktop icon {taken.Icon.Clsid}: {ex.Message}");
        }
    }

    /// <summary>Makes Explorer re-read the desktop icon settings.</summary>
    public static void RefreshDesktop()
        => NativeMethods.SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private static int? ReadSetting(string clsid)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HideKey);
            return key?.GetValue(clsid) is int value ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteSetting(string clsid, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(HideKey);
        key.SetValue(clsid, value, RegistryValueKind.DWord);
    }

    private static IntPtr Parse(string clsid)
    {
        var hr = SHParseDisplayName($"::{clsid}", IntPtr.Zero, out var pidl, 0, out _);
        return hr == 0 ? pidl : IntPtr.Zero;
    }

    private const uint SHGFI_DISPLAYNAME = 0x000000200;
    private const uint SHGFI_PIDL = 0x000000008;
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    /// <summary>IShellLinkW up to SetIDList; the slot order must match the vtable.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);
    }
}
