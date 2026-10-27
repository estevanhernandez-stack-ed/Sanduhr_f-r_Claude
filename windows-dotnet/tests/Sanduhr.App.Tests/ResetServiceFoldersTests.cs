using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8 (R12; decisions 3 and 4; preflight P1 to P3): Reset's Claude Code part keeps Sanduhr's copies, its receipts and
/// the switches by kind while a folder is kept, and keeps everything while the receipts can't be read or an undo failed. Removes them
/// as before when every folder was undone. No window, no static: a scratch tree and a fake undo.
/// </summary>
public sealed class ResetServiceFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-resetsvc-" + Guid.NewGuid().ToString("N"));
    private readonly Paths _paths;

    public ResetServiceFoldersTests()
    {
        _paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"), Path.Combine(_root, "ext"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Ext => _paths.ExternalDirPath;
    private string ModsFile => Path.Combine(Ext, ResetPlan.ModsName, "sanduhr-meters", "x");
    private string McpFile => Path.Combine(Ext, ResetPlan.McpName, "v1", "x");
    private string McpLauncher => Path.Combine(Ext, ResetPlan.BinName, "sanduhr-mcp.cmd");
    private string StatuslineScript => Path.Combine(Ext, ResetPlan.BinName, "sanduhr-statusline.ps1");
    private string McpSettings => Path.Combine(Ext, ResetPlan.McpSettingsName);
    private string Receipts => ResetPlan.ReceiptsFile(_paths);

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    /// <summary>Sanduhr's copies of its Claude Code pieces, in the places the inventory lists them.</summary>
    private List<string> Seed()
    {
        var files = new List<string> { ModsFile, McpFile, McpLauncher, StatuslineScript, McpSettings };
        foreach (var f in files) Touch(f);
        Directory.CreateDirectory(Path.GetDirectoryName(Receipts)!);
        File.WriteAllText(Receipts, "[]");
        files.Add(Receipts);
        return files;
    }

    private sealed class Harness
    {
        public required ResetService Service { get; init; }
        public List<IReadOnlyCollection<ResetFolder>> Asked { get; } = [];
        public int McpCleared;
        public int StatuslineCleared;
    }

    private Harness Make(IReadOnlyList<string>? undoFailures = null)
    {
        Harness? h = null;
        var service = new ResetService(
            _paths,
            new AccountStore(new AppWindowFixture.MemoryCredentials()),
            new PublishTokenStore(new AppWindowFixture.MemoryCredentials()),
            keep => { h!.Asked.Add(keep); return Task.FromResult(undoFailures ?? []); },
            () => { },
            clearMcpSwitches: () => h!.McpCleared++,
            clearStatuslineSwitches: () => h!.StatuslineCleared++);
        return h = new Harness { Service = service };
    }

    private static readonly ResetChoices ClaudeCodeOnly = new(ClaudeCodeEntries: true, Accounts: false, History: false, Themes: false, Settings: false);

    private static ResetFolder Folder(string path, params IntegrationKind[] kinds) => new(path, kinds);

    private static readonly IntegrationKind[] All =
        [IntegrationKind.Statusline, IntegrationKind.Meters, IntegrationKind.Hooks, IntegrationKind.Mcp];

    [Fact]
    public async Task A_folder_kept_for_every_kind_keeps_every_copy_the_receipts_and_both_switches()
    {
        var files = Seed();
        var h = Make();
        var kept = Folder(Path.Combine(_root, "home", ".claude-keep"), All);

        var results = await h.Service.RunAsync(ClaudeCodeOnly, [kept]);

        Assert.True(Assert.Single(results).Ok);
        Assert.Same(kept, Assert.Single(Assert.Single(h.Asked)));   // the undo is told which folder and kinds stay
        Assert.All(files, f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} was deleted"));
        Assert.Equal(0, h.McpCleared);
        Assert.Equal(0, h.StatuslineCleared);
    }

    [Fact]
    public async Task Unreadable_receipts_keep_every_copy_and_switch_even_with_nothing_kept()
    {
        var files = Seed();
        File.WriteAllText(Receipts, "{ not a list");
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, []);

        Assert.All(files, f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} was deleted"));
        Assert.Equal(0, h.McpCleared);
        Assert.Equal(0, h.StatuslineCleared);
    }

    [Fact]
    public async Task A_set_aside_receipts_copy_counts_as_unreadable()
    {
        var files = Seed();
        File.WriteAllText(Receipts + ".unreadable", "{ not a list");
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, []);

        Assert.All(files, f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} was deleted"));
        Assert.Equal(0, h.McpCleared + h.StatuslineCleared);
    }

    [Fact]
    public async Task A_failed_statusline_undo_keeps_the_receipts_and_every_copy()
    {
        var files = Seed();
        var h = Make(["a statusline entry"]);

        var results = await h.Service.RunAsync(ClaudeCodeOnly, []);

        Assert.False(Assert.Single(results).Ok);
        Assert.All(files, f => Assert.True(File.Exists(f), $"{Path.GetFileName(f)} was deleted"));
        Assert.Equal(0, h.McpCleared + h.StatuslineCleared);
    }

    /// <summary>Today's reset, pinned: passes before and after this task.</summary>
    [Fact]
    public async Task With_nothing_kept_the_copies_receipts_and_both_switches_go_as_before()
    {
        var files = Seed();
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, []);

        Assert.All(files, f => Assert.False(File.Exists(f), $"{Path.GetFileName(f)} stayed"));
        Assert.Equal(1, h.McpCleared);
        Assert.Equal(1, h.StatuslineCleared);
    }

    /// <summary>P3: a kept glow-hook-only folder runs no Sanduhr file, so the MCP server, the mods and the switches go.</summary>
    [Fact]
    public async Task A_kept_hooks_only_folder_keeps_the_receipts_but_not_the_mcp_server_the_mods_or_the_switches()
    {
        Seed();
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, [Folder(Path.Combine(_root, "home", ".claude-glow"), IntegrationKind.Hooks)]);

        Assert.True(File.Exists(Receipts));
        Assert.False(File.Exists(McpFile));
        Assert.False(File.Exists(McpLauncher));
        Assert.False(File.Exists(McpSettings));
        Assert.False(File.Exists(StatuslineScript));
        Assert.False(File.Exists(ModsFile));
        Assert.Equal(1, h.McpCleared);
        Assert.Equal(1, h.StatuslineCleared);
    }

    [Fact]
    public async Task A_kept_mcp_folder_keeps_the_server_files_and_its_switch_but_not_the_mods_or_the_statusline_switch()
    {
        Seed();
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, [Folder(Path.Combine(_root, "home", ".claude-mcp"), IntegrationKind.Mcp)]);

        Assert.True(File.Exists(McpFile));
        Assert.True(File.Exists(McpLauncher));
        Assert.True(File.Exists(McpSettings));
        Assert.False(File.Exists(ModsFile));
        Assert.Equal(0, h.McpCleared);
        Assert.Equal(1, h.StatuslineCleared);
    }

    [Fact]
    public async Task A_kept_meters_folder_keeps_the_mods_and_the_statusline_switch_but_not_the_mcp_server()
    {
        Seed();
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, [Folder(Path.Combine(_root, "home", ".claude-meters"), IntegrationKind.Meters)]);

        Assert.True(File.Exists(ModsFile));
        Assert.True(File.Exists(Receipts));
        Assert.False(File.Exists(McpFile));
        Assert.False(File.Exists(McpLauncher));
        Assert.Equal(1, h.McpCleared);
        Assert.Equal(0, h.StatuslineCleared);
    }

    /// <summary>The shared folder: only Sanduhr's own names go, and the folder itself only when nothing else is in it.</summary>
    [Fact]
    public async Task Another_tools_file_in_the_shared_folder_survives_and_keeps_the_folder()
    {
        Seed();
        string decoy = Path.Combine(Ext, "time", "keep.txt");
        Touch(decoy);
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, []);

        Assert.True(File.Exists(decoy));
        Assert.False(File.Exists(McpFile));
    }

    [Fact]
    public async Task The_shared_folder_goes_when_nothing_is_left_in_it()
    {
        Seed();
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, []);

        Assert.False(Directory.Exists(Ext));
    }

    [Fact]
    public async Task A_kept_folder_keeps_the_shared_folder_too()
    {
        Seed();
        var h = Make();

        await h.Service.RunAsync(ClaudeCodeOnly, [Folder(Path.Combine(_root, "home", ".claude-keep"), All)]);

        Assert.True(Directory.Exists(Ext));
    }

    /// <summary>P2: Settings deletes mcp-settings.json and mcp_enabled, so a kept MCP entry is left unconsented. The dialog says so.</summary>
    [Fact]
    public void The_dialog_flag_for_a_kept_mcp_server_is_on_only_with_settings_ticked_and_an_mcp_folder_kept()
    {
        var mcp = Folder("a", IntegrationKind.Mcp, IntegrationKind.Hooks);
        var hooks = Folder("b", IntegrationKind.Hooks);
        var withSettings = new ResetChoices(Settings: true);
        var without = new ResetChoices(Settings: false);

        Assert.True(ResetService.KeptMcpLosesItsSettings(withSettings, [mcp]));
        Assert.True(ResetService.KeptMcpLosesItsSettings(withSettings, [hooks, mcp]));
        Assert.False(ResetService.KeptMcpLosesItsSettings(without, [mcp]));
        Assert.False(ResetService.KeptMcpLosesItsSettings(withSettings, [hooks]));
        Assert.False(ResetService.KeptMcpLosesItsSettings(withSettings, []));
    }

    [Fact]
    public async Task Ticking_settings_still_removes_mcp_settings_json_though_an_mcp_folder_is_kept()
    {
        Seed();
        var h = Make();
        var all = new ResetChoices(ClaudeCodeEntries: true, Accounts: false, History: false, Themes: false, Settings: true);

        await h.Service.RunAsync(all, [Folder(Path.Combine(_root, "home", ".claude-mcp"), IntegrationKind.Mcp)]);

        Assert.False(File.Exists(McpSettings));   // the Settings part's own list; the dialog line (Task 5) tells the person
        Assert.True(File.Exists(McpLauncher));
    }
}
