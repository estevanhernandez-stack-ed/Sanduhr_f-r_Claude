using System.Runtime.InteropServices;
using Sanduhr.Core;

namespace Sanduhr.App.Interop;

/// <summary>
/// Where the desktop icons sit, read-only, through the Shell's own desktop folder view (look and placement spec, section 2; the
/// declarations are the drawer-look spike's, verified on build 26220, <c>spikes/drawer-look/VERDICT.md</c>). Out of process to Explorer:
/// <c>IShellWindows.FindWindowSW(SWC_DESKTOP)</c> to <c>IServiceProvider.QueryService(SID_STopLevelBrowser)</c> to
/// <c>IShellBrowser.QueryActiveShellView</c> to <c>IFolderView</c>. Nothing here moves, selects or writes an icon. Every failure
/// (Explorer restarting, icons hidden, a COM error, a thread that is not STA) is a null answer, never an exception.
/// </summary>
internal static class DesktopIcons
{
    /// <summary>The desktop's icons as rectangles in physical screen pixels (virtual-screen coordinates, as monitor bounds are); an empty
    /// list for a desktop with no icons; null when the Shell does not answer.</summary>
    public static IReadOnlyList<PxRect>? Read()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || !ShellAnswers()) return null;
        try { return DeskNative.PerMonitor(ReadCore); }
        catch (Exception) { return null; }   // the Shell can fail in more ways than COM names; a reading is never worth a crash
    }

    /// <summary>How many icons the desktop holds and the size of one icon cell, in physical pixels; null when the Shell does not answer.</summary>
    public static (int Count, PxPoint Cell)? Spacing()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || !ShellAnswers()) return null;
        try
        {
            return DeskNative.PerMonitor<(int, PxPoint)?>(() =>
            {
                using var s = Session.Open();
                if (s is null) return null;
                s.View.ItemCount(Native.SVGIO_ALLVIEW, out int count);
                s.View.GetSpacing(out var cell);
                return cell.X > 0 && cell.Y > 0 ? (count, new PxPoint(cell.X, cell.Y)) : null;
            });
        }
        catch (Exception) { return null; }
    }

    /// <summary>Outgoing COM to a hung Explorer would block this thread (and the Desk) with no timeout: before any call, ask the icons' window
    /// for a no-op and give it a quarter of a second. A hung, missing or restarting Shell is a null reading and the caller falls back.</summary>
    private static bool ShellAnswers()
    {
        try
        {
            var host = DeskNative.IconsHost();
            if (host == IntPtr.Zero) return false;
            return Native.SendMessageTimeoutW(host, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, 0x0002 | 0x0001 /* SMTO_ABORTIFHUNG | SMTO_BLOCK */,
                250, out _) != IntPtr.Zero;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Horizontal room, in device-independent pixels, between a cell's left edge and the icon image in it (the spike measured 14 at 100%).</summary>
    private const int CellPad = 14;

    private static IReadOnlyList<PxRect>? ReadCore()
    {
        using var s = Session.Open();
        if (s is null) return null;
        s.View.ItemCount(Native.SVGIO_ALLVIEW, out int count);
        s.View.GetSpacing(out var cell);
        if (cell.X <= 0 || cell.Y <= 0) return null;   // GetDefaultSpacing is unreliable (VERDICT.md); an unreadable GetSpacing is no reading
        if (count <= 0) return [];
        // Positions are the list view's client pixels: the window that maps them to the screen is the list view, else its DefView.
        var list = Native.FindWindowExW(s.DefView, IntPtr.Zero, "SysListView32", null);
        if (list == IntPtr.Zero) list = s.DefView;
        uint dpi = Native.GetDpiForWindow(list);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var points = new Native.POINT[count];
        int read = 0;
        for (int i = 0; i < count; i++)
        {
            s.View.Item(i, out IntPtr pidl);
            try { s.View.GetItemPosition(pidl, out points[read]); read++; }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }
        if (read == 0) return [];
        // Always map list view client to screen, never assume the origins agree (a monitor left of or above the primary moves them apart).
        if (Native.MapWindowPoints(list, IntPtr.Zero, points, (uint)read) == 0 && Marshal.GetLastWin32Error() != 0) return null;
        int pad = (int)Math.Round(CellPad * scale);
        var rects = new List<PxRect>(read);
        for (int i = 0; i < read; i++) rects.Add(new PxRect(points[i].X - pad, points[i].Y, cell.X, cell.Y));
        return rects;
    }

    /// <summary>The Shell objects one reading holds, released together.</summary>
    private sealed class Session : IDisposable
    {
        private readonly List<object> _com = [];
        public Native.IFolderView View { get; private set; } = null!;
        public IntPtr DefView { get; private set; }

        public static Session? Open()
        {
            var s = new Session();
            try
            {
                var windows = (Native.IShellWindows)new Native.ShellWindows();
                s._com.Add(windows);
                object loc = null!, root = null!;   // VT_EMPTY both: the desktop is found by class alone
                object disp = windows.FindWindowSW(ref loc, ref root, Native.SWC_DESKTOP, out _, Native.SWFO_NEEDDISPATCH);
                if (disp is null) { s.Dispose(); return null; }
                s._com.Add(disp);
                if (disp is not Native.IServiceProvider sp) { s.Dispose(); return null; }
                Guid sid = Native.SID_STopLevelBrowser, iid = Native.IID_IShellBrowser;
                if (sp.QueryService(ref sid, ref iid, out IntPtr pBrowser) != 0 || pBrowser == IntPtr.Zero) { s.Dispose(); return null; }
                try
                {
                    var browser = (Native.IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
                    s._com.Add(browser);
                    browser.QueryActiveShellView(out Native.IShellView view);
                    s._com.Add(view);
                    view.GetWindow(out IntPtr defView);
                    s.DefView = defView;
                    s.View = (Native.IFolderView)view;
                }
                finally { Marshal.Release(pBrowser); }
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            for (int i = _com.Count - 1; i >= 0; i--)
                if (Marshal.IsComObject(_com[i])) Marshal.FinalReleaseComObject(_com[i]);
            _com.Clear();
        }
    }

    /// <summary>The spike's verified declarations (<c>spikes/drawer-look/DesktopIconsNative.cs</c>). Method order is shobjidl_core.h / exdisp.h order;
    /// every slot before the last one called is declared, so the vtable lines up.</summary>
    private static class Native
    {
        public const int SWC_DESKTOP = 8;
        public const int SWFO_NEEDDISPATCH = 1;
        public const uint SVGIO_ALLVIEW = 0x00000002;

        public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        /// <summary>exdisp.h. Dual; the IDispatch slots come first and are supplied by the runtime.</summary>
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IShellWindows
        {
            int get_Count();
            [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
            [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
            int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
            int RegisterPending(int lThreadId, ref object pvarloc, ref object pvarlocRoot, int swClass);
            void Revoke(int lCookie);
            void OnNavigate(int lCookie, ref object pvarLoc);
            void OnActivated(int lCookie, [MarshalAs(UnmanagedType.VariantBool)] bool fActive);
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(ref object pvarLoc, ref object pvarLocRoot, int swClass, out int pHWND, int swfwOptions);
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

        /// <summary>shobjidl_core.h. Derives IOleWindow; only GetWindow is called (the SHELLDLL_DefView hwnd). The rest of the vtable is not
        /// declared, so never call past ContextSensitiveHelp.</summary>
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
            [PreserveSig] int GetAutoArrange();
            void SelectItem(int iItem, uint dwFlags);
            void SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
        }

        [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
        public class ShellWindows { }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string? title);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int MapWindowPoints(IntPtr from, IntPtr to, [In, Out] POINT[] points, uint count);
    }
}
