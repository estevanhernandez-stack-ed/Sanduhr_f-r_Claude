using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>The agent layer on the widget: sources stack (Claude, then Codex), a combined two-tone bar per shared window, the Codex
/// card, and a tray that follows the primary source.</summary>
[Collection("SettingsWindow")]
public sealed class WidgetSourcesTests : AppWindowFixture
{
    private static readonly Dictionary<string, bool> ClaudeOnly = new() { ["claude"] = true, ["codex"] = false };
    private static readonly Dictionary<string, bool> Both = new() { ["claude"] = true, ["codex"] = true };
    private static readonly Dictionary<string, bool> CodexOnly = new() { ["claude"] = false, ["codex"] = true };

    private static JsonObject ClaudeData(double? five, double? week)
    {
        var data = new JsonObject();
        var now = DateTimeOffset.UtcNow;
        if (five is { } f)
            data["five_hour"] = new JsonObject { ["utilization"] = f, ["resets_at"] = now.AddHours(2).ToString("o", CultureInfo.InvariantCulture) };
        if (week is { } w)
            data["seven_day"] = new JsonObject { ["utilization"] = w, ["resets_at"] = now.AddDays(3).ToString("o", CultureInfo.InvariantCulture) };
        return data;
    }

    private static CodexState CodexOk(double? five, double? week, bool fresh = true, DateTimeOffset? asOf = null, params string[] freshTiers)
    {
        var tiers = new JsonObject();
        var now = DateTimeOffset.UtcNow;
        if (five is { } f)
            tiers["five_hour"] = new JsonObject { ["utilization"] = f, ["resets_at"] = now.AddHours(2).ToString("o", CultureInfo.InvariantCulture) };
        if (week is { } w)
            tiers["seven_day"] = new JsonObject { ["utilization"] = w, ["resets_at"] = now.AddDays(3).ToString("o", CultureInfo.InvariantCulture) };
        var keys = freshTiers.Length > 0 ? freshTiers : fresh ? tiers.Select(p => p.Key).ToArray() : [];
        return new CodexState(CodexReadStatus.Ok, tiers, asOf ?? now, fresh, "plus", new HashSet<string>(keys));
    }

    private SettingsStore Setup(Dictionary<string, bool> usage, string? primary = null)
    {
        var store = new SettingsStore(ScratchPaths());
        store.SaveSetup(new SetupSettings(true, usage));
        if (primary is not null) store.SaveSources(SourceSettings.Default with { Primary = primary });
        return store;
    }

