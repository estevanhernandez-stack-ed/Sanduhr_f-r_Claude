using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;

namespace Sanduhr.App.Interop;

/// <summary>
/// DWM backdrop interop — the .NET equivalent of the Python build's
/// <c>mica.py</c> (which called the same DWM attributes via ctypes). Applies
/// Windows 11 <b>Mica</b> to a borderless WPF window plus immersive dark mode +
/// rounded corners, and reports whether the OS actually supports it so the
/// caller can fall back to a solid panel on older Windows.
///
/// Spec ("Glass / Mica") names "WPF-UI <c>SystemBackdrop</c> or CsWin32
/// <c>DwmExtendFrameIntoClientArea</c> + <c>DWMWA_SYSTEMBACKDROP_TYPE</c>".
/// JUDGMENT CALL: hand-written <c>[DllImport]</c> (same pattern as Core's
/// <see cref="Sanduhr.Core.WindowsCredentialManager"/>) is used instead of the
/// CsWin32 source generator so the Mica path compiles deterministically with
/// zero generator config — the CsWin32 package is still referenced per the
/// stack. Real Mica requires a NON-layered window
/// (<c>AllowsTransparency=False</c>); see <c>MainWindow</c> for why that wins
/// over the literal "AllowsTransparency=True" in the task.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MicaHelper
{
    // DWMWINDOWATTRIBUTE values (dwmapi.h).
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // DWM_WINDOW_CORNER_PREFERENCE.
    private const int DWMWCP_ROUND = 2;

    // DWM_SYSTEMBACKDROP_TYPE.
    private const int DWMSBT_NONE = 1;       // No backdrop (solid)
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    // Win11 21H2 = build 22000 (dark mode + rounded corners).
    private const int Win11Build = 22000;
    // DWMWA_SYSTEMBACKDROP_TYPE (the documented Mica toggle) lands in 22H2 = 22621.
    private const int Win11_22H2Build = 22621;

    /// <summary>True when the OS is new enough for the documented Mica backdrop.</summary>
    public static bool IsMicaSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version.Build >= Win11_22H2Build;

    private static bool IsWin11 =>
        Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version.Build >= Win11Build;

    /// <summary>
    /// Apply Mica + dark titlebar + rounded corners to <paramref name="window"/>.
    /// Call from <c>OnSourceInitialized</c> (the HWND must exist). Returns true
    /// when Mica was applied — the caller then keeps a Transparent window
    /// background so the backdrop shows; false means "render the solid fallback".
    /// </summary>
    public static bool TryApplyMica(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        // Dark titlebar/frame so the borderless chrome reads dark (Win11).
        if (IsWin11)
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        if (!IsMicaSupported)
            return false;

        // Sheet-of-glass: extend the DWM frame across the whole client so the
        // backdrop fills it (equivalent to WindowChrome.GlassFrameThickness=-1).
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int backdrop = DWMSBT_MAINWINDOW;
        int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        return hr == 0;
    }

    /// <summary>
    /// Disable the system backdrop so the window renders solid — the Matrix
    /// theme's phosphor-terminal opt-out (parity with <c>mica.disable_mica</c>).
    /// Sets <c>DWMSBT_NONE</c> and collapses the extended frame so no Mica bleeds
    /// at the edges. No-op (harmless DWM error) on Windows without backdrop support.
    /// </summary>
    public static void DisableBackdrop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int backdrop = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
}
