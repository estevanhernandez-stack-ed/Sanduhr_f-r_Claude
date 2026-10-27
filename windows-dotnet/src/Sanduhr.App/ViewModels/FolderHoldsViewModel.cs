using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One line under a section: a name and what it says.</summary>
public sealed record ModsLine(string Name, string Text)
{
    /// <summary>What a screen reader reads for the line: "Notification, 2 groups", never the record's fields.</summary>
    public string AutomationName => $"{Name}, {Text}";

    public override string ToString() => AutomationName;
}

/// <summary>
/// A Claude Code folder card's "What this folder holds" (Settings v2 slice 4; the read-only report Mods &amp; Config had, WF4, WF9): settings.json,
/// the statusline, hooks, plugin switches, MCP servers, CLAUDE.md and how long Claude Code keeps transcripts. Read from files only, when the
/// section is first opened and on Refresh; nothing runs and nothing is written. Secret values never show (<see cref="ConfigMask"/>).
/// </summary>
public sealed partial class FolderHoldsViewModel : ObservableObject
{
    private readonly ClaudeCodeHost _host;

    internal FolderHoldsViewModel(ClaudeCodeHost host, string folder)
    {
        _host = host;
        Folder = folder;
    }

    public string Folder { get; }

    public ObservableCollection<ModsLine> HookLines { get; } = [];
    public ObservableCollection<ModsLine> PluginLines { get; } = [];
    public ObservableCollection<string> McpNames { get; } = [];

    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _settingsSummary = "";
    [ObservableProperty] private string _settingsDetail = "";
    [ObservableProperty] private bool _settingsUnreadable;
    [ObservableProperty] private string _statuslineSummary = "";
    [ObservableProperty] private string _statuslineCommand = "";
    [ObservableProperty] private string _hooksSummary = "";
    [ObservableProperty] private string _pluginsSummary = "";
    [ObservableProperty] private string _mcpSummary = "";
    [ObservableProperty] private string _mcpProjects = "";
    [ObservableProperty] private string _claudeMdSummary = "";
    [ObservableProperty] private string _retentionSummary = "";
    [ObservableProperty] private bool _retentionWarning;

    /// <summary>Refresh's spoken name: the folder it reads.</summary>
    public string RefreshName => SettingsNames.RefreshFolderNamed(_host.Display(Folder));

    /// <summary>Reads the folder the first time the section opens.</summary>
    public void EnsureLoaded()
    {
        if (!Loaded) Refresh();
    }

    /// <summary>Reads the folder again. Files only.</summary>
    [RelayCommand]
    public void Refresh()
    {
        HookLines.Clear();
        PluginLines.Clear();
        McpNames.Clear();
        FolderReport report;
        try { report = (_host.ReadReport ?? ModInventory.Read)(Folder, _host.Profile); }
        catch (Exception)
        {
            // Slice 4 row 25: a failed read leaves no line from the last one.
            SettingsDetail = "";
            SettingsUnreadable = false;
            StatuslineSummary = "";
            StatuslineCommand = "";
            HooksSummary = "";
            PluginsSummary = "";
            McpSummary = "";
            McpProjects = "";
            ClaudeMdSummary = "";
            RetentionSummary = "";
            RetentionWarning = false;
            SettingsSummary = SettingsNames.HoldsCantRead;
            Loaded = true;
            return;
        }

        SettingsUnreadable = report.Settings.Kind == SettingsKind.Unreadable;
        (SettingsSummary, SettingsDetail) = report.Settings.Kind switch
        {
            SettingsKind.Readable => (SettingsNames.SettingsReadable, SettingsNames.SettingsReadableDetail),
            SettingsKind.Unreadable when report.Settings.Line > 0 =>
                (SettingsNames.SettingsUnreadableAt(report.Settings.Line, report.Settings.Column), SettingsNames.SettingsUnreadableStrict),
            SettingsKind.Unreadable => (SettingsNames.SettingsUnreadable, SettingsNames.SettingsCantOpen),
            _ => (SettingsNames.SettingsMissing, SettingsNames.SettingsMissingDetail),
        };

        if (report.Statusline is { } sl)
        {
            StatuslineSummary = sl.IsSanduhr ? SettingsNames.StatuslineSanduhrs : SettingsNames.StatuslineSomeoneElses;
            StatuslineCommand = StreamerMode.FoldersIn(sl.Command, [Folder, .. _host.Folders().Select(f => f.Path)]);   // slice 7, CP 9: the folder's own name too   // streamer: masked - CP 9: FoldersIn masks on this line
        }
        else
        {
            StatuslineSummary = SettingsNames.HoldsNone;
            StatuslineCommand = "";
        }

        foreach (var h in report.Hooks)
            HookLines.Add(new ModsLine(h.Event, SettingsNames.HookGroups(h.Groups, h.Sanduhr)));
        int groups = report.Hooks.Sum(h => h.Groups);
        HooksSummary = report.Hooks.Count == 0 ? SettingsNames.HoldsNone : SettingsNames.HooksOnEvents(groups, report.Hooks.Count);

        foreach (var (name, on) in report.EnabledPlugins.OrderBy(p => p.Key, StringComparer.Ordinal))   // streamer: masked - CP 10: the add-on name passes Mask.AddOn on the next line
            PluginLines.Add(new ModsLine(StreamerMode.Mask.AddOn(name), on ? SettingsNames.FolderOn : SettingsNames.FolderOff));   // CP 10
        int pluginsOn = report.EnabledPlugins.Count(p => p.Value);   // streamer: key - a count, never drawn
        PluginsSummary = ModsPageText.SwitchSummary(pluginsOn, report.EnabledPlugins.Count - pluginsOn);

        foreach (var n in report.Mcp.UserServers) McpNames.Add(StreamerMode.Mask.AddOn(n));   // slice 7, CP 10   // streamer: masked - CP 10: Mask.AddOn masks on this line
        McpSummary = report.Mcp.UserServers.Count == 0 ? SettingsNames.HoldsNone : SettingsNames.McpServerCount(report.Mcp.UserServers.Count);   // streamer: key - a count, never drawn
        McpProjects = report.Mcp.LocalProjectCount > 0 ? SettingsNames.McpProjectsOwn(report.Mcp.LocalProjectCount) : "";

        ClaudeMdSummary = report.ClaudeMd.Exists
            ? SettingsNames.ClaudeMdSize(report.ClaudeMd.Lines, report.ClaudeMd.Bytes)
            : SettingsNames.HoldsNone;

        RetentionWarning = report.Retention.Warning;
        RetentionSummary = report.Retention.Warning
            ? ModInventory.RetentionWarning + "."
            : report.Retention.CleanupPeriodDays is { } days ? SettingsNames.RetentionDays(days) : SettingsNames.RetentionUnknown;
        Loaded = true;
    }
}
