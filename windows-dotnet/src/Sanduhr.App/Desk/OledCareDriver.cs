using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// OLED care's two clocks (spec 3.7.0), running only while the switch is on: every <see cref="OledDrift.Every"/> the orbit moves
/// on to its next point (<see cref="DriftStepped"/>), and every <see cref="OledIdle.PollEvery"/> the time of the last input is
/// looked at (GetLastInputInfo, one number, memory only) and <see cref="OledIdle"/> says whether to dim or wake
/// (<see cref="DimChanged"/>). Turning it off wakes at once. It touches no window; the Desk controller and the app act on its events.
/// </summary>
internal sealed class OledCareDriver : IDisposable
{
    private readonly OledIdle _idle;
    private readonly Func<DateTimeOffset?> _lastInput;
    private readonly DispatcherTimer _drift;
    private readonly DispatcherTimer _poll;
    private bool _on;
    private bool _disposed;

    /// <param name="lastInput">When the last input happened; the app's reads Windows, tests pass their own.</param>
    /// <param name="clock">The clock the idle rule measures against; UTC now in the app.</param>
    public OledCareDriver(Func<DateTimeOffset?>? lastInput = null, Func<DateTimeOffset>? clock = null,
        TimeSpan? driftEvery = null, TimeSpan? pollEvery = null)
    {
        _lastInput = lastInput ?? DeskSensorsNative.LastInputAt;
        _idle = new OledIdle(clock ?? (() => DateTimeOffset.UtcNow));
        _drift = new DispatcherTimer(DispatcherPriority.Background) { Interval = driftEvery ?? OledDrift.Every };
        _drift.Tick += (_, _) => Advance();
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = pollEvery ?? OledIdle.PollEvery };
        _poll.Tick += (_, _) => Poll();
    }

    /// <summary>The orbit's current point.</summary>
    public int Step { get; private set; }

    /// <summary>True while dimmed.</summary>
    public bool Dimmed => _idle.Dimmed;

    /// <summary>Switch the care on or off. On starts both clocks (the first look at the last input comes with the first poll);
    /// off stops them and, if it had dimmed, wakes.</summary>
    public bool On
    {
        get => _on;
        set
        {
            if (_disposed || value == _on) return;
            _on = value;
            if (value)
            {
                _drift.Start();
                _poll.Start();
                return;
            }
            _drift.Stop();
            _poll.Stop();
            Raise(_idle.Reset());
        }
    }

    /// <summary>The orbit moved on; <see cref="Step"/> is the new point.</summary>
    public event Action? DriftStepped;

    /// <summary>The dim began (true) or ended (false). <see cref="Ease"/> says over how long.</summary>
    public event Action<bool>? DimChanged;

    /// <summary>How long the change just announced should take: Windows' Show animations off snaps it.</summary>
    public TimeSpan Ease { get; private set; }

    /// <summary>Moves to the orbit's next point (the drift timer calls it; tests do too).</summary>
    internal void Advance()
    {
        if (!_on || _disposed) return;
        Step = (Step + 1) % OledDrift.Steps;
        DriftStepped?.Invoke();
    }

    /// <summary>Looks at the last input once (the poll timer calls it; tests do too).</summary>
    internal void Poll()
    {
        if (!_on || _disposed) return;
        Raise(_idle.Observe(_lastInput()));
    }

    private void Raise(OledIdleEdge edge)
    {
        if (edge == OledIdleEdge.None) return;
        Ease = OledIdle.EaseFor(edge, System.Windows.SystemParameters.ClientAreaAnimation);
        DimChanged?.Invoke(edge == OledIdleEdge.Dim);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _drift.Stop();
        _poll.Stop();
        _disposed = true;
    }
}
