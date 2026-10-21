using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>The five parts of Reset Sanduhr…, in the order they run (the Mac's <c>ResetPart</c>, less its Desk messages).</summary>
public enum ResetPart
{
    /// <summary>Sanduhr's entries in Claude Code folders, undone first: their receipts live in a folder a later part deletes.</summary>
    ClaudeCodeEntries,
    /// <summary>Remove Account for each account, the publish token, every WebView2 profile.</summary>
    Accounts,
    /// <summary>History files, snapshots, the vault, handoff and signal files, logs.</summary>
    History,
    /// <summary>The themes folder.</summary>
    Themes,
    /// <summary><c>settings.json</c>, last.</summary>
    Settings,
}

/// <summary>Which parts the user left checked. All on by default.</summary>
public sealed record ResetChoices(
    bool ClaudeCodeEntries = true,
    bool Accounts = true,
    bool History = true,
    bool Themes = true,
    bool Settings = true)
{
    public bool Includes(ResetPart part) => part switch
    {
        ResetPart.ClaudeCodeEntries => ClaudeCodeEntries,
        ResetPart.Accounts => Accounts,
        ResetPart.History => History,
        ResetPart.Themes => Themes,
        ResetPart.Settings => Settings,
        _ => false,
    };
}

/// <summary>What one step did. <see cref="Failures"/> are short phrases naming kinds of things
/// ("a Claude Code entry", "a profile folder"), never a path, an account label or a folder name.</summary>
public sealed record ResetStepResult(ResetPart Part, bool Ok, IReadOnlyList<string> Failures)
{
    public static ResetStepResult Done(ResetPart part) => new(part, true, []);

    public static ResetStepResult Failed(ResetPart part, IEnumerable<string> failures)
    {
        var list = failures.ToList();
        return new(part, list.Count == 0, list);
    }
}

/// <summary>One file or folder a part deletes.</summary>
public sealed record ResetItem(string Path, bool IsDirectory);

/// <summary>
/// Reset Sanduhr… (spec 4.7): the ordered steps, the keep-going run, and the inventory of what each
/// part deletes. The run takes its actions as a delegate, so order and the keep-going rule are tested
/// without deleting anything; <see cref="Inventory"/> is the dry run. The <c>backups\</c> folder is
/// never listed: Claude Code files Sanduhr changed keep their timestamped copies there.
/// </summary>
public static partial class ResetPlan
{
    public const string BackupsName = "backups";
    public const string IntegrationsName = "integrations";
    public const string ModsName = "mods";
    public const string McpName = "mcp";
    public const string BinName = "bin";
    public const string McpSettingsName = "mcp-settings.json";

    /// <summary>The sign-in window's WebView2 user-data root (<c>SignInCoordinator</c> makes a folder per attempt inside it).</summary>
    public const string SignInProfileName = "webview2";

    [GeneratedRegex("^webview2-[0-9a-f]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountProfileName();

    /// <summary>True for a per-account profile folder name: <c>webview2-</c> and eight lowercase hex digits.
    /// Not the shared <c>webview2-fetch</c> folder and not the sign-in <c>webview2</c> root.</summary>
    public static bool IsAccountProfileName(string name) => AccountProfileName().IsMatch(name);

    /// <summary>The per-account profile folders that exist right now, found by listing so accounts
    /// already gone are covered too.</summary>
    public static IReadOnlyList<string> AccountProfileDirs(Paths paths)
    {
        try
        {
            if (!Directory.Exists(paths.AppDataDir)) return [];
            return [.. Directory.EnumerateDirectories(paths.AppDataDir)
                .Where(d => IsAccountProfileName(Path.GetFileName(d)))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The checked parts, in run order.</summary>
    public static IReadOnlyList<ResetPart> Steps(ResetChoices choices) =>
        [.. Enum.GetValues<ResetPart>().Where(choices.Includes)];

    /// <summary>
    /// Runs every step in order. A step that throws becomes a failed result and the rest still run.
    /// The exception's text is dropped: it can carry a path.
    /// </summary>
    public static async Task<IReadOnlyList<ResetStepResult>> RunAsync(
        IReadOnlyList<ResetPart> steps, Func<ResetPart, Task<ResetStepResult>> run)
    {
        var results = new List<ResetStepResult>(steps.Count);
        foreach (var part in steps)
        {
            try
            {
                results.Add(await run(part));
            }
            catch (Exception)
            {
                results.Add(new ResetStepResult(part, false, ["the step stopped unexpectedly"]));
            }
        }
        return results;
    }

    /// <summary>What each part deletes right now: the files and folders that exist, per part.
    /// Account profile folders are found by listing, so accounts already gone are covered too.</summary>
    public static IReadOnlyDictionary<ResetPart, IReadOnlyList<ResetItem>> Inventory(Paths paths)
    {
        string app = paths.AppDataDir;
        string local = Path.GetDirectoryName(paths.VaultDir)!;
        // The external folder (%USERPROFILE%\.sanduhr) is shared ground: only Sanduhr's own names are
        // listed, never the folder itself, and the path is read without creating it.
        string ext = paths.ExternalDirPath;
        string backups = Path.Combine(app, BackupsName);
        var map = new Dictionary<ResetPart, IReadOnlyList<ResetItem>>();

        // Claude Code entries: the receipts file and Sanduhr's copies, in ~\.sanduhr and the pre-3.5.0
        // AppData places. The service deletes these only after the undo succeeded, since a failed folder
        // keeps its receipt so Settings can still remove it.
        var cc = new List<ResetItem>();
        AddDir(cc, Path.Combine(app, IntegrationsName));
        foreach (var name in new[] { ModsName, McpName, BinName })
        {
            AddDir(cc, Path.Combine(ext, name));
            AddDir(cc, Path.Combine(app, name));
        }
        AddFile(cc, Path.Combine(ext, McpSettingsName));
        map[ResetPart.ClaudeCodeEntries] = Finish(cc, backups);

        var accounts = new List<ResetItem>();
        AddMatchingDirs(accounts, app, name => AccountProfileName().IsMatch(name));
        AddDir(accounts, paths.WebView2FetchDir);
        AddDir(accounts, Path.Combine(app, SignInProfileName));
        AddFile(accounts, paths.LegacyConfigFile);
        map[ResetPart.Accounts] = Finish(accounts, backups);

        var history = new List<ResetItem>();
        AddMatchingFiles(history, app, "history*.json");
        AddDir(history, paths.VaultDir);
        AddFile(history, Path.Combine(local, "last-update-check.txt"));
        AddFile(history, paths.LegacyHistoryFile);
        AddFile(history, paths.LastErrorFile);
        AddMatchingFiles(history, app, "sanduhr.log*");
        AddFile(history, Path.Combine(app, "fetch-debug.log"));
        AddFile(history, Path.Combine(app, "signin-debug.log"));
        // The snapshots, seats, handoff and signal files live in ~\.sanduhr since 3.5.0; builds before it
        // wrote them in AppData. Each is taken from both places. Includes S6's handoffs: Claude's
        // suggested Desk messages and watcher requests (spec 6.9).
        foreach (var dir in new[] { ext, app })
        {
            AddMatchingFiles(history, dir, "snapshot*.json");
            AddFile(history, Path.Combine(dir, SeatFiles.SeatsName));
            AddFile(history, Path.Combine(dir, PublishHandoff.RequestFileName));
            AddFile(history, Path.Combine(dir, PublishHandoff.ResultFileName));
            AddFile(history, Path.Combine(dir, ThemeHandoff.RequestFileName));
            AddFile(history, Path.Combine(dir, ThemeHandoff.ResultFileName));
            AddMatchingFiles(history, dir, "glow-*.signal");
            AddMatchingFiles(history, dir, "desk-messages-*.json");
            AddMatchingFiles(history, dir, "watch-request-*.json");
        }
        map[ResetPart.History] = Finish(history, backups);

        var themes = new List<ResetItem>();
        AddDir(themes, paths.ThemesDir);
        map[ResetPart.Themes] = Finish(themes, backups);

        var settings = new List<ResetItem>();
        // The Desk messages (spec 6.2) are the user's own lines, kept with the settings.
        AddFile(settings, Path.Combine(app, "messages.txt"));
        AddFile(settings, Path.Combine(app, "messages.txt.previous"));
        AddFile(settings, paths.SettingsFile);
        // The Launcher's button images (spec, Desk Launcher) are chosen settings, kept and removed with them.
        AddDir(settings, paths.LauncherDir);
        // A stale projection must not outlive the consent it copies; the watchers switch is a copy of one too.
        AddFile(settings, Path.Combine(ext, McpSettingsName));
        AddFile(settings, Path.Combine(ext, WatcherRequest.SwitchFile));
        map[ResetPart.Settings] = Finish(settings, backups);

        return map;
    }

    /// <summary>Drops anything inside <c>backups\</c> (nothing above should name it; this is the belt) and
    /// anything listed twice.</summary>
    private static IReadOnlyList<ResetItem> Finish(List<ResetItem> items, string backups)
    {
        string root = Path.GetFullPath(backups).TrimEnd('\\', '/');
        return [.. items
            .Where(i =>
            {
                string full = Path.GetFullPath(i.Path).TrimEnd('\\', '/');
                return !full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            })
            .DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase)];
    }

    private static void AddFile(List<ResetItem> to, string path)
    {
        if (File.Exists(path)) to.Add(new ResetItem(path, false));
    }

    private static void AddDir(List<ResetItem> to, string path)
    {
        if (Directory.Exists(path)) to.Add(new ResetItem(path, true));
    }

    private static void AddMatchingFiles(List<ResetItem> to, string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            to.Add(new ResetItem(f, false));
    }

    private static void AddMatchingDirs(List<ResetItem> to, string dir, Func<string, bool> match)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            if (match(Path.GetFileName(d))) to.Add(new ResetItem(d, true));
    }
}
