using System.Diagnostics;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// 5.0 spec 7: scripts/check-release-assets.ps1 fails a draft release that is missing a file. The classic miss is
/// a feed: an architecture whose feed was not uploaded never updates. Release tags come as v5.0.0 and v5.0.0.0.
/// </summary>
public class ReleaseAssetCheckTests : IDisposable
{
    private readonly TempDir _root = new();

    public ReleaseAssetCheckTests()
    {
        foreach (var name in new[]
                 {
                     "Sanduhr-Store-x64-v5.0.0.0.msix", "Sanduhr-Store-arm64-v5.0.0.0.msix",
                     "626Labs.Sanduhr-win-Setup.exe", "626Labs.Sanduhr-win-arm64-Setup.exe",
                     "626Labs.Sanduhr-5.0.0-full.nupkg", "626Labs.Sanduhr-5.0.0-win-arm64-full.nupkg",
                     "626Labs.Sanduhr-win-Portable.zip", "626Labs.Sanduhr-win-arm64-Portable.zip",
                 })
            File.WriteAllText(Path.Combine(_root.Path, name), "x");
        Feed("releases.win.json", "626Labs.Sanduhr-5.0.0-full.nupkg");
        Feed("releases.win-arm64.json", "626Labs.Sanduhr-5.0.0-win-arm64-full.nupkg");
    }

    public void Dispose() => _root.Dispose();

    private void Feed(string file, string package)
        => File.WriteAllText(Path.Combine(_root.Path, file),
            "{\"Assets\":[{\"PackageId\":\"626Labs.Sanduhr\",\"Version\":\"5.0.0\",\"Type\":\"Full\",\"FileName\":\"" + package + "\"}]}");

    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "scripts", "check-release-assets.ps1");
    }

    private (int Exit, string Output) Run(string tag)
    {
        var start = new ProcessStartInfo(StatuslineScriptRunTests.FindPowerShell()!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Script(), "-Dir", _root.Path, "-Tag", tag })
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [PowerShellTheory]
    [InlineData("v5.0.0")]
    [InlineData("v5.0.0.0")]
    public void A_complete_release_passes_under_either_tag_shape(string tag)
    {
        var (exit, output) = Run(tag);
        Assert.True(exit == 0, output);
    }

    [PowerShellFact]
    public void A_missing_feed_fails_and_is_named()
    {
        File.Delete(Path.Combine(_root.Path, "releases.win-arm64.json"));
        var (exit, output) = Run("v5.0.0.0");
        Assert.Equal(1, exit);
        Assert.Contains("releases.win-arm64.json", output);
    }

    [PowerShellFact]
    public void A_feed_that_lists_another_package_fails()
    {
        Feed("releases.win-arm64.json", "626Labs.Sanduhr-4.9.0-win-arm64-full.nupkg");
        var (exit, output) = Run("v5.0.0.0");
        Assert.Equal(1, exit);
        Assert.Contains("626Labs.Sanduhr-5.0.0-win-arm64-full.nupkg", output);
    }

    [PowerShellFact]
    public void The_old_x64_package_name_does_not_count()
    {
        File.Move(Path.Combine(_root.Path, "Sanduhr-Store-x64-v5.0.0.0.msix"), Path.Combine(_root.Path, "Sanduhr-Store-v5.0.0.0.msix"));
        var (exit, output) = Run("v5.0.0.0");
        Assert.Equal(1, exit);
        Assert.Contains("Sanduhr-Store-x64-v5.0.0.0.msix", output);
    }

    [PowerShellFact]
    public void A_tag_that_is_not_a_version_is_refused()
    {
        var (exit, _) = Run("latest");
        Assert.NotEqual(0, exit);
    }
}
