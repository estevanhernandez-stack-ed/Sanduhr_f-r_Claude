using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerRulesTests
{
    private const string Root = @"C:\Users\pat\Drawers";
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static DesktopItem File(string name, int daysOld = 0) =>
        new($@"C:\Users\pat\Desktop\{name}", false, Now.AddDays(-daysOld));

    [Theory]
    [InlineData("Screenshot 2026-10-29 101500.png", DrawerRuleKind.Screenshots, true)]
    [InlineData("screenshot (3).PNG", DrawerRuleKind.Screenshots, true)]
    [InlineData("holiday.png", DrawerRuleKind.Screenshots, false)]
    [InlineData("Screenshot notes.txt", DrawerRuleKind.Screenshots, false)]
    [InlineData("holiday.JPG", DrawerRuleKind.Images, true)]
    [InlineData("taxes.pdf", DrawerRuleKind.Documents, true)]
    [InlineData("setup.msix", DrawerRuleKind.Installers, true)]
    [InlineData("backup.7z", DrawerRuleKind.Archives, true)]
    [InlineData("Game.lnk", DrawerRuleKind.Shortcuts, true)]
    [InlineData("site.url", DrawerRuleKind.Shortcuts, true)]
    [InlineData("noextension", DrawerRuleKind.Documents, false)]
    public void Kind_rules_match_by_extension_and_screenshots_by_name_too(string name, DrawerRuleKind kind, bool match) =>
        Assert.Equal(match, DrawerRules.Matches(new DrawerRule(kind, "x"), File(name), Now));

    [Fact]
    public void Older_than_counts_whole_days_since_the_last_write()
    {
        var rule = new DrawerRule(DrawerRuleKind.OlderThan, "older", Days: 30);
        Assert.True(DrawerRules.Matches(rule, File("old.txt", 31), Now));
        Assert.False(DrawerRules.Matches(rule, File("new.txt", 29), Now));
    }

    [Fact]
    public void Name_rules_ignore_case_and_need_text()
    {
        Assert.True(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameContains, "r", Text: "invoice"), File("March INVOICE.pdf"), Now));
        Assert.True(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameStartsWith, "r", Text: "trail"), File("Trail-map.png"), Now));
        Assert.False(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameStartsWith, "r", Text: "trail"), File("my trail.png"), Now));
        Assert.False(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.NameContains, "r", Text: ""), File("anything.pdf"), Now));
    }

    [Fact]
    public void Folders_never_match_a_rule()
    {
        var folder = new DesktopItem(@"C:\Users\pat\Desktop\Project.zip", true, Now.AddDays(-400));
        foreach (var kind in Enum.GetValues<DrawerRuleKind>())
            Assert.False(DrawerRules.Matches(new DrawerRule(kind, "x", Text: "Project"), folder, Now));
    }

    [Fact]
    public void A_rule_that_is_off_never_matches() =>
        Assert.False(DrawerRules.Matches(new DrawerRule(DrawerRuleKind.Images, "x", On: false), File("a.png"), Now));

    [Fact]
    public void The_first_matching_rule_wins_and_unmatched_files_stay()
    {
        var rules = new[]
        {
            new DrawerRule(DrawerRuleKind.Screenshots, "screenshots"),
            new DrawerRule(DrawerRuleKind.Images, "images"),
        };
        var plan = DrawerRules.Plan(
            [File("Screenshot 1.png"), File("cat.png"), File("notes.txt")],
            rules, DrawerRules.DefaultDrawers(Root), Root, Now);
        Assert.Equal(DrawerPlanSource.Rules, plan.Source);
        // Moves come in path order (cat before Screenshot), each to its first matching rule's drawer.
        Assert.Equal(
            [("cat.png", "images"), ("Screenshot 1.png", "screenshots")],
            plan.Moves.Select(m => (Path.GetFileName(m.Source), m.DrawerId)));
    }

    [Fact]
    public void The_plan_carries_only_the_drawers_it_fills()
    {
        var plan = DrawerRules.Plan([File("a.pdf")], DrawerRules.Defaults, DrawerRules.DefaultDrawers(Root), Root, Now);
        Assert.Equal(["documents"], plan.Drawers.Select(d => d.Id));
        Assert.Equal(@"C:\Users\pat\Drawers\Documents", plan.Drawers[0].Home);
    }

    [Fact]
    public void A_rule_whose_drawer_is_missing_is_skipped()
    {
        var plan = DrawerRules.Plan([File("a.pdf")], [new DrawerRule(DrawerRuleKind.Documents, "gone")], [], Root, Now);
        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void Defaults_leave_shortcuts_alone_and_sweep_old_files_last()
    {
        Assert.False(DrawerRules.Defaults.Single(r => r.Kind == DrawerRuleKind.Shortcuts).On);
        Assert.Equal(DrawerRuleKind.OlderThan, DrawerRules.Defaults[^1].Kind);
        foreach (var r in DrawerRules.Defaults)
            Assert.Contains(DrawerRules.DefaultDrawers(Root), d => d.Id == r.DrawerId);
    }

    [Fact]
    public void Default_drawers_are_owned_and_live_directly_in_the_root()
    {
        foreach (var d in DrawerRules.DefaultDrawers(Root))
        {
            Assert.True(d.Owned);
            Assert.Equal(Root, Path.GetDirectoryName(d.Home));
            Assert.True(DrawerIds.IsValid(d.Id));
            Assert.True(DrawerNames.IsValid(d.Name));
        }
    }

    [Fact]
    public void A_rule_plan_passes_the_validator()
    {
        var roots = new DrawerRoots(@"C:\Users\pat\Desktop", @"C:\Users\Public\Desktop", Root);
        var plan = DrawerRules.Plan([File("a.pdf"), File("Screenshot 2.png"), File("old.txt", 90)],
            DrawerRules.Defaults, DrawerRules.DefaultDrawers(Root), Root, Now);
        var check = DrawerPlanValidator.Check(plan, roots, []);
        Assert.True(check.Ok, check.Error);
        Assert.Equal(3, check.Moves.Count);
    }
}
