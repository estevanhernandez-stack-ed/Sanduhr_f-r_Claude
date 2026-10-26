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
    string ModSourceDir)
{
    public static ClaudeCodeHost For(WidgetViewModel widget) => new(
        ClaudeCodeProfile.Dir, ClaudeCodeProfile.ConfigDir, widget.LoadCcFoldersAdded,
        widget.SanduhrAppDataDir, widget.SanduhrExternalDir, widget.SanduhrLegacyModsRoot,
        () => widget.ShownActiveAccount is { } label ? widget.AccountFolder(label) : null,
        Path.Combine(AppContext.BaseDirectory, "mods", ClaudeCodeIntegrations.ModName));

    public ClaudeCodeIntegrations Integrations() =>
        new(Profile, SanduhrAppDataDir, ModSourceDir, externalDir: SanduhrExternalDir) { LegacyModsRoots = [SanduhrLegacyModsRoot] };

    /// <summary>Every known folder: found first, then added by hand.</summary>
    public IReadOnlyList<CcFolder> Folders() => ClaudeCodeFolders.Build(Profile, ConfigDir, AddedFolders());

    /// <summary>The path with the profile folder written as ~.</summary>
    public string Display(string path) => ClaudeCodeFolders.Display(path, Profile);

    /// <summary>Every known folder read for the one list: its report, Sanduhr's mod state and the meters status. Files only; never throws.</summary>
    public IReadOnlyList<FolderMods> ReadFolders()
    {
        var integ = Integrations();
        var list = new List<FolderMods>();
        foreach (var f in Folders())
        {
            var report = ModInventory.Read(f.Path, Profile);
            OwnModState? own = null;
            var meters = IntegrationStatus.NotInstalled;
            try
            {
                own = integ.OwnModStateOf(f.Path);
                meters = integ.Status(IntegrationKind.Meters, f.Path);
            }
            catch (Exception) { /* a folder that can't be read reads as nothing of Sanduhr's */ }
            list.Add(new FolderMods(f.Path, Display(f.Path), report, own, meters));
        }
        return list;
    }
}
