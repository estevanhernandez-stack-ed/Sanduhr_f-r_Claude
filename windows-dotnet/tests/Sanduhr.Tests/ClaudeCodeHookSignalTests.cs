using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeCodeHookSignalTests
{
    private static JsonObject Group(string command, string? matcher = null)
    {
        var g = new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["shell"] = "powershell", ["command"] = command }),
        };
        if (matcher is not null) g["matcher"] = matcher;
        return g;
    }

    [Theory]
    [InlineData("prompt")]
    [InlineData("waiting")]
    [InlineData("done")]
    public void The_command_writes_one_file_per_event_naming_the_event_folder_and_session(string word)
    {
        var cmd = ClaudeCodeIntegrations.HookCommand(word);
        Assert.Contains("'.sanduhr'", cmd);
        Assert.Contains("Test-Path -LiteralPath $d", cmd);            // does nothing when Sanduhr's folder is not there
        Assert.Contains($"'event' = '{word}'", cmd);
        Assert.Contains("[string]$i.cwd", cmd);
        Assert.Contains("[string]$i.session_id", cmd);
        Assert.Contains("'" + ClaudeCodeSignalFile.Prefix + "'", cmd);
        Assert.Contains("$PID", cmd);                                 // two sessions in one millisecond still get two names
        Assert.Contains(".tmp", cmd);                                 // written, then renamed: never read half-written
        Assert.Contains("Move-Item", cmd);
    }

    [Fact]
    public void The_command_reads_nothing_of_the_conversation()
    {
        foreach (var word in new[] { "prompt", "waiting", "done" })
        {
            var cmd = ClaudeCodeIntegrations.HookCommand(word);
            foreach (var field in new[] { "prompt", "transcript_path", "message", "tool_input", "tool_response", "last_assistant_message" })
                Assert.DoesNotContain("$i." + field, cmd);
        }
    }

    [Fact]
    public void The_older_command_is_unchanged_byte_for_byte() =>
        Assert.Equal(
            "$d = Join-Path $env:USERPROFILE '.sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d 'glow-done.signal'), '') }",
            ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.DoneSignal));

    [Fact]
    public void Each_generation_of_the_command_is_recognised_as_Sanduhrs()
    {
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("prompt"))));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("done"))));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("waiting"), ClaudeCodeIntegrations.WaitingMatcher)));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.DoneSignal))));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(
            Group(ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.WaitingSignal), ClaudeCodeIntegrations.WaitingMatcher)));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.LegacyHookCommand(ClaudeCodeIntegrations.DoneSignal))));
    }

    [Fact]
    public void A_near_copy_is_the_persons_own()
    {
        Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("done") + " ")));
        Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("done"), "something")));
        Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("waiting"))));   // no matcher
    }

    [Fact]
    public void The_new_command_reads_as_Sanduhrs_command() =>
        Assert.True(ClaudeCodeIntegrations.IsOurHookCommand(new JsonObject
        {
            ["type"] = "command", ["command"] = ClaudeCodeIntegrations.HookCommand("done"),
        }));
}

/// <summary>
/// Runs the real hook command with powershell.exe (5.1, the shell the hook names) against a made-up profile: USERPROFILE
/// points at a temp folder, so nothing of the machine's own Sanduhr folder is touched.
/// </summary>
public sealed class ClaudeCodeHookCommandRunTests : IDisposable
{
    private readonly TempDir _root = new();
    private string Profile => Path.Combine(_root.Path, "profile");
    private string SanduhrDir => Path.Combine(Profile, ".sanduhr");

    public ClaudeCodeHookCommandRunTests() => Directory.CreateDirectory(Profile);

    public void Dispose() => _root.Dispose();

    private const string StopInput =
        "{\"session_id\":\"sess-1\",\"cwd\":\"C:\\\\proj\\\\alpha\",\"hook_event_name\":\"Stop\",\"transcript_path\":\"C:\\\\x\\\\t.jsonl\",\"last_assistant_message\":\"SECRET-REPLY\"}";

