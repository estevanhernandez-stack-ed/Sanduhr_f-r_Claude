using System.Text.RegularExpressions;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 2: every entry point into Settings passes a page and an anchor (spec, "One landing rule"), the
/// --settings switch reaches the running Sanduhr, and Settings opens fitted and where it was left. App cannot run in a
/// test, so its wiring is read from source, like <c>SettingsSourceScanTests</c>.
/// </summary>
public class SettingsEntryPointScanTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([AppDir(), .. parts]));

    [Fact]
    public void Every_App_call_that_names_a_page_also_names_an_anchor()
    {
        var app = Read("App.xaml.cs");
        var pageOnly = Regex.Matches(app, @"ShowSettingsAsync\([^,()]+\)").Select(m => m.Value).ToList();
        Assert.True(pageOnly.Count == 0, "page without an anchor: " + string.Join(", ", pageOnly));
        Assert.Contains("SettingsIndex.MenuAnchor(page)", app);
        Assert.Contains("SettingsIndex.MenuAnchor(SettingsPage.Desk)", app);
        Assert.Contains("SettingsIndex.MenuAnchor(SettingsPage.DeskMessage)", app);
    }

    [Fact]
    public void Show_me_passes_the_cards_anchor()
    {
        var window = Read("Views", "WhatsNewWindow.xaml.cs");
        Assert.Contains("Action<SettingsPage, string?> showMe", window);
        Assert.Contains("callback(destination, anchor);", window);
        Assert.Contains("ShowMe, EndTour", Read("App.xaml.cs"));
    }

    [Fact]
    public void Both_account_menus_offer_Accounts_settings()
    {
        foreach (var file in new[] { "App.xaml.cs", "MainWindow.xaml.cs" })
        {
            var text = Read(file);
            Assert.Contains("PopulateSwitchList(", text);
            Assert.DoesNotContain("includeManage: false", text);
        }
    }

    [Fact]
    public void The_settings_switch_reaches_the_running_Sanduhr_and_a_first_launch()
    {
        var program = Read("Program.cs");
        Assert.Contains("SettingsIndex.LinkArgument(args)", program);
        Assert.Contains("SingleInstance.HandoffDeadline", program);
        Assert.DoesNotContain("TimeSpan.FromSeconds(15)", program);
        var app = Read("App.xaml.cs");
        Assert.Contains("SettingsIndex.LinkFromArgs(", app);
        Assert.Contains("Instance?.Listen(message =>", app);
        Assert.Contains("or Settings at its `--settings` link.", app);
    }

    [Fact]
    public void Settings_opens_fitted_and_where_it_was_left()
    {
        var app = Read("App.xaml.cs");
        int fit = app.IndexOf("_settingsWindow.FitToWorkArea(_window);", StringComparison.Ordinal);
        int show = app.IndexOf("_settingsWindow.Show();", StringComparison.Ordinal);
        Assert.True(fit >= 0 && show > fit, "FitToWorkArea must run before the first Show");
        Assert.Equal(fit, app.LastIndexOf("_settingsWindow.FitToWorkArea(", StringComparison.Ordinal));
        Assert.Contains("SettingsIndex.Check(asked, anchor)", app);
        Assert.Contains("SettingsIndex.Landing(page, anchor, _lastSettings)", app);
        Assert.Contains("_lastSettings = opened.Place;", app);
    }

    [Fact]
    public void Quit_comes_after_the_page_list_and_the_close_button_is_not_a_tab_stop()
    {
        var xaml = Read("Views", "SettingsWindow.xaml");
        Assert.Matches(@"<Grid DockPanel\.Dock=""Top""[^>]*KeyboardNavigation\.TabIndex=""0""", xaml);
        Assert.Matches(@"<Grid KeyboardNavigation\.TabIndex=""1""", xaml);
        Assert.Matches(@"x:Name=""QuitButton""[^>]*KeyboardNavigation\.TabIndex=""2""", xaml);
        Assert.Matches(@"x:Name=""PART_SelectedContentHost""[^>]*KeyboardNavigation\.TabIndex=""3""", xaml);
        var close = Regex.Match(xaml, @"x:Key=""CloseButton""[\s\S]*?</Style>");
        Assert.True(close.Success, "no CloseButton style");
        Assert.Contains(@"<Setter Property=""IsTabStop"" Value=""False"" />", close.Value);
    }
}
