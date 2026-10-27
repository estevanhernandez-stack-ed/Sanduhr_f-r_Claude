using System.IO;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8 (decision 8): ThemeContrast's surfaces are the ones Settings paints. ThemePalette's brushes, composited the way
/// the window draws them, equal ThemeContrast.SettingsSurfaces within one 8-bit step, and the XAML still draws pages, wells and the
/// sidebar on those brushes over an opaque window.
/// </summary>
public sealed class SettingsContrastTests
{
    private static (double R, double G, double B) Over(Color top, Color under)
    {
        double a = top.A / 255.0;
        return ((top.R * a + under.R * (1 - a)) / 255, (top.G * a + under.G * (1 - a)) / 255, (top.B * a + under.B * (1 - a)) / 255);
    }

    private static (double R, double G, double B) Of(Color c) => (c.R / 255.0, c.G / 255.0, c.B / 255.0);

    [Fact]
    public void The_surfaces_ThemeContrast_measures_are_the_brushes_ThemePalette_sets() => Sta.Run(() =>
    {
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var res = new ResourceDictionary();
            new ThemePalette(key, ThemeCatalog.BuiltIns[key]).Apply(res);
            Color bg = ((SolidColorBrush)res["Sanduhr.Brush.Bg"]).Color;
            Color glass = ((SolidColorBrush)res["Sanduhr.Brush.Glass"]).Color;
            Color title = ((SolidColorBrush)res["Sanduhr.Brush.TitleBg"]).Color;
            Assert.Equal(255, glass.A);   // the page card is solid
            var painted = new[] { Of(glass), Of(bg), Over(title, bg) };
            var measured = ThemeContrast.SettingsSurfaces(ThemeCatalog.BuiltIns[key]);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(Math.Abs(painted[i].R - measured[i].Color.R) <= 1 / 255.0
                    && Math.Abs(painted[i].G - measured[i].Color.G) <= 1 / 255.0
                    && Math.Abs(painted[i].B - measured[i].Color.B) <= 1 / 255.0,
                    $"{key} {measured[i].Name}: painted {ThemeContrast.Hex(painted[i])}, measured {ThemeContrast.Hex(measured[i].Color)}");
            }
        }
    });

    [Fact]
    public void Settings_draws_pages_on_Glass_and_the_sidebar_on_TitleBg_over_an_opaque_Bg_window()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml"));
        Assert.Contains("AllowsTransparency=\"False\"", xaml);
        Assert.Contains("<Border Grid.Column=\"0\" Background=\"{DynamicResource Sanduhr.Brush.TitleBg}\"", xaml);
        Assert.Contains("<Border x:Name=\"RootBorder\"", xaml);
        Assert.Contains("<Border x:Name=\"ThemesRoot\" Background=\"{DynamicResource Sanduhr.Brush.Glass}\"", xaml);
    }
}

/// <summary>Settings v2, slice 8: the sidebar's selected row draws its label in Sanduhr.Brush.SelectedLabel beside an accent bar.</summary>
[Collection("SettingsWindow")]
public sealed class SidebarSelectedLabelTests : AppWindowFixture
{
    [Theory]
    [InlineData("obsidian")]
    [InlineData("aurora")]
    public void The_selected_row_draws_the_themes_selected_label_and_only_it_shows_the_bar(string key) => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        Sanduhr.App.Views.SettingsWindow? window = null;
        try
        {
            var svm = new Sanduhr.App.ViewModels.SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new Sanduhr.App.Updates.UpdateProbeResult(Sanduhr.App.Updates.UpdateProbeStatus.UpToDate)),
                isStoreBuild: false);
            window = new Sanduhr.App.Views.SettingsWindow(svm);
            var def = ThemeCatalog.BuiltIns[key];
            new ThemePalette(key, def).Apply(window.Resources);
            var guard = NoActivate.Show(window);
            window.SelectPage(SettingsPage.Themes);
            window.UpdateLayout();
            guard.Check();
            var tabs = (System.Windows.Controls.TabControl)window.FindName("Tabs");
            var selected = (System.Windows.Controls.TabItem)tabs.SelectedItem;
            var expected = (Color)ColorConverter.ConvertFromString(ThemeContrast.SelectedLabel(def));
            Assert.Equal(expected, ((SolidColorBrush)selected.Foreground).Color);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)selected.Template.FindName("bar", selected)).Visibility);
            var other = tabs.Items.Cast<System.Windows.Controls.TabItem>().First(t => !ReferenceEquals(t, selected));
            other.ApplyTemplate();
            Assert.NotEqual(Visibility.Visible, ((FrameworkElement)other.Template.FindName("bar", other)).Visibility);
        }
        finally
        {
            window?.Close();
            widget.Dispose();
        }
    });
}