    private int RunHook(string word, string stdin)
    {
        var psi = new ProcessStartInfo(StatuslineScriptRunTests.FindPowerShell()!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),   // what Claude Code writes
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", ClaudeCodeIntegrations.HookCommand(word) })
            psi.ArgumentList.Add(a);
        psi.Environment["USERPROFILE"] = Profile;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail("the hook command did not finish in 30 s");
        }
        Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}; stderr: {stderr.Result}; stdout: {stdout.Result}");
        return p.ExitCode;
    }

    [PowerShellFact]
    public void The_real_command_writes_one_decodable_signal_and_nothing_of_the_conversation()
    {
        Directory.CreateDirectory(SanduhrDir);
        Assert.Equal(0, RunHook(ClaudeCodeSignalFile.Done, StopInput));

        var path = Assert.Single(Directory.GetFiles(SanduhrDir, ClaudeCodeSignalFile.Pattern));
        Assert.Empty(Directory.GetFiles(SanduhrDir, "*.tmp"));
        var bytes = File.ReadAllBytes(path);
        var signal = ClaudeCodeSignalFile.Decode(bytes, DateTimeOffset.UtcNow);
        Assert.NotNull(signal);
        Assert.Equal(ClaudeCodeSignalFile.Done, signal!.Event);
        Assert.Equal("sess-1", signal.Session);
        Assert.Equal(@"C:\proj\alpha", signal.Folder);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("SECRET-REPLY", text);
        Assert.DoesNotContain("transcript", text);
    }

    [PowerShellFact]
    public void A_folder_name_that_is_not_ascii_comes_through_unchanged()
    {
        Directory.CreateDirectory(SanduhrDir);
        const string folder = "C:\\Users\\Jos\u00e9\\Gr\u00fc\u00dfe-\u65e5\u672c";
        // The characters themselves, not \u escapes: Claude Code's JSON carries them raw, as UTF-8.
        Assert.Equal(0, RunHook(ClaudeCodeSignalFile.Waiting, "{\"session_id\":\"sess-2\",\"cwd\":\"" + folder.Replace("\\", "\\\\") + "\"}"));

        var path = Assert.Single(Directory.GetFiles(SanduhrDir, ClaudeCodeSignalFile.Pattern));
        var signal = ClaudeCodeSignalFile.Decode(File.ReadAllBytes(path), DateTimeOffset.UtcNow);
        Assert.NotNull(signal);
        Assert.Equal(ClaudeCodeSignalFile.Waiting, signal!.Event);
        Assert.Equal("sess-2", signal.Session);
        Assert.Equal(folder, signal.Folder);
    }

    [PowerShellFact]
    public void Without_sanduhrs_folder_the_real_command_writes_nothing()
    {
        Assert.Equal(0, RunHook(ClaudeCodeSignalFile.Done, StopInput));
        Assert.False(Directory.Exists(SanduhrDir));
        Assert.Empty(Directory.GetFiles(_root.Path, ClaudeCodeSignalFile.Prefix + "*", SearchOption.AllDirectories));
    }

    [PowerShellFact]
    public void With_no_input_the_real_command_still_writes_a_signal_with_no_session_and_no_folder()
    {
        Directory.CreateDirectory(SanduhrDir);
        Assert.Equal(0, RunHook(ClaudeCodeSignalFile.Prompt, ""));

        var path = Assert.Single(Directory.GetFiles(SanduhrDir, ClaudeCodeSignalFile.Pattern));
        Assert.Empty(Directory.GetFiles(SanduhrDir, "*.tmp"));
        var signal = ClaudeCodeSignalFile.Decode(File.ReadAllBytes(path), DateTimeOffset.UtcNow);
        Assert.NotNull(signal);
        Assert.Equal(ClaudeCodeSignalFile.Prompt, signal!.Event);
        Assert.Equal(ClaudeCodeSignalFile.NoSession, signal.Session);
        Assert.Null(signal.Folder);
    }
}
