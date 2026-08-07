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
        Assert.Single(Directory.GetFiles(ccDir, ".claude.json.sanduhr-backup-*"));
        Assert.False(File.Exists(CcConfig(home, ".claude-personal") + ".tmp"));
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
    public void DetectCcHomes_finds_the_known_set_in_stable_order()
    {
        using var home = new TempDir();
        using var sanduhr = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        Assert.Equal(new[] { ".claude", ".claude-personal" }, Make(home, sanduhr).DetectCcHomes());
    }
}
