namespace Sanduhr.Core;

/// <summary>The companion's mood (FORMAT 5.5 <c>moods</c>, behaviour FORMAT 6).</summary>
public enum CompanionMood { Content, Focused, Worried, Exhausted, Celebrate }

/// <summary>A one-shot event the companion reacts to (FORMAT 5.5 <c>triggers</c>): its animation plays once, then the mood resumes.</summary>
public enum CompanionTrigger { NeedsYou, JobPassed, NearLimit, Idle }

/// <summary>
/// Mood from the widget's own pacing (FORMAT 6): under pace is content, near pace focused, weekly use at 85% or more or
/// over pace worried, at the limit exhausted (it sleeps until the reset). Celebrate is the reset itself, which needs
/// memory of the last reading; <see cref="CompanionBrain"/> adds it.
/// </summary>
public static class CompanionMoods
{
    /// <summary>Weekly use at or above this is worried, whatever the pace.</summary>
    public const double WorriedWeeklyPercent = 85;

    /// <summary>Within this many points of the pace line is near pace (the widget's "On pace", <see cref="Pacing.PaceInfo"/>).</summary>
    public const double NearPacePoints = 5;

    /// <summary>The <c>sprite.json</c> key for a mood.</summary>
    public static string Key(CompanionMood mood) => mood switch
    {
        CompanionMood.Content => "content",
        CompanionMood.Focused => "focused",
        CompanionMood.Worried => "worried",
        CompanionMood.Exhausted => "exhausted",
        _ => "celebrate",
    };

    /// <summary>The <c>sprite.json</c> key for a trigger.</summary>
    public static string Key(CompanionTrigger trigger) => trigger switch
    {
        CompanionTrigger.NeedsYou => "needs_you",
        CompanionTrigger.JobPassed => "job_passed",
        CompanionTrigger.NearLimit => "near_limit",
        _ => "idle",
    };

    /// <summary>
    /// The mood the numbers alone give. Signed out or no numbers is content (the pet never frets over missing data).
    /// Any limit at 100% or more is exhausted. Weekly at <see cref="WorriedWeeklyPercent"/> or more, or any limit ahead of its
    /// pace line by <see cref="NearPacePoints"/> or more, is worried. A limit in use within <see cref="NearPacePoints"/> of its
    /// pace line is focused. Otherwise content.
    /// </summary>
    public static CompanionMood FromUsage(DeskUsage usage, DateTimeOffset now)
    {
        if (usage.SignInNeeded) return CompanionMood.Content;
        var tiers = usage.LineTiers ?? usage.Tiers;
        if (tiers.Count == 0) return CompanionMood.Content;
        if (tiers.Any(t => Windowed(t) && t.Utilization >= 100)) return CompanionMood.Exhausted;
        if (tiers.Any(t => t.Key == TierModel.SevenDay && t.Utilization >= WorriedWeeklyPercent)) return CompanionMood.Worried;
        bool near = false;
        foreach (var t in tiers)
        {
            if (!Windowed(t) || Pacing.PaceFrac(t.ResetsAt, t.Key, now) is not { } frac) continue;
            double diff = t.Utilization - frac * 100;
            if (diff >= NearPacePoints) return CompanionMood.Worried;
            if (t.Utilization > 0 && Math.Abs(diff) < NearPacePoints) near = true;
        }
        return near ? CompanionMood.Focused : CompanionMood.Content;
    }

    /// <summary>A limit that runs in a window with a reset time: the session and the weekly ones.</summary>
    internal static bool Windowed(DeskTier t) =>
        (t.Key == TierModel.FiveHour || t.Key.StartsWith(TierModel.SevenDay, StringComparison.Ordinal)) && Pacing.Parse(t.ResetsAt) is not null;
}

