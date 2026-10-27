using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 8 (R12; decisions 2 and 3): Reset's Claude Code part undoes every folder but the kept ones. A kept folder ends
/// byte for byte as it was, the files its entries run stay, and an undone folder loses only what still points at Sanduhr's files.
/// Real installers on a made-up profile under the temp folder.
/// </summary>
public sealed class ClaudeCodeResetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-ccreset-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_root, "home");
    private string SanduhrDir => Path.Combine(_root, "appdata", "Sanduhr");
    private string ExternalDir => Path.Combine(Profile, ".sanduhr");
    private string BinDir => Path.Combine(ExternalDir, "bin");
    private string Source => Path.Combine(_root, "app", "mods", "sanduhr-meters");
    private string Receipts => Path.Combine(SanduhrDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
    private const string Settings = "{\n  \"theme\": \"dark\"\n}\n";

    public ClaudeCodeResetTests()
    {
        Directory.CreateDirectory(Profile);
        Write(Source, ".claude-plugin/plugin.json", "{ \"name\": \"sanduhr-meters\", \"version\": \"0.3.0\" }");
        Write(Source, "hooks/hooks.json", "{ \"modules\": [\"./register.tsx\"] }");
        Write(Source, "hooks/register.tsx", "export {};");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void Write(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    /// <summary>A discovered home (it has projects\) with a settings.json of its own.</summary>
    private string Home(string name)
    {
        var dir = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(dir, "projects"));
        File.WriteAllText(Path.Combine(dir, "settings.json"), Settings);
        return dir;
    }

    private McpIntegrationInstaller Mcp() => new(Profile, SanduhrDir, externalDir: ExternalDir);

    /// <summary>All four of Sanduhr's entries in one folder, the way Settings installs them.</summary>
    private void InstallAll(string folder)
    {
        var integ = new ClaudeCodeIntegrations(Profile, SanduhrDir, Source, externalDir: ExternalDir);
        var meters = integ.Install(IntegrationKind.Meters, folder);
        Assert.True(meters.Ok, meters.Message);
        var hooks = integ.Install(IntegrationKind.Hooks, folder);
        Assert.True(hooks.Ok, hooks.Message);
        var statusline = new StatuslineInstaller(Profile, BinDir, null, SanduhrDir);
        Assert.True(statusline.InstallScript());
        Assert.True(statusline.Install(folder, StatuslineMode.Replace, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine));
        var mcp = Mcp();
        Assert.True(mcp.Register(Path.GetFileName(folder)).Ok);
        Directory.CreateDirectory(Path.GetDirectoryName(mcp.LauncherPath)!);
        File.WriteAllText(mcp.LauncherPath, "@echo off");
    }

    private ClaudeCodeResetScope Scope() =>
        new(Profile, SanduhrDir, ExternalDir, BinDir, ClaudeCodeFolders.Build(Profile, null, []), null, []);

    /// <summary>Every file under a folder, by path, with its bytes.</summary>
    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static void Same(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after, string what)
    {
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (file, bytes) in before)
            Assert.True(bytes.AsSpan().SequenceEqual(after[file]), $"{what}: {Path.GetFileName(file)} changed");
    }

    private void Clean(string folder)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "settings.json")))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Settings), json), $"{Path.GetFileName(folder)} still holds: {json.ToJsonString()}");
        Assert.False(Mcp().IsRegistered(Path.GetFileName(folder)));
    }

    [Fact]
    public void A_kept_folder_ends_byte_for_byte_and_keeps_its_receipts()
    {
        string undo = Home(".claude-undo"), keep = Home(".claude-keep");
        InstallAll(undo);
        InstallAll(keep);
        var before = Snapshot(keep);

        var failures = ClaudeCodeReset.Undo(Scope(), [keep]);

        Assert.Empty(failures);
        Same(before, Snapshot(keep), "the kept folder");
        Clean(undo);
        var left = new ReceiptStore(Receipts).Load().Records;
        Assert.NotEmpty(left);
        Assert.All(left, r => Assert.True(ClaudeCodeFolders.Same(r.Folder, keep), $"a receipt for {Path.GetFileName(r.Folder)} survived"));
    }

    [Fact]
    public void While_a_folder_is_kept_the_files_its_entries_run_stay()
    {
        string undo = Home(".claude-undo"), keep = Home(".claude-keep");
        InstallAll(undo);
        InstallAll(keep);

        ClaudeCodeReset.Undo(Scope(), [keep]);

        Assert.True(File.Exists(Mcp().LauncherPath), "the MCP launcher went");
        Assert.True(File.Exists(new StatuslineInstaller(Profile, BinDir).ScriptPath), "the statusline script went");
        Assert.True(Directory.EnumerateFileSystemEntries(Path.Combine(ExternalDir, "mods")).Any(), "the meters mod's copy went");
    }

    [Fact]
    public void A_kept_folder_matches_however_it_is_written()
    {
        string undo = Home(".claude-undo"), keep = Home(".claude-keep");
        InstallAll(undo);
        InstallAll(keep);
        var before = Snapshot(keep);

        ClaudeCodeReset.Undo(Scope(), [keep.ToUpperInvariant() + "\\"]);

        Same(before, Snapshot(keep), "the kept folder, written in capitals with a trailing slash");
        Clean(undo);
    }

    [Fact]
    public void Keeping_dot_claude_leaves_the_claude_json_beside_the_profile()
    {
        string dotClaude = Home(".claude"), undo = Home(".claude-undo");
        InstallAll(dotClaude);
        InstallAll(undo);
        string beside = Mcp().ConfigPathFor(".claude");   // .claude's MCP entry lives here, outside the folder
        Assert.True(File.Exists(beside));
        var before = Snapshot(dotClaude);
        var besideBytes = File.ReadAllBytes(beside);

        ClaudeCodeReset.Undo(Scope(), [dotClaude]);

        Same(before, Snapshot(dotClaude), ".claude");
        Assert.Equal(besideBytes, File.ReadAllBytes(beside));
        Assert.True(Mcp().IsRegistered(".claude"));
        Clean(undo);
    }

    /// <summary>Today's reset, pinned: it passes before and after this task.</summary>
    [Fact]
    public void With_nothing_kept_every_folder_is_undone_and_the_launcher_and_script_go()
    {
        string a = Home(".claude-a"), b = Home(".claude-b");
        InstallAll(a);
        InstallAll(b);

        Assert.Empty(ClaudeCodeReset.Undo(Scope(), []));

        Clean(a);
        Clean(b);
        Assert.False(File.Exists(Mcp().LauncherPath));
        Assert.False(File.Exists(new StatuslineInstaller(Profile, BinDir).ScriptPath));
    }

    /// <summary>Remove's rule, through Reset: what someone else put beside Sanduhr's entry stays. Passes before and after this task.</summary>
    [Fact]
    public void An_undone_folder_keeps_what_does_not_point_at_Sanduhr()
    {
        string undo = Home(".claude-undo");
        InstallAll(undo);
        string file = Path.Combine(undo, "settings.json");
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        string theirs = Path.Combine(_root, "theirs", "plugin");
        var env = root["env"]!.AsObject();
        env[ClaudeCodeIntegrations.PluginDirsKey] = env[ClaudeCodeIntegrations.PluginDirsKey]!.GetValue<string>()
            + ClaudeCodeIntegrations.PluginDirsSeparator + theirs;
        File.WriteAllText(file, root.ToJsonString());

        ClaudeCodeReset.Undo(Scope(), []);

        var after = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Equal(theirs, after["env"]?[ClaudeCodeIntegrations.PluginDirsKey]?.GetValue<string>());
        Assert.Null(after["statusLine"]);
        Assert.Null(after["hooks"]);
    }

    /// <summary>The person's own command chained inside Sanduhr's statusLine.</summary>
    private static string? Chain(string settings) =>
        StatuslineInstaller.TryParseCommand(JsonNode.Parse(File.ReadAllText(settings))!["statusLine"]?["command"]?.GetValue<string>())?.Chain;

    private static string Hash(string file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));

    /// <summary>P1: a settings.json that can't be read right now is a failure, and the folder's statusLine still runs the script.</summary>
    [Fact]
    public void A_folder_that_cant_be_read_keeps_its_statusline_and_the_script()
    {
        string locked = Home(".claude-locked"), other = Home(".claude-other");
        InstallAll(locked);
        InstallAll(other);
        string file = Path.Combine(locked, "settings.json");
        string before = Hash(file);
        string script = new StatuslineInstaller(Profile, BinDir).ScriptPath;

        IReadOnlyList<string> failures;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            failures = ClaudeCodeReset.Undo(Scope(), []);

        Assert.Contains("a statusline entry", failures);
        Assert.True(File.Exists(script), "the script went while a folder still runs it");
        Assert.Equal(before, Hash(file));
        Assert.Contains(new ReceiptStore(Receipts).Load().Records, r => r.Kind == IntegrationKind.Statusline && ClaudeCodeFolders.Same(r.Folder, locked));
    }

    /// <summary>P1: with receipts that can't be read, Remove would drop a Combine statusline's key and lose the person's own line.</summary>
    [Fact]
    public void Unreadable_receipts_keep_the_script_and_a_chained_line()
    {
        string home = Home(".claude-combine");
        string file = Path.Combine(home, "settings.json");
        File.WriteAllText(file, "{\n  \"theme\": \"dark\",\n  \"statusLine\": { \"type\": \"command\", \"command\": \"my-own-line.sh\" }\n}\n");
        var statusline = new StatuslineInstaller(Profile, BinDir, null, SanduhrDir);
        Assert.True(statusline.InstallScript());
        Assert.True(statusline.Install(home, StatuslineMode.Combine, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine));
        Assert.Equal("my-own-line.sh", Chain(file));
        File.WriteAllText(Receipts, "{ not a list");
        string before = Hash(file);

        ClaudeCodeReset.Undo(Scope(), []);

        Assert.Equal(before, Hash(file));
        Assert.Equal("my-own-line.sh", Chain(file));
        Assert.True(File.Exists(statusline.ScriptPath), "the script went with the receipts unreadable");
    }

    /// <summary>P3: keeping the glow hook alone leaves exactly the hook; the other kinds in that folder, and the files only they run, go.</summary>
    [Fact]
    public void Keeping_one_kind_leaves_that_kind_and_undoes_the_rest()
    {
        string home = Home(".claude-both");
        InstallAll(home);
        string file = Path.Combine(home, "settings.json");

        var failures = ClaudeCodeReset.UndoKeeping(Scope(), [new ResetFolder(home, [IntegrationKind.Hooks])]).Failures;

        Assert.Empty(failures);
        var after = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.NotNull(after["hooks"]);
        Assert.Null(after["statusLine"]);
        Assert.False(Mcp().IsRegistered(Path.GetFileName(home)));
        Assert.False(File.Exists(Mcp().LauncherPath), "the MCP launcher stayed with no MCP entry kept");
        Assert.False(File.Exists(new StatuslineInstaller(Profile, BinDir).ScriptPath), "the script stayed with no statusline kept");
        Assert.Contains(new ReceiptStore(Receipts).Load().Records, r => r.Kind == IntegrationKind.Hooks && ClaudeCodeFolders.Same(r.Folder, home));
        Assert.DoesNotContain(new ReceiptStore(Receipts).Load().Records, r => r.Kind != IntegrationKind.Hooks);
    }

    /// <summary>P3, the other way: keeping the statusline keeps the script, and the folder's other kinds are undone.</summary>
    [Fact]
    public void Keeping_the_statusline_keeps_the_script_and_undoes_the_rest()
    {
        string home = Home(".claude-both");
        InstallAll(home);

        Assert.Empty(ClaudeCodeReset.UndoKeeping(Scope(), [new ResetFolder(home, [IntegrationKind.Statusline])]).Failures);

        var after = JsonNode.Parse(File.ReadAllText(Path.Combine(home, "settings.json")))!;
        Assert.NotNull(after["statusLine"]);
        Assert.Null(after["hooks"]);
        Assert.True(File.Exists(new StatuslineInstaller(Profile, BinDir).ScriptPath));
        Assert.False(File.Exists(Mcp().LauncherPath));
    }

    /// <summary>An entry the person pointed somewhere else is not Sanduhr's: Reset leaves it, and the script still goes.</summary>
    [Fact]
    public void A_statusline_the_person_changed_to_point_elsewhere_is_left_alone()
    {
        string home = Home(".claude-mine");
        InstallAll(home);
        string file = Path.Combine(home, "settings.json");
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        root["statusLine"] = JsonNode.Parse("{ \"type\": \"command\", \"command\": \"their-line.sh\" }");
        File.WriteAllText(file, root.ToJsonString());

        ClaudeCodeReset.Undo(Scope(), []);

        Assert.Equal("their-line.sh", JsonNode.Parse(File.ReadAllText(file))!["statusLine"]!["command"]!.GetValue<string>());
    }

    /// <summary>~\.sanduhr is shared ground: another tool's files in it stay.</summary>
    [Fact]
    public void Other_tools_files_in_the_external_folder_stay()
    {
        string home = Home(".claude-a");
        InstallAll(home);
        string theirs = Path.Combine(ExternalDir, "time", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(theirs)!);
        File.WriteAllText(theirs, "{}");

        Assert.Empty(ClaudeCodeReset.Undo(Scope(), []));

        Assert.True(File.Exists(theirs));
    }

    [Fact]
    public void The_result_names_each_kind_that_came_out_and_where()
    {
        string undo = Home(".claude-undo");
        InstallAll(undo);

        var result = ClaudeCodeReset.UndoKeeping(Scope(), []);

        Assert.Empty(result.Failures);
        Assert.Empty(result.Left);
        Assert.True(result.McpOut && result.StatuslineOut);
        foreach (var kind in new[] { IntegrationKind.Meters, IntegrationKind.Hooks, IntegrationKind.Statusline, IntegrationKind.Mcp })
            Assert.Contains(result.Removed, r => r.Kind == kind && ClaudeCodeFolders.Same(r.Folder, undo));
    }

    [Fact]
    public void Unreadable_receipts_still_remove_the_mcp_entries_and_report_the_receipt_bound_kinds_as_left()
    {
        string undo = Home(".claude-undo");
        InstallAll(undo);
        File.WriteAllText(Receipts, "{ not a list");

        var result = ClaudeCodeReset.UndoKeeping(Scope(), []);

        Assert.False(Mcp().IsRegistered(".claude-undo"));
        Assert.True(result.McpOut);
        Assert.False(result.StatuslineOut);
        Assert.Contains(result.Removed, r => r.Kind == IntegrationKind.Mcp);
        Assert.DoesNotContain(result.Removed, r => r.Kind == IntegrationKind.Statusline);
    }
}
