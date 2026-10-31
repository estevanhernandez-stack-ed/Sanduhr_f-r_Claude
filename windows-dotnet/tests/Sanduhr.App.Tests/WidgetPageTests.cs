using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: the Widget page (WF15, WF24). Every widget-only choice gets a Settings control.</summary>
[Collection("SettingsWindow")]
public sealed class WidgetPageTests : AppWindowFixture
{
    private JsonObject Settings() => (JsonObject)JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!;

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
    public void The_page_follows_the_widget_and_writes_through_it() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new WidgetPageViewModel(widget);
        try
        {
            page.SelectedGraphStyle = WidgetPageViewModel.GraphStyles.Single(o => o.Value == SparklineStyle.Horizon);
            page.KeepOnTop = true;
            page.DeepWorkMinutes = "45";
            page.ToggleCompactCommand.Execute(null);

            Assert.Equal(SparklineStyle.Horizon, widget.SparklineStyle);
            Assert.True(widget.Pinned);
            Assert.Equal(45, widget.LoadFocusDuration());
            Assert.True(page.IsCompact);
            Assert.Equal(("Horizon", true, 45), ((string?)Settings()["sparkline_style"], (bool?)Settings()["pinned"], (int?)Settings()["focus_mode_duration"]));

            // The widget's strip changes it (its cycle calls SetSparklineStyle, then plays a sound a test doesn't want): the page follows.
            widget.SetSparklineStyle(SparklineStyle.Classic);
            Assert.Equal(SparklineStyle.Classic, page.SelectedGraphStyle.Value);
        }
        finally { page.Detach(); widget.Dispose(); }
    });

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("ten")]
    [InlineData("")]
    public void A_Deep_Work_length_out_of_range_is_not_saved_and_says_why(string typed) => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new WidgetPageViewModel(widget);
        try
        {
            page.DeepWorkMinutes = typed;
            Assert.Equal(25, widget.LoadFocusDuration());
            Assert.True(page.HasDeepWorkHint);
            Assert.Equal(SettingsNames.DeepWorkRange, page.DeepWorkHint);
        }
        finally { page.Detach(); widget.Dispose(); }
    });

    [Fact]
    public void Limit_order_moves_a_limit_and_offers_no_move_off_the_ends() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();
        var page = new WidgetPageViewModel(widget);
        try
        {
            Assert.Equal(widget.ShownLimitOrder, page.Limits.Select(r => r.Key));
            Assert.False(page.Limits[0].CanMoveUp);
            Assert.False(page.Limits[^1].CanMoveDown);
            var second = page.Limits[1];
            Assert.True(second.CanMoveUp);

            second.MoveUpCommand.Execute(null);

            Assert.Equal(second.Key, widget.ShownLimitOrder[0]);
            Assert.Same(second, page.Limits[0]);   // synced in place
            Assert.False(second.CanMoveUp);
            Assert.Equal(second.Key, (string?)((JsonArray)Settings()["tier_order"]!)[0]);
        }
        finally { page.Detach(); widget.Dispose(); }
    });

    /// <summary>P4: the empty line is a live region, so HasNoLimits flips and is raised with the rows; P17: its words are the widget's.</summary>
    [Fact]
    public void HasNoLimits_flips_with_the_rows_and_the_empty_line_is_the_widgets() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new WidgetPageViewModel(widget);
        try
        {
            var raised = new List<string?>();
            page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            Assert.True(page.HasNoLimits);
            Assert.Equal(widget.LimitsEmptyLine(), page.LimitsEmptyText);
            Assert.Equal(SettingsNames.EachLimitNotYet, page.LimitsEmptyText);

            widget.StartDemo();
            Assert.False(page.HasNoLimits);
            Assert.Contains(nameof(WidgetPageViewModel.HasNoLimits), raised);
            Assert.Equal("", page.LimitsEmptyText);
            Assert.NotEmpty(page.Limits);
        }
        finally { page.Detach(); widget.Dispose(); }
    });

    [Fact]
    public void After_Detach_a_change_no_longer_reloads() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var page = new WidgetPageViewModel(widget);
        try
        {
            page.Detach();
            widget.StartDemo();
            Assert.Empty(page.Limits);
            Assert.True(page.HasNoLimits);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_page_sits_first_in_the_Widget_group_and_opens_on_its_options() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tabs = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().ToList();
            var tab = tabs.Single(t => (string)t.Tag == "Widget");
            Assert.Equal(tabs.IndexOf(tabs.Single(t => (string)t.Tag == "Themes")) - 1, tabs.IndexOf(tab));
            Assert.Equal("Widget", SettingsSidebar.GetGroupHeader(tab));
            Assert.True(SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.WidgetOptions)!.IsVisible);
            Assert.Equal(new SettingsLink(SettingsPage.Widget, SettingsIndex.WidgetOptions), window.Place);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>P22: the focus timer's own stepper writes the key behind the page's back; the box reads it again on each visit.</summary>
    [Fact]
    public void Deep_Work_length_is_read_again_each_time_the_page_is_selected() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            window.UpdateLayout();
            Pump();
            Assert.Equal("25", svm.WidgetPage.DeepWorkMinutes);

            window.SelectPage(SettingsPage.Themes, null);
            window.UpdateLayout();
            Pump();
            widget.SaveFocusDuration(50);   // the timer's stepper, not the page
            Assert.Equal("25", svm.WidgetPage.DeepWorkMinutes);

            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            window.UpdateLayout();
            Pump();
            guard.Check();

            Assert.Equal("50", svm.WidgetPage.DeepWorkMinutes);
            Assert.False(svm.WidgetPage.HasDeepWorkHint);
            Assert.Equal(50, widget.LoadFocusDuration());
        }
        finally { window.Close(); widget.Dispose(); }
    });

    /// <summary>P13: after a Move the keyboard stays on the moved row's same button, or its other one when that button collapsed.</summary>
    [DesktopFact]
    public void After_a_move_the_keyboard_stays_on_the_moved_limit() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        widget.StartDemo();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.WidgetOptions);
            window.UpdateLayout();
            Pump();
            guard.RouteKeyboard();
            var page = svm.WidgetPage;
            var list = Below<ItemsControl>(window).Single(l => l.Name == "LimitOrderList");

            Button ButtonOf(string key, string name) => Below<Button>(list)
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name && ((LimitOrderRowViewModel)b.DataContext).Key == key);

            void Invoke(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

            // Move the second limit up: it is now first, so Move up collapses and focus goes to its Move down.
            var moved = page.Limits[1];
            var up = ButtonOf(moved.Key, moved.MoveUpName);
            Assert.True(up.Focus());
            Invoke(up);
            Pump();
            window.UpdateLayout();
            Pump();
            guard.Check();
            Assert.Same(moved, page.Limits[0]);
            var focused = Keyboard.FocusedElement as Button;
            Assert.NotNull(focused);
            Assert.Equal(moved.MoveDownName, System.Windows.Automation.AutomationProperties.GetName(focused));

            // Move it down again: Move down still acts (the second row), and focus stays on it.
            Invoke(focused);
            Pump();
            window.UpdateLayout();
            Pump();
            guard.Check();
            Assert.Same(moved, page.Limits[1]);
            focused = Keyboard.FocusedElement as Button;
            Assert.NotNull(focused);
            Assert.Equal(moved.MoveDownName, System.Windows.Automation.AutomationProperties.GetName(focused));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_Widget_page_offers_Set_up_Sanduhr_while_usage_is_off() => AppHost.Run(() =>
    {
        new Sanduhr.App.Services.SettingsStore(ScratchPaths())
            .SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget();
        var page = new WidgetPageViewModel(widget);
        try
        {
            bool opened = false;
            page.SetupRequested = () => opened = true;
            Assert.True(page.ShowUsageOff);
            page.OpenSetupCommand.Execute(null);
            Assert.True(opened);
        }
        finally { page.Detach(); widget.Dispose(); }
    });

    [Fact]
    public void The_Widget_page_follows_usage_turning_on() => AppHost.Run(() =>
    {
        new Sanduhr.App.Services.SettingsStore(ScratchPaths())
            .SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget();   // no account: turning usage on builds no client and reaches no network
        var page = new WidgetPageViewModel(widget);
        try
        {
            Assert.True(page.ShowUsageOff);
            widget.SetUsageOn(true);
            Assert.False(page.ShowUsageOff);
        }
        finally { page.Detach(); widget.Dispose(); }
    });

    [Fact]
    public void The_section_shows_only_while_usage_is_off() => AppHost.Run(() =>
    {
        new Sanduhr.App.Services.SettingsStore(ScratchPaths())
            .SaveSetup(new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = false }));
        var (widget, _) = ScratchWidget();   // no account: turning usage on builds no client and reaches no network
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Widget, SettingsIndex.UsageOffSection);
            window.UpdateLayout();
            Pump();
            guard.Check();
            var tab = ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == "Widget");
            var section = SettingsAnchor.Find((DependencyObject)tab.Content, SettingsIndex.UsageOffSection)!;
            Assert.True(section.IsVisible);

            widget.SetUsageOn(true);
            window.UpdateLayout();
            Pump();
            Assert.False(section.IsVisible);
        }
        finally { window.Close(); widget.Dispose(); }
    });
}
