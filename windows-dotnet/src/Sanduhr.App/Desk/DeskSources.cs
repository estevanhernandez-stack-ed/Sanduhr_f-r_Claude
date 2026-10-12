using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Telemetry for the Desk (spec 6.4): CPU, RAM and disk read by P/Invoke every
/// <see cref="Telemetry.Interval"/>, only while something listens. The first CPU reading needs two
/// samples, so the first <see cref="Updated"/> comes one interval after <see cref="Start"/>. Readings
/// stay in memory.
/// </summary>
internal sealed class TelemetrySource : IDisposable
{
    private readonly DispatcherTimer _timer;
    private CpuTimes? _last;

    public TelemetrySource()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Telemetry.Interval };
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>The latest reading; null until two CPU samples exist.</summary>
    public TelemetrySample? Latest { get; private set; }

    public event Action? Updated;

    public bool Running => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled) return;
        Sample();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _last = null;
    }

    private void Sample()
    {
        var t = DeskSensorsNative.SystemTimes();
        if (t is not { } times) return;
        var now = new CpuTimes(times.Idle, times.Kernel, times.User);
        var mem = DeskSensorsNative.MemoryLoad();
        var disk = DeskSensorsNative.SystemDrive();
        if (_last is { } prev)
        {
            Latest = new TelemetrySample(
                Telemetry.CpuPercent(prev, now),
                mem is { } m ? Telemetry.MemoryPercent(m) : 0,
                disk is { } d ? Telemetry.DiskUsedPercent(d.Total, d.Free) : 0,
                DateTimeOffset.Now);
            Updated?.Invoke();
        }
        _last = now;
    }

    public void Dispose() => Stop();
}

/// <summary>
/// The bars beside now playing (spec 6.6): the default output's peak level, sampled every
/// <see cref="AudioBars.FrameInterval"/> only while <see cref="Start"/>ed (the controller starts it only
/// while a track plays and the piece is on screen). After <see cref="AudioBars"/> says idle, it drops to
/// one check a second until sound returns. Stopped, the meter is released and nothing ticks. A level
/// only: no audio is captured.
/// </summary>
internal sealed class AudioLevelSource : IDisposable
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(1);
    private readonly DispatcherTimer _timer;
    private DeskSensorsNative.PeakMeter? _meter;

    public AudioLevelSource()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = AudioBars.FrameInterval };
        _timer.Tick += (_, _) => Tick();
    }

    public AudioBars Bars { get; } = new();

    /// <summary>A new frame of levels.</summary>
    public event Action? Frame;

    public bool Running => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _meter ??= new DeskSensorsNative.PeakMeter();
        _timer.Interval = AudioBars.FrameInterval;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _meter?.Dispose();
        _meter = null;
        Bars.Reset();
        Frame?.Invoke();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        float peak = _meter?.Read() ?? 0f;
        bool wasIdle = Bars.IsIdle(now);
        Bars.Push(peak, now);
        bool idle = Bars.IsIdle(now);
        _timer.Interval = idle ? IdleCheck : AudioBars.FrameInterval;
        if (!(wasIdle && idle)) Frame?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _meter?.Dispose();
        _meter = null;
    }
}
