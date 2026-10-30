using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Runs Reset Sanduhr… (spec 4.7) for the parts the user left checked, in <see cref="ResetPlan"/>'s
/// order, every step tried and none stopping the rest. Stops account follow and the seat meters first
/// (through <c>stopBackgroundWork</c>, so nothing writes back), removes each account through
/// <see cref="AccountStore.RemoveAccount"/>, clears the publish token, then deletes what
/// <see cref="ResetPlan.Inventory"/> lists. Failures are reported by kind ("a profile folder"), and
/// nothing here logs: not a path, not an account label.
///
/// The Claude Code undo is the orchestrator's: <c>undoClaudeCodeEntries</c> takes the folders (and kinds) to leave as they are and
/// returns the kinds of failures (empty when every entry went). Sanduhr's own copies of those entries (receipts, mods, the MCP server
/// copy, the bin scripts) are deleted only when the undo reported none and the receipts could be read, and then per kind: a kept
/// folder keeps the receipts, and the files and switch of each kind it still runs (the statusline script and switch for a kept
/// statusline or meters mod, the MCP launcher, server, projection and switch for a kept MCP entry, the mods for a kept meters mod).
/// A failed or kept folder keeps its receipt so Settings can still remove that entry after the next launch.
/// <c>backups\</c> is never touched.
/// </summary>
internal sealed class ResetService
{
    private readonly Paths _paths;
    private readonly AccountStore _accounts;
    private readonly PublishTokenStore _publishToken;
    private readonly Func<IReadOnlyCollection<ResetFolder>, Task<ClaudeCodeUndoResult>> _undoClaudeCodeEntries;
    private readonly Action _stopBackgroundWork;
    private readonly Func<Task<int>>? _deleteAccountProfiles;

    /// <summary>Turn the MCP pair and the statusline pair of switches off in settings.json once their entries are out (see
    /// <see cref="SettingsStore.ClearMcpSwitchesForReset"/>); null in contexts without a store.</summary>
    private readonly Action? _clearMcpSwitches;
    private readonly Action? _clearStatuslineSwitches;

    /// <summary>Puts every drawer file back on the Desktop (the drawers' turn-off); null in contexts without drawers.</summary>
    private readonly Func<DrawerReturnRun?>? _returnDrawers;

    public ResetService(
        Paths paths,
        AccountStore accounts,
        PublishTokenStore publishToken,
        Func<IReadOnlyCollection<ResetFolder>, Task<ClaudeCodeUndoResult>> undoClaudeCodeEntries,
        Action stopBackgroundWork,
        Func<Task<int>>? deleteAccountProfiles = null,
        Action? clearMcpSwitches = null,
        Action? clearStatuslineSwitches = null,
        Func<DrawerReturnRun?>? returnDrawers = null)
    {
        _returnDrawers = returnDrawers;
        _clearMcpSwitches = clearMcpSwitches;
        _clearStatuslineSwitches = clearStatuslineSwitches;
        _deleteAccountProfiles = deleteAccountProfiles;
        _paths = paths;
        _accounts = accounts;
        _publishToken = publishToken;
        _undoClaudeCodeEntries = undoClaudeCodeEntries;
        _stopBackgroundWork = stopBackgroundWork;
    }

    /// <summary>
    /// Whether ticking Settings leaves a kept MCP entry without its consent: <c>mcp-settings.json</c> and <c>mcp_enabled</c> go with the
    /// Settings part, so the kept server answers without usage until Install sets them again. The dialog shows
    /// <see cref="SettingsNames.ResetKeptMcpSettings"/> while this is true.
    /// </summary>
    public static bool KeptMcpLosesItsSettings(ResetChoices choices, IReadOnlyCollection<ResetFolder> keep)
        => choices.Settings && keep.Any(f => f.Kinds.Contains(IntegrationKind.Mcp));

    /// <summary>Runs the checked parts. <paramref name="started"/> is told which part is about to run.</summary>
    public async Task<IReadOnlyList<ResetStepResult>> RunAsync(ResetChoices choices, IReadOnlyCollection<ResetFolder> keepFolders, Action<ResetPart>? started = null)
    {
        // Follow and seat meters off before anything is removed, so nothing re-creates a file or
        // re-reads a key that is about to go. A stop that throws must not block the reset.
        try { _stopBackgroundWork(); } catch { /* the steps still run */ }

        var results = await ResetPlan.RunAsync(ResetPlan.Steps(choices), async part =>
        {
            started?.Invoke(part);
            return part switch
            {
                ResetPart.ClaudeCodeEntries => await ClaudeCodeEntriesAsync(keepFolders),
                ResetPart.Accounts => await AccountsAsync(),
                ResetPart.Settings => await SettingsAsync(),
                _ => await DeleteInventoryAsync(part),
            };
        });
        RemoveExternalDirIfEmpty();
        return results;
    }

