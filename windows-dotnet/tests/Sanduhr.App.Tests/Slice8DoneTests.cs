using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 8, Done: "the contrast test passes for all six themes, and Reset can leave one folder's entries in place."
/// Reset is driven through the real dialog's Reset button, with streamer mode on, over three real installs (statusline, meters mod,
/// MCP and glow hook each, one statusline Combine-chained) in a scratch profile; the service and the undo are the app's own.
/// </summary>
[Collection("SettingsWindow")]
public sealed class Slice8DoneTests : AppWindowFixture
{
    private const string TheirCommand = "my-own-line.cmd --fancy";
    private const string Settings = "{\n  \"theme\": \"dark\"\n}\n";

    [Fact]
    public void Every_built_in_reads_on_the_page_and_the_sidebar()
    {
        var bad = new List<string>();
        foreach (var key in ThemeCatalog.BuiltInOrder)
        {
            var def = ThemeCatalog.BuiltIns[key];
            foreach (var (token, hex) in new[] { ("TextDim", def.TextDim), ("TextMuted", def.TextMuted), ("SelectedLabel", ThemeContrast.SelectedLabel(def)) })
            {
                double worst = ThemeContrast.Worst(hex, def);
                if (worst < ThemeContrast.TextMin) bad.Add($"{key} {token} {hex}: {worst:0.00}:1");
            }
        }
        Assert.Equal(6, ThemeCatalog.BuiltInOrder.Count);
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    private sealed class World : IDisposable
    {
        public required WidgetViewModel Widget { get; init; }
        public required AccountStore Accounts { get; init; }
        public required string A { get; init; }   // Combine-chained statusline
        public required string B { get; init; }   // the one Reset keeps
        public required string C { get; init; }
        public required ClaudeCodeIntegrations Integ { get; init; }
        public required StatuslineInstaller Statusline { get; init; }
        public required McpIntegrationInstaller Mcp { get; init; }
        public required string Decoy { get; init; }
        public void Dispose() => Widget.Dispose();
    }

    private World Build()
    {
        string a = Home(".claude-qxalpha", "{\n  \"theme\": \"dark\",\n  \"statusLine\": { \"type\": \"command\", \"command\": \"" + TheirCommand + "\" }\n}\n");
        string b = Home(".claude-qxbravo", Settings);
        string c = Home(".claude-qxcharlie", Settings);
        var (widget, accounts) = ScratchWidget("Qxacct");
        try
        {
            string source = Path.Combine(Root, "app", "mods", "sanduhr-meters");
            Directory.CreateDirectory(Path.Combine(source, ".claude-plugin"));
            Directory.CreateDirectory(Path.Combine(source, "hooks"));
            File.WriteAllText(Path.Combine(source, ".claude-plugin", "plugin.json"), "{ \"name\": \"sanduhr-meters\", \"version\": \"0.3.0\" }");
            File.WriteAllText(Path.Combine(source, "hooks", "hooks.json"), "{ \"modules\": [\"./register.tsx\"] }");
            File.WriteAllText(Path.Combine(source, "hooks", "register.tsx"), "export {};");
            var integ = new ClaudeCodeIntegrations(Profile, widget.SanduhrAppDataDir, source, externalDir: widget.SanduhrExternalDir);
            var statusline = new StatuslineInstaller(Profile, widget.StatuslineBinDir, null, widget.SanduhrAppDataDir);
            var mcp = new McpIntegrationInstaller(Profile, widget.SanduhrAppDataDir, externalDir: widget.SanduhrExternalDir);
            Assert.True(statusline.InstallScript());
            foreach (var folder in new[] { a, b, c })
            {
                Assert.True(integ.Install(IntegrationKind.Meters, folder).Ok, "meters " + folder);
                Assert.True(integ.Install(IntegrationKind.Hooks, folder).Ok, "hooks " + folder);
                var mode = folder == a ? StatuslineMode.Combine : StatuslineMode.Replace;
                Assert.True(statusline.Install(Path.GetFileName(folder), mode, StatuslineInstaller.DefaultSegments, StatuslineInstaller.JoinLine), "statusline " + folder);
                Assert.True(mcp.Register(Path.GetFileName(folder)).Ok, "mcp " + folder);
            }
            // The Combine really chained their command inside Sanduhr's.
            Assert.Contains("-Chain", JsonNode.Parse(File.ReadAllText(Path.Combine(a, "settings.json")))!["statusLine"]!["command"]!.GetValue<string>());
            Directory.CreateDirectory(Path.GetDirectoryName(mcp.LauncherPath)!);
            if (!File.Exists(mcp.LauncherPath)) File.WriteAllText(mcp.LauncherPath, "@echo off");
            string decoy = Path.Combine(widget.SanduhrExternalDir, "time", "keep.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(decoy)!);
            File.WriteAllText(decoy, "another tool's file");
            return new World { Widget = widget, Accounts = accounts, A = a, B = b, C = c, Integ = integ, Statusline = statusline, Mcp = mcp, Decoy = decoy };
        }
        catch
        {
            widget.Dispose();
            throw;
        }
    }

    private static Dictionary<string, string> Hashes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), StringComparer.OrdinalIgnoreCase);

