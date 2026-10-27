using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// What the Claude Code cards and Mods &amp; Widgets need from the app: the profile, the folders, Sanduhr's own folders and the meters mod's
/// files. A record, so tests build the views over a scratch profile (it replaces Mods &amp; Config's host, and adds the mod source).
/// </summary>
internal sealed record ClaudeCodeHost(
    string Profile,
    string? ConfigDir,
    Func<IReadOnlyList<string>> AddedFolders,
    string SanduhrAppDataDir,
    string SanduhrExternalDir,
    string SanduhrLegacyModsRoot,
    Func<string?> ActiveAccountFolder,
    string ModSourceDir,
    Func<string, string, FolderReport>? ReadReport = null)
{
    public static ClaudeCodeHost For(WidgetViewModel widget) => new(
        ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded,   // streamer: notname - profile folders handed to the folder discovery, never drawn
        widget.SanduhrAppDataDir, widget.SanduhrExternalDir, widget.SanduhrLegacyModsRoot,
        () => widget.ShownActiveAccount is { } label ? widget.AccountFolder(label) : null,   // streamer: key - folder lookup for the active account, never drawn
        Path.Combine(AppContext.BaseDirectory, "mods", ClaudeCodeIntegrations.ModName));

    public ClaudeCodeIntegrations Integrations() =>
        new(Profile, SanduhrAppDataDir, ModSourceDir, externalDir: SanduhrExternalDir) { LegacyModsRoots = [SanduhrLegacyModsRoot] };

    /// <summary>Every known folder: found first, then added by hand.</summary>
    public IReadOnlyList<CcFolder> Folders() => ClaudeCodeFolders.Build(Profile, ConfigDir, AddedFolders());

    /// <summary>The path with the profile folder written as ~.</summary>
    /// <remarks>Streamer mode (slice 7, CP 7): the folder's codename while hidden. Cards, consents, Mods &amp; Widgets' meters rows and
    /// Privacy's folder-reads row all read their folder names here.</remarks>
    public string Display(string path) => StreamerMode.ShowFolder(path, Profile);

    /// <summary>Every known folder read for the one list: its report, Sanduhr's mod state and the meters status. Files only; never throws.</summary>
    public IReadOnlyList<FolderMods> ReadFolders()
    {
        var integ = Integrations();
        return [.. Folders().Select(f => ReadFolder(f.Path, integ))];   // streamer: key - reads each folder's report; the page draws folders through Display (CP 7)
    }

    /// <summary>One folder read the same way. A read that throws reads as a folder whose settings.json can't be read, so the others still list.</summary>
    public FolderMods ReadFolder(string folder, ClaudeCodeIntegrations? integ = null)
    {
        integ ??= Integrations();
        FolderReport report;
        try { report = (ReadReport ?? ModInventory.Read)(folder, Profile); }
        catch (Exception)
        {
            report = new FolderReport(folder, true, new SettingsState(SettingsKind.Unreadable), null, [], new Dictionary<string, bool>(),
                [], new McpSummary([], 0), new ClaudeMdInfo(false, 0, 0), new RetentionInfo(null, false, null));
        }
        OwnModState? own = null;
        var meters = IntegrationStatus.NotInstalled;
        try
        {
            own = integ.OwnModStateOf(folder);
            meters = integ.Status(IntegrationKind.Meters, folder);
        }
        catch (Exception) { /* a folder that can't be read reads as nothing of Sanduhr's */ }
        return new FolderMods(folder, Display(folder), report, own, meters);
    }
}