    /// <summary>
    /// <c>%USERPROFILE%\.sanduhr</c> is shared ground: other tools keep their own names in it. After the
    /// parts ran it is removed only when nothing is left in it, never recursively, and a failure is swallowed.
    /// </summary>
    private void RemoveExternalDirIfEmpty()
    {
        try
        {
            string dir = _paths.ExternalDirPath;   // streamer: notname - a file location used for IO, never drawn
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: false);
        }
        catch (Exception)
        {
            // Not empty, in use, or not ours to remove: leave it.
        }
    }

    private async Task<ResetStepResult> ClaudeCodeEntriesAsync(IReadOnlyCollection<ResetFolder> keep)
    {
        var failures = new List<string>();
        // Read before the undo: a receipt save moves a damaged file aside and leaves a readable one (slice 8 decision 4, P1).
        bool unreadable = ResetPlan.ReceiptsUnreadable(_paths);
        ClaudeCodeUndoResult? undone = null;
        try
        {
            undone = await _undoClaudeCodeEntries(keep);
            failures.AddRange(undone.Failures);
        }
        catch (Exception)
        {
            failures.Add("a Claude Code entry");
        }

        bool Kept(IntegrationKind kind) => keep.Any(f => f.Kinds.Contains(kind));
        bool keepsMcp = Kept(IntegrationKind.Mcp);
        // The statusline switch also means "the meters mod reads the snapshot" (SnapshotConsumers), so a kept meters mod holds it.
        bool keepsStatusline = Kept(IntegrationKind.Statusline) || Kept(IntegrationKind.Meters);

        // Each kind's switch goes off by what the undo took out of that kind, whatever the other kinds or the receipts did: a
        // switch left on re-creates the projection, the launcher or the script at the next launch. A kind the undo could not take
        // out (a failure, or an entry it was not safe to open) keeps its switch, so its entry can still be removed. A Settings
        // part that runs later deletes the file anyway.
        if (undone is not null)
        {
            if (!keepsMcp && undone.McpOut)
                try { _clearMcpSwitches?.Invoke(); } catch (Exception) { /* best effort */ }
            if (!keepsStatusline && undone.StatuslineOut && !unreadable)
                try { _clearStatuslineSwitches?.Invoke(); } catch (Exception) { /* best effort */ }
        }

        // A failed entry (a statusline one included) keeps its receipt and the files it points at, so it can still be removed
        // later. So does every kept folder, and every entry when the receipts can't be read (R12): the receipts file never goes
        // after a failure. Past that the rest goes per kind, by what a kept entry still runs (P3).
        if (failures.Count == 0 && !unreadable)
        {
            failures.AddRange(await DeleteItemsAsync(ResetPart.ClaudeCodeEntries,
                item => !StaysForKept(item, keep.Count > 0, keepsMcp, Kept(IntegrationKind.Statusline), Kept(IntegrationKind.Meters))));
        }
        return ResetStepResult.Failed(ResetPart.ClaudeCodeEntries, Distinct(failures));
    }

    /// <summary>Whether a Claude Code inventory item stays because a kept entry still needs it. Named by leaf, like the inventory lists
    /// them: the receipts folder, the mods, the MCP server and its projection, and <c>bin\</c> (the statusline script and the MCP launcher).</summary>
    private static bool StaysForKept(ResetItem item, bool anyKept, bool mcp, bool statusline, bool meters)
    {
        string name = Path.GetFileName(item.Path.TrimEnd('\\', '/'));   // streamer: notname - leaf compared to Sanduhr's own names, never drawn
        if (name.Equals(ResetPlan.IntegrationsName, StringComparison.OrdinalIgnoreCase)) return anyKept;
        if (name.Equals(ResetPlan.ModsName, StringComparison.OrdinalIgnoreCase)) return meters;
        if (name.Equals(ResetPlan.McpName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(ResetPlan.McpSettingsName, StringComparison.OrdinalIgnoreCase)) return mcp;
        if (name.Equals(ResetPlan.BinName, StringComparison.OrdinalIgnoreCase)) return mcp || statusline;
        return anyKept;   // a name this doesn't know stays while anything is kept
    }

    private async Task<ResetStepResult> AccountsAsync()
    {
        var failures = new List<string>();

        IReadOnlyList<string> labels;
        try { labels = _accounts.ListAccounts().ToList(); }   // streamer: key - labels removed from the registry, never drawn
        catch (Exception)
        {
            labels = [];
            failures.Add("the account list");
        }
        foreach (var label in labels)
        {
            try { _accounts.RemoveAccount(label); }
            catch (Exception) { failures.Add("an account's sign-in keys"); }
        }

        try { _publishToken.Clear(); }
        catch (Exception) { failures.Add("the publish token"); }

        // The per-account profile folders go through the reader's gate, so a read in flight cannot hold
        // or recreate one; the inventory delete below then finds them gone.
        if (_deleteAccountProfiles is not null)
        {
            try
            {
                if (await _deleteAccountProfiles() > 0) failures.Add("a sign-in profile folder");
            }
            catch (Exception) { failures.Add("a sign-in profile folder"); }
        }

        failures.AddRange(await DeleteItemsAsync(ResetPart.Accounts));
        return ResetStepResult.Failed(ResetPart.Accounts, Distinct(failures));
    }

    /// <summary>Reset's drawers part (5.0 drawers spec, section 4) runs inside Settings, first: every drawer file goes back to the Desktop
    /// while the drawer settings that say where they are still exist. Reset never deletes a user's file.</summary>
    /// <summary>The failure line when Reset could not put every drawer file back.</summary>
    internal const string DrawerStayedFailure = "Some drawer files stayed in the Drawers folder on your Desktop.";

    private async Task<ResetStepResult> SettingsAsync()
    {
        var stayed = false;
        try
        {
            var run = _returnDrawers?.Invoke();
            stayed = run is not null && (run.KeptHomes.Count > 0 || run.Failure is not null);
        }
        catch (Exception) { stayed = true; /* the files stay in plain sight; the reset carries on */ }
        var result = await DeleteInventoryAsync(ResetPart.Settings);
        // Settings is deleted either way: the files are safe and in plain sight, and the step says so.
        return stayed ? ResetStepResult.Failed(ResetPart.Settings, [.. result.Failures, DrawerStayedFailure]) : result;
    }

    private async Task<ResetStepResult> DeleteInventoryAsync(ResetPart part)
        => ResetStepResult.Failed(part, Distinct(await DeleteItemsAsync(part)));

    private async Task<List<string>> DeleteItemsAsync(ResetPart part, Func<ResetItem, bool>? include = null)
    {
        var failures = new List<string>();
        IReadOnlyList<ResetItem> items;
        try { items = ResetPlan.Inventory(_paths)[part]; }
        catch (Exception)
        {
            failures.Add("the list of files");
            return failures;
        }
        foreach (var item in items.Where(i => include?.Invoke(i) ?? true))
            if (!await TryDeleteAsync(item))
                failures.Add(KindOf(part, item));
        return failures;
    }

    // WebView2's browser processes release their file locks a moment after the last window closes,
    // so a profile folder gets more tries than a plain file.
    private static async Task<bool> TryDeleteAsync(ResetItem item)
    {
        int attempts = item.IsDirectory ? 6 : 3;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                // Off the UI thread: a vault or a browser profile can be thousands of files.
                await Task.Run(() =>
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.Path))
                        {
                            ClearReadOnly(item.Path);
                            Directory.Delete(item.Path, recursive: true);
                        }
                    }
                    else if (File.Exists(item.Path))
                    {
                        File.SetAttributes(item.Path, FileAttributes.Normal);
                        File.Delete(item.Path);
                    }
                });
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(400);
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }

    private static void ClearReadOnly(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(f);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(f, attrs & ~FileAttributes.ReadOnly);
            }
        }
        catch (Exception)
        {
            // The delete reports it if it matters.
        }
    }

    /// <summary>A phrase for the kind of thing that stayed. Never a path or a name.</summary>
    private string KindOf(ResetPart part, ResetItem item)
    {
        string name = Path.GetFileName(item.Path.TrimEnd('\\', '/'));   // streamer: notname - leaf classified into a phrase; the result is a kind, never a path or name
        if (name.StartsWith("webview2", StringComparison.OrdinalIgnoreCase)) return "a sign-in profile folder";
        if (string.Equals(item.Path, _paths.VaultDir, StringComparison.OrdinalIgnoreCase)) return "the session archive";   // streamer: notname - compared to classify the kind, never drawn
        if (string.Equals(item.Path, _paths.WidgetsDir, StringComparison.OrdinalIgnoreCase)) return "the widgets folder";
        return part switch
        {
            ResetPart.ClaudeCodeEntries => item.IsDirectory ? "a folder of Sanduhr's Claude Code files" : "one of Sanduhr's Claude Code files",
            ResetPart.Themes => "the themes folder",
            ResetPart.Settings => "the settings file",
            _ => item.IsDirectory ? "a folder" : "a file",
        };
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> failures)
        => [.. failures.Distinct(StringComparer.Ordinal)];
}
