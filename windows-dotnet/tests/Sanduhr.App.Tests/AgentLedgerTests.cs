using System.IO;
using System.Windows.Media;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Spec 3, Task 9: the Local burn page splits by agent. The ledger's Agent filter and label, the project rows' shares, the
/// strip's spoken name and the CSV's agent column. With one agent every figure reads as it did before.</summary>
[Collection("SettingsWindow")]
public sealed class AgentLedgerTests : AppWindowFixture
{
    private static VaultSessionInfo Session(string uuid, string root, string agent, string project = "api", string key = "k-api") =>
        new(uuid, root, key, project, null,
            DateTimeOffset.Parse("2026-10-20T10:00:00+00:00"), DateTimeOffset.Parse("2026-10-20T11:00:00+00:00"),
            600, new Dictionary<string, long> { ["m"] = 600 }, null,
            new Dictionary<string, VaultDayBucket> { ["2026-10-20"] = new() { Total = 600 } },
            null, 0, 0, agent);

    [Fact]
    public void The_agent_filter_shows_only_with_two_agents_and_narrows_rows() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var vm = new CcLedgerViewModel(widget, false, _ => { });
            vm.PresentForTests([Session("u1", ".claude", "claude"), Session("u2", "codex", "codex", "web", "k-web")]);
            Assert.True(vm.ShowAgentFilter);
            Assert.Equal(new[] { "All", "Claude Code", "Codex" }, vm.AgentFilters);
            Assert.Equal(2, vm.View.Cast<LedgerRowViewModel>().Count());

            vm.SelectedAgentFilter = "Codex";
            var shown = vm.View.Cast<LedgerRowViewModel>().ToList();
            Assert.Single(shown);
            Assert.All(shown, r => Assert.Equal("Codex", r.AgentLabel));

            // Day mode and the agent filter compose: a chip click leaves the agent filter on.
            vm.SetScopeCommand.Execute("All");
            Assert.Single(vm.View.Cast<LedgerRowViewModel>());
            vm.SelectedAgentFilter = "All";
            Assert.Equal(2, vm.View.Cast<LedgerRowViewModel>().Count());
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void With_one_agent_the_filter_stays_hidden_and_rows_read_as_before() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var vm = new CcLedgerViewModel(widget, false, _ => { });
            vm.PresentForTests([Session("u1", ".claude", "claude")]);
            Assert.False(vm.ShowAgentFilter);
            Assert.DoesNotContain("Claude Code", vm.Rows[0].ToString());
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_stacked_project_spanning_agents_reads_several_agents() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var vm = new CcLedgerViewModel(widget, true, _ => { });
            vm.PresentForTests([Session("u1", ".claude", "claude"), Session("u2", "codex", "codex")]);
            Assert.Equal(SettingsNames.SeveralAgents, vm.Rows.Single().AgentLabel);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_sessions_csv_names_each_rows_agent() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var vm = new CcLedgerViewModel(widget, false, _ => { });
            vm.PresentForTests([Session("u2", "codex", "codex")]);
            var rows = (System.Collections.IEnumerable)typeof(CcLedgerViewModel)
                .GetMethod("BuildCsvRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(vm, null)!;
            Assert.Equal("Codex", rows.Cast<VaultLedgerCsv.Row>().Single().Agent);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Project_rows_carry_each_agents_share_and_read_plain_with_one_agent() => AppHost.Run(() =>
    {
        var split = new Dictionary<string, Dictionary<string, long>> { ["api"] = new() { ["codex"] = 300_000 } };
        var rows = LocalCcViewModel.Rows([("api", 1_200_000L), ("web", 500_000L)], CodenameCategory.Project, split);
        Assert.Equal($"{TokenFormat.Compact(1_200_000)} (Claude Code {TokenFormat.Compact(900_000)}, Codex {TokenFormat.Compact(300_000)})", rows[0].Tokens);
        Assert.Equal(TokenFormat.Compact(500_000), rows[1].Tokens);
        Assert.EndsWith("Codex " + TokenFormat.Compact(300_000) + ")", rows[0].ToString());
    });

    [Fact]
    public void The_strip_names_each_agent_with_a_figure_and_stays_unnamed_with_one() => AppHost.Run(() =>
    {
        var day = DateOnly.FromDateTime(DateTime.Now);
        var strip = new LocalCcBarStrip();
        var byDay = new Dictionary<DateOnly, long> { [day] = 1_000_000 };
        var byAgent = new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>
        {
            ["codex"] = new Dictionary<DateOnly, long> { [day] = 250_000 },
        };
        strip.SetData(byDay, ThemePalette.Obsidian, byAgent, _ => Colors.Teal);
        Assert.Equal($"30 days, Claude Code {TokenFormat.Compact(750_000)}, Codex {TokenFormat.Compact(250_000)}",
            System.Windows.Automation.AutomationProperties.GetName(strip));

        strip.SetData(byDay, ThemePalette.Obsidian);
        Assert.Equal("", System.Windows.Automation.AutomationProperties.GetName(strip));
    });

    private static (VaultReader Reader, string Dir) VaultWith(params (string Root, string Day, long Total)[] days)
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agents").FullName;
        var store = new VaultStore(dir);
        foreach (var root in days.Select(d => d.Root).Distinct())
        {
            if (root != ".claude") store.SaveMeta(root, new VaultRootMeta { Agent = root });
            foreach (var month in days.Where(d => d.Root == root).Select(d => d.Day[..7]).Distinct())
            {
                var shard = new VaultRollupShard { SchemaVersion = 1 };
                foreach (var (_, day, total) in days.Where(d => d.Root == root && d.Day.StartsWith(month)))
                {
                    var rd = new VaultRollupDay { Total = total, Input = total / 2, Output = total / 2 };
                    rd.ByProject["api~12345678"] = total;
                    shard.Days[day] = rd;
                }
                store.SaveRollupShard(root, month, shard);
            }
        }
        return (new VaultReader(store), dir);
    }

