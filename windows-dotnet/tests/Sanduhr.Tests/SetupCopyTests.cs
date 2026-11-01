using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SetupCopyTests
{
    [Fact]
    public void Unticking_usage_says_the_account_stays_and_names_the_add_ons_only_when_installed()
    {
        var before = new SetupChoices(true, false, true);
        var after = before with { ClaudeUsage = false };
        Assert.Equal([SetupCopy.UsageOffLine], SetupChange.Lines(before, after, 0, addonsInstalled: false, hasAccount: true));
        Assert.Equal([SetupCopy.UsageOffLine, SetupCopy.AddOnsStayLine], SetupChange.Lines(before, after, 0, addonsInstalled: true, hasAccount: true));
    }

    [Fact]
    public void The_account_stays_saved_line_needs_an_account_to_stay()
    {
        var before = new SetupChoices(true, false, true);
        var after = before with { ClaudeUsage = false };
        Assert.Empty(SetupChange.Lines(before, after, 0, addonsInstalled: false, hasAccount: false));
        Assert.Equal([SetupCopy.AddOnsStayLine], SetupChange.Lines(before, after, 0, addonsInstalled: true, hasAccount: false));
    }

    [Fact]
    public void Unticking_drawers_counts_the_files_going_back_and_desktop_says_the_layout_is_kept()
    {
        var before = new SetupChoices(true, true, false);
        Assert.Equal(["3 drawer files go back to your Desktop."], SetupChange.Lines(before, before with { Drawers = false }, 3, false, true));
        Assert.Equal(["1 drawer file goes back to your Desktop."], SetupChange.Lines(before, before with { Drawers = false }, 1, false, true));
        Assert.Equal([SetupCopy.DesktopOffLine], SetupChange.Lines(before, before with { Desktop = false }, 0, false, true));
        Assert.Empty(SetupChange.Lines(before, before, 0, true, true));
    }

    [Fact]
    public void Nothing_ticked_is_blocked_with_a_reason()
    {
        Assert.Equal(SetupCopy.PickOne, SetupChange.Blocked(new SetupChoices(false, false, false)));
        Assert.Null(SetupChange.Blocked(new SetupChoices(false, false, true)));
    }

    [Fact]
    public void The_copy_names_no_AI_product_but_Claude_and_has_no_em_dash()
    {
        foreach (var s in SetupCopy.All)
        {
            Assert.DoesNotContain("\u2014", s);
            foreach (var mark in new[] { "ChatGPT", "Gemini", "Copilot", "OpenAI", "Codex" }) Assert.DoesNotContain(mark, s);
        }
    }
}
