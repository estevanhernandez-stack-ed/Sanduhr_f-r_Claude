using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerPlanValidatorTests
{
    private static readonly DrawerRoots Roots = new(@"C:\Users\pat\Desktop", @"C:\Users\Public\Desktop", @"C:\Users\pat\Drawers");
    private static readonly DrawerDef Shots = new("screenshots", "Screenshots", @"C:\Users\pat\Drawers\Screenshots", true);
    private static readonly DrawerDef Downloads = new("downloads", "Downloads", @"C:\Users\pat\Downloads", false);

    private static DrawerPlan Agent(IReadOnlyList<DrawerDef> drawers, params DrawerMove[] moves) =>
        new(DrawerPlanSource.Agent, "codex", drawers, moves);

    [Fact]
    public void A_desktop_file_into_a_new_owned_drawer_passes()
    {
        var c = DrawerPlanValidator.Check(Agent([Shots], new DrawerMove(@"C:\Users\pat\Desktop\shot.png", "screenshots")), Roots, []);
        Assert.True(c.Ok, c.Error);
        Assert.Single(c.Moves);
        Assert.Equal([Shots], c.Drawers);
    }

    [Fact]
    public void A_move_into_an_existing_drawer_needs_no_definition()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\a.zip", "downloads")), Roots, [Downloads]);
        Assert.True(c.Ok, c.Error);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\Users\pat\Desktop\..\..\other\Desktop\x.txt")]
    [InlineData(@"C:/Users/pat/Desktop/../Documents/secret.txt")]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData(@"\\?\C:\Users\pat\Desktop\x.txt")]
    [InlineData(@"Desktop\x.txt")]
    [InlineData(@"C:\Users\pat\Desktop\sub\deep.txt")]
    public void A_source_outside_the_roots_refuses_the_whole_plan(string source)
    {
        var plan = Agent([Shots],
            new DrawerMove(@"C:\Users\pat\Desktop\fine.png", "screenshots"),
            new DrawerMove(source, "screenshots"));
        var c = DrawerPlanValidator.Check(plan, Roots, []);
        Assert.False(c.Ok);
        Assert.Empty(c.Moves);
        Assert.Contains("outside", c.Error);
    }

    [Theory]
    [InlineData(@"c:\users\PAT\desktop\shot.png")]
    [InlineData(@"C:/Users/pat/Desktop/shot.png")]
    [InlineData(@"C:\Users\pat\Desktop\.\shot.png")]
    public void Case_slashes_and_dot_segments_still_count_as_the_Desktop(string source)
    {
        var c = DrawerPlanValidator.Check(Agent([Shots], new DrawerMove(source, "screenshots")), Roots, []);
        Assert.True(c.Ok, c.Error);
    }

    [Fact]
    public void Public_desktop_items_are_listed_as_shared_and_never_moved()
    {
        var plan = Agent([Shots],
            new DrawerMove(@"C:\Users\Public\Desktop\Game.lnk", "screenshots"),
            new DrawerMove(@"C:\Users\pat\Desktop\shot.png", "screenshots"));
        var c = DrawerPlanValidator.Check(plan, Roots, []);
        Assert.True(c.Ok, c.Error);
        Assert.Equal([@"C:\Users\Public\Desktop\Game.lnk"], c.Shared);
        Assert.Equal([@"C:\Users\pat\Desktop\shot.png"], c.Moves.Select(m => m.Source));
    }

    [Fact]
    public void A_source_already_in_its_drawer_is_dropped_as_done()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Drawers\Screenshots\shot.png", "screenshots")), Roots, [Shots]);
        Assert.True(c.Ok, c.Error);
        Assert.Empty(c.Moves);
    }

    [Fact]
    public void A_file_moves_from_one_drawer_to_another()
    {
        var docs = new DrawerDef("documents", "Documents", @"C:\Users\pat\Drawers\Documents", true);
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Drawers\Screenshots\a.pdf", "documents")), Roots, [Shots, docs]);
        Assert.True(c.Ok, c.Error);
        Assert.Single(c.Moves);
    }

    [Fact]
    public void A_drawer_home_itself_cannot_be_moved()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Drawers\Screenshots", "downloads")), Roots, [Shots, Downloads]);
        Assert.False(c.Ok);
    }

    [Fact]
    public void With_the_root_on_the_Desktop_the_root_is_refused_and_its_drawers_are_sources()
    {
        var onDesk = new DrawerRoots(@"C:\Users\pat\Desktop", @"C:\Users\Public\Desktop", @"C:\Users\pat\Desktop\Drawers");
        var shots = new DrawerDef("screenshots", "Screenshots", @"C:\Users\pat\Desktop\Drawers\Screenshots", true);
        var docs = new DrawerDef("documents", "Documents", @"C:\Users\pat\Desktop\Drawers\Documents", true);

        var root = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\Drawers", "screenshots")), onDesk, [shots]);
        Assert.False(root.Ok);
        Assert.Contains("Drawers folder", root.Error);

        var between = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\Drawers\Screenshots\a.pdf", "documents")), onDesk, [shots, docs]);
        Assert.True(between.Ok, between.Error);
        Assert.Single(between.Moves);

        var fresh = DrawerPlanValidator.Check(Agent([docs], new DrawerMove(@"C:\Users\pat\Desktop\a.pdf", "documents")), onDesk, []);
        Assert.True(fresh.Ok, fresh.Error);
    }

    [Fact]
    public void A_view_drawer_on_the_Desktop_makes_a_desktop_source_a_no_op()
    {
        var desk = new DrawerDef("desk", "Desk", @"C:\Users\pat\Desktop", false);
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\a.txt", "desk")), Roots, [desk]);
        Assert.True(c.Ok, c.Error);
        Assert.Empty(c.Moves);
    }

    [Fact]
    public void An_unknown_drawer_refuses_the_plan()
    {
        var c = DrawerPlanValidator.Check(Agent([], new DrawerMove(@"C:\Users\pat\Desktop\a.txt", "nowhere")), Roots, []);
        Assert.False(c.Ok);
        Assert.Contains("nowhere", c.Error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("dots.")]
    public void A_drawer_name_Windows_rejects_refuses_the_plan(string name)
    {
        var bad = new DrawerDef("bad", name, @"C:\Users\pat\Drawers\bad", true);
        var c = DrawerPlanValidator.Check(Agent([bad]), Roots, []);
        Assert.False(c.Ok);
        Assert.Contains(name, c.Error);
    }

    [Fact]
    public void A_plan_drawer_must_be_owned_and_live_directly_under_the_Drawers_root()
    {
        foreach (var d in new[]
        {
            new DrawerDef("x", "X", @"C:\Users\pat\Documents\X", true),
            new DrawerDef("x", "X", @"C:\Users\pat\Drawers\deeper\X", true),
            new DrawerDef("x", "X", @"C:\Users\pat\Drawers\X", false),
        })
            Assert.False(DrawerPlanValidator.Check(Agent([d]), Roots, []).Ok);
    }

    [Fact]
    public void A_plan_cannot_move_an_existing_drawer_to_another_home()
    {
        var moved = Shots with { Home = @"C:\Users\pat\Drawers\Elsewhere" };
        Assert.False(DrawerPlanValidator.Check(Agent([moved]), Roots, [Shots]).Ok);
    }

    [Fact]
    public void The_same_source_twice_refuses_the_plan()
    {
        var c = DrawerPlanValidator.Check(Agent([Shots],
            new DrawerMove(@"C:\Users\pat\Desktop\a.png", "screenshots"),
            new DrawerMove(@"c:\users\pat\desktop\A.png", "screenshots")), Roots, []);
        Assert.False(c.Ok);
    }

    [Fact]
    public void Too_many_drawers_or_moves_refuse_the_plan()
    {
        var many = Enumerable.Range(0, DrawerPlanValidator.MaxDrawers + 1)
            .Select(i => new DrawerDef($"d{i}", $"D{i}", $@"C:\Users\pat\Drawers\D{i}", true)).ToArray();
        Assert.False(DrawerPlanValidator.Check(Agent(many), Roots, []).Ok);
        var moves = Enumerable.Range(0, DrawerPlanValidator.MaxMoves + 1)
            .Select(i => new DrawerMove($@"C:\Users\pat\Desktop\f{i}.txt", "screenshots")).ToArray();
        Assert.False(DrawerPlanValidator.Check(Agent([Shots], moves), Roots, []).Ok);
    }

    [Fact]
    public void An_agent_name_is_required_and_short()
    {
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Agent, null, [], []), Roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Agent, new string('a', 41), [], []), Roots, []).Ok);
        Assert.True(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [], []), Roots, []).Ok);
    }
}
