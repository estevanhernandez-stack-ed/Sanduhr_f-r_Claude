using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

public sealed class DrawerPlanEditWindowTests
{
    private static readonly DrawerPlan Plan = new(DrawerPlanSource.Rules, null,
        [new("images", "Images", @"C:\D\Drawers\Images", true), new("documents", "Documents", @"C:\D\Drawers\Documents", true)],
        [new(@"C:\D\a.png", "images"), new(@"C:\D\b.pdf", "documents")]);

    [Fact]
    public void Edits_come_back_as_unticked_paths_and_renames() => Sta.Run(() =>
    {
        var w = new DrawerPlanEditWindow(Plan, ["documents"]);
        w.Untick(@"C:\D\b.pdf");
        w.Rename("images", "Photos");
        var r = w.TryResult(out var error);
        Assert.Null(error);
        Assert.Equal([@"C:\D\b.pdf"], r!.Unticked);
        Assert.Equal("Photos", r.Renames["images"]);
        Assert.False(w.CanRename("documents"));   // an existing drawer is renamed from its own menu, not here
    });

    [Fact]
    public void A_bad_name_is_refused_with_a_line() => Sta.Run(() =>
    {
        var w = new DrawerPlanEditWindow(Plan, []);
        w.Rename("images", "CON");
        Assert.Null(w.TryResult(out var error));
        Assert.Equal("\"CON\" can't be a folder name.", error);
    });
}
