using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Task 4 of the 4.0 polish wave: the Mods page's outcomes are announced, and the meters rows are named, focusable items with their reason.</summary>
[Collection("SettingsWindow")]
public sealed class ModsPageHeardTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Vm, NoActivate Guard) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        return (window, svm, guard);
    }

    private static void Settle(Window window, NoActivate guard)
    {
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

    [Fact]
    public void A_new_outcome_on_the_status_line_is_announced_and_the_first_state_is_not() => AppHost.Run(() =>
    {
        Home(".claude-work", """{"enabledPlugins":{"a@m":true}}""");
        var announced = new List<string>();
        LiveRegion.Raised = e => announced.Add((e as TextBlock)?.Text ?? "");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Assert.Empty(announced);

            svm.ModsPage.Status = "Outcome one.";
            Settle(window, guard);
            svm.ModsPage.Status = "Outcome two.";
            Settle(window, guard);

            Assert.Equal(["Outcome one.", "Outcome two."], announced);
            var line = (TextBlock)Below<ModsPage>(window).Single().FindName("StatusLine");
            Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Polite, System.Windows.Automation.AutomationProperties.GetLiveSetting(line));
        }
        finally { LiveRegion.Raised = null; window.Close(); }
    });

    [Fact]
    public void A_meters_row_is_a_named_focusable_item_and_an_unreadable_one_says_why() => AppHost.Run(() =>
    {
        Home(".claude", "{}");
        Home(".claude-work", "{ broken");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            var page = Below<ModsPage>(window).Single();

            var peers = Walk(UIElementAutomationPeer.CreatePeerForElement(page)).ToList();
            var unreadable = svm.ModsPage.Meters.Single(r => r.HasReason);
            Assert.Contains(unreadable.Reason, unreadable.RowName);
            foreach (var row in svm.ModsPage.Meters)
            {
                var rowPeer = peers.Single(p => p.GetName() == row.RowName);
                Assert.True(rowPeer.IsKeyboardFocusable(), row.RowName + " is not focusable");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_no_folder_line_is_a_live_region() => AppHost.Run(() =>
    {
        Directory.Delete(Path.Combine(Profile, ".claude"), true);
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            Assert.False(svm.ModsPage.HasMeterFolders);
            var page = Below<ModsPage>(window).Single();
            var line = Below<TextBlock>(page).Single(t => t.Text == SettingsNames.NoFolderInMods);
            Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Polite, System.Windows.Automation.AutomationProperties.GetLiveSetting(line));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_page_unloaded_and_loaded_twice_still_announces_a_change_once() => AppHost.Run(() =>
    {
        // Preflight P11: WPF can raise Unloaded twice for one removal. Each pending Loaded handler must not hook the text again.
        Home(".claude-work", """{"enabledPlugins":{"a@m":true}}""");
        var announced = new List<string>();
        LiveRegion.Raised = e => announced.Add((e as TextBlock)?.Text ?? "");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var line = (TextBlock)Below<ModsPage>(window).Single().FindName("StatusLine");
            for (int i = 0; i < 2; i++)
            {
                line.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, line));
                line.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, line));   // the double Unloaded
                line.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, line));
            }
            Settle(window, guard);

            svm.ModsPage.Status = "Outcome once.";
            Settle(window, guard);

            Assert.Equal(["Outcome once."], announced);
        }
        finally { LiveRegion.Raised = null; window.Close(); }
    });

    [Fact]
    public void Two_identical_outcomes_in_a_row_are_announced_twice() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");
        var announced = new List<string>();
        LiveRegion.Raised = el => announced.Add((el as TextBlock)?.Text ?? "");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            Assert.Empty(announced);

            svm.ModsPage.SwitchPack("good-palette", "Good palette", true);
            Settle(window, guard);
            var first = svm.ModsPage.Status;
            svm.ModsPage.SwitchPack("good-palette", "Good palette", true);
            Settle(window, guard);

            Assert.Equal(first, svm.ModsPage.Status);
            Assert.Equal([first, first], announced);
        }
        finally { LiveRegion.Raised = null; window.Close(); }
    });

    [Fact]
    public void Two_changes_in_one_frame_announce_the_last_text_once() => AppHost.Run(() =>
    {
        Home(".claude-work", """{"enabledPlugins":{"a@m":true}}""");
        var announced = new List<string>();
        LiveRegion.Raised = el => announced.Add((el as TextBlock)?.Text ?? "");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);

            svm.ModsPage.Status = "First.";
            svm.ModsPage.Status = "Second.";
            Settle(window, guard);

            Assert.Equal(["Second."], announced);
        }
        finally { LiveRegion.Raised = null; window.Close(); }
    });

    [Fact]
    public void Loaded_twice_in_a_row_still_announces_a_change_once() => AppHost.Run(() =>
    {
        Home(".claude-work", """{"enabledPlugins":{"a@m":true}}""");
        var announced = new List<string>();
        LiveRegion.Raised = el => announced.Add((el as TextBlock)?.Text ?? "");
        var (window, svm, guard) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.ModList);
            Settle(window, guard);
            var line = (TextBlock)Below<ModsPage>(window).Single().FindName("StatusLine");
            line.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, line));
            line.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, line));

            svm.ModsPage.Status = "Outcome once.";
            Settle(window, guard);

            Assert.Equal(["Outcome once."], announced);
        }
        finally { LiveRegion.Raised = null; window.Close(); }
    });

    [Fact]
    public void A_text_block_set_to_announce_before_it_loads_is_not_hooked_until_it_loads() => AppHost.Run(() =>
    {
        var hooked = (DependencyProperty)typeof(LiveRegion).GetField("IsHookedProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var tb = new TextBlock();
        LiveRegion.SetAnnounces(tb, true);
        Assert.False((bool)tb.GetValue(hooked));
        tb.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, tb));
        Assert.True((bool)tb.GetValue(hooked));
        tb.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, tb));
        Assert.False((bool)tb.GetValue(hooked));
    });

    /// <summary>A meters row is one stop; its switch is the next Tab.</summary>
    [DesktopFact]
    public void The_meters_switch_is_one_Tab_past_its_row() => AppHost.Run(() =>
    {
        Home(".claude", "{}");
        var (window, svm, guard) = Open("Personal");
        try
        {
            guard.RouteKeyboard();
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Meters);
            Settle(window, guard);
            var page = Below<ModsPage>(window).Single();
            var first = svm.ModsPage.Meters[0];
            var item = Below<ListBoxItem>(page).Single(i => i.Content == first);
            Assert.True(item.Focus(), "the row did not take focus");
            Assert.Same(item, Keyboard.FocusedElement);

            item.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

            var focused = Keyboard.FocusedElement as CheckBox;
            Assert.NotNull(focused);
            Assert.Equal(first.SwitchName, System.Windows.Automation.AutomationProperties.GetName(focused));
            guard.Check();
        }
        finally { window.Close(); }
    });
}
