using System.Diagnostics;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>A fact that is skipped, with a message, where Windows PowerShell isn't there to run the script.</summary>
public sealed class PowerShellFactAttribute : FactAttribute
{
    public PowerShellFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The statusline script runs under Windows PowerShell 5.1; this is not Windows.";
        else if (StatuslineScriptRunTests.FindPowerShell() is null) Skip = "powershell.exe was not found; the statusline script run tests need it.";
    }
}

/// <summary>A fact that is skipped, with a message, where Git for Windows' bash.exe cannot be found.</summary>
public sealed class GitBashFactAttribute : FactAttribute
{
    public GitBashFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Git Bash is only looked for on Windows.";
        else if (StatuslineScriptRunTests.FindPowerShell() is null) Skip = "powershell.exe was not found; the statusline script run tests need it.";
        else if (StatuslineScriptRunTests.FindGitBash() is null) Skip = "Git Bash was not found (no bin/bash.exe in git.exe's install root, where Claude Code and the script look); this test needs Git for Windows.";
    }
}

/// <summary>
/// Runs the embedded statusline script for real with powershell.exe (5.1, the shell Claude Code's
/// command names) in a sandbox: APPDATA and USERPROFILE point at temp folders, so nothing of the
/// machine's own Sanduhr data is read.
/// </summary>
public class StatuslineScriptRunTests : IDisposable
{
    private readonly TempDir _root = new();
    private readonly string _appData;
    private readonly string _profile;
    private readonly string _sanduhr;
    private readonly string _script;

    public StatuslineScriptRunTests()
    {
        _appData = Path.Combine(_root.Path, "appdata");
        _profile = Path.Combine(_root.Path, "profile");
        _sanduhr = Path.Combine(_appData, "Sanduhr");
        Directory.CreateDirectory(_sanduhr);
        Directory.CreateDirectory(_profile);
        var installer = new StatuslineInstaller(_profile, Path.Combine(_root.Path, "bin"));
        Assert.True(installer.InstallScript());
        _script = installer.ScriptPath;
    }

    public void Dispose() => _root.Dispose();

    internal static string? FindPowerShell()
    {
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(system)) return system;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "powershell.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>bin\bash.exe in the git install root found from git.exe on PATH, as the script looks for it; never System32 (WSL).</summary>
    internal static string? FindGitBash()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var git = Path.Combine(dir.Trim('"'), "git.exe");
                if (!File.Exists(git)) continue;
                var up = Path.GetDirectoryName(git);
                for (int i = 0; i < 3 && up is not null; i++, up = Path.GetDirectoryName(up))
                {
                    var candidate = Path.Combine(up, "bin", "bash.exe");
                    if (File.Exists(candidate) && !candidate.Contains(@"\System32\", StringComparison.OrdinalIgnoreCase)) return candidate;
                }
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    // -- fixtures -------------------------------------------------------------

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static string Snapshot(int fiveHour, int sevenDay, string status = "ok", double capturedSecondsAgo = 10)
        => $$"""
        {"schema_version":1,"captured_at":"{{Iso(DateTimeOffset.UtcNow.AddSeconds(-capturedSecondsAgo))}}","status":"{{status}}",
         "tiers":[{"key":"five_hour","utilization":{{fiveHour}},"resets_at":"{{Iso(DateTimeOffset.UtcNow.AddHours(2))}}"},
                  {"key":"seven_day","utilization":{{sevenDay}},"resets_at":"{{Iso(DateTimeOffset.UtcNow.AddDays(3))}}"}]}
        """;

    private void WriteSanduhr(string name, string content) => File.WriteAllText(Path.Combine(_sanduhr, name), content);

    private sealed record Run(string Stdout, string Stderr, int ExitCode, TimeSpan Elapsed)
    {
        public string Text => Stdout.Replace("\r\n", "\n");
    }

    private Run RunScript(string[] args, string stdin = "{}", Dictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo(FindPowerShell()!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _script }.Concat(args))
            psi.ArgumentList.Add(a);
        psi.Environment["APPDATA"] = _appData;
        psi.Environment["USERPROFILE"] = _profile;
        psi.Environment.Remove("CLAUDE_CONFIG_DIR");
        psi.Environment.Remove("SANDUHR_CHAIN_DEPTH");
        // Windows PowerShell takes 1 to 2 s to start, which is the whole of their command's 2 s budget on a
        // slow machine: the run tests give it room, and the timeout test puts the default back.
        psi.Environment["SANDUHR_CHAIN_MS"] = "15000";
        foreach (var (k, v) in env ?? [])
        {
            if (v is null) psi.Environment.Remove(k); else psi.Environment[k] = v;
        }

        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail("the statusline script did not finish in 30 s");
        }
        return new Run(stdout.Result, stderr.Result, p.ExitCode, sw.Elapsed);
    }

