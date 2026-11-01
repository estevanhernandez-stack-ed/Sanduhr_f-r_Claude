using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

public sealed class SetupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-setupsvc-" + Guid.NewGuid().ToString("N")[..8]);
    public SetupServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (IOException) { } }
    private SettingsStore Store() => new(new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")));

    private sealed class FakeHost : ISetupHost
    {
        public bool DrawersOn { get; set; }
        public bool DrawersEverSorted { get; set; }
        public int DrawerFiles { get; set; }
        public bool AddOnsInstalled { get; set; }
        public bool UsageOn { get; set; }
        public bool HasAccount { get; set; }
        public SetupDrawersOff OffAnswer { get; set; } = new(true, null);
        public string? SortAnswer { get; set; }
        public List<string> Calls { get; } = [];
        public SetupDrawersOff TurnOffDrawers() { Calls.Add("off"); if (OffAnswer.Off) DrawersOn = false; return OffAnswer; }
        public void TurnOnDrawers() { Calls.Add("on"); DrawersOn = true; }
        public string? SortNow() { Calls.Add("sort"); return SortAnswer; }
        public void SetUsage(bool on) { Calls.Add("usage:" + on); UsageOn = on; }
        public void ReloadDesk() => Calls.Add("reload");
    }

    [Fact]
    public void A_first_run_with_the_default_ticks_turns_on_the_Desk_and_drawers_and_opens_the_first_review()
    {
        var store = Store();
        var host = new FakeHost { UsageOn = true };   // the widget loads usage on before any setup
        var result = new SetupService(store, host).Apply(SetupChoices.FirstRun);
        Assert.True(store.LoadDesk().Enabled);
        Assert.True(store.LoadSetup()!.Desktop);
        Assert.False(store.LoadSetup()!.ClaudeUsage);
        Assert.Equal(["usage:False", "on", "reload", "sort"], host.Calls);
        Assert.True(result.ReviewOpened);
    }

    [Fact]
    public void Drawers_already_sorted_once_do_not_review_again_when_ticked()
    {
        var host = new FakeHost { DrawersEverSorted = true };
        var result = new SetupService(Store(), host).Apply(new SetupChoices(false, true, false));
        Assert.DoesNotContain("sort", host.Calls);
        Assert.False(result.ReviewOpened);
    }

    [Fact]
    public void Drawers_that_cannot_turn_off_stay_ticked_and_the_rest_still_applies()
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var host = new FakeHost { DrawersOn = true, UsageOn = true, OffAnswer = new(false, "Drawers could not be turned off.") };
        var svc = new SetupService(store, host);
        var result = svc.Apply(new SetupChoices(true, false, false));
        Assert.True(result.Now.Drawers);                       // still on
        Assert.Contains("Drawers could not be turned off.", result.Lines);
        Assert.False(host.UsageOn);                            // usage off still applied
        Assert.True(store.LoadDesk().Enabled);                 // desktop still ticked
    }

    [Fact]
    public void Untick_everything_but_usage_turns_the_Desk_off_and_keeps_the_layout()
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Enabled = true, Layout = "clock:bl:1" });
        var layout = store.LoadDesk().Layout;   // as the store normalizes it
        Assert.Contains("clock", layout);
        var host = new FakeHost { UsageOn = false };
        new SetupService(store, host).Apply(new SetupChoices(false, false, true));
        Assert.False(store.LoadDesk().Enabled);
        Assert.Equal(layout, store.LoadDesk().Layout);
        Assert.True(host.UsageOn);
    }

    [Fact]
    public void Nothing_ticked_changes_nothing()
    {
        var store = Store();
        var host = new FakeHost();
        var result = new SetupService(store, host).Apply(new SetupChoices(false, false, false));
        Assert.Empty(host.Calls);
        Assert.Null(store.LoadSetup());
        Assert.Equal([SetupCopy.PickOne], result.Lines);
    }

    [Fact]
    public void Preview_lists_what_unticking_will_do()
    {
        var store = Store();
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var host = new FakeHost { DrawersOn = true, DrawerFiles = 2, UsageOn = true, AddOnsInstalled = true, HasAccount = true };
        var lines = new SetupService(store, host).Preview(new SetupChoices(true, false, false));
        Assert.Equal([SetupCopy.DrawersOffLine(2), SetupCopy.UsageOffLine, SetupCopy.AddOnsStayLine], lines);
    }

    [Fact]
    public void Preview_says_the_account_stays_saved_only_when_there_is_an_account()
    {
        var store = Store();
        store.SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true }));
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var host = new FakeHost { UsageOn = true, HasAccount = false };
        var svc = new SetupService(store, host);
        Assert.DoesNotContain(SetupCopy.UsageOffLine, svc.Preview(new SetupChoices(true, false, false)));
        host.HasAccount = true;
        Assert.Contains(SetupCopy.UsageOffLine, svc.Preview(new SetupChoices(true, false, false)));
    }

    [Fact]
    public void Drawers_off_is_tried_before_anything_else_changes()
    {
        var host = new FakeHost { DrawersOn = true, UsageOn = true };
        new SetupService(Store(), host).Apply(new SetupChoices(true, false, false));
        Assert.Equal(["off", "usage:False", "reload"], host.Calls);
    }

    [Fact]
    public void A_refused_turn_off_keeps_the_Desk_running_for_drawers_and_opens_no_review()
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var host = new FakeHost { DrawersOn = true, OffAnswer = new(false, null) };   // a no to the count: no line
        var result = new SetupService(store, host).Apply(new SetupChoices(false, false, true));
        Assert.True(result.Now.Drawers);
        Assert.True(store.LoadDesk().Enabled);   // desktop || drawers: drawers kept it on
        Assert.DoesNotContain("sort", host.Calls);
        Assert.False(result.ReviewOpened);
    }

    [Fact]
    public void A_review_with_nothing_to_sort_is_not_shown_as_a_problem()
    {
        var host = new FakeHost { SortAnswer = DrawerReview.NothingToSort };
        var result = new SetupService(Store(), host).Apply(new SetupChoices(false, true, false));
        Assert.DoesNotContain(DrawerReview.NothingToSort, result.Lines);
        Assert.False(result.ReviewOpened);
    }

    [Fact]
    public void A_review_that_cannot_open_says_why()
    {
        var host = new FakeHost { SortAnswer = DrawerReview.Busy };
        var result = new SetupService(Store(), host).Apply(new SetupChoices(false, true, false));
        Assert.Contains(DrawerReview.Busy, result.Lines);
    }

    [Fact]
    public void Drawers_already_on_are_not_turned_on_or_reviewed_again()
    {
        var host = new FakeHost { DrawersOn = true };
        new SetupService(Store(), host).Apply(new SetupChoices(true, true, false));
        Assert.DoesNotContain("on", host.Calls);
        Assert.DoesNotContain("sort", host.Calls);
        Assert.Single(host.Calls, c => c == "reload");
    }

    [Fact]
    public void A_save_that_fails_says_so_and_still_tells_the_Desk_about_a_turn_off()
    {
        var store = Store();
        store.SaveDesk(store.LoadDesk() with { Enabled = true });
        var file = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local")).SettingsFile;
        Assert.True(File.Exists(file));
        File.SetAttributes(file, FileAttributes.ReadOnly);   // the write throws UnauthorizedAccessException
        try
        {
            var host = new FakeHost { DrawersOn = true };
            var result = new SetupService(store, host).Apply(new SetupChoices(true, false, false));
            Assert.Contains(SetupCopy.SaveFailed, result.Lines);
            Assert.Contains("reload", host.Calls);
            Assert.False(result.Now.Drawers);
        }
        finally { File.SetAttributes(file, FileAttributes.Normal); }
    }
}
