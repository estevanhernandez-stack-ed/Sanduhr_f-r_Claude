using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sanduhr.App.Interop;

/// <summary>
/// Restart to update: asks Windows to relaunch Sanduhr after an update closes it. Windows honours the
/// registration only for a process that has run at least 60 seconds. Hand-written <c>[DllImport]</c>,
/// same pattern as <see cref="JobNative"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RestartNative
{
    // Restart for an update only: not after a crash, a hang or a reboot.
    private const int RESTART_NO_CRASH = 1;
    private const int RESTART_NO_HANG = 2;
    private const int RESTART_NO_REBOOT = 8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? pwzCommandline, int dwFlags);

    [DllImport("kernel32.dll")]
    private static extern int UnregisterApplicationRestart();

    /// <summary>True when Windows took the registration.</summary>
    public static bool RegisterForUpdate()
    {
        try { return RegisterApplicationRestart(null, RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT) == 0; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public static void Unregister()
    {
        try { _ = UnregisterApplicationRestart(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