    [Fact]
    public void Claude_only_has_one_section_and_no_combined_bars() => AppHost.Run(() =>
    {
        Setup(ClaudeOnly);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexState.Off);
            Assert.Single(widget.Sections);
            Assert.Equal("claude", widget.Sections[0].Source);
            Assert.Empty(widget.CombinedBars);
            Assert.Same(widget.Sections[0].Tiers, widget.Tiers);
            Assert.Equal(2, widget.Tiers.Count);
            Assert.False(widget.Sections[0].ShowHeader);   // one section: the widget looks as it always did
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Both_ticked_stack_claude_then_codex_with_two_combined_bars() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(50, 70));
            Assert.Equal(["claude", "codex"], widget.Sections.Select(s => s.Source));
            Assert.Equal(["Session (5hr)", "Weekly"], widget.CombinedBars.Select(b => b.WindowLabel));
            Assert.All(widget.Sections, s => Assert.True(s.ShowHeader));
            Assert.Equal(["Session (5hr)", "Weekly"], widget.Sections[1].Tiers.Select(t => t.Label));   // not "Weekly - All Models"
            Assert.Equal(["five_hour", "seven_day"], widget.Sections[1].Tiers.Select(t => t.TierKey));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Combined_weekly_bar_is_two_tone_when_codex_is_further_out() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(null, 40));
            widget.ApplyCodex(CodexOk(null, 70));
            var bar = Assert.Single(widget.CombinedBars);
            Assert.Equal("Weekly", bar.WindowLabel);
            Assert.Equal("claude", bar.Layout.Primary!.Source);
            Assert.Equal("codex", bar.Layout.Beyond!.Source);
            Assert.Equal(0.40, bar.Layout.Primary.To, 3);
            Assert.Equal(0.70, bar.Layout.Beyond.To, 3);
            Assert.Equal("Weekly. Claude 40%. Codex 70%.", bar.AccessibleName);
            Assert.NotNull(bar.BeyondBrush);
            Assert.Equal(Color.FromRgb(0xE0, 0x87, 0x5A), ((SolidColorBrush)bar.PrimaryBrush).Color);
            Assert.Equal(Color.FromRgb(0x2F, 0xB5, 0xA3), ((SolidColorBrush)bar.BeyondBrush!).Color);
            var tick = Assert.Single(bar.Ticks);
            Assert.Equal(0.70, tick.At, 3);
            Assert.False(string.IsNullOrWhiteSpace(tick.Tooltip));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_stale_codex_section_dims_and_says_as_of() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var asOf = new DateTimeOffset(DateTime.Today.AddHours(15).AddMinutes(42));
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(50, 70, fresh: false, asOf: asOf));
            var codex = widget.Sections[1];
            Assert.True(codex.Stale);
            Assert.Equal("as of 3:42 PM", codex.AsOfText);
            Assert.All(codex.Tiers, t => Assert.Equal(0.55, t.Opacity));
            Assert.All(widget.Sections[0].Tiers, t => Assert.Equal(1.0, t.Opacity));
            // The stale source's segment and tick draw at the same opacity.
            var weekly = widget.CombinedBars.Single(b => b.WindowLabel == "Weekly");
            Assert.Equal(0.55, weekly.BeyondBrush!.Opacity);
            Assert.Contains("as of earlier", weekly.AccessibleName);
            Assert.Equal("Codex weekly, 70%. As of 3:42 PM.", weekly.BeyondTooltip);
            Assert.Equal("Claude weekly, 40%.", weekly.PrimaryTooltip);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_tier_dims_on_its_own_freshness_not_the_sections() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            // Any tier fresh makes the section fresh, but only the five-hour number can be trusted.
            widget.ApplyCodex(CodexOk(0, 70, fresh: true, asOf: DateTimeOffset.UtcNow.AddHours(-3), "five_hour"));
            var codex = widget.Sections[1];
            Assert.False(codex.Stale);
            Assert.StartsWith("as of ", codex.AsOfText);   // shown whether or not the reading is stale
            Assert.Equal(1.0, codex.Tiers.Single(t => t.TierKey == "five_hour").Opacity);
            Assert.Equal(0.55, codex.Tiers.Single(t => t.TierKey == "seven_day").Opacity);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void An_old_reading_names_its_date() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var asOf = new DateTimeOffset(new DateTime(2026, 10, 22, 15, 42, 0, DateTimeKind.Local));
            widget.ApplyCodex(CodexOk(50, 70, fresh: false, asOf: asOf));
            Assert.Equal("as of Oct 22, 3:42 PM", widget.Sections.Single(s => s.Source == "codex").AsOfText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void No_limits_shows_the_run_codex_line_and_no_cards() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(new CodexState(CodexReadStatus.NoLimits, new JsonObject(), null, false, null, new HashSet<string>()));
            var codex = widget.Sections.Single(s => s.Source == "codex");
            Assert.Equal("Run Codex once to see your limits.", codex.EmptyText);
            Assert.Empty(codex.Tiers);
            Assert.Empty(widget.CombinedBars);

            widget.ApplyCodex(new CodexState(CodexReadStatus.Unreadable, new JsonObject(), null, false, null, new HashSet<string>()));
            Assert.Equal("Couldn't read Codex's logs.", codex.EmptyText);
            widget.ApplyCodex(CodexOk(10, 20));
            Assert.Null(codex.EmptyText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_real_not_installed_says_so_while_the_placeholder_says_run_codex() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexState.Off);   // not polled yet
            var codex = widget.Sections.Single(s => s.Source == "codex");
            Assert.Equal("Run Codex once to see your limits.", codex.EmptyText);
            widget.ApplyCodex(new CodexState(CodexReadStatus.NotInstalled, new JsonObject(), null, false, null, new HashSet<string>()));
            Assert.Equal("Codex isn't installed on this PC.", codex.EmptyText);
            Assert.Empty(codex.Tiers);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_source_tooltip_truncates_like_the_cards() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(50, 70.9, fresh: true, asOf: DateTimeOffset.UtcNow));
            var weekly = widget.CombinedBars.Single(b => b.WindowLabel == "Weekly");
            Assert.Equal("Codex weekly, 70%.", weekly.BeyondTooltip);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Collapsing_a_source_saves_and_shows_a_one_line_summary() => AppHost.Run(() =>
    {
        var store = Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(40, 12));
            var codex = widget.Sections.Single(s => s.Source == "codex");
            Assert.False(codex.Collapsed);
            codex.ToggleCollapseCommand.Execute(null);
            Assert.True(codex.Collapsed);
            Assert.Contains("codex", store.LoadSources().Collapsed);
            Assert.Equal("5h 40% · wk 12%", codex.CollapsedSummary);
            Assert.Equal("Expand Codex", codex.CollapseName);
            codex.ToggleCollapseCommand.Execute(null);
            Assert.DoesNotContain("codex", store.LoadSources().Collapsed);
            Assert.Equal("Collapse Codex", codex.CollapseName);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_tray_follows_the_primary_and_names_a_source_further_out() => AppHost.Run(() =>
    {
        Setup(Both, primary: "codex");
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.TrayMode = TrayMode.Weekly;
            widget.ApplyClaudeForTests(ClaudeData(10, 30));
            widget.ApplyCodex(CodexOk(20, 55));
            Assert.Equal(55, widget.CurrentTrayReading!.Percent);
            Assert.DoesNotContain("Claude", widget.CurrentTrayGlyph.Tooltip.Replace("Sanduhr für Claude", ""));

            widget.ApplyClaudeForTests(ClaudeData(10, 80));
            Assert.Equal(55, widget.CurrentTrayReading!.Percent);
            Assert.Equal("Sanduhr für Claude — Codex week 55%. Claude week 80%.", widget.CurrentTrayGlyph.Tooltip);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_tray_names_only_a_fresh_source_further_out() => AppHost.Run(() =>
    {
        Setup(Both);   // primary claude
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.TrayMode = TrayMode.Weekly;
            widget.ApplyClaudeForTests(ClaudeData(10, 30));
            widget.ApplyCodex(CodexOk(20, 55, fresh: false));
            Assert.Equal(30, widget.CurrentTrayReading!.Percent);
            Assert.DoesNotContain("Codex week", widget.CurrentTrayGlyph.Tooltip);
            widget.ApplyCodex(CodexOk(20, 55));
            Assert.Equal("Sanduhr für Claude — Claude week 30%. Codex week 55%.", widget.CurrentTrayGlyph.Tooltip);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_codex_only_user_sees_only_the_codex_section_with_no_sign_in_card() => AppHost.Run(() =>
    {
        Setup(CodexOnly);
        var (widget, _) = ScratchWidget();   // no Claude account
        try
        {
            widget.Start();
            widget.ApplyCodex(CodexOk(15, 25));
            Assert.Equal(["codex"], widget.Sections.Select(s => s.Source));
            Assert.True(widget.Sections[0].ShowHeader);   // a lone Codex section keeps its name, as-of and chevron
            Assert.StartsWith("as of ", widget.Sections[0].AsOfText);
            Assert.False(widget.ShowSignInPrompt);
            Assert.Equal(SignInReason.None, widget.Reason);
            Assert.True(widget.AnyUsageOn);
            Assert.False(widget.ShowUsageOff);
            Assert.Empty(widget.Tiers);
            Assert.Empty(widget.CombinedBars);
            Assert.Equal(TrayGlyphKind.Number, widget.CurrentTrayGlyph.Kind);   // the primary falls to Codex
            Assert.Equal(25, widget.CurrentTrayReading!.Percent);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Turning_claude_usage_off_leaves_codex_on() => AppHost.Run(() =>
    {
        var store = Setup(Both);
        var (widget, _) = ScratchWidget();
        try
        {
            widget.SetUsageOn(false);
            var setup = store.LoadSetup()!;
            Assert.False(setup.ClaudeUsage);
            Assert.True(setup.CodexUsage);
            Assert.True(widget.AnyUsageOn);
            Assert.Equal(["codex"], widget.Sections.Select(s => s.Source));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_collapsed_lone_claude_section_comes_back_expanded_with_no_header() => AppHost.Run(() =>
    {
        var store = Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(50, 70));
            var claude = widget.Sections[0];
            claude.ToggleCollapseCommand.Execute(null);
            Assert.True(claude.Collapsed);
            Assert.True(claude.ShowSummary);
            // Untick Codex: Claude is alone, so nothing could expand it. It is drawn expanded, header hidden, whatever was saved.
            store.SaveSetup(new SetupSettings(true, ClaudeOnly));
            widget.RefreshSources();
            claude = Assert.Single(widget.Sections);
            Assert.False(claude.ShowHeader);
            Assert.True(claude.ShowCards);
            Assert.False(claude.ShowSummary);
            Assert.Contains("claude", store.LoadSources().Collapsed);   // the saved choice is kept for when Codex comes back
            store.SaveSetup(new SetupSettings(true, Both));
            widget.RefreshSources();
            Assert.True(widget.Sections[0].ShowSummary);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_lone_codex_section_keeps_its_header_and_can_still_collapse() => AppHost.Run(() =>
    {
        var store = Setup(CodexOnly);
        var (widget, _) = ScratchWidget();
        try
        {
            widget.ApplyCodex(CodexOk(40, 12));
            var codex = Assert.Single(widget.Sections);
            Assert.True(codex.ShowHeader);
            codex.ToggleCollapseCommand.Execute(null);
            Assert.True(codex.ShowSummary);
            Assert.False(codex.ShowCards);
            Assert.Contains("codex", store.LoadSources().Collapsed);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_collapsed_summary_truncates_like_the_cards() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyCodex(CodexOk(39.9, 12.7));
            var codex = widget.Sections[1];
            Assert.Equal("5h 39% · wk 12%", codex.CollapsedSummary);
            Assert.Equal("39%", codex.Tiers[0].ValueText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Codex_toasts_use_the_card_labels() => AppHost.Run(() =>
    {
        Assert.Equal("Session (5hr)", SourceSectionViewModel.CodexTierLabel("five_hour"));
        Assert.Equal("Weekly", SourceSectionViewModel.CodexTierLabel("seven_day"));
    });

    [Fact]
    public void Changing_the_setup_refreshes_the_sections() => AppHost.Run(() =>
    {
        var store = Setup(ClaudeOnly);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            Assert.Single(widget.Sections);
            store.SaveSetup(new SetupSettings(true, Both));
            widget.RefreshSources();
            Assert.Equal(["claude", "codex"], widget.Sections.Select(s => s.Source));
            store.SaveSetup(new SetupSettings(true, ClaudeOnly));
            widget.RefreshSources();
            Assert.Single(widget.Sections);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_stacked_window_names_its_bars_sections_and_chevrons_in_words() => AppHost.Run(() =>
    {
        Setup(Both);
        var (widget, _) = ScratchWidget("Personal");
        var window = new MainWindow { DataContext = widget };
        OffScreen(window);
        window.Show();
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(50, 70));
            window.UpdateLayout();
            var names = NamesOf(window);
            Assert.Empty(LeaksIn(names));
            Assert.Contains("Weekly. Claude 40%. Codex 70%.", names);
            Assert.Contains("Session (5hr). Claude 30%. Codex 50%.", names);
            Assert.Contains("Collapse Claude", names);
            Assert.Contains("Collapse Codex", names);
            Assert.Contains(names, n => n.StartsWith("Session (5hr), ") && n.Contains('%'));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    // ---- the Desk's feed: Codex tiers, per-tier staleness, the primary and the colors ----

    [Fact]
    public void The_desk_usage_carries_codex_tiers_per_tier_staleness_the_primary_and_the_colors() => AppHost.Run(() =>
    {
        Setup(Both, primary: "codex");
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            int raised = 0;
            widget.DeskUsageChanged += () => raised++;
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            int afterClaude = raised;
            widget.ApplyCodex(CodexOk(50, 70.9, fresh: true, asOf: DateTimeOffset.UtcNow, "five_hour"));
            Assert.True(raised > afterClaude);   // Codex arriving redraws the Desk
            var usage = widget.CurrentDeskUsage;
            var codex = usage.TiersOf("codex");
            Assert.Equal(["five_hour", "seven_day"], codex.Select(t => t.Key));
            Assert.Equal([50, 70.9], codex.Select(t => t.Utilization));
            Assert.NotNull(codex[0].ResetsAt);
            Assert.False(usage.IsTierStale("codex", "five_hour"));
            Assert.True(usage.IsTierStale("codex", "seven_day"));   // not in the fresh set
            Assert.Equal("codex", usage.SourcePrimary);
            Assert.Equal("#E0875A", usage.SourceColors!["claude"], ignoreCase: true);
            Assert.Equal("#2FB5A3", usage.SourceColors["codex"], ignoreCase: true);
            Assert.Equal(["five_hour", "seven_day"], usage.Tiers.Select(t => t.Key));   // Claude's own tiers are untouched
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_desk_usage_has_no_codex_tiers_with_codex_off_or_unreadable() => AppHost.Run(() =>
    {
        Setup(ClaudeOnly);
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.ApplyClaudeForTests(ClaudeData(30, 40));
            widget.ApplyCodex(CodexOk(50, 70));   // a state arrives, but Codex usage is not on
            Assert.Null(widget.CurrentDeskUsage.SourceTiers);
            Assert.Empty(widget.CurrentDeskUsage.TiersOf("codex"));
            Assert.Equal("claude", widget.CurrentDeskUsage.SourcePrimary);
        }
        finally { widget.Dispose(); }

        Setup(Both);
        var (second, _) = ScratchWidget("Personal");
        try
        {
            second.ApplyClaudeForTests(ClaudeData(30, 40));
            second.ApplyCodex(CodexState.Off);
            Assert.Empty(second.CurrentDeskUsage.TiersOf("codex"));
        }
        finally { second.Dispose(); }
    });
}
