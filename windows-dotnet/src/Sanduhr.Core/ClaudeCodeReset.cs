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
        => UndoKeeping(scope, [.. keep.Select(k => new ResetFolder(k, SettingsNames.ResetKindOrder))]);

    /// <summary>Keeps the kinds each <see cref="ResetFolder"/> lists and undoes the rest.</summary>
    public static IReadOnlyList<string> UndoKeeping(ClaudeCodeResetScope scope, IReadOnlyCollection<ResetFolder> keep)
    {
        var failures = new List<string>();
        bool Kept(string path, IntegrationKind kind)
            => keep.Any(k => k.Kinds.Contains(kind) && ClaudeCodeFolders.Same(k.Path, path));
        bool KeptAny(IntegrationKind kind) => keep.Any(k => k.Kinds.Contains(kind));

        string receiptsPath = Path.Combine(scope.SanduhrDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
        // Read before any write: unreadable receipts mean Reset can't tell which entries are Sanduhr's to restore.
        bool receiptsUnreadable = ResetPlan.ReceiptsUnreadable(receiptsPath);
        var (receipts, _) = SnapshotConsumers.ReadReceipts(receiptsPath);

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
                if (!integ.Remove(kind, folder).Ok)
                    failures.Add("a Claude Code entry");
        }
        catch (Exception) { failures.Add("a Claude Code entry"); }

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
                    statuslineFailed = true;
                else if (status is StatuslineStatus.Installed or StatuslineStatus.Outdated
                         && (!receiptsUnreadable || receiptFolders.Any(r => ClaudeCodeFolders.Same(r, folder)))
                         && !statusline.Remove(folder))
                    statuslineFailed = true;
            }
            catch (Exception) { statuslineFailed = true; }
        }
        if (statuslineFailed) failures.Add("a statusline entry");
        if (!KeptAny(IntegrationKind.Statusline) && !receiptsUnreadable && !statuslineFailed)
        {
            try { statusline.RemoveScript(); } catch (Exception) { failures.Add("the statusline script"); }
        }

        // The MCP server: deregistered from every discovered home and the one the person picked, except where it is kept. The
        // launcher and server files go only when no MCP entry is kept and every deregister worked.
        try
        {
            var mcp = new McpIntegrationInstaller(scope.ProfileDir, scope.SanduhrDir, externalDir: scope.ExternalDir);
            var homes = scope.Folders.Where(f => f.Discovered).Select(f => f.Name)
                .Concat(scope.SavedMcpHome is { Length: > 0 } saved ? [saved] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(home => !Kept(Path.Combine(scope.ProfileDir, home), IntegrationKind.Mcp));
            if (!mcp.UndoAll(homes, removeFiles: !KeptAny(IntegrationKind.Mcp)))
                failures.Add("an MCP server entry");
        }
        catch (Exception) { failures.Add("an MCP server entry"); }

        return [.. failures.Distinct()];
    }
}
