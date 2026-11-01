using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sanduhr.App.Interop;

/// <summary>
/// The wallpaper blur behind a Glass drawer (drawers look and placement spec, section 1; the Task 0 spike, <c>spikes/drawer-look/VERDICT.md</c>).
/// The documented route (<c>DWMWA_SYSTEMBACKDROP_TYPE</c>) draws flat grey on a Desk window because the window never activates, so this
/// uses the undocumented <c>SetWindowCompositionAttribute</c> (<c>WCA_ACCENT_POLICY = 19</c>) with <c>ACCENT_ENABLE_ACRYLICBLURBEHIND = 4</c>
/// and <c>AccentFlags = 0</c> (flag 2 drew a flat fill). The blur fills the whole window rectangle, so the caller gives a Glass drawer a
/// window exactly its panel's size; <c>DWMWCP_ROUND</c> clips it to DWM's round corner.
///
/// Every call is guarded: a missing entry point, a false return or any exception means no blur, and the drawer keeps its tinted plate.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DrawerBackdrop
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentAcrylicBlurBehind = 4;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmwcpDefault = 0;
    private const int DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public int Attrib;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Blurs what is behind the window and asks DWM to round its corners. <c>Blurred</c> is whether Windows took the blur (false
    /// leaves the window as it was); <c>Rounded</c> is whether DWM took the corner preference (Windows 10 does not, and the blur then fills
    /// a square window).</summary>
    /// <param name="tintAbgr">The accent's gradient color, AABBGGRR. The spike ran it near-clear (alpha 1): the panel's own brush does the tint.</param>
    public static (bool Blurred, bool Rounded) Apply(IntPtr hwnd, int tintAbgr)
    {
        if (hwnd == IntPtr.Zero) return (false, false);
        try
        {
            if (!SetAccent(hwnd, AccentAcrylicBlurBehind, tintAbgr)) return (false, false);
            return (true, SetCorners(hwnd, DwmwcpRound));
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException or SEHException
            or InvalidOperationException or ArgumentException or OverflowException)
        {
            return (false, false);
        }
    }

    /// <summary>Takes the blur and the corner preference off (the drawer's look changed away from Glass). Never throws.</summary>
    public static void Clear(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            SetAccent(hwnd, AccentDisabled, 0);
            SetCorners(hwnd, DwmwcpDefault);
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException or SEHException
            or InvalidOperationException or ArgumentException or OverflowException)
        {
            // nothing was applied that could stay
        }
    }

    /// <summary>The plate's color as the accent's gradient color: rgb of the plate at alpha 1, in AABBGGRR order.</summary>
    public static int NearClearTint(byte r, byte g, byte b) => unchecked((int)(0x01000000u | (uint)(b << 16) | (uint)(g << 8) | r));

    private static bool SetAccent(IntPtr hwnd, int state, int tintAbgr)
    {
        var accent = new AccentPolicy { AccentState = state, AccentFlags = 0, GradientColor = tintAbgr, AnimationId = 0 };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, p, false);
            var data = new WindowCompositionAttribData { Attrib = WcaAccentPolicy, Data = p, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>True when DWM took the preference; a build without the attribute answers an error code and the corners stay square.</summary>
    private static bool SetCorners(IntPtr hwnd, int preference)
    {
        int value = preference;
        return DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref value, sizeof(int)) == 0;
    }
}
