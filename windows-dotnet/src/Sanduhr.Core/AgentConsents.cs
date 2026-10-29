namespace Sanduhr.Core;

/// <summary>How a person decides what Claude may do through one row of tools.</summary>
public enum AgentConsentKind
{
    /// <summary>One switch in settings.json, off by default.</summary>
    Switch,
    /// <summary>One switch per Claude Code folder ("mcp_roots").</summary>
    PerFolder,
    /// <summary>Follows "Publish daily at" ("publish.enabled").</summary>
    FollowsPublish,
    /// <summary>No switch: everything Claude proposes waits for the person.</summary>
    AsksFirst,
    /// <summary>No switch: Claude can do it whenever the MCP server is installed.</summary>
    NoSwitch,
}

/// <summary>One row of Settings, Privacy, What Claude can change: the tools it covers, how it is decided, and where its home is.
/// <paramref name="DefaultOn"/> is the state before the person touches anything (every switch starts off; a folder missing from "mcp_roots" is not allowed).</summary>
public sealed record AgentConsent(string Id, string Name, string Caption, AgentConsentKind Kind, string? Key,
    SettingsPage Home, string HomeAnchor, IReadOnlyList<string> Tools, bool DefaultOn = false)
{
    public string LinkTitle => SettingsNames.LinkTitle(Home);
    public string Link => SettingsIndex.PageId(Home) + "#" + HomeAnchor;
    /// <summary>The link spoken with its row, so the two "Claude Code settings…" read apart.</summary>
    public string LinkName => $"{LinkTitle}, {Name}";
}

/// <summary>
/// What Claude can change (Settings v2, slice 6; WF10): every tool of Sanduhr's MCP server, in nine rows (decision 9). The spec's four consents
/// come first in its order; then the rows the spec didn't name, so the list is the whole truth. AgentConsentsTests holds it to ToolCatalog.cs.
/// Pure; not linked into the MCP server.
/// </summary>
public static class AgentConsents
{
    public const string Watchers = "watchers";
    public const string DeskDirect = "desk-direct";
    public const string SongsDirect = "songs-direct";
    public const string FolderReads = "folder-reads";
    public const string Publish = "publish";
    public const string Theme = "theme";
    public const string Messages = "messages";
    public const string WidgetPacks = "widget-packs";
    public const string Reads = "reads";

    public static IReadOnlyList<AgentConsent> All { get; } =
    [
        new(Watchers, SettingsNames.WatchersSwitch, SettingsNames.AgentWatchersCaption, AgentConsentKind.Switch, "watchers_agents",
            SettingsPage.Watchers, SettingsIndex.Watchers, ["watch_start", "watch_update", "watch_end"]),
        new(DeskDirect, SettingsNames.DeskDirectSwitch, SettingsNames.AgentDeskDirectCaption, AgentConsentKind.Switch, "desk_direct",
            SettingsPage.Desk, SettingsIndex.DeskDesigns, ["propose_desk"]),
        new(SongsDirect, SettingsNames.NowPlayingLooksDirect, SettingsNames.AgentSongsDirectCaption, AgentConsentKind.Switch, "now_playing.looks_direct",
            SettingsPage.NowPlaying, SettingsIndex.NowPlayingLooks, ["propose_now_playing_looks"]),
        new(FolderReads, SettingsNames.LetClaudeReadTotals, SettingsNames.AgentFolderReadsCaption, AgentConsentKind.PerFolder, "mcp_roots",
            SettingsPage.ClaudeCode, SettingsIndex.Folders, ["get_local_burn_by_project", "get_model_usage", "get_usage_history"]),
        new(Publish, SettingsNames.AgentPublish, SettingsNames.AgentPublishCaption, AgentConsentKind.FollowsPublish, "publish.enabled",
            SettingsPage.Publish, SettingsIndex.Schedule, ["publish_usage"]),
        new(Theme, SettingsNames.AgentTheme, SettingsNames.AgentThemeCaption, AgentConsentKind.NoSwitch, null,
            SettingsPage.Themes, SettingsIndex.Gallery, ["propose_theme"]),
        new(Messages, SettingsNames.AgentMessages, SettingsNames.AgentMessagesCaption, AgentConsentKind.AsksFirst, null,
            SettingsPage.DeskMessage, SettingsIndex.MessageSuggestion, ["propose_desk_messages"]),
        new(WidgetPacks, SettingsNames.AgentWidgetPacks, SettingsNames.AgentWidgetPacksCaption, AgentConsentKind.AsksFirst, null,
            SettingsPage.Mods, SettingsIndex.ModList, ["propose_widget"]),
        new(Reads, SettingsNames.AgentReads, SettingsNames.AgentReadsCaption, AgentConsentKind.NoSwitch, null,
            SettingsPage.ClaudeCode, SettingsIndex.Folders, ["get_usage", "get_desk", "get_desk_messages", "ping"]),
    ];

    /// <summary>A row's state as the list shows it. <paramref name="on"/> is the switch (Switch, FollowsPublish); <paramref name="folders"/>
    /// is each discovered folder, shown as on screen, and its read switch (PerFolder).</summary>
    public static string State(AgentConsent c, bool on, IReadOnlyList<(string Folder, bool On)> folders) => c.Kind switch
    {
        AgentConsentKind.Switch => on ? SettingsNames.AgentOn : SettingsNames.AgentOff,
        AgentConsentKind.FollowsPublish => SettingsNames.AgentFollowsPublish(on),
        AgentConsentKind.PerFolder => SettingsNames.AgentFolders(folders),
        AgentConsentKind.AsksFirst => SettingsNames.AgentAsksFirst,
        _ => SettingsNames.AgentNoSwitch,
    };
}
