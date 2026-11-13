using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// Every row on Settings, Desk, Pieces reaches UI Automation with its controls, the 4.8.0 Shows picker included, read
/// both with caches dropped and as a live client meets them. In the 4.8.0 hand pass a long-running session's UI
/// Automation client saw no controls under any row after the first (Tab still moved focus); a fresh run did not repeat
/// it, and this pins the tree the rows build.
/// </summary>
[Collection("SettingsWindow")]
public sealed class DeskPieceRowsHeardTests : AppWindowFixture
{
    [Fact]
    public void Every_piece_row_is_heard_with_its_controls() => AppHost.Run(() =>
    {
        var window = new SettingsWindow(new Sanduhr.App.ViewModels.SettingsViewModel(DemoWidget(), () => Task.CompletedTask, _ => Task.CompletedTask));
        OffScreen(window);
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Desk");
            NamesOf(window);   // realizes every row
            var names = CachedNames(UIElementAutomationPeer.CreatePeerForElement(window));

            var rows = window.ViewModel.Desk.Pieces;
            Assert.True(rows.Count >= 3);
            var missing = rows.SelectMany(r => new[] { r.ShownName, $"{r.Name}, place", $"{r.Name}, monitor" })
                .Concat(rows.Where(r => r.HasStyle).Select(r => $"{r.Name}, style"))
                .Concat(rows.Where(r => r.HasSource).Select(r => $"{r.Name}, shows"))
                .Except(names).ToList();
            Assert.True(missing.Count == 0, "not heard: " + string.Join(", ", missing));
        }
        finally { window.Close(); }
    });

    /// <summary>The names as a live client meets them: each peer's children as it lists them, caches and all.</summary>
    private static List<string> CachedNames(AutomationPeer root)
    {
        var names = new List<string>();
        void Visit(AutomationPeer p)
        {
            try { names.Add(p.GetName() ?? ""); } catch (NullReferenceException) { }
            IList<AutomationPeer>? children = null;
            try { children = p.GetChildren(); } catch (NullReferenceException) { }
            foreach (var c in children ?? []) Visit(c);
        }
        Visit(root);
        return names;
    }
}
