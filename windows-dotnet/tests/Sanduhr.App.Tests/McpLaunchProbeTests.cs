using System.Diagnostics;
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// #144: after an install the launcher is started the way Claude Code starts it and must answer initialize as Sanduhr.
/// A registration that points at a command that can't start used to fail silently (Claude Code shows it as no server at all).
/// These start real processes: small .cmd launchers that answer, mis-answer, point nowhere or hang, and the real server.
/// </summary>
public sealed class McpLaunchProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-probe-" + Guid.NewGuid().ToString("N")[..8]);

    public McpLaunchProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Launcher(string body)
    {
        string path = Path.Combine(_root, "bin", "sanduhr-mcp.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return path;
    }

    private static McpProbeResult Check(string launcher, double seconds = 15) =>
        McpLaunchProbe.CheckAsync(launcher, TimeSpan.FromSeconds(seconds)).GetAwaiter().GetResult();

    [Fact]
    public void A_launcher_that_answers_as_Sanduhr_is_ok_with_its_version()
    {
        var result = Check(Launcher("""echo {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","serverInfo":{"name":"sanduhr","version":"9.9.9"}}}"""));

        Assert.True(result.Ok, result.Reason);
        Assert.Equal("9.9.9", result.Version);
    }

    [Fact]
    public void A_notification_before_the_answer_is_read_past()
    {
        var result = Check(Launcher(
            "echo {\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}\r\n" +
            "echo {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{\"name\":\"sanduhr\",\"version\":\"1.0\"}}}"));

        Assert.True(result.Ok, result.Reason);
    }

    [Fact]
    public void A_different_server_answering_is_not_ok()
    {
        var result = Check(Launcher("""echo {"jsonrpc":"2.0","id":1,"result":{"serverInfo":{"name":"someone-else","version":"1"}}}"""));

        Assert.False(result.Ok);
        Assert.Equal(SettingsNames.McpProbeWrongAnswer, result.Reason);
    }

    [Fact]
    public void A_launcher_pointing_at_a_server_that_is_gone_says_so()
    {
        // What the 3.4.2 machine in #144 would have had once the launcher existed: a target that isn't there.
        var result = Check(Launcher($"\"{Path.Combine(_root, "mcp", "4.0.0", "sanduhr-mcp.exe")}\" %*"));

        Assert.False(result.Ok);
        Assert.Equal(SettingsNames.McpProbeTargetMissing, result.Reason);
        Assert.DoesNotContain(_root, result.Reason!);   // no path in the status line
    }

    [Fact]
    public void A_missing_launcher_says_so_without_starting_anything()
    {
        var result = Check(Path.Combine(_root, "bin", "nope.cmd"));

        Assert.False(result.Ok);
        Assert.Equal(SettingsNames.McpProbeNoLauncher, result.Reason);
    }

    [Fact]
    public void A_server_that_never_answers_times_out_and_is_stopped()
    {
        string marker = Path.Combine(_root, "still-running.txt");
        // Waits about 30 seconds without reading stdin or writing stdout, then leaves a marker: if the probe did not stop it, the marker appears.
        var launcher = Launcher($"ping -n 30 127.0.0.1 > nul\r\necho late > \"{marker}\"");
        var clock = Stopwatch.StartNew();

        var result = Check(launcher, seconds: 1);

        Assert.False(result.Ok);
        Assert.Equal(SettingsNames.McpProbeNoAnswer(TimeSpan.FromSeconds(1)), result.Reason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Thread.Sleep(500);
        Assert.False(File.Exists(marker));
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"x\"}", null)]           // a notification: not the answer
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}", null)]       // another id: not the answer
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-1}}", false)]
    [InlineData("not json at all", false)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"serverInfo\":{\"name\":\"sanduhr\"}}}", true)]
    public void One_line_is_judged_as_the_answer_or_skipped(string line, bool? ok)
    {
        var verdict = McpLaunchProbe.Answer(line);

        if (ok is null) Assert.Null(verdict);
        else Assert.Equal(ok, verdict!.Ok);
    }

    /// <summary>The built server, through a launcher the real installer wrote: the path an install takes, end to end.</summary>
    [SkippableFact]
    public void The_real_server_through_the_real_launcher_answers()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Sanduhr.Mcp")))
            dir = Path.GetDirectoryName(dir);
        string? exe = dir is null ? null : Directory.EnumerateFiles(Path.Combine(dir, "src", "Sanduhr.Mcp", "bin"), "sanduhr-mcp.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        Skip.If(exe is null, "sanduhr-mcp.exe is not built in this tree");

        var installer = new McpIntegrationInstaller(Path.Combine(_root, "home"), Path.Combine(_root, "appdata"), externalDir: Path.Combine(_root, "ext"));
        Assert.True(installer.WriteLauncher(Path.GetDirectoryName(exe)!));

        var result = Check(installer.LauncherPath, seconds: 30);

        Assert.True(result.Ok, result.Reason);
        Assert.False(string.IsNullOrEmpty(result.Version));
    }
}
