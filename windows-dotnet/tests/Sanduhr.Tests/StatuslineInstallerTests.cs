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
    public void DetectCcHomes_finds_discovered_homes_in_stable_order()
    {
        // Discover requires projects/ (or the home's .claude.json) for a folder to count.
        using var home = new TempDir();
        using var bin = new TempDir();
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-personal", "projects"));
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude", "projects"));
        Assert.Equal(new[] { ".claude", ".claude-personal" }, Make(home, bin).DetectCcHomes());
    }

    [Fact]
    public void DetectCcHomes_finds_a_third_home_and_skips_backup_copies()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        foreach (var name in new[] { ".claude", ".claude-work", ".claude-backup-1", ".claude-work.config-backup-20261001" })
            Directory.CreateDirectory(Path.Combine(home.Path, name, "projects"));
        Assert.Equal(new[] { ".claude", ".claude-work" }, Make(home, bin).DetectCcHomes());
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

/// <summary>Stage 4 (4.4): the installer on the splice engine, with receipts and exact undo.</summary>
public class StatuslineInstallerSpliceTests
{
    private sealed class Rig : IDisposable
    {
        public TempDir Home { get; } = new();
        public TempDir Bin { get; } = new();
        public TempDir Data { get; } = new();
        public StatuslineInstaller Installer { get; }
        public List<string> Log { get; } = [];
        public string Settings => Path.Combine(Home.Path, ".claude", "settings.json");
        public string Receipts => Path.Combine(Data.Path, "integrations", "installs.json");

        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Home.Path, ".claude"));
            Installer = new StatuslineInstaller(Home.Path, Bin.Path, Log.Add, Data.Path);
            Installer.InstallScript();
        }

        public void Write(string text) => File.WriteAllBytes(Settings, new System.Text.UTF8Encoding(false).GetBytes(text));
        public byte[] Bytes() => File.ReadAllBytes(Settings);
        public string Text() => System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Settings));

        public void Dispose() { Home.Dispose(); Bin.Dispose(); Data.Dispose(); }
    }

    private static string Settings(string eol, string statusLine)
        => "{" + eol + "  \"model\": \"opus\"," + eol + "  \"statusLine\": " + statusLine + "," + eol
           + "  \"env\": {" + eol + "    \"A\": \"1\"" + eol + "  }" + eol + "}" + eol;

    private const string TheirsLf = "{\n    \"type\": \"command\",\n    \"command\": \"~/bin/mine.sh \\u00e9\",\n    \"padding\": 2\n  }";

    [Theory]
    [InlineData("\n", StatuslineMode.Replace)]
    [InlineData("\r\n", StatuslineMode.Replace)]
    [InlineData("\n", StatuslineMode.Combine)]
    [InlineData("\r\n", StatuslineMode.Combine)]
    public void Remove_restores_their_statusline_byte_for_byte(string eol, StatuslineMode mode)
    {
        using var rig = new Rig();
        var theirs = TheirsLf.Replace("\n", eol);
        var original = Settings(eol, theirs);
        rig.Write(original);

        Assert.True(rig.Installer.Install(".claude", mode, StatuslineInstaller.DefaultSegments, "line"), string.Join("; ", rig.Log));
        Assert.NotEqual(original, rig.Text());
        Assert.Equal(StatuslineStatus.Installed, rig.Installer.Status(".claude"));
        Assert.Contains("\"model\": \"opus\"", rig.Text()); // everything else untouched

        Assert.True(rig.Installer.Remove(".claude"));
        Assert.Equal(original, rig.Text());
        Assert.Equal(StatuslineStatus.Other, rig.Installer.Status(".claude"));
        Assert.Equal("~/bin/mine.sh \u00e9", rig.Installer.TheirCommand(".claude"));
    }

    [Fact]
    public void Combine_chains_their_command_as_base64_and_keeps_it_through_a_reinstall()
    {
        using var rig = new Rig();
        rig.Write(Settings("\n", TheirsLf));
        Assert.Equal("~/bin/mine.sh \u00e9", rig.Installer.TheirCommand(".claude"));

        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Combine, ["weekly", "session"], "same"));
        var parsed = StatuslineInstaller.TryParseCommand(CommandIn(rig));
        Assert.NotNull(parsed);
        Assert.Equal("~/bin/mine.sh \u00e9", parsed!.Chain);
        Assert.Equal("same", parsed.Join);
        Assert.Equal(new[] { "session", "weekly" }, parsed.Segments);
        Assert.Null(rig.Installer.TheirCommand(".claude")); // ours now

        // An update of our own entry keeps what the first install recorded: a replace in between
        // does not lose theirs, and combining again chains it back.
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Null(StatuslineInstaller.TryParseCommand(CommandIn(rig))!.Chain);
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Combine, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Equal("~/bin/mine.sh \u00e9", StatuslineInstaller.TryParseCommand(CommandIn(rig))!.Chain);

        Assert.True(rig.Installer.Remove(".claude"));
        Assert.Equal(Settings("\n", TheirsLf), rig.Text());
    }

    [Fact]
    public void Install_without_their_entry_then_Remove_returns_the_file()
    {
        using var rig = new Rig();
        var original = "{\r\n  \"model\": \"opus\"\r\n}\r\n";
        rig.Write(original);
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Combine, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Contains("statusLine", rig.Text());
        Assert.DoesNotContain("-Chain", rig.Text()); // nothing to combine with
        Assert.True(rig.Installer.Remove(".claude"));
        Assert.Equal(original, rig.Text());
    }

    [Fact]
    public void A_file_Sanduhr_created_is_deleted_again()
    {
        using var rig = new Rig();
        Assert.False(File.Exists(rig.Settings));
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        Assert.True(File.Exists(rig.Settings));
        Assert.True(rig.Installer.Remove(".claude"));
        Assert.False(File.Exists(rig.Settings));
        Assert.Equal(StatuslineStatus.NotInstalled, rig.Installer.Status(".claude"));
    }

    [Fact]
    public void Remove_leaves_an_entry_the_user_changed_alone_and_drops_the_receipt()
    {
        using var rig = new Rig();
        rig.Write(Settings("\n", TheirsLf));
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        var changed = Settings("\n", "{\"type\":\"command\",\"command\":\"something-new.exe\"}");
        rig.Write(changed);

        Assert.True(rig.Installer.Remove(".claude"));
        Assert.Equal(changed, rig.Text());
        Assert.False(File.Exists(rig.Receipts)); // the stale receipt is gone
    }

    [Fact]
    public void Install_writes_the_backup_and_receipt_in_the_sanduhr_folder_not_beside_the_file()
    {
        using var rig = new Rig();
        rig.Write(Settings("\n", TheirsLf));
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Combine, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Single(Directory.GetFiles(Path.Combine(rig.Data.Path, "backups")));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(Path.GetDirectoryName(rig.Settings)!).Select(Path.GetFileName));
        var receipts = File.ReadAllText(rig.Receipts).Replace(" ", "");
        Assert.Contains("\"kind\":\"statusline\"", receipts);
        Assert.Contains("\"mode\":\"combine\"", receipts);
    }

    [Theory]
    [InlineData("{\"statusLine\": {}, \"statusLine\": {}}")]
    [InlineData("{\"env\": {}, \"env\": {}}")]
    public void A_settings_file_with_duplicate_keys_is_unreadable_and_never_written(string text)
    {
        using var rig = new Rig();
        rig.Write(text);
        Assert.Equal(StatuslineStatus.Unreadable, rig.Installer.Status(".claude"));
        Assert.False(rig.Installer.Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        Assert.False(rig.Installer.Install(".claude", StatuslineMode.Combine, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Equal(text, rig.Text());
        Assert.False(Directory.Exists(Path.Combine(rig.Data.Path, "backups")));
    }

    [Fact]
    public void A_malformed_settings_file_is_never_written()
    {
        using var rig = new Rig();
        rig.Write("{ definitely not json");
        Assert.Equal(StatuslineStatus.Unreadable, rig.Installer.Status(".claude"));
        Assert.False(rig.Installer.Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Equal("{ definitely not json", rig.Text());
        Assert.False(Directory.Exists(Path.Combine(rig.Data.Path, "backups")));
    }

    [Fact]
    public void Status_tells_installed_from_outdated_from_other()
    {
        using var rig = new Rig();
        Assert.Equal(StatuslineStatus.NotInstalled, rig.Installer.Status(".claude"));
        rig.Write(Settings("\n", TheirsLf));
        Assert.Equal(StatuslineStatus.Other, rig.Installer.Status(".claude"));
        Assert.True(rig.Installer.Install(".claude", StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, "line"));
        Assert.Equal(StatuslineStatus.Installed, rig.Installer.Status(".claude"));
        File.WriteAllText(rig.Installer.ScriptPath, "# an older script");
        Assert.Equal(StatuslineStatus.Outdated, rig.Installer.Status(".claude"));
        Assert.True(rig.Installer.IsRegistered(".claude"));
    }

    [Fact]
    public void Register_and_Deregister_are_wrappers_over_Install_and_Remove()
    {
        using var rig = new Rig();
        rig.Write(Settings("\r\n", TheirsLf.Replace("\n", "\r\n")));
        var original = rig.Text();
        Assert.True(rig.Installer.Register(".claude"));
        Assert.True(rig.Installer.IsRegistered(".claude"));
        Assert.Equal(rig.Installer.BuildCommand(), CommandIn(rig));
        Assert.True(rig.Installer.Deregister(".claude"));
        Assert.Equal(original, rig.Text());
    }

    // -- IsOurs parsing -------------------------------------------------------

    [Fact]
    public void Commands_round_trip_through_the_parser()
    {
        using var home = new TempDir();
        using var bin = new TempDir();
        var installer = new StatuslineInstaller(home.Path, bin.Path);
        var cases = new (string? Chain, string Join, string[] Segments)[]
        {
            (null, "line", ["session", "weekly", "resets"]),
            (null, "line", ["weekly"]),
            ("bash ~/s.sh | head -1", "same", ["session", "resets"]),
            ("echo \"h\u00e9llo\" ; date", "line", ["session", "weekly", "resets"]),
        };
        foreach (var (chain, join, segments) in cases)
        {
            var parsed = StatuslineInstaller.TryParseCommand(installer.BuildCommand(chain, join, segments));
            Assert.NotNull(parsed);
            Assert.Equal(chain, parsed!.Chain);
            Assert.Equal(chain is null ? "line" : join, parsed.Join);
            Assert.Equal(segments, parsed.Segments);
            Assert.Equal(installer.ScriptPath, parsed.ScriptPath);
        }
        // Unknown names drop out, and what is left being the default writes no -Mine.
        Assert.DoesNotContain("-Mine", installer.BuildCommand(null, "line", ["weekly", "resets", "session", "bogus"]));
        Assert.Contains("-Mine weekly", installer.BuildCommand(null, "line", ["weekly"]));
    }

    [Theory]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\x\\sanduhr-statusline.ps1\" ; evil")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\x\\sanduhr-statusline.ps1\" | tee out")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\x\\sanduhr-statusline.ps1\" -Extra 1")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\x\\sanduhr-statusline.ps1\" -Chain not-base64 -Join line")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\x\\sanduhr-statusline.ps1\" -Mine weekly,session")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\x\\sanduhr-statusline.ps1.bak\"")]
    [InlineData("echo \"C:\\x\\sanduhr-statusline.ps1\"")]
    [InlineData("my-wrapper.cmd C:\\x\\sanduhr-statusline.ps1")]
    [InlineData("powershell -File \"C:\\x\\sanduhr-statusline.ps1\"")]
    [InlineData("")]
    public void A_lookalike_command_is_not_ours(string command)
        => Assert.Null(StatuslineInstaller.TryParseCommand(command));

    [Fact]
    public void A_lookalike_in_settings_is_other_and_never_removed()
    {
        using var rig = new Rig();
        var theirs = "{\"type\":\"command\",\"command\":\"powershell -NoProfile -ExecutionPolicy Bypass -File \\\"C:\\\\x\\\\sanduhr-statusline.ps1\\\" ; mine\"}";
        var original = Settings("\n", theirs);
        rig.Write(original);
        Assert.Equal(StatuslineStatus.Other, rig.Installer.Status(".claude"));
        Assert.True(rig.Installer.Remove(".claude"));
        Assert.Equal(original, rig.Text());
    }

    private static string CommandIn(Rig rig)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(rig.Bytes());
        return doc.RootElement.GetProperty("statusLine").GetProperty("command").GetString()!;
    }
}
