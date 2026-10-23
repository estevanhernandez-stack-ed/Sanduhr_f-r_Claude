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

    /// <summary>The render harness and demo previews: a fixed reading, no sampling.</summary>
    internal void Preview(TelemetrySample sample)
    {
        Latest = sample;
        Updated?.Invoke();
    }

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
/// The bars beside now playing (spec 6.6): the peak level of the playing app's audio session (not the
/// whole output, so a Windows chime never moves them), sampled every <see cref="AudioBars.FrameInterval"/>
/// only while <see cref="Start"/>ed (the controller starts it only while a track plays and the piece is on
/// screen). <see cref="Follow"/> names the app (the media session's AppUserModelId, held in memory only).
/// After <see cref="AudioBars"/> says idle, it drops to one check a second until sound returns. Stopped,
/// the meter is released and nothing ticks. With Windows' animations off (<see cref="Calm"/>) it ticks at
/// <see cref="AudioBarMotion.CalmFrameInterval"/> and the bars stay calm. A level only: no audio is captured.
/// </summary>
internal sealed class AudioLevelSource : IDisposable
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(1);
    private readonly DispatcherTimer _timer;
    private DeskSensorsNative.SessionPeakMeter? _meter;
    private string? _appId;

    public AudioLevelSource()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = AudioBars.FrameInterval };
        _timer.Tick += (_, _) => Tick();
    }

    public AudioBars Bars { get; } = new();

    /// <summary>A new frame of levels.</summary>
    public event Action? Frame;

    public bool Running => _timer.IsEnabled;

    /// <summary>Windows' animations are off (Settings, Accessibility, Animation effects): slower frames, no peak caps. Read at each tick.</summary>
    public bool Calm => CalmNow();

    /// <summary>Where <see cref="Calm"/> is read from; the harness and tests set it.</summary>
    internal Func<bool> CalmNow { get; set; } = () => !System.Windows.SystemParameters.ClientAreaAnimation;

    private TimeSpan Interval => Calm ? AudioBarMotion.CalmFrameInterval : AudioBars.FrameInterval;

    /// <summary>True when the last frame found the playing app's own audio session; false when it fell
    /// back to the loudest other session (the live check reads it). Never a title or an app id.</summary>
    internal bool FollowingApp { get; private set; }

    /// <summary>Which app's audio the bars follow: the playing media session's AppUserModelId. Null
    /// follows the loudest session that is not System Sounds.</summary>
    public void Follow(string? appId) => _appId = appId;

    /// <summary>Previews and the cost runner: one frame from a given level, without the meter.</summary>
    internal void Pump(float peak)
    {
        Bars.Push(peak, DateTimeOffset.Now);
        Frame?.Invoke();
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _meter ??= new DeskSensorsNative.SessionPeakMeter();
        _timer.Interval = Interval;
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
        var pick = _meter?.Read(_appId);
        FollowingApp = pick?.Matched == true;
        float peak = pick?.Peak ?? 0f;
        bool wasIdle = Bars.IsIdle(now);
        Bars.Push(peak, now);
        bool idle = Bars.IsIdle(now);
        _timer.Interval = idle ? IdleCheck : Interval;
        if (!(wasIdle && idle)) Frame?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _meter?.Dispose();
        _meter = null;
    }
}
