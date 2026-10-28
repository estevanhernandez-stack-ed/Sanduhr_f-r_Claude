using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: the Done line, and the six pages it touches read and land the way the spec says.</summary>
[Collection("SettingsWindow")]
public sealed class Slice5PagesTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();   // sample limits for Each limit and Limit order, over scratch folders
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        return (window, svm, widget, NoActivate.Show(window));
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
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

    private static string Spoken(UIElement e) => UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() ?? "";

    private static T Named<T>(DependencyObject root, string name) where T : UIElement =>
        Below<T>(root).Single(e => e.IsVisible && Spoken(e) == name);

    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    /// <summary>Done (spec, slice 5): every key in the widget-only list has a Settings control. Each control is found by its spoken
    /// name and acted on the way a person would, and the key it writes is read back from the file it lives in.</summary>
    [Fact]
    public void Every_widget_only_key_has_a_Settings_control() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open();
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            Settle(window, guard);

            Named<ComboBox>(window, SettingsNames.GraphStyle).SelectedItem = WidgetPageViewModel.GraphStyles.Single(o => o.Value == SparklineStyle.Horizon);
            Named<CheckBox>(window, SettingsNames.KeepWidgetOnTop).IsChecked = true;
            var minutes = Named<TextBox>(window, SettingsNames.DeepWorkLengthSpoken);
            minutes.Text = "50";
            minutes.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();   // what leaving the box does
            var compact = Named<CheckBox>(window, SettingsNames.CompactMode);
            compact.Command.Execute(compact.CommandParameter);                   // what a click runs
            var first = widget.ShownLimitOrder[0];
            var down = Named<Button>(window, SettingsNames.MoveLimitDown(TierModel.Label(first)));
            down.Command.Execute(null);
            Settle(window, guard);

            window.SelectPage(SettingsPage.About, SettingsIndex.Version);
            Settle(window, guard);
            Named<CheckBox>(window, SettingsNames.HideWhatsNewAfterUpdates).IsChecked = true;

            // The widget's own keys live in its settings.json (scratch); About's in the Settings window's (the fixture's dev profile).
            var widgetKeys = (JsonObject)JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;
            Assert.Equal("Horizon", (string?)widgetKeys["sparkline_style"]);
            Assert.True((bool?)widgetKeys["pinned"]);
            Assert.Equal(50, (int?)widgetKeys["focus_mode_duration"]);
            Assert.NotEqual(first, (string?)((JsonArray)widgetKeys["tier_order"]!)[0]);
            Assert.True(widget.IsCompact);
            var aboutKeys = (JsonObject)JsonNode.Parse(File.ReadAllText(new Sanduhr.Core.Paths().SettingsFile))!;
            Assert.True((bool?)aboutKeys["whats_new_hide_after_updates"]);
        }
        finally { window.Close(); }
    });

    /// <summary>Every button and box on the slice's pages is spoken with its visible label first (WF34; WCAG 2.5.3), item templates included,
    /// and no name leaks a type.</summary>
    [Fact]
    public void Every_control_on_the_slice_5_pages_is_spoken_with_its_label_first() => AppHost.Run(() =>
    {
        var (window, svm, _, guard) = Open();
        try
        {
            var bad = new List<string>();
            var leaks = new List<string>();
            // Whole pages for the five this slice rebuilds; on About only the Version section, the one part this slice changes.
            foreach (var (page, anchor, wholePage) in new (SettingsPage, string?, bool)[]
            {
                (SettingsPage.Accounts, null, true), (SettingsPage.Alerts, SettingsIndex.EachLimit, true),
                (SettingsPage.ClaudeUsage, SettingsIndex.MeterHistory, true), (SettingsPage.Publish, SettingsIndex.Sample, true),
                (SettingsPage.Widget, SettingsIndex.WidgetOptions, true), (SettingsPage.About, SettingsIndex.Version, false),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());
                DependencyObject root = wholePage ? (DependencyObject)tab.Content : SettingsAnchor.Find((DependencyObject)tab.Content, anchor!)!;
                leaks.AddRange(LeaksIn(NamesOf(window)).Select(n => $"{page}: \"{n}\""));   // while this page is realized (P19)
                foreach (var b in Below<ButtonBase>(root).Where(b => b.IsVisible && b.Content is string))
                {
                    var name = Spoken(b);
                    if (name.Length == 0 || !name.StartsWith(Bare((string)b.Content), StringComparison.Ordinal))
                        bad.Add($"{page}: \"{b.Content}\" is spoken \"{name}\"");
                }
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
            Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
        }
        finally { window.Close(); }
    });

    /// <summary>A landing on Each limit puts the keyboard on the first limit's Warn box; on the Widget page, on Compact mode.</summary>
    [DesktopFact]
    public void A_landing_puts_the_keyboard_on_the_sections_first_control() => AppHost.Run(() =>
    {
        var (window, svm, _, guard) = Open();
        try
        {
            guard.RouteKeyboard();
            foreach (var (page, anchor, expected) in new[]
            {
                (SettingsPage.Alerts, SettingsIndex.EachLimit, SettingsNames.WarnForLimit(svm.Alerts.Limits[0].Label)),
                (SettingsPage.Widget, SettingsIndex.WidgetOptions, SettingsNames.CompactMode),
            })
            {
                window.SelectPage(page, anchor);
                Settle(window, guard);
                var focused = Keyboard.FocusedElement as UIElement;
                var name = focused is null ? "" : Spoken(focused);
                Assert.True(name == expected,
                    $"focus is on {focused?.GetType().Name ?? "nothing"} \"{name}\" on {window.Place}, not \"{expected}\"");
                guard.Check();
            }
        }
        finally { window.Close(); }
    });
}