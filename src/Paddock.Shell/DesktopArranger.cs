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

            var columns = new[]
            {
                new SortColumn { Key = ItemType, Direction = SORT_ASCENDING },
                new SortColumn { Key = Name, Direction = SORT_ASCENDING },
            };

            return ((IFolderView2)view).SetSortColumns(columns, columns.Length) == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not sort the desktop by item type: {ex.Message}");
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
        void Item();
        void ItemCount();
        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();
        void GetItemPosition();
        void GetSpacing();
        void GetDefaultSpacing();
        void GetAutoArrange();
        void SelectItem();
        void SelectAndPositionItems();

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
