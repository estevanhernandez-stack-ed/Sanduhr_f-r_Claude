using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The meters mod and the glow hooks in a Claude Code folder's settings.json. Made-up homes under the temp folder only.</summary>
public sealed class ClaudeCodeIntegrationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-cci-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_root, "home");
    private string SanduhrDir => Path.Combine(_root, "appdata", "Sanduhr");
    private string Source => Path.Combine(_root, "app", "mods", "sanduhr-meters");
    private string Receipts => Path.Combine(SanduhrDir, "integrations", "installs.json");

    public ClaudeCodeIntegrationsTests()
    {
        Directory.CreateDirectory(Profile);
        Write(Source, ".claude-plugin/plugin.json", "{ \"name\": \"sanduhr-meters\", \"version\": \"0.3.0\" }");
        Write(Source, "hooks/hooks.json", "{ \"modules\": [\"./register.tsx\"] }");
        Write(Source, "hooks/register.tsx", "export {};");
        // What the build leaves out, in case a dev run points at the repo's folder.
        Write(Source, "hooks/register.test.tsx", "test");
        Write(Source, "hooks/meters.test.ts", "test");
        Write(Source, ".gitignore", "node_modules");
        Write(Source, ".claude-plugin/types/generated.d.ts", "declare const x: 1;");
        Touch(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
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

    /// <summary>Sets every source file's write time, which the stamp reads.</summary>
    private void Touch(DateTime utc)
    {
        foreach (var f in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, utc);
    }

    private ClaudeCodeIntegrations Sut(Action<string>? log = null) => new(Profile, SanduhrDir, Source, log);

    private string Folder(string name = ".claude")
    {
        var d = Path.Combine(Profile, name);
        Directory.CreateDirectory(Path.Combine(d, "projects"));
        return d;
    }

    private static string SettingsOf(string folder) => Path.Combine(folder, "settings.json");

    private static void Ok(IntegrationResult r) => Assert.True(r.Ok, r.Failure + ": " + r.Message);

    private static void Ok(ModUpdateResult r) => Assert.True(r.Ok, r.Failure + ": " + r.Message);

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[]? Bytes(string folder) => File.Exists(SettingsOf(folder)) ? File.ReadAllBytes(SettingsOf(folder)) : null;

    private static void Put(string folder, string? text)
    {
        if (text is null) File.Delete(SettingsOf(folder));
        else File.WriteAllBytes(SettingsOf(folder), B(text));
    }

    private static JsonObject Json(string folder) => (JsonObject)JsonNode.Parse(File.ReadAllBytes(SettingsOf(folder)))!;

    private static string Quote(string s) => JsonSerializer.Serialize(s);

    private static string Mod(ClaudeCodeIntegrations sut) => sut.ModPathFor(sut.CurrentModStamp()!);

    private static IEnumerable<string> FolderFiles(string folder)
        => Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).Where(n => n != "projects").OrderBy(n => n)!;

    // ------------------------------------------------------------------ mod files

    [Fact]
    public void Refresh_copies_the_shipped_files_under_a_stamp_and_is_idempotent()
    {
        var sut = Sut();
        var stamp = sut.CurrentModStamp();
        Assert.Equal("v0.3.0-20260101120000", stamp);
        var path = sut.RefreshModFiles();
        Assert.Equal(Path.Combine(SanduhrDir, "mods", stamp!, "sanduhr-meters"), path);
        Assert.True(File.Exists(Path.Combine(path!, ".claude-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(path!, "hooks", "hooks.json")));
        Assert.True(File.Exists(Path.Combine(path!, "hooks", "register.tsx")));
        Assert.False(File.Exists(Path.Combine(path!, "hooks", "register.test.tsx")));
        Assert.False(File.Exists(Path.Combine(path!, "hooks", "meters.test.ts")));
        Assert.False(File.Exists(Path.Combine(path!, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(path!, ".claude-plugin", "types")));

        var marker = Path.Combine(path!, "hooks", "marker.txt");
        File.WriteAllText(marker, "kept");
        Assert.Equal(path, sut.RefreshModFiles());
        Assert.True(File.Exists(marker)); // not copied over again
        Assert.Equal(new[] { stamp }, Directory.GetDirectories(Path.Combine(SanduhrDir, "mods")).Select(Path.GetFileName));
    }

    [Fact]
    public void A_newer_source_file_makes_a_new_stamp_and_keeps_the_old_folder()
    {
        var sut = Sut();
        var a = sut.RefreshModFiles();
        Touch(new DateTime(2026, 2, 1, 8, 30, 5, DateTimeKind.Utc));
        var b = sut.RefreshModFiles();
        Assert.NotEqual(a, b);
        Assert.EndsWith(@"v0.3.0-20260201083005\sanduhr-meters", b);
        Assert.True(Directory.Exists(a));
    }

    [Fact]
    public void Refresh_without_a_source_returns_null()
    {
        var sut = new ClaudeCodeIntegrations(Profile, SanduhrDir, Path.Combine(_root, "nope"));
        Assert.Null(sut.CurrentModStamp());
        Assert.Null(sut.RefreshModFiles());
        var res = sut.Install(IntegrationKind.Meters, Folder());
        Assert.False(res.Ok);
        Assert.Equal(IntegrationFailure.ModFilesMissing, res.Failure);
    }

    // ------------------------------------------------------------------ meters: byte-for-byte

    public static IEnumerable<object?[]> MetersInputs()
    {
        yield return new object?[] { null };
        yield return new object?[] { "{}" };
        yield return new object?[] { "{}\n" };
        yield return new object?[] { "{\n  \"model\": \"opus\",\n  \"env\": {\n    \"FOO\": \"1\",\n    \"BAR\": \"two\"\n  }\n}\n" };
        yield return new object?[] { "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"C:\\\\Users\\\\me\\\\other-mod\"\n  },\n  \"x\": 1\n}\n" };
        yield return new object?[] { "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"C:\\\\a;D:\\\\b\"\n  }\n}\n" };
        yield return new object?[] { "{\r\n  \"model\": \"opus\",\r\n  \"theme\": \"dark\"\r\n}\r\n" };
        yield return new object?[] { "{\r\n  \"env\": {\r\n    \"FOO\": \"1\"\r\n  }\r\n}\r\n" };
        yield return new object?[] { "{\"model\":\"opus\",\"env\":{\"A\":\"b\"}}" };
        yield return new object?[] { "{\"env\":{}}" };
        yield return new object?[] { "{\n  \"env\": {\n  }\n}\n" };
        yield return new object?[] { "{\n\t\"model\": \"opus\"\n}\n" };
        yield return new object?[] { "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"\"\n  }\n}\n" };
    }

    [Theory]
    [MemberData(nameof(MetersInputs))]
    public void Meters_install_then_remove_returns_the_original_bytes(string? original)
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, original);

        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Meters, folder));
        var res = sut.Install(IntegrationKind.Meters, folder);
        Assert.True(res.Ok, res.Message);
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Meters, folder));
        var dirs = (string)Json(folder)["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!;
        Assert.Contains(Mod(sut), dirs.Split(';'));
        Assert.True(File.Exists(Receipts));

        var removed = sut.Remove(IntegrationKind.Meters, folder);
        Assert.True(removed.Ok, removed.Message);
        Assert.Equal(original is null ? null : B(original), Bytes(folder));
        Assert.False(File.Exists(Receipts));
        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Meters, folder));
        Assert.Equal(Array.Empty<string>(), FolderFiles(folder).Where(n => n != "settings.json"));
    }

    [Fact]
    public void Meters_install_keeps_folders_already_listed_and_joins_with_a_semicolon()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"C:\\\\a;D:\\\\b\"\n  }\n}\n");
        Ok(sut.Install(IntegrationKind.Meters, folder));
        Assert.Equal(@"C:\a;D:\b;" + Mod(sut), (string)Json(folder)["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!);
        // A second install replaces nothing and adds nothing.
        var once = Bytes(folder);
        Ok(sut.Install(IntegrationKind.Meters, folder));
        Assert.Equal(once, Bytes(folder));
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(@"C:\a;D:\b", (string)Json(folder)["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!);
    }

    [Fact]
    public void Meters_remove_after_the_user_changed_the_list_takes_out_only_sanduhrs_entry()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{\n  \"model\": \"opus\"\n}\n");
        Ok(sut.Install(IntegrationKind.Meters, folder));
        // The user adds a folder of their own and a setting.
        var root = Json(folder);
        root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"] = @"C:\mine;" + Mod(sut);
        root["theme"] = "dark";
        File.WriteAllText(SettingsOf(folder), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Ok(sut.Remove(IntegrationKind.Meters, folder));
        var after = Json(folder);
        Assert.Equal(@"C:\mine", (string)after["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!);
        Assert.Equal("dark", (string)after["theme"]!);
        Assert.Equal("opus", (string)after["model"]!);
    }

    [Fact]
    public void Meters_remove_leaves_an_entry_that_is_not_sanduhrs_alone()
    {
        var sut = Sut();
        var folder = Folder();
        var text = "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"C:\\\\mine\"\n  }\n}\n";
        Put(folder, text);
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(text), Bytes(folder));
        // A copy of the mod elsewhere is the user's.
        var elsewhere = "{\n  \"env\": {\n    \"CLAUDE_CODE_PLUGIN_DIRS\": \"C:\\\\repo\\\\sanduhr-meters\"\n  }\n}\n";
        Put(folder, elsewhere);
        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Meters, folder));
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(elsewhere), Bytes(folder));
    }

    [Fact]
    public void Meters_is_outdated_when_the_entry_names_another_stamp_and_update_moves_it()
    {
        var sut = Sut();
        var folder = Folder();
        var original = "{\n  \"model\": \"opus\"\n}\n";
        Put(folder, original);
        Ok(sut.Install(IntegrationKind.Meters, folder));
        var first = sut.OwnModStateOf(folder);
        Assert.Equal("v0.3.0-20260101120000", first.Pinned);
        Assert.Equal(ModUpdateOutcome.UpToDate, sut.UpdateOwnMod(folder).Outcome);

        Touch(new DateTime(2026, 3, 3, 3, 3, 3, DateTimeKind.Utc));
        Assert.Equal(IntegrationStatus.Outdated, sut.Status(IntegrationKind.Meters, folder));
        var up = sut.UpdateOwnMod(folder);
        Assert.True(up.Ok, up.Message);
        Assert.Equal(ModUpdateOutcome.Updated, up.Outcome);
        Assert.Equal("v0.3.0-20260303030303", sut.OwnModStateOf(folder).Pinned);
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Meters, folder));
        Assert.Equal(ModUpdateOutcome.UpToDate, sut.UpdateOwnMod(folder).Outcome);
        // The old version's files stay for sessions that still load them.
        Assert.True(Directory.Exists(Path.Combine(SanduhrDir, "mods", "v0.3.0-20260101120000")));

        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(original), Bytes(folder));
    }

    [Fact]
    public void Update_without_an_entry_does_nothing()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        var up = sut.UpdateOwnMod(folder);
        Assert.True(up.Ok);
        Assert.Equal(ModUpdateOutcome.UpToDate, up.Outcome);
        Assert.Equal(B("{}"), Bytes(folder));
    }

    // ------------------------------------------------------------------ the switch

    [Theory]
    [InlineData("{\n  \"model\": \"opus\",\n  \"enabledPlugins\": {\n    \"helper@market\": true\n  }\n}\n")]
    [InlineData("{\n  \"model\": \"opus\"\n}\n")]
    [InlineData("{\"model\":\"opus\",\"enabledPlugins\":{}}")]
    [InlineData("{\n  \"enabledPlugins\": {\n  }\n}\n")]
    [InlineData("{\r\n  \"model\": \"opus\"\r\n}\r\n")]
    public void Off_on_off_cycles_end_byte_identical_and_remove_restores_the_original(string original)
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, original);
        Ok(sut.Install(IntegrationKind.Meters, folder));
        var installed = Bytes(folder);
        Assert.True(sut.OwnModStateOf(folder).IsOn);

        for (int i = 0; i < 3; i++)
        {
            Ok(sut.SwitchOwnMod(folder, false));
            var state = sut.OwnModStateOf(folder);
            Assert.True(state.Listed);
            Assert.False(state.IsOn);
            Assert.Equal(false, state.Inline);
            Assert.Equal(false, state.Switched);
            var off = Bytes(folder);
            Ok(sut.SwitchOwnMod(folder, false)); // Off again changes nothing
            Assert.Equal(off, Bytes(folder));

            Ok(sut.SwitchOwnMod(folder, true));
            Assert.Equal(installed, Bytes(folder));
            Assert.Null(sut.OwnModStateOf(folder).Switched);
        }

        Ok(sut.SwitchOwnMod(folder, false));
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(original), Bytes(folder));
        Assert.False(File.Exists(Receipts));
    }

    [Fact]
    public void On_in_a_folder_without_the_mod_pins_the_apps_version_and_remove_undoes_it()
    {
        var sut = Sut();
        var folder = Folder();
        var original = "{\n  \"model\": \"opus\"\n}\n";
        Put(folder, original);
        Ok(sut.SwitchOwnMod(folder, true));
        var state = sut.OwnModStateOf(folder);
        Assert.True(state.IsOn);
        Assert.Equal("v0.3.0-20260101120000", state.Pinned);
        Assert.True(state.HasReceipt);
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(original), Bytes(folder));
    }

    [Fact]
    public void A_users_own_enabledPlugins_false_is_respected_and_restored()
    {
        var sut = Sut();
        var folder = Folder();
        var original = "{\n  \"enabledPlugins\": {\n    \"sanduhr-meters@inline\": false,\n    \"helper@market\": true\n  }\n}\n";
        Put(folder, original);

        // On overrides the user's false with a true it records.
        Ok(sut.SwitchOwnMod(folder, true));
        var on = sut.OwnModStateOf(folder);
        Assert.True(on.IsOn);
        Assert.Equal(true, on.Inline);
        Assert.Equal(true, on.Switched);

        // Off puts the user's false back instead of writing a new one.
        Ok(sut.SwitchOwnMod(folder, false));
        var off = sut.OwnModStateOf(folder);
        Assert.False(off.IsOn);
        Assert.Equal(false, off.Inline);
        Assert.Null(off.Switched);
        Assert.False((bool)Json(folder)["enabledPlugins"]!["sanduhr-meters@inline"]!);

        // Back on, then remove: the file is the user's again.
        Ok(sut.SwitchOwnMod(folder, true));
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(original), Bytes(folder));
        Assert.False(File.Exists(Receipts));
    }

    [Fact]
    public void Off_when_the_user_already_set_false_writes_nothing()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{\n  \"enabledPlugins\": {\n    \"sanduhr-meters@inline\": false\n  }\n}\n");
        Ok(sut.SwitchOwnMod(folder, true));
        Ok(sut.SwitchOwnMod(folder, false));
        var off = Bytes(folder);
        Ok(sut.SwitchOwnMod(folder, false));
        Assert.Equal(off, Bytes(folder));
    }

    [Fact]
    public void Undoing_a_switch_someone_changed_since_leaves_their_value()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        Ok(sut.Install(IntegrationKind.Meters, folder));
        Ok(sut.SwitchOwnMod(folder, false));
        var root = Json(folder);
        root["enabledPlugins"]!["sanduhr-meters@inline"] = true; // the user turned it on themselves
        File.WriteAllText(SettingsOf(folder), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.True((bool)Json(folder)["enabledPlugins"]!["sanduhr-meters@inline"]!);
        Assert.Null(Json(folder)["env"]);
    }

    // ------------------------------------------------------------------ hooks

    private const string UserHooks =
        "{\n  \"model\": \"opus\",\n  \"hooks\": {\n    \"Notification\": [\n      {\n        \"hooks\": [\n          { \"type\": \"command\", \"command\": \"notify-send hi\" }\n        ]\n      }\n    ],\n    \"Stop\": [\n      {\n        \"matcher\": \"\",\n        \"hooks\": [\n          { \"type\": \"command\", \"command\": \"echo done\" }\n        ]\n      }\n    ],\n    \"PreToolUse\": []\n  }\n}\n";

    public static IEnumerable<object?[]> HooksInputs()
    {
        yield return new object?[] { null };
        yield return new object?[] { "{}" };
        yield return new object?[] { "{\n  \"model\": \"opus\"\n}\n" };
        yield return new object?[] { UserHooks };
        yield return new object?[] { UserHooks.Replace("\n", "\r\n") };
        yield return new object?[] { "{\n  \"hooks\": {}\n}\n" };
        yield return new object?[] { "{\n  \"hooks\": {\n  }\n}\n" };
        yield return new object?[] { "{\n  \"hooks\": {\n    \"PreToolUse\": [\n      { \"hooks\": [ { \"type\": \"command\", \"command\": \"x\" } ] }\n    ]\n  }\n}\n" };
        yield return new object?[] { "{\n  \"hooks\": {\n    \"Notification\": [],\n    \"Stop\": []\n  }\n}\n" };
        yield return new object?[] { "{\n  \"hooks\": {\n    \"Notification\": [\n    ]\n  },\n  \"x\": 1\n}\n" };
        yield return new object?[] { "{\"model\":\"opus\",\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"a\"}]}]}}" };
    }

    [Theory]
    [MemberData(nameof(HooksInputs))]
    public void Hooks_install_then_remove_returns_the_original_bytes(string? original)
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, original);
        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Hooks, folder));

        var res = sut.Install(IntegrationKind.Hooks, folder);
        Assert.True(res.Ok, res.Message);
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Hooks, folder));
        var hooks = (JsonObject)Json(folder)["hooks"]!;
        foreach (var name in new[] { "Notification", "Stop" })
        {
            var ours = ((JsonArray)hooks[name]!).Where(ClaudeCodeIntegrations.IsOurHookGroup).ToList();
            Assert.Single(ours);
            var hook = ours[0]!["hooks"]![0]!;
            Assert.Equal("command", (string)hook["type"]!);
            Assert.Equal("powershell", (string)hook["shell"]!);
            Assert.True((bool)hook["async"]!);
            Assert.Equal(5, (int)hook["timeout"]!);
        }
        var notification = ((JsonArray)hooks["Notification"]!).First(ClaudeCodeIntegrations.IsOurHookGroup)!;
        Assert.Equal("permission_prompt|idle_prompt|elicitation_dialog|agent_needs_input", (string)notification["matcher"]!);
        Assert.Contains("glow-waiting.signal", (string)notification["hooks"]![0]!["command"]!);
        var stop = ((JsonArray)hooks["Stop"]!).First(ClaudeCodeIntegrations.IsOurHookGroup)!;
        Assert.Null(stop["matcher"]);
        Assert.Contains("glow-done.signal", (string)stop["hooks"]![0]!["command"]!);

        // The user's own groups survive the install.
        if (original?.Contains("notify-send hi") == true)
            Assert.Contains(((JsonArray)hooks["Notification"]!), g => g?["hooks"]?[0]?["command"]?.ToString() == "notify-send hi");
        if (original?.Contains("echo done") == true)
            Assert.Contains(((JsonArray)hooks["Stop"]!), g => g?["hooks"]?[0]?["command"]?.ToString() == "echo done");

        // Installing again changes nothing.
        var once = Bytes(folder);
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        Assert.Equal(once, Bytes(folder));

        var removed = sut.Remove(IntegrationKind.Hooks, folder);
        Assert.True(removed.Ok, removed.Message);
        Assert.Equal(original is null ? null : B(original), Bytes(folder));
        Assert.False(File.Exists(Receipts));
        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Hooks, folder));
    }

    [Fact]
    public void A_users_group_that_also_runs_sanduhrs_command_is_theirs_and_never_removed()
    {
        var sut = Sut();
        var folder = Folder();
        var shared = new JsonObject
        {
            ["hooks"] = new JsonArray(
                new JsonObject { ["type"] = "command", ["command"] = "my-own-thing" },
                (JsonNode)ClaudeCodeIntegrationsTestAccess.OurHookCommand("Stop"))
        };
        var root = new JsonObject { ["hooks"] = new JsonObject { ["Stop"] = new JsonArray(shared) } };
        var original = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Put(folder, original);

        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Hooks, folder));
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        Assert.Equal(2, ((JsonArray)Json(folder)["hooks"]!["Stop"]!).Count);
        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        Assert.Equal(B(original), Bytes(folder));
    }

    [Fact]
    public void Hooks_remove_after_the_user_changed_things_takes_out_only_sanduhrs_groups()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{\n  \"model\": \"opus\"\n}\n");
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        var root = Json(folder);
        ((JsonArray)root["hooks"]!["Stop"]!).Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = "later" }) });
        root["theme"] = "dark";
        File.WriteAllText(SettingsOf(folder), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        var after = Json(folder);
        Assert.Equal("dark", (string)after["theme"]!);
        var stop = (JsonArray)after["hooks"]!["Stop"]!;
        Assert.Single(stop);
        Assert.Equal("later", (string)stop[0]!["hooks"]![0]!["command"]!);
        Assert.Null(after["hooks"]!["Notification"]);
    }

    [Fact]
    public void Hooks_in_another_form_are_outdated_and_install_rewrites_them_in_place()
    {
        var sut = Sut();
        var folder = Folder();
        var original = UserHooks;
        Put(folder, original);
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        // An older Sanduhr wrote a shorter timeout.
        var text = Encoding.UTF8.GetString(Bytes(folder)!).Replace("\"timeout\": 5", "\"timeout\": 9");
        File.WriteAllText(SettingsOf(folder), text);
        Assert.Equal(IntegrationStatus.Outdated, sut.Status(IntegrationKind.Hooks, folder));
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Hooks, folder));
        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        Assert.Equal(B(original), Bytes(folder));
    }

    [Fact]
    public void Hooks_and_meters_share_one_file_and_each_remove_leaves_the_other()
    {
        var sut = Sut();
        var folder = Folder();
        var original = "{\r\n  \"model\": \"opus\"\r\n}\r\n";
        Put(folder, original);
        Ok(sut.Install(IntegrationKind.Meters, folder));
        var meters = Bytes(folder);
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Meters, folder));
        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        Assert.Equal(meters, Bytes(folder));
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B(original), Bytes(folder));
    }

    // ------------------------------------------------------------------ concurrency and refusal

    [Fact]
    public void A_change_between_plan_and_commit_makes_the_install_start_over()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{\n  \"model\": \"opus\"\n}\n");
        int calls = 0;
        sut.BeforeCommit = file =>
        {
            if (++calls == 1) File.WriteAllText(file, "{\n  \"model\": \"opus\",\n  \"theme\": \"dark\"\n}\n"); // Claude Code wrote meanwhile
        };
        var res = sut.Install(IntegrationKind.Meters, folder);
        Assert.True(res.Ok, res.Message);
        Assert.Equal(2, calls);
        var root = Json(folder);
        Assert.Equal("dark", (string)root["theme"]!); // the other writer's change is kept
        Assert.Contains(Mod(sut), ((string)root["env"]!["CLAUDE_CODE_PLUGIN_DIRS"]!).Split(';'));

        sut.BeforeCommit = null;
        Ok(sut.Remove(IntegrationKind.Meters, folder));
        Assert.Equal(B("{\n  \"model\": \"opus\",\n  \"theme\": \"dark\"\n}\n"), Bytes(folder));
    }

    [Fact]
    public void A_file_that_keeps_changing_fails_after_three_tries_and_writes_nothing()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        int n = 0;
        sut.BeforeCommit = file => File.WriteAllText(file, "{\"n\":" + ++n + "}");
        var res = sut.Install(IntegrationKind.Hooks, folder);
        Assert.False(res.Ok);
        Assert.Equal(IntegrationFailure.WriteFailed, res.Failure);
        Assert.Equal(3, n);
        Assert.Equal(B("{\"n\":3}"), Bytes(folder));
        Assert.False(File.Exists(Receipts));
    }

    [Theory]
    [InlineData("{\"a\":")]
    [InlineData("[1, 2]")]
    [InlineData("// comment\n{}")]
    [InlineData("{\"a\": 1,}")]
    [InlineData("")]
    [InlineData("{\"env\": {}, \"env\": {}}")]
    [InlineData("{\"hooks\": {\"Stop\": []}, \"hooks\": {}}")]
    [InlineData("{\"env\": {\"A\": \"1\", \"A\": \"2\"}}")]
    public void An_unreadable_settings_file_is_never_written(string text)
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, text);
        var before = Bytes(folder);
        Assert.Equal(IntegrationStatus.Unreadable, sut.Status(IntegrationKind.Meters, folder));
        Assert.Equal(IntegrationStatus.Unreadable, sut.Status(IntegrationKind.Hooks, folder));
        Assert.False(sut.OwnModStateOf(folder).Readable);
        foreach (var res in new[]
        {
            sut.Install(IntegrationKind.Meters, folder),
            sut.Install(IntegrationKind.Hooks, folder),
            sut.SwitchOwnMod(folder, true),
            sut.SwitchOwnMod(folder, false),
        })
        {
            Assert.False(res.Ok);
            Assert.Equal(IntegrationFailure.Malformed, res.Failure);
            Assert.Contains("settings.json", res.Message);
            Assert.Contains(folder, res.Message);
        }
        Assert.False(sut.UpdateOwnMod(folder).Ok);
        Assert.Equal(before, Bytes(folder));
        Assert.False(File.Exists(Receipts));
        Assert.False(Directory.Exists(Path.Combine(SanduhrDir, "backups")));
        Assert.Equal(new[] { "settings.json" }, FolderFiles(folder));
    }

    [Fact]
    public void Remove_claims_only_the_exact_shape_and_leaves_a_users_near_copies()
    {
        var sut = Sut();
        var folder = Folder();
        var ours = ClaudeCodeIntegrations.HookCommand("glow-waiting.signal");
        var done = ClaudeCodeIntegrations.HookCommand("glow-done.signal");
        string Hook(string cmd, string shell = "powershell") => "{\"type\":\"command\",\"shell\":" + Quote(shell) + ",\"command\":" + Quote(cmd) + "}";
        var matcher = Quote(ClaudeCodeIntegrations.WaitingMatcher);
        var nearCopies = new[]
        {
            // extra hook beside ours
            "{\"matcher\":" + matcher + ",\"hooks\":[" + Hook(ours) + "," + Hook("echo hi") + "]}",
            // a different matcher
            "{\"matcher\":\"idle_prompt\",\"hooks\":[" + Hook(ours) + "]}",
            // no matcher on Notification
            "{\"hooks\":[" + Hook(ours) + "]}",
            // a matcher on Stop
            "{\"matcher\":\"x\",\"hooks\":[" + Hook(done) + "]}",
            // an edited command
            "{\"matcher\":" + matcher + ",\"hooks\":[" + Hook(ours + "; echo extra") + "]}",
            // another shell
            "{\"matcher\":" + matcher + ",\"hooks\":[" + Hook(ours, "bash") + "]}",
        };
        Put(folder, "{\"hooks\":{\"Notification\":[" + string.Join(",", nearCopies.Take(3)) + "],\"Stop\":[" + string.Join(",", nearCopies.Skip(3)) + "]}}");
        foreach (var g in nearCopies) Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(JsonNode.Parse(g)));
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        var hooks = (JsonObject)Json(folder)["hooks"]!;
        Assert.Equal(3, ((JsonArray)hooks["Notification"]!).Count);
        Assert.Equal(3, ((JsonArray)hooks["Stop"]!).Count);
        Assert.DoesNotContain(((JsonArray)hooks["Notification"]!), g => ClaudeCodeIntegrations.IsOurHookGroup(g));
    }

    [Fact]
    public void A_member_of_the_wrong_shape_is_refused_not_overwritten()
    {
        var sut = Sut();
        var folder = Folder();
        var cases = new (string Text, IntegrationKind Kind)[]
        {
            ("{\"env\": 5}", IntegrationKind.Meters),
            ("{\"env\": {\"CLAUDE_CODE_PLUGIN_DIRS\": [1]}}", IntegrationKind.Meters),
            ("{\"hooks\": []}", IntegrationKind.Hooks),
            ("{\"hooks\": {\"Stop\": {}}}", IntegrationKind.Hooks),
        };
        foreach (var (text, kind) in cases)
        {
            Put(folder, text);
            var res = sut.Install(kind, folder);
            Assert.False(res.Ok);
            Assert.Equal(IntegrationFailure.Malformed, res.Failure);
            Assert.Equal(B(text), Bytes(folder));
            Assert.Equal(IntegrationStatus.Unreadable, sut.Status(kind, folder));
        }
        // A switch into an enabledPlugins that is not an object.
        Put(folder, "{\"env\": {\"CLAUDE_CODE_PLUGIN_DIRS\": " + Quote(Mod(sut)) + "}, \"enabledPlugins\": 3}");
        var before = Bytes(folder);
        var sw = sut.SwitchOwnMod(folder, false);
        Assert.False(sw.Ok);
        Assert.Equal(IntegrationFailure.Malformed, sw.Failure);
        Assert.Equal(before, Bytes(folder));
    }

    [Fact]
    public void Backups_go_to_the_sanduhr_folder_never_beside_the_file()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{\n  \"model\": \"opus\"\n}\n");
        Ok(sut.Install(IntegrationKind.Meters, folder));
        Assert.Equal(new[] { "settings.json" }, FolderFiles(folder));
        var backups = Directory.GetFiles(Path.Combine(SanduhrDir, "backups"));
        Assert.Single(backups);
        Assert.Equal(B("{\n  \"model\": \"opus\"\n}\n"), File.ReadAllBytes(backups[0]));
    }

    [Fact]
    public void A_receipt_set_aside_stops_the_remove_and_changes_nothing()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        var installed = Bytes(folder);
        // A newer Sanduhr wrote a record this one cannot decode.
        var rec = "{\"kind\":\"hooks\",\"folder\":" + Quote(folder) + ",\"file\":" + Quote(SettingsOf(folder)) + ",\"createdFile\":\"yes\"}";
        File.WriteAllText(Receipts, "[" + rec + "]");
        var res = sut.Remove(IntegrationKind.Hooks, folder);
        Assert.False(res.Ok);
        Assert.Equal(IntegrationFailure.ReceiptSetAside, res.Failure);
        Assert.Equal(installed, Bytes(folder));
        Assert.Contains("createdFile", File.ReadAllText(Receipts)); // kept byte for byte
    }

    [Fact]
    public void Logs_carry_the_operation_and_exception_type_never_a_path()
    {
        var logs = new List<string>();
        var sut = Sut(logs.Add);
        var folder = Folder();
        Put(folder, "{}");
        var path = Path.Combine(SanduhrDir, "mods");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "a file where the mods folder should be");
        Assert.False(sut.Install(IntegrationKind.Meters, folder).Ok);
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.DoesNotContain(_root, l));
    }

    // ------------------------------------------------------------------ undo all and prune

    [Fact]
    public void UndoAll_removes_both_kinds_in_every_folder_and_reports_each()
    {
        var sut = Sut();
        var a = Folder(".claude");
        var b = Folder(".claude-work");
        var textA = "{\n  \"model\": \"opus\"\n}\n";
        Put(a, textA);
        Put(b, null);
        Ok(sut.Install(IntegrationKind.Meters, a));
        Ok(sut.Install(IntegrationKind.Hooks, a));
        Ok(sut.Install(IntegrationKind.Meters, b));
        Ok(sut.Install(IntegrationKind.Hooks, b));
        Ok(sut.SwitchOwnMod(a, false));

        var reports = sut.UndoAll();
        Assert.Equal(4, reports.Count);
        Assert.All(reports, r => Assert.True(r.Ok, r.Message));
        Assert.Equal(2, reports.Count(r => r.Kind == IntegrationKind.Meters));
        Assert.Equal(2, reports.Count(r => r.Kind == IntegrationKind.Hooks));
        Assert.Equal(B(textA), Bytes(a));
        Assert.Null(Bytes(b));
        Assert.False(File.Exists(Receipts));
        Assert.Empty(sut.UndoAll());
    }

    [Fact]
    public void UndoAll_with_folders_leaves_the_others()
    {
        var sut = Sut();
        var a = Folder(".claude");
        var b = Folder(".claude-work");
        Put(a, "{}");
        Put(b, "{}");
        Ok(sut.Install(IntegrationKind.Hooks, a));
        Ok(sut.Install(IntegrationKind.Hooks, b));
        var inB = Bytes(b);
        var reports = sut.UndoAll([a]);
        Assert.Single(reports);
        Assert.Equal(B("{}"), Bytes(a));
        Assert.Equal(inB, Bytes(b));
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Hooks, b));
    }

    [Fact]
    public void UndoAll_reports_a_failing_receipt_and_keeps_going()
    {
        var sut = Sut();
        var a = Folder(".claude");
        var b = Folder(".claude-work");
        Put(a, "{}");
        Put(b, "{}");
        Ok(sut.Install(IntegrationKind.Hooks, a));
        Ok(sut.Install(IntegrationKind.Hooks, b));
        var broken = "{\"a\":"; // the user broke a's file
        Put(a, broken);
        var reports = sut.UndoAll();
        Assert.Equal(2, reports.Count);
        Assert.Single(reports, r => !r.Ok);
        Assert.Equal(B(broken), Bytes(a));
        Assert.Equal(B("{}"), Bytes(b));
    }

    [Fact]
    public void Prune_deletes_unreferenced_stamps_and_keeps_the_ones_a_receipt_or_a_settings_file_names()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        Ok(sut.Install(IntegrationKind.Meters, folder));
        var pinned = sut.OwnModStateOf(folder).Pinned!;

        Touch(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var orphan = sut.RefreshModFiles()!; // a version nothing names
        Touch(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var listed = sut.RefreshModFiles()!; // named by a settings.json of the user's, no receipt
        var other = Folder(".claude-other");
        Put(other, "{\"env\":{\"CLAUDE_CODE_PLUGIN_DIRS\":" + Quote(listed) + "}}");

        // The receipt still pins the first stamp although the user's file no longer lists it.
        Put(folder, "{}");
        Directory.CreateDirectory(Path.Combine(SanduhrDir, "mods", ".staging-leftover"));

        var removed = sut.PruneModFiles();
        Assert.Equal(2, removed);
        var left = Directory.GetDirectories(Path.Combine(SanduhrDir, "mods")).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Contains(pinned, left);
        Assert.Contains(Path.GetFileName(Path.GetDirectoryName(listed)), left);
        Assert.DoesNotContain(Path.GetFileName(Path.GetDirectoryName(orphan)), left);
        Assert.DoesNotContain(".staging-leftover", left);
    }

    [Fact]
    public void Prune_keeps_everything_while_a_receipt_cannot_be_read()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        var path = sut.RefreshModFiles()!;
        Directory.CreateDirectory(Path.GetDirectoryName(Receipts)!);
        File.WriteAllText(Receipts, "[{\"kind\":\"meters\",\"folder\":1}]");
        Assert.Equal(0, sut.PruneModFiles());
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Prune_skips_a_folder_that_is_locked()
    {
        var sut = Sut();
        var path = sut.RefreshModFiles()!;
        using var held = new FileStream(Path.Combine(path, "hooks", "hooks.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(0, sut.PruneModFiles());
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Remove_uses_the_file_the_receipt_names()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, "{}");
        Ok(sut.Install(IntegrationKind.Hooks, folder));
        // The folder is named by another spelling (trailing slash, other case).
        var other = folder.ToUpperInvariant() + Path.DirectorySeparatorChar;
        Ok(sut.Remove(IntegrationKind.Hooks, other));
        Assert.Equal(B("{}"), Bytes(folder));
        Assert.False(File.Exists(Receipts));
    }
}

/// <summary>Builds a Sanduhr hook command the way the installer writes it, for the "theirs" test.</summary>
internal static class ClaudeCodeIntegrationsTestAccess
{
    public static JsonNode OurHookCommand(string eventName)
    {
        var group = ClaudeCodeIntegrations.HookGroup(eventName);
        var ov = (JsonEdit.ObjectValue)group;
        var hooks = (JsonEdit.ArrayValue)ov.Pairs.First(p => p.Key == "hooks").Value;
        var hook = (JsonEdit.ObjectValue)hooks.Items[0];
        var o = new JsonObject();
        foreach (var p in hook.Pairs)
            o[p.Key] = p.Value switch
            {
                JsonEdit.StringValue s => JsonValue.Create(s.Text),
                JsonEdit.BoolValue b => JsonValue.Create(b.Flag),
                JsonEdit.IntValue i => JsonValue.Create(i.Number),
                _ => throw new InvalidOperationException(),
            };
        return o;
    }
}
