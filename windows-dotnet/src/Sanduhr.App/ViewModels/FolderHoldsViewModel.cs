using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// A Claude Code folder card's "What this folder holds" (Settings v2 slice 4; Mods &amp; Config's read-only report, WF4, WF9): settings.json,
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
        try { report = ModInventory.Read(Folder, _host.Profile); }
        catch (Exception)
        {
            SettingsSummary = "Couldn't read this folder.";
            Loaded = true;
            return;
        }

        SettingsUnreadable = report.Settings.Kind == SettingsKind.Unreadable;
        (SettingsSummary, SettingsDetail) = report.Settings.Kind switch
        {
            SettingsKind.Readable => ("Readable", "settings.json parses as JSON."),
            SettingsKind.Unreadable when report.Settings.Line > 0 =>
                ($"Unreadable: line {report.Settings.Line}, column {report.Settings.Column}",
                 "Claude Code reads settings.json strictly: no comments, no trailing commas. Sanduhr changes nothing in it while it is unreadable."),
            SettingsKind.Unreadable => ("Unreadable", "settings.json can't be opened. Sanduhr changes nothing in it."),
            _ => ("Missing", "This folder has no settings.json yet."),
        };

        if (report.Statusline is { } sl)
        {
            StatuslineSummary = sl.IsSanduhr ? "Sanduhr's statusline" : "Someone else's statusline";
            StatuslineCommand = sl.Command;
        }
        else
        {
            StatuslineSummary = "None";
            StatuslineCommand = "";
        }

        foreach (var h in report.Hooks)
            HookLines.Add(new ModsLine(h.Event,
                $"{h.Groups} {(h.Groups == 1 ? "group" : "groups")}" + (h.Sanduhr > 0 ? $", {h.Sanduhr} from Sanduhr" : "")));
        int groups = report.Hooks.Sum(h => h.Groups);
        HooksSummary = report.Hooks.Count == 0
            ? "None"
            : $"{groups} {(groups == 1 ? "group" : "groups")} on {report.Hooks.Count} {(report.Hooks.Count == 1 ? "event" : "events")}";

        foreach (var (name, on) in report.EnabledPlugins.OrderBy(p => p.Key, StringComparer.Ordinal))
            PluginLines.Add(new ModsLine(name, on ? "On" : "Off"));
        int pluginsOn = report.EnabledPlugins.Count(p => p.Value);
        PluginsSummary = ModsPageText.SwitchSummary(pluginsOn, report.EnabledPlugins.Count - pluginsOn);

        foreach (var n in report.Mcp.UserServers) McpNames.Add(n);
        McpSummary = report.Mcp.UserServers.Count == 0
            ? "None"
            : $"{report.Mcp.UserServers.Count} {(report.Mcp.UserServers.Count == 1 ? "server" : "servers")}";
        McpProjects = report.Mcp.LocalProjectCount > 0
            ? $"{report.Mcp.LocalProjectCount} {(report.Mcp.LocalProjectCount == 1 ? "project has" : "projects have")} their own servers."
            : "";

        ClaudeMdSummary = report.ClaudeMd.Exists
            ? $"{report.ClaudeMd.Lines} {(report.ClaudeMd.Lines == 1 ? "line" : "lines")}, {FormatBytes(report.ClaudeMd.Bytes)}"
            : "None";

        RetentionWarning = report.Retention.Warning;
        RetentionSummary = report.Retention.Warning
            ? ModInventory.RetentionWarning + "."
            : report.Retention.CleanupPeriodDays is { } days ? $"{days} days." : "Unknown while settings.json is unreadable.";
        Loaded = true;
    }

    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024.0:0.#} KB";
}
