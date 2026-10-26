using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5: what Alerts, Each limit and the Widget page read and write through the widget. A scratch widget in demo
/// mode supplies sample limits (Session and Weekly at least) over scratch folders; every write lands in the scratch settings.json.
/// </summary>
[Collection("SettingsWindow")]
public sealed class WidgetLimitsTests : AppWindowFixture
{
    private JsonObject Settings() => (JsonObject)JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;

    private static string[] Strings(JsonNode? node) => node is JsonArray a ? [.. a.Select(n => n!.GetValue<string>())] : [];

    /// <summary>Hands the widget a fetched answer without a network: the field a fetch sets.</summary>
    private static void Feed(WidgetViewModel widget, string json) =>
        typeof(WidgetViewModel).GetField("_lastData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(widget, JsonNode.Parse(json));

    [Fact]
    public void Muting_hiding_and_showing_a_limit_reach_the_rows_and_the_file_and_say_so() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.StartDemo();
            int raised = 0;
            widget.LimitsChanged += () => raised++;

            widget.SetWarnings(TierModel.SevenDay, false);
            widget.SetShown(TierModel.FiveHour, false);

            var rows = widget.LimitRows();
            Assert.False(rows.Single(r => r.Key == TierModel.SevenDay).WarnOn);
            Assert.False(rows.Single(r => r.Key == TierModel.FiveHour).Shown);
            Assert.Equal([TierModel.SevenDay], Strings(Settings()["alert_muted_tiers"]));
            Assert.Equal([TierModel.FiveHour], Strings(Settings()["hidden_tiers"]));
            Assert.True(raised >= 2, $"LimitsChanged raised {raised} times");

            widget.SetShown(TierModel.FiveHour, true);
            widget.SetWarnings(TierModel.SevenDay, true);
            Assert.All(widget.LimitRows(), r => Assert.True(r.WarnOn && r.Shown));
            Assert.Empty(Strings(Settings()["hidden_tiers"]));
            Assert.Empty(Strings(Settings()["alert_muted_tiers"]));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_muted_limit_the_account_no_longer_has_still_has_a_row() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");   // signed in, nothing fetched
        try
        {
            widget.SetWarnings(TierModel.SevenDayOpus, false);

            var row = Assert.Single(widget.LimitRows());
            Assert.Equal(TierModel.SevenDayOpus, row.Key);
            Assert.False(row.WarnOn);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_limit_with_null_utilization_gets_no_row_because_it_never_gets_a_card() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            Feed(widget, $$"""{ "{{TierModel.FiveHour}}": {"utilization": 10, "resets_at": null}, "{{TierModel.SevenDayOpus}}": {"utilization": null, "resets_at": null} }""");

            var row = Assert.Single(widget.LimitRows());
            Assert.Equal(TierModel.FiveHour, row.Key);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Removing_the_active_account_empties_the_shown_order_and_says_so() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal", "Other");
        try
        {
            Feed(widget, $$"""{ "{{TierModel.FiveHour}}": {"utilization": 10, "resets_at": null}, "{{TierModel.SevenDay}}": {"utilization": 20, "resets_at": null} }""");
            widget.SetLimitOrder([TierModel.SevenDay, TierModel.FiveHour]);
            Assert.NotEmpty(widget.ShownLimitOrder);
            int raised = 0;
            widget.LimitsChanged += () => raised++;

            // The sign-out continues on this dispatcher thread: pump it rather than block on the task.
            var signOut = widget.SignOutAccountAsync("Personal");
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!signOut.IsCompleted && DateTime.UtcNow < until)
            {
                Pump();
                Thread.Sleep(15);
            }
            Assert.True(signOut.IsCompletedSuccessfully, "sign-out did not finish");

            Assert.Empty(widget.ShownLimitOrder);
            Assert.True(raised >= 1, "LimitsChanged was not raised");
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Moving_a_limit_rewrites_the_order_and_the_cards_follow() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.StartDemo();
            var shown = widget.ShownLimitOrder;
            Assert.True(shown.Count >= 2, "demo mode shows at least two limits");

            var moved = LimitSettings.Move(shown, shown[1], -1)!;
            widget.SetLimitOrder(moved);

            Assert.Equal(moved, widget.ShownLimitOrder);
            Assert.Equal(moved, widget.Tiers.Select(t => t.TierKey));
            Assert.Equal(moved, Strings(Settings()["tier_order"]).Take(moved.Count));
        }
        finally { widget.Dispose(); }
    });

    private static readonly string ThreeLimits =
        $$"""{ "{{TierModel.FiveHour}}": {"utilization": 10, "resets_at": null}, "{{TierModel.SevenDay}}": {"utilization": 20, "resets_at": null}, "{{TierModel.IguanaNecktie}}": {"utilization": 30, "resets_at": "2099-01-01T00:00:00Z"} }""";

    /// <summary>A scratch widget showing three limits, rendered through the public order setter.</summary>
    private (WidgetViewModel Widget, string[] Order) ThreeShown()
    {
        var (widget, _) = ScratchWidget("Personal");
        Feed(widget, ThreeLimits);
        widget.SetLimitOrder([TierModel.FiveHour, TierModel.SevenDay, TierModel.IguanaNecktie]);
        Assert.Equal(3, widget.ShownLimitOrder.Count);
        return (widget, [.. widget.ShownLimitOrder]);
    }

    [Fact]
    public void A_drag_on_the_widget_reaches_the_shown_order_once_and_a_Settings_move_builds_on_it() => AppHost.Run(() =>
    {
        var (widget, order) = ThreeShown();
        try
        {
            int raised = 0;
            widget.LimitsChanged += () => raised++;

            widget.MoveTier(2, 0);

            Assert.Equal([order[2], order[0], order[1]], widget.ShownLimitOrder);
            Assert.Equal(1, raised);

            var moved = LimitSettings.Move(widget.ShownLimitOrder, order[0], 1)!;   // Move down on the old first
            widget.SetLimitOrder(moved);
            Assert.Equal([order[2], order[1], order[0]], widget.ShownLimitOrder);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Hiding_a_limit_from_the_card_menu_takes_it_out_of_the_shown_order_once() => AppHost.Run(() =>
    {
        var (widget, order) = ThreeShown();
        try
        {
            int raised = 0;
            widget.LimitsChanged += () => raised++;

            widget.HideTier(order[1]);

            Assert.Equal([order[0], order[2]], widget.ShownLimitOrder);
            Assert.Equal(1, raised);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Hiding_a_limit_until_it_resets_takes_it_out_of_the_shown_order_once() => AppHost.Run(() =>
    {
        var (widget, order) = ThreeShown();
        try
        {
            int raised = 0;
            widget.LimitsChanged += () => raised++;
            Assert.True(widget.CanHideUntilReset(TierModel.IguanaNecktie));

            widget.HideUntilReset(TierModel.IguanaNecktie);

            Assert.DoesNotContain(TierModel.IguanaNecktie, widget.ShownLimitOrder);
            Assert.Equal(2, widget.ShownLimitOrder.Count);
            Assert.Equal(1, raised);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Graph_style_and_keep_on_top_write_their_keys() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.SetSparklineStyle(SparklineStyle.Horizon);
            widget.SetPinned(true);

            Assert.Equal(SparklineStyle.Horizon, widget.SparklineStyle);
            Assert.True(widget.Pinned);
            Assert.Equal("Horizon", (string?)Settings()["sparkline_style"]);
            Assert.True((bool?)Settings()["pinned"]);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Ending_Just_the_Desk_saves_it_off_and_brings_back_a_reminder_it_had_silenced() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveJustTheDesk(new JustTheDeskState(true, SilencedReminder: true));
        store.SaveDeskSignInReminder(false);
        var (widget, _) = ScratchWidget();
        try
        {
            Assert.True(widget.JustTheDesk);

            Assert.True(widget.EndJustTheDesk());

            Assert.False(widget.JustTheDesk);
            Assert.False(store.LoadJustTheDesk().On);
            Assert.True(store.LoadDeskSignInReminder());
            Assert.False(widget.EndJustTheDesk());   // not on: nothing to end
        }
        finally { widget.Dispose(); }
    });
}
