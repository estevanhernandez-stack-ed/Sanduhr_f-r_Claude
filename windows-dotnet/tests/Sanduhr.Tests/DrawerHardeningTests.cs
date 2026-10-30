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
