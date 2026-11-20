using System.Diagnostics;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// 5.0 spec 7: both architectures pack into one dist\release folder. Building one must leave the other's files,
/// or a release goes out with a feed missing and that architecture's installs never update.
/// </summary>
public class VelopackCleanTests : IDisposable
{
    private static readonly string[] X64Files =
    [
        "626Labs.Sanduhr-5.0.0-full.nupkg", "626Labs.Sanduhr-5.0.0-delta.nupkg", "626Labs.Sanduhr-win-Setup.exe",
        "626Labs.Sanduhr-win-Portable.zip", "releases.win.json", "assets.win.json", "RELEASES",
    ];

    private static readonly string[] Arm64Files =
    [
        "626Labs.Sanduhr-5.0.0-win-arm64-full.nupkg", "626Labs.Sanduhr-win-arm64-Setup.exe",
        "626Labs.Sanduhr-win-arm64-Portable.zip", "releases.win-arm64.json", "assets.win-arm64.json", "RELEASES-win-arm64",
    ];

    private readonly TempDir _root = new();

    public VelopackCleanTests()
    {
        foreach (var name in X64Files.Concat(Arm64Files))
            File.WriteAllText(Path.Combine(_root.Path, name), "x");
    }

    public void Dispose() => _root.Dispose();

    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "scripts", "build-velopack-release.ps1");
    }

    private (int Exit, string Output) Clean(string runtime, string? dir = null)
    {
        var start = new ProcessStartInfo(StatuslineScriptRunTests.FindPowerShell()!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Script(),
                     "-Version", "5.0.0", "-Runtime", runtime, "-ReleaseDir", dir ?? _root.Path, "-CleanOnly",
                 })
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    private string[] Left() => Directory.GetFiles(_root.Path).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToArray();

    [PowerShellFact]
    public void Cleaning_for_arm64_leaves_every_x64_file()
    {
        var (exit, output) = Clean("win-arm64");
        Assert.True(exit == 0, output);
        Assert.Equal(X64Files.Order(StringComparer.Ordinal).ToArray(), Left());
    }

    [PowerShellFact]
    public void Cleaning_for_x64_leaves_every_arm64_file()
    {
        var (exit, output) = Clean("win-x64");
        Assert.True(exit == 0, output);
        Assert.Equal(Arm64Files.Order(StringComparer.Ordinal).ToArray(), Left());
    }

    [PowerShellFact]
    public void A_folder_that_does_not_exist_is_not_an_error()
    {
        var (exit, output) = Clean("win-arm64", Path.Combine(_root.Path, "not-there"));
        Assert.True(exit == 0, output);
        Assert.Equal(X64Files.Length + Arm64Files.Length, Left().Length);   // and nothing else was touched
    }
}
