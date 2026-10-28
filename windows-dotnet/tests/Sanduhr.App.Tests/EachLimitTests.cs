using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: Alerts, Each limit (WF13). The Done line's second clause: "a muted limit can be restored from Alerts".</summary>
[Collection("SettingsWindow")]
public sealed class EachLimitTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, WidgetViewModel Widget, NoActivate Guard) Open(bool demo)
    {
        var (widget, _) = ScratchWidget("Personal");
        if (demo) widget.StartDemo();
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

    private static CheckBox Box(SettingsWindow window, string name) =>
        Below<CheckBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == name);

    private string[] Muted() => JsonNode.Parse(File.ReadAllText(ScratchPaths().SettingsFile))!["alert_muted_tiers"] is JsonArray a
        ? [.. a.Select(n => n!.GetValue<string>())] : [];

    /// <summary>Done (spec, slice 5): a muted limit can be restored from Alerts, even one the account doesn't have today.</summary>
    [Fact]
    public void A_muted_limit_can_be_restored_from_Alerts() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(demo: false);
        try
        {
            widget.SetWarnings(TierModel.SevenDayOpus, false);
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            Settle(window, guard);
            var warn = Box(window, SettingsNames.WarnForLimit("Weekly - Opus"));
            Assert.False(warn.IsChecked);
            Assert.True(warn.IsEnabled && warn.IsVisible);

            warn.IsChecked = true;   // what a click sets; the binding writes it through
            Settle(window, guard);

            Assert.Empty(Muted());
            Assert.True(widget.WarningsOn(TierModel.SevenDayOpus));
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_rows_follow_the_card_menu_and_keep_their_place() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(demo: true);
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            Settle(window, guard);
            var session = svm.Alerts.Limits.Single(r => r.Key == TierModel.FiveHour);
            var box = Box(window, SettingsNames.ShowLimit(session.Label));

            widget.HideTier(TierModel.FiveHour);   // the card menu's "Hide this limit"
            Settle(window, guard);

            Assert.Same(session, svm.Alerts.Limits.Single(r => r.Key == TierModel.FiveHour));   // synced in place, not rebuilt
            Assert.False(box.IsChecked);
            Assert.Same(box, Box(window, SettingsNames.ShowLimit(session.Label)));

            box.IsChecked = true;
            Settle(window, guard);
            Assert.DoesNotContain(TierModel.FiveHour, widget.HiddenTierKeys);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void With_alerts_off_Warn_dims_and_Show_still_acts() => AppHost.Run(() =>
    {
        var (window, svm, widget, guard) = Open(demo: true);
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            svm.Alerts.AlertsEnabled = false;
            Settle(window, guard);
            var label = svm.Alerts.Limits[0].Label;
            Assert.False(Box(window, SettingsNames.WarnForLimit(label)).IsEnabled);
            Assert.True(Box(window, SettingsNames.ShowLimit(label)).IsEnabled);
            Assert.True(((FrameworkElement)window.FindName("AlertsOffLine")).IsVisible);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void Signed_out_the_list_says_when_limits_show() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Alerts, SettingsIndex.EachLimit);
            Settle(window, guard);
            var line = (TextBlock)window.FindName("EachLimitEmpty");
            Assert.True(line.IsVisible);
            Assert.Equal(SettingsNames.EachLimitSignedOut, line.Text);
            Assert.False(((Control)window.FindName("EachLimitList")).Focusable);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void HasNoLimits_flips_with_the_rows_and_is_raised() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var vm = new AlertsViewModel(widget, () => Task.CompletedTask);
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            Assert.True(vm.HasNoLimits);
            Assert.Equal(SettingsNames.EachLimitNotYet, vm.LimitsEmptyText);

            widget.StartDemo();
            Assert.False(vm.HasNoLimits);
            Assert.Contains(nameof(AlertsViewModel.HasNoLimits), raised);
            Assert.Equal("", vm.LimitsEmptyText);
            vm.Detach();
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void After_Detach_a_change_no_longer_reloads() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.StartDemo();
            var vm = new AlertsViewModel(widget, () => Task.CompletedTask);
            var key = vm.Limits[0].Key;
            Assert.True(vm.Limits[0].Shown);
            vm.Detach();
            widget.HideTier(key);
            Assert.True(vm.Limits[0].Shown);   // not synced: it stopped following
        }
        finally { widget.Dispose(); }
    });
}
