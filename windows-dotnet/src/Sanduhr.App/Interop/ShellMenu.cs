using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.App.Interop;

/// <summary>One of Sanduhr's own items under Explorer's menu (Rename drawer, Fold, Hide, Drawer settings…).</summary>
internal sealed record ShellMenuItem(int Id, string Text);

/// <summary>What was picked: the Shell's canonical verb ("open", "rename", "delete"; null for one without), or one of ours.</summary>
internal sealed record ShellMenuChoice(string? Verb, int? Own);

/// <summary>
/// Explorer's own context menu for an item (spec section 2), with Sanduhr's items below a separator, or ours alone on empty space.
/// The spike's three rules (VERDICT.md): the owner goes to the foreground before <c>TrackPopupMenuEx</c> and gets <c>WM_NULL</c>
/// after, or every pick is dismissed; and <c>WM_INITMENUPOPUP</c>, <c>WM_DRAWITEM</c>, <c>WM_MEASUREITEM</c> and <c>WM_MENUCHAR</c>
/// go to <c>IContextMenu3</c> (<see cref="Forward"/>, from the Desk window's hook), or Open with and Send to stay empty.
/// </summary>
internal static class ShellMenu
{
    public const int OwnFirst = 0x8000;
    private const uint First = 1, Last = 0x7FFF;
    private const uint CMF_NORMAL = 0, CMF_CANRENAME = 0x10, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000, GCS_VERBW = 4, MF_STRING = 0, MF_SEPARATOR = 0x800;

    [ThreadStatic] private static ShellNative.IContextMenu3? t_open;

    /// <summary>Hands a menu message to the open Shell menu. True when it took it (the hook then marks it handled).</summary>
    public static bool Forward(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (t_open is null || msg is not (0x117 or 0x2B or 0x2C or 0x120)) return false;
        return t_open.HandleMenuMsg2((uint)msg, wParam, lParam, out result) == 0;
    }

    /// <summary>
    /// Shows the menu at a screen point and runs the pick. A Shell verb <paramref name="takeVerb"/> claims (true) is handed back
    /// instead of run (the drawer renames inline). Null when dismissed or when there is nothing to show.
    /// </summary>
    public static ShellMenuChoice? Show(IntPtr owner, string? path, int x, int y, IReadOnlyList<ShellMenuItem> extras, Func<string, bool> takeVerb)
    {
        ShellNative.IContextMenu? menu = null;
        var h = ShellNative.CreatePopupMenu();
        try
        {
            if (path is not null)
            {
                try
                {
                    var bhid = ShellNative.BHID_SFUIObject;
                    var iid = typeof(ShellNative.IContextMenu).GUID;
                    ShellNative.Item(path).BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var o);
                    menu = (ShellNative.IContextMenu)o;
                    t_open = o as ShellNative.IContextMenu3;
                    menu.QueryContextMenu(h, 0, First, Last, CMF_NORMAL | CMF_CANRENAME);
                }
                catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException) { menu = null; }
            }
            if (extras.Count > 0 && ShellNative.GetMenuItemCount(h) > 0) ShellNative.AppendMenuW(h, MF_SEPARATOR, UIntPtr.Zero, null);
            foreach (var item in extras) ShellNative.AppendMenuW(h, MF_STRING, (UIntPtr)(uint)item.Id, item.Text);
            if (ShellNative.GetMenuItemCount(h) == 0) return null;

            DeskNative.SetForegroundWindow(owner);
            uint cmd = ShellNative.TrackPopupMenuEx(h, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, owner, IntPtr.Zero);
            ShellNative.PostMessageW(owner, 0, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 0) return null;
            if (cmd >= OwnFirst) return new ShellMenuChoice(null, (int)cmd);
            if (menu is null) return null;

            var verb = Verb(menu, cmd - First);
            if (verb is not null && takeVerb(verb)) return new ShellMenuChoice(verb, null);
            Invoke(menu, owner, (IntPtr)(cmd - First), (IntPtr)(cmd - First), x, y);
            return new ShellMenuChoice(verb, null);
        }
        finally
        {
            t_open = null;
            ShellNative.DestroyMenu(h);
            if (menu is not null) Marshal.FinalReleaseComObject(menu);
        }
    }

    /// <summary>Runs one verb on an item without showing a menu (the Delete key: Explorer's own delete, to the Recycle Bin).</summary>
    public static bool InvokeVerb(IntPtr owner, string path, string verb)
    {
        ShellNative.IContextMenu? menu = null;
        var h = ShellNative.CreatePopupMenu();
        IntPtr ansi = Marshal.StringToHGlobalAnsi(verb), wide = Marshal.StringToHGlobalUni(verb);
        try
        {
            var bhid = ShellNative.BHID_SFUIObject;
            var iid = typeof(ShellNative.IContextMenu).GUID;
            ShellNative.Item(path).BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var o);
            menu = (ShellNative.IContextMenu)o;
            menu.QueryContextMenu(h, 0, First, Last, CMF_NORMAL);
            return Invoke(menu, owner, ansi, wide, 0, 0) >= 0;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException) { return false; }
        finally
        {
            Marshal.FreeHGlobal(ansi);
            Marshal.FreeHGlobal(wide);
            ShellNative.DestroyMenu(h);
            if (menu is not null) Marshal.FinalReleaseComObject(menu);
        }
    }

    private static string? Verb(ShellNative.IContextMenu menu, uint offset)
    {
        var sb = new StringBuilder(64);
        return menu.GetCommandString((UIntPtr)offset, GCS_VERBW, IntPtr.Zero, sb, sb.Capacity) == 0 && sb.Length > 0
            ? sb.ToString().ToLowerInvariant() : null;
    }

    private static int Invoke(ShellNative.IContextMenu menu, IntPtr owner, IntPtr verb, IntPtr verbW, int x, int y)
    {
        var info = new ShellNative.CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<ShellNative.CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
            hwnd = owner,
            lpVerb = verb,
            lpVerbW = verbW,
            nShow = 1,
            ptInvoke = new ShellNative.POINT { X = x, Y = y },
        };
        return menu.InvokeCommand(ref info);
    }
}
