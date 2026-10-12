using System.IO;
using System.Runtime.InteropServices;

namespace Sanduhr.App.Interop;

/// <summary>
/// The Desk's local readings (spec 6.4, 6.6, 6.7), by hand-written interop as <see cref="DeskNative"/>:
/// CPU, memory and disk from kernel32; the system output peak level from the Core Audio endpoint meter
/// (<c>IAudioMeterInformation</c>, a level only: nothing here captures or records audio); and the
/// wallpaper's path, placement and background color from <c>IDesktopWallpaper</c> (the wallpaper file,
/// never the screen). Every call is cheap, never throws to the caller, and reports failure as null.
/// </summary>
internal static class DeskSensorsNative
{
    // ---- CPU, memory, disk ----

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string dir, out ulong freeToCaller, out ulong total, out ulong totalFree);

    /// <summary>Idle, kernel (idle included) and user time since boot, in 100 ns ticks.</summary>
    public static (ulong Idle, ulong Kernel, ulong User)? SystemTimes() =>
        GetSystemTimes(out var i, out var k, out var u) ? (i.Value, k.Value, u.Value) : null;

    /// <summary>The share of physical memory in use, 0 to 100, as Task Manager reports it.</summary>
    public static uint? MemoryLoad()
    {
        var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref s) ? s.dwMemoryLoad : null;
    }

    /// <summary>Total and free bytes of the system drive (the Windows folder's root).</summary>
    public static (ulong Total, ulong Free)? SystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
            return GetDiskFreeSpaceExW(root, out _, out var total, out var free) ? (total, free) : null;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            return null;
        }
    }

    // ---- The output peak meter ----

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    private const int eRender = 0;
    private const int eConsole = 0;
    private const int CLSCTX_ALL = 0x17;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    /// <summary>
    /// The default playback device's peak meter: the loudest sample level of what is playing, 0 to 1,
    /// as the volume mixer's bar shows it. It reads a level only; it opens no audio stream, so nothing
    /// is captured, recorded or heard. Re-acquired when a read fails (the default device changed or
    /// went away). Release it when nothing plays.
    /// </summary>
    internal sealed class PeakMeter : IDisposable
    {
        private IAudioMeterInformation? _meter;
        private DateTime _retryAfter = DateTime.MinValue;

        /// <summary>The peak now, or null when no output device is available.</summary>
        public float? Read()
        {
            if (_meter is null && !Acquire()) return null;
            try
            {
                if (_meter!.GetPeakValue(out var peak) == 0) return peak;
            }
            catch (Exception e) when (e is COMException or InvalidComObjectException) { }
            Release();
            return Acquire() && _meter!.GetPeakValue(out var again) == 0 ? again : null;
        }

        private bool Acquire()
        {
            if (DateTime.UtcNow < _retryAfter) return false;
            try
            {
                var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                if (type is null || Activator.CreateInstance(type) is not IMMDeviceEnumerator en) return Fail();
                try
                {
                    if (en.GetDefaultAudioEndpoint(eRender, eConsole, out var device) != 0 || device is null) return Fail();
                    try
                    {
                        var iid = IID_IAudioMeterInformation;
                        if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var o) != 0 || o is not IAudioMeterInformation m)
                            return Fail();
                        _meter = m;
                        return true;
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                finally { Marshal.ReleaseComObject(en); }
            }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException)
            {
                return Fail();
            }
        }

        // No device (or Core Audio unavailable): try again in a few seconds rather than every frame.
        private bool Fail()
        {
            _retryAfter = DateTime.UtcNow.AddSeconds(5);
            return false;
        }

        private void Release()
        {
            if (_meter is not null)
            {
                try { Marshal.ReleaseComObject(_meter); } catch (ArgumentException) { }
                _meter = null;
            }
        }

        public void Dispose() => Release();
    }

    // ---- The wallpaper ----

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out RECT rect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(int position);
        [PreserveSig] int GetPosition(out int position);
    }

    private static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    /// <summary>One monitor's wallpaper as Windows draws it: the monitor's rect in physical pixels, the
    /// image file (null for a plain color or when none is set) and the position mode (Windows uses one
    /// mode for every monitor).</summary>
    internal sealed record WallpaperMonitor(int Left, int Top, int Right, int Bottom, string? Path);

    /// <summary>What <c>IDesktopWallpaper</c> reports: each monitor, the position mode and the
    /// background color as 0x00BBGGRR. Null when the shell's object is unavailable.</summary>
    public static (IReadOnlyList<WallpaperMonitor> Monitors, int Position, uint Background)? Wallpaper()
    {
        object? o = null;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_DesktopWallpaper);
            o = type is null ? null : Activator.CreateInstance(type);
            if (o is not IDesktopWallpaper w) return null;
            var list = new List<WallpaperMonitor>();
            return DeskNative.PerMonitor(() =>
            {
                if (w.GetMonitorDevicePathCount(out var count) != 0) return ((IReadOnlyList<WallpaperMonitor>, int, uint)?)null;
                for (uint i = 0; i < count; i++)
                {
                    if (w.GetMonitorDevicePathAt(i, out var id) != 0 || id is null) continue;
                    if (w.GetMonitorRECT(id, out var r) != 0) continue; // a detached monitor
                    string? path = w.GetWallpaper(id, out var p) == 0 && !string.IsNullOrWhiteSpace(p) ? p : null;
                    list.Add(new WallpaperMonitor(r.Left, r.Top, r.Right, r.Bottom, path));
                }
                int pos = w.GetPosition(out var mode) == 0 ? mode : 4; // Fill, Windows' default
                uint bg = w.GetBackgroundColor(out var c) == 0 ? c : 0;
                return (list, pos, bg);
            });
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException)
        {
            return null;
        }
        finally
        {
            if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }
}
