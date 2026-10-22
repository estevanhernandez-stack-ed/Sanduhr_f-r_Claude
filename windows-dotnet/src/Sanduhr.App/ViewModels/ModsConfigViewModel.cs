using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>A folder in the Mods &amp; Config picker.</summary>
public sealed record ModsFolderOption(string Path, string Display);

/// <summary>What Mods &amp; Config needs from the app, so the page can be built over a scratch profile.</summary>
internal sealed record ModsConfigHost(
    string Profile,
    string? ConfigDir,
    Func<IReadOnlyList<string>> AddedFolders,
    string SanduhrAppDataDir,
    string SanduhrExternalDir,
    string SanduhrLegacyModsRoot,
    Func<string?> ActiveAccountFolder)
{
    public static ModsConfigHost For(WidgetViewModel widget) => new(
        ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded,
        widget.SanduhrAppDataDir, widget.SanduhrExternalDir, widget.SanduhrLegacyModsRoot,
        () => widget.ShownActiveAccount is { } label ? widget.AccountFolder(label) : null);
}

/// <summary>One line under a section: a name and what it says.</summary>
public sealed record ModsLine(string Name, string Text);

/// <summary>One plugin a folder loads. A plugin that also carries mod hooks is a mod and wears a "mod" tag.</summary>
public sealed partial class ModRowViewModel : ObservableObject
{
    private readonly ModsConfigViewModel _owner;

    public string Name { get; }
    public string VersionText { get; }
    public string Description { get; }
    public string StateText { get; }
    public string Touches { get; }
    public bool IsSanduhr { get; }
    /// <summary>The plugin is a Claude Code mod (its hooks list modules).</summary>
    public bool IsMod { get; }
    public bool IsOn { get; }
    public bool IsOff { get; }
    public bool HasDescription => Description.Length > 0;
    public bool HasTouches => Touches.Length > 0;
    public string TurnOnName => $"Turn on {Name}";
    public string TurnOffName => $"Turn off {Name}";
    public string UpdateName => $"Update {Name}";
    public string RemoveName => $"Remove {Name}";

    public IRelayCommand TurnOnCommand { get; }
    public IRelayCommand TurnOffCommand { get; }
    public IRelayCommand UpdateCommand { get; }
    public IRelayCommand RemoveCommand { get; }

    internal ModRowViewModel(ModsConfigViewModel owner, ModItem item, OwnModState? own)
    {
        _owner = owner;
        Name = item.Name;
        VersionText = item.Version is { Length: > 0 } v ? "v" + v.TrimStart('v') : "";
        Description = item.Description;
        IsSanduhr = item.IsSanduhr && own is not null;
        IsMod = item.IsMod;
        StateText = item.State switch
        {
            ModState.On when !ModsPageText.HasFiles(item) => "On, no files here",
            ModState.Off when !ModsPageText.HasFiles(item) => "Off, no files here",
            ModState.On => "On",
            ModState.Off => "Off",
            _ => "Files missing",
        };
        IsOn = IsSanduhr && own!.IsOn;
        IsOff = IsSanduhr && own!.Listed && !own.IsOn;
        Touches = ModsConfigViewModel.DescribeTouches(item.Touches);
        TurnOnCommand = new RelayCommand(() => _owner.SwitchOwnMod(true));
        TurnOffCommand = new RelayCommand(() => _owner.SwitchOwnMod(false));
        UpdateCommand = new RelayCommand(_owner.UpdateOwnMod);
        RemoveCommand = new RelayCommand(_owner.RemoveOwnMod);
    }
}

/// <summary>
/// Mods &amp; Config, slice 1: what one Claude Code folder holds, read from its files and never run. The sections
/// each open with a one-line summary: settings.json, the statusline, hooks, plugins, mods, MCP servers,
/// CLAUDE.md and how long transcripts are kept. Secret values never show (<see cref="ConfigMask"/>). The one
/// thing that changes anything is Sanduhr's own meters mod row: On, Off, Update and Remove.
/// </summary>
public sealed partial class ModsConfigViewModel : ObservableObject
{
    private readonly ModsConfigHost _host;
    private Window? _owner;
    private bool _loading;

    /// <summary>The folder picked on this page earlier in the session. The Settings window is rebuilt each time it
    /// opens; this is what keeps the choice.</summary>
    private static string? s_sessionFolder;

    internal static void ForgetSessionChoice() => s_sessionFolder = null;

    public ObservableCollection<ModsFolderOption> Folders { get; } = new();
    public ObservableCollection<ModsLine> HookLines { get; } = new();
    public ObservableCollection<ModsLine> PluginLines { get; } = new();
    public ObservableCollection<ModRowViewModel> Mods { get; } = new();
    public ObservableCollection<string> McpNames { get; } = new();

    [ObservableProperty] private ModsFolderOption? _selectedFolder;

