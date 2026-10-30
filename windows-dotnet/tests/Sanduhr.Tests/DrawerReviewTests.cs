using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>What the drawers review says and how Edit changes a plan (5.0 drawers spec, sections 1 and 4).</summary>
public sealed class DrawerReviewTests
{
    private const string Desk = @"C:\Users\Ada\Desktop";
    private static readonly string Root = Path.Combine(Desk, "Drawers");

    private static DrawerDef Def(string name, bool owned = true) => new(DrawerIds.From(name), name, Path.Combine(Root, name), owned);

    private static (DrawerPlan Plan, DrawerPlanCheck Check) Plan(params (string File, string Drawer)[] moves)
    {
        var drawers = moves.Select(m => m.Drawer).Distinct().Select(n => Def(n)).ToArray();
        var plan = new DrawerPlan(DrawerPlanSource.Rules, null, drawers,
            moves.Select(m => new DrawerMove(Path.Combine(Desk, m.File), DrawerIds.From(m.Drawer))).ToArray());
        var check = DrawerPlanValidator.Check(plan, new DrawerRoots(Desk, @"C:\Users\Public\Desktop", Root), []);
        Assert.True(check.Ok, check.Error);
        return (plan, check);
    }

    [Theory]
    [InlineData(14, 3, "14 files into 3 drawers")]
    [InlineData(1, 1, "1 file into 1 drawer")]
    [InlineData(2, 1, "2 files into 1 drawer")]
    public void The_title_counts_files_and_drawers(int files, int drawers, string expected) =>
        Assert.Equal(expected, DrawerReview.Title(files, drawers));

    [Fact]
    public void The_model_names_each_drawer_with_its_count_and_marks_new_ones()
    {
        var (plan, check) = Plan(("a.png", "Images"), ("b.png", "Images"), ("c.pdf", "Documents"));
        var m = DrawerReview.Model(plan, check, ["documents"], note: null, warnings: []);
        Assert.Equal("3 files into 2 drawers", m.Title);
        Assert.Equal("Sanduhr suggests drawers", m.Heading);
        Assert.Equal(DrawerReview.BarName, m.BarName);
        Assert.True(m.CanEdit);
        Assert.True(m.CanApply);
        Assert.Contains("Images (new): 2 files", m.Changes);
        Assert.Contains("Documents: 1 file", m.Changes);
    }

    [Fact]
    public void An_agent_plan_is_headed_with_the_agents_name()
    {
        var (plan, check) = Plan(("a.png", "Images"));
        var m = DrawerReview.Model(plan with { Source = DrawerPlanSource.Agent, Agent = "Codex" }, check, [], null, []);
        Assert.Equal("Codex suggests drawers", m.Heading);
    }

    [Fact]
    public void Shared_items_are_named_as_shortcuts_when_they_all_are()
    {
        Assert.Equal("3 shortcuts are shared with every user and need an administrator to move.",
            DrawerReview.SharedLine([@"C:\Users\Public\Desktop\A.lnk", @"C:\Users\Public\Desktop\B.lnk", @"C:\Users\Public\Desktop\C.url"]));
        Assert.Equal("1 item is shared with every user and needs an administrator to move.",
            DrawerReview.SharedLine([@"C:\Users\Public\Desktop\notes.txt"]));
        Assert.Null(DrawerReview.SharedLine([]));
    }

    [Fact]
    public void The_result_line_names_skipped_files_and_caps_at_three()
    {
        Assert.Equal("13 moved.", DrawerReview.ResultLine(13, []));
        Assert.Equal("13 moved, 1 skipped: report.docx is open in Word.",
            DrawerReview.ResultLine(13, [new("report.docx", DrawerSkip.OpenIn("Word"))]));
        var five = Enumerable.Range(1, 5).Select(i => new DrawerSkipped($"f{i}.txt", DrawerSkip.MovedSince)).ToArray();
        Assert.Equal("0 moved, 5 skipped: f1.txt was moved since. f2.txt was moved since. f3.txt was moved since. And 2 more.",
            DrawerReview.ResultLine(0, five));
    }

