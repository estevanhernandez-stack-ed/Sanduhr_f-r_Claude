using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Claude usage you can turn off (setup spec, sections 1 and 2): the widget pauses and resumes, and keeps the account.</summary>
[Collection("SettingsWindow")]
public sealed class WidgetUsageTests : AppWindowFixture
{
    [Fact]
    public void Usage_off_keeps_the_account_hides_the_sign_in_card_and_saves_the_choice() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        var (widget, accounts) = ScratchWidget("Personal");
        try
        {
            widget.SetUsageOn(false);
            Assert.False(widget.UsageOn);
            Assert.False(widget.ShowSignInPrompt);
            Assert.True(widget.ShowUsageOff);
            Assert.Single(accounts.ListAccounts());
            Assert.False(store.LoadSetup()!.ClaudeUsage);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Usage_back_on_with_no_account_brings_the_sign_in_card_back() => AppHost.Run(() =>
    {
        // No account, so resuming builds no client and reaches no network.
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget();
        try
        {
            widget.Start();
            Assert.False(widget.ShowSignInPrompt);
            widget.SetUsageOn(true);
            Assert.True(widget.UsageOn);
            Assert.Equal(SignInReason.FirstRun, widget.Reason);
            Assert.True(widget.ShowSignInPrompt);
            Assert.True(store.LoadSetup()!.ClaudeUsage);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Usage_on_with_no_account_shows_the_first_run_card() => AppHost.Run(() =>
    {
        new SettingsStore(ScratchPaths()).SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        var (widget, _) = ScratchWidget();
        try
        {
            widget.Start();
            Assert.Equal(SignInReason.FirstRun, widget.Reason);
            Assert.True(widget.ShowSignInPrompt);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_setup_with_usage_off_loads_paused_at_start() => AppHost.Run(() =>
    {
        new SettingsStore(ScratchPaths()).SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.Start();
            Assert.False(widget.UsageOn);
            Assert.Equal(SignInReason.None, widget.Reason);
            Assert.False(widget.ShowSignInPrompt);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void No_setup_reads_usage_as_on_so_an_upgraders_meters_never_vanish() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try { Assert.True(widget.UsageOn); }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Usage_off_draws_no_sign_in_line_on_the_Desk() => AppHost.Run(() =>
    {
        new SettingsStore(ScratchPaths()).SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        var (widget, _) = ScratchWidget();   // no account, so the Desk would ask to sign in
        try
        {
            widget.Start();
            Assert.True(widget.CurrentDeskUsage.SignInNeeded);
            widget.SetUsageOn(false);
            Assert.False(widget.CurrentDeskUsage.SignInNeeded);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_setup_still_pending_with_no_account_draws_no_sign_in_line_and_saves_nothing() => AppHost.Run(() =>
    {
        var store = new SettingsStore(ScratchPaths());
        var (widget, _) = ScratchWidget();   // no setup, no account: usage reads on in memory
        try
        {
            widget.Start();
            Assert.True(widget.UsageOn);
            Assert.True(widget.CurrentDeskUsage.SignInNeeded);
            int raised = 0;
            widget.DeskUsageChanged += () => raised++;
            widget.SetupPending = true;
            Assert.Equal(1, raised);
            Assert.False(widget.CurrentDeskUsage.SignInNeeded);
            Assert.Empty(widget.CurrentDeskUsage.Tiers);
            Assert.Null(store.LoadSetup());   // held back in memory only: the setup screen stays pending
            widget.SetupPending = false;
            Assert.True(widget.CurrentDeskUsage.SignInNeeded);
        }
        finally { widget.Dispose(); }
    });

    private static readonly string TwoLimits =
        $$"""{ "{{TierModel.FiveHour}}": {"utilization": 10, "resets_at": null}, "{{TierModel.SevenDay}}": {"utilization": 20, "resets_at": null} }""";

    /// <summary>Hands the widget a fetched answer without a network: the field a fetch sets.</summary>
    private static void Feed(WidgetViewModel widget, string json) =>
        typeof(WidgetViewModel).GetField("_lastData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(widget, JsonNode.Parse(json));

    [Fact]
    public void A_setup_still_pending_with_an_account_signed_in_still_draws_the_tiers() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");   // signed in before any setup: the hold ends with the account
        try
        {
            Feed(widget, TwoLimits);
            widget.SetLimitOrder([TierModel.FiveHour, TierModel.SevenDay]);
            Assert.NotEmpty(widget.CurrentDeskUsage.Tiers);
            widget.SetupPending = true;
            Assert.NotEmpty(widget.CurrentDeskUsage.Tiers);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Usage_off_pauses_the_seat_meters_and_following_and_usage_on_resumes_them() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.FollowAccount = true;
            Assert.True(widget.FollowerRunning);
            Assert.False(widget.SeatMetersPaused);

            widget.SetUsageOn(false);
            Assert.False(widget.FollowerRunning);
            Assert.True(widget.SeatMetersPaused);

            widget.FollowAccount = false;
            widget.FollowAccount = true;   // turning following on while usage is off starts nothing
            Assert.False(widget.FollowerRunning);

            widget.SetUsageOn(true, fetchNow: false);   // no client is built, so nothing reaches the network
            Assert.True(widget.FollowerRunning);
            Assert.False(widget.SeatMetersPaused);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Start_with_usage_off_leaves_the_seat_meters_paused_following_idle_and_the_timer_ticking() => AppHost.Run(() =>
    {
        new SettingsStore(ScratchPaths()).SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.FollowAccount = true;
            widget.Start();
            Assert.True(widget.SeatMetersPaused);
            Assert.False(widget.FollowerRunning);
            var timer = (System.Windows.Threading.DispatcherTimer)typeof(WidgetViewModel)
                .GetField("_refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!;
            Assert.True(timer.IsEnabled);   // a tick fetches nothing, but the local burn and the vault still refresh
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Usage_off_leaves_no_numbers_on_the_cards_the_Desk_the_tray_or_the_snapshot() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        TrayReading? lastTray = new(10, "5h");
        try
        {
            Feed(widget, TwoLimits);
            widget.SetLimitOrder([TierModel.FiveHour, TierModel.SevenDay]);
            Assert.NotEmpty(widget.Tiers);
            Assert.NotEmpty(widget.CurrentDeskUsage.Tiers);
            var snapshot = ScratchPaths().SnapshotFile;
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.WriteAllText(snapshot, "{}");
            widget.TrayReadingChanged += r => lastTray = r;
            int desk = 0;
            widget.DeskUsageChanged += () => desk++;

            widget.SetUsageOn(false);

            Assert.Empty(widget.Tiers);
            Assert.Empty(widget.CurrentDeskUsage.Tiers);
            Assert.Empty(widget.CurrentDeskUsage.LineTiers ?? []);
            Assert.False(widget.CurrentDeskUsage.SignInNeeded);
            Assert.Null(lastTray);
            Assert.Null(widget.CurrentTrayReading);
            Assert.False(File.Exists(snapshot));
            Assert.True(desk > 0);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_sign_in_while_usage_is_off_turns_it_on_and_the_rebuild_then_asks_for_it() => AppHost.Run(() =>
    {
        new SettingsStore(ScratchPaths()).SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget();   // no account, so the rebuild builds no client and reaches no network
        try
        {
            widget.Start();
            widget.SetUsageOn(true, fetchNow: false);
            Assert.True(widget.UsageOn);
            Assert.Equal(SignInReason.None, widget.Reason);   // fetchNow: false rebuilds nothing

            widget.ReloadAfterSignInAsync().GetAwaiter().GetResult();   // completes synchronously: no fetcher, no await
            Assert.Equal(SignInReason.FirstRun, widget.Reason);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_failed_save_still_turns_usage_on_for_a_sign_in_but_not_for_a_plain_toggle() => AppHost.Run(() =>
    {
        new SettingsStore(ScratchPaths()).SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget();
        try
        {
            var settings = ScratchPaths().SettingsFile;
            File.Delete(settings);
            Directory.CreateDirectory(settings);   // the file cannot be written
            Assert.ThrowsAny<Exception>(() => widget.SetUsageOn(true));
            Assert.False(widget.UsageOn);

            widget.SetUsageOn(true, fetchNow: false);
            Assert.True(widget.UsageOn);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_v1_config_becomes_an_account_before_the_setup_migration_counts() => AppHost.Run(() =>
    {
        var paths = ScratchPaths();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.LegacyConfigFile)!);
        File.WriteAllText(paths.LegacyConfigFile, "{\"session_key\": \"placeholder-legacy\"}");
        var (widget, accounts) = ScratchWidget();
        try
        {
            Assert.Empty(accounts.ListAccounts());
            widget.MigrateLegacyCredentials();
            Assert.Single(accounts.ListAccounts());
            widget.MigrateLegacyCredentials();   // idempotent
            Assert.Single(accounts.ListAccounts());
        }
        finally { widget.Dispose(); }
    });
}
