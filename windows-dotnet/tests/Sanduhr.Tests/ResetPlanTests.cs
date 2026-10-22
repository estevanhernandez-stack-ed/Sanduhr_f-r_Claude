using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ResetPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-reset-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private Paths Make() => new(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"));

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Steps_run_in_the_documented_order()
    {
        Assert.Equal(
            [ResetPart.ClaudeCodeEntries, ResetPart.Accounts, ResetPart.History, ResetPart.Themes, ResetPart.Settings],
            ResetPlan.Steps(new ResetChoices()));
    }

    [Fact]
    public void Unchecked_parts_are_skipped_and_order_holds()
    {
        var steps = ResetPlan.Steps(new ResetChoices(ClaudeCodeEntries: false, History: false));
        Assert.Equal([ResetPart.Accounts, ResetPart.Themes, ResetPart.Settings], steps);
        Assert.Empty(ResetPlan.Steps(new ResetChoices(false, false, false, false, false)));
    }

    [Fact]
    public async Task A_throwing_step_fails_alone_and_the_rest_run_in_order()
    {
        var ran = new List<ResetPart>();
        var results = await ResetPlan.RunAsync(ResetPlan.Steps(new ResetChoices()), part =>
        {
            ran.Add(part);
            if (part == ResetPart.Accounts)
                throw new IOException(@"C:\Users\someone\secret-label");
            return Task.FromResult(ResetStepResult.Done(part));
        });

        Assert.Equal(Enum.GetValues<ResetPart>(), ran);
        Assert.Equal(5, results.Count);
        var failed = Assert.Single(results, r => !r.Ok);
        Assert.Equal(ResetPart.Accounts, failed.Part);
        Assert.DoesNotContain("secret", string.Join(' ', failed.Failures));
        Assert.DoesNotContain("Users", string.Join(' ', failed.Failures));
    }

    [Fact]
    public async Task Failed_results_pass_through_with_their_failures()
    {
        var results = await ResetPlan.RunAsync([ResetPart.History], p =>
            Task.FromResult(ResetStepResult.Failed(p, ["a folder"])));
        var r = Assert.Single(results);
        Assert.False(r.Ok);
        Assert.Equal(["a folder"], r.Failures);
    }

    [Fact]
    public void Inventory_lists_each_part_and_never_backups()
    {
        var paths = Make();
        string app = paths.AppDataDir;
        string[] files =
        [
            "history.json", "history.abcd1234.json", "snapshot.json", "snapshot.abcd1234.json", "seats.json",
            "publish-request.json", "publish-result.json", "theme-request.json", "theme-result.json",
            "glow-waiting.signal", "glow-done.signal", "last_error.json", "sanduhr.log", "sanduhr.log.1",
            "settings.json", "integrations\\installs.json", "mods\\m\\file.txt", "mcp\\v1\\sanduhr-mcp.exe",
            "bin\\sanduhr-statusline.ps1", "bin\\sanduhr-mcp.cmd", "themes\\mine.json",
            "webview2-0a1b2c3d\\Default\\Cookies", "webview2-fetch\\x", "webview2\\guid\\x",
            "backups\\keep-me.json", "backups\\sub\\also.json",
            "messages.txt", "messages.txt.previous", "desk-messages-request.json", "watch-request-1-2-w1.json",
            "launcher\\" + new string('a', 64) + ".png",
            "designs\\" + new string('b', 32) + ".json",
            "now-playing-looks.json",
        ];
        // The AppData tree above is what a pre-3.5.0 build left; the same names exist in ~\.sanduhr now.
        string ext = paths.ExternalDirPath;
        string[] externalFiles =
        [
            "snapshot.json", "snapshot.abcd1234.json", "seats.json",
            "publish-request.json", "publish-result.json", "theme-request.json", "theme-result.json",
            "glow-waiting.signal", "glow-done.signal", "desk-messages-request.json", "watch-request-1-2-w1.json",
            "mods\\m\\file.txt", "mcp\\v1\\sanduhr-mcp.exe", "bin\\sanduhr-statusline.ps1", "mcp-settings.json",
            "watchers.json", "desk-state.json", "wallpaper-preview.png", "desk-design-request.json", "desk-design-result.json",
            "now-playing-looks-request.json", "now-playing-looks-result.json", "now-playing-look.json", "now-playing-look-offer.json",
        ];
        foreach (var f in files) Touch(Path.Combine(app, f));
        foreach (var f in externalFiles) Touch(Path.Combine(ext, f));
        Directory.CreateDirectory(Path.Combine(app, "webview2-nothex12"));   // not an account profile
        Touch(Path.Combine(paths.VaultDir, "root", "meta.json"));

        var inv = ResetPlan.Inventory(paths);
        string Rel(ResetItem i) => i.Path.StartsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? "~\\" + Path.GetRelativePath(ext, i.Path)
            : Path.GetRelativePath(app, i.Path);

        Assert.Equal(
            new HashSet<string>
            {
                "integrations", "mods", "mcp", "bin",
                "~\\mods", "~\\mcp", "~\\bin", "~\\mcp-settings.json",
            },
            inv[ResetPart.ClaudeCodeEntries].Select(Rel).ToHashSet());
        Assert.Equal(new HashSet<string> { "webview2-0a1b2c3d", "webview2-fetch", "webview2" },
            inv[ResetPart.Accounts].Select(Rel).ToHashSet());

        var hist = inv[ResetPart.History].Select(i => i.Path == paths.VaultDir ? "<vault>" : Rel(i)).ToHashSet();
        foreach (var expected in new[]
        {
            "history.json", "history.abcd1234.json", "last_error.json", "sanduhr.log", "sanduhr.log.1", "<vault>",
            // old AppData copies
            "snapshot.json", "snapshot.abcd1234.json", "seats.json",
            "publish-request.json", "publish-result.json", "theme-request.json", "theme-result.json",
            "glow-waiting.signal", "glow-done.signal", "desk-messages-request.json", "watch-request-1-2-w1.json",
            // the same names in ~\.sanduhr
            "~\\snapshot.json", "~\\snapshot.abcd1234.json", "~\\seats.json",
            "~\\publish-request.json", "~\\publish-result.json", "~\\theme-request.json", "~\\theme-result.json",
            "~\\glow-waiting.signal", "~\\glow-done.signal", "~\\desk-messages-request.json", "~\\watch-request-1-2-w1.json",
            "~\\desk-design-request.json", "~\\desk-design-result.json",
            "~\\now-playing-looks-request.json", "~\\now-playing-looks-result.json", "~\\now-playing-look.json", "~\\now-playing-look-offer.json",
        })
            Assert.Contains(expected, hist);

        Assert.Equal(["themes"], inv[ResetPart.Themes].Select(Rel));
        Assert.Equal(["messages.txt", "messages.txt.previous", "settings.json", "launcher", "designs", "now-playing-looks.json", "~\\mcp-settings.json", "~\\watchers.json", "~\\desk-state.json", "~\\wallpaper-preview.png"],
            inv[ResetPart.Settings].Select(Rel));

        string backups = Path.Combine(app, "backups");
        foreach (var items in inv.Values)
        {
            Assert.DoesNotContain(items, i => i.Path.StartsWith(backups, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(items, i => string.Equals(i.Path.TrimEnd('\\'), ext, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_foreign_file_in_the_external_folder_is_never_listed()
    {
        var paths = Make();
        string ext = paths.ExternalDirPath;
        Touch(Path.Combine(ext, "time", "x"));
        Touch(Path.Combine(ext, "snapshot.json"));
        Touch(Path.Combine(ext, "other-tool.json"));

        var all = ResetPlan.Inventory(paths).Values.SelectMany(v => v).Select(i => i.Path).ToList();
        Assert.Contains(Path.Combine(ext, "snapshot.json"), all);
        Assert.DoesNotContain(all, p => p.StartsWith(Path.Combine(ext, "time"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(all, p => p.EndsWith("other-tool.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inventory_on_an_empty_tree_is_empty_and_does_not_create_the_external_folder()
    {
        var paths = Make();
        var inv = ResetPlan.Inventory(paths);
        Assert.Equal(5, inv.Count);
        Assert.All(inv.Values, v => Assert.Empty(v));
        Assert.False(Directory.Exists(paths.ExternalDirPath));
    }
}
