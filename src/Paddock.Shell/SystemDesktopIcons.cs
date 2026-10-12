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
    public sealed record Definition(string Clsid, bool ShownByDefault);

    /// <summary>A system icon that was on the desktop, and what the setting held before we touched it.</summary>
    public sealed record Taken(Definition Icon, int? PreviousValue);

    /// <summary>In the order they appear in the System pad: Recycle Bin always last.</summary>
    public static IReadOnlyList<Definition> All { get; } =
    [
        new("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", ShownByDefault: false), // This PC
        new("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", ShownByDefault: false), // User's Files
        new("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", ShownByDefault: false), // Network
        new("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", ShownByDefault: false), // Control Panel
        new("{645FF040-5081-101B-9F08-00AA002F954E}", ShownByDefault: true),  // Recycle Bin
    ];

    /// <summary>
    /// The built-in icon a shortcut target stands for ("::{CLSID}", as
    /// <see cref="ShortcutReader"/> reports it), or null.
    /// </summary>
    public static Definition? FromTarget(string? target)
    {
        if (target is null || !target.StartsWith("::", StringComparison.Ordinal))
            return null;

        var clsid = target[2..];
        return All.FirstOrDefault(d => string.Equals(d.Clsid, clsid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Position in the System pad; unknown shell objects go just before the Recycle Bin.</summary>
    public static int Rank(string? target)
    {
        var icon = FromTarget(target);
        // Known icons take even slots; an unknown one sits in the odd slot before the Recycle Bin.
        return icon is null ? (All.Count - 1) * 2 - 1 : All.ToList().IndexOf(icon) * 2;
    }

    /// <summary>Switches a built-in icon back on, e.g. when its shortcut leaves Paddock.</summary>
    public static void Show(Definition icon)
    {
        try
        {
            WriteSetting(icon.Clsid, 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not show desktop icon {icon.Clsid}: {ex.Message}");
        }
    }

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