/// <summary>
/// The companion's memory between readings (pure: no clock of its own, no IO). It turns the widget's readings, the
/// watchers and the user's last input into the mood and the one-shot triggers of FORMAT 6:
/// <list type="bullet">
/// <item><c>celebrate</c>: a weekly limit, or a limit that was at 100%, rolled into a new window (its reset time moved on
/// by an hour or more). Held for <see cref="CelebrateFor"/>, then the numbers' mood again.</item>
/// <item><c>needs_you</c>: a watcher went to waiting on you. <c>job_passed</c>: a watcher finished green.</item>
/// <item><c>near_limit</c>: a limit crossed one of the alert thresholds (Settings, Alerts: warn and urgent) or 100%,
/// upward, since the last reading. The first reading only sets the baseline.</item>
/// <item><c>idle</c>: no input for <see cref="IdleAfter"/>. Fires once, again only after input returns.</item>
/// </list>
/// </summary>
public sealed class CompanionBrain
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan CelebrateFor = TimeSpan.FromMinutes(10);

    /// <summary>A reset time that moves on by less than this is the API's jitter, not a new window.</summary>
    private static readonly TimeSpan Rollover = TimeSpan.FromHours(1);

    private readonly Dictionary<string, (double Utilization, DateTimeOffset? Resets)> _tiers = new(StringComparer.Ordinal);
    private bool _usageSeen;
    private readonly Dictionary<string, WatcherState> _watchers = new(StringComparer.Ordinal);
    private bool _watchersSeen;
    private bool _idleFired;
    private DateTimeOffset? _celebrateUntil;
    private CompanionMood _numbers = CompanionMood.Content;

    /// <summary>The mood now: celebrate while a reset is fresh, otherwise what the last reading's numbers say.</summary>
    public CompanionMood Mood(DateTimeOffset now) =>
        _celebrateUntil is { } until && now < until ? CompanionMood.Celebrate : _numbers;

    /// <summary>A new reading. Returns <see cref="CompanionTrigger.NearLimit"/> when a limit crossed a threshold upward.</summary>
    public IReadOnlyList<CompanionTrigger> ObserveUsage(DeskUsage usage, int warnPercent, int urgentPercent, DateTimeOffset now)
    {
        var fired = new List<CompanionTrigger>();
        _numbers = CompanionMoods.FromUsage(usage, now);
        if (usage.SignInNeeded) return fired;
        var tiers = usage.LineTiers ?? usage.Tiers;
        var thresholds = new[] { warnPercent, urgentPercent, 100 }.Where(p => p is > 0 and <= 100).Distinct().ToArray();
        foreach (var t in tiers)
        {
            var resets = Pacing.Parse(t.ResetsAt);
            if (_tiers.TryGetValue(t.Key, out var was))
            {
                bool rolled = was.Resets is { } a && resets is { } b && b - a >= Rollover;
                if (rolled)
                {
                    if (t.Key == TierModel.SevenDay || was.Utilization >= 100) _celebrateUntil = now + CelebrateFor;
                }
                else if (_usageSeen && thresholds.Any(p => was.Utilization < p && t.Utilization >= p))
                {
                    if (!fired.Contains(CompanionTrigger.NearLimit)) fired.Add(CompanionTrigger.NearLimit);
                }
            }
            _tiers[t.Key] = (t.Utilization, resets);
        }
        _usageSeen = true;
        // A fresh reset outranks the numbers only while they are calm: a new window that is already at the limit sleeps.
        if (_numbers == CompanionMood.Exhausted) _celebrateUntil = null;
        return fired;
    }

    /// <summary>The watchers now. Returns the triggers for watchers that changed into waiting or passed since the last call
    /// (the first call only sets the baseline).</summary>
    public IReadOnlyList<CompanionTrigger> ObserveWatchers(IEnumerable<Watcher> watchers)
    {
        var fired = new List<CompanionTrigger>();
        var now = new Dictionary<string, WatcherState>(StringComparer.Ordinal);
        foreach (var w in watchers) now[w.Id] = w.State;
        if (_watchersSeen)
        {
            foreach (var (id, state) in now)
            {
                bool changed = !_watchers.TryGetValue(id, out var before) || before != state;
                if (!changed) continue;
                if (state == WatcherState.Waiting && !fired.Contains(CompanionTrigger.NeedsYou)) fired.Add(CompanionTrigger.NeedsYou);
                if (state == WatcherState.Passed && !fired.Contains(CompanionTrigger.JobPassed)) fired.Add(CompanionTrigger.JobPassed);
            }
        }
        _watchers.Clear();
        foreach (var (id, state) in now) _watchers[id] = state;
        _watchersSeen = true;
        return fired;
    }

    /// <summary>The user's last input. <see cref="CompanionTrigger.Idle"/> once it is <see cref="IdleAfter"/> old; armed again by new input.</summary>
    public CompanionTrigger? ObserveActivity(DateTimeOffset lastInput, DateTimeOffset now)
    {
        if (now - lastInput < IdleAfter)
        {
            _idleFired = false;
            return null;
        }
        if (_idleFired) return null;
        _idleFired = true;
        return CompanionTrigger.Idle;
    }
}

