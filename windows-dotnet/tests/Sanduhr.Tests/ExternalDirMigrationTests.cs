using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Plan B, B.4: pre-3.5.0 registrations (statusline, MCP, meters mod, glow hooks) named <c>%APPDATA%\Sanduhr</c>; the
/// migration and the engines' RepointLegacy methods move the ones that are still Sanduhr's to <c>%USERPROFILE%\.sanduhr</c>.
/// Everything lives under a temp root: a made-up profile, a made-up AppData.
/// </summary>
public sealed class ExternalDirMigrationTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private string Root => _tmp.Path;
    private string Profile => Path.Combine(Root, "home");
    private string AppData => Path.Combine(Root, "appdata", "Sanduhr");   // the old location, and where backups and receipts stay
    private string External => Path.Combine(Profile, ".sanduhr");         // the new one
    private string ModSource => Path.Combine(Root, "app", "mods", "sanduhr-meters");
    private string McpSource => Path.Combine(Root, "app", "mcp");
    private string Marker => Path.Combine(AppData, ".external-dir-migration");
    private string Folder => Path.Combine(Profile, ".claude");
    private string Settings => Path.Combine(Folder, "settings.json");
    private string LegacyScript => Path.Combine(AppData, "bin", StatuslineInstaller.ScriptName);
    private string LegacyLauncher => Path.Combine(AppData, "bin", "sanduhr-mcp.cmd");
    private string Backups => Path.Combine(AppData, "backups");

    public ExternalDirMigrationTests()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "projects"));
        WriteSource(ModSource, ".claude-plugin/plugin.json", "{ \"name\": \"sanduhr-meters\", \"version\": \"0.3.0\" }");
        WriteSource(ModSource, "hooks/register.tsx", "export {};");
        TouchSource(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        Directory.CreateDirectory(McpSource);
        File.WriteAllText(Path.Combine(McpSource, "sanduhr-mcp.exe"), "fake-exe");
    }

    public void Dispose() => _tmp.Dispose();

    private static void WriteSource(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    private void TouchSource(DateTime utc)
    {
        foreach (var f in Directory.EnumerateFiles(ModSource, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, utc);
    }

    // The new-location engines (what 3.5.0 builds) ...
    private StatuslineInstaller NewStatusline() => new(Profile, Path.Combine(External, "bin"), null, AppData);
    private McpIntegrationInstaller NewMcp() => new(Profile, AppData, null, External);
    private ClaudeCodeIntegrations NewCc() => new(Profile, AppData, ModSource, null, External) { LegacyModsRoots = [Path.Combine(AppData, "mods")] };
    // ... and the old ones (everything under AppData), which write the registrations the migration finds.
    private StatuslineInstaller OldStatusline() => new(Profile, Path.Combine(AppData, "bin"), null, AppData);
    private McpIntegrationInstaller OldMcp() => new(Profile, AppData, null);
    private ClaudeCodeIntegrations OldCc() => new(Profile, AppData, ModSource);

    private ExternalDirMigrationInputs Inputs(string? mcpSource = "default") => new(
        Marker, [Folder], [".claude"], LegacyScript, LegacyLauncher, NewStatusline(), NewMcp(), NewCc(),
        mcpSource == "default" ? McpSource : mcpSource);

    private string McpConfig => NewMcp().ConfigPathFor(".claude");

    private static JsonObject Json(string path) => (JsonObject)JsonNode.Parse(File.ReadAllBytes(path))!;

    private int BackupCount => Directory.Exists(Backups) ? Directory.GetFiles(Backups).Length : 0;

    private List<IntegrationReceipt> Receipts(IntegrationKind kind)
        => new ReceiptStore(Path.Combine(AppData, "integrations", "installs.json")).Load().Records.Where(r => r.Kind == kind).ToList();

    private static JsonObject Without(JsonObject o, params string[] keys)
    {
        var c = (JsonObject)o.DeepClone();
        foreach (var k in keys) c.Remove(k);
        return c;
    }

    // ------------------------------------------------------------------ statusline

    [Fact]
    public void Statusline_replace_is_repointed_and_the_users_old_line_stays_in_the_receipt()
    {
        File.WriteAllText(Settings, "{\n  \"model\": \"opus\",\n  \"statusLine\": { \"type\": \"command\", \"command\": \"echo mine\" }\n}\n");
        Assert.True(OldStatusline().Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine));
        var before = Json(Settings);
        var prev = Receipts(IntegrationKind.Statusline).Single().Previous;
        Assert.NotNull(prev);
        int backups = BackupCount;

        var outcome = NewStatusline().RepointLegacy(Folder, LegacyScript);

        Assert.Equal(RepointOutcome.Repointed, outcome);
        var parsed = StatuslineInstaller.TryParseCommand((string?)Json(Settings)["statusLine"]!["command"]);
        Assert.Equal(NewStatusline().ScriptPath, parsed!.ScriptPath);
        Assert.True(File.Exists(NewStatusline().ScriptPath));
        Assert.Null(parsed.Chain);
        Assert.True(JsonNode.DeepEquals(Without(before, "statusLine"), Without(Json(Settings), "statusLine")));
        Assert.Equal(prev, Receipts(IntegrationKind.Statusline).Single().Previous);
        Assert.Equal(StatuslineMode.Replace, Receipts(IntegrationKind.Statusline).Single().Mode);
        Assert.True(BackupCount > backups);

        // Remove still gives the user's line back.
        Assert.True(NewStatusline().Remove(".claude"));
        Assert.Equal("echo mine", (string?)Json(Settings)["statusLine"]!["command"]);
    }

    [Fact]
    public void Statusline_combine_keeps_chain_join_and_segments()
    {
        File.WriteAllText(Settings, "{ \"statusLine\": { \"type\": \"command\", \"command\": \"echo mine\" } }");
        Assert.True(OldStatusline().Install(".claude", StatuslineMode.Combine, ["session", "resets"], StatuslineInstaller.JoinSame));
        var was = StatuslineInstaller.TryParseCommand((string?)Json(Settings)["statusLine"]!["command"])!;
        Assert.Equal("echo mine", was.Chain);
        var prev = Receipts(IntegrationKind.Statusline).Single().Previous;

        Assert.Equal(RepointOutcome.Repointed, NewStatusline().RepointLegacy(Folder, LegacyScript));

        var now = StatuslineInstaller.TryParseCommand((string?)Json(Settings)["statusLine"]!["command"])!;
        Assert.Equal(NewStatusline().ScriptPath, now.ScriptPath);
        Assert.Equal("echo mine", now.Chain);
        Assert.Equal(StatuslineInstaller.JoinSame, now.Join);
        Assert.Equal(["session", "resets"], now.Segments);
        var receipt = Receipts(IntegrationKind.Statusline).Single();
        Assert.Equal(prev, receipt.Previous);
        Assert.Equal(StatuslineMode.Combine, receipt.Mode);
    }

    [Fact]
    public void Statusline_that_is_not_the_old_script_is_left_alone()
    {
        var other = Path.Combine(Root, "elsewhere");
        var sut = NewStatusline();
        foreach (var text in new[]
        {
            "{ \"statusLine\": { \"type\": \"command\", \"command\": \"echo mine\" } }",
            "{ \"statusLine\": { \"type\": \"command\", \"command\": " + JsonSerializer.Serialize(new StatuslineInstaller(Profile, other, null, AppData).BuildCommand()) + " } }",
            "{ \"statusLine\": { \"type\": \"command\", \"command\": " + JsonSerializer.Serialize(sut.BuildCommand()) + " } }", // already the new one
            "{ \"model\": \"opus\" }",
        })
        {
            File.WriteAllText(Settings, text);
            var bytes = File.ReadAllBytes(Settings);
            int backups = BackupCount;
            Assert.Equal(RepointOutcome.NothingToDo, sut.RepointLegacy(Folder, LegacyScript));
            Assert.Equal(bytes, File.ReadAllBytes(Settings));
            Assert.Equal(backups, BackupCount);
        }
    }

    // ------------------------------------------------------------------ mcp

    private string McpText(string command)
        => "{\n  \"numStartups\": 7,\n  \"mcpServers\": {\n    \"other\": { \"command\": \"x\" },\n    \"sanduhr\": {  \"command\": "
           + JsonSerializer.Serialize(command) + ",\n      \"args\": [], \"env\": { \"A\": \"b\" } }\n  },\n  \"tail\": [1,  2]\n}\n";

    [Fact]
    public void Mcp_launcher_is_replaced_and_every_other_byte_stays()
    {
        var original = McpText(LegacyLauncher);
        Directory.CreateDirectory(Path.GetDirectoryName(McpConfig)!);
        File.WriteAllText(McpConfig, original);
        int backups = BackupCount;
        var sut = NewMcp();

        Assert.Equal(RepointOutcome.Repointed, sut.RepointFile(McpConfig, LegacyLauncher));

        Assert.Equal(McpText(sut.LauncherPath), File.ReadAllText(McpConfig));
        Assert.StartsWith(External, sut.LauncherPath);
        Assert.True(BackupCount > backups);
        // Again: already moved, nothing to do.
        Assert.Equal(RepointOutcome.NothingToDo, sut.RepointFile(McpConfig, LegacyLauncher));
    }

    [Fact]
    public void Mcp_foreign_or_already_new_entry_is_left_alone_and_a_broken_file_fails_untouched()
    {
        var sut = NewMcp();
        Directory.CreateDirectory(Path.GetDirectoryName(McpConfig)!);
        foreach (var text in new[]
        {
            McpText("C:\\tools\\my-own-server.cmd"),
            McpText(sut.LauncherPath),
            "{ \"mcpServers\": { \"other\": {} } }",
            "[1,2]",
        })
        {
            File.WriteAllText(McpConfig, text);
            var bytes = File.ReadAllBytes(McpConfig);
            int backups = BackupCount;
            Assert.Equal(RepointOutcome.NothingToDo, sut.RepointFile(McpConfig, LegacyLauncher));
            Assert.Equal(bytes, File.ReadAllBytes(McpConfig));
            Assert.Equal(backups, BackupCount);
        }
        File.WriteAllText(McpConfig, "{ not json");
        var broken = File.ReadAllBytes(McpConfig);
        Assert.Equal(RepointOutcome.Failed, sut.RepointFile(McpConfig, LegacyLauncher));
        Assert.Equal(broken, File.ReadAllBytes(McpConfig));
        Assert.Equal(RepointOutcome.NothingToDo, sut.RepointFile(Path.Combine(Root, "missing.json"), LegacyLauncher));
    }

    // ------------------------------------------------------------------ meters

    private string InstallLegacyMeters(string settingsText)
    {
        File.WriteAllText(Settings, settingsText);
        var old = OldCc();
        Assert.True(old.Install(IntegrationKind.Meters, Folder).Ok);
        var stamp = old.CurrentModStamp()!;
        TouchSource(new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)); // the shipped mod moves on
        return stamp;
    }

    private const string MetersSettings = "{\n  \"model\": \"opus\",\n  \"env\": { \"CLAUDE_CODE_PLUGIN_DIRS\": \"C:\\\\other\", \"X\": \"1\" }\n}\n";

    [Fact]
    public void Meters_legacy_entry_reads_outdated_and_is_repointed_in_place()
    {
        var oldStamp = InstallLegacyMeters(MetersSettings);
        var sut = NewCc();
        var before = Json(Settings);
        var prev = Receipts(IntegrationKind.Meters).Single().Previous;
        Assert.NotNull(prev);
        Assert.Equal(IntegrationStatus.Outdated, sut.Status(IntegrationKind.Meters, Folder));
        int backups = BackupCount;

        Assert.Equal(RepointOutcome.Repointed, sut.RepointLegacy(IntegrationKind.Meters, Folder));

        var newStamp = sut.CurrentModStamp()!;
        Assert.NotEqual(oldStamp, newStamp);
        var list = (string)Json(Settings)["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!;
        Assert.Equal("C:\\other;" + sut.ModPathFor(newStamp), list);
        Assert.StartsWith(External, sut.ModPathFor(newStamp));
        Assert.True(File.Exists(Path.Combine(sut.ModPathFor(newStamp), ".claude-plugin", "plugin.json")));
        Assert.Equal("1", (string?)Json(Settings)["env"]!["X"]);
        Assert.Equal("opus", (string?)Json(Settings)["model"]);
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Meters, Folder));
        var receipt = Receipts(IntegrationKind.Meters).Single();
        Assert.Equal(newStamp, receipt.Pinned);
        Assert.Equal(prev, receipt.Previous);
        Assert.True(BackupCount > backups);
        Assert.Equal(RepointOutcome.NothingToDo, sut.RepointLegacy(IntegrationKind.Meters, Folder));
        _ = before;
    }

    [Fact]
    public void Meters_entry_under_another_root_is_left_alone()
    {
        var sut = NewCc();
        var foreign = Path.Combine(Root, "someone-else", "mods", "v0.1.0-20250101000000", "sanduhr-meters");
        File.WriteAllText(Settings, "{ \"env\": { \"CLAUDE_CODE_PLUGIN_DIRS\": " + JsonSerializer.Serialize(foreign) + " } }");
        var bytes = File.ReadAllBytes(Settings);
        Assert.Equal(RepointOutcome.NothingToDo, sut.RepointLegacy(IntegrationKind.Meters, Folder));
        Assert.Equal(bytes, File.ReadAllBytes(Settings));
        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Meters, Folder));
    }

    [Fact]
    public void Meters_repoint_fails_without_the_mod_files_and_changes_nothing()
    {
        InstallLegacyMeters(MetersSettings);
        var sut = NewCc();
        sut.ModSourceDir = Path.Combine(Root, "nowhere");
        var bytes = File.ReadAllBytes(Settings);
        Assert.Equal(RepointOutcome.Failed, sut.RepointLegacy(IntegrationKind.Meters, Folder));
        Assert.Equal(bytes, File.ReadAllBytes(Settings));
    }

    [Fact]
    public void Meters_remove_takes_out_a_legacy_entry_and_gives_the_file_back()
    {
        File.WriteAllText(Settings, MetersSettings);
        var original = File.ReadAllBytes(Settings);
        Assert.True(OldCc().Install(IntegrationKind.Meters, Folder).Ok);
        Assert.NotEqual(original, File.ReadAllBytes(Settings));
        var sut = NewCc();
        Assert.True(sut.Remove(IntegrationKind.Meters, Folder).Ok);
        Assert.Equal(original, File.ReadAllBytes(Settings));
    }

    // ------------------------------------------------------------------ hooks

    /// <summary>Installs the hooks, then rewrites their command to the pre-3.5.0 text (the receipt stays).</summary>
    private void InstallLegacyHooks(string settingsText)
    {
        File.WriteAllText(Settings, settingsText);
        Assert.True(NewCc().Install(IntegrationKind.Hooks, Folder).Ok);
        var text = File.ReadAllText(Settings);
        var legacy = text.Replace("$env:USERPROFILE '.sanduhr'", "$env:APPDATA 'Sanduhr'");
        Assert.NotEqual(text, legacy);
        File.WriteAllText(Settings, legacy);
    }

    private const string HooksSettings =
        "{\n  \"model\": \"opus\",\n  \"hooks\": {\n    \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"command\": \"my-own-thing\" } ] } ]\n  }\n}\n";

    [Fact]
    public void Hooks_legacy_group_reads_outdated_and_is_repointed_beside_the_users_own()
    {
        InstallLegacyHooks(HooksSettings);
        var sut = NewCc();
        var receipt0 = Receipts(IntegrationKind.Hooks).Single();
        Assert.Equal(IntegrationStatus.Outdated, sut.Status(IntegrationKind.Hooks, Folder));
        var mine = Json(Settings)["hooks"]!["Stop"]!.AsArray().Where(g => !ClaudeCodeIntegrations.IsOurHookGroup(g)).Select(g => g!.ToJsonString()).ToList();
        int backups = BackupCount;

        Assert.Equal(RepointOutcome.Repointed, sut.RepointLegacy(IntegrationKind.Hooks, Folder));

        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Hooks, Folder));
        var text = File.ReadAllText(Settings);
        Assert.DoesNotContain("$env:APPDATA", text);
        var stop = Json(Settings)["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal(mine, stop.Where(g => !ClaudeCodeIntegrations.IsOurHookGroup(g)).Select(g => g!.ToJsonString()).ToList());
        Assert.Single(Json(Settings)["hooks"]!["Notification"]!.AsArray());
        var receipt = Receipts(IntegrationKind.Hooks).Single();
        Assert.Equal(receipt0.Hooks!.CreatedEvents, receipt.Hooks!.CreatedEvents);
        Assert.Equal(receipt0.Hooks.HooksInner, receipt.Hooks.HooksInner);
        Assert.True(BackupCount > backups);
    }

    [Fact]
    public void Hooks_near_copy_or_users_own_hook_is_left_alone()
    {
        var sut = NewCc();
        var legacy = ClaudeCodeIntegrations.LegacyHookCommand(ClaudeCodeIntegrations.DoneSignal);
        foreach (var text in new[]
        {
            HooksSettings,
            // legacy command plus a second hook in the group: the user's group
            "{ \"hooks\": { \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"shell\": \"powershell\", \"command\": " + JsonSerializer.Serialize(legacy)
                + " }, { \"type\": \"command\", \"command\": \"echo\" } ] } ] } }",
            // legacy command edited by the user
            "{ \"hooks\": { \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"shell\": \"powershell\", \"command\": " + JsonSerializer.Serialize(legacy + "; echo x") + " } ] } ] } }",
            // legacy command on another shell
            "{ \"hooks\": { \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"shell\": \"bash\", \"command\": " + JsonSerializer.Serialize(legacy) + " } ] } ] } }",
        })
        {
            File.WriteAllText(Settings, text);
            var bytes = File.ReadAllBytes(Settings);
            int backups = BackupCount;
            Assert.Equal(RepointOutcome.NothingToDo, sut.RepointLegacy(IntegrationKind.Hooks, Folder));
            Assert.Equal(bytes, File.ReadAllBytes(Settings));
            Assert.Equal(backups, BackupCount);
        }
    }

    [Fact]
    public void Hooks_remove_takes_out_a_legacy_group_and_gives_the_file_back()
    {
        File.WriteAllText(Settings, HooksSettings);
        var original = File.ReadAllBytes(Settings);
        InstallLegacyHooks(HooksSettings);
        Assert.True(NewCc().Remove(IntegrationKind.Hooks, Folder).Ok);
        Assert.Equal(original, File.ReadAllBytes(Settings));
    }

    [Fact]
    public void Hook_command_is_the_new_text_and_the_legacy_text_is_the_old_one()
    {
        Assert.Equal("$d = Join-Path $env:USERPROFILE '.sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d 'glow-done.signal'), '') }",
            ClaudeCodeIntegrations.HookCommand("glow-done.signal"));
        Assert.Equal("$d = Join-Path $env:APPDATA 'Sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d 'glow-done.signal'), '') }",
            ClaudeCodeIntegrations.LegacyHookCommand("glow-done.signal"));
    }

    // ------------------------------------------------------------------ the migration

    private void SeedAllLegacy()
    {
        // statusline + meters + hooks share one settings.json; the MCP entry sits in .claude.json.
        File.WriteAllText(Settings, "{ \"model\": \"opus\" }");
        Assert.True(OldStatusline().Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine));
        Assert.True(OldCc().Install(IntegrationKind.Meters, Folder).Ok);
        Assert.True(NewCc().Install(IntegrationKind.Hooks, Folder).Ok);
        File.WriteAllText(Settings, File.ReadAllText(Settings).Replace("$env:USERPROFILE '.sanduhr'", "$env:APPDATA 'Sanduhr'"));
        Directory.CreateDirectory(Path.GetDirectoryName(McpConfig)!);
        File.WriteAllText(McpConfig, McpText(LegacyLauncher));
    }

    [Fact]
    public void Run_repoints_every_kind_and_writes_done_then_a_second_run_changes_nothing()
    {
        SeedAllLegacy();
        var logs = new List<string>();
        var sut = new ExternalDirMigration(logs.Add);

        var first = sut.Run(Inputs());

        Assert.False(first.ShortCircuited);
        Assert.True(first.Completed);
        Assert.Equal((1, 1, 1, 1, 0), (first.StatuslineRepointed, first.McpRepointed, first.MetersRepointed, first.HooksRepointed, first.Failed));
        Assert.Equal("done", File.ReadAllText(Marker));
        var settings = File.ReadAllBytes(Settings);
        var mcp = File.ReadAllBytes(McpConfig);
        Assert.DoesNotContain("$env:APPDATA", File.ReadAllText(Settings));
        Assert.Contains(External.Replace("\\", "\\\\"), File.ReadAllText(Settings));
        Assert.True(File.Exists(NewMcp().LauncherPath));
        Assert.All(logs, l => { Assert.StartsWith("external-dir-migration", l); Assert.DoesNotContain(Root, l); });

        int backups = BackupCount;
        var second = sut.Run(Inputs());
        Assert.True(second.ShortCircuited);
        Assert.True(second.Completed);
        Assert.Equal(settings, File.ReadAllBytes(Settings));
        Assert.Equal(mcp, File.ReadAllBytes(McpConfig));
        Assert.Equal(backups, BackupCount);
    }

    [Fact]
    public void Mcp_is_skipped_and_not_done_when_there_is_no_server_to_copy_then_a_later_run_finishes()
    {
        SeedAllLegacy();
        var mcpBytes = File.ReadAllBytes(McpConfig);
        var sut = new ExternalDirMigration();

        var first = sut.Run(Inputs(mcpSource: null));

        Assert.False(first.Completed);
        Assert.Equal(1, first.Failed);
        Assert.Equal(0, first.McpRepointed);
        Assert.Equal(1, first.StatuslineRepointed); // the rest still moved
        Assert.Equal(mcpBytes, File.ReadAllBytes(McpConfig));
        Assert.Equal("attempt 1", File.ReadAllText(Marker));

        var second = sut.Run(Inputs());
        Assert.False(second.ShortCircuited);
        Assert.True(second.Completed);
        Assert.Equal(1, second.McpRepointed);
        Assert.Equal(0, second.StatuslineRepointed); // already moved
        Assert.Equal("done", File.ReadAllText(Marker));
    }

    [Fact]
    public void A_missing_server_folder_counts_like_none()
    {
        SeedAllLegacy();
        var r = new ExternalDirMigration().Run(Inputs(mcpSource: Path.Combine(Root, "nowhere")));
        Assert.False(r.Completed);
        Assert.Equal("attempt 1", File.ReadAllText(Marker));
    }

    [Fact]
    public void Mcp_without_a_legacy_entry_needs_no_server_files()
    {
        File.WriteAllText(Settings, "{ \"model\": \"opus\" }");
        var r = new ExternalDirMigration().Run(Inputs(mcpSource: null));
        Assert.True(r.Completed);
        Assert.Equal("done", File.ReadAllText(Marker));
    }

    [Fact]
    public void The_attempt_cap_stops_it()
    {
        SeedAllLegacy();
        var before = File.ReadAllBytes(Settings);
        Directory.CreateDirectory(AppData);
        File.WriteAllText(Marker, "attempt 3");

        var r = new ExternalDirMigration().Run(Inputs());

        Assert.True(r.ShortCircuited);
        Assert.False(r.Completed);
        Assert.Equal(before, File.ReadAllBytes(Settings));
        Assert.Equal("attempt 3", File.ReadAllText(Marker));
    }

    [Fact]
    public void A_failure_counts_up_the_attempts()
    {
        SeedAllLegacy();
        var sut = new ExternalDirMigration();
        sut.Run(Inputs(mcpSource: null));
        Assert.Equal("attempt 1", File.ReadAllText(Marker));
        sut.Run(Inputs(mcpSource: null));
        Assert.Equal("attempt 2", File.ReadAllText(Marker));
        sut.Run(Inputs(mcpSource: null));
        Assert.Equal("attempt 3", File.ReadAllText(Marker));
        var r = sut.Run(Inputs()); // capped: even with a server it stays put
        Assert.True(r.ShortCircuited);
    }

    [Fact]
    public void Entries_that_are_no_longer_sanduhrs_are_left_alone_and_the_run_still_completes()
    {
        File.WriteAllText(Settings, "{ \"statusLine\": { \"type\": \"command\", \"command\": \"echo mine\" } }");
        Directory.CreateDirectory(Path.GetDirectoryName(McpConfig)!);
        File.WriteAllText(McpConfig, McpText("C:\\tools\\my-own-server.cmd"));
        var s = File.ReadAllBytes(Settings);
        var m = File.ReadAllBytes(McpConfig);

        var r = new ExternalDirMigration().Run(Inputs(mcpSource: null));

        Assert.True(r.Completed);
        Assert.Equal((0, 0, 0, 0, 0), (r.StatuslineRepointed, r.McpRepointed, r.MetersRepointed, r.HooksRepointed, r.Failed));
        Assert.Equal(s, File.ReadAllBytes(Settings));
        Assert.Equal(m, File.ReadAllBytes(McpConfig));
    }

    [Fact]
    public void Run_never_throws_on_a_bad_marker_path()
    {
        var inputs = Inputs() with { MarkerPath = new string('x', 40) + "\0:?" };
        var r = new ExternalDirMigration().Run(inputs);
        Assert.NotNull(r);
    }
}
