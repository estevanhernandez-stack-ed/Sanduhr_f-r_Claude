using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Desk;
using Sanduhr.App.Desk.UnderIcons;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Which Desk islands go under the desktop icons (spec 5.0d): every widget, never a drawer or the Launcher, and the flag never
/// changes what an island is otherwise.</summary>
public sealed class UnderIconsHostTests
{
    private static DeskIsland Island(params string[] widgets) =>
        new(new Border(), new Point(0, 0), HorizontalAlignment.Left, VerticalAlignment.Top) { Widgets = widgets };

    [Fact]
    public void WidgetsGoUnderTheIcons() => Sta.Run(() =>
    {
        Assert.True(UnderIconsHost.Eligible(Island("clock", "claude")));
        Assert.True(UnderIconsHost.Eligible(Island("nowPlaying")));
        Assert.True(UnderIconsHost.Eligible(Island("weather") with { TakesClicks = true }));
        Assert.True(UnderIconsHost.Eligible(Island(CompanionSettings.Widget)));
    });

    [Fact]
    public void DrawersAndTheLauncherStayAbove() => Sta.Run(() =>
    {
        Assert.False(UnderIconsHost.Eligible(Island("launcher")));
        Assert.False(UnderIconsHost.Eligible(Island("clock", "launcher")));
        Assert.False(UnderIconsHost.Eligible(Island("drawer") with { Drawer = true, TakesClicks = true, TakesFocus = true }));
    });

    [Fact]
    public void TheFlagIsOffUnlessTheHostSetsIt() => Sta.Run(() =>
    {
        Assert.False(Island("clock").UnderIcons);
    });
}
