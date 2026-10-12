using System.Runtime.InteropServices;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Talks to Explorer's own desktop view, the documented way: ShellWindows →
/// the desktop's IShellBrowser → its active view → IFolderView2. Used to do
/// what right-click → Sort by → Item type does on the desktop.
/// </summary>
public static class DesktopArranger
{
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const int SORT_ASCENDING = 1;

    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>PKEY_ItemTypeText — the "Item type" column.</summary>
    private static readonly PropertyKey ItemType = new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 4);

    /// <summary>PKEY_ItemNameDisplay — a second key so items of one type sort by name.</summary>
    private static readonly PropertyKey Name = new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);

    /// <summary>Sorts the desktop icons by item type (then name). Returns false if the desktop view was unreachable.</summary>
    public static bool SortByItemType()
        => WithDesktopView(view =>
        {
            var columns = new[]
            {
                new SortColumn { Key = ItemType, Direction = SORT_ASCENDING },
                new SortColumn { Key = Name, Direction = SORT_ASCENDING },
            };

            return view.SetSortColumns(columns, columns.Length) == 0;
        });

    /// <summary>
    /// Puts the built-in icons (This PC, user folder, … Recycle Bin) in the
    /// first grid slots, in <see cref="SystemDesktopIcons.Rank"/> order, and
    /// shifts everything else along behind them in its current order. Run it
    /// after <see cref="SortByItemType"/>: a plain type sort files "System
    /// Folder" under S.
    /// </summary>
    public static bool MoveSystemIconsFirst()
        => WithDesktopView(view =>
        {
            if (view.ItemCount(SVGIO_ALLVIEW, out var count) != 0 || count == 0)
                return false;

            var items = new List<(IntPtr Pidl, Point Position, int Rank)>();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    if (view.Item(i, out var pidl) != 0 || pidl == IntPtr.Zero)
                        continue;

                    view.GetItemPosition(pidl, out var point);
                    items.Add((pidl, point, SystemRank(pidl)));
                }

                // The slots as they are filled: down each column, then across.
                var slots = items.Select(i => i.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
                var current = items.OrderBy(i => i.Position.X).ThenBy(i => i.Position.Y).ToList();

                var wanted = current.Where(i => i.Rank >= 0).OrderBy(i => i.Rank)
                    .Concat(current.Where(i => i.Rank < 0))
                    .ToList();

                if (wanted.Select(i => i.Pidl).SequenceEqual(current.Select(i => i.Pidl)))
                    return true;

                var pidls = wanted.Select(i => i.Pidl).ToArray();
                var points = slots.ToArray();
                return view.SelectAndPositionItems((uint)pidls.Length, pidls, points, SVSI_POSITIONITEM) == 0;
            }
            finally
            {
                foreach (var item in items)
                    Marshal.FreeCoTaskMem(item.Pidl);
            }
        });

    /// <summary>System-pad rank of a desktop item, or -1 for an ordinary file or folder.</summary>
    private static int SystemRank(IntPtr childPidl)
    {
        // Desktop items' child IDs are also absolute: the desktop is the root.
        if (SHGetNameFromIDList(childPidl, SIGDN_DESKTOPABSOLUTEPARSING, out var namePtr) != 0 || namePtr == IntPtr.Zero)
            return -1;

        string? name;
        try
        {
            name = Marshal.PtrToStringUni(namePtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePtr);
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.Equals(name?.TrimEnd('\\'), profile.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            name = "::{59031a47-3f72-44a7-89c5-5595fe6b30ee}";

        return SystemDesktopIcons.FromTarget(name) is null ? -1 : SystemDesktopIcons.Rank(name);
    }

    private static bool WithDesktopView(Func<IFolderView2, bool> action)
    {
        object? windows = null;
        object? desktop = null;
        object? browser = null;
        object? view = null;

        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!);
            object location = 0; // CSIDL_DESKTOP
            object root = Type.Missing;

            desktop = ((IShellWindows)windows!).FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
            if (desktop is null)
                return false;

            var service = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            if (((IServiceProvider)desktop).QueryService(ref service, ref iid, out browser) != 0 || browser is null)
                return false;

            if (((IShellBrowser)browser).QueryActiveShellView(out view) != 0 || view is null)
                return false;

            return action((IFolderView2)view);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not arrange the desktop: {ex.Message}");
            return false;
        }
        finally
        {
            foreach (var com in new[] { view, browser, desktop, windows })
            {
                if (com is not null && Marshal.IsComObject(com))
                    Marshal.ReleaseComObject(com);
            }
        }
    }

    private const uint SVGIO_ALLVIEW = 0x2;
    private const uint SVSI_POSITIONITEM = 0x80;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    [StructLayout(LayoutKind.Sequential)]
    private struct SortColumn
    {
        public PropertyKey Key;
        public int Direction;
    }

    /// <summary>Slots before FindWindowSW are placeholders; only the count and order matter.</summary>
    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void get_Count();
        void Item();
        void _NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(
            [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
            [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
            int swClass,
            out int pHwnd,
            int swfwOptions);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppvObject);
    }

    /// <summary>IShellBrowser (IOleWindow first); placeholders up to QueryActiveShellView.</summary>
    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object? ppshv);
    }

    /// <summary>IFolderView2, flattened with its IFolderView base; placeholders up to SetSortColumns.</summary>
    [ComImport]
    [Guid("1af3a467-214f-4298-908e-06b03e0b39f9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();

        [PreserveSig]
        int Item(int iItemIndex, out IntPtr ppidl);

        [PreserveSig]
        int ItemCount(uint uFlags, out int pcItems);

        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();

        [PreserveSig]
        int GetItemPosition(IntPtr pidl, out Point ppt);

        void GetSpacing();
        void GetDefaultSpacing();
        void GetAutoArrange();
        void SelectItem();

        [PreserveSig]
        int SelectAndPositionItems(
            uint cidl,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Point[] apt,
            uint dwFlags);

        // IFolderView2
        void SetGroupBy();
        void GetGroupBy();
        void SetViewProperty();
        void GetViewProperty();
        void SetTileViewProperties();
        void SetExtendedTileViewProperties();
        void SetText();
        void SetCurrentFolderFlags();
        void GetCurrentFolderFlags();
        void GetSortColumnCount();

        [PreserveSig]
        int SetSortColumns([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SortColumn[] rgSortColumns, int cColumns);
    }
}
