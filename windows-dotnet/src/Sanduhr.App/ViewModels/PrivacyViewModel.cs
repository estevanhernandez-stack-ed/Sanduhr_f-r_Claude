using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row of What Claude can change, as shown. Reads as "name, state" (decision 21), never as the record.</summary>
public sealed record AgentRow(string Name, string State, string Caption, string LinkTitle, string Link, string LinkName)
{
    public override string ToString() => $"{Name}, {State}";
}

/// <summary>
/// Settings, Privacy (Settings v2, slice 6; WF6, WF10). The page has three parts:
/// <list type="bullet">
/// <item><description>What leaves this PC is <see cref="NetworkDisclosure.All"/> as it is.</description></item>
/// <item><description>What Claude can change is <see cref="AgentConsents.All"/>. Each switch is read from the store its own page writes
/// (decision 10): Watchers, Desk and Now playing write <c>new SettingsStore(new Paths())</c>; the Claude Code cards write <c>mcp_roots</c>
/// through the widget; Publish usage writes <c>publish.enabled</c> through the widget's service.</description></item>
/// <item><description>The MCP server's install is read from the folders, as the cards read it (decision 11).</description></item>
/// </list>
/// It subscribes to nothing. The switch rows are read at construction (cheap); the MCP install, which reads each folder's config
/// (<c>~\.claude.json</c> can run to megabytes), is read only by <see cref="Reload"/>, which Settings calls on each Privacy visit (P20).
/// </summary>
public sealed partial class PrivacyViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ClaudeCodeHost _host;
    private readonly Func<IReadOnlyDictionary<string, bool>> _mcpRoots;
    private readonly Func<bool> _publishOn;
    private readonly Func<string, bool>? _isRegistered;

    /// <summary>Streamer mode and Show the real name (slice 7), set by Settings; null in tests that build Privacy alone.</summary>
    public StreamerSettingsViewModel? Streamer { get; internal set; }

    /// <summary>What leaves this PC, in the page's order.</summary>
    public IReadOnlyList<NetworkRow> Network => NetworkDisclosure.All;

    /// <summary>What Claude can change, in the page's order, with each row's state as read on the last visit.</summary>
    public ObservableCollection<AgentRow> Agents { get; } = new();

    /// <summary>Where the MCP server is installed, as the Claude Code cards count it. Empty until the first <see cref="Reload"/>.</summary>
    [ObservableProperty] private string _mcpStatus = "";

    public string ClaudeCodeLinkTitle => SettingsNames.LinkTitle(SettingsPage.ClaudeCode);
    public string ClaudeCodeLink => SettingsIndex.PageId(SettingsPage.ClaudeCode) + "#" + SettingsIndex.Folders;
    public string McpStatusLinkName => SettingsNames.McpStatusLinkName;
    public string AboutLinkTitle => SettingsNames.LinkTitle(SettingsPage.About);
    public string AboutLink => SettingsIndex.PageId(SettingsPage.About) + "#" + SettingsIndex.Where;
    public string AboutLinkName => SettingsNames.PrivacyWhereLinkName;

    /// <param name="isRegistered">Tests count the install checks through this; the default asks <see cref="McpIntegrationInstaller"/>.</param>
    internal PrivacyViewModel(SettingsStore store, ClaudeCodeHost host, Func<IReadOnlyDictionary<string, bool>> mcpRoots, Func<bool> publishOn,
        Func<string, bool>? isRegistered = null)
    {
        _store = store;
        _host = host;
        _mcpRoots = mcpRoots;
        _publishOn = publishOn;
        _isRegistered = isRegistered;
        ReadAgents();
    }

    /// <summary>Read every state again (the page was opened; a consent's own page may have changed it), including the MCP install.</summary>
    public void Reload()
    {
        ReadAgents();
        var discovered = _host.Folders().Where(f => f.Discovered).ToList();   // streamer: key - the discovered folders counted for the page, never drawn here
        var check = _isRegistered ?? DefaultCheck();
        McpStatus = SettingsNames.McpInstalledStatus(discovered.Count(f => Registered(check, f.Name)), discovered.Count);
    }

    private Func<string, bool> DefaultCheck()
    {
        var installer = new McpIntegrationInstaller(_host.Profile, _host.SanduhrAppDataDir, externalDir: _host.SanduhrExternalDir);
        return installer.IsRegistered;
    }

    private void ReadAgents()
    {
        var roots = _mcpRoots();
        IReadOnlyList<(string Folder, bool On)> folders =
            [.. _host.Folders().Where(f => f.Discovered).Select(f => (_host.Display(f.Path), roots.GetValueOrDefault(f.Name)))];   // streamer: masked - via Display: the folder shows through StreamerMode.ShowFolder (CP 7)
        Agents.Clear();
        foreach (var c in AgentConsents.All)
            Agents.Add(new AgentRow(c.Name, AgentConsents.State(c, SwitchOn(c), folders), c.Caption, c.LinkTitle, c.Link, c.LinkName));
    }

    private bool SwitchOn(AgentConsent c) => c.Id switch
    {
        AgentConsents.Watchers => _store.LoadWatchersAgents(),
        AgentConsents.DeskDirect => _store.LoadDeskDirect(),
        AgentConsents.SongsDirect => _store.LoadNowPlaying().LooksDirect,
        AgentConsents.Publish => _publishOn(),
        _ => false,
    };

    private static bool Registered(Func<string, bool> check, string name)
    {
        try { return check(name); }
        catch (Exception) { return false; }   // a folder that can't be read doesn't count, as on its card
    }
}
