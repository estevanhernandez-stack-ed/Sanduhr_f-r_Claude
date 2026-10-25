using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Views;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match in the Theme Studio: a Font picker and the Desk look section, saved into the theme and previewed live (the Desk
/// redraws on the preview's ThemeChanged while it follows).</summary>
[Collection("SettingsWindow")]
public sealed class ThemeStudioDeskTests : AppWindowFixture
{
    [Fact]
    public void The_studio_saves_the_font_and_the_desk_look_into_the_theme() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var errors = new List<string>();
        var studio = new ThemeStudioViewModel(widget, errors.Add, _ => { }, () => { }, ["Consolas", "Segoe UI"]);
        studio.Activate();   // starts from the active theme, Obsidian
        Assert.Equal(SettingsNames.ThemeDefaultFont, studio.Font);
        Assert.Equal("#6c63ff, #e8e4dc", studio.DeskEditor.BarsDefault);

        studio.Font = "Consolas";
        studio.DeskEditor.Bars = "#6c63ff, #e8e4dc";
        studio.FileName = "studio-desk";
        studio.SaveCommand.Execute(null);

        Assert.Empty(errors);
        var saved = widget.GetThemeDefinition("studio-desk")!;
        Assert.Equal("Consolas", saved.Font);
        Assert.Equal(["#6c63ff", "#e8e4dc"], saved.Desk!.Bars);
    });

    [Fact]
    public void Starting_from_a_theme_with_a_desk_look_shows_it_and_the_rules_greyed() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk", Font = "Consolas", Desk = new ThemeDesk { Ink = ["#ffffff"] } };
        Directory.CreateDirectory(widget.ThemesDir);
        File.WriteAllText(Path.Combine(widget.ThemesDir, "dusk.json"), ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
        widget.ReloadUserThemes();
        var studio = new ThemeStudioViewModel(widget, _ => { }, _ => { }, () => { }, ["Consolas"]);

        studio.SelectedStart = widget.Themes.First(t => t.Key == "dusk");

        Assert.Equal("Consolas", studio.Font);
        Assert.Equal("#ffffff", studio.DeskEditor.Ink);
        Assert.Equal("", studio.DeskEditor.Bars);
        Assert.Equal("#38bdf8, #e2e8f0", studio.DeskEditor.BarsDefault);
    });

    [Fact]
    public void A_desk_edit_previews_through_ThemeChanged() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        ThemeDefinition? seen = null;
        widget.ThemeChanged += p => seen = p.Definition;
        var studio = new ThemeStudioViewModel(widget, _ => { }, _ => { }, () => { }, []);
        studio.Activate();

        studio.DeskEditor.Ink = "#ffffff";

        Assert.Equal(["#ffffff"], seen!.Desk!.Ink);
        Assert.Equal("obsidian", widget.ActiveThemeKey);   // a preview, nothing saved
        studio.Deactivate();
    });

    [Fact]
    public void A_font_this_pc_lacks_is_kept_in_the_picker_so_it_is_not_blank() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var gone = ThemeCatalog.BuiltIns["aurora"] with { Name = "Gone", Font = "Zzz Missing Face" };
        Directory.CreateDirectory(widget.ThemesDir);
        File.WriteAllText(Path.Combine(widget.ThemesDir, "gone.json"), ThemeDraft.FromDefinition("gone", gone).ToJsonText());
        widget.ReloadUserThemes();
        var studio = new ThemeStudioViewModel(widget, _ => { }, _ => { }, () => { }, ["Consolas"]);

        studio.SelectedStart = widget.Themes.First(t => t.Key == "gone");

        Assert.Equal("Zzz Missing Face", studio.Font);
        Assert.Contains("Zzz Missing Face", studio.FontChoices);
    });

    [Fact]
    public void The_default_font_entry_saves_no_font_key() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var errors = new List<string>();
        var studio = new ThemeStudioViewModel(widget, errors.Add, _ => { }, () => { }, ["Consolas"]);
        studio.Activate();
        studio.Font = "Consolas";
        studio.Font = SettingsNames.ThemeDefaultFont;
        studio.FileName = "plain-font";
        studio.SaveCommand.Execute(null);

        Assert.Empty(errors);
        Assert.Null(widget.GetThemeDefinition("plain-font")!.Font);
        Assert.DoesNotContain("\"font\"", File.ReadAllText(Path.Combine(widget.ThemesDir, "plain-font.json")));
    });

    [Fact]
    public void Switching_to_a_theme_without_a_desk_look_leaves_no_stale_rows() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var dusk = ThemeCatalog.BuiltIns["aurora"] with { Name = "Dusk", Font = "Consolas", Desk = new ThemeDesk { Ink = ["#ffffff"] } };
        Directory.CreateDirectory(widget.ThemesDir);
        File.WriteAllText(Path.Combine(widget.ThemesDir, "dusk.json"), ThemeDraft.FromDefinition("dusk", dusk).ToJsonText());
        widget.ReloadUserThemes();
        var studio = new ThemeStudioViewModel(widget, _ => { }, _ => { }, () => { }, ["Consolas"]);
        studio.SelectedStart = widget.Themes.First(t => t.Key == "dusk");
        Assert.Equal("#ffffff", studio.DeskEditor.Ink);

        studio.SelectedStart = widget.Themes.First(t => t.Key == "obsidian");

        Assert.Equal("", studio.DeskEditor.Ink);
        Assert.Equal("", studio.DeskEditor.Bars);
        Assert.True(studio.DeskEditor.Desk.IsEmpty);
        Assert.Equal(SettingsNames.ThemeDefaultFont, studio.Font);
    });

    [DesktopFact]
    public void The_hosted_editor_and_the_font_picker_wear_the_themed_styles() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Tag == "Themes");
            svm.Themes.IsStudioMode = true;
            window.UpdateLayout();
            Pump();
            guard.Check();

            var editor = Descendants<Sanduhr.App.Views.ThemeDeskEditor>(window).Single();
            var combo = Descendants<ComboBox>(editor).Single();
            Assert.Equal(window.FindResource("ThemedComboBox"), combo.Style);
            Assert.Equal(window.FindResource("ThemedCheckBox"), Descendants<CheckBox>(editor).Single().Style);
            Assert.NotEmpty(Descendants<TextBox>(editor));
            Assert.All(Descendants<TextBox>(editor), t => Assert.Equal(window.FindResource("Input"), t.Style));
            var picker = Descendants<ComboBox>(window).Single(c => AutomationProperties.GetName(c) == SettingsNames.ThemeFontPicker);
            Assert.Equal(window.FindResource("ThemedComboBox"), picker.Style);
        }
        finally { window.Close(); }
    });

    private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        void Walk(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is T t) found.Add(t);
                Walk(c);
            }
        }
        Walk(root);
        return found;
    }
}
