using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// What a screen reader and UI Automation see in Settings, Desk, Designs (spec, the agent-designed Desk): each saved design is a
/// row named for what it is ("Dusk, applied Oct 21 8:15 PM, pinned", never the type name), its Switch to, Rename, Pin and Delete
/// buttons are found under the list and named per row, and Put my Desk back is a Button with that name. The real panel, realized
/// in a window nobody sees.
/// </summary>
public sealed class DesignsPanelAutomationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-uia-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DesignsPanelAutomationTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
        _store.SaveDeskDirect(true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Propose(DeskDesignService s, string id, string json)
    {
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(s.Handoff.Paths.Request, Encoding.UTF8.GetBytes(
            "{\"schema_version\":1,\"id\":\"" + id + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"design\":" + json + "}"));
        s.Handoff.Check();
    }

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
            foreach (var p in Walk(child)) yield return p;
    }

    /// <summary>UI Automation's Invoke runs the click on the dispatcher, later: let it.</summary>
    private static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    private static string Describe(IEnumerable<AutomationPeer> peers) =>
        string.Join("\n", peers.Select(p => $"{p.GetAutomationControlType()} '{p.GetName()}' ({p.GetClassName()})"));

    private void WithPanel(Action<DesignsSettingsViewModel, List<AutomationPeer>> body) => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        using var service = new DeskDesignService(_paths, _store) { InstalledFonts = () => ["Segoe UI"] };
        Propose(service, "a", """{"name":"Orbital","plate":true,"layout":{"clock":{"anchor":"tr"}}}""");
        var vm = new DesignsSettingsViewModel(() => service);
        vm.PinCommand.Execute(vm.Rows.Single(r => r.Name == "Orbital"));
        vm.Reload();

        var panel = new DesignsSettingsPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 640, Height = 700, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Left = -20000;
        window.Show();
        try
        {
            panel.UpdateLayout();
            body(vm, [.. Walk(UIElementAutomationPeer.CreatePeerForElement(panel))]);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void EachRowIsNamedForTheDesignNotForItsType() =>
        WithPanel((vm, peers) =>
        {
            var orbital = vm.Rows.Single(r => r.Name == "Orbital");
            Assert.Matches(@"^Orbital, applied [A-Z][a-z]{2} \d{1,2}.*, pinned$", orbital.Summary);
            var names = peers.Select(p => p.GetName()).ToList();
            Assert.Contains(orbital.Summary, names);
            Assert.Contains(vm.Rows.Single(r => r.Name == "Before Orbital").Summary, names);
            Assert.DoesNotContain(names, n => n.Contains("DesignRow") || n.Contains("Sanduhr.App.ViewModels"));
            Assert.Contains("Saved designs", names);
        });

    [Fact]
    public void EachRowsButtonsAreFoundUnderTheListAndNamedPerRow() =>
        WithPanel((vm, peers) =>
        {
            foreach (var row in vm.Rows)
            {
                foreach (var name in new[] { row.SwitchName, row.RenameName, row.PinName, row.DeleteName })
                {
                    var buttons = peers.Where(p => p.GetName() == name).ToList();
                    var button = Assert.Single(buttons);
                    Assert.Equal(AutomationControlType.Button, button.GetAutomationControlType());
                    Assert.True(button.IsEnabled());
                    Assert.NotNull(button.GetPattern(PatternInterface.Invoke));
                }
            }
            Assert.Equal("Unpin Orbital", vm.Rows.Single(r => r.Name == "Orbital").PinName);
            Assert.Equal("Pin Before Orbital", vm.Rows.Single(r => r.Name == "Before Orbital").PinName);
        });

    [Fact]
    public void ARowsButtonsLiveInsideThatRowAndInvokeItsDesign() =>
        WithPanel((vm, peers) =>
        {
            // The buttons are children of their row, so a screen reader that walks row by row reads "Switch to Before Orbital" in the right place.
            var row = peers.Single(p => p.GetName() == vm.Rows.Single(r => r.Name == "Before Orbital").Summary);
            var inside = Walk(row).Select(p => p.GetName()).ToList();
            Assert.Contains("Switch to Before Orbital", inside);
            Assert.Contains("Delete Before Orbital", inside);
            Assert.DoesNotContain("Switch to Orbital", inside);

            var invoke = (IInvokeProvider)peers.Single(p => p.GetName() == "Switch to Before Orbital").GetPattern(PatternInterface.Invoke)!;
            invoke.Invoke();
            Pump();
            Assert.Equal("Switched to Before Orbital.", vm.Status);
        });

    [Fact]
    public void PutMyDeskBackIsAButtonWithThatNameAndSaysWhatItDid() =>
        WithPanel((vm, peers) =>
        {
            var button = peers.Single(p => p.GetName() == "Put my Desk back" && p.GetAutomationControlType() == AutomationControlType.Button);
            Assert.Equal(AutomationControlType.Button, button.GetAutomationControlType());
            Assert.Equal("Button", button.GetClassName());
            Assert.True(button.IsEnabled());
            Assert.Equal("Switches to the design saved before the last change", button.GetHelpText());

            ((IInvokeProvider)button.GetPattern(PatternInterface.Invoke)!).Invoke();
            Pump();
            Assert.Equal("Your Desk is back as it was.", vm.Status);
        });

    [Fact]
    public void PutMyDeskBackIsStillAButtonWhenThereIsNothingToPutBack()
    {
        Sta.Run(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var vm = new DesignsSettingsViewModel(() => null);
            var panel = new DesignsSettingsPanel { DataContext = vm };
            var window = new Window { Content = panel, Width = 640, Height = 400, ShowActivated = false, WindowStyle = WindowStyle.None };
            window.Left = -20000;
            window.Show();
            try
            {
                panel.UpdateLayout();
                var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(panel)).ToList();
                var button = peers.Single(p => p.GetName() == "Put my Desk back" && p.GetAutomationControlType() == AutomationControlType.Button);
                Assert.Equal(AutomationControlType.Button, button.GetAutomationControlType());
                Assert.False(button.IsEnabled());
            }
            finally { window.Close(); }
        });
    }
}
