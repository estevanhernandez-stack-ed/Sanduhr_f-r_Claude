using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2 reach (spec, "Deep links and entry points"): page ids, links, the --settings switch, the
/// section each menu opens and where an opening Settings lands.</summary>
public class SettingsLinkTests
{
    [Fact]
    public void Every_page_has_a_lowercase_id_that_reads_back()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var id = SettingsIndex.PageId(page);
            Assert.Equal(id.ToLowerInvariant(), id);
            Assert.Equal(page, SettingsIndex.PageFromId(id));
            Assert.Equal(page, SettingsIndex.PageFromId(id.ToUpperInvariant()));
        }
        Assert.Equal("claudeusage", SettingsIndex.PageId(SettingsPage.ClaudeUsage));
        Assert.Null(SettingsIndex.PageFromId("3"));
        Assert.Null(SettingsIndex.PageFromId("desk look"));
        Assert.Null(SettingsIndex.PageFromId(""));
        Assert.Null(SettingsIndex.PageFromId(null));
        Assert.Null(SettingsIndex.PageFromId("desklook")); // slice 3's page: unknown until then
    }

    [Theory]
    [InlineData("desk#pieces", SettingsPage.Desk, "pieces")]
    [InlineData("DESK#Pieces", SettingsPage.Desk, "pieces")]
    [InlineData("  claudeusage#sessions  ", SettingsPage.ClaudeUsage, "sessions")]
    [InlineData("desk", SettingsPage.Desk, null)]
    [InlineData("desk#", SettingsPage.Desk, null)]
    [InlineData("desk#nowhere", SettingsPage.Desk, null)]
    [InlineData("about#version", SettingsPage.About, "version")]
    [InlineData("history#chart", SettingsPage.History, "chart")]
    [InlineData("widget", SettingsPage.Widget, null)]
    [InlineData("widget#tray", SettingsPage.Widget, null)]
    [InlineData("", SettingsPage.General, null)]
    [InlineData("nonsense#tray", SettingsPage.General, null)]
    [InlineData("3", SettingsPage.General, null)]
    public void A_link_reads_as_a_page_and_an_anchor_on_it(string text, SettingsPage page, string? anchor) =>
        Assert.Equal(new SettingsLink(page, anchor), SettingsIndex.ParseLink(text));

    [Fact]
    public void No_text_is_no_link() => Assert.Null(SettingsIndex.ParseLink(null));

    [Theory]
    [InlineData(new[] { "--settings", "desk#pieces" }, "desk#pieces")]
    [InlineData(new[] { "--settings=about#version" }, "about#version")]
    [InlineData(new[] { "--SETTINGS", "alerts" }, "alerts")]
    [InlineData(new[] { "--demo", "--settings" }, "general")]
    [InlineData(new[] { "--settings", "--demo" }, "general")]
    [InlineData(new[] { "--settings=" }, "general")]
    [InlineData(new[] { "--demo" }, null)]
    [InlineData(new string[0], null)]
    public void The_settings_switch_reads_its_value(string[] args, string? expected) =>
        Assert.Equal(expected, SettingsIndex.LinkArgument(args));

    [Fact]
    public void A_launch_with_the_switch_opens_that_place()
    {
        Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.Sessions),
            SettingsIndex.LinkFromArgs(["--settings", "claudeusage#sessions"]));
        Assert.Null(SettingsIndex.LinkFromArgs(["--demo"]));
    }
}
