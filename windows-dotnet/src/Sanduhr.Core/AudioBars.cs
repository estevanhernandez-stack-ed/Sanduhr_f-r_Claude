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

/// <summary>
/// How the drawn bars move (spec 3.8, G: livelier bars). Each bar eases toward its level in <see cref="AudioBars.Levels"/>: up by
/// a share of the gap each frame (<see cref="Attack"/>), down by a share of it (<see cref="Decay"/>) but never faster than
/// <see cref="Falloff"/>, so a drop reads as a fall rather than a cut. Over each bar a peak cap rides its highest point, holds there
/// for <see cref="CapHoldFrames"/> frames, then drops with growing speed until it lands on the bar. Everything comes from the same
/// peak level the session meter gives; no audio is captured. <see cref="Calm"/> (Windows' animations off) keeps the bars calm:
/// slower easing, no caps, and the source ticks at <see cref="CalmFrameInterval"/>. Pure: the Desk steps it once per frame.
/// </summary>
public sealed class AudioBarMotion
{
    /// <summary>Share of a rise closed per frame.</summary>
    public const float Attack = 0.55f;
    /// <summary>Share of a drop closed per frame, before the <see cref="Falloff"/> limit.</summary>
    public const float Decay = 0.35f;
    /// <summary>The most a bar drops in one frame (a full bar falls in a little over a second at 20 frames a second).</summary>
    public const float Falloff = 0.045f;
    /// <summary>How long a cap holds at its peak before it drops: 9 frames, about 0.45 s.</summary>
    public const int CapHoldFrames = 9;
    /// <summary>What a dropping cap's speed grows by each frame.</summary>
    public const float CapGravity = 0.004f;
    /// <summary>The calm bars' easing: gentler both ways.</summary>
    public const float CalmAttack = 0.3f;
    public const float CalmFalloff = 0.02f;
    /// <summary>The calm bars update five times a second instead of twenty.</summary>
    public static readonly TimeSpan CalmFrameInterval = TimeSpan.FromMilliseconds(200);

    private readonly float[] _heights;
    private readonly float[] _caps;
    private readonly float[] _capSpeed;
    private readonly int[] _hold;

    public AudioBarMotion(int count = 16)
    {
        int n = Math.Max(1, count);
        _heights = new float[n];
        _caps = new float[n];
        _capSpeed = new float[n];
        _hold = new int[n];
    }

    /// <summary>Windows' animations are off: calm easing and no caps.</summary>
    public bool Calm { get; set; }

    /// <summary>True when the caps are drawn (not <see cref="Calm"/>).</summary>
    public bool ShowsCaps => !Calm;

    /// <summary>Each bar's drawn height, 0 to 1. A copy.</summary>
    public IReadOnlyList<float> Heights => (float[])_heights.Clone();

    /// <summary>Each bar's cap, 0 to 1, never below its bar. A copy.</summary>
    public IReadOnlyList<float> Caps => (float[])_caps.Clone();

    /// <summary>One frame toward <paramref name="levels"/> (one per bar; a missing one counts as 0, NaN as 0, the rest clamped to 0..1).</summary>
    public void Step(IReadOnlyList<float> levels)
    {
        float attack = Calm ? CalmAttack : Attack;
        float falloff = Calm ? CalmFalloff : Falloff;
        for (int i = 0; i < _heights.Length; i++)
        {
            float t = i < levels.Count && !float.IsNaN(levels[i]) ? Math.Clamp(levels[i], 0f, 1f) : 0f;
            float h = _heights[i];
            if (t > h) h += (t - h) * attack;
            else h -= Math.Min((h - t) * Decay, falloff);
            if (h < 0.0005f) h = 0;
            _heights[i] = h;

            if (Calm)
            {
                // No peak animation: the cap sits on the bar and is not drawn.
                _caps[i] = h;
                _capSpeed[i] = 0;
                _hold[i] = 0;
                continue;
            }
            if (h >= _caps[i])
            {
                _caps[i] = h;
                _capSpeed[i] = 0;
                _hold[i] = CapHoldFrames;
            }
            else if (_hold[i] > 0)
            {
                _hold[i]--;
            }
            else
            {
                _capSpeed[i] += CapGravity;
                _caps[i] = Math.Max(h, _caps[i] - _capSpeed[i]);
            }
        }
    }

    /// <summary>Every bar and cap back to 0, as when the source stops.</summary>
    public void Reset()
    {
        Array.Clear(_heights);
        Array.Clear(_caps);
        Array.Clear(_capSpeed);
        Array.Clear(_hold);
    }
}
