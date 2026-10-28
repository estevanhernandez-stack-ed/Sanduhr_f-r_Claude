using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 5: the meter history is Claude Usage's first view (WF14), and the History page's old links land on it.</summary>
[Collection("SettingsWindow")]
public sealed class ClaudeUsageMetersTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard, WidgetViewModel Widget) Open(params string[] accounts)
    {
        var (widget, _) = ScratchWidget(accounts);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        return (window, svm, NoActivate.Show(window), widget);
    }

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static TabItem TabOf(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    [Fact]
    public void Claude_Usage_opens_on_Meters_with_the_empty_line_until_there_is_history() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeUsage);
            Settle(window, guard);
            Assert.Equal(SettingsIndex.MetersView, svm.ClaudeCode.Section);
            var line = (TextBlock)window.FindName("HistoryEmptyText");
            Assert.True(line.IsVisible, $"the empty line is not shown on {window.Place}");
            Assert.Equal(SettingsNames.MeterHistoryEmpty, line.Text);
            Assert.Same(svm.History, svm.ClaudeCode.Meters);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void A_history_link_lands_on_the_meters_view_in_view() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open("Personal");
        try
        {
            svm.ClaudeCode.Section = SettingsIndex.SessionsView;
            window.SelectPage(SettingsPage.History, SettingsIndex.HistoryExport);
            Settle(window, guard);
            Assert.Equal(new SettingsLink(SettingsPage.ClaudeUsage, SettingsIndex.MeterHistoryExport), window.Place);
            Assert.Equal(SettingsIndex.MetersView, svm.ClaudeCode.Section);
            var export = SettingsAnchor.Find((DependencyObject)TabOf(window, SettingsPage.ClaudeUsage).Content, SettingsIndex.MeterHistoryExport)!;
            Assert.True(export.IsVisible);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void The_Meters_pill_shows_the_view_and_redraws_the_chart() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open("Personal");
        try
        {
            window.SelectPage(SettingsPage.ClaudeUsage, SettingsIndex.Sessions);
            Settle(window, guard);
            int redraws = 0;
            svm.History.Changed += () => redraws++;

            svm.ClaudeCode.SetMetersCommand.Execute(null);
            Settle(window, guard);

            Assert.Equal(SettingsIndex.MetersView, svm.ClaudeCode.Section);
            Assert.True(redraws > 0, "the chart was not redrawn");
            Assert.True(((FrameworkElement)window.FindName("Chart")).IsVisible || ((FrameworkElement)window.FindName("HistoryEmptyText")).IsVisible);
        }
        finally { window.Close(); widget.Dispose(); }
    });

    [Fact]
    public void A_card_that_named_History_says_it_opens_Claude_Usage()
    {
        var card = WhatsNewCard.Of("3.0.0", "history-card", "Meter history", "Body.", "", WhatsNewPreview.None, SettingsPage.History);
        Assert.Equal("Opens Settings, Claude Usage", new WhatsNewCardItem(card, null).ShowMeToolTip);
    }
}