/// <summary>What to draw this frame: the animation, the frame index into the sheet, and whether it is a one-shot trigger.</summary>
public readonly record struct CompanionPose(string Animation, int Frame, bool OneShot);

/// <summary>
/// Which frame the companion draws (pure: the time comes in). A looping animation (the mood's, or <c>walk</c> while roaming)
/// starts at its first frame whenever it changes and runs at the pack's <c>fps</c>. A trigger's animation plays once from
/// its first frame, then the loop resumes from its first frame. Reduce motion draws every animation at 1 fps.
/// Exhausted sleeps: it never walks.
/// </summary>
public sealed class CompanionAnimator
{
    private readonly CompanionSprite _sprite;
    private (string Animation, DateTimeOffset Start)? _shot;
    private string? _loop;
    private DateTimeOffset _loopStart;

    public CompanionAnimator(CompanionSprite sprite) => _sprite = sprite;

    /// <summary>Frames a second: the pack's, or 1 with reduce motion on.</summary>
    public static int FpsFor(CompanionSprite sprite, bool reduceMotion) => reduceMotion ? 1 : Math.Clamp(sprite.Fps, 1, 24);

    /// <summary>Starts <paramref name="trigger"/>'s animation from its first frame (a trigger already playing is left to finish).</summary>
    public void Play(CompanionTrigger trigger, DateTimeOffset now)
    {
        if (!_sprite.Triggers.TryGetValue(trigger, out var anim) || _sprite.Frames(anim).Count == 0) return;
        if (_shot is { } s && s.Animation == anim) return;
        _shot = (anim, now);
    }

    /// <summary>True while a trigger's animation is still playing at <paramref name="now"/>.</summary>
    public bool Playing(DateTimeOffset now, bool reduceMotion) =>
        _shot is { } s && Index(now - s.Start, FpsFor(_sprite, reduceMotion)) < _sprite.Frames(s.Animation).Count;

    /// <summary>True when the companion may walk now: not sleeping and no trigger playing.</summary>
    public bool CanWalk(CompanionMood mood, DateTimeOffset now, bool reduceMotion) =>
        mood != CompanionMood.Exhausted && !Playing(now, reduceMotion);

    /// <summary>The frame to draw at <paramref name="now"/>.</summary>
    public CompanionPose Pose(CompanionMood mood, bool walking, DateTimeOffset now, bool reduceMotion)
    {
        int fps = FpsFor(_sprite, reduceMotion);
        if (_shot is { } s)
        {
            var frames = _sprite.Frames(s.Animation);
            int i = Index(now - s.Start, fps);
            if (i < frames.Count) return new CompanionPose(s.Animation, frames[i], true);
            _shot = null;
            _loop = null; // the mood resumes from its first frame
        }
        string loop = walking && mood != CompanionMood.Exhausted && _sprite.Frames(CompanionSprite.Walk).Count > 0
            ? CompanionSprite.Walk
            : _sprite.Moods.TryGetValue(mood, out var m) ? m : _sprite.Moods[CompanionMood.Content];
        if (_loop != loop)
        {
            _loop = loop;
            _loopStart = now;
        }
        var loopFrames = _sprite.Frames(loop);
        if (loopFrames.Count == 0) return new CompanionPose(loop, 0, false);
        return new CompanionPose(loop, loopFrames[Index(now - _loopStart, fps) % loopFrames.Count], false);
    }

    private static int Index(TimeSpan elapsed, int fps) =>
        elapsed <= TimeSpan.Zero ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(elapsed.TotalSeconds * fps + 1e-9));
}
