using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Controls;
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
            // One line, once, under the section: it covers the font, ink and plate controls, so it is not inside Ink and color.
            var page = (DependencyObject)TabOf(window, SettingsPage.DeskLook).Content;
            var line = Assert.Single(Below<TextBlock>(page), t => t.Text == SettingsNames.ThemeLookSetsThis);
            Assert.True(line.IsVisible);
            Assert.DoesNotContain(line, Below<TextBlock>(ink));
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

            var box = Below<CheckBox>(Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch))
                .Single(c => AutomationProperties.GetName(c) == SettingsNames.FollowThemeSwitch);
            ((IToggleProvider)new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle)!).Toggle();   // the real switch
            Settle(window, guard);

            Assert.False(svm.ThemeMatch.Follow);
            Assert.False(box.IsChecked);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_change_made_elsewhere_is_read_again_by_the_section_and_the_own_look_controls() => AppHost.Run(() =>
    {
        var (window, svm, guard, _) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook);
            svm.ThemeMatch.Follow = true;
            Settle(window, guard);
            var ink = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskInk);
            Assert.All(Below<ComboBox>(ink), c => Assert.False(c.IsEnabled));

            new SettingsStore(new Paths()).SaveDeskFollowsTheme(false);   // Put my Desk back, a design or the MCP did this
            svm.Desk.RefreshFromStore();
            svm.ThemeMatch.Reload();
            Settle(window, guard);

            Assert.False(svm.ThemeMatch.Follow);
            Assert.All(Below<ComboBox>(ink), c => Assert.True(c.IsEnabled));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Opening_while_the_Desk_already_follows_shows_the_followed_line() => AppHost.Run(() =>
    {
        new SettingsStore(new Paths()).SaveDeskFollowsTheme(true);
        var (window, svm, guard, _) = Open();
        try
        {
            Settle(window, guard);
            Assert.True(svm.ThemeMatch.Follow);
            Assert.Equal(SettingsNames.FollowingTheme("Obsidian"), svm.ThemeMatch.Status);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_editor_shows_only_while_following_and_an_edit_updates_the_theme() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook);
            Settle(window, guard);
            var section = Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch);
            Assert.False(Below<ThemeDeskEditor>(section).Single().IsVisible);

            svm.ThemeMatch.Follow = true;
            Settle(window, guard);
            Assert.True(Below<ThemeDeskEditor>(section).Single().IsVisible);
            Assert.Equal("#e8e4dc, #6c63ff", svm.ThemeMatch.Editor.Ink);   // Follow spelled the look out into Obsidian + Desk

            svm.ThemeMatch.Service.AskWhere = (_, canUpdate) => { Assert.True(canUpdate); return ThemeEditChoice.UpdateTheme; };
            svm.ThemeMatch.Editor.Ink = "#ffffff";
            Settle(window, guard);
            Assert.Equal(["#ffffff"], widget.GetThemeDefinition("obsidian-desk")!.Desk!.Ink);
            Assert.Equal(SettingsNames.ThemeUpdated("Obsidian + Desk"), svm.ThemeMatch.Status);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_cancelled_edit_puts_the_editor_back() => AppHost.Run(() =>
    {
        var (window, svm, guard, widget) = Open();
        try
        {
            svm.ThemeMatch.Follow = true;
            svm.ThemeMatch.Service.AskWhere = (_, _) => ThemeEditChoice.Cancel;
            svm.ThemeMatch.Editor.Ink = "#ffffff";
            Settle(window, guard);
            Assert.Equal("#e8e4dc, #6c63ff", svm.ThemeMatch.Editor.Ink);
            Assert.Equal(["#e8e4dc", "#6c63ff"], widget.GetThemeDefinition("obsidian-desk")!.Desk!.Ink);
        }
        finally { window.Close(); }
    });

    private static ThemeDeskEditor FollowingEditor(SettingsWindow window, SettingsViewModel svm, NoActivate guard)
    {
        window.SelectPage(SettingsPage.DeskLook);
        svm.ThemeMatch.Follow = true;
        Settle(window, guard);
        return Below<ThemeDeskEditor>(Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch)).Single();
    }

    [Fact]
    public void The_font_commits_once_when_the_list_closes_never_per_arrow_key() => AppHost.Run(() =>
    {
        var (window, svm, guard, _) = Open();
        try
        {
            var editor = FollowingEditor(window, svm, guard);
            var combo = Below<ComboBox>(editor).Single();
            Assert.True(svm.ThemeMatch.Editor.FontChoices.Count > 2);
            int asked = 0;
            svm.ThemeMatch.Service.AskWhere = (_, _) => { asked++; return ThemeEditChoice.UpdateTheme; };
            combo.SelectedIndex = 1;
            combo.SelectedIndex = 2;
            Settle(window, guard);
            Assert.Equal(0, asked);
            var last = svm.ThemeMatch.Editor.FontChoices[2];
            typeof(ComboBox).GetMethod("OnDropDownClosed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(combo, [EventArgs.Empty]);
            Settle(window, guard);
            Assert.Equal(1, asked);
            Assert.Equal(last, svm.ThemeMatch.Editor.Desk.Font);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_greyed_default_steps_aside_while_typing_and_a_cancel_puts_the_text_back() => AppHost.Run(() =>
    {
        var (window, svm, guard, _) = Open();
        try
        {
            var editor = FollowingEditor(window, svm, guard);
            var boxes = Below<TextBox>(editor).ToList();
            var bars = boxes[1];   // Follow spelled the ink out; the bars are still the rule's
            var overlay = Below<DecorativeText>(editor).ElementAt(1);
            Assert.Equal(Visibility.Visible, overlay.Visibility);
            bars.Text = "#fff";   // typing: the row has not lost focus yet
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            bars.Text = "";

            svm.ThemeMatch.Service.AskWhere = (_, _) => ThemeEditChoice.Cancel;
            var ink = boxes[0];
            ink.Focus();
            ink.Text = "#ffffff";
            boxes[2].Focus();   // the row is left: it commits, and Cancel puts it back
            Settle(window, guard);
            Assert.Equal("#e8e4dc, #6c63ff", ink.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_greyed_default_is_not_read_twice() => AppHost.Run(() =>
    {
        var (window, svm, guard, _) = Open();
        try
        {
            window.SelectPage(SettingsPage.DeskLook);
            svm.ThemeMatch.Follow = true;
            Settle(window, guard);
            var editor = Below<ThemeDeskEditor>(Marked(window, SettingsPage.DeskLook, SettingsIndex.DeskThemeMatch)).Single();
            var overlays = Below<DecorativeText>(editor).ToList();
            Assert.NotEmpty(overlays);
            foreach (var o in overlays)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(o);
                Assert.False(peer.IsControlElement());
                Assert.False(peer.IsContentElement());
            }
            var box = Below<TextBox>(editor).First();
            Assert.Equal(SettingsNames.ThemeInkColors, AutomationProperties.GetName(box));
            Assert.Equal(svm.ThemeMatch.Editor.InkDefault, AutomationProperties.GetHelpText(box));
        }
        finally { window.Close(); }
    });
}
