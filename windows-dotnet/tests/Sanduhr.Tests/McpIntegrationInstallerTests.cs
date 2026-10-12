using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class McpIntegrationInstallerTests
{
    private static McpIntegrationInstaller Make(TempDir home, TempDir sanduhrDir)
        => new(home.Path, sanduhrDir.Path);

    private static string MakeSource(TempDir tmp, string stampSuffix = "a")
    {
        string src = Path.Combine(tmp.Path, "source-" + stampSuffix);
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "sanduhr-mcp.exe"), "fake-exe-" + stampSuffix);
        File.WriteAllText(Path.Combine(src, "sanduhr-mcp.dll"), "fake-dll");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "dep.dll"), "dep");
        return src;
    }

    private static string CcConfig(TempDir home, string ccHome)
        => Path.Combine(home.Path, ccHome, ".claude.json");

    // -- server files + launcher ----------------------------------------------

    [Fact]
    public void InstallServerFiles_copies_the_tree_into_a_stamped_version_dir()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        string src = MakeSource(sanduhr);

        string? dir = installer.InstallServerFiles(src);

        Assert.NotNull(dir);
        Assert.StartsWith(installer.VersionsDir, dir);
        Assert.True(File.Exists(Path.Combine(dir!, "sanduhr-mcp.exe")));
        Assert.True(File.Exists(Path.Combine(dir!, "sub", "dep.dll")));
    }

    [Fact]
    public void InstallServerFiles_without_the_exe_refuses()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        string src = Path.Combine(sanduhr.Path, "empty");
        Directory.CreateDirectory(src);
        Assert.Null(Make(home, sanduhr).InstallServerFiles(src));
    }

    [Fact]
    public void A_newer_build_lands_in_a_new_dir_and_prune_keeps_only_current()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        string src = MakeSource(sanduhr);

        string dir1 = installer.InstallServerFiles(src)!;
        // Simulate a rebuild: bump the exe mtime so the stamp changes.
        File.SetLastWriteTimeUtc(Path.Combine(src, "sanduhr-mcp.exe"),
            DateTime.UtcNow.AddMinutes(5));
        string dir2 = installer.InstallServerFiles(src)!;

        Assert.NotEqual(dir1, dir2);
        installer.PruneOldVersions(dir2);
        Assert.False(Directory.Exists(dir1));
        Assert.True(Directory.Exists(dir2));
    }

    [Fact]
    public void Launcher_points_at_the_version_dir_and_carries_no_bom()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        string dir = installer.InstallServerFiles(MakeSource(sanduhr))!;

        Assert.True(installer.WriteLauncher(dir));
        byte[] bytes = File.ReadAllBytes(installer.LauncherPath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF);   // a BOM breaks cmd.exe line 1
        string text = File.ReadAllText(installer.LauncherPath);
        Assert.Contains($"\"{Path.Combine(dir, "sanduhr-mcp.exe")}\" %*", text);
        Assert.StartsWith("@echo off", text);
    }

    [Fact]
    public void RefreshInstall_chains_files_launcher_prune()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        string src = MakeSource(sanduhr);

        string? dir = installer.RefreshInstall(src);
        Assert.NotNull(dir);
        Assert.Contains(dir!, File.ReadAllText(installer.LauncherPath));
    }

    // -- registration ---------------------------------------------------------

    [Fact]
    public void Register_creates_claude_json_when_missing()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        var installer = Make(home, sanduhr);

        var result = installer.Register(".claude-personal");

        Assert.True(result.Ok);
        Assert.Null(result.PriorCommand);
        Assert.True(installer.IsRegistered(".claude-personal"));
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcConfig(home, ".claude-personal")))!;
        Assert.Equal(installer.LauncherPath, (string?)root["mcpServers"]!["sanduhr"]!["command"]);
    }

    [Fact]
    public void Register_preserves_other_servers_and_unknown_keys_with_backup()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var ccDir = Path.Combine(home.Path, ".claude-personal");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(CcConfig(home, ".claude-personal"),
            """{"numStartups":42,"mcpServers":{"firebase":{"command":"cmd","args":["/c","firebase-mcp"]}}}""");

        var installer = Make(home, sanduhr);
        Assert.True(installer.Register(".claude-personal").Ok);

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcConfig(home, ".claude-personal")))!;
        Assert.Equal(42, (int)root["numStartups"]!.GetValue<int>());          // untouched
        Assert.NotNull(root["mcpServers"]!["firebase"]);                       // untouched
        Assert.NotNull(root["mcpServers"]!["sanduhr"]);                        // added
        Assert.Empty(Directory.GetFiles(ccDir, "*sanduhr-backup*"));            // never beside a Claude Code file
        Assert.Single(Directory.GetFiles(Path.Combine(sanduhr.Path, "backups")));  // one copy of the original, in ours
        Assert.False(File.Exists(CcConfig(home, ".claude-personal") + ".tmp"));
    }

    [Fact]
    public void Register_then_deregister_leave_other_bytes_untouched_and_no_backup_beside_the_file()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var ccDir = Path.Combine(home.Path, ".claude-personal");
        Directory.CreateDirectory(ccDir);
        const string original = "{\r\n    \"numStartups\": 42,\r\n    \"mcpServers\": {\r\n        \"firebase\": {\"command\": \"cmd\"}\r\n    },\r\n    \"tail\":   [1,  2]\r\n}\r\n";
        File.WriteAllText(CcConfig(home, ".claude-personal"), original);
        var installer = Make(home, sanduhr);

        Assert.True(installer.Register(".claude-personal").Ok);
        string registered = File.ReadAllText(CcConfig(home, ".claude-personal"));
        Assert.Contains("\"tail\":   [1,  2]", registered);
        Assert.True(installer.Deregister(".claude-personal"));

        Assert.Equal(original, File.ReadAllText(CcConfig(home, ".claude-personal")));
        Assert.Empty(Directory.GetFiles(ccDir, "*sanduhr-backup*"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(sanduhr.Path, "backups")).Length);   // one per write
    }

    [Fact]
    public void Register_reowns_an_existing_sanduhr_entry_and_reports_the_prior()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        File.WriteAllText(CcConfig(home, ".claude"),
            """{"mcpServers":{"sanduhr":{"command":"C:\\old\\path\\sanduhr-mcp.cmd","args":[]}}}""");

        var installer = Make(home, sanduhr);
        var result = installer.Register(".claude");

        Assert.True(result.Ok);
        Assert.Equal(@"C:\old\path\sanduhr-mcp.cmd", result.PriorCommand);
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcConfig(home, ".claude")))!;
        Assert.Equal(installer.LauncherPath, (string?)root["mcpServers"]!["sanduhr"]!["command"]);
    }

    [Fact]
    public void Register_refuses_a_malformed_claude_json()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var ccDir = Path.Combine(home.Path, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(CcConfig(home, ".claude"), "{ broken");

        var result = Make(home, sanduhr).Register(".claude");

        Assert.False(result.Ok);
        Assert.Equal("{ broken", File.ReadAllText(CcConfig(home, ".claude")));
        Assert.Empty(Directory.GetFiles(ccDir, ".claude.json.sanduhr-backup-*"));
    }

    // -- deregistration -------------------------------------------------------

    [Fact]
    public void Deregister_removes_only_ours_and_keeps_the_rest()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        var installer = Make(home, sanduhr);
        File.WriteAllText(CcConfig(home, ".claude-personal"),
            """{"mcpServers":{"discord":{"command":"cmd","args":[]}}}""");
        installer.Register(".claude-personal");

        Assert.True(installer.Deregister(".claude-personal"));

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcConfig(home, ".claude-personal")))!;
        Assert.Null(root["mcpServers"]!["sanduhr"]);
        Assert.NotNull(root["mcpServers"]!["discord"]);
        Assert.False(installer.IsRegistered(".claude-personal"));
    }

    [Fact]
    public void Deregister_leaves_a_foreign_sanduhr_entry_alone()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        // Someone registered the NAME sanduhr pointing at their own thing.
        File.WriteAllText(CcConfig(home, ".claude"),
            """{"mcpServers":{"sanduhr":{"command":"my-own-tool.exe","args":[]}}}""");

        var installer = Make(home, sanduhr);
        Assert.True(installer.Deregister(".claude"));
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcConfig(home, ".claude")))!;
        Assert.Equal("my-own-tool.exe", (string?)root["mcpServers"]!["sanduhr"]!["command"]);
    }

    [Fact]
    public void Deregister_tolerates_missing_file_and_missing_entry()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        var installer = Make(home, sanduhr);
        Assert.True(installer.Deregister(".claude"));                          // no file
        File.WriteAllText(CcConfig(home, ".claude"), """{"mcpServers":{}}""");
        Assert.True(installer.Deregister(".claude"));                          // no entry
    }

    [Fact]
    public void RemoveIntegrationFiles_clears_launcher_and_version_dirs()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        string src = MakeSource(sanduhr);
        installer.RefreshInstall(src);
        Assert.True(File.Exists(installer.LauncherPath));

        installer.RemoveIntegrationFiles();

        Assert.False(File.Exists(installer.LauncherPath));
        Assert.False(Directory.Exists(installer.VersionsDir));
    }

    [Fact]
    public void InstallServerFiles_reuses_the_dir_for_an_identical_build()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        string src = MakeSource(sanduhr);

        string dir1 = installer.InstallServerFiles(src)!;
        File.WriteAllText(Path.Combine(dir1, "pinned-marker"), "held by a running session");
        string dir2 = installer.InstallServerFiles(src)!;

        Assert.Equal(dir1, dir2);
        Assert.True(File.Exists(Path.Combine(dir1, "pinned-marker")));   // nothing re-copied over it
    }

    // -- config path: where Claude Code actually reads .claude.json -----------

    [Fact]
    public void ConfigPathFor_default_home_is_the_profile_root_file()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        Assert.Equal(Path.Combine(home.Path, ".claude.json"), Make(home, sanduhr).ConfigPathFor(".claude"));
    }

    [Fact]
    public void ConfigPathFor_default_home_prefers_an_existing_inner_file()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        File.WriteAllText(Path.Combine(home.Path, ".claude", ".claude.json"), "{}");   // CLAUDE_CONFIG_DIR layout
        Assert.Equal(Path.Combine(home.Path, ".claude", ".claude.json"), Make(home, sanduhr).ConfigPathFor(".claude"));
    }

    [Fact]
    public void ConfigPathFor_a_named_home_is_always_inside_it()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Assert.Equal(Path.Combine(home.Path, ".claude-personal", ".claude.json"),
            Make(home, sanduhr).ConfigPathFor(".claude-personal"));
    }

    [Fact]
    public void Register_default_home_writes_the_profile_root_file_and_deregister_finds_it()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        File.WriteAllText(Path.Combine(home.Path, ".claude.json"),
            """{"numStartups":7,"mcpServers":{"github":{"command":"gh-mcp","args":[]}}}""");
        var installer = Make(home, sanduhr);

        Assert.True(installer.Register(".claude").Ok);

        Assert.False(File.Exists(Path.Combine(home.Path, ".claude", ".claude.json")));   // never invented the inner file
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(home.Path, ".claude.json")))!;
        Assert.Equal(installer.LauncherPath, (string?)root["mcpServers"]!["sanduhr"]!["command"]);
        Assert.NotNull(root["mcpServers"]!["github"]);
        Assert.True(installer.IsRegistered(".claude"));
        Assert.True(installer.Deregister(".claude"));
        Assert.False(installer.IsRegistered(".claude"));
    }

    [Fact]
    public void DetectCcHomes_finds_discovered_homes_in_stable_order()
    {
        // Discover requires projects/ (or the home's .claude.json) for a folder to count.
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal", "projects"));
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude", "projects"));
        Assert.Equal(new[] { ".claude", ".claude-personal" }, Make(home, sanduhr).DetectCcHomes());
    }

    [Fact]
    public void DetectCcHomes_finds_a_third_home_and_skips_backup_copies()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        foreach (var name in new[] { ".claude", ".claude-work", ".claude-backup-1", ".claude-work.config-backup-20261001" })
            Directory.CreateDirectory(Path.Combine(home.Path, name, "projects"));
        Assert.Equal(new[] { ".claude", ".claude-work" }, Make(home, sanduhr).DetectCcHomes());
    }

    // -- both candidate files for the default home -----------------------------

    private const string OurEntryJson = """{"command":"C:\\x\\sanduhr-mcp.cmd","args":[]}""";

    private static (string Outer, string Inner) Seed(TempDir home, string outerJson, string innerJson)
    {
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        string outer = Path.Combine(home.Path, ".claude.json");
        string inner = Path.Combine(home.Path, ".claude", ".claude.json");
        File.WriteAllText(outer, outerJson);
        File.WriteAllText(inner, innerJson);
        return (outer, inner);
    }

    [Fact]
    public void An_entry_in_the_inner_stub_is_found_and_removed_while_the_outer_file_is_signed_in()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var (outer, inner) = Seed(home,
            """{"oauthAccount":{"emailAddress":"a@b.c"},"mcpServers":{}}""",
            "{\"mcpServers\":{\"sanduhr\":" + OurEntryJson + "}}");
        var installer = Make(home, sanduhr);
        Assert.Equal(outer, installer.ConfigPathFor(".claude"));   // the signed-in file wins

        Assert.True(installer.IsRegistered(".claude"));
        Assert.True(installer.Deregister(".claude"));

        Assert.False(installer.IsRegistered(".claude"));
        Assert.DoesNotContain("sanduhr", File.ReadAllText(inner));
    }

    [Fact]
    public void An_entry_in_both_files_is_removed_from_both()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        string json = "{\"oauthAccount\":{},\"mcpServers\":{\"sanduhr\":" + OurEntryJson + "}}";
        var (outer, inner) = Seed(home, json, json);
        var installer = Make(home, sanduhr);

        Assert.True(installer.Deregister(".claude"));

        Assert.DoesNotContain("sanduhr", File.ReadAllText(outer));
        Assert.DoesNotContain("sanduhr", File.ReadAllText(inner));
        Assert.Contains("oauthAccount", File.ReadAllText(outer));
    }

    [Fact]
    public void A_foreign_entry_in_the_other_file_is_left_alone()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        const string foreign = """{"mcpServers":{"sanduhr":{"command":"my-own-tool.exe"}}}""";
        var (_, inner) = Seed(home, """{"oauthAccount":{}}""", foreign);

        Assert.True(Make(home, sanduhr).Deregister(".claude"));
        Assert.Equal(foreign, File.ReadAllText(inner));
    }

    [Fact]
    public void UndoAll_removes_the_files_when_every_deregister_works()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        var installer = Make(home, sanduhr);
        installer.RefreshInstall(MakeSource(sanduhr));
        Assert.True(installer.Register(".claude-personal").Ok);

        Assert.True(installer.UndoAll([".claude-personal"]));

        Assert.False(installer.IsRegistered(".claude-personal"));
        Assert.False(File.Exists(installer.LauncherPath));
    }

    [Fact]
    public void UndoAll_keeps_the_files_when_a_deregister_failed()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-work"));
        var installer = Make(home, sanduhr);
        installer.RefreshInstall(MakeSource(sanduhr));
        Assert.True(installer.Register(".claude-personal").Ok);
        File.WriteAllText(CcConfig(home, ".claude-work"), "{ broken");   // cannot be read, so cannot be cleared

        Assert.False(installer.UndoAll([".claude-personal", ".claude-work"]));

        Assert.True(File.Exists(installer.LauncherPath));
        Assert.True(Directory.Exists(installer.VersionsDir));
        Assert.False(installer.IsRegistered(".claude-personal"));   // the working one was still cleared
    }

    [Fact]
    public void ConfigPathFor_follows_the_shared_placement_rule()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        var installer = Make(home, sanduhr);
        Assert.Equal(Path.Combine(home.Path, ".claude-work", ".claude.json"), installer.ConfigPathFor(".claude-work"));
        Assert.Equal(ClaudeCodeHomes.ConfigFile(Path.Combine(home.Path, ".claude"), home.Path),
            installer.ConfigPathFor(".claude"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_config_with_a_byte_order_mark_round_trips_through_register_and_deregister(string eol)
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-work", "projects"));
        string path = CcConfig(home, ".claude-work");
        string text = "{" + eol + "  \"numStartups\": 3," + eol + "  \"mcpServers\": {" + eol
                      + "    \"other\": {" + eol + "      \"command\": \"x\"" + eol + "    }" + eol + "  }" + eol + "}" + eol;
        byte[] original = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(text)];
        File.WriteAllBytes(path, original);
        var installer = Make(home, sanduhr);

        Assert.True(installer.Register(".claude-work").Ok);
        byte[] registered = File.ReadAllBytes(path);
        Assert.Equal(original[..3], registered[..3]);   // the BOM stays
        Assert.True(installer.IsRegistered(".claude-work"));

        Assert.True(installer.Deregister(".claude-work"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void An_mcpServers_that_is_not_an_object_is_never_replaced()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-work", "projects"));
        string path = CcConfig(home, ".claude-work");
        File.WriteAllText(path, "{\"mcpServers\": [\"theirs\"]}");
        byte[] before = File.ReadAllBytes(path);

        Assert.False(Make(home, sanduhr).Register(".claude-work").Ok);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
