namespace Sanduhr.App.Desk.UnderIcons;

/// <summary>
/// When the under-icons host next copies its islands. At rest it waits for a wake (a layout pass, or an animation starting, see
/// <see cref="InkWake"/>) and checks once soon after it. A change that check finds is one step (a minute, a meter, a CPU reading), so
/// the copy rests again at once; motion keeps waking it, and an announced animation keeps the checks fast until it ends. A slow check
/// at <see cref="Rest"/> catches anything that changed without a wake, and a change found that way, which nothing announced, keeps the
/// checks fast for a few more in case it is motion. Before this the host copied every island
/// once a second whether or not anything changed, which in software rendering cost about 2% of a core at rest.
/// </summary>
internal sealed class InkCadence
{
    /// <summary>The safety net at rest: a change that raised no wake still shows within this.</summary>
    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(5);
    /// <summary>The check while frames keep changing (an animation runs).</summary>
    public static readonly TimeSpan Hot = TimeSpan.FromMilliseconds(33);
    /// <summary>The check after a wake at rest: late enough that a burst of layout passes shares one copy.</summary>
    public static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(50);
    /// <summary>Unchanged fast checks after an unannounced change before going back to rest: a one-off costs a few checks, not seconds of them.</summary>
    public const int QuietBeforeRest = 3;

    private int _quiet = QuietBeforeRest;
    private DateTime _hotUntil = DateTime.MinValue;
    private bool _soonBooked;

    /// <summary>True while neither a change nor an animation keeps the checks fast.</summary>
    public bool Resting(DateTime now) => _quiet >= QuietBeforeRest && now >= _hotUntil;

    /// <summary>
    /// Something on the Desk may have changed; an animation of <paramref name="animates"/> keeps the checks fast until it ends. Returns
    /// the interval to set on the timer now, or null to leave it: the checks are already fast, or a check soon is already booked (so a
    /// stream of wakes cannot push it back for ever).
    /// </summary>
    public TimeSpan? Wake(DateTime now, TimeSpan animates)
    {
        bool resting = Resting(now);
        if (animates > TimeSpan.Zero && now + animates > _hotUntil) _hotUntil = now + animates;
        if (!resting || _soonBooked) return null;
        _soonBooked = true;
        return Soon;
    }

    /// <summary>A check ran and found the islands <paramref name="changed"/> or not: the interval to the next one.</summary>
    public TimeSpan Checked(DateTime now, bool changed)
    {
        bool stepAtRest = _soonBooked && _quiet >= QuietBeforeRest;
        _soonBooked = false;
        if (changed && !stepAtRest) _quiet = 0;
        else if (!changed && _quiet < QuietBeforeRest) _quiet++;
        return Resting(now) ? Rest : Hot;
    }
}

/// <summary>
/// The Desk's own word that something under the icons is about to move without a layout pass: an opacity or gradient animation, or a
/// property set straight on a piece. The under-icons host listens (UI thread only); with nothing listening a wake costs nothing.
/// </summary>
internal static class InkWake
{
    /// <summary>Raised with how long the motion runs (zero for a one-off change).</summary>
    public static event Action<TimeSpan>? Woken;

    public static void Now(TimeSpan animates = default) => Woken?.Invoke(animates);

    public static void Now(double seconds) => Now(TimeSpan.FromSeconds(seconds));
}
