using System.Runtime.InteropServices;

namespace Sanduhr.App.Interop;

/// <summary>
/// Explorer's menus in dark mode when Windows is (VERDICT.md, change 4): uxtheme's undocumented ordinals 135
/// (<c>SetPreferredAppMode</c>, 1 = follow the system) and 136 (<c>FlushMenuThemes</c>), once at start. Guarded: a Windows without them
/// keeps light menus. Only Win32 menus follow it; Sanduhr's own WPF menus are themed already.
/// </summary>
internal static class DarkMenus
{
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();

    public static bool Enable()
    {
        if (Environment.OSVersion.Version.Build < 17763) return false;
        try
        {
            SetPreferredAppMode(1);
            FlushMenuThemes();
            return true;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { return false; }
    }
}
