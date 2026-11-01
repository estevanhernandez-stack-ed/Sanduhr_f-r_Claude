using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Desk;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>The Drawers page's "Drawer look" section and each drawer's Look menu (look and placement spec, sections 1 and 2): the view model
/// saves what the page shows, Tidy reaches the app with the chosen corner and direction, the page names every control, and the menu's check
/// marks and submenu are built from the settings. Nothing here opens a real Explorer menu.</summary>
public sealed class DrawersLookPageTests
{
    private static (DrawersSettingsViewModel Vm, DrawerService S, SettingsStore Store) Make(DrawerSandbox box)
    {
        var paths = new Paths(appDataBase: Path.Combine(box.Root, "appdata"));
        var store = new SettingsStore(paths);
        store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
        store.SaveDrawers(DrawerSettings.Default);
        var s = new DrawerService(store, paths, new DrawerPlaces(box.Desktop, box.Public, []), Dispatcher.CurrentDispatcher);
        return (new DrawersSettingsViewModel(s, null, store), s, store);
    }

    [Fact]
    public void The_page_reads_and_saves_every_new_setting() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        Assert.Equal(0, vm.LookIndex);
        Assert.Equal(55, vm.Opacity);
        Assert.Equal("55%", vm.OpacityText);
        Assert.True(vm.ShowOpacity);
        Assert.True(vm.Snap);
        Assert.True(vm.KeepClear);

        vm.LookIndex = (int)DrawerLookKind.Outline;
        Assert.False(vm.ShowOpacity);
        vm.Opacity = 70;
        Assert.Equal("70%", vm.OpacityText);
        vm.Snap = false;
        vm.KeepClear = false;
        vm.TidyCornerIndex = (int)DrawerCorner.BottomLeft;
        vm.TidyFlowIndex = (int)DrawerFlow.Row;

