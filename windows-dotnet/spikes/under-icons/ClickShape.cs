using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace UnderIcons;

/// <summary>
/// THROWAWAY SPIKE, 2026-11-02 part 2: the click layer shaped to the piece minus the desktop icons, so an icon keeps
/// its own clicks and an exposed part of the piece takes a right-click for the Desk menu. Icon rects come from UI
/// Automation (the desktop list view's items), never from reading Explorer's memory.
/// </summary>
internal static class ClickShape
{
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr m, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr m, uint flags, int x, int y, int r, IntPtr h, IntPtr rc);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    private const int RGN_DIFF = 4;
    private const int Pad = 2;

    /// <summary>Visible desktop icons in screen pixels; empty when the icons are hidden.</summary>
    public static List<Rectangle> IconRects(out string note)
    {
        var list = new List<Rectangle>();
        var shell = N.GetShellWindow();
        var def = N.FindWindowExW(shell, IntPtr.Zero, "SHELLDLL_DefView", null);
        var lv = N.FindWindowExW(def, IntPtr.Zero, "SysListView32", null);
        if (lv == IntPtr.Zero) { note = "no desktop list view"; return list; }
        if (!N.IsWindowVisible(lv)) { note = "desktop icons hidden"; return list; }
        var t0 = DateTime.UtcNow;
        var items = AutomationElement.FromHandle(lv).FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement it in items)
        {
            var r = it.Current.BoundingRectangle;
            if (r.IsEmpty || it.Current.IsOffscreen) continue;
            list.Add(new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
        }
        note = $"{list.Count} visible icons of {items.Count} items, read in {(DateTime.UtcNow - t0).TotalMilliseconds:0} ms";
        return list;
    }

    /// <summary>Shapes <paramref name="click"/> (at <paramref name="piece"/> in screen pixels) to the piece minus every
    /// icon rect it overlaps. Returns the icon rects cut out, in screen pixels.</summary>
    public static List<Rectangle> Apply(IntPtr click, Rectangle piece, List<Rectangle> icons)
    {
        var cut = new List<Rectangle>();
        var rgn = CreateRectRgn(0, 0, piece.Width, piece.Height);
        foreach (var ic in icons)
        {
            var r = Rectangle.Inflate(ic, Pad, Pad);
            if (!r.IntersectsWith(piece)) continue;
            cut.Add(ic);
            var hole = CreateRectRgn(r.Left - piece.Left, r.Top - piece.Top, r.Right - piece.Left, r.Bottom - piece.Top);
            CombineRgn(rgn, rgn, hole, RGN_DIFF);
            DeleteObject(hole);
        }
        SetWindowRgn(click, rgn, true);   // the system owns rgn now
        return cut;
    }

    /// <summary>Stand-in for the Desk menu. Returns the chosen label, or null when dismissed.</summary>
    public static string? ShowMenu(IntPtr owner, int x, int y)
    {
        var m = CreatePopupMenu();
        string[] labels = ["Arrange the Desk", "Desk Look…", "Hide the Desk", "Settings…", "Done (end the test)"];
        for (int i = 0; i < labels.Length; i++) AppendMenuW(m, 0, (UIntPtr)(uint)(i + 1), labels[i]);
        SetForegroundWindow(owner);
        int id = TrackPopupMenu(m, 0x0100 /*TPM_RETURNCMD*/ | 0x0002 /*TPM_RIGHTBUTTON*/, x, y, 0, owner, IntPtr.Zero);
        N.PostMessageW(owner, 0, IntPtr.Zero, IntPtr.Zero);   // WM_NULL, so the menu closes cleanly
        DestroyMenu(m);
        return id > 0 ? labels[id - 1] : null;
    }
}