    [Fact]
    public void An_agents_hot_day_burn_is_read_into_the_hot_maps_and_the_window_split()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var t = today.ToString("yyyy-MM-dd");
        var (reader, dir) = VaultWith(("codex", t, 400_000));
        try
        {
            var a = LocalCcViewModel.ReadAgents(reader, ["codex"], today.AddDays(-29), today, today);
            Assert.Equal(400_000, a.ByDay["codex"][today]);
            Assert.Equal(400_000, a.HotDay[today]);
            Assert.Equal(400_000, a.HotProjects["api"]);
            Assert.Equal(200_000, a.HotIn[today]);
            Assert.Equal(400_000, a.Projects["api"]["codex"]);
            var row = LocalCcViewModel.Rows([("api", 1_000_000L)], CodenameCategory.Project, a.Projects).Single();
            Assert.Equal($"{TokenFormat.Compact(1_000_000)} (Claude Code {TokenFormat.Compact(600_000)}, Codex {TokenFormat.Compact(400_000)})", row.Tokens);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_consented_agent_root_with_no_rows_changes_nothing_on_the_strip()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (reader, dir) = VaultWith((".claude", today.AddDays(-3).ToString("yyyy-MM-dd"), 100));
        try
        {
            var a = LocalCcViewModel.ReadAgents(reader, [".claude", "codex"], today.AddDays(-29), today, today);
            Assert.Empty(a.ByDay);
            Assert.Empty(a.Projects);
            AppHost.Run(() =>
            {
                var strip = new LocalCcBarStrip();
                strip.SetData(new Dictionary<DateOnly, long> { [today] = 5 }, ThemePalette.Obsidian, a.ByDay, _ => Colors.Teal);
                Assert.False(strip.Stacked);
                strip.SetData(new Dictionary<DateOnly, long> { [today] = 5 }, ThemePalette.Obsidian,
                    new Dictionary<string, IReadOnlyDictionary<DateOnly, long>> { ["codex"] = new Dictionary<DateOnly, long>() }, _ => Colors.Teal);
                Assert.False(strip.Stacked);
                Assert.Equal("", System.Windows.Automation.AutomationProperties.GetName(strip));
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_stacked_project_with_a_claude_and_a_codex_session_stays_under_the_codex_filter() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var vm = new CcLedgerViewModel(widget, true, _ => { });
            vm.PresentForTests([Session("u1", ".claude", "claude"), Session("u2", "codex", "codex")]);
            vm.SelectedAgentFilter = "Codex";
            Assert.Single(vm.View.Cast<LedgerRowViewModel>());
            vm.SelectedAgentFilter = "Claude Code";
            Assert.Single(vm.View.Cast<LedgerRowViewModel>());
        }
        finally { widget.Dispose(); }
    });
}
