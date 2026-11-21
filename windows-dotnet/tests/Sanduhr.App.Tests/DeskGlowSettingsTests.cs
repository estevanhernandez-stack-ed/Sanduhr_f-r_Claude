using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Desk Look, Ink and color (5.0 spec 5, section 7): Neon glow and the drop shadow exclude each other, the strength
/// is a percentage, and the message's own glow shows as covered while Neon glow is on.</summary>
public sealed class DeskGlowSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-glow-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskGlowSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskSettingsViewModel Desk() => new(_store, ["Segoe UI"]);

    [Fact]
    public void A_fresh_Desk_has_the_shadow_and_no_glow() => Sta.Run(() =>
    {
        var vm = Desk();
        Assert.True(vm.InkShadow);
        Assert.False(vm.InkGlow);
        Assert.Equal(60, vm.InkGlowPercent);
        Assert.Equal("60%", vm.InkGlowPercentText);
    });

    [Fact]
    public void Turning_one_on_turns_the_other_off_and_saves_it() => Sta.Run(() =>
    {
        var vm = Desk();
        int changes = 0;
        vm.Changed = () => changes++;

        vm.InkGlow = true;
        Assert.False(vm.InkShadow);
        Assert.True(_store.LoadDeskInk().Glow);
        Assert.False(_store.LoadDeskInk().Shadow);

        vm.InkShadow = true;
        Assert.False(vm.InkGlow);
        Assert.False(_store.LoadDeskInk().Glow);
        Assert.True(_store.LoadDeskInk().Shadow);
        Assert.Equal(2, changes);
    });

    [Fact]
    public void Turning_one_off_leaves_the_other_off() => Sta.Run(() =>
    {
        var vm = Desk();
        vm.InkShadow = false;
        Assert.False(vm.InkGlow);
        vm.InkGlow = true;
        vm.InkGlow = false;
        Assert.False(vm.InkShadow);
    });

    [Fact]
    public void The_strength_is_a_percentage_and_is_kept() => Sta.Run(() =>
    {
        var vm = Desk();
        vm.InkGlow = true;
        vm.InkGlowPercent = 85;
        Assert.Equal("85%", vm.InkGlowPercentText);   // the text follows the thumb at once
        vm.Flush();   // the drag settles
        Assert.Equal(0.85, _store.LoadDeskInk().GlowStrength);

        var again = Desk();
        Assert.True(again.InkGlow);
        Assert.Equal(85, again.InkGlowPercent);
    });

    [Fact]
    public void A_drag_of_the_strength_saves_once() => Sta.Run(() =>
    {
        var vm = Desk();
        vm.InkGlow = true;
        int changes = 0;
        vm.Changed = () => changes++;

        foreach (var v in new double[] { 70, 75, 80, 90, 100 }) vm.InkGlowPercent = v;
        Assert.Equal(0, changes);   // nothing is written or rebuilt mid-drag
        Assert.Equal(0.6, _store.LoadDeskInk().GlowStrength);
        Assert.Equal("100%", vm.InkGlowPercentText);

        vm.Flush();
        Assert.Equal(1, changes);
        Assert.Equal(1.0, _store.LoadDeskInk().GlowStrength);
        vm.Flush();
        Assert.Equal(1, changes);   // a second flush has nothing left to save
    });

    [Fact]
    public void The_glow_tells_whoever_listens() => Sta.Run(() =>
    {
        var vm = Desk();
        var heard = new List<bool>();
        vm.InkGlowChanged += () => heard.Add(vm.InkGlow);
        vm.InkGlow = true;
        vm.InkShadow = true;   // which turns the glow off
        Assert.Equal([true, false], heard);
    });

    [Fact]
    public void A_design_or_look_that_turns_the_glow_on_tells_the_message_box() => Sta.Run(() =>
    {
        var vm = Desk();
        var heard = new List<bool>();
        vm.InkGlowChanged += () => heard.Add(vm.InkGlow);
        _store.SaveDeskInk(DeskInkSettings.Default.WithGlow(true));
        vm.RefreshFromStore();
        Assert.Equal([true], heard);
        vm.RefreshFromStore();
        Assert.Equal([true], heard);   // told once, not on every reload
    });

    [Fact]
    public void The_one_color_note_shows_only_when_it_is_true()
    {
        Sta.Run(() =>
        {
            _store.SaveDeskUnderIcons(false);
            var vm = Desk();
            vm.InkGlow = true;
            Assert.Null(vm.GlowUnderIconsNote);   // the Desk is above the icons
        });
        Sta.Run(() =>
        {
            _store.SaveDeskInk(DeskInkSettings.Default);   // the first block saved the glow on in this same store
            _store.SaveDeskUnderIcons(true);
            var vm = Desk();
            Assert.Null(vm.GlowUnderIconsNote);   // no glow, nothing to say
            vm.InkGlow = true;
            Assert.Equal(Sanduhr.App.Desk.DeskGlowLayer.UnderIconsOneColor ? SettingsNames.GlowUnderIconsOneColor : null, vm.GlowUnderIconsNote);
        });
    }

    [Fact]
    public void While_neon_glow_is_on_the_messages_own_glow_shows_on_and_cannot_be_changed() => Sta.Run(() =>
    {
        var page = new DeskMessagePageViewModel(_store, Path.Combine(_paths.AppDataDir, "messages.txt"), () => true, () => { }, ["Segoe UI"]);
        page.ThemeColors = false;   // the message's own colors are in play (a fresh message follows the theme, which disables the box)
        page.Glow = false;
        Assert.False(page.GlowShown);
        Assert.True(page.GlowEnabled);
        Assert.Null(page.GlowCoveredNote);

        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        page.SetNeonGlow(true);
        Assert.True(page.GlowShown);
        Assert.False(page.GlowEnabled);
        Assert.Equal(SettingsNames.CoveredByNeonGlow, page.GlowCoveredNote);
        Assert.Contains(nameof(page.GlowShown), raised);
        Assert.Contains(nameof(page.GlowEnabled), raised);
        Assert.Contains(nameof(page.GlowCoveredNote), raised);

        page.GlowShown = false;   // a stray write while covered changes nothing
        Assert.False(page.Glow);
        Assert.True(page.GlowShown);

        page.SetNeonGlow(false);
        Assert.False(page.GlowShown);   // the message's own setting was never touched
        page.Dispose();
    });

    [Fact]
    public void The_message_page_reads_the_glow_when_it_refreshes() => Sta.Run(() =>
    {
        var page = new DeskMessagePageViewModel(_store, Path.Combine(_paths.AppDataDir, "messages.txt"), () => true, () => { }, ["Segoe UI"]);
        _store.SaveDeskInk(DeskInkSettings.Default.WithGlow(true));
        page.RefreshSettings();
        Assert.Equal(SettingsNames.CoveredByNeonGlow, page.GlowCoveredNote);
        page.Dispose();
    });

    [Fact]
    public void The_page_has_the_switch_the_slider_and_the_covered_line()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "DeskLookPage.xaml")))
            dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "DeskLookPage.xaml"));

        Assert.Contains("IsChecked=\"{Binding InkGlow, Mode=TwoWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.NeonGlow}\"", xaml);
        Assert.Contains("Value=\"{Binding InkGlowPercent, Mode=TwoWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.GlowStrength}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding GlowShown, Mode=TwoWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding GlowEnabled}\"", xaml);
        Assert.Contains("{Binding GlowCoveredNote}", xaml);
        Assert.Contains("{Binding GlowUnderIconsNote}", xaml);
        // The glow is one switch for the whole Desk: it is not inside the block that greys out while the Desk follows the theme.
        int glow = xaml.IndexOf("{Binding InkGlow, Mode=TwoWay}", StringComparison.Ordinal);
        int shadow = xaml.IndexOf("{Binding InkShadow, Mode=TwoWay}", StringComparison.Ordinal);
        int closeOfInkBlock = xaml.IndexOf("</StackPanel>", shadow, StringComparison.Ordinal);
        Assert.True(glow > closeOfInkBlock, "Neon glow sits inside the block that is disabled while following the theme");
    }

    [Fact]
    public void Settings_search_finds_the_glow()
    {
        Assert.Contains(SettingsIndex.Search("neon glow"), h => h.Entry.Page == SettingsPage.DeskLook);
        Assert.Contains(SettingsIndex.Search("glow strength"), h => h.Entry.Page == SettingsPage.DeskLook);
    }
}
