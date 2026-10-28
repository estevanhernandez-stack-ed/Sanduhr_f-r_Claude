using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Slice 8 final review M5 (owner taste 3): the Reset dialog paints its frame solid, so every line in it reads 4.5:1 or
/// better on the frame in every built-in theme, whatever window or wallpaper is behind it.</summary>
[Collection("SettingsWindow")]
public sealed class ResetWindowSurfaceTests : AppWindowFixture
{
    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    public static TheoryData<string> BuiltIns()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThemeCatalog.BuiltInOrder) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void The_frame_is_opaque_and_every_line_reads_on_it(string key) => AppHost.Run(() =>
    {
        // One row, so the row template realizes and its TextDim kinds line (the 11 px line M5 is about) is measured too.
        var row = new ResetFolderRow(new ResetFolder(Path.Combine(Root, ".claude"), [IntegrationKind.Statusline, IntegrationKind.Meters]), true);
        var window = new ResetWindow(null, ResetFolderListViewModel.For([row], unreadable: true));
        new ThemePalette(key, ThemeCatalog.BuiltIns[key]).Apply(window.Resources);
        OffScreen(window);
        window.Show();
        try
        {
            var frame = (Border)window.FindName("Frame");
            var under = ((SolidColorBrush)frame.Background).Color;
            Assert.Equal(255, under.A);
            var surface = (under.R / 255.0, under.G / 255.0, under.B / 255.0);
            var texts = Below<TextBlock>(frame).Where(t => t.Foreground is SolidColorBrush).ToList();
            Assert.NotEmpty(texts);
            foreach (var t in texts)
            {
                var c = ((SolidColorBrush)t.Foreground).Color;
                double ratio = ThemeContrast.Ratio(ThemeContrast.Hex((c.R / 255.0, c.G / 255.0, c.B / 255.0)), surface);
                Assert.True(ratio >= ThemeContrast.TextMin, $"{key}: \"{t.Text}\" reads {ratio:0.00} on the frame");
            }
        }
        finally { window.Close(); }
    });

    /// <summary>Polish smoke defect A: the results view drew "Done" in raw Accent (3.95:1 in Obsidian). Accent drawn as text is AccentText.</summary>
    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void The_results_view_draws_Done_in_AccentText_and_it_reads_on_the_frame(string key) => AppHost.Run(() =>
    {
        var window = new ResetWindow(null, ResetFolderListViewModel.For([], unreadable: false));
        new ThemePalette(key, ThemeCatalog.BuiltIns[key]).Apply(window.Resources);
        OffScreen(window);
        window.Show();
        try
        {
            window.ShowResults([ResetStepResult.Done(ResetPart.ClaudeCodeEntries), ResetStepResult.Done(ResetPart.ClaudeCodeEntries)]);
            window.UpdateLayout();
            var frame = (Border)window.FindName("Frame");
            var surface = ((SolidColorBrush)frame.Background).Color;
            var accentText = (SolidColorBrush)window.FindResource("Sanduhr.Brush.AccentText");
            var statuses = Below<TextBlock>((ItemsControl)window.FindName("ResultsList")).Where(t => t.Text == "Done").ToList();
            Assert.NotEmpty(statuses);
            foreach (var t in statuses)
            {
                var c = ((SolidColorBrush)t.Foreground).Color;
                Assert.Equal(accentText.Color, c);
                double ratio = ThemeContrast.Ratio(ThemeContrast.Hex((c.R / 255.0, c.G / 255.0, c.B / 255.0)), (surface.R / 255.0, surface.G / 255.0, surface.B / 255.0));
                Assert.True(ratio >= ThemeContrast.TextMin, $"{key}: \"Done\" reads {ratio:0.00} on the frame");
            }
        }
        finally { window.Close(); }
    });

    private static bool SetsBrush(Trigger t, DependencyProperty property, string key) =>
        t.Setters.OfType<Setter>().Any(s => s.Property == property && s.Value is DynamicResourceExtension d && d.ResourceKey as string == key);

    /// <summary>Polish smoke defect B: Cancel pressed drew no Accent border. Same press look as the dialog's DialogFlatButton.</summary>
    [Fact]
    public void The_flat_button_presses_in_GlassHover_with_an_Accent_border() => AppHost.Run(() =>
    {
        var window = new ResetWindow(null, ResetFolderListViewModel.For([], unreadable: false));
        var style = (Style)window.FindResource("FlatButton");
        var template = (ControlTemplate)style.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
        var pressed = template.Triggers.OfType<Trigger>().Single(t => t.Property == System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty);
        Assert.True(SetsBrush(pressed, Border.BackgroundProperty, "Sanduhr.Brush.GlassHover"));
        Assert.True(SetsBrush(pressed, Border.BorderBrushProperty, "Sanduhr.Brush.Accent"));
    });
}
