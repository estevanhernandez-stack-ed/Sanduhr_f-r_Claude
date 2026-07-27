using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class StatuslineInstallerTests
{
    private static StatuslineInstaller Make(TempDir home, TempDir bin)
        => new(home.Path, bin.Path);

    private static string CcSettingsPath(TempDir home, string ccHome)
        => Path.Combine(home.Path, ccHome, "settings.json");

    // -- home detection -------------------------------------------------------

    [Fact]
    public void DetectCcHomes_returns_empty_when_none_exist()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        Assert.Empty(Make(home, bin).DetectCcHomes());
    }

    [Fact]
    public void DetectCcHomes_finds_both_known_homes_in_stable_order()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        Assert.Equal(new[] { ".claude", ".claude-personal" }, Make(home, bin).DetectCcHomes());
    }

    // -- script install -------------------------------------------------------

    [Fact]
    public void InstallScript_writes_the_script_with_a_utf8_bom()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var installer = Make(home, bin);
        Assert.True(installer.InstallScript());
        Assert.True(File.Exists(installer.ScriptPath));

        // PS 5.1 needs the BOM to decode the non-ASCII separator correctly.
        byte[] head = File.ReadAllBytes(installer.ScriptPath)[..3];
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, head);
        Assert.Contains("snapshot.json", File.ReadAllText(installer.ScriptPath));
    }

    [Fact]
    public void InstallScript_is_idempotent_and_acts_as_the_update_channel()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var installer = Make(home, bin);
        Assert.True(installer.InstallScript());
        File.WriteAllText(installer.ScriptPath, "# stale drifted copy");
        Assert.True(installer.InstallScript()); // re-install refreshes the body
        Assert.Contains("snapshot.json", File.ReadAllText(installer.ScriptPath));
    }

    [Fact]
    public void RemoveScript_deletes_and_tolerates_absence()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var installer = Make(home, bin);
        installer.InstallScript();
        installer.RemoveScript();
        Assert.False(File.Exists(installer.ScriptPath));
        installer.RemoveScript(); // no-op, not a throw
    }

    // -- registration ---------------------------------------------------------

    [Fact]
    public void Register_creates_settings_json_when_missing()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal"));
        var installer = Make(home, bin);

        Assert.True(installer.Register(".claude-personal"));
        Assert.True(installer.IsRegistered(".claude-personal"));

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcSettingsPath(home, ".claude-personal")))!;
        var sl = (JsonObject)root["statusLine"]!;
        Assert.Equal("command", (string?)sl["type"]);
        Assert.Contains("sanduhr-statusline.ps1", (string?)sl["command"]);
    }

    [Fact]
    public void Register_preserves_unknown_keys_and_takes_a_backup()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var ccDir = Path.Combine(home.Path, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(CcSettingsPath(home, ".claude"),
            """{"model":"opus","hooks":{"PostToolUse":[{"matcher":"git commit"}]}}""");

        var installer = Make(home, bin);
        Assert.True(installer.Register(".claude"));

        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcSettingsPath(home, ".claude")))!;
        Assert.Equal("opus", (string?)root["model"]);                    // untouched
        Assert.NotNull(root["hooks"]);                                    // untouched
        Assert.NotNull(root["statusLine"]);                               // added
        Assert.Single(Directory.GetFiles(ccDir, "settings.json.sanduhr-backup-*"));
        Assert.False(File.Exists(CcSettingsPath(home, ".claude") + ".tmp"));
    }

    [Fact]
    public void Register_refuses_to_touch_a_malformed_settings_json()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var ccDir = Path.Combine(home.Path, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(CcSettingsPath(home, ".claude"), "{ definitely not json");

        var installer = Make(home, bin);
        Assert.False(installer.Register(".claude"));
        Assert.Equal("{ definitely not json", File.ReadAllText(CcSettingsPath(home, ".claude")));
        Assert.Empty(Directory.GetFiles(ccDir, "settings.json.sanduhr-backup-*"));
    }

    // -- deregistration -------------------------------------------------------

    [Fact]
    public void Deregister_removes_only_our_entry_and_keeps_the_rest()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        var installer = Make(home, bin);
        File.WriteAllText(CcSettingsPath(home, ".claude"), """{"model":"opus"}""");
        installer.Register(".claude");

        Assert.True(installer.Deregister(".claude"));
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcSettingsPath(home, ".claude")))!;
        Assert.Null(root["statusLine"]);
        Assert.Equal("opus", (string?)root["model"]);
        Assert.False(installer.IsRegistered(".claude"));
    }

    [Fact]
    public void Deregister_leaves_a_foreign_statusline_alone()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        File.WriteAllText(CcSettingsPath(home, ".claude"),
            """{"statusLine":{"type":"command","command":"my-own-statusline.exe"}}""");

        var installer = Make(home, bin);
        Assert.True(installer.Deregister(".claude"));   // end state: nothing of ours present
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(CcSettingsPath(home, ".claude")))!;
        Assert.Equal("my-own-statusline.exe", (string?)root["statusLine"]?["command"]);
    }

    [Fact]
    public void Deregister_tolerates_a_missing_settings_file()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        Assert.True(Make(home, bin).Deregister(".claude"));
    }

    [Fact]
    public void BuildCommand_pins_the_invocation_to_the_installed_script()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var installer = Make(home, bin);
        Assert.Contains($"\"{installer.ScriptPath}\"", installer.BuildCommand());
        Assert.Contains("-NoProfile", installer.BuildCommand());
    }
}
