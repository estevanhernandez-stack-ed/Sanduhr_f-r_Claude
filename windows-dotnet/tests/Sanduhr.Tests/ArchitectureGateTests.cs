using System.Diagnostics;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// 5.0 spec 7: scripts/check-architecture.ps1 stops a build whose output holds a binary for the wrong processor.
/// The files here are synthetic: a DOS header, a PE signature and the CPU field, nothing else.
/// </summary>
public class ArchitectureGateTests : IDisposable
{
    private const ushort X64 = 0x8664;
    private const ushort Arm64 = 0xAA64;
    private const ushort Neutral = 0x014C;   // what managed, architecture-neutral files carry

    private readonly TempDir _root = new();

    public void Dispose() => _root.Dispose();

    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "scripts", "check-architecture.ps1");
    }

    private void Pe(string relative, ushort machine)
    {
        var path = Path.Combine(_root.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
        File.WriteAllBytes(path, bytes);
    }

    private void GoodArm64()
    {
        Pe("Sanduhr.exe", Arm64);
        Pe(Path.Combine("mcp", "sanduhr-mcp.exe"), Arm64);
        Pe("coreclr.dll", Arm64);
        Pe("Sanduhr.Core.dll", Neutral);
    }

    private (int Exit, string Output) Run(string runtime)
    {
        var start = new ProcessStartInfo(StatuslineScriptRunTests.FindPowerShell()!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Script(), "-Dir", _root.Path, "-Runtime", runtime })
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [PowerShellFact]
    public void A_folder_of_the_right_architecture_passes()
    {
        GoodArm64();
        var (exit, output) = Run("win-arm64");
        Assert.True(exit == 0, output);
    }

    [PowerShellFact]
    public void A_file_for_the_other_64_bit_architecture_fails_and_is_named()
    {
        GoodArm64();
        Pe(Path.Combine("runtimes", "stray.dll"), X64);
        var (exit, output) = Run("win-arm64");
        Assert.Equal(1, exit);
        Assert.Contains("stray.dll", output);
    }

    [PowerShellFact]
    public void The_app_itself_must_be_the_target_architecture()
    {
        GoodArm64();
        Pe("Sanduhr.exe", Neutral);
        var (exit, output) = Run("win-arm64");
        Assert.Equal(1, exit);
        Assert.Contains("Sanduhr.exe", output);
    }

    [PowerShellFact]
    public void A_missing_server_fails()
    {
        GoodArm64();
        File.Delete(Path.Combine(_root.Path, "mcp", "sanduhr-mcp.exe"));
        var (exit, output) = Run("win-arm64");
        Assert.Equal(1, exit);
        Assert.Contains("sanduhr-mcp.exe", output);
    }

    [PowerShellFact]
    public void A_dll_that_is_not_a_program_file_is_ignored()
    {
        GoodArm64();
        File.WriteAllText(Path.Combine(_root.Path, "notes.dll"), "not a binary");
        var (exit, output) = Run("win-arm64");
        Assert.True(exit == 0, output);
    }

    [PowerShellFact]
    public void An_x64_folder_with_an_arm64_stray_fails()
    {
        Pe("Sanduhr.exe", X64);
        Pe(Path.Combine("mcp", "sanduhr-mcp.exe"), X64);
        Pe("stray.dll", Arm64);
        var (exit, output) = Run("win-x64");
        Assert.Equal(1, exit);
        Assert.Contains("stray.dll", output);
    }

    [PowerShellFact]
    public void An_unknown_runtime_is_refused()
    {
        GoodArm64();
        var (exit, _) = Run("win-x86");
        Assert.NotEqual(0, exit);
    }
}
