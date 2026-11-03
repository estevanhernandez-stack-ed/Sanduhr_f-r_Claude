using System.Runtime.InteropServices;

namespace UnderIcons;

/// <summary>THROWAWAY SPIKE: synthetic right-clicks and Escape for the hook's self-test.</summary>
internal static class Inject
{
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct U { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public U u; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    public static void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        var down = new INPUT { type = 0, u = new U { mi = new MOUSEINPUT { flags = 0x0008 } } };
        var up = new INPUT { type = 0, u = new U { mi = new MOUSEINPUT { flags = 0x0010 } } };
        SendInput(2, [down, up], Marshal.SizeOf<INPUT>());
    }

    public static void Escape()
    {
        var d = new INPUT { type = 1, u = new U { ki = new KEYBDINPUT { vk = 0x1B } } };
        var u = new INPUT { type = 1, u = new U { ki = new KEYBDINPUT { vk = 0x1B, flags = 0x2 } } };
        SendInput(2, [d, u], Marshal.SizeOf<INPUT>());
    }
}
