using System.Globalization;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>FORMAT 6: mood from pacing, the triggers, which frame plays, and the roaming route and clock.</summary>
public class CompanionBehaviourTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A tier <paramref name="through"/> of the way through its window (0 to 1) at <see cref="Now"/>.</summary>
    private static DeskTier Tier(string key, double util, double through, DateTimeOffset? at = null)
    {
        var span = key == TierModel.FiveHour ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
        return new DeskTier(key, util, Iso((at ?? Now) + span * (1 - through)));
    }

    private static DeskUsage Usage(params DeskTier[] tiers) => new(tiers, Now, false, false, null);

    private static CompanionSprite Hourglass() =>
        CompanionSprite.Parse(File.ReadAllBytes(Path.Combine(WidgetPackFixtures.Root, "packs", "hourglass", "sprite.json")))!;

    // ---- mood ----

    [Fact]
    public void Under_pace_is_content_near_is_focused_over_is_worried()
    {
        Assert.Equal(CompanionMood.Content, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 10, 0.5), Tier(TierModel.SevenDay, 20, 0.5)), Now));
        Assert.Equal(CompanionMood.Focused, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 48, 0.5), Tier(TierModel.SevenDay, 20, 0.5)), Now));
        Assert.Equal(CompanionMood.Worried, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 70, 0.5), Tier(TierModel.SevenDay, 20, 0.5)), Now));
        Assert.Equal(CompanionMood.Worried, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 10, 0.5), Tier(TierModel.SevenDay, 55, 0.5)), Now));
    }

    [Fact]
    public void Weekly_at_85_is_worried_even_under_pace_and_the_limit_is_exhausted()
    {
        Assert.Equal(CompanionMood.Worried, CompanionMoods.FromUsage(Usage(Tier(TierModel.SevenDay, 85, 0.95)), Now));
        Assert.Equal(CompanionMood.Content, CompanionMoods.FromUsage(Usage(Tier(TierModel.SevenDay, 84, 0.99)), Now));
        Assert.Equal(CompanionMood.Exhausted, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 100, 0.9)), Now));
        Assert.Equal(CompanionMood.Exhausted, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 20, 0.9), Tier(TierModel.SevenDay, 104, 0.9)), Now));
    }

    [Fact]
    public void Signed_out_no_numbers_or_nothing_used_is_content()
    {
        Assert.Equal(CompanionMood.Content, CompanionMoods.FromUsage(DeskUsage.Empty, Now));
        Assert.Equal(CompanionMood.Content, CompanionMoods.FromUsage(new DeskUsage([Tier(TierModel.FiveHour, 100, 0.5)], Now, true, false, null), Now));
        Assert.Equal(CompanionMood.Content, CompanionMoods.FromUsage(Usage(Tier(TierModel.FiveHour, 0, 0.02)), Now));
    }

    [Theory]
    [InlineData(CompanionMood.Content, "content")]
    [InlineData(CompanionMood.Focused, "focused")]
    [InlineData(CompanionMood.Worried, "worried")]
    [InlineData(CompanionMood.Exhausted, "exhausted")]
    [InlineData(CompanionMood.Celebrate, "celebrate")]
    public void Each_mood_has_its_format_key(CompanionMood mood, string key)
    {
        Assert.Equal(key, CompanionMoods.Key(mood));
        Assert.Contains(key, PackValidator.MoodKeys);
    }

    [Fact]
    public void Each_trigger_has_its_format_key() =>
        Assert.Equal(PackValidator.TriggerKeys, Enum.GetValues<CompanionTrigger>().Select(CompanionMoods.Key));

    // ---- the brain ----

    [Fact]
    public void The_weekly_reset_celebrates_for_ten_minutes_then_the_numbers_mood_again()
    {
        var brain = new CompanionBrain();
        brain.ObserveUsage(Usage(Tier(TierModel.SevenDay, 60, 0.999)), 80, 95, Now);
        Assert.Equal(CompanionMood.Content, brain.Mood(Now));
        var later = Now + TimeSpan.FromMinutes(20);
        brain.ObserveUsage(Usage(Tier(TierModel.SevenDay, 0, 0.001, later)), 80, 95, later);
        Assert.Equal(CompanionMood.Celebrate, brain.Mood(later));
        Assert.Equal(CompanionMood.Celebrate, brain.Mood(later + TimeSpan.FromMinutes(9)));
        Assert.Equal(CompanionMood.Content, brain.Mood(later + CompanionBrain.CelebrateFor));
    }

    [Fact]
    public void Exhausted_sleeps_until_the_session_resets_and_the_reset_celebrates()
    {
        var brain = new CompanionBrain();
        brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 100, 0.9)), 80, 95, Now);
        Assert.Equal(CompanionMood.Exhausted, brain.Mood(Now));
        var reset = Now + TimeSpan.FromMinutes(40);
        brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 2, 0.01, reset)), 80, 95, reset);
        Assert.Equal(CompanionMood.Celebrate, brain.Mood(reset));
    }

    [Fact]
    public void A_session_reset_from_a_calm_window_is_not_a_party()
    {
        var brain = new CompanionBrain();
        brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 30, 0.9)), 80, 95, Now);
        var reset = Now + TimeSpan.FromHours(1);
        brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 0, 0.01, reset)), 80, 95, reset);
        Assert.Equal(CompanionMood.Content, brain.Mood(reset));
    }

    [Fact]
    public void Reset_time_jitter_is_not_a_reset()
    {
        var brain = new CompanionBrain();
        brain.ObserveUsage(Usage(new DeskTier(TierModel.SevenDay, 60, Iso(Now + TimeSpan.FromDays(1)))), 80, 95, Now);
        brain.ObserveUsage(Usage(new DeskTier(TierModel.SevenDay, 61, Iso(Now + TimeSpan.FromDays(1) + TimeSpan.FromSeconds(30)))), 80, 95, Now);
        Assert.NotEqual(CompanionMood.Celebrate, brain.Mood(Now));
    }

    [Fact]
    public void Crossing_an_alert_threshold_upward_plays_near_limit_once()
    {
        var brain = new CompanionBrain();
        Assert.Empty(brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 85, 0.9)), 80, 95, Now)); // the first reading is the baseline
        Assert.Empty(brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 88, 0.9)), 80, 95, Now));
        Assert.Equal([CompanionTrigger.NearLimit], brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 96, 0.9)), 80, 95, Now));
        Assert.Empty(brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 97, 0.9)), 80, 95, Now));
        Assert.Equal([CompanionTrigger.NearLimit], brain.ObserveUsage(Usage(Tier(TierModel.FiveHour, 100, 0.9)), 80, 95, Now));
        // Down and back up over the warn line crosses again.
        brain.ObserveUsage(Usage(Tier(TierModel.SevenDay, 50, 0.5)), 80, 95, Now);
        Assert.Equal([CompanionTrigger.NearLimit], brain.ObserveUsage(Usage(Tier(TierModel.SevenDay, 81, 0.5)), 80, 95, Now));
    }

    private static Watcher W(string id, WatcherState state) => new()
    {
        Id = id, Source = WatcherSource.Agent, Title = id, State = state, Started = Now, Touched = Now,
    };

    [Fact]
    public void A_watcher_waiting_on_you_plays_needs_you_and_one_that_passed_plays_job_passed()
    {
        var brain = new CompanionBrain();
        Assert.Empty(brain.ObserveWatchers([W("a:1", WatcherState.Waiting)])); // baseline: already waiting at start
        Assert.Empty(brain.ObserveWatchers([W("a:1", WatcherState.Waiting), W("a:2", WatcherState.Running)]));
        Assert.Equal([CompanionTrigger.NeedsYou], brain.ObserveWatchers([W("a:1", WatcherState.Running), W("a:2", WatcherState.Waiting)]));
        Assert.Equal([CompanionTrigger.JobPassed], brain.ObserveWatchers([W("a:1", WatcherState.Passed), W("a:2", WatcherState.Waiting)]));
        Assert.Empty(brain.ObserveWatchers([W("a:1", WatcherState.Passed)]));
        Assert.Empty(brain.ObserveWatchers([W("a:3", WatcherState.Failed)]));
        Assert.Equal([CompanionTrigger.NeedsYou, CompanionTrigger.JobPassed],
            brain.ObserveWatchers([W("a:4", WatcherState.Waiting), W("a:5", WatcherState.Passed)]));
    }

    [Fact]
    public void Twenty_minutes_without_input_plays_idle_once_until_input_returns()
    {
        var brain = new CompanionBrain();
        Assert.Null(brain.ObserveActivity(Now, Now + TimeSpan.FromMinutes(19)));
        Assert.Equal(CompanionTrigger.Idle, brain.ObserveActivity(Now, Now + TimeSpan.FromMinutes(20)));
        Assert.Null(brain.ObserveActivity(Now, Now + TimeSpan.FromMinutes(45)));
        var back = Now + TimeSpan.FromMinutes(50);
        Assert.Null(brain.ObserveActivity(back, back));
        Assert.Equal(CompanionTrigger.Idle, brain.ObserveActivity(back, back + CompanionBrain.IdleAfter));
    }

    // ---- frames ----

    [Fact]
    public void The_mood_loops_at_the_packs_fps_from_its_first_frame()
    {
        var a = new CompanionAnimator(Hourglass());
        Assert.Equal(new CompanionPose("sit", 4, false), a.Pose(CompanionMood.Content, false, Now, false));
        Assert.Equal(5, a.Pose(CompanionMood.Content, false, Now + TimeSpan.FromSeconds(0.125), false).Frame);
        Assert.Equal(4, a.Pose(CompanionMood.Content, false, Now + TimeSpan.FromSeconds(0.25), false).Frame);
        // A new mood starts at its own first frame.
        var t = Now + TimeSpan.FromSeconds(1.3);
        Assert.Equal(new CompanionPose("worry", 16, false), a.Pose(CompanionMood.Worried, false, t, false));
        Assert.Equal(new CompanionPose("focus", 18, false), a.Pose(CompanionMood.Focused, false, t, false));
        Assert.Equal("sleep", a.Pose(CompanionMood.Exhausted, false, t, false).Animation);
        Assert.Equal("celebrate", a.Pose(CompanionMood.Celebrate, false, t, false).Animation);
    }

    [Theory]
    [InlineData(CompanionTrigger.NeedsYou, "pop")]
    [InlineData(CompanionTrigger.JobPassed, "cheer")]
    [InlineData(CompanionTrigger.NearLimit, "worry")]
    [InlineData(CompanionTrigger.Idle, "nap")]
    public void A_trigger_plays_its_animation_once_then_the_mood_resumes(CompanionTrigger trigger, string animation)
    {
        var sprite = Hourglass();
        var a = new CompanionAnimator(sprite);
        a.Pose(CompanionMood.Content, false, Now, false);
        a.Play(trigger, Now);
        var frames = sprite.Frames(animation);
        for (int i = 0; i < frames.Count; i++)
        {
            var at = Now + TimeSpan.FromSeconds(i / 8.0);
            Assert.Equal(new CompanionPose(animation, frames[i], true), a.Pose(CompanionMood.Content, true, at, false));
            Assert.True(a.Playing(at, false));
            Assert.False(a.CanWalk(CompanionMood.Content, at, false));
        }
        var after = Now + TimeSpan.FromSeconds(frames.Count / 8.0);
        Assert.False(a.Playing(after, false));
        Assert.Equal(new CompanionPose("sit", 4, false), a.Pose(CompanionMood.Content, false, after, false));
    }

    [Fact]
    public void Reduce_motion_draws_at_one_frame_a_second()
    {
        var sprite = Hourglass();
        Assert.Equal(1, CompanionAnimator.FpsFor(sprite, true));
        Assert.Equal(8, CompanionAnimator.FpsFor(sprite, false));
        var a = new CompanionAnimator(sprite);
        Assert.Equal(4, a.Pose(CompanionMood.Content, false, Now, true).Frame);
        Assert.Equal(4, a.Pose(CompanionMood.Content, false, Now + TimeSpan.FromSeconds(0.9), true).Frame);
        Assert.Equal(5, a.Pose(CompanionMood.Content, false, Now + TimeSpan.FromSeconds(1), true).Frame);
        a.Play(CompanionTrigger.NeedsYou, Now + TimeSpan.FromSeconds(2));
        Assert.True(a.Playing(Now + TimeSpan.FromSeconds(5.5), true)); // four frames take four seconds
        Assert.False(a.Playing(Now + TimeSpan.FromSeconds(6), true));
    }

    [Fact]
    public void Walking_draws_walk_but_exhausted_never_walks()
    {
        var a = new CompanionAnimator(Hourglass());
        Assert.Equal(new CompanionPose("walk", 0, false), a.Pose(CompanionMood.Content, true, Now, false));
        Assert.Equal(3, a.Pose(CompanionMood.Content, true, Now + TimeSpan.FromSeconds(3 / 8.0), false).Frame);
        Assert.Equal("sleep", a.Pose(CompanionMood.Exhausted, true, Now, false).Animation);
        Assert.False(a.CanWalk(CompanionMood.Exhausted, Now, false));
        Assert.True(a.CanWalk(CompanionMood.Worried, Now, false));
    }

    // ---- roaming ----

    private static readonly DeskRect Screen = new(0, 0, 1000, 600);

    [Fact]
    public void Along_the_bottom_it_walks_there_and_back_facing_its_way()
    {
        var path = CompanionPath.Build([CompanionEdge.Bottom], Screen, 64, 64)!;
        Assert.Equal(1000 - 64, path.Length, 6);
        Assert.Equal(new CompanionSpot(32, 600, false, CompanionEdge.Bottom), path.At(0));
        Assert.Equal(new CompanionSpot(132, 600, false, CompanionEdge.Bottom), path.At(100));
        var back = path.At(path.Length + 100);
        Assert.Equal(968 - 100, back.X, 6);
        Assert.True(back.FacingLeft);
        Assert.Equal(32, path.At(2 * path.Length).X, 6);
    }

    [Fact]
    public void Bottom_left_and_right_make_one_route_up_both_sides()
    {
        var path = CompanionPath.Build([CompanionEdge.Bottom, CompanionEdge.Left, CompanionEdge.Right], Screen, 64, 64)!;
        double side = 600 - 64, bottom = 1000 - 64;
        Assert.Equal(2 * side + bottom, path.Length, 6);
        var start = path.At(0);
        Assert.Equal((32.0, 64.0, CompanionEdge.Left), (start.X, start.Y, start.Edge));
        Assert.False(start.FacingLeft);
        Assert.Equal(CompanionEdge.Bottom, path.At(side + 10).Edge);
        var right = path.At(side + bottom + 10);
        Assert.Equal((968.0, 590.0, CompanionEdge.Right), (right.X, right.Y, right.Edge));
        Assert.True(right.FacingLeft);
    }

    [Fact]
    public void Without_the_bottom_it_hops_between_the_sides()
    {
        var path = CompanionPath.Build([CompanionEdge.Left, CompanionEdge.Right], Screen, 64, 64)!;
        Assert.Equal(2 * (600 - 64), path.Length, 6);
        Assert.Equal(CompanionEdge.Left, path.At(600 - 64 - 1).Edge);
        var hop = path.At(600 - 64 + 1);
        Assert.Equal(CompanionEdge.Right, hop.Edge);
        Assert.Equal(968, hop.X, 6);
    }

    [Fact]
    public void A_route_starts_nearest_the_anchor_and_a_screen_too_small_has_none()
    {
        var path = CompanionPath.Build([CompanionEdge.Bottom], Screen, 64, 64)!;
        Assert.Equal(0, path.Nearest(0, 600), 6);
        Assert.Equal(path.Length, path.Nearest(1000, 600), 6);
        Assert.Equal(500 - 32, path.Nearest(500, 0), 6);
        Assert.Null(CompanionPath.Build([CompanionEdge.Bottom], new DeskRect(0, 0, 60, 600), 64, 64));
        Assert.Null(CompanionPath.Build([], Screen, 64, 64));
    }

    [Fact]
    public void The_roamer_walks_then_rests_and_holds_still_when_it_may_not_walk()
    {
        var r = new CompanionRoamer { Start = 100 };
        Assert.True(r.Walking);
        for (int i = 0; i < 48; i++) r.Advance(TimeSpan.FromMilliseconds(250), true); // 12 s
        Assert.Equal(12, r.WalkedSeconds, 3);
        Assert.False(r.Walking);
        Assert.Equal(100 + 12 * 30, r.Distance(30), 3);
        for (int i = 0; i < 16; i++) r.Advance(TimeSpan.FromMilliseconds(250), true); // resting 4 s
        Assert.Equal(12, r.WalkedSeconds, 3);
        r.Advance(TimeSpan.FromSeconds(5), false); // asleep, covered or a trigger playing: nothing moves
        Assert.Equal(12, r.WalkedSeconds, 3);
        for (int i = 0; i < 20; i++) r.Advance(TimeSpan.FromMilliseconds(250), true); // the rest ends, walking again
        Assert.True(r.Walking);
        Assert.True(r.WalkedSeconds > 12);
        // A long gap is not a sprint.
        var s = new CompanionRoamer();
        s.Advance(TimeSpan.FromHours(3), true);
        Assert.Equal(1, s.WalkedSeconds, 6);
    }
}
