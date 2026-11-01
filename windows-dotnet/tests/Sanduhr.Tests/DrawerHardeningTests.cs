using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Plan 1's deferred minors and the Core pieces plan 2's App code needs (drawers plan 2, Task 2).</summary>
public sealed class DrawerHardeningTests
{
    private const string Desk = @"C:\Users\Ada\Desktop";
    private const string Home = @"C:\Users\Ada\Desktop\Drawers\Images";

    [Theory]
    [InlineData("COM¹")]
    [InlineData("LPT³.txt")]
    [InlineData("CONIN$")]
    [InlineData("conout$")]
    public void Device_names_with_superscripts_and_console_names_are_refused(string name) =>
        Assert.False(DrawerNames.IsValid(name));

    [Fact]
    public void A_dotfile_clash_is_numbered_after_its_whole_name()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Desk, ".gitignore") };
        Assert.Equal(Path.Combine(Desk, ".gitignore (2)"), DrawerNames.Free(Desk, ".gitignore", false, taken.Contains));
    }

    [Fact]
    public void Free_gives_up_instead_of_looping_forever() =>
        Assert.Throws<InvalidOperationException>(() => DrawerNames.Free(Desk, "a.txt", false, _ => true));

    [Fact]
    public void Two_names_that_both_slug_to_drawer_get_different_ids()
    {
        Assert.Equal("drawer", DrawerIds.Unique("Фото", []));
        Assert.Equal("drawer-2", DrawerIds.Unique("写真", ["drawer"]));
        Assert.Equal("images-3", DrawerIds.Unique("Images", ["images", "images-2"]));
    }

    [Fact]
    public void A_unique_id_stays_within_forty_characters()
    {
        var name = new string('a', 40);
        var id = DrawerIds.Unique(name, [name]);
        Assert.True(DrawerIds.IsValid(id));
        Assert.EndsWith("-2", id);
    }

    [Fact]
    public void A_plan_with_null_lists_or_entries_is_refused_not_thrown()
    {
        var roots = new DrawerRoots(Desk, @"C:\Users\Public\Desktop", Path.Combine(Desk, "Drawers"));
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, null!, null!), roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [null!], []), roots, []).Ok);
        Assert.False(DrawerPlanValidator.Check(new DrawerPlan(DrawerPlanSource.Rules, null, [], [null!]), roots, []).Ok);
    }

    [Fact]
    public void A_move_from_the_Desktop_into_a_home_is_allowed() =>
        Assert.Null(DrawerPaths.MoveAllowed(Desk + @"\a.png", Home, Desk, [Home], toDesktop: false));

    [Fact]
    public void A_source_whose_final_path_is_elsewhere_is_refused() =>
        // A Desktop junction or an 8.3 name that resolves to System32 arrives here as its final path.
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(@"C:\Windows\System32\drivers", Home, Desk, [Home], toDesktop: false));

    [Fact]
    public void The_Desktop_is_a_target_only_for_undo_and_return()
    {
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Home + @"\a.png", Desk, Desk, [Home], toDesktop: false));
        Assert.Null(DrawerPaths.MoveAllowed(Home + @"\a.png", Desk, Desk, [Home], toDesktop: true));
    }

    [Fact]
    public void A_target_outside_the_homes_is_refused() =>
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Desk + @"\a.png", @"C:\Temp", Desk, [Home], toDesktop: true));

    // ---- the final review's MoveAllowed cases ----

    private const string DrawersRoot = @"C:\Users\Ada\Desktop\Drawers";
    private const string Docs = @"C:\Users\Ada\Desktop\Drawers\Documents";

    [Fact]
    public void A_source_that_is_the_Drawers_root_is_refused_since_it_holds_the_homes() =>
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(DrawersRoot, Home, Desk, [Home, Docs], toDesktop: false));

    [Fact]
    public void A_source_that_is_or_holds_a_home_is_refused()
    {
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Docs, Home, Desk, [Home, Docs], toDesktop: false));   // a home itself
        // A Desktop folder a view drawer's home sits inside would swallow that drawer.
        var view = Desk + @"\Project\Notes";
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Desk + @"\Project", Home, Desk, [Home, view], toDesktop: false));
    }

    [Fact]
    public void A_move_from_one_home_to_another_is_allowed() =>
        Assert.Null(DrawerPaths.MoveAllowed(Docs + @"\a.pdf", Home, Desk, [Home, Docs], toDesktop: false));

    [Fact]
    public void A_source_in_a_sibling_of_the_Desktop_is_refused()
    {
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(@"C:\Users\Ada\Documents\a.pdf", Home, Desk, [Home], toDesktop: false));
        // A name that only starts like the Desktop is a sibling too.
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(@"C:\Users\Ada\Desktop2\a.pdf", Home, Desk, [Home], toDesktop: false));
    }

    [Theory]
    [InlineData(@"\\server\share\a.png")]
    [InlineData(@"\\?\C:\Users\Ada\Desktop\a.png")]
    [InlineData(@"C:\Users\Ada\Desktop\..\Documents\a.png")]
    [InlineData(@"C:\Users\Ada\Desktop\Drawers\Images\..\..\..\a.png")]
    [InlineData(@"Desktop\a.png")]
    public void UNC_device_relative_and_dot_dot_escaping_sources_are_refused(string source) =>
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(source, Home, Desk, [Home], toDesktop: false));

    [Theory]
    [InlineData(@"\\server\share\Images")]
    [InlineData(@"C:\Users\Ada\Desktop\Drawers\Images\..\..\..\Temp")]
    public void UNC_and_dot_dot_escaping_targets_are_refused(string target) =>
        Assert.Equal(DrawerSkip.Outside, DrawerPaths.MoveAllowed(Desk + @"\a.png", target, Desk, [Home], toDesktop: false));

    [Fact]
    public void A_dot_dot_that_stays_inside_resolves_and_is_allowed() =>
        Assert.Null(DrawerPaths.MoveAllowed(Desk + @"\Drawers\..\a.png", Home, Desk, [Home], toDesktop: false));

    // ---- the final review's fixes in Core (M3, M4, I3b) ----

    [Fact]
    public void Only_the_default_Drawers_root_counts_as_Sanduhrs_own()
    {
        Assert.True(DrawerSettings.Default.RootIsDefault(Desk));
        Assert.True((DrawerSettings.Default with { Root = Desk + @"\drawers\" }).RootIsDefault(Desk));
        Assert.False((DrawerSettings.Default with { Root = @"D:\Sorted" }).RootIsDefault(Desk));
    }

    [Fact]
    public void A_return_for_a_picked_root_removes_the_homes_but_never_the_root()
    {
        const string picked = @"D:\Sorted";
        var docs = new DrawerDef("documents", "Documents", picked + @"\Documents", true);
        var keep = DrawerReturn.Plan([docs], picked, Desk, _ => [], _ => false, removeRoot: false);
        Assert.Equal([picked + @"\Documents"], keep.HomesToRemove);
        var own = DrawerReturn.Plan([docs], picked, Desk, _ => [], _ => false);
        Assert.Equal(picked, own.HomesToRemove[^1]);
    }

    [Fact]
    public void Repoint_moves_a_renamed_drawers_entries_and_nothing_else()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var j = new DrawerJournal()
            .Add(new JournalEntry("a", now, DrawerPlanSource.User, null,
            [
                new JournalMove(Desk + @"\a.pdf", Docs + @"\a.pdf", false, now),
                new JournalMove(Desk + @"\b.png", Home + @"\b.png", false, now),
                new JournalMove(Desk + @"\c.pdf", Docs + @" old\c.pdf", false, now),   // a sibling that only starts like it
            ]), now)
            .Repoint(Docs, DrawersRoot + @"\Papers");
        var moves = j.Entries.Single().Moves;
        Assert.Equal(DrawersRoot + @"\Papers\a.pdf", moves[0].To);
        Assert.Equal(Desk + @"\a.pdf", moves[0].From);
        Assert.Equal(Home + @"\b.png", moves[1].To);
        Assert.Equal(Docs + @" old\c.pdf", moves[2].To);
    }

    [Fact]
    public void The_review_model_draws_drawer_names_through_the_mask_it_is_given()
    {
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, [new DrawerDef("tax", "Tax 2026", DrawersRoot + @"\Tax 2026", true)],
            [new DrawerMove(Desk + @"\a.pdf", "tax")]);
        var check = DrawerPlanValidator.Check(plan, new DrawerRoots(Desk, @"C:\Users\Public\Desktop", DrawersRoot), []);
        Assert.True(check.Ok, check.Error);
        Assert.Equal("Tax 2026 (new): 1 file, including a.pdf", DrawerReview.Model(plan, check, [], null, []).Changes.Single());
        Assert.Equal("Larkspur (new): 1 file, including a.pdf", DrawerReview.Model(plan, check, [], null, [], _ => "Larkspur").Changes.Single());
    }

    [Fact]
    public void A_journal_entry_can_be_dropped_by_id()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var j = new DrawerJournal()
            .Add(new JournalEntry("a", now, DrawerPlanSource.Rules, null, [new JournalMove(Desk + @"\x.png", Home + @"\x.png", false, now)]), now)
            .Add(new JournalEntry("b", now, DrawerPlanSource.User, null, [new JournalMove(Desk + @"\y.png", Home + @"\y.png", false, now)]), now);
        Assert.Equal(["b"], j.Without("a").Entries.Select(e => e.Id));
        Assert.Equal(2, j.Without("zzz").Entries.Count);
    }

    [Fact]
    public void Undo_reasons_read_as_predicates()
    {
        Assert.Equal("was moved since.", DrawerUndo.MovedSince);
        Assert.Equal("changed since.", DrawerUndo.ChangedSince);
        Assert.Equal("is open in Word.", DrawerSkip.OpenIn("Word"));
    }

    [Fact]
    public void Folded_drawers_round_trip_and_bad_ids_drop()
    {
        var s = DrawerSettings.Default with { Folded = ["images", "documents"] };
        Assert.Equal(["images", "documents"], DrawerSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString())).Folded);
        var bad = JsonNode.Parse("""{"folded":["images","Not An Id",3]}""");
        Assert.Equal(["images"], DrawerSettings.Decode(bad).Folded);
        Assert.Empty(DrawerSettings.Decode(JsonNode.Parse("{}")).Folded);
    }
}
