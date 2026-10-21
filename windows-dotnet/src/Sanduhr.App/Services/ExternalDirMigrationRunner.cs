using System.IO;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// App side of <see cref="ExternalDirMigration"/> (spec Plan B, B.4): gathers every Claude Code folder and MCP home
/// Sanduhr knows, builds the engines the way the Claude Code page does, runs the one-time re-point to
/// <c>%USERPROFILE%\.sanduhr</c> and writes path-free lines to <c>sanduhr.log</c>. Meant for a background task: never
/// throws, never blocks start-up.
/// </summary>
public static class ExternalDirMigrationRunner
{
    public const string MarkerName = ".external-dir-migration";

    public static void Run(SettingsStore settings, Paths paths)
    {
        Action<string> log = msg =>
        {
            // PRIVACY.md contract: counts and exception type names only, never a path.
            try { File.AppendAllText(paths.LogFile, $"{DateTime.UtcNow:o} {msg}{Environment.NewLine}"); }
            catch { }
        };
        try
        {
            ExternalDirMigrationInputs inputs = BuildInputs(settings, paths);
            new ExternalDirMigration(log).Run(inputs);
        }
        catch (Exception e)
        {
            log($"external-dir-migration: runner failed ({e.GetType().Name})");
        }
    }

    internal static ExternalDirMigrationInputs BuildInputs(SettingsStore settings, Paths paths)
    {
        string profile = ClaudeCodeProfile.Dir;
        string appData = paths.AppDataDir;
        string external = paths.ExternalDir;

        var discovered = ClaudeCodeFolders.Build(profile, ClaudeCodeProfile.ConfigDir, settings.LoadCcFoldersAdded());
        var folders = discovered.Select(f => f.Path).ToList();
        try
        {
            var (receipts, _) = SnapshotConsumers.ReadReceipts(Path.Combine(appData, "integrations", "installs.json"));
            foreach (var r in receipts)
            {
                var folder = ClaudeCodeFolders.Norm(r.Folder);
                if (folder.Length > 0 && !folders.Any(f => ClaudeCodeFolders.Same(f, folder)))
                    folders.Add(folder);
            }
        }
        catch (Exception) { /* receipts unreadable: the known folders still run */ }

        var homes = discovered.Where(f => f.Discovered).Select(f => f.Name)
            .Concat(settings.LoadMcpCcHome() is { Length: > 0 } saved ? [saved] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string baseDir = AppContext.BaseDirectory;
        string modSource = Path.Combine(baseDir, "mods", ClaudeCodeIntegrations.ModName);
        string mcpSource = Path.Combine(baseDir, "mcp");

        var integrations = new ClaudeCodeIntegrations(profile, appData, modSource, externalDir: external)
        {
            LegacyModsRoots = [paths.LegacyModsRoot],
        };

        return new ExternalDirMigrationInputs(
            MarkerPath: Path.Combine(appData, MarkerName),
            Folders: folders,
            McpHomes: homes,
            LegacyStatuslineScript: paths.LegacyStatuslineScriptFile,
            LegacyMcpLauncher: paths.LegacyMcpLauncherFile,
            StatuslineInstaller: new StatuslineInstaller(profile, paths.StatuslineBinDir, null, appData),
            McpIntegrationInstaller: new McpIntegrationInstaller(profile, appData, externalDir: external),
            ClaudeCodeIntegrations: integrations,
            McpSourceDir: Directory.Exists(mcpSource) ? mcpSource : null,
            LegacyDir: string.Equals(Path.GetFullPath(appData).TrimEnd('\\'), Path.GetFullPath(external).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase) ? null : paths.AppDataDir);
    }
}