    private ResetWindow OpenReset(World w, out ResetFolderListViewModel list)
    {
        var paths = ScratchPaths();
        var service = new ResetService(paths, w.Accounts, new PublishTokenStore(new MemoryCredentials()),
            k => Task.Run<IReadOnlyList<string>>(() => ClaudeCodeViewModel.UndoEverything(w.Widget, k)),
            () => { },
            clearMcpSwitches: () => new SettingsStore(paths).ClearMcpSwitchesForReset(),
            clearStatuslineSwitches: () => new SettingsStore(paths).ClearStatuslineSwitchesForReset());
        list = ResetFolderListViewModel.Read(w.Widget);
        var window = new ResetWindow(service, list);
        OffScreen(window);
        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>Claude Code part plus Settings ticked; the row for <paramref name="keep"/> unticked, found by the codename it shows.</summary>
    private static void TickAndUntick(ResetWindow window, ResetFolderListViewModel list, string keep)
    {
        foreach (var name in new[] { "AccountsBox", "HistoryBox", "ThemesBox" })
            ((CheckBox)window.FindName(name)).IsChecked = false;
        ((CheckBox)window.FindName("SettingsBox")).IsChecked = true;
        ((CheckBox)window.FindName("ClaudeCodeBox")).IsChecked = true;
        var row = list.Rows.Single(r => ClaudeCodeFolders.Same(r.Path, keep));
        Assert.True(Codenames.IsCodename(row.Shown), row.Shown);
        Assert.False(row.Shown.Contains("qx", StringComparison.OrdinalIgnoreCase), row.Shown);
        var box = Boxes(window).Single(b => (string)b.Content == row.Shown);
        box.IsChecked = false;
    }

    private static void ClickResetAndWait(ResetWindow window)
    {
        var reset = (Button)window.FindName("ResetButton");
        ((IInvokeProvider)new ButtonAutomationPeer(reset).GetPattern(PatternInterface.Invoke)!).Invoke();
        var quit = (Button)window.FindName("QuitButton");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (quit.Visibility != Visibility.Visible && DateTime.UtcNow < deadline)
        {
            Pump();
            Thread.Sleep(10);
        }
        Assert.Equal(Visibility.Visible, quit.Visibility);
    }

    private static string? StatusLineOf(string folder) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "settings.json")))!["statusLine"]?["command"]?.GetValue<string>();

    [Fact]
    public void Reset_through_the_dialog_with_streamer_on_leaves_the_kept_folder_byte_for_byte_and_undoes_the_others() => AppHost.Run(() =>
    {
        using var w = Build();
        ResetWindow? window = null;
        try
        {
            var beforeB = Hashes(w.B);
            Assert.NotEmpty(beforeB);
            Assert.True(w.Mcp.IsRegistered(Path.GetFileName(w.B)));
            w.Widget.Streamer.SetOn(true);
            window = OpenReset(w, out var list);
            Assert.Equal(3, list.Rows.Count);
            TickAndUntick(window, list, w.B);

            // B had MCP and Settings is ticked: the dialog says the kept server loses its consent.
            var mcpLine = (TextBlock)window.FindName("KeptMcpSettingsText");
            Assert.Equal(Visibility.Visible, mcpLine.Visibility);
            Assert.Equal(SettingsNames.ResetKeptMcpSettings, mcpLine.Text);
            Assert.All(NamesOf(window), n => Assert.False(n.Contains("qx", StringComparison.OrdinalIgnoreCase), n));

            ClickResetAndWait(window);
            Assert.Equal("Reset is done.", ((TextBlock)window.FindName("ProgressText")).Text);

            // The kept folder: every file, byte for byte.
            var afterB = Hashes(w.B);
            Assert.Equal(beforeB.Keys.Order(), afterB.Keys.Order());
            foreach (var (file, hash) in beforeB)
                Assert.True(hash == afterB[file], $"{Path.GetFileName(file)} in the kept folder changed");
            Assert.True(w.Mcp.IsRegistered(Path.GetFileName(w.B)));
            Assert.True(w.Statusline.IsRegistered(Path.GetFileName(w.B)));

            // The others: no Sanduhr entries left, and the Combine user's own command is back.
            foreach (var folder in new[] { w.A, w.C })
            {
                string name = Path.GetFileName(folder);
                Assert.False(w.Statusline.IsRegistered(name), name + " statusline");
                Assert.False(w.Mcp.IsRegistered(name), name + " mcp");
                var text = File.ReadAllText(Path.Combine(folder, "settings.json"));
                Assert.DoesNotContain("sanduhr", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hooks", text, StringComparison.OrdinalIgnoreCase);
                if (File.Exists(Path.Combine(folder, ".claude.json")))
                    Assert.DoesNotContain("sanduhr", File.ReadAllText(Path.Combine(folder, ".claude.json")), StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(TheirCommand, StatusLineOf(w.A));
            Assert.Null(StatusLineOf(w.C));

            // What the kept folder runs, and the record that removes it later, stayed; shared ground too.
            Assert.True(File.Exists(w.Statusline.ScriptPath));
            Assert.True(File.Exists(w.Mcp.LauncherPath));
            var receiptsFile = ResetPlan.ReceiptsFile(ScratchPaths());
            Assert.True(File.Exists(receiptsFile));
            var receipts = new ReceiptStore(receiptsFile).Load().Records;
            Assert.NotEmpty(receipts);
            Assert.All(receipts, r => Assert.True(ClaudeCodeFolders.Same(r.Folder, w.B), r.Folder));
            Assert.True(File.Exists(w.Decoy));
        }
        finally { window?.Close(); }
    });

    /// <summary>Tier 2: a folder whose settings.json is held open fails its undo; Reset says so and keeps the script, the receipts and
    /// the kept folder, and the held folder is untouched.</summary>
    [Fact]
    public void A_locked_settings_json_during_the_dialog_run_keeps_the_script_and_receipts_and_reports_it() => AppHost.Run(() =>
    {
        using var w = Build();
        ResetWindow? window = null;
        try
        {
            var beforeB = Hashes(w.B);
            var beforeA = Hashes(w.A)[Path.Combine(w.A, "settings.json")];
            w.Widget.Streamer.SetOn(true);
            window = OpenReset(w, out var list);
            TickAndUntick(window, list, w.B);
            using (new FileStream(Path.Combine(w.A, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                ClickResetAndWait(window);
            }
            Assert.NotEqual("Reset is done.", ((TextBlock)window.FindName("ProgressText")).Text);
            Assert.Equal(beforeB, Hashes(w.B));
            Assert.Equal(beforeA, Hashes(w.A)[Path.Combine(w.A, "settings.json")]);   // the held file is untouched; the MCP entry (another file) may go
            Assert.True(File.Exists(w.Statusline.ScriptPath));
            Assert.True(File.Exists(ResetPlan.ReceiptsFile(ScratchPaths())));
            Assert.True(File.Exists(w.Decoy));
            Assert.True(w.Statusline.IsRegistered(Path.GetFileName(w.A)));   // still there, so Settings can still remove it
        }
        finally { window?.Close(); }
    });

    private static IEnumerable<CheckBox> Boxes(ResetWindow window)
    {
        window.UpdateLayout();
        var list = (ItemsControl)window.FindName("FolderList");
        for (int i = 0; i < list.Items.Count; i++)
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject c && Descendant<CheckBox>(c) is { } box)
                yield return box;
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (Descendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
