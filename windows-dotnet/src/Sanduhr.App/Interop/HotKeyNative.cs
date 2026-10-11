using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sanduhr.App.Interop;

/// <summary>
/// <c>RegisterHotKey</c> / <c>UnregisterHotKey</c> for the global shortcut. Hand-written
/// <c>[DllImport]</c>, same pattern as <see cref="MicaHelper"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HotKeyNative
{
    /// <summary>Holding the keys down does not repeat WM_HOTKEY.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;

    /// <summary>Another app (or this one) already owns the combo.</summary>
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    /// <summary>Parent handle of a message-only window.</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
