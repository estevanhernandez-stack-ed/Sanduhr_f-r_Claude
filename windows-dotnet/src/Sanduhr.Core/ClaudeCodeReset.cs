namespace Sanduhr.Core;

/// <summary>Where Reset's Claude Code part works: the profile its folders sit under, Sanduhr's two folders, the statusline's bin
/// folder, the folders Settings lists and the MCP home the person picked. App code builds it from the widget; tests point it at a
/// temp tree.</summary>
public sealed record ClaudeCodeResetScope(
    string ProfileDir,
    string SanduhrDir,
    string ExternalDir,
    string StatuslineBinDir,
    IReadOnlyList<CcFolder> Folders,
    string? SavedMcpHome,
    IReadOnlyList<string> LegacyModsRoots);

/// <summary>One entry the Claude Code undo took out, or failed to take out: its kind and the real folder (the MCP home's folder for
/// an MCP entry). Never shown.</summary>
public sealed record ClaudeCodeUndone(IntegrationKind Kind, string Folder);

/// <summary>
/// What <see cref="ClaudeCodeReset.UndoKeeping"/> did, per kind: <see cref="Removed"/> and <see cref="Failed"/> (kind and folder),
/// <see cref="Left"/> (kinds with something still in place that a switch or a file could re-create or still needs: a failure, a kept
/// entry's neighbour it could not list, or a statusline it was not safe to open), and <see cref="Failures"/> (kinds of failure, as
/// phrases, never a path). The orchestrator clears each kind's switch from this, not from the failure phrases.
/// </summary>
public sealed record ClaudeCodeUndoResult(
    IReadOnlyList<string> Failures,
    IReadOnlyList<ClaudeCodeUndone> Removed,
    IReadOnlyList<ClaudeCodeUndone> Failed,
    IReadOnlyCollection<IntegrationKind> Left)
{
    /// <summary>No failures, nothing removed, nothing left: a run that found nothing to do.</summary>
    public static ClaudeCodeUndoResult Clean { get; } = new([], [], [], []);

    /// <summary>Every MCP deregister worked, so no MCP entry Sanduhr could list is left.</summary>
    public bool McpOut => !Left.Contains(IntegrationKind.Mcp);

    /// <summary>The statusline entries and the meters mod (which reads the same snapshot switch) are all out or none were found,
    /// and the receipts could be read.</summary>
    public bool StatuslineOut => !Left.Contains(IntegrationKind.Statusline) && !Left.Contains(IntegrationKind.Meters);
}

/// <summary>
/// Reset Sanduhr…, the Claude Code part (spec, Reset; R12; Settings v2, slice 8): takes Sanduhr's entries out of every folder but the
/// ones kept, each through its installer's Remove, which takes out only what still points at Sanduhr's files. Keeping is per kind: a
/// kept (folder, kind) is not opened for writing and the files that kind runs stay (the statusline script, the MCP launcher and
/// server copies); every other kind in that folder is undone. The shared statusline script goes only when no statusline is kept, the
/// receipts read cleanly and no statusline undo failed. Returns kinds of failures, never a path.
/// </summary>
public static class ClaudeCodeReset
{
    /// <summary>Keeps every kind in each folder given.</summary>
    public static IReadOnlyList<string> Undo(ClaudeCodeResetScope scope, IReadOnlyCollection<string> keep)
        => UndoKeeping(scope, [.. keep.Select(k => new ResetFolder(k, SettingsNames.ResetKindOrder))]).Failures;

