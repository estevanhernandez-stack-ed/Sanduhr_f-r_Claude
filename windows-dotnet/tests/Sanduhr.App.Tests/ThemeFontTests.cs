using System.IO;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Theme match: a theme's <c>font</c> draws the widget's text when this PC has the family; Segoe UI otherwise; Matrix and
/// Blueprint keep their monospace values.</summary>
public sealed class ThemeFontTests
{
    private static FontFamily? Only(string family, string name) => name == family ? new FontFamily(family) : null;

    [Fact]
    public void A_theme_without_a_font_keeps_the_widget_in_Segoe_UI() => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        new ThemePalette("obsidian", ThemeCatalog.BuiltIns["obsidian"], f => Only("Consolas", f)).Apply(res);
        Assert.Equal(ThemePalette.DefaultUiFamily, ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        Assert.Equal(ThemePalette.DefaultUiFamily, ((FontFamily)res["Sanduhr.Font.Value"]).Source);
    });

    [Fact]
    public void A_themes_font_draws_the_text_and_the_values_when_the_pc_has_it() => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        new ThemePalette("t", ThemeCatalog.BuiltIns["obsidian"] with { Font = "Consolas" }, f => Only("Consolas", f)).Apply(res);
        Assert.Equal("Consolas", ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        Assert.Equal("Consolas", ((FontFamily)res["Sanduhr.Font.Value"]).Source);
        // Missing on this PC: Segoe UI, not a family the theme never named.
        new ThemePalette("t", ThemeCatalog.BuiltIns["obsidian"] with { Font = "Nope Sans" }, f => Only("Consolas", f)).Apply(res);
        Assert.Equal(ThemePalette.DefaultUiFamily, ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        // Matrix keeps its monospace values whatever its font.
        new ThemePalette("matrix", ThemeCatalog.BuiltIns["matrix"] with { Font = "Consolas" }, f => Only("Consolas", f)).Apply(res);
        Assert.Equal("Consolas", ((FontFamily)res["Sanduhr.Font.Ui"]).Source);
        Assert.Equal("Cascadia Code, Consolas", ((FontFamily)res["Sanduhr.Font.Value"]).Source);
    });

    [Fact]
    public void DeskFonts_knows_bundled_installed_and_pack_families() => Sta.Run(() =>
    {
        var fonts = new DeskFonts(["Consolas"], new Dictionary<string, Uri> { ["Pack Hand"] = new Uri("file:///C:/fonts/") });
        Assert.True(fonts.Has("EsteFont Pro"));
        Assert.True(fonts.Has("consolas"));
        Assert.True(fonts.Has("Pack Hand"));
        Assert.False(fonts.Has("Nope Sans"));
    });

    [Fact]
    public void The_widget_window_takes_its_font_from_the_theme()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "MainWindow.xaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "MainWindow.xaml"));
        Assert.Contains("FontFamily=\"{DynamicResource Sanduhr.Font.Ui}\"", xaml);
    }
}