    private static string Chain(string command) => Convert.ToBase64String(Encoding.UTF8.GetBytes(command));

    // PowerShell stands in for Git Bash: a CLAUDE_CODE_GIT_BASH_PATH that does not exist means
    // "no Git Bash", and the script then runs their command with `powershell -NoProfile -Command`.
    private static readonly Dictionary<string, string?> NoGitBash = new()
    {
        ["CLAUDE_CODE_GIT_BASH_PATH"] = @"C:\sanduhr-test-no-such-git\bin\bash.exe",
    };

    // The command Claude Code would run is whatever the user wrote; under the PowerShell fallback the
    // script wraps it in `powershell -NoProfile -Command`, so a bare cmdlet is enough.
    private const string Theirs = "Write-Output theirs";
    private const string TheirsNested = "powershell -NoProfile -Command \"Write-Output theirs\"";

    // -- plain ----------------------------------------------------------------

    [PowerShellFact]
    public void A_plain_snapshot_prints_session_weekly_and_the_weekly_reset()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var run = RunScript([]);
        Assert.Equal(0, run.ExitCode);
        var line = run.Text.TrimEnd('\n');
        Assert.StartsWith("5h 42% | wk 17% | wk resets ", line);
        Assert.DoesNotContain("\n", line);
        Assert.Matches("[\\u0020-\\u007E]+", line); // ASCII
        Assert.All(line, c => Assert.True(c <= 0x7E));
    }

    [PowerShellFact]
    public void Mine_weekly_prints_only_the_weekly_number()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        Assert.Equal("wk 17%\n", RunScript(["-Mine", "weekly"]).Text);
        Assert.Equal("5h 42%\n", RunScript(["-Mine", "session"]).Text);
        Assert.StartsWith("wk resets ", RunScript(["-Mine", "resets"]).Text);
    }

    [PowerShellFact]
    public void A_missing_snapshot_prints_nothing_and_a_dead_one_says_so()
    {
        Assert.Equal("", RunScript([]).Text);
        WriteSanduhr("snapshot.json", Snapshot(42, 17, capturedSecondsAgo: 1200));
        Assert.Equal("sanduhr: stale 20m - start widget\n", RunScript([]).Text);
    }

    [PowerShellFact]
    public void An_error_snapshot_keeps_the_error_rule()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17, status: "error").Replace("\"status\":\"error\"", "\"status\":\"error\",\"error_kind\":\"session_expired\""));
        Assert.StartsWith("sanduhr: reauth needed | last 5h 42% | wk 17%", RunScript([]).Text);
    }

    // -- seats ----------------------------------------------------------------

    private string Seat(string folder, string accountRef)
        => $"{{\"schema_version\":1,\"seats\":[{{\"folder\":{System.Text.Json.JsonSerializer.Serialize(folder)},\"account_ref\":\"{accountRef}\"}}]}}";

    [PowerShellFact]
    public void The_seat_lookup_picks_the_seat_file_for_the_config_dir()
    {
        var seatFolder = Path.Combine(_root.Path, "seat-a");
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        WriteSanduhr("snapshot.abcd1234.json", Snapshot(77, 55));
        WriteSanduhr("seats.json", Seat(seatFolder, "abcd1234"));

        // Compared as full paths, ignoring case and trailing separators.
        var linked = RunScript(["-Mine", "session,weekly"], env: new() { ["CLAUDE_CONFIG_DIR"] = seatFolder.ToUpperInvariant() + "\\" });
        Assert.Equal("5h 77% | wk 55%\n", linked.Text);

        var other = RunScript(["-Mine", "session,weekly"], env: new() { ["CLAUDE_CONFIG_DIR"] = Path.Combine(_root.Path, "elsewhere") });
        Assert.Equal("5h 42% | wk 17%\n", other.Text);

        var unset = RunScript(["-Mine", "session,weekly"]);
        Assert.Equal("5h 42% | wk 17%\n", unset.Text);
    }

    [PowerShellFact]
    public void An_unset_config_dir_means_the_dot_claude_folder_in_the_profile()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        WriteSanduhr("snapshot.0a0b0c0d.json", Snapshot(9, 8));
        WriteSanduhr("seats.json", Seat(Path.Combine(_profile, ".claude"), "0a0b0c0d"));
        Assert.Equal("5h 9% | wk 8%\n", RunScript(["-Mine", "session,weekly"]).Text);
    }

    [PowerShellFact]
    public void A_linked_account_whose_file_is_missing_prints_nothing_of_sanduhrs()
    {
        var seatFolder = Path.Combine(_root.Path, "seat-b");
        WriteSanduhr("snapshot.json", Snapshot(42, 17)); // the active account's, which is not this seat's
        WriteSanduhr("seats.json", Seat(seatFolder, "deadbeef"));
        var run = RunScript([], env: new() { ["CLAUDE_CONFIG_DIR"] = seatFolder });
        Assert.Equal("", run.Text);
    }

    [PowerShellFact]
    public void A_bad_seats_file_falls_back_to_the_plain_snapshot()
    {
        var seatFolder = Path.Combine(_root.Path, "seat-c");
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        WriteSanduhr("snapshot.abcd1234.json", Snapshot(77, 55));
        var env = new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = seatFolder };
        foreach (var seats in new[]
        {
            "{ not json",
            Seat(seatFolder, "abcd1234").Replace("\"schema_version\":1", "\"schema_version\":2"),
            Seat(seatFolder, "ABCD1234"),   // refs are lowercase hex
            Seat(seatFolder, "abcd123"),    // eight characters
            Seat("relative\\folder", "abcd1234"),
        })
        {
            WriteSanduhr("seats.json", seats);
            Assert.Equal("5h 42% | wk 17%\n", RunScript(["-Mine", "session,weekly"], env: env).Text);
        }
    }

    // -- combine --------------------------------------------------------------

    [PowerShellFact]
    public void Combine_on_a_line_of_its_own_prints_their_rows_then_sanduhrs()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var run = RunScript(["-Chain", Chain(TheirsNested), "-Join", "line", "-Mine", "session,weekly"], env: NoGitBash);
        Assert.Equal("theirs\n5h 42% | wk 17%\n", run.Text);
    }

    [PowerShellFact]
    public void Combine_on_the_same_row_appends_after_an_sgr_reset()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var run = RunScript(["-Chain", Chain(Theirs), "-Join", "same", "-Mine", "session,weekly"], env: NoGitBash);
        Assert.Equal("theirs\u001b[0m | 5h 42% | wk 17%\n", run.Text);
    }

    [PowerShellFact]
    public void Their_command_gets_the_same_stdin_and_its_utf8_output_survives()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        // Their command echoes the first stdin line back with a non-ASCII mark, as UTF-8.
        var theirs = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); "
            + "$in = [Console]::In.ReadToEnd().Trim(); Write-Output ($in + ' ' + [char]0x00E9 + [char]0x2713)";
        var run = RunScript(["-Chain", Chain(theirs), "-Join", "line", "-Mine", "weekly"], stdin: "{\"model\":\"x\"}", env: NoGitBash);
        Assert.Equal("{\"model\":\"x\"} \u00e9\u2713\nwk 17%\n", run.Text);
    }

    [PowerShellFact]
    public void A_command_that_times_out_prints_only_sanduhrs_line_within_the_budget()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        // Their command sleeps 20 s inside a nested shell. The script gives it 2 s, kills the tree (a
        // grandchild holding the pipe would keep this read open for the full 20 s) and prints its own line.
        // The bound is generous because Windows PowerShell's own start-up counts against it.
        var slow = "powershell -NoProfile -Command \"Start-Sleep 20; Write-Output late\"";
        var env = new Dictionary<string, string?>(NoGitBash) { ["SANDUHR_CHAIN_MS"] = null };
        var run = RunScript(["-Chain", Chain(slow), "-Join", "line", "-Mine", "session,weekly"], env: env);
        Assert.Equal("5h 42% | wk 17%\n", run.Text);
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(9), $"took {run.Elapsed.TotalSeconds:0.0} s");
    }

    [PowerShellFact]
    public void A_failing_command_prints_only_sanduhrs_line()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var failing = "powershell -NoProfile -Command \"Write-Output half; exit 3\"";
        var run = RunScript(["-Chain", Chain(failing), "-Join", "line", "-Mine", "weekly"], env: NoGitBash);
        Assert.Equal("wk 17%\n", run.Text);
    }

    [PowerShellFact]
    public void With_no_snapshot_a_combined_line_is_just_theirs()
    {
        var run = RunScript(["-Chain", Chain(Theirs), "-Join", "line"], env: NoGitBash);
        Assert.Equal("theirs\n", run.Text);
    }

    [PowerShellFact]
    public void A_chain_inside_a_chain_is_not_run_again()
    {
        // The depth guard: when the user's command is Sanduhr's own script (a loop), the inner copy
        // runs without chaining.
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        var run = RunScript(["-Chain", Chain(Theirs), "-Join", "line", "-Mine", "weekly"],
            env: new Dictionary<string, string?>(NoGitBash) { ["SANDUHR_CHAIN_DEPTH"] = "1" });
        Assert.Equal("wk 17%\n", run.Text);
    }

    // -- real Git Bash --------------------------------------------------------

    [GitBashFact]
    public void Under_real_git_bash_a_chain_with_quotes_backslash_and_dollar_round_trips_and_runs()
    {
        WriteSanduhr("snapshot.json", Snapshot(42, 17));
        // Bash text:  printf '%s\n' "a\"b'c\\d\$e"   which prints   a"b'c\d$e
        const string theirs = "printf '%s\\n' \"a\\\"b'c\\\\d\\$e\"";
        const string expected = "a\"b'c\\d$e";
        var installer = new StatuslineInstaller(_profile, Path.Combine(_root.Path, "bin"));
        Assert.True(installer.InstallScript());
        var command = installer.BuildCommand(theirs, "line", ["weekly"]);

        // The installed command parses back to the same chain.
        var parsed = StatuslineInstaller.TryParseCommand(command);
        Assert.NotNull(parsed);
        Assert.Equal(theirs, parsed!.Chain);

        // Claude Code runs the whole statusLine command through Git Bash: do the same. The script then
        // runs their chain through Git Bash again (CLAUDE_CODE_GIT_BASH_PATH unset, found via git.exe).
        var psi = new ProcessStartInfo(FindGitBash()!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        psi.Environment["APPDATA"] = _appData;
        psi.Environment["USERPROFILE"] = _profile;
        psi.Environment.Remove("CLAUDE_CONFIG_DIR");
        psi.Environment.Remove("CLAUDE_CODE_GIT_BASH_PATH");
        psi.Environment.Remove("SANDUHR_CHAIN_DEPTH");
        psi.Environment["SANDUHR_CHAIN_MS"] = "15000";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardInput.Write("{}");
        p.StandardInput.Close();
        if (!p.WaitForExit(40_000))
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail("the statusLine command did not finish in 40 s under Git Bash");
        }
        Assert.True(p.ExitCode == 0, $"exit {p.ExitCode}; stderr: {stderr.Result}");
        Assert.Equal(expected + "\nwk 17%\n", stdout.Result.Replace("\r\n", "\n"));
    }
}
