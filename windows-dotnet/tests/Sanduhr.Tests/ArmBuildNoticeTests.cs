using System.Runtime.InteropServices;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// 5.0 spec 7: an x64 GitHub install running emulated on an Arm PC is told once that a native build exists.
/// The decision is pure, so every case that must stay silent is pinned here.
/// </summary>
public class ArmBuildNoticeTests
{
    [Theory]
    [InlineData(Architecture.X64, Architecture.Arm64, false, true)]     // the one case: x64 build, Arm PC, not the Store
    [InlineData(Architecture.X64, Architecture.Arm64, true, false)]     // the Store hands an Arm PC the arm64 package itself
    [InlineData(Architecture.X64, Architecture.X64, false, false)]      // an ordinary x64 PC
    [InlineData(Architecture.Arm64, Architecture.Arm64, false, false)]  // already the native build
    [InlineData(Architecture.Arm64, Architecture.Arm64, true, false)]
    [InlineData(Architecture.X86, Architecture.Arm64, false, false)]    // not a build Sanduhr ships
    [InlineData(Architecture.X64, Architecture.X86, false, false)]
    public void It_applies_only_to_an_x64_build_on_an_Arm_pc_outside_the_Store(
        Architecture process, Architecture machine, bool store, bool expected)
        => Assert.Equal(expected, ArmBuildNotice.Applies(process, machine, store));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pc_whose_architecture_could_not_be_read_gets_no_notice(bool store)
        => Assert.False(ArmBuildNotice.Applies(Architecture.X64, null, store));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]    // shown once already
    [InlineData(false, false, false)]  // does not apply
    [InlineData(false, true, false)]
    public void The_notification_is_due_once(bool applies, bool alreadyShown, bool expected)
        => Assert.Equal(expected, ArmBuildNotice.ToastDue(applies, alreadyShown));

    [Fact]
    public void The_link_lists_the_running_major_s_releases()
        => Assert.Equal(
            "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases?q=v5&expanded=true",
            ArmBuildNotice.ReleasesUrl(5));

    [Theory]
    [InlineData(0)]    // a test build
    [InlineData(-1)]
    public void A_build_with_no_major_version_still_gets_a_working_link(int major)
        => Assert.Equal("https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases", ArmBuildNotice.ReleasesUrl(major));

    [Fact]
    public void The_copy_is_the_spec_s()
    {
        Assert.Equal("A native Arm build is available", ArmBuildNotice.Title);
        Assert.Equal("This PC has an Arm processor and this copy of Sanduhr is the x64 build. The Arm build runs without emulation.", ArmBuildNotice.Body);
        Assert.Equal("Get the Arm build", ArmBuildNotice.GetText);
        Assert.Equal("Not now", ArmBuildNotice.LaterText);
        Assert.Equal("This PC has an Arm processor and this copy of Sanduhr is the x64 build, which Windows runs through emulation.", ArmBuildNotice.AboutLine);
        Assert.Equal("arm_build_notice_shown", ArmBuildNotice.SettingsKey);
        Assert.Equal("arm build notice: due", ArmBuildNotice.LogDue);
    }
}