    /// <summary>Keeps the kinds each <see cref="ResetFolder"/> lists and undoes the rest.</summary>
    public static ClaudeCodeUndoResult UndoKeeping(ClaudeCodeResetScope scope, IReadOnlyCollection<ResetFolder> keep)
    {
        var failures = new List<string>();
        var removed = new List<ClaudeCodeUndone>();
        var failed = new List<ClaudeCodeUndone>();
        var left = new HashSet<IntegrationKind>();
        bool Kept(string path, IntegrationKind kind)
            => keep.Any(k => k.Kinds.Contains(kind) && ClaudeCodeFolders.Same(k.Path, path));
        bool KeptAny(IntegrationKind kind) => keep.Any(k => k.Kinds.Contains(kind));

        string receiptsPath = Path.Combine(scope.SanduhrDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
        // Read before any write: unreadable receipts mean Reset can't tell which entries are Sanduhr's to restore.
        bool receiptsUnreadable = ResetPlan.ReceiptsUnreadable(receiptsPath);
        var (receipts, _) = SnapshotConsumers.ReadReceipts(receiptsPath);
        // Unreadable receipts hide which meters, hooks and statusline entries exist, so those kinds stay "left" for the switches.
        if (receiptsUnreadable)
        {
            left.Add(IntegrationKind.Meters);
            left.Add(IntegrationKind.Hooks);
            left.Add(IntegrationKind.Statusline);
        }

        // The meters mod and the glow hooks, receipt by receipt, kind by kind.
        try
        {
            var integ = new ClaudeCodeIntegrations(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir)
            {
                LegacyModsRoots = scope.LegacyModsRoots,
            };
            var targets = receipts
                .Where(r => r.Kind is IntegrationKind.Meters or IntegrationKind.Hooks)
                .Where(r => !Kept(r.Folder, r.Kind))
                .Select(r => (r.Kind, r.Folder)).Distinct().ToList();
            foreach (var (kind, folder) in targets)
            {
                if (integ.Remove(kind, folder).Ok)
                    removed.Add(new ClaudeCodeUndone(kind, folder));
                else
                {
                    failures.Add("a Claude Code entry");
                    failed.Add(new ClaudeCodeUndone(kind, folder));
                    left.Add(kind);
                }
            }
        }
        catch (Exception)
        {
            failures.Add("a Claude Code entry");
            left.Add(IntegrationKind.Meters);
            left.Add(IntegrationKind.Hooks);
        }

        // The statusline: known folders first, then any folder a receipt names that is no longer listed, so Reset never leaves an
        // orphan statusLine. Each folder once. A kept folder is skipped. A folder that can't be read right now is a failure, and
        // with unreadable receipts only a folder with a readable statusline receipt is touched: Remove without one would drop the
        // key and lose a chained line of the person's own.
        var statusline = new StatuslineInstaller(scope.ProfileDir, scope.StatuslineBinDir, null, scope.SanduhrDir);
        var statuslineFolders = scope.Folders.Select(f => f.Path).ToList();
        var receiptFolders = SnapshotConsumers.StatuslineReceiptFolders(receipts);
        foreach (var folder in receiptFolders)
            if (!statuslineFolders.Any(p => ClaudeCodeFolders.Same(p, folder)))
                statuslineFolders.Add(folder);
        bool statuslineFailed = false;
        foreach (var folder in statuslineFolders.Where(f => !Kept(f, IntegrationKind.Statusline)))
        {
            try
            {
                // A rooted second argument to Path.Combine returns it, so a full folder path works as the home name.
                var status = statusline.Status(folder);
                if (status == StatuslineStatus.Unreadable)
                {
                    statuslineFailed = true;
                    failed.Add(new ClaudeCodeUndone(IntegrationKind.Statusline, folder));
                }
                else if (status is StatuslineStatus.Installed or StatuslineStatus.Outdated
                         && (!receiptsUnreadable || receiptFolders.Any(r => ClaudeCodeFolders.Same(r, folder))))
                {
                    if (statusline.Remove(folder))
                        removed.Add(new ClaudeCodeUndone(IntegrationKind.Statusline, folder));
                    else
                    {
                        statuslineFailed = true;
                        failed.Add(new ClaudeCodeUndone(IntegrationKind.Statusline, folder));
                    }
                }
            }
            catch (Exception)
            {
                statuslineFailed = true;
                failed.Add(new ClaudeCodeUndone(IntegrationKind.Statusline, folder));
            }
        }
        if (statuslineFailed)
        {
            failures.Add("a statusline entry");
            left.Add(IntegrationKind.Statusline);
        }
        if (!KeptAny(IntegrationKind.Statusline) && !receiptsUnreadable && !statuslineFailed)
        {
            try { statusline.RemoveScript(); }
            catch (Exception)
            {
                failures.Add("the statusline script");
                left.Add(IntegrationKind.Statusline);
            }
        }

        // The MCP server: deregistered from every discovered home and the one the person picked, except where it is kept. The
        // launcher and server files go only when no MCP entry is kept and every deregister worked. What is left or gone is
        // reported whatever the receipts' state or the other kinds did: the MCP entry has no receipt.
        try
        {
            var mcp = new McpIntegrationInstaller(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir);
            var homes = scope.Folders.Where(f => f.Discovered).Select(f => f.Name)
                .Concat(scope.SavedMcpHome is { Length: > 0 } saved ? [saved] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(home => !Kept(Path.Combine(scope.ProfileDir, home), IntegrationKind.Mcp));
            bool all = true;
            foreach (var home in homes)
            {
                var entry = new ClaudeCodeUndone(IntegrationKind.Mcp, Path.Combine(scope.ProfileDir, home));
                if (mcp.Deregister(home)) removed.Add(entry);
                else { all = false; failed.Add(entry); }
            }
            if (all && !KeptAny(IntegrationKind.Mcp))
                mcp.RemoveIntegrationFiles();
            if (!all)
            {
                failures.Add("an MCP server entry");
                left.Add(IntegrationKind.Mcp);
            }
        }
        catch (Exception)
        {
            failures.Add("an MCP server entry");
            left.Add(IntegrationKind.Mcp);
        }

        return new ClaudeCodeUndoResult([.. failures.Distinct()], removed, failed, left);
    }
}
