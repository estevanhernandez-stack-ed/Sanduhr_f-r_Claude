using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings, Desk, Designs and the direct switch (spec, the agent-designed Desk, saved designs): the list, Switch to, Rename, Pin and
/// Delete with their accessible names, Put my Desk back, the live line each action reads out, and the store's Launcher-image rule.
/// </summary>
public sealed class DesignsSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-designs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private DateTimeOffset _now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    public DesignsSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root);
        _store = new SettingsStore(_paths);
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
        _store.SaveDeskDirect(true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void WithService(Action<DeskDesignService> body) => Sta.Run(() =>
    {
        using var service = new DeskDesignService(_paths, _store) { InstalledFonts = () => ["Segoe UI"], Clock = () => _now };
        body(service);
    });

    private void Propose(DeskDesignService s, string id, string json)
    {
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(s.Handoff.Paths.Request, Encoding.UTF8.GetBytes(
            "{\"schema_version\":1,\"id\":\"" + id + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"design\":" + json + "}"));
        s.Handoff.Check();
        _now = _now.AddMinutes(1);
    }

    [Fact]
    public void TheListShowsTheKeptDesignsNewestFirstWithAccessibleNames()
    {
        WithService(s =>
        {
            Propose(s, "a", """{"name":"Dusk","plate":true}""");
            var vm = new DesignsSettingsViewModel(() => s);

            Assert.Equal(["Dusk", "Before Dusk"], vm.Rows.Select(r => r.Name).ToArray());
            Assert.False(vm.HasNoDesigns);
            Assert.Equal("2 designs kept. ", vm.CountText);
            var row = vm.Rows[0];
            Assert.Equal("Switch to Dusk", row.SwitchName);
            Assert.Equal("Rename Dusk", row.RenameName);
            Assert.Equal("Pin Dusk", row.PinName);
            Assert.Equal("Delete Dusk", row.DeleteName);
            Assert.StartsWith("Dusk, applied ", row.Summary);
            Assert.False(string.IsNullOrWhiteSpace(row.Date));
        });
    }

    [Fact]
    public void AnEmptyLibrarySaysSoAndPutMyDeskBackIsOff()
    {
        WithService(s =>
        {
            var vm = new DesignsSettingsViewModel(() => s);
            Assert.True(vm.HasNoDesigns);
            Assert.Equal("", vm.CountText);
            Assert.False(vm.CanPutBack);
            Assert.False(vm.PutBackCommand.CanExecute(null));
        });
        var none = new DesignsSettingsViewModel(() => null);
        Assert.Empty(none.Rows);
        Assert.False(none.CanPutBack);
    }

    [Fact]
    public void SwitchToChangesTheDeskAndTheLineSaysSo()
    {
        WithService(s =>
        {
            Propose(s, "a", """{"name":"Dusk","layout":{"clock":{"anchor":"tr"}}}""");
            var vm = new DesignsSettingsViewModel(() => s);
            var before = vm.Rows.Single(r => r.Name == "Before Dusk");

            vm.SwitchToCommand.Execute(before);
            Assert.Equal("Switched to Before Dusk.", vm.Status);
            Assert.True(vm.HasStatus);
            Assert.Equal(DeskAnchor.BL, new DeskArrangement(_store.LoadDesk().Layout).Placement("clock")!.Anchor);
            Assert.Equal("Before Dusk", vm.Rows[0].Name);   // the one switched to is the newest
        });
    }

    [Fact]
    public void PutMyDeskBackButtonRestoresAndReadsOutThatItDid()
    {
        WithService(s =>
        {
            Propose(s, "a", """{"name":"Dusk","layout":{"clock":{"anchor":"tr"}}}""");
            var vm = new DesignsSettingsViewModel(() => s);
            Assert.True(vm.CanPutBack);
            Assert.True(vm.PutBackCommand.CanExecute(null));

            vm.PutBackCommand.Execute(null);
            Assert.Equal("Your Desk is back as it was.", vm.Status);
            Assert.Equal(DeskAnchor.BL, new DeskArrangement(_store.LoadDesk().Layout).Placement("clock")!.Anchor);
        });
    }

    [Fact]
    public void RenamePinAndDeleteWorkFromTheList()
    {
        WithService(s =>
        {
            Propose(s, "a", """{"name":"Dusk","plate":true}""");
            string? asked = null;
            var vm = new DesignsSettingsViewModel(() => s, (title, prompt, initial) => { asked = initial; return "  Evening  "; });

            vm.RenameCommand.Execute(vm.Rows[0]);
            Assert.Equal("Dusk", asked);
            Assert.Equal("Renamed to Evening.", vm.Status);
            Assert.Equal("Evening", vm.Rows[0].Name);
            Assert.Equal("Switch to Evening", vm.Rows[0].SwitchName);

            vm.PinCommand.Execute(vm.Rows[0]);
            Assert.True(vm.Rows[0].Pinned);
            Assert.Equal("Unpin", vm.Rows[0].PinText);
            Assert.Equal("Unpin Evening", vm.Rows[0].PinName);
            Assert.Contains("pinned", vm.Rows[0].Summary);
            Assert.Contains("never dropped", vm.Status);

            vm.PinCommand.Execute(vm.Rows[0]);
            Assert.False(vm.Rows[0].Pinned);

            vm.DeleteCommand.Execute(vm.Rows[0]);
            Assert.Equal("Deleted Evening.", vm.Status);
            Assert.DoesNotContain(vm.Rows, r => r.Name == "Evening");
        });
    }

    [Fact]
    public void ACancelledOrUnchangedRenameChangesNothing()
    {
        WithService(s =>
        {
            Propose(s, "a", """{"name":"Dusk","plate":true}""");
            var vm = new DesignsSettingsViewModel(() => s, (_, _, _) => null);
            vm.RenameCommand.Execute(vm.Rows[0]);
            Assert.Equal("", vm.Status);
            var same = new DesignsSettingsViewModel(() => s, (_, _, initial) => initial);
            same.RenameCommand.Execute(same.Rows[0]);
            Assert.Equal("", same.Status);
            var blank = new DesignsSettingsViewModel(() => s, (_, _, _) => "   ");
            blank.RenameCommand.Execute(blank.Rows[0]);
            Assert.Equal("Dusk", blank.Rows[0].Name);
        });
    }

    [Fact]
    public void ARowForADesignThatWentSaysSoInsteadOfFailing()
    {
        WithService(s =>
        {
            Propose(s, "a", """{"name":"Dusk","plate":true}""");
            var vm = new DesignsSettingsViewModel(() => s);
            var row = vm.Rows[0];
            s.Delete(row.Id);
            vm.SwitchToCommand.Execute(row);
            Assert.Equal("That design is gone.", vm.Status);
            Assert.DoesNotContain(vm.Rows, r => r.Id == row.Id);
        });
    }

    [Theory]
    [InlineData("design")]
    [InlineData("designs")]
    [InlineData("undo")]
    [InlineData("put back")]
    [InlineData("Put my Desk back")]
    [InlineData("restore")]
    public void TheDesignsAreSearchableByWhatAPersonWouldType(string query)
    {
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == SettingsPage.Desk && h.Entry.Anchor == SettingsIndex.DeskDesigns);
    }

    [Fact]
    public void PutMyDeskBackAndTheDesignsSectionAreNamedControlsOnTheDeskPage()
    {
        Assert.Equal(SettingsPage.Desk, SettingsNames.Table.Single(t => t.Name == SettingsNames.PutMyDeskBack).Page);
        Assert.Equal(SettingsIndex.DeskDesigns, SettingsIndex.NameAnchors[SettingsNames.PutMyDeskBack]);
        Assert.Equal("Put my Desk back", SettingsNames.PutMyDeskBack);
    }
}
