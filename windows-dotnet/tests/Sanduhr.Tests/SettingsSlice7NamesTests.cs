using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 7: streamer mode's words. The spec's sentences are pinned word for word; the rest are checked for the copy rules.</summary>
public class SettingsSlice7NamesTests
{
    [Fact]
    public void The_switch_and_its_caption_are_the_specs_words()
    {
        Assert.Equal("Streamer mode", SettingsNames.StreamerSection);
        Assert.Equal("Streamer mode: hide accounts, folders and projects", SettingsNames.StreamerSwitch);
        Assert.Equal("Live numbers stay. Names become codenames until you choose to show one.", SettingsNames.StreamerCaption);
        Assert.Equal("Claude Code's own screen isn't covered. Check what it shows before you share it.", SettingsNames.StreamerCantReach);
        Assert.Equal("Shuffle codenames…", SettingsNames.ShuffleCodenames);
        Assert.Equal("Every codename changes. Viewers following a name will lose it.", SettingsNames.ShuffleDetail);
        Assert.Equal("Shuffle", SettingsNames.ShuffleConfirm);
        Assert.Equal("Cancel", SettingsNames.ShuffleCancel);
        Assert.Equal("New codenames drawn.", SettingsNames.ShuffleDone);
        Assert.Equal("Show the real name", SettingsNames.ShowRealNameSection);
        Assert.Equal("Show the real name on stream", SettingsNames.ShowRealNameOnStream);
        Assert.Equal("Exports keep real names.", SettingsNames.ExportsKeepRealNames);
    }

    /// <summary>P18: the statusline consent says the folder name on the line is the codename while streamer mode is on.</summary>
    [Fact]
    public void The_statusline_consent_names_the_codename_clause()
    {
        Assert.Contains("(its codename while streamer mode is on)", SettingsNames.StatuslineNameClause);
        Assert.Contains("SANDUHR_LABEL", SettingsNames.StatuslineNameClause);
    }

    [Fact]
    public void The_remove_detail_drops_the_history_file_while_the_name_is_hidden()
    {
        Assert.Contains("(history.Work.json)", SettingsNames.RemoveAccountDetail("Work"));
        Assert.DoesNotContain("history.", SettingsNames.RemoveAccountDetail("Dune Fox", nameHidden: true));
        Assert.Contains("its meter history, its Claude Code folder link", SettingsNames.RemoveAccountDetail("Dune Fox", nameHidden: true));
    }

    /// <summary>Exactly true (decision 11, P4; P6 option A): what streamer mode can't reach is named, and MCP answers are among it, paths included.</summary>
    [Fact]
    public void What_it_cant_reach_names_typed_text_Explorer_the_sign_in_window_the_MCP_answers_and_the_watcher_cards()
    {
        var more = SettingsNames.StreamerCantReachMore;
        Assert.Contains("type yourself", more);
        Assert.Contains("Explorer", more);
        Assert.Contains("Save dialog", more);
        Assert.Contains("sign-in window", more);
        Assert.Contains("MCP server", more);
        Assert.Contains("full paths and folder names", more);
        Assert.Contains("watcher cards (the folders in an absolute Windows path in it are hidden)", more);
    }

    /// <summary>The demo line: the search word "demo" lands on it (spec), and it says how demo mode starts, as DemoMode does.</summary>
    [Fact]
    public void The_demo_line_says_how_demo_mode_starts()
    {
        Assert.Contains(DemoMode.Switch, SettingsNames.StreamerDemoLine);
        Assert.Contains(DemoMode.EnvironmentVariable + "=1", SettingsNames.StreamerDemoLine);
        Assert.Contains("--demo", SettingsNames.StreamerDemoLine);
        Assert.Contains("SANDUHR_DEMO=1", SettingsNames.StreamerDemoLine);
    }

