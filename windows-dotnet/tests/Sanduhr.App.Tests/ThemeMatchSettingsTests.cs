using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match on a real SettingsWindow over scratch folders: Desk Look's first section, Follow and Match now, and the Desk's own
/// look greyed while it follows. Every window is shown through <see cref="NoActivate"/>.</summary>
[Collection("SettingsWindow")]
public sealed class ThemeMatchSettingsTests : AppWindowFixture
{
    private (SettingsWindow Window, SettingsViewModel Svm, NoActivate Guard, WidgetViewModel Widget) Open()
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.ThemeMatch.Service.AskAddOrNew = _ => throw new Xunit.Sdk.XunitException("asked add-or-new");
        var window = new SettingsWindow(svm);
        return (window, svm, NoActivate.Show(window), widget);
    }

    private static TabItem TabOf(SettingsWindow window, SettingsPage page) =>
        ((TabControl)window.FindName("Tabs")).Items.OfType<TabItem>().Single(t => (string)t.Tag == page.ToString());

    private static void Settle(SettingsWindow window, NoActivate guard)
    {
        window.UpdateLayout();
        Pump();
        guard.Check();
    }

    private static FrameworkElement Marked(SettingsWindow window, SettingsPage page, string anchor) =>
        SettingsAnchor.Find((DependencyObject)TabOf(window, page).Content, anchor)
            ?? throw new Xunit.Sdk.XunitException($"{page}/{anchor} is not marked");

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
    public void Desk_Look_opens_on_Match_the_widget_theme_with_its_switch_and_button() => AppHost.Run(() =>
    {
        var (window, _, guard, _) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch);
            Settle(window, guard);
            var section = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch);
            Assert.Contains(SettingsNames.FollowThemeSwitch, Below<CheckBox>(section).Select(AutomationProperties.GetName));
            Assert.Contains(SettingsNames.MatchNowButton, Below<Button>(section).Select(AutomationProperties.GetName));
            Assert.Equal(new SettingsLink(SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch), window.Place);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Following_greys_the_desks_own_ink_and_plate_and_Match_now() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);
            svm.ThemeMatch.Follow = true;   // Obsidian is a built-in: a new theme is made, nothing asked
            Settle(window, guard);
            Assert.Equal("obsidian-desk", widget.ActiveThemeKey);
            Assert.Equal(SettingsNames.FollowingTheme("Obsidian + Desk"), svm.ThemeMatch.Status);
            Assert.False(svm.ThemeMatch.MatchNowCommand.CanExecute(null));
            var ink = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskInk);
            Assert.All(Below<ComboBox>(ink), c => Assert.False(c.IsEnabled));
            Assert.Contains(Below<TextBlock>(ink), t => t.Text == SettingsNames.ThemeLookSetsThis && t.IsVisible);
            var plate = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskPlate);
            Assert.All(Below<CheckBox>(plate), c => Assert.False(c.IsEnabled));

            svm.ThemeMatch.Follow = false;   // the look stays, as the Desk's own: the controls read it again
            Settle(window, guard);
            Assert.All(Below<ComboBox>(ink), c => Assert.True(c.IsEnabled));
            Assert.Equal(DeskInkMode.Custom, svm.Desk.InkChoice.Mode);
            Assert.True(svm.ThemeMatch.MatchNowCommand.CanExecute(null));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Cancel_on_a_theme_of_yours_puts_the_switch_back() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk" };
            System.IO.Directory.CreateDirectory(widget.ThemesDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(widget.ThemesDir, "dusk.json"), ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
            widget.ReloadUserThemes();
            widget.ApplyThemeByKey("dusk");
            svm.ThemeMatch.Service.AskAddOrNew = _ => ThemeAddChoice.Cancel;
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);

            svm.ThemeMatch.Follow = true;
            Settle(window, guard);

            Assert.False(svm.ThemeMatch.Follow);
            var box = Below<CheckBox>(Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch))
                .Single(c => AutomationProperties.GetName(c) == SettingsNames.FollowThemeSwitch);
            Assert.False(box.IsChecked);
        }
        finally { window.Close(); }
    });
}
