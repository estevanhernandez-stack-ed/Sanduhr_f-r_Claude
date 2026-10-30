using System.Runtime.InteropServices;
using System.Text;

namespace DrawerIsland;

/// <summary>THROWAWAY SPIKE. Explorer's own context menu for one file: IShellItem, BHID_SFUIObject, IContextMenu.</summary>
internal static class ShellMenu
{
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdn, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, StringBuilder commandstring, int cch);
    }

    // v2: IContextMenu2/3, so owner-drawn and lazily filled submenus (Open with, Send to) work.
    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idcmd, uint uflags, IntPtr reserved, StringBuilder commandstring, int cch);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    private static IContextMenu3? s_active;

    /// <summary>Forwards the menu messages to the open Shell menu. True when it took one.</summary>
    public static bool Forward(int msg, IntPtr w, IntPtr l, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (s_active is null || msg is not (0x117 or 0x2B or 0x2C or 0x120)) return false; // INITMENUPOPUP, DRAWITEM, MEASUREITEM, MENUCHAR
        return s_active.HandleMenuMsg2((uint)msg, w, l, out result) == 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow; public uint dwHotKey; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW; public string? lpParametersW; public string? lpDirectoryW; public string? lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr h, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    private const uint CMF_NORMAL = 0, CMF_EXTENDEDVERBS = 0x100, TPM_RETURNCMD = 0x100, CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint First = 1, Last = 0x7FFF;

    /// <summary>Shows the menu at a screen point and runs the chosen verb. Returns the verb's id, or null when dismissed.</summary>
    public static string? Show(IntPtr owner, string path, int x, int y)
    {
        var iidItem = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidItem, out var item);
        var bhid = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"); // BHID_SFUIObject
        var iidMenu = typeof(IContextMenu).GUID;
        item.BindToHandler(IntPtr.Zero, ref bhid, ref iidMenu, out var o);
        var menu = (IContextMenu)o;
        s_active = o as IContextMenu3;
        Program.Log($"menu: IContextMenu3 {(s_active is null ? "no" : "yes")}");
        var h = CreatePopupMenu();
        try
        {
            menu.QueryContextMenu(h, 0, First, Last, CMF_NORMAL);
            // v2: the documented TrackPopupMenu rule: owner in the foreground before, WM_NULL after.
            Program.Log($"menu: SetForegroundWindow {SetForegroundWindow(owner)}");
            uint cmd = TrackPopupMenuEx(h, TPM_RETURNCMD, x, y, owner, IntPtr.Zero);
            PostMessageW(owner, 0, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 0) return null;
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = owner,
                lpVerb = (IntPtr)(cmd - First),
                lpVerbW = (IntPtr)(cmd - First),
                nShow = 1,
                ptInvoke = new POINT { X = x, Y = y },
            };
            menu.InvokeCommand(ref info);
            return $"command {cmd - First}";
        }
        finally { s_active = null; DestroyMenu(h); }
    }
}
