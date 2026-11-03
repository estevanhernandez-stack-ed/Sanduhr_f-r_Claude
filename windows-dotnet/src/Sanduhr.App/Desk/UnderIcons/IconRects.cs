using System.Runtime.Versioning;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk.UnderIcons;

/// <summary>
/// The desktop icons' rects for the under-icons hook (spec 5.0d), read by <see cref="DesktopIcons"/> (the Shell's own view of the
/// desktop, with its hung-Explorer guard) every 2 s on a pool thread while running: never on the UI thread or the ink thread. Physical
/// pixels, widened by 2 px. A read the Shell does not answer keeps the last.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class IconRects : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    private const int Widen = 2;

    private readonly Action<IReadOnlyList<PxRect>> _read;
    private readonly Timer _timer;
    private int _busy;

    /// <param name="read">Called on a pool thread with each new read.</param>
    public IconRects(Action<IReadOnlyList<PxRect>> read)
    {
        _read = read;
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, Every);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>Read again now (Explorer restarted, or the Desk was just drawn).</summary>
    public void Now() => _timer.Change(TimeSpan.Zero, Every);

    public void Dispose() => _timer.Dispose();

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            // Null: the Shell did not answer (hung, missing or restarting); keep the last read.
            if (DesktopIcons.Read() is { } icons)
                _read([.. icons.Select(r => new PxRect(r.X - Widen, r.Y - Widen, r.Width + 2 * Widen, r.Height + 2 * Widen))]);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
