using System.Diagnostics;
using System.IO.MemoryMappedFiles;
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
/// A second launch with <c>--settings &lt;page&gt;[#&lt;anchor&gt;]</c> hands that link over before it asks: it writes
/// the link into a small named mailbox (<c>.Message</c>, shared memory, one writer at a time under <c>.Post</c>) and the
/// first instance reads and clears it when it answers. Best effort, and it never stops or delays a launch: every
/// mailbox step catches everything, a busy <c>.Post</c> is skipped rather than waited for, and the whole hand-off
/// (post, signal, wait for the answer) runs under one deadline. Without a mailbox the first just shows its widget.
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
    private readonly MemoryMappedFile? _mailbox;
    private RegisteredWaitHandle? _listener;
    private volatile bool _accepting = true;
    private bool _disposed;

    /// <summary>The mailbox's size: a 32-bit length, then that many UTF-8 bytes.</summary>
    private const int MailboxBytes = 1024;

    /// <summary>The longest link a second launch hands over; a longer one is dropped, never cut.</summary>
    public const int MaxMessageBytes = MailboxBytes - sizeof(int);

    /// <summary>
    /// The one deadline a production second launch passes to <see cref="Claim"/>: its whole hand-off (posting the link,
    /// signalling, waiting for the answer or for a quitting instance to exit) fits in this, never a sum of waits.
    /// </summary>
    public static readonly TimeSpan HandoffDeadline = TimeSpan.FromSeconds(15);

    /// <summary>How long a second launch waits for the <c>.Post</c> mutex: not at all. Busy means skip the post.</summary>
    private static readonly TimeSpan PostWait = TimeSpan.Zero;

    /// <summary>True when this process is the one Sanduhr for its data folder.</summary>
    public bool IsPrimary { get; }

    private SingleInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle ack, bool isPrimary, MemoryMappedFile? mailbox)
    {
        _mutex = mutex;
        _show = show;
        _ack = ack;
        IsPrimary = isPrimary;
        _mailbox = mailbox;
    }

    /// <summary>The kernel-object name prefix for a data folder: per session, case-insensitive on the path.</summary>
    public static string NameFor(string dataDir)
    {
        var full = System.IO.Path.GetFullPath(dataDir).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
        return @"Local\Sanduhr.Instance." + hash;
    }

    /// <summary>
    /// Claims the data folder. When another instance holds it, hands it <paramref name="message"/> (a settings link from
    /// <c>--settings</c>, or null), asks it to show itself and waits: it answers (this launch is the duplicate,
    /// <see cref="IsPrimary"/> false) or it exits (this launch takes over). The posting, the signal and the wait all run
    /// under <paramref name="handoff"/> as one deadline, started on entry.
    /// </summary>
    public static SingleInstance Claim(string dataDir, TimeSpan handoff, string? message = null)
    {
        var clock = Stopwatch.StartNew();
        var name = NameFor(dataDir);
        var mutex = new Mutex(initiallyOwned: false, name + ".Lock");
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        var ack = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Shown");

        MemoryMappedFile? mailbox = null;
        bool owns = Take(mutex, TimeSpan.Zero);
        if (owns)
        {
            mailbox = OpenMailbox(name);
            ClearStale(name, mailbox);
        }
        else
        {
            // One second launch at a time writes the mailbox, and keeps it until the first instance has answered.
            // A busy .Post (another launch is mid-hand-off, or a quit is in progress) is skipped: this launch then
            // asks plainly and the first instance just shows its widget.
            Mutex? post = null;
            bool posting = false;
            try
            {
                post = new Mutex(initiallyOwned: false, name + ".Post");
                posting = Take(post, PostWait);
            }
            catch { /* best effort; never stops a launch */ }
            try
            {
                if (posting)
                    Post(name, message);
                show.Set();
                try
                {
                    var left = handoff - clock.Elapsed;
                    owns = WaitHandle.WaitAny([ack, mutex], left > TimeSpan.Zero ? left : TimeSpan.Zero) == 1;
                }
                catch (AbandonedMutexException e) when (e.MutexIndex == 1)
                {
                    owns = true; // the first instance died holding it: ours now
                }
                if (owns)
                {
                    show.Reset(); // nobody is left to answer our own request
                    mailbox = OpenMailbox(name);
                    // What this launch posted to an instance that then quit is not a request to itself. Cleared while
                    // .Post is still ours, so it cannot wipe a link another launch posts right after we let go. Without
                    // .Post (it was busy) nothing here is ours to clear.
                    if (posting)
                        Clear(mailbox);
                }
            }
            finally
            {
                if (posting)
                {
                    try { post!.ReleaseMutex(); }
                    catch { /* best effort */ }
                }
                try { post?.Dispose(); }
                catch { /* best effort */ }
            }
        }
        return new SingleInstance(mutex, show, ack, owns, mailbox);
    }

    /// <summary>
    /// A new first instance wipes a link left by an earlier run, but only while it holds <c>.Post</c>, taken without
    /// waiting: a second launch that already holds it may have just posted, and its link must survive. When .Post is busy
    /// the clear is skipped and a stale link is the harmless case the mailbox already tolerates.
    /// </summary>
    private static void ClearStale(string name, MemoryMappedFile? mailbox)
    {
        Mutex? post = null;
        bool held = false;
        try
        {
            post = new Mutex(initiallyOwned: false, name + ".Post");
            held = Take(post, PostWait);
            if (held)
                Clear(mailbox);
        }
        catch { /* best effort; never stops a launch */ }
        finally
        {
            if (held)
            {
                try { post!.ReleaseMutex(); }
                catch { /* best effort */ }
            }
            try { post?.Dispose(); }
            catch { /* best effort */ }
        }
    }

    // The mailbox is best effort end to end: a smaller or foreign mapping under the name, a denied open or a dead
    // view all throw different things, and none of them may stop Sanduhr from starting or answering.
    private static MemoryMappedFile? OpenMailbox(string name)
    {
        try { return MemoryMappedFile.CreateOrOpen(name + ".Message", MailboxBytes); }
        catch { return null; }
    }

    /// <summary>A second launch's write; a first instance without a mailbox simply shows its widget.</summary>
    private static void Post(string name, string? message)
    {
        try
        {
            using var mailbox = MemoryMappedFile.OpenExisting(name + ".Message", MemoryMappedFileRights.ReadWrite);
            Write(mailbox, message);
        }
        catch { /* best effort; never stops a launch */ }
    }

    private static void Write(MemoryMappedFile? mailbox, string? message)
    {
        if (mailbox is null)
            return;
        byte[] bytes = message is null ? [] : Encoding.UTF8.GetBytes(message);
        if (bytes.Length > MaxMessageBytes)
            bytes = []; // dropped, never cut: half a link could open some other page
        using var view = mailbox.CreateViewAccessor(0, MailboxBytes);
        view.WriteArray(sizeof(int), bytes, 0, bytes.Length);
        view.Write(0, bytes.Length);
    }

    private static void Clear(MemoryMappedFile? mailbox)
    {
        try { Write(mailbox, null); }
        catch { /* best effort; never stops a launch */ }
    }

    /// <summary>On the primary: the link the asking launch left, cleared as it is read; null when it left none.</summary>
    private string? TakeMessage()
    {
        if (_mailbox is null)
            return null;
        try
        {
            using var view = _mailbox.CreateViewAccessor(0, MailboxBytes);
            int length = view.ReadInt32(0);
            string? message = null;
            if (length > 0 && length <= MaxMessageBytes)
            {
                var bytes = new byte[length];
                view.ReadArray(sizeof(int), bytes, 0, length);
                message = Encoding.UTF8.GetString(bytes);
            }
            view.Write(0, 0);
            return message;
        }
        catch
        {
            return null; // best effort: the widget still shows
        }
    }

    private static bool Take(Mutex mutex, TimeSpan wait)
    {
        try { return mutex.WaitOne(wait); }
        catch (AbandonedMutexException) { return true; }
    }

    /// <summary>
    /// On the primary: runs <paramref name="onShow"/> (on a pool thread) whenever a second launch asks, with the settings
    /// link it handed over, or null for a plain launch.
    /// </summary>
    public void Listen(Action<string?> onShow)
    {
        if (!IsPrimary || _listener is not null)
            return;
        _listener = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) =>
        {
            if (!_accepting)
                return;
            try { onShow(TakeMessage()); }
            catch { /* a failed show still answers, so the duplicate exits */ }
            try { _ack.Set(); }
            catch (ObjectDisposedException) { /* disposed mid-answer */ }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>On the primary: runs <paramref name="onShow"/> whenever a second launch asks; any link is ignored.</summary>
    public void Listen(Action onShow) => Listen(_ => onShow());

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
        _mailbox?.Dispose();
    }
}
