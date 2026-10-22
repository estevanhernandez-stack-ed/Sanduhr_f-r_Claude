using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The PNG a person picks for a button: the size cap, the signature check, the 256 px fit and the hashed name.</summary>
public class LauncherImagePolicyTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [Fact]
    public void ARealPngUnderOneMegabyteMayBeTried()
    {
        Assert.Null(LauncherImagePolicy.RefuseFile(10_000, Png));
        Assert.Null(LauncherImagePolicy.RefuseFile(LauncherImagePolicy.MaxBytes, Png));
    }

    [Fact]
    public void OverOneMegabyteEmptyOrNotAPngIsRefused()
    {
        Assert.NotNull(LauncherImagePolicy.RefuseFile(LauncherImagePolicy.MaxBytes + 1, Png));
        Assert.NotNull(LauncherImagePolicy.RefuseFile(0, Png));
        Assert.NotNull(LauncherImagePolicy.RefuseFile(100, "GIF89a......."u8));
        Assert.NotNull(LauncherImagePolicy.RefuseFile(100, new byte[] { 0x89, 0x50 }));
        Assert.NotNull(LauncherImagePolicy.RefuseFile(100, "MZ.........."u8));
    }

    [Fact]
    public void ADecodedBitmapThatIsHugeOrEmptyIsRefused()
    {
        Assert.Null(LauncherImagePolicy.RefuseSize(256, 256));
        Assert.Null(LauncherImagePolicy.RefuseSize(4096, 4096));
        Assert.NotNull(LauncherImagePolicy.RefuseSize(4097, 10));
        Assert.NotNull(LauncherImagePolicy.RefuseSize(10, 20000));
        Assert.NotNull(LauncherImagePolicy.RefuseSize(0, 10));
        Assert.NotNull(LauncherImagePolicy.RefuseSize(10, -1));
    }

    [Theory]
    [InlineData(256, 256, 0, 0, 256, 256)]
    [InlineData(512, 512, 0, 0, 256, 256)]
    [InlineData(100, 100, 0, 0, 256, 256)]     // a small icon is scaled up to fill its square
    [InlineData(1024, 512, 0, 64, 256, 128)]
    [InlineData(512, 1024, 64, 0, 128, 256)]
    [InlineData(1, 1, 0, 0, 256, 256)]
    public void FitScalesTheLongestSideToTheSquareAndCenters(int w, int h, int x, int y, int fw, int fh) =>
        Assert.Equal((x, y, fw, fh), LauncherImagePolicy.Fit(w, h));

    [Fact]
    public void FitAlwaysStaysInsideTheSquare()
    {
        foreach (var (w, h) in new[] { (3, 4000), (4000, 3), (1, 4096), (255, 257), (333, 77) })
        {
            var (x, y, fw, fh) = LauncherImagePolicy.Fit(w, h);
            Assert.True(x >= 0 && y >= 0 && fw >= 1 && fh >= 1);
            Assert.True(x + fw <= LauncherImagePolicy.Side && y + fh <= LauncherImagePolicy.Side);
        }
        Assert.Equal((0, 0, 0, 0), LauncherImagePolicy.Fit(0, 5));
    }

    [Fact]
    public void TheStoredNameIsTheContentHashAndPassesTheNameRule()
    {
        var a = LauncherImagePolicy.FileName([1, 2, 3]);
        var b = LauncherImagePolicy.FileName([1, 2, 3]);
        var c = LauncherImagePolicy.FileName([1, 2, 4]);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(LauncherButton.IsImageName(a));
        Assert.Equal(64 + 4, a.Length);
    }
}
