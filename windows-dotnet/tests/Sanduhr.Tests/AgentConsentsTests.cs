using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 6 (spec, Privacy, "What Claude can change"; WF10): every tool Sanduhr's MCP server offers is in exactly one row, so a
/// new tool fails here until the page says what it lets Claude do. The tools are read from src/Sanduhr.Mcp/ToolCatalog.cs, the list the
/// server answers tools/list with.
/// </summary>
public class AgentConsentsTests
{
    private static string Src(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    internal static IReadOnlyList<string> ToolsIn(string catalogSource) =>
        [.. Regex.Matches(catalogSource, @"\bTool\(\s*""([a-z_]+)""").Select(m => m.Groups[1].Value)];

    private static IReadOnlyList<string> CatalogTools() => ToolsIn(Src("src", "Sanduhr.Mcp", "ToolCatalog.cs"));

    /// <summary>The tools no row names, and the tools more than one row names.</summary>
    internal static (IReadOnlyList<string> Missing, IReadOnlyList<string> Doubled) Coverage(IReadOnlyList<string> tools, IReadOnlyList<AgentConsent> rows)
    {
        var counts = tools.Select(t => (t, n: rows.Count(c => c.Tools.Contains(t)))).ToList();
        return ([.. counts.Where(x => x.n == 0).Select(x => x.t)], [.. counts.Where(x => x.n > 1).Select(x => x.t)]);
    }

    [Fact]
    public void Every_MCP_tool_is_in_exactly_one_row()
    {
        var tools = CatalogTools();
        Assert.Equal(16, tools.Count);   // the scan must find the catalog, not nothing
        var (missing, doubled) = Coverage(tools, AgentConsents.All);
        Assert.Empty(missing);
        Assert.Empty(doubled);
        Assert.Equal(tools.Order(StringComparer.Ordinal), AgentConsents.All.SelectMany(c => c.Tools).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_tool_added_to_the_server_with_no_row_is_found_and_a_tool_in_two_rows_is_found()
    {
        var real = Src("src", "Sanduhr.Mcp", "ToolCatalog.cs");
        var grown = ToolsIn(real + "\n        Tool(\n            \"propose_brand_new\",\n            \"d\", null);");
        Assert.Equal(17, grown.Count);
        var (missing, _) = Coverage(grown, AgentConsents.All);
        Assert.Equal(["propose_brand_new"], missing);

        var twice = AgentConsents.All.Append(AgentConsents.All[0] with { Id = "again", Tools = ["ping"] }).ToList();
        var (_, doubled) = Coverage(CatalogTools(), twice);
        Assert.Equal(["ping"], doubled);
    }

    [Fact]
    public void The_spec_four_come_first_in_its_order_and_wear_their_switches_names()
    {
        Assert.Equal([AgentConsents.Watchers, AgentConsents.DeskDirect, AgentConsents.SongsDirect, AgentConsents.FolderReads],
            AgentConsents.All.Take(4).Select(c => c.Id));
        Assert.Equal([SettingsNames.WatchersSwitch, SettingsNames.DeskDirectSwitch, SettingsNames.NowPlayingLooksDirect, SettingsNames.LetClaudeReadTotals],
            AgentConsents.All.Take(4).Select(c => c.Name));
    }

    [Fact]
    public void The_reads_row_wears_the_reads_caption()
    {
        var reads = AgentConsents.All.Single(c => c.Id == AgentConsents.Reads);
        Assert.Equal(SettingsNames.AgentReadsCaption, reads.Caption);
        Assert.Equal(["get_usage", "get_desk", "get_desk_messages", "ping"], reads.Tools);
    }

    [Fact]
    public void A_row_with_a_switch_names_its_key_and_a_row_without_names_none()
    {
        foreach (var c in AgentConsents.All)
        {
            if (c.Kind is AgentConsentKind.AsksFirst or AgentConsentKind.NoSwitch)
                Assert.Null(c.Key);
            else
                Assert.False(string.IsNullOrEmpty(c.Key), c.Id);
        }
        Assert.Equal(["watchers_agents", "desk_direct", "now_playing.looks_direct", "mcp_roots", "publish.enabled"],
            AgentConsents.All.Where(c => c.Key is not null).Select(c => c.Key!));
    }

    [Fact]
    public void Every_switch_row_starts_off_as_the_store_its_page_writes_does()
    {
        Assert.All(AgentConsents.All, c => Assert.False(c.DefaultOn, c.Id));
        // The stores: each reader's fallback is off (a folder missing from mcp_roots is not allowed).
        var store = Src("src", "Sanduhr.App", "Services", "SettingsStore.cs");
        Assert.Contains("ReadBool(Read(), \"watchers_agents\", false)", store);
        Assert.Contains("ReadBool(Read(), \"desk_direct\", false)", store);
        Assert.Contains("node?.GetValue<bool>() ?? false", store);
        Assert.False(NowPlayingSettings.Default.LooksDirect);
        Assert.False(PublishSettings.Defaults.Enabled);
        Assert.Empty(PublishSettings.Defaults.Roots);
    }

    [Fact]
    public void Every_row_links_to_its_home_section()
    {
        foreach (var c in AgentConsents.All)
        {
            Assert.Equal(new SettingsLink(c.Home, c.HomeAnchor), SettingsIndex.Check(c.Home, c.HomeAnchor));
            Assert.False(SettingsIndex.FetchingSections.ContainsKey((c.Home, c.HomeAnchor)), c.Id);
            Assert.Equal(SettingsNames.LinkTitle(c.Home), c.LinkTitle);
            Assert.Equal($"{SettingsIndex.PageId(c.Home)}#{c.HomeAnchor}", c.Link);
            Assert.StartsWith(c.LinkTitle + ", ", c.LinkName);
            Assert.Empty(SettingsNames.RetiredNames(c.Name));
            Assert.Empty(SettingsNames.PathProblems(c.Caption));
        }
    }

    [Fact]
    public void States_read_as_the_kind_says()
    {
        AgentConsent Of(string id) => AgentConsents.All.Single(c => c.Id == id);
        IReadOnlyList<(string, bool)> none = [];
        Assert.Equal("On", AgentConsents.State(Of(AgentConsents.Watchers), true, none));
        Assert.Equal("Off", AgentConsents.State(Of(AgentConsents.DeskDirect), false, none));
        Assert.Equal("On, with Publish daily at", AgentConsents.State(Of(AgentConsents.Publish), true, none));
        Assert.Equal("Asks you first", AgentConsents.State(Of(AgentConsents.Messages), true, none));
        Assert.Equal("Asks you first", AgentConsents.State(Of(AgentConsents.WidgetPacks), false, none));
        Assert.Equal("No switch", AgentConsents.State(Of(AgentConsents.Theme), false, none));
        Assert.Equal("No switch", AgentConsents.State(Of(AgentConsents.Reads), false, none));
        Assert.Equal(@"On for ~\.claude.", AgentConsents.State(Of(AgentConsents.FolderReads), false, [(@"~\.claude", true)]));
        Assert.Equal(@"On for ~\.claude. Off for ~\.claude-work.",
            AgentConsents.State(Of(AgentConsents.FolderReads), false, [(@"~\.claude", true), (@"~\.claude-work", false)]));
    }
}