    [ObservableProperty] private string _settingsSummary = "";
    [ObservableProperty] private string _settingsDetail = "";
    [ObservableProperty] private bool _settingsUnreadable;
    [ObservableProperty] private string _statuslineSummary = "";
    [ObservableProperty] private string _statuslineCommand = "";
    [ObservableProperty] private string _hooksSummary = "";
    [ObservableProperty] private string _pluginsSummary = "";
    /// <summary>The list's heading: "Plugins (46 installed, 41 on)".</summary>
    [ObservableProperty] private string _pluginListTitle = "";
    /// <summary>Why the list and the settings.json switches differ in size; empty when they agree.</summary>
    [ObservableProperty] private string _pluginListNote = "";
    [ObservableProperty] private string _mcpSummary = "";
    [ObservableProperty] private string _mcpProjects = "";
    [ObservableProperty] private string _claudeMdSummary = "";
    [ObservableProperty] private string _retentionSummary = "";
    [ObservableProperty] private bool _retentionWarning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasFolders;

    /// <summary>Raised after the meters mod was switched, updated or removed here, so the Claude Code page re-reads.</summary>
    public event Action? OwnModChanged;

    public ModsConfigViewModel(WidgetViewModel widget) : this(ModsConfigHost.For(widget)) { }

    internal ModsConfigViewModel(ModsConfigHost host)
    {
        _host = host;
        ReloadFolders();
    }

    public void AttachOwner(Window owner) => _owner = owner;

    private string Profile => _host.Profile;

    private ClaudeCodeIntegrations MakeIntegrations() => new(
        Profile, _host.SanduhrAppDataDir, Path.Combine(AppContext.BaseDirectory, "mods", ClaudeCodeIntegrations.ModName),
        externalDir: _host.SanduhrExternalDir)
    {
        LegacyModsRoots = [_host.SanduhrLegacyModsRoot],
    };

    /// <summary>Re-reads the folder list (the Claude Code page added or forgot one) and then the selected folder.</summary>
    public void ReloadFolders()
    {
        var keep = SelectedFolder?.Path ?? s_sessionFolder;
        _loading = true;
        Folders.Clear();
        foreach (var f in ClaudeCodeFolders.Build(Profile, _host.ConfigDir, _host.AddedFolders()))
            Folders.Add(new ModsFolderOption(f.Path, ClaudeCodeFolders.Display(f.Path, Profile)));
        HasFolders = Folders.Count > 0;
        string? active = null;
        try { active = _host.ActiveAccountFolder(); } catch (Exception) { }
        var pick = ModsFolderChoice.Pick([.. Folders.Select(f => f.Path)], keep, _host.ConfigDir, active, ModInventory.EnabledPluginCount);
        SelectedFolder = Folders.FirstOrDefault(f => pick is not null && ClaudeCodeFolders.Same(f.Path, pick));
        _loading = false;
        Refresh();
    }

    partial void OnSelectedFolderChanged(ModsFolderOption? value)
    {
        if (_loading) return;
        if (value is not null) s_sessionFolder = value.Path;
        Refresh();
    }

    /// <summary>Reads the selected folder again. Files only; nothing runs and nothing is written.</summary>
    [RelayCommand]
    public void Refresh()
    {
        HookLines.Clear();
        PluginLines.Clear();
        Mods.Clear();
        McpNames.Clear();
        if (SelectedFolder is not { } folder)
        {
            SettingsSummary = "No Claude Code folder found.";
            SettingsDetail = "Add one on the Claude Code page.";
            SettingsUnreadable = false;
            StatuslineSummary = HooksSummary = PluginsSummary = PluginListTitle = PluginListNote = McpSummary = ClaudeMdSummary = RetentionSummary = "";
            StatuslineCommand = McpProjects = "";
            RetentionWarning = false;
            return;
        }

        FolderReport report;
        try { report = ModInventory.Read(folder.Path, Profile); }
        catch (Exception)
        {
            SettingsSummary = "Couldn't read this folder.";
            return;
        }

        // Settings
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

        // Statusline
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

        // Hooks
        foreach (var h in report.Hooks)
            HookLines.Add(new ModsLine(h.Event,
                $"{h.Groups} {(h.Groups == 1 ? "group" : "groups")}" + (h.Sanduhr > 0 ? $", {h.Sanduhr} from Sanduhr" : "")));
        int groups = report.Hooks.Sum(h => h.Groups);
        HooksSummary = report.Hooks.Count == 0
            ? "None"
            : $"{groups} {(groups == 1 ? "group" : "groups")} on {report.Hooks.Count} {(report.Hooks.Count == 1 ? "event" : "events")}";

        // Plugins
        foreach (var (name, on) in report.EnabledPlugins.OrderBy(p => p.Key, StringComparer.Ordinal))
            PluginLines.Add(new ModsLine(name, on ? "On" : "Off"));
        int pluginsOn = report.EnabledPlugins.Count(p => p.Value);
        int pluginsOff = report.EnabledPlugins.Count - pluginsOn;
        PluginsSummary = ModsPageText.SwitchSummary(pluginsOn, pluginsOff);

        // The plugins the folder loads, mods among them
        OwnModState? own = null;
        try { own = MakeIntegrations().OwnModStateOf(folder.Path); } catch (Exception) { }
        foreach (var item in report.Mods)
            Mods.Add(new ModRowViewModel(this, item, own));
        var tally = ModsPageText.Tally(report.Mods, report.EnabledPlugins);
        PluginListTitle = ModsPageText.ListTitle(tally);
        PluginListNote = ModsPageText.Reconcile(tally, pluginsOn, pluginsOff);

        // MCP servers: names only
        foreach (var n in report.Mcp.UserServers) McpNames.Add(n);
        McpSummary = report.Mcp.UserServers.Count == 0
            ? "None"
            : $"{report.Mcp.UserServers.Count} {(report.Mcp.UserServers.Count == 1 ? "server" : "servers")}";
        McpProjects = report.Mcp.LocalProjectCount > 0
            ? $"{report.Mcp.LocalProjectCount} {(report.Mcp.LocalProjectCount == 1 ? "project has" : "projects have")} their own servers."
            : "";

        // CLAUDE.md
        ClaudeMdSummary = report.ClaudeMd.Exists
            ? $"{report.ClaudeMd.Lines} {(report.ClaudeMd.Lines == 1 ? "line" : "lines")}, {FormatBytes(report.ClaudeMd.Bytes)}"
            : "None";

        // History kept
        RetentionWarning = report.Retention.Warning;
        RetentionSummary = report.Retention.Warning
            ? ModInventory.RetentionWarning + "."
            : report.Retention.CleanupPeriodDays is { } days ? $"Kept for {days} days." : "Unknown while settings.json is unreadable.";
    }

