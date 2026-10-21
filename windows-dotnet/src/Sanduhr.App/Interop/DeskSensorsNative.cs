using System.IO;
using System.Runtime.InteropServices;
using Sanduhr.Core;

namespace Sanduhr.App.Interop;

/// <summary>
/// The Desk's local readings (spec 6.4, 6.6, 6.7), by hand-written interop as <see cref="DeskNative"/>:
/// CPU, memory and disk from kernel32; the playing app's output peak level from its Core Audio session
/// meter (<c>IAudioSessionManager2</c> and <c>IAudioMeterInformation</c>, a level only: nothing here
/// captures or records audio); and the
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

    // ---- The playing app's peak meter ----

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private const int eRender = 0;
    private const int eConsole = 0;
    private const int CLSCTX_ALL = 0x17;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

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

    // IAudioSessionManager2 derives from IAudioSessionManager: its two methods hold their slots.
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags, out IntPtr audioVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    // IAudioSessionControl2 derives from IAudioSessionControl: its nine methods hold their slots.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, char[] name, ref uint size);

    /// <summary>A process's image name without the path or ".exe" ("Spotify", "chrome"), null when it
    /// cannot be read (gone, protected).</summary>
    private static string? ProcessName(uint pid)
    {
        if (pid == 0) return null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return QueryFullProcessImageNameW(h, 0, buffer, ref size)
                ? Path.GetFileNameWithoutExtension(new string(buffer, 0, (int)size))
                : null;
        }
        catch (ArgumentException) { return null; }
        finally { CloseHandle(h); }
    }

    /// <summary>
    /// The peak level of the app that is playing, from the default output device's audio sessions (the
    /// volume mixer's per-app bars). Each session is one app's stream; this reads each session's meter
    /// (a level only: no stream is opened, so nothing is captured, recorded or heard), skips the
    /// "System Sounds" session (chimes and dings) and Sanduhr's own, and hands the rest to
    /// <see cref="AudioSessionMatch.Pick"/>: the sessions of the playing app, or when none is found the
    /// loudest of the others. The device's whole-output meter is never read. The session list is read
    /// again each second (a player's session appears when it starts; the default device may change),
    /// the meters every frame. Release it when nothing plays.
    /// </summary>
    internal sealed class SessionPeakMeter : IDisposable
    {
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(1);
        private readonly uint _ownPid = (uint)Environment.ProcessId;
        private readonly List<Entry> _entries = [];
        private DateTime _refreshAt = DateTime.MinValue;
        private bool _device;

        private sealed record Entry(IAudioSessionControl2 Control, IAudioMeterInformation Meter, string? Name, bool SystemSounds, bool Own);

        /// <summary>The level for the app with this AppUserModelId now, and whether its sessions were
        /// found; null when no output device is available.</summary>
        public AudioSessionPick? Read(string? appId)
        {
            if (DateTime.UtcNow >= _refreshAt) Refresh();
            if (!_device) return null;
            var readings = new List<AudioSessionReading>(_entries.Count);
            bool stale = false;
            foreach (var e in _entries)
            {
                try
                {
                    if (e.Meter.GetPeakValue(out var peak) == 0)
                        readings.Add(new AudioSessionReading(e.Name, e.SystemSounds, e.Own, peak));
                }
                catch (Exception ex) when (ex is COMException or InvalidComObjectException) { stale = true; }
            }
            if (stale) _refreshAt = DateTime.MinValue; // a session went away under us: list again next frame
            return AudioSessionMatch.Pick(appId, readings);
        }

        private void Refresh()
        {
            ReleaseEntries();
            _refreshAt = DateTime.UtcNow + RefreshEvery;
            _device = false;
            object? enumerator = null;
            IMMDevice? device = null;
            try
            {
                var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                if (type is null || (enumerator = Activator.CreateInstance(type)) is not IMMDeviceEnumerator en) return;
                if (en.GetDefaultAudioEndpoint(eRender, eConsole, out device) != 0 || device is null) return;
                var iid = IID_IAudioSessionManager2;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var o) != 0 || o is not IAudioSessionManager2 manager) return;
                _device = true;
                try
                {
                    if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions is null) return;
                    try
                    {
                        if (sessions.GetCount(out int count) != 0) return;
                        for (int i = 0; i < count; i++)
                        {
                            if (sessions.GetSession(i, out var control) != 0 || control is null) continue;
                            Add(control);
                        }
                    }
                    finally { Marshal.ReleaseComObject(sessions); }
                }
                finally { Marshal.ReleaseComObject(manager); }
            }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException)
            {
                // Core Audio unavailable or the device went away mid-read: no sessions until the next refresh.
            }
            finally
            {
                if (device is not null) Marshal.ReleaseComObject(device);
                if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
            }
        }

        private void Add(IAudioSessionControl2 control)
        {
            try
            {
                // The session object also answers as its own meter.
                if (control is not IAudioMeterInformation meter) { Marshal.ReleaseComObject(control); return; }
                bool system = control.IsSystemSoundsSession() == 0; // S_OK: it is the System Sounds session
                control.GetProcessId(out var pid);
                _entries.Add(new Entry(control, meter, system ? null : ProcessName(pid), system, pid == _ownPid));
            }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException)
            {
                try { Marshal.ReleaseComObject(control); } catch (ArgumentException) { }
            }
        }

        private void ReleaseEntries()
        {
            foreach (var e in _entries)
            {
                try { Marshal.ReleaseComObject(e.Control); } catch (ArgumentException) { }
            }
            _entries.Clear();
        }

        public void Dispose()
        {
            ReleaseEntries();
            _device = false;
            _refreshAt = DateTime.MinValue;
        }
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
