using System.IO;
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
/// The Claude Code undo is the orchestrator's: <c>undoClaudeCodeEntries</c> returns the kinds of
/// failures (empty when every entry went). Sanduhr's own copies of those entries (receipts, mods,
/// the MCP server copy, the bin scripts) are deleted only when the undo reported none, because a failed
/// folder keeps its receipt so Settings can still remove that entry after the next launch.
/// <c>backups\</c> is never touched.
/// </summary>
internal sealed class ResetService
{
    private readonly Paths _paths;
    private readonly AccountStore _accounts;
    private readonly PublishTokenStore _publishToken;
    private readonly Func<Task<IReadOnlyList<string>>> _undoClaudeCodeEntries;
    private readonly Action _stopBackgroundWork;
    private readonly Func<Task<int>>? _deleteAccountProfiles;

    /// <summary>Turns the Claude Code switches off in settings.json once the entries are out (see
    /// <see cref="SettingsStore.ClearClaudeCodeSwitchesForReset"/>); null in contexts without a store.</summary>
    private readonly Action? _clearClaudeCodeSwitches;

    public ResetService(
        Paths paths,
        AccountStore accounts,
        PublishTokenStore publishToken,
        Func<Task<IReadOnlyList<string>>> undoClaudeCodeEntries,
        Action stopBackgroundWork,
        Func<Task<int>>? deleteAccountProfiles = null,
        Action? clearClaudeCodeSwitches = null)
    {
        _clearClaudeCodeSwitches = clearClaudeCodeSwitches;
        _deleteAccountProfiles = deleteAccountProfiles;
        _paths = paths;
        _accounts = accounts;
        _publishToken = publishToken;
        _undoClaudeCodeEntries = undoClaudeCodeEntries;
        _stopBackgroundWork = stopBackgroundWork;
    }

    /// <summary>Runs the checked parts. <paramref name="started"/> is told which part is about to run.</summary>
    public async Task<IReadOnlyList<ResetStepResult>> RunAsync(ResetChoices choices, Action<ResetPart>? started = null)
    {
        // Follow and seat meters off before anything is removed, so nothing re-creates a file or
        // re-reads a key that is about to go. A stop that throws must not block the reset.
        try { _stopBackgroundWork(); } catch { /* the steps still run */ }

        var results = await ResetPlan.RunAsync(ResetPlan.Steps(choices), async part =>
        {
            started?.Invoke(part);
            return part switch
            {
                ResetPart.ClaudeCodeEntries => await ClaudeCodeEntriesAsync(),
                ResetPart.Accounts => await AccountsAsync(),
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
            string dir = _paths.ExternalDirPath;
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: false);
        }
        catch (Exception)
        {
            // Not empty, in use, or not ours to remove: leave it.
        }
    }

    private async Task<ResetStepResult> ClaudeCodeEntriesAsync()
    {
        var failures = new List<string>();
        try
        {
            failures.AddRange(await _undoClaudeCodeEntries());
        }
        catch (Exception)
        {
            failures.Add("a Claude Code entry");
        }

        // A failed entry keeps its receipt (and the files it points at) so it can still be removed later.
        if (failures.Count == 0)
        {
            // The entries are out: their switches go off too, or the next launch re-creates the projection,
            // the launcher and the script. A Settings part that runs later deletes the file anyway.
            try { _clearClaudeCodeSwitches?.Invoke(); } catch (Exception) { /* best effort */ }
            failures.AddRange(await DeleteItemsAsync(ResetPart.ClaudeCodeEntries));
        }
        return ResetStepResult.Failed(ResetPart.ClaudeCodeEntries, Distinct(failures));
    }

    private async Task<ResetStepResult> AccountsAsync()
    {
        var failures = new List<string>();

        IReadOnlyList<string> labels;
        try { labels = _accounts.ListAccounts().ToList(); }
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

    private async Task<ResetStepResult> DeleteInventoryAsync(ResetPart part)
        => ResetStepResult.Failed(part, Distinct(await DeleteItemsAsync(part)));

    private async Task<List<string>> DeleteItemsAsync(ResetPart part)
    {
        var failures = new List<string>();
        IReadOnlyList<ResetItem> items;
        try { items = ResetPlan.Inventory(_paths)[part]; }
        catch (Exception)
        {
            failures.Add("the list of files");
            return failures;
        }
        foreach (var item in items)
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
        string name = Path.GetFileName(item.Path.TrimEnd('\\', '/'));
        if (name.StartsWith("webview2", StringComparison.OrdinalIgnoreCase)) return "a sign-in profile folder";
        if (string.Equals(item.Path, _paths.VaultDir, StringComparison.OrdinalIgnoreCase)) return "the session archive";
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