        var saved = store.LoadDrawers();
        Assert.Equal(DrawerLookKind.Outline, saved.Look);
        Assert.Equal(70, saved.Opacity);
        Assert.False(saved.Snap);
        Assert.False(saved.KeepClear);
        Assert.Equal(new DrawerTidy(DrawerCorner.BottomLeft, DrawerFlow.Row), saved.Tidy);
        Assert.Equal(DrawerLookKind.Outline, s.Settings.Look);
        s.Dispose();
    });

    [Fact]
    public void The_opacity_readout_shows_for_Solid_and_Glass_only() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        foreach (var (kind, shown) in new[] { (DrawerLookKind.Solid, true), (DrawerLookKind.Glass, true), (DrawerLookKind.Outline, false), (DrawerLookKind.Bare, false) })
        {
            vm.LookIndex = (int)kind;
            Assert.Equal(shown, vm.ShowOpacity);
        }
        s.Dispose();
    });

    [Fact]
    public void The_opacity_readout_also_shows_when_any_drawers_own_look_has_a_fill() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        vm.LookIndex = (int)DrawerLookKind.Outline;
        Assert.False(vm.ShowOpacity);

        s.Save(s.Settings with { Looks = new Dictionary<string, DrawerLookKind> { ["images"] = DrawerLookKind.Bare } });
        vm.Refresh();
        Assert.False(vm.ShowOpacity);   // no drawer draws a plate

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        s.Save(s.Settings with { Looks = new Dictionary<string, DrawerLookKind> { ["images"] = DrawerLookKind.Glass } });
        vm.Refresh();
        Assert.True(vm.ShowOpacity);    // the shared look is Outline, but one drawer is Glass
        Assert.Contains(nameof(DrawersSettingsViewModel.ShowOpacity), changed);
        s.Dispose();
    });

    [Fact]
    public void Opacity_stays_in_range() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        vm.Opacity = 5;
        vm.Flush();
        Assert.Equal(DrawerStyle.MinOpacity, vm.Opacity);
        Assert.Equal(DrawerStyle.MinOpacity, store.LoadDrawers().Opacity);
        vm.Opacity = 400;
        vm.Flush();
        Assert.Equal(DrawerStyle.MaxOpacity, vm.Opacity);
        Assert.Equal(DrawerStyle.MaxOpacity, store.LoadDrawers().Opacity);
        s.Dispose();
    });

    [Fact]
    public void Dragging_the_opacity_slider_saves_once_when_it_settles() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        int saves = 0;
        s.Changed += () => saves++;
        foreach (var v in new[] { 60, 65, 70, 75 }) vm.Opacity = v;
        Assert.Equal(75, vm.Opacity);
        Assert.Equal("75%", vm.OpacityText);   // the readout follows the thumb at once
        Assert.Equal(0, saves);
        Assert.Equal(55, store.LoadDrawers().Opacity);
        vm.Flush();
        Assert.Equal(1, saves);
        Assert.Equal(75, store.LoadDrawers().Opacity);
        vm.Flush();
        Assert.Equal(1, saves);   // nothing pending, nothing saved
        s.Dispose();
    });

    [Fact]
    public void The_settle_timer_saves_a_resting_opacity() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        vm.Opacity = 90;
        Assert.Equal(55, store.LoadDrawers().Opacity);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (store.LoadDrawers().Opacity != 90 && DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => { Thread.Sleep(20); frame.Continue = false; });
            Dispatcher.PushFrame(frame);
        }
        Assert.Equal(90, store.LoadDrawers().Opacity);
        s.Dispose();
    });

    [Fact]
    public void Another_change_saves_a_settling_opacity_first_and_closing_keeps_it() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        vm.Opacity = 80;
        vm.Snap = false;
        var saved = store.LoadDrawers();
        Assert.Equal(80, saved.Opacity);
        Assert.False(saved.Snap);

        vm.Opacity = 30;
        vm.Dispose();   // Settings closes before the timer ticks
        Assert.Equal(30, store.LoadDrawers().Opacity);
        s.Dispose();
    });

    [Fact]
    public void A_settling_opacity_survives_a_change_made_elsewhere() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, store) = Make(box);
        vm.Opacity = 70;
        s.Save(s.Settings with { Snap = false });   // the service changes under the drag; Refresh runs
        Assert.Equal(70, vm.Opacity);
        Assert.False(vm.Snap);
        vm.Flush();
        var saved = store.LoadDrawers();
        Assert.Equal(70, saved.Opacity);
        Assert.False(saved.Snap);
        s.Dispose();
    });

    [Fact]
    public void A_change_made_elsewhere_reaches_the_page() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        s.Save(s.Settings with { Look = DrawerLookKind.Glass, Opacity = 80, Snap = false, Tidy = new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Row) });
        Assert.Equal((int)DrawerLookKind.Glass, vm.LookIndex);
        Assert.Equal(80, vm.Opacity);
        Assert.False(vm.Snap);
        Assert.Equal((int)DrawerCorner.TopLeft, vm.TidyCornerIndex);
        Assert.Equal((int)DrawerFlow.Row, vm.TidyFlowIndex);
        s.Dispose();
    });

    [Fact]
    public void Tidy_asks_the_app_with_the_chosen_corner_and_direction() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        DrawerTidy? asked = null;
        vm.TidyRequested = t => { asked = t; return "Nothing to tidy."; };
        vm.TidyCornerIndex = (int)DrawerCorner.TopLeft;
        vm.TidyCommand.Execute(null);
        Assert.Equal(new DrawerTidy(DrawerCorner.TopLeft, DrawerFlow.Column), asked);
        Assert.Equal("Nothing to tidy.", vm.TidyStatus);
        Assert.Equal("", vm.Status);   // the refusal shows under the Tidy row, not in the line at the top of the page
        s.Dispose();
    });

    [Fact]
    public void Tidy_with_no_app_says_nothing() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        vm.TidyStatus = "old";
        vm.TidyCommand.Execute(null);
        Assert.Equal("", vm.TidyStatus);
        s.Dispose();
    });

    [Fact]
    public void The_look_section_names_every_control_and_hides_opacity_for_Outline() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        var page = new DrawersPage { DataContext = vm };
        Lay(page);
        var section = SettingsAnchor.Find(page, SettingsIndex.DrawersLookSection);
        Assert.NotNull(section);
        var controls = Below<Control>(section!).Where(c => c is ComboBox or Slider or CheckBox or Button).ToList();
        Assert.Equal(3, controls.OfType<ComboBox>().Count());
        Assert.Single(controls.OfType<Slider>());
        Assert.Equal(2, controls.OfType<CheckBox>().Count());
        Assert.Single(controls.OfType<Button>());
        Assert.All(controls, c => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)), c.GetType().Name));
        Assert.Contains(controls.OfType<ComboBox>(), c => AutomationProperties.GetName(c) == SettingsNames.DrawersLook && c.Items.Count == 4);
        Assert.Contains(controls.OfType<Button>(), b => AutomationProperties.GetName(b) == SettingsNames.DrawersTidy);
        Assert.Contains(Below<TextBlock>(section!), b => AutomationProperties.GetLiveSetting(b) == AutomationLiveSetting.Polite);   // the Tidy line

        var slider = controls.OfType<Slider>().Single();
        Assert.Equal(SettingsNames.DrawersOpacity, AutomationProperties.GetName(slider));
        Assert.Equal(Visibility.Visible, ((FrameworkElement)slider.Parent).Visibility);
        vm.LookIndex = (int)DrawerLookKind.Outline;
        Lay(page);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)slider.Parent).Visibility);
        vm.Dispose();
        s.Dispose();
    });

    [Fact]
    public void Put_drawers_back_top_right_is_replaced_by_Tidy() => AppHost.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (vm, s, _) = Make(box);
        var page = new DrawersPage { DataContext = vm };
        Lay(page);
        Assert.DoesNotContain(Below<Button>(page), b => AutomationProperties.GetName(b).Contains("top right", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Below<Button>(page), b => AutomationProperties.GetName(b) == SettingsNames.DrawersTidy);
        vm.Dispose();
        s.Dispose();
    });

    // ---- the Look menu ----

    [Fact]
    public void The_Look_menu_checks_Same_when_the_drawer_has_no_look_of_its_own()
    {
        var menu = DrawerPiece.LookMenu(DrawerSettings.Default, "images");
        Assert.Equal(SettingsNames.DrawerLookMenu, menu.Text);
        Assert.NotNull(menu.Children);
        Assert.Equal([SettingsNames.DrawerLookSame, "Solid", "Glass", "Outline", "Bare"], menu.Children!.Select(c => c.Text));
        Assert.Equal([true, false, false, false, false], menu.Children!.Select(c => c.Checked));
        Assert.Equal(menu.Children!.Count, menu.Children!.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void The_Look_menu_checks_the_drawers_own_look_and_no_other()
    {
        var settings = DrawerSettings.Default with { Looks = new Dictionary<string, DrawerLookKind> { ["images"] = DrawerLookKind.Glass, ["other"] = DrawerLookKind.Bare } };
        var children = DrawerPiece.LookMenu(settings, "images").Children!;
        Assert.Equal([false, false, true, false, false], children.Select(c => c.Checked));
    }

    [Fact]
    public void A_pick_sets_or_clears_only_this_drawers_look()
    {
        var start = DrawerSettings.Default with { Looks = new Dictionary<string, DrawerLookKind> { ["other"] = DrawerLookKind.Bare } };
        var set = DrawerPiece.LooksWith(start, "images", DrawerLookKind.Outline);
        Assert.Equal(DrawerLookKind.Outline, set["images"]);
        Assert.Equal(DrawerLookKind.Bare, set["other"]);
        var cleared = DrawerPiece.LooksWith(start with { Looks = set }, "images", null);
        Assert.False(cleared.ContainsKey("images"));
        Assert.Equal(DrawerLookKind.Bare, cleared["other"]);
        Assert.False(start.Looks.ContainsKey("images"));   // the old settings were not edited in place
    }

    [Fact]
    public void The_menu_ids_stay_in_our_range_and_apart()
    {
        int[] ids = [DrawerPiece.RenameId, DrawerPiece.FoldId, DrawerPiece.HideId, DrawerPiece.SettingsId, DrawerPiece.LookId, DrawerPiece.LookSameId, DrawerPiece.TidyId,
            .. Enumerable.Range(0, 4).Select(i => DrawerPiece.LookFirstId + i)];
        Assert.All(ids, id => Assert.True(id >= ShellMenu.OwnFirst));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Tidy_from_a_drawers_menu_reaches_the_app_with_that_drawers_id() => Sta.Run(() =>
    {
        using var box = new DrawerSandbox();
        var (_, s, _) = Make(box);
        Assert.Null(s.RequestTidy("images"));   // no app: nothing to ask
        string? asked = null;
        s.TidyRequested = id => { asked = id; return "line"; };
        Assert.Equal("line", s.RequestTidy("images"));
        Assert.Equal("images", asked);
        s.Dispose();
        Assert.Null(s.TidyRequested);
    });

    // ---- ShellMenu: a checked item and a submenu ----

    [Fact]
    public void ShellMenu_Append_checks_items_and_attaches_a_submenu()
    {
        const uint MF_CHECKED = 0x8, MF_POPUP = 0x10, ByPosition = 0x400;
        var h = ShellNative.CreatePopupMenu();
        try
        {
            ShellMenu.Append(h,
            [
                new ShellMenuItem(0x8001, "Plain"),
                new ShellMenuItem(0x8002, "Ticked", Checked: true),
                new ShellMenuItem(0x8003, "Look", Children:
                [
                    new ShellMenuItem(0x8004, "One", Checked: true),
                    new ShellMenuItem(0x8005, "Two"),
                ]),
            ]);
            Assert.Equal(3, ShellNative.GetMenuItemCount(h));
            Assert.Equal(0u, ShellNative.GetMenuState(h, 0, ByPosition) & MF_CHECKED);
            Assert.Equal(MF_CHECKED, ShellNative.GetMenuState(h, 1, ByPosition) & MF_CHECKED);
            Assert.NotEqual(0u, ShellNative.GetMenuState(h, 2, ByPosition) & MF_POPUP);
            var sub = ShellNative.GetSubMenu(h, 2);
            Assert.NotEqual(IntPtr.Zero, sub);
            Assert.Equal(2, ShellNative.GetMenuItemCount(sub));
            Assert.Equal(MF_CHECKED, ShellNative.GetMenuState(sub, 0, ByPosition) & MF_CHECKED);
            Assert.Equal(0u, ShellNative.GetMenuState(sub, 1, ByPosition) & MF_CHECKED);
        }
        finally { ShellNative.DestroyMenu(h); }
    }

    [Fact]
    public void ShellMenu_destroys_the_submenus_with_the_parent()
    {
        var h = ShellNative.CreatePopupMenu();
        ShellMenu.Append(h, [new ShellMenuItem(1, "Look", Children: [new ShellMenuItem(2, "One")])]);
        var sub = ShellNative.GetSubMenu(h, 0);
        Assert.NotEqual(IntPtr.Zero, sub);
        ShellNative.DestroyMenu(h);
        Assert.Equal(-1, ShellNative.GetMenuItemCount(sub));   // the handle is gone with its parent
    }

    // ---- helpers ----

    private static void Lay(FrameworkElement page)
    {
        page.Measure(new Size(600, 1400));
        page.Arrange(new Rect(0, 0, 600, 1400));
        page.UpdateLayout();
    }

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }
}
