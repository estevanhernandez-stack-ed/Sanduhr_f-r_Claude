using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 8 (WF49; spec, Themes; decision 12): the gallery and the installed list grow to fit what they hold, the
/// page scrolls as one, and the wheel over a list scrolls the page.</summary>
[Collection("SettingsWindow")]
public sealed class ThemesListsTests : AppWindowFixture
{
    private const int UserThemes = 30;

    private (SettingsWindow Window, WidgetViewModel Widget) Open(double height)
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        SettingsWindow? window = null;
        try
        {
            Directory.CreateDirectory(widget.ThemesDir);
            var o = ThemeCatalog.BuiltIns["obsidian"];
            for (int i = 0; i < UserThemes; i++)
            {
                var json = new JsonObject
                {
                    ["name"] = $"Theme {i:00}", ["bg"] = o.Bg, ["glass"] = o.Glass, ["glass_on_mica"] = o.GlassOnMica, ["title_bg"] = o.TitleBg,
                    ["border"] = o.Border, ["text"] = o.Text, ["text_secondary"] = o.TextSecondary, ["text_dim"] = o.TextDim,
                    ["text_muted"] = o.TextMuted, ["accent"] = o.Accent, ["bar_bg"] = o.BarBg, ["footer_bg"] = o.FooterBg,
                    ["pace_marker"] = o.PaceMarker, ["sparkline"] = o.Sparkline,
                };
                File.WriteAllText(Path.Combine(widget.ThemesDir, $"theme-{i:00}.json"), json.ToJsonString());
            }
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
                checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
            svm.Themes.ReloadCommand.Execute(null);
            window = new SettingsWindow(svm);
            var guard = NoActivate.Show(window);
            window.Width = 800;
            window.Height = height;   // after Show: FitToWorkArea sizes the window on open
            window.SelectPage(SettingsPage.Themes);
            window.UpdateLayout();
            Pump();
            guard.Check();
            return (window, widget);
        }
        catch
        {
            window?.Close();
            widget.Dispose();
            throw;
        }
    }

    private static ScrollViewer? ScrollerAbove(DependencyObject element)
    {
        for (var p = VisualTreeHelper.GetParent(element); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer s) return s;
        return null;
    }

    private static int Count<T>(DependencyObject root) where T : DependencyObject
    {
        int n = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T) n++;
            n += Count<T>(c);
        }
        return n;
    }

    /// <summary>Every item container lies inside the list's own bounds: nothing is clipped by a cap.</summary>
    private static void NothingClipped(ListBox list, string what)
    {
        Assert.True(list.Items.Count > 0, $"{what} is empty, so the check below is vacuous");
        for (int i = 0; i < list.Items.Count; i++)
        {
            var c = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(i);
            Assert.NotNull(c);
            var box = c.TransformToAncestor(list).TransformBounds(new Rect(c.RenderSize));
            Assert.True(box.Bottom <= list.ActualHeight + 0.5, $"{what} item {i} ends at {box.Bottom:0} of {list.ActualHeight:0}");
        }
    }

    [Fact]
    public void Every_theme_shows_and_the_page_scrolls() => AppHost.Run(() =>
    {
        var (window, widget) = Open(height: 600);
        try
        {
            var page = (ScrollViewer)window.FindName("ThemesPageScroller");
            var gallery = (ListBox)window.FindName("ThemeGallery");
            var installed = (ListBox)window.FindName("ThemesList");
            Assert.Same(page, ScrollerAbove(gallery));
            Assert.Same(page, ScrollerAbove(installed));
            Assert.Equal(0, Count<ScrollViewer>(installed));
            Assert.Equal(0, Count<ScrollViewer>(gallery));
            Assert.Equal(UserThemes, installed.Items.Count);
            Assert.True(gallery.Items.Count >= 6 + UserThemes, $"gallery holds {gallery.Items.Count}");
            NothingClipped(gallery, "the gallery");
            NothingClipped(installed, "the installed list");
            Assert.True(page.ExtentHeight > page.ViewportHeight, "at 600 DIP the page should scroll rather than squeeze");
            Assert.False(page.Focusable);
        }
        finally
        {
            window.Close();
            widget.Dispose();
        }
    });

    [Fact]
    public void The_page_scroller_wears_the_slim_scrollbar() => AppHost.Run(() =>
    {
        var (window, widget) = Open(height: 600);
        try
        {
            var page = (ScrollViewer)window.FindName("ThemesPageScroller");
            var slim = (Style)window.FindResource("SlimThemeScrollBar");
            var style = page.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] as Style;
            Assert.NotNull(style);
            Assert.Same(slim, style!.BasedOn);
        }
        finally
        {
            window.Close();
            widget.Dispose();
        }
    });

    [Fact]
    public void Wheel_over_the_installed_list_scrolls_the_page() => AppHost.Run(() =>
    {
        var (window, widget) = Open(height: 600);
        try
        {
            var page = (ScrollViewer)window.FindName("ThemesPageScroller");
            var installed = (ListBox)window.FindName("ThemesList");
            double before = page.VerticalOffset;
            installed.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            window.UpdateLayout();
            Assert.True(page.VerticalOffset > before, $"the page stayed at {page.VerticalOffset:0}");
        }
        finally
        {
            window.Close();
            widget.Dispose();
        }
    });

    [Theory]
    [InlineData("gallery")]
    [InlineData("studio")]
    [InlineData("installed")]
    public void A_search_landing_puts_the_section_at_the_top_of_the_page(string anchor) => AppHost.Run(() =>
    {
        var (window, widget) = Open(height: 600);
        try
        {
            window.SelectPage(SettingsPage.Themes, anchor);
            window.UpdateLayout();
            Pump();
            var page = (ScrollViewer)window.FindName("ThemesPageScroller");
            var target = Assert.IsAssignableFrom<FrameworkElement>(SettingsAnchor.Find(page, anchor));
            var top = target.TransformToAncestor(page).Transform(new Point(0, 0)).Y;
            bool atTop = Math.Abs(top - SettingsWindow.LandingTopMargin) <= 2;
            bool atEnd = page.VerticalOffset >= page.ScrollableHeight - 1;
            bool atStart = page.VerticalOffset <= 0 && top <= SettingsWindow.LandingTopMargin + 2;
            Assert.True(atTop || atEnd || atStart, $"{anchor}: top {top:0} (offset {page.VerticalOffset:0} of {page.ScrollableHeight:0})");
        }
        finally
        {
            window.Close();
            widget.Dispose();
        }
    });

    [Fact]
    public void The_Themes_block_has_no_list_caps()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "SettingsWindow.xaml"));
        int start = xaml.IndexOf("<TabItem Header=\"Themes\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("</TabItem>", start, StringComparison.Ordinal);
        var themes = xaml[start..end];
        Assert.DoesNotContain("MaxHeight=\"112\"", themes);
        Assert.DoesNotContain("Height=\"72\"", themes.Replace("MinHeight=\"72\"", ""));
    }
}
