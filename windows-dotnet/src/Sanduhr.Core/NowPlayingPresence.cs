namespace Sanduhr.Core;

/// <summary>Where the now-playing piece is in its life: hidden, fading and growing in, up, or
/// fading and shrinking out.</summary>
public enum PresencePhase
{
    Hidden,
    Opening,
    Shown,
    Closing,
}

/// <summary>
/// Whether the now-playing piece is up, and how far (the Command Center skin's collapse, with the
/// session state in place of an audio-level guess). The only input is whether the media session
/// is <i>active</i> (playing, or paused while "Keep showing while paused" is on): an active session
/// shows the piece at once; one that stays inactive for <see cref="Grace"/> hides it, so a skip or a
/// brief pause never flickers. A sound with no active session is not an input at all, so system
/// chimes can never open the piece. Pure: the caller supplies the clock and schedules
/// <see cref="Settle"/> for the wait <see cref="Observe"/> returns.
///
/// <see cref="Amount"/> is the fade and the gap together, 0 (gone) to 1 (up): the piece's opacity
/// and the share of its height the neighbours leave it. Closing takes <see cref="CloseDuration"/>,
/// opening the slower <see cref="OpenDuration"/> (as the skin closes in about 0.7 s and opens in
/// about a second). A turn around mid-way continues from where it is, so it never jumps. With
/// <see cref="Animate"/> off (Windows' animations off) the amount snaps.
/// </summary>
public sealed class NowPlayingPresence
{
    /// <summary>How long a session may be inactive before the piece hides.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>Hiding: fade out while the neighbours close the gap.</summary>
    public static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(600);

    /// <summary>Showing: slower than closing, as the skin opens.</summary>
    public static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(1000);

    private bool _wanted;
    private DateTimeOffset? _inactiveSince;
    // The linear position when the last turn began, when it began, and which way it heads.
    private double _from;
    private DateTimeOffset _since;

    /// <summary>False snaps the amount to 0 or 1 at once (Windows' "Show animations" is off).</summary>
    public bool Animate { get; set; } = true;

    /// <summary>The piece should be up: an active session, or one inactive for less than the grace.</summary>
    public bool Wanted => _wanted;

    /// <summary>
    /// The latest reading of the session. Returns how long until the piece would hide if nothing
    /// changes (the caller waits that long, then calls <see cref="Settle"/>), null when nothing is
    /// pending. Active shows the piece now; inactive only starts the grace, and repeating it does
    /// not restart it.
    /// </summary>
    public TimeSpan? Observe(bool active, DateTimeOffset now)
    {
        if (active)
        {
            _inactiveSince = null;
            if (!_wanted) Turn(true, now);
            return null;
        }
        if (!_wanted) return null;
        _inactiveSince ??= now;
        return Settle(now);
    }

    /// <summary>Hides the piece when the grace has run out. Returns the wait still left, null when
    /// settled or when nothing is pending.</summary>
    public TimeSpan? Settle(DateTimeOffset now)
    {
        if (!_wanted || _inactiveSince is not { } since) return null;
        var left = Grace - (now - since);
        if (left > TimeSpan.Zero) return left;
        _inactiveSince = null;
        Turn(false, now);
        return null;
    }

    /// <summary>Starts hiding now, with no grace (the user excluded the playing app). Active again
    /// later shows the piece as usual.</summary>
    public void Dismiss(DateTimeOffset now)
    {
        _inactiveSince = null;
        if (_wanted) Turn(false, now);
    }

    /// <summary>Gone at once, no fade (the service stopped: nothing is listening any more).</summary>
    public void Reset()
    {
        _wanted = false;
        _inactiveSince = null;
        _from = 0;
        _since = default;
    }

    /// <summary>The piece's place now, 0 (gone) to 1 (up), eased (smoothstep) so it starts and
    /// lands softly.</summary>
    public double Amount(DateTimeOffset now)
    {
        double t = Linear(now);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Hidden, opening, shown or closing at <paramref name="now"/>.</summary>
    public PresencePhase Phase(DateTimeOffset now)
    {
        double t = Linear(now);
        if (_wanted) return t >= 1 ? PresencePhase.Shown : PresencePhase.Opening;
        return t <= 0 ? PresencePhase.Hidden : PresencePhase.Closing;
    }

    /// <summary>True while the amount is still moving (the caller keeps its frame timer running).</summary>
    public bool IsMoving(DateTimeOffset now) => Phase(now) is PresencePhase.Opening or PresencePhase.Closing;

    /// <summary>The piece is on screen at all: up, or still fading out.</summary>
    public bool OnScreen(DateTimeOffset now) => Phase(now) != PresencePhase.Hidden;

    private void Turn(bool wanted, DateTimeOffset now)
    {
        _from = Linear(now);
        _since = now;
        _wanted = wanted;
    }

    private double Linear(DateTimeOffset now)
    {
        if (!Animate) return _wanted ? 1 : 0;
        var span = _wanted ? OpenDuration : CloseDuration;
        double step = Math.Max(0, (now - _since).TotalSeconds) / span.TotalSeconds;
        return Math.Clamp(_wanted ? _from + step : _from - step, 0, 1);
    }
}
