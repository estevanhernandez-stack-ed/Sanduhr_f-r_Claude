using System.Runtime.InteropServices;

namespace DrawerLook;

/// <summary>
/// Verified on build 26220 by this spike (see VERDICT.md): the desktop's icon view, reached the documented way.
/// IShellWindows.FindWindowSW(SWC_DESKTOP) -> IServiceProvider.QueryService(SID_STopLevelBrowser, IShellBrowser)
/// -> IShellBrowser.QueryActiveShellView -> IFolderView. Method order is shobjidl_core.h / exdisp.h order; every
/// slot before the last one called is declared, so the vtable lines up. Task 5 copies this file into
/// Sanduhr.App/Interop. Run on an STA thread.
/// </summary>
internal static class DesktopIconsNative
{
    public const int SWC_DESKTOP = 8;
    public const int SWFO_NEEDDISPATCH = 1;
    public const uint SVGIO_ALLVIEW = 0x00000002;
    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const int S_OK = 0;

    public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <summary>exdisp.h. Dual; the IDispatch slots come first and are supplied by the runtime.</summary>
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IShellWindows
    {
        int get_Count();                                                   // Count
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
        int RegisterPending(int lThreadId, ref object pvarloc, ref object pvarlocRoot, int swClass);
        void Revoke(int lCookie);
        void OnNavigate(int lCookie, ref object pvarLoc);
        void OnActivated(int lCookie, [MarshalAs(UnmanagedType.VariantBool)] bool fActive);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object pvarLoc, ref object pvarLocRoot, int swClass, out int pHWND, int swfwOptions);
        // OnCreated, ProcessAttachDetach follow; not called.
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    /// <summary>shobjidl_core.h. Derives IOleWindow (GetWindow, ContextSensitiveHelp).</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellBrowser
    {
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
        void InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        void SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        void RemoveMenusSB(IntPtr hmenuShared);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string pszStatusText);
        void EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool fEnable);
        void TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        void BrowseObject(IntPtr pidl, uint wFlags);
        void GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        void GetControlWindow(uint id, out IntPtr phwnd);
        void SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView ppshv);
        void OnViewWindowActive(IShellView pshv);
        void SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
    }

    /// <summary>shobjidl_core.h. Derives IOleWindow; only GetWindow is called (the SHELLDLL_DefView hwnd).
    /// The rest of the vtable is not declared, so never call past ContextSensitiveHelp.</summary>
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellView
    {
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }

    /// <summary>shobjidl_core.h, full vtable order.</summary>
    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFolderView
    {
        void GetCurrentViewMode(out uint pViewMode);
        void SetCurrentViewMode(uint ViewMode);
        void GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void Item(int iItemIndex, out IntPtr ppidl);
        void ItemCount(uint uFlags, out int pcItems);
        void Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetSelectionMarkedItem(out int piItem);
        void GetFocusedItem(out int piItem);
        void GetItemPosition(IntPtr pidl, out POINT ppt);
        void GetSpacing(out POINT ppt);
        void GetDefaultSpacing(out POINT ppt);
        /// <summary>S_OK (0) when auto-arrange is on, S_FALSE (1) when off.</summary>
        [PreserveSig] int GetAutoArrange();
        void SelectItem(int iItem, uint dwFlags);
        void SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
    }

    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    public class ShellWindows { }

    /// <summary>Desktop item pidls are relative to the desktop root, so they are absolute too.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr pv);

    /// <summary>The desktop's folder view and its DefView window, or null when Explorer is not up.</summary>
    public static (IFolderView View, IntPtr DefView)? Open()
    {
        var windows = (IShellWindows)new ShellWindows();
        object loc = null!, root = null!;   // VT_EMPTY both: the desktop is found by class alone.
        object disp = windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
        if (disp is not IServiceProvider sp) return null;
        Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
        if (sp.QueryService(ref sid, ref iid, out IntPtr pBrowser) != S_OK || pBrowser == IntPtr.Zero) return null;
        try
        {
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
            browser.QueryActiveShellView(out IShellView view);
            view.GetWindow(out IntPtr defView);
            return ((IFolderView)view, defView);
        }
        finally { Marshal.Release(pBrowser); }
    }

    /// <summary>Each item's display name and position (DefView/list view client pixels, see VERDICT.md).</summary>
    public static List<(string Name, POINT At)> Items(IFolderView view)
    {
        view.ItemCount(SVGIO_ALLVIEW, out int n);
        var list = new List<(string, POINT)>(n);
        for (int i = 0; i < n; i++)
        {
            view.Item(i, out IntPtr pidl);
            try
            {
                view.GetItemPosition(pidl, out POINT at);
                string name = "?";
                if (SHGetNameFromIDList(pidl, SIGDN_NORMALDISPLAY, out IntPtr psz) == S_OK)
                {
                    name = Marshal.PtrToStringUni(psz) ?? "?";
                    CoTaskMemFree(psz);
                }
                list.Add((name, at));
            }
            finally { CoTaskMemFree(pidl); }
        }
        return list;
    }
}
