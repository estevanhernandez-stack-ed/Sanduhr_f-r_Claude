using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Sanduhr.App.Interop;

namespace Sanduhr.App.Desk;

/// <summary>
/// Keyboard focus for a drawer (5.0 drawers spec, section 2; VERDICT.md change 1). Desk windows never activate
/// (<c>WS_EX_NOACTIVATE</c>), so the keyboard could never reach a drawer. A click into one clears the style for the visit and makes the
/// window the foreground one; the host refuses the raise that activation asks for (<c>KeepZOrder</c>, the spike's z-order pin), so the
/// drawer stays under the apps as desktop icons do. A click away gives the style back (<see cref="GaveBack"/>, from the host's hook).
/// </summary>
internal static class DeskFocus
{
    /// <summary>A Desk window gave activation back (the window's handle).</summary>
    public static event Action<IntPtr>? Lost;

    public static bool Take(UIElement target)
    {
        if (PresentationSource.FromVisual(target) is not HwndSource src) return false;
        var h = src.Handle;
        if (DeskNative.GetForegroundWindow() != h)
        {
            int style = DeskNative.GetWindowLong(h, DeskNative.GWL_EXSTYLE);
            DeskNative.SetWindowLongPtr(h, DeskNative.GWL_EXSTYLE, (IntPtr)(style & ~DeskNative.WS_EX_NOACTIVATE));
            DeskNative.SetForegroundWindow(h);
            // Refused (the foreground lock): no WA_INACTIVE will ever come, so give the style back now.
            if (DeskNative.GetForegroundWindow() != h) { GaveBack(h); return false; }
        }
        Keyboard.Focus(target);
        return DeskNative.GetForegroundWindow() == h;
    }

    /// <summary>The window lost activation (or is going away): it never activates again until the next click into a drawer.</summary>
    public static void GaveBack(IntPtr h)
    {
        int style = DeskNative.GetWindowLong(h, DeskNative.GWL_EXSTYLE);
        DeskNative.SetWindowLongPtr(h, DeskNative.GWL_EXSTYLE, (IntPtr)(style | DeskNative.WS_EX_NOACTIVATE));
        Lost?.Invoke(h);
    }
}
