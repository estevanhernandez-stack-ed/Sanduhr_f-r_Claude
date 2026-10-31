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
}