    private static string FormatBytes(long bytes) =>
        bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024.0:0.#} KB";

    internal static string DescribeTouches(ModTouches t)
    {
        var parts = new List<string>();
        if (t.Surfaces.Count > 0)
            parts.Add("Draws " + string.Join(", ", t.Surfaces.Select(s => s switch
            {
                ModSurface.Band => "a band",
                ModSurface.Pane => "a pane",
                ModSurface.Status => "the status line",
                ModSurface.Toast => "toasts",
                _ => "slash commands",
            })));
        if (t.Capabilities.Count > 0)
            parts.Add("can " + string.Join(", ", t.Capabilities.Select(c => c switch
            {
                ModCapability.Process => "run programs",
                ModCapability.Http => "use the network",
                _ => "read and write files",
            })));
        if (parts.Count == 0) return "";
        var line = string.Join("; ", parts);
        return char.ToUpperInvariant(line[0]) + line[1..] + ".";
    }

    // ------------------------------------------------------------------ Sanduhr's meters mod

    private string? FolderPath => SelectedFolder?.Path;

    internal void SwitchOwnMod(bool on)
    {
        if (FolderPath is not { } path) return;
        try
        {
            var res = MakeIntegrations().SwitchOwnMod(path, on);
            StatusText = res.Ok ? (on ? "Meters mod turned on." : "Meters mod turned off.")
                                : Short(res.Message, "Couldn't change the switch.");
        }
        catch (Exception) { StatusText = "Couldn't change the switch."; }
        Refresh();
        OwnModChanged?.Invoke();
    }

    internal void UpdateOwnMod()
    {
        if (FolderPath is not { } path) return;
        try
        {
            var integ = MakeIntegrations();
            var state = integ.OwnModStateOf(path);
            var version = state.Pinned ?? "the installed version";
            var current = integ.CurrentModStamp() ?? "the new version";
            var ask = ThemedDialog.Show(_owner, "Update the meters mod?",
                $"This folder's meters mod is pinned to {version}. Updating points it at {current}, "
                + "the version that ships with this Sanduhr. The mod's files are copied again and one entry in "
                + "this folder's settings.json changes. The old version's files stay until Sanduhr prunes them.",
                MessageBoxButton.YesNo, ThemedDialogKind.Info, primaryLabel: "Update", secondaryLabel: "Not now");
            if (ask != MessageBoxResult.Yes) return;
            var res = integ.UpdateOwnMod(path);
            StatusText = !res.Ok ? Short(res.Message, "Couldn't update the meters mod.")
                : res.Outcome == ModUpdateOutcome.Updated ? "Meters mod updated." : "Meters mod is already current.";
        }
        catch (Exception) { StatusText = "Couldn't update the meters mod."; }
        Refresh();
        OwnModChanged?.Invoke();
    }

    internal void RemoveOwnMod()
    {
        if (FolderPath is not { } path) return;
        try
        {
            var ask = ThemedDialog.Show(_owner, "Remove the meters mod?",
                "Takes Sanduhr's entry out of this folder's settings.json and leaves any other mod alone.",
                MessageBoxButton.YesNo, ThemedDialogKind.Warning, primaryLabel: "Remove", secondaryLabel: "Cancel");
            if (ask != MessageBoxResult.Yes) return;
            var res = MakeIntegrations().Remove(IntegrationKind.Meters, path);
            StatusText = res.Ok ? "Meters mod removed." : Short(res.Message, "Couldn't remove the meters mod.");
        }
        catch (Exception) { StatusText = "Couldn't remove the meters mod."; }
        Refresh();
        OwnModChanged?.Invoke();
    }

    private string Short(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback : message.Replace(Profile, "~", StringComparison.OrdinalIgnoreCase);
}
