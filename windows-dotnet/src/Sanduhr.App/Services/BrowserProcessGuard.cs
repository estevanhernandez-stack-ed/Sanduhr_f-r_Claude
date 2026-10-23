using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32.SafeHandles;
using Sanduhr.App.Interop;

namespace Sanduhr.App.Services;

/// <summary>
/// Keeps WebView2's browser processes from outliving Sanduhr. An <c>msedgewebview2.exe</c> left running
/// after Sanduhr quits still carries the package identity, and Windows then refuses to update the
/// package (0x80073D02, "the resources it modifies are in use"). Seen 2026-10-22 on 3.7.1.
/// <para>
/// Two layers. Every browser process Sanduhr starts (the usage transport, each inactive account's
/// check, the sign-in window) is <see cref="Adopt(CoreWebView2)">adopted</see> into a job object that
/// ends its processes when the job's last handle closes, which Windows does when Sanduhr exits by any
/// road: Quit, a crash, End task. On a normal quit, <see cref="WaitForExitAsync"/> first gives each
/// browser a bounded time to close by itself after its controllers are disposed (so its profile is
/// flushed), then ends the ones still running.
/// </para>
/// <para>
/// Only processes this guard adopted are ever ended, and only while they still carry the name they were
/// adopted with, so a reused process id never ends someone else's process.
/// </para>
/// </summary>
public sealed class BrowserProcessGuard : IDisposable
{
    /// <summary>The guard for this process's WebView2 browsers.</summary>
    public static BrowserProcessGuard Current { get; } = new(LogToFile);

    // Operation and counts only, never a path or a profile name.
    private static void LogToFile(string message)
    {
        try { System.IO.File.AppendAllText(new Sanduhr.Core.Paths().LogFile, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}"); }
        catch { /* diagnostics never break a quit */ }
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, (Process Process, string Name)> _adopted = new();
    private readonly Action<string>? _log;
    private SafeFileHandle? _job;
    private bool _jobTried;
    private bool _disposed;

    public BrowserProcessGuard(Action<string>? log = null) => _log = log;

    /// <summary>Set once Sanduhr is quitting: nothing new should start a browser after it.</summary>
    public bool ShuttingDown { get; private set; }

    /// <summary>How many adopted browser processes are still running.</summary>
    public int RunningCount
    {
        get
        {
            lock (_gate)
            {
                Prune();
                return _adopted.Count;
            }
        }
    }

    /// <summary>Adopts the browser process behind <paramref name="core"/>. Never throws.</summary>
    public void Adopt(CoreWebView2 core)
    {
        int pid;
        try { pid = (int)core.BrowserProcessId; }
        catch (Exception e)
        {
            _log?.Invoke($"webview2 guard: no browser id ({e.GetType().Name})");
            return;
        }
        Adopt(pid);
    }

    /// <summary>Adopts process <paramref name="pid"/>: tracked for the quit, and put in the kill-on-close job. Never throws.</summary>
    public bool Adopt(int pid)
    {
        Process process;
        string name;
        try
        {
            process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false; // already gone
        }

        lock (_gate)
        {
            if (_disposed)
            {
                process.Dispose();
                return false;
            }
            if (_adopted.ContainsKey(pid))
            {
                process.Dispose();
                return true;
            }
            _adopted[pid] = (process, name);
            var job = JobLocked();
            bool inJob = false;
            try { inJob = job is not null && JobNative.Assign(job, process.Handle); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (!inJob)
                _log?.Invoke("webview2 guard: browser not in job (ends on quit only)");
            return true;
        }
    }

    /// <summary>Marks the quit: <see cref="ShuttingDown"/> turns true.</summary>
    public void BeginShutdown() => ShuttingDown = true;

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for every adopted browser to exit by itself, then ends the ones
    /// still running. Returns how many had to be ended. Call after the WebView2 controllers are disposed.
    /// </summary>
    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Process[] running;
            lock (_gate)
            {
                Prune();
                running = _adopted.Values.Select(a => a.Process).ToArray();
            }
            if (running.Length == 0)
                return 0;
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                break;
            using var cts = new CancellationTokenSource(left);
            try { await Task.WhenAll(running.Select(p => p.WaitForExitAsync(cts.Token))).ConfigureAwait(true); }
            catch (OperationCanceledException) { break; }
            catch (InvalidOperationException) { /* one was disposed; prune and look again */ }
        }
        return EndSurvivors();
    }

    /// <summary>Synchronous form for paths that cannot await (Exit): waits up to <paramref name="timeout"/>, then ends survivors.</summary>
    public int WaitForExit(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Process[] running;
        lock (_gate)
        {
            Prune();
            running = _adopted.Values.Select(a => a.Process).ToArray();
        }
        foreach (var p in running)
        {
            var left = deadline - DateTime.UtcNow;
            try { p.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return EndSurvivors();
    }

    /// <summary>Ends every adopted process still running under its adopted name. Returns how many it ended.</summary>
    private int EndSurvivors()
    {
        int ended = 0;
        lock (_gate)
        {
            Prune();
            foreach (var (pid, (process, name)) in _adopted.ToArray())
            {
                try
                {
                    process.Refresh();
                    if (!process.HasExited && process.ProcessName == name)
                    {
                        process.Kill(entireProcessTree: true);
                        ended++;
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                process.Dispose();
                _adopted.Remove(pid);
            }
        }
        if (ended > 0)
            _log?.Invoke($"webview2 guard: ended {ended} browser process(es) that outlived quit");
        return ended;
    }

    /// <summary>Drops processes that have exited. Caller holds <see cref="_gate"/>.</summary>
    private void Prune()
    {
        foreach (var (pid, (process, _)) in _adopted.ToArray())
        {
            bool exited;
            try { exited = process.HasExited; }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { exited = true; }
            if (exited)
            {
                process.Dispose();
                _adopted.Remove(pid);
            }
        }
    }

    private SafeFileHandle? JobLocked()
    {
        if (!_jobTried)
        {
            _jobTried = true;
            try { _job = JobNative.CreateKillOnCloseJob(); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { _job = null; }
            if (_job is null)
                _log?.Invoke("webview2 guard: no job object");
        }
        return _job;
    }

    /// <summary>Closes the job, which ends any adopted process still in it. Exiting the process does the same.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _job?.Dispose();
            _job = null;
            foreach (var (process, _) in _adopted.Values)
                process.Dispose();
            _adopted.Clear();
        }
    }
}
