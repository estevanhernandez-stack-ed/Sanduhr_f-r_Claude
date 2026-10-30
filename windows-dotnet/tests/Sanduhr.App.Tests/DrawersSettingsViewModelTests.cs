using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

public sealed class DrawersSettingsViewModelTests
{
    private static (DrawersSettingsViewModel Vm, DrawerService S, SettingsStore Store) Make(DrawerSandbox box, bool enabled = false)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default with { Enabled = enabled });
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        return (new DrawersSettingsViewModel(s, null, store) { Confirm = _ => true, PickFolder = () => null }, s, store);
    }

    [Fact]
    public void Turning_drawers_on_turns_the_Desk_on_and_says_where_files_live() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        vm.Enabled = true;
        Assert.True(store.LoadDrawers().Enabled);
        Assert.True(store.LoadDesk().Enabled);
        Assert.Equal(DrawerReview.FirstOnLine, vm.Status);
        s.Dispose();
    });

    [Fact]
    public void Turning_off_asks_with_the_count_and_a_no_keeps_drawers_on() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var home = box.Dir(@"Desktop\Drawers\Images");
        box.File(@"Desktop\Drawers\Images\a.png");   // inside the drawer: the plan counts only what the homes hold
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", home, true)] });
        string? asked = null;
        vm.Confirm = q => { asked = q; return false; };
        vm.Enabled = false;
        Assert.Equal(DrawerReturn.Prompt(1), asked);
        Assert.True(vm.Enabled);
        Assert.True(store.LoadDrawers().Enabled);
        s.Dispose();
    });

    [Fact]
    public void Turning_off_with_a_yes_returns_the_files_and_says_so() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var home = box.Dir(@"Desktop\Drawers\Images");
        box.File(@"Desktop\Drawers\Images\a.png");
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", home, true)] });
        vm.Enabled = false;
        Assert.False(store.LoadDrawers().Enabled);
        Assert.True(File.Exists(Path.Combine(box.Desktop, "a.png")));
        Assert.Equal("1 moved.", vm.Status);
        s.Dispose();
    });

    [Fact]
    public void The_folder_moves_only_while_no_owned_drawer_exists() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var elsewhere = box.Dir("Elsewhere");
        vm.PickFolder = () => elsewhere;
        vm.ChangeRootCommand.Execute(null);
        Assert.Equal(elsewhere, store.LoadDrawers().Root);
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", Path.Combine(elsewhere, "Images"), true)] });
        Assert.False(vm.CanChangeRoot);
        s.Dispose();
    });

    [Fact]
    public void Rules_move_up_and_down_turn_off_and_a_name_rule_is_added() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        vm.Rules[1].MoveUpCommand.Execute(null);
        Assert.Equal(DrawerRuleKind.Images, store.LoadDrawers().Rules[0].Kind);
        vm.Rules[0].On = false;
        Assert.False(store.LoadDrawers().Rules[0].On);
        vm.NewRuleText = "invoice";
        vm.NewRuleDrawer = "Receipts";
        vm.AddNameRuleCommand.Execute(null);
        var added = store.LoadDrawers().Rules[^1];
        Assert.Equal((DrawerRuleKind.NameContains, "invoice", "receipts"), (added.Kind, added.Text, added.DrawerId));
        s.Dispose();
    });

    [Fact]
    public void Recent_moves_list_the_journal_and_Undo_puts_files_back() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        var home = box.Dir(@"Desktop\Drawers\Images");
        s.Save(s.Settings with { Drawers = [new DrawerDef("images", "Images", home, true)] });
        var a = box.File(@"Desktop\a.png");
        s.MoveIn([a], "images");
        vm.Refresh();
        var row = Assert.Single(vm.Recent);
        Assert.Contains("1 file", row.Line);
        row.UndoCommand.Execute(null);
        Assert.True(File.Exists(a));
        Assert.Empty(vm.Recent);
        s.Dispose();
    });

    [Fact]
    public void Put_drawers_back_top_right_clears_the_places_and_Show_drawers_clears_hidden() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box, enabled: true);
        s.Save(s.Settings with
        {
            Drawers = [new DrawerDef("images", "Images", box.Dir(@"Desktop\Drawers\Images"), true)],
            Places = new Dictionary<string, DrawerPlace> { ["images"] = new DrawerPlace("m", 10, 20) },
            Hidden = ["images"],
        });
        vm.ResetPlacesCommand.Execute(null);
        Assert.Empty(store.LoadDrawers().Places);
        Assert.Single(store.LoadDrawers().Hidden);
        vm.ShowDrawersCommand.Execute(null);
        Assert.Empty(store.LoadDrawers().Hidden);
        s.Dispose();
    });

    [Fact]
    public void Without_drawers_the_page_still_opens() => Sta.Run(() =>
    {
        var paths = new Paths(appDataBase: Path.Combine(Path.GetTempPath(), "dvm-" + Guid.NewGuid().ToString("N")[..6]));
        var vm = new DrawersSettingsViewModel(null, null, new SettingsStore(paths));
        Assert.False(vm.Enabled);
        Assert.Empty(vm.DrawerRows);
    });
}
