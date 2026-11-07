using Sanduhr.App.Desk;
using Sanduhr.App.Interop;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>
/// Which WM_SETTINGCHANGE broadcasts rebuild the Desk. Windows' animation effects decide at build time whether the
/// companion roams (DeskView reads <c>Roaming</c> when it lays out), so turning them on used to leave the companion
/// standing still, its feet moving, until Sanduhr restarted.
/// </summary>
public class DeskSettingChangeRebuildTests
{
    [Theory]
    [InlineData(DeskNative.SPI_SETCLIENTAREAANIMATION)]
    [InlineData(DeskNative.SPI_SETUIEFFECTS)]
    [InlineData(DeskNative.SPI_SETANIMATION)]
    public void Turning_animation_effects_on_or_off_rebuilds_the_Desk(int setting) =>
        Assert.Equal("animation effects changed", DesktopLayerHost.SettingChangeRebuild(setting));

    [Fact]
    public void The_work_area_and_the_wallpaper_still_rebuild_it()
    {
        Assert.Equal("work area changed", DesktopLayerHost.SettingChangeRebuild(DeskNative.SPI_SETWORKAREA));
        Assert.Equal("wallpaper changed", DesktopLayerHost.SettingChangeRebuild(DeskNative.SPI_SETDESKWALLPAPER));
    }

    [Theory]
    [InlineData(0)]          // a policy or locale change: wParam is 0
    [InlineData(0x0071)]     // SPI_SETMOUSESPEED
    [InlineData(0x2001)]     // SPI_SETFOREGROUNDLOCKTIMEOUT
    public void Other_settings_do_not(int setting) =>
        Assert.Null(DesktopLayerHost.SettingChangeRebuild(setting));
}
