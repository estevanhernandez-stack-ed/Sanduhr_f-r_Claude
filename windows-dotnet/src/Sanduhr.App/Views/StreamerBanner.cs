using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// "Streamer mode is on: names are codenames. Privacy settings…" (spec, Streamer mode, While on), under the first line of every page that
/// masks something (<see cref="SettingsIndex.StreamerPages"/>). It shows while the window's <c>Privacy.Streamer.On</c> is true, through a
/// binding to the window's DataContext, so it subscribes to nothing; its line is a live region on the same flag. Built in code, like <see cref="SectionJumpLinks"/>, so the own-file pages
/// need no converter of their own.
/// </summary>
public sealed class StreamerBanner : Border
{
    public StreamerBanner()
    {
        Margin = new Thickness(0, 0, 0, 12);
        Padding = new Thickness(10, 6, 10, 6);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Sanduhr.Brush.Bg");
        SetResourceReference(BorderBrushProperty, "Sanduhr.Brush.Accent");

        var row = new WrapPanel();
        var text = new TextBlock
        {
            Text = SettingsNames.StreamerBanner,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
        var link = new Button { Content = SettingsNames.LinkTitle(SettingsPage.Privacy) };
        link.SetResourceReference(StyleProperty, "SettingsLinkButton");
        SettingsLinkTo.SetLink(link, SettingsIndex.PageId(SettingsPage.Privacy) + "#" + SettingsIndex.Streamer);
        AutomationProperties.SetName(link, SettingsNames.StreamerBannerLinkName);
        // The banner appears and disappears while the window is open, so the line is a live region on the same flag (WF: a live region
        // must raise LiveRegionChanged; LiveRegion.Shown does).
        text.SetBinding(LiveRegion.ShownProperty, OnFlag());
        row.Children.Add(text);
        row.Children.Add(link);
        Child = row;

        Visibility = Visibility.Collapsed;
        var visible = OnFlag();
        visible.Converter = new BooleanToVisibilityConverter();
        visible.FallbackValue = Visibility.Collapsed;
        SetBinding(VisibilityProperty, visible);
    }

    private static Binding OnFlag() => new("DataContext.Privacy.Streamer.On")
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
        FallbackValue = false,
        Mode = BindingMode.OneWay,
    };
}
