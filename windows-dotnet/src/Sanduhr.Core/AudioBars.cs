namespace Sanduhr.Core;

/// <summary>
/// State for the Desk audio visualizer (spec 6.6): a short history of peak levels, newest at the right,
/// with fast attack and slow decay. It only ever sees peak values (0 to 1) from the playing app's audio
/// session meter (<see cref="AudioSessionMatch"/> picks the session; System Sounds never reach it); no
/// audio is captured. New for Windows; the Mac has no visualizer.
/// </summary>
public sealed class AudioBars
{
    /// <summary>20 frames a second.</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Knighthawk's silence threshold on the raw peak.</summary>
    public const float IdleThreshold = 0.004f;

    /// <summary>How long every raw peak must stay under <see cref="IdleThreshold"/> before the bars are idle.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(5);

    private const float Decay = 0.82f;

    private readonly float[] _levels;
    private float _smoothed;
    private bool _hasPushed;
    private DateTimeOffset? _quietSince;

    /// <summary>Creates the bars with <paramref name="count"/> zeroed slots (at least 1).</summary>
    public AudioBars(int count = 16)
    {
        _levels = new float[Math.Max(1, count)];
    }

    /// <summary>The stored levels, oldest first, newest last, always <c>count</c> long. A copy.</summary>
    public IReadOnlyList<float> Levels => (float[])_levels.Clone();

    /// <summary>
    /// Appends one peak. A higher value is taken as is (attack); a lower one decays the previous
    /// smoothed level by 0.82. The stored value is the square root of the smoothed level, a perceptual
    /// curve. NaN counts as 0; the peak is clamped to 0..1.
    /// </summary>
    public void Push(float peak, DateTimeOffset now)
    {
        float raw = float.IsNaN(peak) ? 0f : Math.Clamp(peak, 0f, 1f);
        _smoothed = raw > _smoothed ? raw : _smoothed * Decay;
        Array.Copy(_levels, 1, _levels, 0, _levels.Length - 1);
        _levels[^1] = Math.Clamp(MathF.Sqrt(_smoothed), 0f, 1f);

        if (raw >= IdleThreshold) _quietSince = null;
        else _quietSince ??= now;
        _hasPushed = true;
    }

    /// <summary>True with nothing pushed yet, or once every raw peak has stayed under 0.004 for 5 seconds.</summary>
    public bool IsIdle(DateTimeOffset now) =>
        !_hasPushed || (_quietSince is { } since && now - since >= IdleAfter);

    /// <summary>Zeroes the levels and forgets the quiet run, as when the meter is released.</summary>
    public void Reset()
    {
        Array.Clear(_levels);
        _smoothed = 0;
        _hasPushed = false;
        _quietSince = null;
    }
}
