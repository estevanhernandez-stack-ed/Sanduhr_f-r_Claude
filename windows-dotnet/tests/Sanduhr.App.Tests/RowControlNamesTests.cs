using System.IO;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// A control repeated on every row of a list is named for its row. Every folder's account combo on Settings, Claude
/// Code read "Linked account for this folder", and every message line's buttons read "Remove this line": a screen
/// reader could not tell one row's control from the next.
/// </summary>
[Collection("SettingsWindow")]
public sealed class RowControlNamesTests : AppWindowFixture
{
    /// <summary>The names of the interactive controls on one Settings page, as UI Automation reads them.</summary>
    private static List<(AutomationControlType Type, string Name)> ControlsOn(SettingsWindow window, string page)
    {
        var tabs = (TabControl)window.FindName("Tabs");
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == page);
        NamesOf(window);
        var controls = new List<(AutomationControlType, string)>();
        foreach (var peer in Walk(UIElementAutomationPeer.CreatePeerForElement(window)))
        {
            try
            {
                var type = peer.GetAutomationControlType();
                if (type is AutomationControlType.Button or AutomationControlType.ComboBox or AutomationControlType.CheckBox or AutomationControlType.Edit)
                    controls.Add((type, peer.GetName() ?? ""));
            }
            catch (NullReferenceException) { /* a stale item peer */ }
        }
        return controls;
    }

    private SettingsWindow Open()
    {
        var widget = DemoWidget();
        var window = new SettingsWindow(new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        OffScreen(window);
        window.Show();
        return window;
    }

    [Fact]
    public void Each_folder_row_names_its_account_combo_for_its_folder() => AppHost.Run(() =>
    {
        Directory.CreateDirectory(Path.Combine(Profile, ".claude-personal", "projects"));
        var window = Open();
        try
        {
            var controls = ControlsOn(window, "ClaudeCode");
            var combos = controls.Where(c => c.Type == AutomationControlType.ComboBox && c.Name.StartsWith("Linked account")).Select(c => c.Name).ToList();

            Assert.Contains(@"Linked account for ~\.claude", combos);
            Assert.Contains(@"Linked account for ~\.claude-personal", combos);
            Assert.DoesNotContain("Linked account for this folder", combos);
            Assert.Equal(combos.Count, combos.Distinct().Count());
            Assert.DoesNotContain(controls, c => c.Name.Contains("this folder"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Each_message_line_names_its_controls_for_its_place() => AppHost.Run(() =>
    {
        var window = Open();
        try
        {
            ControlsOn(window, "DeskMessage"); // shows the page: the starter file's lines
            Assert.True(window.ViewModel.MessagePage.Rows.Count >= 2);
            // The names as set on the realized controls (the peer walk stops at the first item of this list).
            var names = new List<string>();
            void Collect(System.Windows.DependencyObject node)
            {
                if (node is Button or ComboBox or TextBox && System.Windows.Automation.AutomationProperties.GetName(node) is { Length: > 0 } name)
                    names.Add(name);
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    Collect(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
            }
            Collect(window);

            var expected = Enumerable.Range(1, window.ViewModel.MessagePage.Rows.Count).SelectMany(n => new[]
                { $"Remove line {n}", $"Move line {n} up", $"Move line {n} down", $"Line {n} text", $"Line {n} color" });
            var missing = expected.Except(names).ToList();
            Assert.True(missing.Count == 0, "missing: " + string.Join(", ", missing));
            Assert.DoesNotContain(names, n => n.Contains("this line"));
        }
        finally { window.Close(); }
    });
}
