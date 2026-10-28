using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 5: Alerts, Each limit (WF13) and Widget, Limit order (WF24). The rows a person sees and the order a move
/// writes, from the widget's last answer and the three per-limit keys. Canonical tiers only: nothing here registers a dynamic tier.
/// </summary>
[Collection(TierRegistryCollection.Name)]
public class LimitSettingsTests
{
    private static readonly string[] None = [];

    [Fact]
    public void Rows_list_the_limits_in_the_last_answer_in_canonical_order_with_their_switches()
    {
        var rows = LimitSettings.Rows([TierModel.SevenDay, TierModel.FiveHour], None, None, None);

        Assert.Equal([TierModel.FiveHour, TierModel.SevenDay], rows.Select(r => r.Key));
        Assert.Equal("Session (5hr)", rows[0].Label);
        Assert.All(rows, r => Assert.True(r.WarnOn && r.Shown && !r.HiddenUntilReset));
    }

    [Fact]
    public void A_muted_or_hidden_limit_missing_from_the_last_answer_still_has_a_row_so_it_can_be_restored()
    {
        var rows = LimitSettings.Rows([TierModel.FiveHour], hidden: [TierModel.SevenDaySonnet], muted: [TierModel.SevenDayOpus], None);

        Assert.Equal([TierModel.FiveHour, TierModel.SevenDaySonnet, TierModel.SevenDayOpus], rows.Select(r => r.Key));
        var sonnet = rows.Single(r => r.Key == TierModel.SevenDaySonnet);
        Assert.False(sonnet.Shown);
        Assert.True(sonnet.WarnOn);
        var opus = rows.Single(r => r.Key == TierModel.SevenDayOpus);
        Assert.False(opus.WarnOn);
        Assert.True(opus.Shown);
    }

    [Fact]
    public void A_limit_hidden_until_it_resets_reads_not_shown_and_says_why()
    {
        var row = Assert.Single(LimitSettings.Rows(None, None, None, hiddenUntilReset: [TierModel.IguanaNecktie]));

        Assert.Equal(TierModel.IguanaNecktie, row.Key);
        Assert.False(row.Shown);
        Assert.True(row.HiddenUntilReset);
        Assert.True(row.WarnOn);
    }

    [Fact]
    public void A_key_the_registry_does_not_know_gets_no_row() =>
        Assert.Empty(LimitSettings.Rows(["no_such_tier"], ["also_unknown"], ["nor_this"], ["nope"]));

    [Fact]
    public void A_key_named_twice_is_one_row()
    {
        var row = Assert.Single(LimitSettings.Rows([TierModel.FiveHour, TierModel.FiveHour], [TierModel.FiveHour], [TierModel.FiveHour], None));

        Assert.False(row.Shown);
        Assert.False(row.WarnOn);
    }

    [Fact]
    public void Move_swaps_with_the_neighbour_and_refuses_to_leave_the_list()
    {
        IReadOnlyList<string> shown = [TierModel.FiveHour, TierModel.SevenDay, TierModel.SevenDaySonnet];

        Assert.Equal([TierModel.SevenDay, TierModel.FiveHour, TierModel.SevenDaySonnet], LimitSettings.Move(shown, TierModel.SevenDay, -1));
        Assert.Equal([TierModel.FiveHour, TierModel.SevenDaySonnet, TierModel.SevenDay], LimitSettings.Move(shown, TierModel.SevenDay, +1));
        Assert.Null(LimitSettings.Move(shown, TierModel.FiveHour, -1));
        Assert.Null(LimitSettings.Move(shown, TierModel.SevenDaySonnet, +1));
        Assert.Null(LimitSettings.Move(shown, TierModel.SevenDayOpus, +1));
        Assert.Null(LimitSettings.Move(shown, TierModel.SevenDay, 0));
        Assert.Null(LimitSettings.Move(shown, TierModel.FiveHour, 2));
    }

    [Fact]
    public void The_saved_order_is_the_shown_order_then_every_other_known_limit_once()
    {
        var order = LimitSettings.SavedOrder([TierModel.SevenDay, TierModel.FiveHour], saved: [TierModel.SevenDayOpus, "unknown"]);

        Assert.Equal([TierModel.SevenDay, TierModel.FiveHour, TierModel.SevenDayOpus], order.Take(3));
        Assert.Equal(order.Count, order.Distinct().Count());
        Assert.DoesNotContain("unknown", order);
        Assert.Equal(TierModel.ResolveOrder(null).Count, order.Count);
    }
}