    [Fact]
    public void The_result_model_offers_Undo_and_Done()
    {
        var r = DrawerReview.Result("13 moved.", canUndo: true);
        Assert.Equal("13 moved.", r.Title);
        Assert.Equal(DrawerReview.Undo, r.ApplyLabel);
        Assert.Equal(DrawerReview.Done, r.DismissLabel);
        Assert.False(r.CanEdit);
        Assert.True(r.CanApply);
        Assert.False(DrawerReview.Result("0 moved.", canUndo: false).CanApply);
    }

    [Fact]
    public void The_result_model_has_its_own_keys_line()
    {
        var undo = DrawerReview.Result("13 moved.", canUndo: true);
        Assert.Equal("Enter undoes. Escape closes.", undo.Keys);
        var none = DrawerReview.Result("13 moved. Undo is not available for this move.", canUndo: false);
        Assert.Equal("Escape closes.", none.Keys);
        Assert.Equal(DeskReviewCopy.KeysLine, new DeskReviewModel("t", null, [], [], false, []).Keys);   // null keeps today's
    }

    [Fact]
    public void Edit_drops_unticked_moves_and_drawers_left_empty()
    {
        var (plan, _) = Plan(("a.png", "Images"), ("c.pdf", "Documents"));
        var edited = DrawerPlanEdit.Apply(plan, new HashSet<string> { Path.Combine(Desk, "c.pdf") }, new Dictionary<string, string>(), [], Root);
        Assert.Equal([Path.Combine(Desk, "a.png")], edited.Moves.Select(m => m.Source));
        Assert.Equal(["images"], edited.Drawers.Select(d => d.Id));
    }

    [Fact]
    public void Edit_renames_a_new_drawer_and_its_folder_but_keeps_its_id()
    {
        var (plan, _) = Plan(("a.png", "Images"));
        var edited = DrawerPlanEdit.Apply(plan, new HashSet<string>(), new Dictionary<string, string> { ["images"] = "Photos" }, [], Root);
        var d = Assert.Single(edited.Drawers);
        Assert.Equal(("images", "Photos", Path.Combine(Root, "Photos")), (d.Id, d.Name, d.Home));
        Assert.Equal("images", edited.Moves[0].DrawerId);
    }

    [Fact]
    public void Edit_never_renames_an_existing_drawer_or_takes_a_bad_name()
    {
        var (plan, _) = Plan(("a.png", "Images"), ("c.pdf", "Documents"));
        var edited = DrawerPlanEdit.Apply(plan, new HashSet<string>(),
            new Dictionary<string, string> { ["documents"] = "Papers", ["images"] = "CON" }, ["documents"], Root);
        Assert.Equal(["Images", "Documents"], edited.Drawers.Select(d => d.Name));
    }

    [Fact]
    public void The_OneDrive_line_names_the_folders_when_sync_differs()
    {
        const string od = @"C:\Users\Ada\OneDrive";
        Assert.Null(DrawerSync.Warning(od + @"\Desktop", od + @"\Desktop\Drawers", [od]));
        Assert.Null(DrawerSync.Warning(Desk, @"D:\Drawers", [od]));
        Assert.Equal(@"Your Desktop syncs with OneDrive and D:\Drawers does not. Files moved into drawers there stop syncing.",
            DrawerSync.Warning(od + @"\Desktop", @"D:\Drawers", [od]));
        Assert.Equal(@"C:\Users\Ada\OneDrive\Drawers syncs with OneDrive and your Desktop does not.",
            DrawerSync.Warning(Desk, od + @"\Drawers", [od]));
    }

    [Fact]
    public void All_but_the_first_two_new_drawers_arrive_folded()
    {
        var created = new[] { Def("Images"), Def("Documents"), Def("Archives") };
        Assert.Equal(["archives"], DrawerArrival.Folded(created));
    }
}
