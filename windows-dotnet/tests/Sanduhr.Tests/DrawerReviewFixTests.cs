using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The final review's Important findings on drawers plan 1, each pinned before its fix.</summary>
public class DrawerReviewFixTests
{
    private const string Desk = @"C:\Users\pat\Desktop";
    private const string Root = @"C:\Users\pat\Desktop\Drawers";
    private static readonly DrawerRoots Roots = new(Desk, @"C:\Users\Public\Desktop", Root);
    private static readonly DrawerDef Shots = new("screenshots", "Screenshots", $@"{Root}\Screenshots", true);
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static DrawerPlan Agent(IReadOnlyList<DrawerDef> drawers, params DrawerMove[] moves) =>
        new(DrawerPlanSource.Agent, "codex", drawers, moves);

    // #1 A journal whose JSON is valid but not an object reads as empty.
    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    public void A_journal_that_is_not_an_object_reads_as_empty(string json) =>
        Assert.Empty(DrawerJournal.Decode(json).Entries);

    // #2 Duplicate ids: settings keep the first, and the validator never throws on them.
    [Fact]
    public void Settings_keep_the_first_drawer_of_a_duplicated_id()
    {
        var s = DrawerSettings.Decode(JsonNode.Parse("""
        {"drawers": [
          {"id": "images", "name": "Images", "home": "C:/a/Images", "owned": true},
          {"id": "images", "name": "Pictures", "home": "C:/a/Pictures", "owned": true}
        ]}
        """));
        Assert.Equal("Images", Assert.Single(s.Drawers).Name);
    }

    [Fact]
    public void The_validator_takes_the_first_of_duplicated_existing_ids()
    {
        var dup = Shots with { Name = "Other", Home = $@"{Root}\Other" };
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove($@"{Desk}\a.png", "screenshots")), Roots, [Shots, dup]);
        Assert.True(c.Ok, c.Error);
    }

    // #3 A colon past the drive letter (an NTFS stream) is never a drawer path.
    [Theory]
    [InlineData(@"C:\Users\pat\Desktop\Drawers::$INDEX_ALLOCATION")]
    [InlineData(@"C:\Users\pat\Desktop\a.txt:secret")]
    public void Stream_syntax_sources_are_refused(string source)
    {
        var c = DrawerPlanValidator.Check(Agent([Shots], new DrawerMove(source, "screenshots")), Roots, []);
        Assert.False(c.Ok);
    }

    [Fact]
    public void Stream_syntax_homes_are_refused() =>
        Assert.False(DrawerPlanValidator.Check(Agent([new DrawerDef("x", "X", $@"{Root}\x:y", true)]), Roots, []).Ok);

    // #4 A new drawer's home is the root plus its own name, and no two drawers share a folder.
    [Theory]
    [InlineData("Fine", "CON")]
    [InlineData("Fine", "Other")]
    public void A_new_drawers_home_must_be_its_own_name_in_the_root(string name, string leaf) =>
        Assert.False(DrawerPlanValidator.Check(Agent([new DrawerDef("fine", name, $@"{Root}\{leaf}", true)]), Roots, []).Ok);

    [Fact]
    public void Two_drawers_cannot_share_a_folder()
    {
        var a = new DrawerDef("a", "Work", $@"{Root}\Work", true);
        var b = new DrawerDef("b", "WORK", $@"{Root}\WORK", true);
        Assert.False(DrawerPlanValidator.Check(Agent([a, b]), Roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(Agent([b]), Roots, [a]).Ok);
    }

    // #5 A source that holds the root or a drawer home is refused, not only one equal to it.
    [Fact]
    public void A_source_holding_the_root_is_refused()
    {
        var roots = new DrawerRoots(Desk, @"C:\Users\Public\Desktop", $@"{Desk}\Sorted\Drawers");
        var shots = new DrawerDef("screenshots", "Screenshots", $@"{Desk}\Sorted\Drawers\Screenshots", true);
        Assert.False(DrawerPlanValidator.Check(Agent([], new DrawerMove($@"{Desk}\Sorted", "screenshots")), roots, [shots]).Ok);
    }

    [Fact]
    public void A_source_holding_a_view_drawers_folder_is_refused()
    {
        var web = new DrawerDef("web", "Web", $@"{Desk}\Projects\web", false);
        Assert.False(DrawerPlanValidator.Check(Agent([], new DrawerMove($@"{Desk}\Projects", "screenshots")), Roots, [Shots, web]).Ok);
    }

    // #6 A root that is the Desktop, holds it, sits in the Public Desktop or is a drive root is refused, and return does nothing with it.
    [Theory]
    [InlineData(@"C:\Users\pat\Desktop")]
    [InlineData(@"C:\Users\pat")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users\Public\Desktop\Drawers")]
    public void An_unsafe_root_refuses_every_plan(string root)
    {
        Assert.False(DrawerPaths.IsSafeRoot(root, Desk, @"C:\Users\Public\Desktop"));
        var roots = new DrawerRoots(Desk, @"C:\Users\Public\Desktop", root);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [], []), roots, []).Ok);
    }

    [Fact]
    public void A_root_elsewhere_in_the_profile_is_fine() =>
        Assert.True(DrawerPaths.IsSafeRoot(@"C:\Users\pat\Documents\Drawers", Desk, @"C:\Users\Public\Desktop"));

    [Fact]
    public void Return_never_schedules_the_Desktop_for_removal()
    {
        var d = new DrawerDef("d", "D", $@"{Desk}\D", true);
        var plan = DrawerReturn.Plan([d], Desk, Desk, _ => [new($@"{Desk}\D\x.txt", false, Now)], _ => false);
        Assert.Empty(plan.Moves);
        Assert.Empty(plan.HomesToRemove);
    }

    // #7 Hidden and system files, and the Desktop's own housekeeping files, never match a rule.
    [Fact]
    public void Hidden_files_never_match()
    {
        var item = new DesktopItem($@"{Desk}\old.txt", false, Now.AddDays(-400), Hidden: true);
        foreach (var r in DrawerRules.Defaults)
            Assert.False(DrawerRules.Matches(r with { On = true }, item, Now));
    }

    [Theory]
    [InlineData("desktop.ini")]
    [InlineData("Thumbs.db")]
    [InlineData("~$report.docx")]
    public void Housekeeping_files_never_match_even_when_not_flagged_hidden(string name)
    {
        var item = new DesktopItem($@"{Desk}\{name}", false, Now.AddDays(-400));
        foreach (var r in DrawerRules.Defaults)
            Assert.False(DrawerRules.Matches(r with { On = true }, item, Now));
    }
}
