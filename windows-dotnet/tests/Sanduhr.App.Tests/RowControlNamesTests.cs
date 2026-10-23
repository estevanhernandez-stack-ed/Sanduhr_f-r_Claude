using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// A control repeated on every row of a list is named for its row. Every folder's account combo on Settings, Claude
/// Code read "Linked account for this folder", and every message line's buttons read "Remove this line": a screen
/// reader could not tell one row's control from the next. The facts read the names set on the realized controls (GitHub's
/// runner gives list content no automation names, see DesktopFact); the desktop fact reads them the way Narrator does.
/// </summary>
[Collection("SettingsWindow")]
public sealed class RowControlNamesTests : AppWindowFixture
{
    private SettingsWindow Open(string page)
    {
        var widget = DemoWidget();
        var window = new SettingsWindow(new Sanduhr.App.ViewModels.SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        OffScreen(window);
        window.Show();
        var tabs = (TabControl)window.FindName("Tabs");
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == page);
        NamesOf(window); // realizes every row
        return window;
    }

    /// <summary>The automation names set on the buttons, combos, check boxes and text boxes under <paramref name="root"/>.</summary>
    private static List<(Type Kind, string Name)> SetNames(DependencyObject root)
    {
        var names = new List<(Type, string)>();
        void Collect(DependencyObject node)
        {
            if (node is Button or ComboBox or CheckBox or TextBox && AutomationProperties.GetName(node) is { Length: > 0 } name)
                names.Add((node.GetType(), name));
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Collect(VisualTreeHelper.GetChild(node, i));
        }
        Collect(root);
        return names;
    }

    [Fact]
    public void Each_folder_row_names_its_controls_for_its_folder() => AppHost.Run(() =>
    {
        Directory.CreateDirectory(Path.Combine(Profile, ".claude-personal", "projects"));
        var window = Open("ClaudeCode");
        try
        {
            var names = SetNames(window);
            var combos = names.Where(n => n.Kind == typeof(ComboBox) && n.Name.StartsWith("Linked account")).Select(n => n.Name).ToList();

            Assert.Contains(@"Linked account for ~\.claude", combos);
            Assert.Contains(@"Linked account for ~\.claude-personal", combos);
            Assert.Equal(combos.Count, combos.Distinct().Count());
            Assert.DoesNotContain(names, n => n.Name.Contains("this folder"));
        }
        finally { window.Close(); }
    });

    [DesktopFact]
    public void A_screen_reader_tells_the_folder_combos_apart() => AppHost.Run(() =>
    {
        Directory.CreateDirectory(Path.Combine(Profile, ".claude-personal", "projects"));
        var window = Open("ClaudeCode");
        try
        {
            var combos = Walk(UIElementAutomationPeer.CreatePeerForElement(window))
                .Where(p => p.GetAutomationControlType() == AutomationControlType.ComboBox)
                .Select(p => p.GetName())
                .Where(n => n.StartsWith("Linked account"))
                .ToList();
            Assert.Contains(@"Linked account for ~\.claude", combos);
            Assert.Contains(@"Linked account for ~\.claude-personal", combos);
            Assert.Equal(combos.Count, combos.Distinct().Count());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Each_message_line_names_its_controls_for_its_place() => AppHost.Run(() =>
    {
        var window = Open("DeskMessage"); // the starter file's lines
        try
        {
            int rows = window.ViewModel.MessagePage.Rows.Count;
            Assert.True(rows >= 2);
            var names = SetNames(window).Select(n => n.Name).ToList();

            var expected = Enumerable.Range(1, rows).SelectMany(n => new[]
                { $"Remove line {n}", $"Move line {n} up", $"Move line {n} down", $"Line {n} text", $"Line {n} color" });
            var missing = expected.Except(names).ToList();
            Assert.True(missing.Count == 0, "missing: " + string.Join(", ", missing));
            Assert.DoesNotContain(names, n => n.Contains("this line"));
        }
        finally { window.Close(); }
    });
}
