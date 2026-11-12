using System.IO;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Pins AppHost.KeepOffTheRealProfile: a bare <c>new Paths()</c> in this assembly never reaches the real profile.</summary>
public class NoRealProfileTests
{
    [Fact]
    public void A_bare_Paths_points_at_a_scratch_profile_not_the_real_one()
    {
        var real = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var p = new Paths();
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SANDUHR_DEV_PROFILE")));
        Assert.False(p.ExternalDirPath.Equals(Path.Combine(real, ".sanduhr"), StringComparison.OrdinalIgnoreCase));
        Assert.False(p.StatuslineScriptFile.StartsWith(Path.Combine(real, ".sanduhr"), StringComparison.OrdinalIgnoreCase));
    }
}
