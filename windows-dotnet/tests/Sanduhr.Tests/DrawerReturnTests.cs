using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerReturnTests
{
    private const string Root = @"C:\Users\pat\Drawers";
    private const string Desktop = @"C:\Users\pat\Desktop";
    private static readonly DateTime T = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DrawerDef Shots = new("screenshots", "Screenshots", $@"{Root}\Screenshots", true);
    private static readonly DrawerDef Docs = new("documents", "Documents", $@"{Root}\Documents", true);
    private static readonly DrawerDef Downloads = new("downloads", "Downloads", @"C:\Users\pat\Downloads", false);

    private static readonly Dictionary<string, DesktopItem[]> Tree = new(StringComparer.OrdinalIgnoreCase)
    {
        [$@"{Root}\Screenshots"] = [new($@"{Root}\Screenshots\a.txt", false, T), new($@"{Root}\Screenshots\Trip", true, T)],
        [$@"{Root}\Documents"] = [new($@"{Root}\Documents\a.txt", false, T)],
        [@"C:\Users\pat\Downloads"] = [new(@"C:\Users\pat\Downloads\big.iso", false, T)],
    };

    private static IEnumerable<DesktopItem> Children(string dir) => Tree.TryGetValue(dir, out var items) ? items : [];

    [Fact]
    public void Owned_homes_empty_onto_the_Desktop_files_and_folders_alike()
    {
        var plan = DrawerReturn.Plan([Shots], Root, Desktop, Children, _ => false);
        Assert.Equal(
            [new UndoStep($@"{Root}\Screenshots\a.txt", $@"{Desktop}\a.txt"), new UndoStep($@"{Root}\Screenshots\Trip", $@"{Desktop}\Trip")],
            plan.Moves);
        Assert.Equal([$@"{Root}\Screenshots", Root], plan.HomesToRemove);
    }

    [Fact]
    public void The_root_goes_last_so_it_is_removed_only_after_its_homes() =>
        Assert.Equal(Root, DrawerReturn.Plan([Shots, Docs], Root, Desktop, Children, _ => false).HomesToRemove[^1]);

    [Fact]
    public void A_view_drawer_keeps_its_contents_where_they_are()
    {
        var plan = DrawerReturn.Plan([Downloads], Root, Desktop, Children, _ => false);
        Assert.Empty(plan.Moves);
        Assert.Empty(plan.HomesToRemove);
    }

    [Fact]
    public void Same_names_from_two_drawers_and_the_Desktop_never_overwrite()
    {
        var plan = DrawerReturn.Plan([Shots, Docs], Root, Desktop, Children, p => p == $@"{Desktop}\a.txt");
        Assert.Equal(
            [$@"{Desktop}\a (2).txt", $@"{Desktop}\Trip", $@"{Desktop}\a (3).txt"],
            plan.Moves.Select(m => m.To));
    }

    [Fact]
    public void An_owned_flag_on_a_folder_outside_the_root_is_not_trusted()
    {
        var odd = new DrawerDef("odd", "Odd", @"C:\Users\pat\Documents\Odd", true);
        var plan = DrawerReturn.Plan([odd], Root, Desktop, _ => [new(@"C:\Users\pat\Documents\Odd\x.txt", false, T)], _ => false);
        Assert.Empty(plan.Moves);
        Assert.Empty(plan.HomesToRemove);
    }

    [Theory]
    [InlineData(1, "Put 1 file back on the Desktop?")]
    [InlineData(42, "Put 42 files back on the Desktop?")]
    public void The_prompt_counts_what_comes_back(int n, string text) =>
        Assert.Equal(text, DrawerReturn.Prompt(n));
}
