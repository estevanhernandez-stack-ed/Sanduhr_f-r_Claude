using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Set up Sanduhr, "Start with a look" (5.0 spec 5, section 6): a pick is a choice like the ticks, applied only on Continue.</summary>
public sealed class SetupLookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-setuplook-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public SetupLookTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A setup host that records what was asked of it, in order.</summary>
    private sealed class Host : ISetupHost
    {
        public List<string> Calls { get; } = [];
        public string? LookSays { get; set; }
        public bool DrawersOn { get; set; }
        public bool DrawersEverSorted => false;
        public int DrawerFiles => 0;
        public bool AddOnsInstalled => false;
        public bool UsageOn => false;
        public bool HasAccount => false;
        public SetupDrawersOff TurnOffDrawers() { Calls.Add("drawers off"); DrawersOn = false; return new SetupDrawersOff(true, null); }
        public void TurnOnDrawers() { Calls.Add("drawers on"); }
        public string? SortNow() { Calls.Add("sort"); return null; }
        public void SetUsage(bool on) => Calls.Add("usage " + on);
        public void ReloadDesk() => Calls.Add("reload");
        public string? ApplyLook(string id) { Calls.Add("look " + id); return LookSays; }
    }

    [Fact]
    public void A_picked_look_is_applied_after_the_Desk_is_on_and_before_the_drawers_review()
    {
        var host = new Host();
        var result = new SetupService(_store, host).Apply(new SetupChoices(true, true, false, false, "marquee"));

        Assert.True(_store.LoadDesk().Enabled);
        Assert.True(_store.LoadSetup()!.Desktop);
        Assert.Equal(["drawers on", "reload", "look marquee", "sort"], host.Calls);
        Assert.DoesNotContain(result.Lines, l => l.Contains("Marquee"));
    }

    [Fact]
    public void With_no_look_picked_setup_does_what_it_always_did()
    {
        var host = new Host();
        new SetupService(_store, host).Apply(new SetupChoices(true, false, false));
        Assert.Equal(["reload"], host.Calls);
    }

    [Fact]
    public void A_look_is_not_applied_when_a_nicer_desktop_is_unticked()
    {
        var host = new Host();
        new SetupService(_store, host).Apply(new SetupChoices(false, true, false, false, "marquee"));
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("look"));
    }

    [Fact]
    public void A_look_that_could_not_be_applied_says_why()
    {
        var host = new Host { LookSays = "The Desk could not be saved first, so nothing was changed." };
        var result = new SetupService(_store, host).Apply(new SetupChoices(true, false, false, false, "marquee"));
        Assert.Contains("The Desk could not be saved first, so nothing was changed.", result.Lines);
    }

    [Fact]
    public void A_setup_that_could_not_save_applies_no_look()
    {
        var host = new Host();
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);
        File.WriteAllText(_paths.SettingsFile, "{}");
        new FileInfo(_paths.SettingsFile).IsReadOnly = true;   // streamer: notname - a scratch settings file in a test
        try
        {
            var result = new SetupService(_store, host).Apply(new SetupChoices(true, false, false, false, "marquee"));
            Assert.Contains(SetupCopy.SaveFailed, result.Lines);
            Assert.DoesNotContain(host.Calls, c => c.StartsWith("look"));
        }
        finally { new FileInfo(_paths.SettingsFile).IsReadOnly = false; }
    }

    // ---- the view model ----

    private static string LookJson(string id, string name) => $$"""
        {
          "schema_version": 1, "id": "{{id}}", "name": "{{name}}", "blurb": "A test look.", "font_fallback": "Segoe UI",
          "design": {
            "layout": {
              "clock": { "anchor": "tc", "size": 1.3 },
              "message": { "hidden": true }, "claude": { "hidden": true }, "meters": { "hidden": true }, "nowPlaying": { "hidden": true },
              "telemetry": { "hidden": true }, "weather": { "hidden": true }, "watchers": { "hidden": true }, "launcher": { "hidden": true },
              "glow": { "hidden": true }, "companion": { "hidden": true }
            },
            "font": "Georgia", "ink": "auto", "plate": false, "shadow": true, "glow": false, "follows_theme": false
          }
        }
        """;

    private void WithSetup(bool firstRun, Action<SetupViewModel, Host> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store)
        {
            LookCatalog = DeskLookCatalog.Load([("alpha", LookJson("alpha", "Alpha")), ("beta", LookJson("beta", "Beta"))]),
            InstalledFonts = () => ["Georgia", "Segoe UI"],
            Monitors = () => [],
            DrawersReviewing = () => false,
        };
        service.LookWallpaper = () => null;
        var host = new Host();
        body(new SetupViewModel(new SetupService(_store, host), firstRun, hasAccount: () => false, codexInstalled: () => false, looks: () => service), host);
    });

    [Fact]
    public void The_row_lists_the_looks_and_nothing_is_picked() => WithSetup(true, (vm, _) =>
    {
        Assert.True(vm.HasLooks);
        Assert.Equal(["Alpha", "Beta"], vm.Looks.Select(l => l.Card.Name));
        Assert.Null(vm.PickedLook);
        Assert.True(vm.LooksEnabled);   // a first run ticks a nicer desktop
        Assert.Equal("Alpha look. A test look. Not picked.", vm.Looks[0].AccessibleName);
    });

    [Fact]
    public void Picking_marks_a_card_picking_it_again_clears_it_and_one_is_picked_at_a_time() => WithSetup(true, (vm, host) =>
    {
        vm.Looks[0].PickCommand.Execute(null);
        Assert.Equal("alpha", vm.PickedLook);
        Assert.True(vm.Looks[0].Picked);
        Assert.Equal("Alpha look. A test look. Picked.", vm.Looks[0].AccessibleName);

        vm.Looks[1].PickCommand.Execute(null);
        Assert.Equal("beta", vm.PickedLook);
        Assert.False(vm.Looks[0].Picked);

        vm.Looks[1].PickCommand.Execute(null);
        Assert.Null(vm.PickedLook);
        Assert.DoesNotContain(vm.Looks, l => l.Picked);
        Assert.Empty(host.Calls);   // nothing changes until Continue
        Assert.False(Directory.Exists(_paths.DesignsDir));
    });

    [Fact]
    public void Continue_carries_the_pick_and_unticking_the_desktop_leaves_it_out() => WithSetup(true, (vm, host) =>
    {
        vm.Looks[1].PickCommand.Execute(null);
        vm.Desktop = false;
        Assert.False(vm.LooksEnabled);
        vm.ContinueCommand.Execute(null);
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("look"));
    });

    [Fact]
    public void Continue_with_a_pick_asks_for_that_look() => WithSetup(true, (vm, host) =>
    {
        vm.Looks[1].PickCommand.Execute(null);
        vm.ContinueCommand.Execute(null);
        Assert.Contains("look beta", host.Calls);
        Assert.True(vm.Saved);
    });

    [Fact]
    public void Without_a_design_service_there_is_no_row() => Sta.Run(() =>
    {
        var vm = new SetupViewModel(new SetupService(_store, new Host()), true, hasAccount: () => false, codexInstalled: () => false);
        Assert.False(vm.HasLooks);
        Assert.Empty(vm.Looks);
    });

    [Fact]
    public void The_window_has_the_row_under_the_desktop_tile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SetupWindow.xaml")))
            dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "SetupWindow.xaml"));

        int chat = xaml.IndexOf("x:Name=\"DesignWithChatCaption\"", StringComparison.Ordinal);
        int row = xaml.IndexOf("x:Name=\"StartWithALookRow\"", StringComparison.Ordinal);
        int drawers = xaml.IndexOf("x:Name=\"DrawersBox\"", StringComparison.Ordinal);
        Assert.True(chat > 0 && chat < row && row < drawers);
        Assert.Contains("{x:Static core:SetupCopy.StartWithALook}", xaml);
        Assert.Contains("IsEnabled=\"{Binding LooksEnabled}\"", xaml);
        Assert.Contains("Command=\"{Binding PickCommand}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding Picked, Mode=OneWay}\"", xaml);
    }
}