    [Fact]
    public void The_banner_and_its_link_are_the_specs()
    {
        Assert.Equal("Streamer mode is on: names are codenames.", SettingsNames.StreamerBanner);
        Assert.Equal("Privacy settings…, Streamer mode", SettingsNames.StreamerBannerLinkName);
        Assert.Equal("Streamer mode on", SettingsNames.StreamerIndicatorName);
        Assert.Equal("Streamer mode · ", SettingsNames.StreamerTooltipPrefix);
    }

    [Fact]
    public void The_tray_tooltip_starts_with_streamer_mode_only_while_it_is_on()
    {
        Assert.Equal("Streamer mode · Sanduhr für Claude, 5h 42%", SettingsNames.TrayTooltip(true, "Sanduhr für Claude, 5h 42%"));
        Assert.Equal("Sanduhr für Claude, 5h 42%", SettingsNames.TrayTooltip(false, "Sanduhr für Claude, 5h 42%"));
    }

    [Fact]
    public void The_chip_is_spoken_with_the_indicator_after_the_name()
    {
        Assert.Equal("Dune Fox, Streamer mode on", SettingsNames.StreamerChipName("Dune Fox", streamer: true));
        Assert.Equal("Work", SettingsNames.StreamerChipName("Work", streamer: false));
    }

    [Fact]
    public void Allowlist_rows_name_the_codename_and_show_the_real_name_only_while_off()
    {
        Assert.Equal("Show the real name on stream, Dune Fox", SettingsNames.AllowlistRowName("Dune Fox"));
        Assert.Equal(@"Dune Fox · ~\.claude-work", SettingsNames.AllowlistRowOff("Dune Fox", @"~\.claude-work"));
        foreach (var c in Enum.GetValues<CodenameCategory>())
            Assert.False(string.IsNullOrWhiteSpace(SettingsNames.CodenameCategoryTitle(c)));
        Assert.Equal(Enum.GetValues<CodenameCategory>().Length,
            Enum.GetValues<CodenameCategory>().Select(SettingsNames.CodenameCategoryTitle).Distinct().Count());
    }

    [Fact]
    public void Every_slice_7_sentence_passes_the_copy_rules()
    {
        string[] all =
        [
            SettingsNames.StreamerSwitch, SettingsNames.StreamerCaption, SettingsNames.StreamerCantReach, SettingsNames.StreamerCantReachMore,
            SettingsNames.StreamerDemoLine, SettingsNames.ShuffleCaption, SettingsNames.ShuffleQuestion, SettingsNames.ShuffleDetail,
            SettingsNames.ShuffleDone, SettingsNames.AllowlistCaptionOn, SettingsNames.AllowlistCaptionOff, SettingsNames.AllowlistEmpty,
            SettingsNames.StreamerBanner, SettingsNames.ExportsKeepRealNames, SettingsNames.PlacesHiddenNote, SettingsNames.EndpointHiddenNote,
        ];
        foreach (var s in all)
        {
            Assert.Empty(SettingsNames.PathProblems(s));
            Assert.Empty(SettingsNames.RetiredNames(s));
            Assert.DoesNotContain("—", s);
            if (s != SettingsNames.StreamerSwitch) // the switch is a label, the spec's words without a period
                Assert.Matches(@"[.?]$", s.TrimEnd());
            Assert.DoesNotContain("  ", s);
            // No literal timing in copy.
            Assert.DoesNotMatch(@"\b\d+\s*(ms|s|sec|seconds?|minutes?|min|hours?)\b", s);
        }
    }

    [Fact]
    public void The_two_anchors_are_the_ids_the_boosts_already_name()
    {
        Assert.Equal("streamer", SettingsIndex.Streamer);
        Assert.Equal("streamer-allowlist", SettingsIndex.StreamerAllowlist);
        Assert.Contains(SettingsIndex.Boosts, b => b.PageId == "privacy" && b.Anchor == SettingsIndex.Streamer);
        Assert.Contains(SettingsIndex.Boosts, b => b.PageId == "privacy" && b.Anchor == SettingsIndex.StreamerAllowlist);
    }
}
