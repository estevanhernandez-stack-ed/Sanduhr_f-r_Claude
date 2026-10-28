namespace Sanduhr.Core;

/// <summary>What one folder says about a piece that reads <c>snapshot.json</c> (the statusline, the meters mod).</summary>
public enum ConsumerState
{
    NotInstalled,
    /// <summary>Installed, current or not.</summary>
    Installed,
    /// <summary>The folder's settings could not be read right now (a sharing violation, a half-written file).
    /// That is not evidence of anything.</summary>
    Unreadable,
}

/// <summary>What to do with <c>statusline_enabled</c> and the statusline script.</summary>
public enum SnapshotDecision
{
    /// <summary>Leave the switch and the script as they are: the answer is not known.</summary>
    Keep,
    /// <summary>A consumer exists: the switch is true.</summary>
    On,
    /// <summary>No consumer anywhere: the switch is false and the script may go.</summary>
    Off,
}

/// <summary>
/// The rule behind <c>statusline_enabled</c>. A folder that can't be read is unknown, not empty, and a
/// receipt in <c>installs.json</c> proves Sanduhr wrote a piece into a folder even when that folder is no
/// longer in the list, so neither may turn the snapshot off.
/// </summary>
public static class SnapshotConsumers
{
    /// <param name="states">One entry per known folder per piece (statusline and meters).</param>
    /// <param name="receiptKinds">The kind of every receipt in <c>installs.json</c>; only Statusline and Meters count.</param>
    /// <param name="receiptsUnknown">The receipts file could not be read as a whole.</param>
    public static SnapshotDecision Decide(IEnumerable<ConsumerState> states,
        IEnumerable<IntegrationKind> receiptKinds, bool receiptsUnknown = false)
    {
        bool unknown = receiptsUnknown;
        foreach (var s in states)
        {
            if (s == ConsumerState.Installed) return SnapshotDecision.On;
            if (s == ConsumerState.Unreadable) unknown = true;
        }
        if (receiptKinds.Any(k => k is IntegrationKind.Statusline or IntegrationKind.Meters))
            return SnapshotDecision.On;
        return unknown ? SnapshotDecision.Keep : SnapshotDecision.Off;
    }

    /// <summary>Whether <c>snapshot.json</c> should exist: a statusline or meters piece wants it
    /// (<c>statusline_enabled</c>), and so does the MCP server (<c>mcp_enabled</c>), whose
    /// <c>get_usage</c> reads it and has nothing else on the Store build.</summary>
    public static bool SnapshotWanted(bool statuslineEnabled, bool mcpEnabled) => statuslineEnabled || mcpEnabled;

    /// <summary>
    /// Whether the widget writes the statusline script at start (slice 8 final review M2). <c>statusline_enabled</c> also stands for
    /// the meters mod, which reads the snapshot and never runs the script, so the switch alone isn't enough: the script is written
    /// when a statusline receipt exists, when the receipts can't be read (unknown is not "none"), or when the script is already
    /// there (an update for a statusline installed before receipts, or one whose receipt is gone).
    /// </summary>
    public static bool ScriptWanted(bool statuslineEnabled, string receiptsPath, string scriptPath)
    {
        if (!statuslineEnabled) return false;
        if (File.Exists(scriptPath)) return true;
        var (receipts, unknown) = ReadReceipts(receiptsPath);
        return unknown || StatuslineReceiptFolders(receipts).Count > 0;
    }

    public static ConsumerState Of(StatuslineStatus status) => status switch
    {
        StatuslineStatus.Installed or StatuslineStatus.Outdated => ConsumerState.Installed,
        StatuslineStatus.Unreadable => ConsumerState.Unreadable,
        _ => ConsumerState.NotInstalled,
    };

    public static ConsumerState Of(IntegrationStatus status) => status switch
    {
        IntegrationStatus.Installed or IntegrationStatus.Outdated => ConsumerState.Installed,
        IntegrationStatus.Unreadable => ConsumerState.Unreadable,
        _ => ConsumerState.NotInstalled,
    };

    /// <summary>The receipts at <paramref name="receiptsPath"/>: every record's kind and folder, and whether the
    /// file was only partly readable (a record or the whole file that didn't decode).</summary>
    public static (IReadOnlyList<IntegrationReceipt> Receipts, bool Unknown) ReadReceipts(string receiptsPath)
    {
        try
        {
            var ledger = new ReceiptStore(receiptsPath).Load();
            return (ledger.Records, ledger.Skipped > 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ([], true);
        }
    }

    /// <summary>The folders named by Statusline receipts, normalized and each once.</summary>
    public static IReadOnlyList<string> StatuslineReceiptFolders(IEnumerable<IntegrationReceipt> receipts)
    {
        var folders = new List<string>();
        foreach (var r in receipts)
        {
            if (r.Kind != IntegrationKind.Statusline) continue;
            var folder = ClaudeCodeFolders.Norm(r.Folder);
            if (folder.Length == 0 || folders.Any(f => ClaudeCodeFolders.Same(f, folder))) continue;
            folders.Add(folder);
        }
        return folders;
    }
}
