using System.Security.Cryptography;
using System.Text;

namespace Sanduhr.App.Services;

/// <summary>
/// One Sanduhr per data folder. A second launch (two tray icons were seen once) built a second widget,
/// a second fetch transport on the same WebView2 profiles and a second set of browser processes. Now
/// the second launch asks the first to show its widget and exits.
/// <para>
/// The claim is keyed by the data folder, so a dev build on <c>SANDUHR_DEV_PROFILE</c> never collides
/// with the installed app. A launch that finds the first instance quitting (it no longer answers) waits
/// for it to finish, up to the handoff time, and then carries on as the one instance; that is the
/// restart after an update.
/// </para>
/// <para>
/// The mutex belongs to the thread that claimed it: dispose on that thread (Program.Main does).
/// </para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _ack;
    private RegisteredWaitHandle? _listener;
    private volatile bool _accepting = true;
    private bool _disposed;

    /// <summary>True when this process is the one Sanduhr for its data folder.</summary>
    public bool IsPrimary { get; }

    private SingleInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle ack, bool isPrimary)
    {
        _mutex = mutex;
        _show = show;
        _ack = ack;
        IsPrimary = isPrimary;
    }

    /// <summary>The kernel-object name prefix for a data folder: per session, case-insensitive on the path.</summary>
    public static string NameFor(string dataDir)
    {
        var full = System.IO.Path.GetFullPath(dataDir).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
        return @"Local\Sanduhr.Instance." + hash;
    }

    /// <summary>
    /// Claims the data folder. When another instance holds it, asks that one to show itself and waits up
    /// to <paramref name="handoff"/>: it answers (this launch is the duplicate, <see cref="IsPrimary"/> false)
    /// or it exits (this launch takes over).
    /// </summary>
    public static SingleInstance Claim(string dataDir, TimeSpan handoff)
    {
        var name = NameFor(dataDir);
        var mutex = new Mutex(initiallyOwned: false, name + ".Lock");
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        var ack = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Shown");

        bool owns = Take(mutex, TimeSpan.Zero);
        if (!owns)
        {
            show.Set();
            try
            {
                owns = WaitHandle.WaitAny([ack, mutex], handoff) == 1;
            }
            catch (AbandonedMutexException e) when (e.MutexIndex == 1)
            {
                owns = true; // the first instance died holding it: ours now
            }
            if (owns)
                show.Reset(); // nobody is left to answer our own request
        }
        return new SingleInstance(mutex, show, ack, owns);
    }

    private static bool Take(Mutex mutex, TimeSpan wait)
    {
        try { return mutex.WaitOne(wait); }
        catch (AbandonedMutexException) { return true; }
    }

    /// <summary>On the primary: runs <paramref name="onShow"/> (on a pool thread) whenever a second launch asks.</summary>
    public void Listen(Action onShow)
    {
        if (!IsPrimary || _listener is not null)
            return;
        _listener = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) =>
        {
            if (!_accepting)
                return;
            try { onShow(); }
            catch { /* a failed show still answers, so the duplicate exits */ }
            _ack.Set();
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Quitting: stop answering, so a launch in the meantime waits for this one to exit and takes over.</summary>
    public void StopAnswering() => _accepting = false;

    /// <summary>A restart to update that did not happen: answer second launches again.</summary>
    public void ResumeAnswering()
    {
        if (!_disposed)
            _accepting = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accepting = false;
        _listener?.Unregister(null);
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not held on this thread */ }
        }
        _mutex.Dispose();
        _show.Dispose();
        _ack.Dispose();
    }
}
