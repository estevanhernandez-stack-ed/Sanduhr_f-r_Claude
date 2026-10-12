using System.Runtime.Versioning;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Reads whether each Desk island window is fully hidden behind other windows (S7, 7.1). A set of
/// out-of-context WinEvent hooks wakes it on the few events that change what covers what; each burst
/// schedules one recompute 250 ms later. A recompute walks the z-order upward from each island window,
/// reads each window above (visibility, minimized, cloaked, the transparent and layered style bits and
/// the DWM frame, never its title) and asks <see cref="DeskCoverage"/>. While any island reads
/// covered, a 5 s recheck catches a covering window restored by keyboard, which raises none of the
/// hooked events; with nothing covered nothing ticks.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DeskCoverageWatch : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    // Foreground, move or resize ended, minimize start..end, a window object shown or hidden, a window
    // cloaked or uncloaked. EVENT_OBJECT_LOCATIONCHANGE is deliberately not hooked: it fires on every
    // cursor move and would wake this watch constantly for nothing.
    private static readonly (uint Min, uint Max)[] Ranges =
    [
        (DeskNative.EVENT_SYSTEM_FOREGROUND, DeskNative.EVENT_SYSTEM_FOREGROUND),
        (DeskNative.EVENT_SYSTEM_MOVESIZEEND, DeskNative.EVENT_SYSTEM_MOVESIZEEND),
        (DeskNative.EVENT_SYSTEM_MINIMIZESTART, DeskNative.EVENT_SYSTEM_MINIMIZEEND),
        (DeskNative.EVENT_OBJECT_SHOW, DeskNative.EVENT_OBJECT_HIDE),
        (DeskNative.EVENT_OBJECT_CLOAKED, DeskNative.EVENT_OBJECT_UNCLOAKED),
    ];

    private readonly Func<IReadOnlyList<IntPtr>> _islands;
    private readonly Func<IntPtr> _sentinel;
    private readonly DispatcherTimer _settle;
    private readonly DispatcherTimer _recheck;
    private readonly List<IntPtr> _hooks = [];
    /// <summary>Kept in a field so the garbage collector cannot free the delegate the hooks call.</summary>
    private DeskNative.WinEventProc? _proc;
    private bool[] _covered = [];
    private bool _disposed;

    /// <param name="islands">The island windows' handles now, in island order.</param>
    /// <param name="sentinel">The host's sentinel window, which is one of its own and never covers.</param>
    public DeskCoverageWatch(Func<IReadOnlyList<IntPtr>> islands, Func<IntPtr> sentinel)
    {
        _islands = islands;
        _sentinel = sentinel;
        _settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = Settle };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Recompute();
        };
        _recheck = new DispatcherTimer(DispatcherPriority.Background) { Interval = Recheck };
        _recheck.Tick += (_, _) => Recompute();
    }

    /// <summary>One flag per island window, in island order; all false when unknown.</summary>
    public IReadOnlyList<bool> Covered => _covered;

    /// <summary>The flags changed.</summary>
    public event Action? Changed;

    /// <summary>Installs the hooks (once) and forgets what was known: <paramref name="islands"/> windows, none covered.</summary>
    public void Start(int islands)
    {
        if (_disposed) return;
        _covered = new bool[islands];
        if (_hooks.Count > 0) return;
        _proc = OnEvent;
        foreach (var (min, max) in Ranges)
        {
            // Sanduhr's own windows count too (Settings or the widget can cover an island), so this
            // process's events are not skipped; a recompute never shows or hides a window itself.
            var h = DeskNative.SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0,
                DeskNative.WINEVENT_OUTOFCONTEXT);
            if (h != IntPtr.Zero) _hooks.Add(h);
        }
    }

    /// <summary>Unhooks, stops both timers and clears the flags without raising <see cref="Changed"/>.</summary>
    public void Stop()
    {
        foreach (var h in _hooks) DeskNative.UnhookWinEvent(h);
        _hooks.Clear();
        _proc = null;
        _settle.Stop();
        _recheck.Stop();
        _covered = [];
    }

    /// <summary>Asks for a recompute 250 ms from now, after the burst of events settles.</summary>
    public void Schedule()
    {
        if (_disposed) return;
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>Reads the z-order now and raises <see cref="Changed"/> when a flag differs.</summary>
    public void Recompute()
    {
        if (_disposed) return;
        var next = Read(_islands(), _sentinel());
        bool changed = !next.AsSpan().SequenceEqual(_covered);
        _covered = next;
        if (next.Contains(true)) { if (!_recheck.IsEnabled) _recheck.Start(); }
        else _recheck.Stop();
        if (changed) Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Only window objects matter; every other object's event returns at once.
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
        Schedule();
    }

    /// <summary>Per island window: is its rectangle fully covered by the occluders above it.</summary>
    private static bool[] Read(IReadOnlyList<IntPtr> islands, IntPtr sentinel)
    {
        var result = new bool[islands.Count];
        // Per-monitor aware, so every rectangle is physical pixels whatever the process is.
        DeskNative.PerMonitor(() =>
        {
            var own = new HashSet<IntPtr>(islands) { sentinel };
            for (int i = 0; i < islands.Count; i++)
            {
                var h = islands[i];
                if (h == IntPtr.Zero || !DeskNative.IsWindow(h) || DeskNative.Rect(h) is not { } rect) continue;
                var frames = new List<PxRect>();
                for (var w = DeskNative.Above(h); w != IntPtr.Zero; w = DeskNative.Above(w))
                {
                    var facts = Facts(w, own);
                    if (DeskCoverage.Occluder(facts)) frames.Add(facts.Frame);
                }
                result[i] = DeskCoverage.FullyCovered(rect, frames);
            }
            return true;
        });
        return result;
    }

    /// <summary>What the rule needs about one window; styles, cloak and frame only, never its text.</summary>
    private static WindowFacts Facts(IntPtr w, HashSet<IntPtr> own)
    {
        bool desk = own.Contains(w);
        bool visible = DeskNative.IsWindowVisible(w);
        bool minimized = visible && DeskNative.IsIconic(w);
        if (!visible || minimized || desk)
            return new WindowFacts(visible, minimized, false, false, false, desk, default);
        int ex = DeskNative.GetWindowLong(w, DeskNative.GWL_EXSTYLE);
        var frame = GlowNative.ExtendedFrame(w) ?? DeskNative.Rect(w) ?? default;
        return new WindowFacts(visible, minimized, GlowNative.IsCloaked(w),
            (ex & WS_EX_TRANSPARENT) != 0, (ex & WS_EX_LAYERED) != 0, desk, frame);
    }
}
