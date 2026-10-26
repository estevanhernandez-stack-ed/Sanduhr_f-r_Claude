namespace Sanduhr.Core;

/// <summary>Who makes the call a <see cref="NetworkRow"/> discloses.</summary>
public enum NetworkCaller
{
    /// <summary>Sanduhr itself: every such call site in src carries a "// network: id" marker (NetworkDisclosureTests).</summary>
    Sanduhr,
    /// <summary>Claude Code, sending what Sanduhr's MCP server answered as part of the conversation. Sanduhr makes no call.</summary>
    ClaudeCode,
}

/// <summary>One row of Settings, Privacy, What leaves this PC (spec, Privacy; WF6): where, when, what, and how to stop it.</summary>
/// <param name="HostsAreNames">True when <c>Hosts</c> is a list of real host names (set in a code font); false when it reads as words (P10).</param>
public sealed record NetworkRow(
    string Id, string Destination, string Hosts, string When, string Carries, string Off,
    SettingsPage OffPage, string OffAnchor, NetworkCaller Caller = NetworkCaller.Sanduhr, bool HostsAreNames = false)
{
    public string OffLinkTitle => SettingsNames.LinkTitle(OffPage);
    public string OffLink => SettingsIndex.PageId(OffPage) + "#" + OffAnchor;
    /// <summary>The link spoken with its row, so two "Accounts settings…" read apart.</summary>
    public string OffLinkName => $"{OffLinkTitle}, {Destination}";
    /// <summary>A list item peer can read an item as its text: the destination, never the record (decision 21).</summary>
    public override string ToString() => Destination;
}

/// <summary>
/// Everything that leaves this PC (Settings v2, slice 6). One row per destination and purpose, in the page's order. NetworkDisclosureTests
/// holds this list to every outbound call in src; a new call fails CI until it is disclosed here and marked at its site. Lines that hand an
/// address to the person's browser are marked <see cref="YourBrowser"/>, and lines that stay on this PC <see cref="LocalOnly"/>; neither is
/// a row. Pure; not linked into the MCP server.
/// </summary>
public static class NetworkDisclosure
{
    /// <summary>A site that opens an https address in the person's default browser: the browser's visit, with nothing of Sanduhr's.</summary>
    public const string YourBrowser = "your-browser";
    /// <summary>A site that never leaves the PC: Docker's named pipe, Explorer, Windows Settings, a relaunch.</summary>
    public const string LocalOnly = "local";
    /// <summary>The privacy policy (owner taste 4: GitHub's rendering of docs/PRIVACY.md).</summary>
    public const string PrivacyPolicyUrl = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/blob/main/docs/PRIVACY.md";

    public static IReadOnlyList<NetworkRow> All { get; } =
    [
        new("claude-usage", SettingsNames.NetClaudeUsage, SettingsNames.NetClaudeUsageHosts,
            SettingsNames.NetClaudeUsageWhen, SettingsNames.NetClaudeUsageCarries, SettingsNames.NetClaudeUsageOff,
            SettingsPage.Accounts, SettingsIndex.AccountList),
        new("claude-sign-in", SettingsNames.NetSignIn, SettingsNames.NetSignInHosts,
            SettingsNames.NetSignInWhen, SettingsNames.NetSignInCarries, SettingsNames.NetSignInOff,
            SettingsPage.Accounts, SettingsIndex.SignInSection),
        new("open-meteo", SettingsNames.NetWeather, "geocoding-api.open-meteo.com, api.open-meteo.com",
            SettingsNames.NetWeatherWhen, SettingsNames.NetWeatherCarries, SettingsNames.NetWeatherOff,
            SettingsPage.Weather, SettingsIndex.WeatherPlace, HostsAreNames: true),
        new("widget-catalog", SettingsNames.NetCatalog, string.Join(", ", WidgetCatalog.AllowedHosts),
            SettingsNames.NetCatalogWhen, SettingsNames.NetCatalogCarries, SettingsNames.NetCatalogOff,
            SettingsPage.Mods, SettingsIndex.ModList, HostsAreNames: true),
        new("github-update", SettingsNames.NetUpdates, SettingsNames.NetUpdatesHosts,
            SettingsNames.NetUpdatesWhen, SettingsNames.NetUpdatesCarries, SettingsNames.NetUpdatesOff,
            SettingsPage.About, SettingsIndex.Updates),
        new("store-update", SettingsNames.NetStore, SettingsNames.NetStoreHosts,
            SettingsNames.NetStoreWhen, SettingsNames.NetStoreCarries, SettingsNames.NetStoreOff,
            SettingsPage.About, SettingsIndex.Updates),
        new("publish-usage", SettingsNames.NetPublish, SettingsNames.NetPublishHosts,
            SettingsNames.NetPublishWhen, SettingsNames.NetPublishCarries, SettingsNames.NetPublishOff,
            SettingsPage.Publish, SettingsIndex.Schedule),
        new("publish-agent", SettingsNames.NetPublishAgent, SettingsNames.NetPublishHosts,
            SettingsNames.NetPublishAgentWhen, SettingsNames.NetPublishAgentCarries, SettingsNames.NetPublishAgentOff,
            SettingsPage.Publish, SettingsIndex.Schedule),
        new("claude-code", SettingsNames.NetThroughClaude, SettingsNames.NetThroughClaudeHosts,
            SettingsNames.NetThroughClaudeWhen, SettingsNames.NetThroughClaudeCarries, SettingsNames.NetThroughClaudeOff,
            SettingsPage.ClaudeCode, SettingsIndex.Folders, NetworkCaller.ClaudeCode),
    ];

    /// <summary>The row with this id, null when there is none.</summary>
    public static NetworkRow? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}
