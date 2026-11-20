using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sanduhr.App.Interop;

/// <summary>
/// The machine's real processor architecture (5.0 spec 7). An x64 Sanduhr on an Arm PC runs through Windows'
/// emulation, and IsWow64Process2 is the call that tells an emulated process what the machine really is.
/// Hand-written <c>[DllImport]</c>, same pattern as <see cref="RestartNative"/>. Windows 10 1709 and later;
/// Sanduhr's floor is 1809.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MachineNative
{
    private const ushort IMAGE_FILE_MACHINE_I386 = 0x014C;
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
    private const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

    /// <summary>The machine's architecture, or null when Windows would not say.</summary>
    public static Architecture? Native()
    {
        try
        {
            if (!IsWow64Process2(GetCurrentProcess(), out _, out var native))
                return null;
            return native switch
            {
                IMAGE_FILE_MACHINE_ARM64 => Architecture.Arm64,
                IMAGE_FILE_MACHINE_AMD64 => Architecture.X64,
                IMAGE_FILE_MACHINE_I386 => Architecture.X86,
                _ => null,
            };
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
