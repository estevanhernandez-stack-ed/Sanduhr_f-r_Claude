using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The Sources page's place in Settings (agent layer, Task 8): first in Usage, titled, searchable.</summary>
public sealed class SettingsSourcesPageTests
{
    [Fact]
    public void Sources_is_the_first_page_of_Usage_before_Claude_Usage_and_Publish()
    {
        var usage = SettingsNames.Sidebar.Single(g => g.Title == "Usage");
        Assert.Equal([SettingsPage.UsageSources, SettingsPage.ClaudeUsage, SettingsPage.Publish], usage.Pages.ToArray());
        Assert.Equal("Sources", SettingsNames.PageTitle(SettingsPage.UsageSources));
    }

    [Theory]
    [InlineData("sources")]
    [InlineData("codex")]
    [InlineData("primary")]
    [InlineData("colors")]
    public void Search_finds_the_Sources_page(string query)
    {
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.UsageSources);
    }

    [Fact]
    public void The_section_entry_has_an_anchor_and_the_page_is_not_a_streamer_page()
    {
        Assert.NotNull(SettingsIndex.Find(SettingsPage.UsageSources, SettingsIndex.SourcesList));
        Assert.DoesNotContain(SettingsPage.UsageSources, SettingsIndex.StreamerPages);   // it draws no name, path or usage
    }

    [Theory]
    [InlineData("#2FB5A3", true)]
    [InlineData("#abcdef", true)]
    [InlineData("teal", false)]
    [InlineData("#12345", false)]
    [InlineData("2FB5A3", false)]
    [InlineData("#2FB5A3 ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsColor_accepts_only_a_hash_and_six_hex_digits(string? text, bool expected) =>
        Assert.Equal(expected, SourceSettings.IsColor(text));

    [Fact]
    public void The_setup_screen_names_Codex_and_the_off_line_for_it()
    {
        Assert.Equal("Codex isn't installed on this PC.", SetupCopy.CodexNotInstalled);
        var before = new SetupChoices(false, false, false, CodexUsage: true);
        Assert.Equal([SetupCopy.CodexOffLine],
            SetupChange.Lines(before, before with { CodexUsage = false, Desktop = true }, 0, false, false));
        Assert.Empty(SetupChange.Lines(before, before with { Desktop = true }, 0, false, false));
    }
}
