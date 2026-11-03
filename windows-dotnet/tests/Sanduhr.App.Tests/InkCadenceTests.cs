using Sanduhr.App.Desk.UnderIcons;

namespace Sanduhr.App.Tests;

/// <summary>When the Desk under the icons is copied: on a change, through an animation, and slowly otherwise, never once a second blind.</summary>
public sealed class InkCadenceTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void At_rest_with_nothing_changing_it_checks_only_at_the_slow_safety_net()
    {
        var c = new InkCadence();
        Assert.True(c.Resting(T0));
        Assert.Equal(InkCadence.Rest, c.Checked(T0, changed: false));
        Assert.True(InkCadence.Rest >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_wake_at_rest_books_one_check_soon_and_more_wakes_do_not_push_it_back()
    {
        var c = new InkCadence();
        Assert.Equal(InkCadence.Soon, c.Wake(T0, TimeSpan.Zero));
        Assert.Null(c.Wake(T0.AddMilliseconds(10), TimeSpan.Zero));
        Assert.Null(c.Wake(T0.AddMilliseconds(20), TimeSpan.Zero));
        // The booked check finds nothing new: back to rest, and the next wake books again.
        Assert.Equal(InkCadence.Rest, c.Checked(T0.AddMilliseconds(60), changed: false));
        Assert.Equal(InkCadence.Soon, c.Wake(T0.AddSeconds(1), TimeSpan.Zero));
    }

    [Fact]
    public void A_change_a_wake_finds_at_rest_is_one_step_and_the_copy_rests_again_at_once()
    {
        var c = new InkCadence();
        Assert.Equal(InkCadence.Soon, c.Wake(T0, TimeSpan.Zero));
        // A CPU reading every 5 s: one copy each, not one plus three follow-ups.
        Assert.Equal(InkCadence.Rest, c.Checked(T0.AddMilliseconds(50), changed: true));
        Assert.True(c.Resting(T0.AddMilliseconds(50)));
    }

    [Fact]
    public void An_unannounced_change_the_safety_net_finds_keeps_checks_fast_for_a_few_then_rests()
    {
        var c = new InkCadence();
        var t = T0;
        Assert.Equal(InkCadence.Hot, c.Checked(t, changed: true));
        for (int i = 1; i < InkCadence.QuietBeforeRest; i++)
            Assert.Equal(InkCadence.Hot, c.Checked(t = t.AddMilliseconds(33), changed: false));
        Assert.Equal(InkCadence.Rest, c.Checked(t.AddMilliseconds(33), changed: false));
    }

    [Fact]
    public void An_animation_keeps_checks_fast_through_a_hold_with_no_change_until_it_ends()
    {
        var c = new InkCadence();
        Assert.Equal(InkCadence.Soon, c.Wake(T0, TimeSpan.FromSeconds(1.5)));
        // The glow's hold: many unchanged checks in a row, still fast.
        var t = T0;
        for (int i = 0; i < 20; i++)
            Assert.Equal(InkCadence.Hot, c.Checked(t = t.AddMilliseconds(50), changed: false));
        Assert.Equal(InkCadence.Rest, c.Checked(T0.AddSeconds(1.6), changed: false));
    }

    [Fact]
    public void A_wake_while_checks_are_fast_leaves_the_timer_alone_but_extends_the_animation()
    {
        var c = new InkCadence();
        c.Checked(T0, changed: true);
        Assert.Null(c.Wake(T0, TimeSpan.FromSeconds(2)));
        Assert.Equal(InkCadence.Hot, c.Checked(T0.AddSeconds(1.9), changed: false));
        Assert.Equal(InkCadence.Hot, c.Checked(T0.AddSeconds(1.95), changed: false));
        Assert.Equal(InkCadence.Hot, c.Checked(T0.AddSeconds(1.99), changed: false));
        Assert.Equal(InkCadence.Rest, c.Checked(T0.AddSeconds(2.1), changed: false));
    }

    [Fact]
    public void A_wake_with_no_listener_costs_nothing() => InkWake.Now(1.5);
}
