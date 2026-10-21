namespace Sanduhr.Core;

/// <summary>Where the handoff's files live: Sanduhr's folder for the request, result and state, and messages.txt.</summary>
/// <param name="Support">%USERPROFILE%\.sanduhr: the request, result and state files.</param>
/// <param name="Messages">messages.txt.</param>
public sealed record DeskMessagePaths(string Support, string Messages)
{
    public string Request => Path.Combine(Support, MessageProposal.RequestFile);
    public string Result => Path.Combine(Support, MessageProposal.ResultFile);
    public string State => Path.Combine(Support, MessageProposal.StateFile);
    /// <summary>messages.txt.previous: what the list was before Claude's lines went in.</summary>
    public string Backup => Messages + ".previous";
}

/// <summary>
/// Claude's suggested Desk messages, the file half. The MCP server's <c>propose_desk_messages</c> drops
/// desk-messages-request.json into Sanduhr's folder; the App watches the folder and calls
/// <see cref="Check"/>, which checks the request again (<see cref="MessageProposal"/>) and either applies
/// it, when the direct opt-in is on, or holds it as the suggestion Settings, Message shows with Add,
/// Review and Dismiss. Each step answers in desk-messages-result.json, which the server reads:
/// <c>pending_approval</c> at once, then <c>applied</c> or <c>rejected</c> when the user decides.
///
/// A suggestion waits in memory: quitting Sanduhr drops it (the server's caller was already told it is
/// pending). A newer request replaces an older one still waiting. The request file is taken (read and
/// removed) as soon as it is seen. Applying keeps the previous file as messages.txt.previous. Nothing
/// here logs message text. Ported from the Mac's Desk/DeskMessageHandoff.swift without the folder watch,
/// the notification and the settings reads, which belong to the App. Not thread-safe: call it from one
/// thread (the UI thread).
/// </summary>
public sealed class DeskMessageHandoff
{
    private readonly Func<bool> _direct;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<bool> _canWrite;
    private byte[]? _lastState;

    /// <param name="paths">The folder and messages.txt.</param>
    /// <param name="direct">Settings, Message: "Let Claude change the messages directly", off by default.</param>
    /// <param name="now">The clock (a request older than ten minutes is dropped).</param>
    /// <param name="canWrite">False once Reset has run: messages.txt is not written again.</param>
    public DeskMessageHandoff(DeskMessagePaths paths, Func<bool>? direct = null, Func<DateTimeOffset>? now = null, Func<bool>? canWrite = null)
    {
        Paths = paths;
        _direct = direct ?? (() => false);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _canWrite = canWrite ?? (() => true);
    }

    public DeskMessagePaths Paths { get; }

    /// <summary>The suggestion waiting for the user, if any.</summary>
    public MessageProposal? Pending { get; private set; }

    /// <summary>Goes up each time messages.txt is changed here, so an open editor reloads.</summary>
    public int Revision { get; private set; }

    /// <summary>A suggestion arrived and waits (the banner decision is the caller's).</summary>
    public event Action<MessageProposal>? SuggestionReceived;

    /// <summary>messages.txt changed here (the Desk picks today's line again).</summary>
    public event Action? Applied;

    /// <summary>A suggestion was added, dismissed or replaced by a newer one (its notice can go).</summary>
    public event Action<MessageProposal>? Decided;

    /// <summary>Reads and removes a waiting request, if there is one.</summary>
    public void Check()
    {
        if (MessageFileIo.Take(Paths.Request) is { } data) Receive(data);
    }

    /// <summary>One request's bytes, as read from the file.</summary>
    public void Receive(byte[] data)
    {
        var decoded = MessageProposal.Decode(data, _now());
        switch (decoded.Kind)
        {
            case MessageDecodedKind.Discard:
                return;
            case MessageDecodedKind.Refused:
                WriteResult(decoded.Id!, MessageProposalStatus.Rejected, null, decoded.Reasons);
                return;
            default:
            {
                var p = decoded.Proposal!;
                if (_direct())
                {
                    Apply(p);
                    return;
                }
                if (Pending is { } old) Decided?.Invoke(old);
                Pending = p;
                WriteResult(p.Id, MessageProposalStatus.PendingApproval, p.Mode);
                SuggestionReceived?.Invoke(p);
                return;
            }
        }
    }

    /// <summary>Add: the waiting suggestion goes into messages.txt.</summary>
    public void Approve()
    {
        if (Pending is not { } p) return;
        Pending = null;
        Decided?.Invoke(p);
        Apply(p);
    }

    /// <summary>Dismiss: the suggestion is dropped and the server's caller hears so.</summary>
    public void Dismiss()
    {
        if (Pending is not { } p) return;
        Pending = null;
        Decided?.Invoke(p);
        WriteResult(p.Id, MessageProposalStatus.Rejected, p.Mode, ["dismissed by the user"]);
    }

    private void Apply(MessageProposal p)
    {
        bool had = File.Exists(Paths.Messages);
        string existing = "";
        if (had)
        {
            if (MessageFileIo.ReadText(Paths.Messages) is not { } text)
            {
                WriteResult(p.Id, MessageProposalStatus.Rejected, p.Mode,
                    ["messages.txt could not be read as UTF-8; it was left alone"]);
                return;
            }
            existing = text;
        }
        if (MessageProposal.Apply(p, existing) is not { } applied)
        {
            WriteResult(p.Id, MessageProposalStatus.Rejected, p.Mode,
                [$"the list would pass {MessageProposal.MaxFileLines} lines"]);
            return;
        }
        // Reset Sanduhr... ran: messages.txt is not written again.
        if (!_canWrite()) return;
        try
        {
            if (had) MessageFileIo.WriteAtomic(Paths.Backup, MessageFileIo.Encode(existing));
            MessageFileIo.WriteAtomic(Paths.Messages, MessageFileIo.Encode(applied.Text));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            WriteResult(p.Id, MessageProposalStatus.Rejected, p.Mode, ["messages.txt could not be written"]);
            return;
        }
        Revision++;
        Applied?.Invoke();
        WriteResult(p.Id, MessageProposalStatus.Applied, p.Mode, applied: applied);
    }

    /// <summary>
    /// The result the server polls for. Windows has no owner-only file mode to set; the file sits in the
    /// user's own %APPDATA% folder and is written through a temp sibling and a replace.
    /// </summary>
    private void WriteResult(string id, MessageProposalStatus status, MessageProposalMode? mode,
        IReadOnlyList<string>? reasons = null, MessageApplied? applied = null)
    {
        var data = MessageProposal.ResultJson(id, status, mode, reasons, applied, _now());
        TryWrite(Paths.Result, data);
    }

    /// <summary>
    /// desk-messages-state.json, for get_desk_messages: rewritten only when the pin, the rotation, the
    /// mix switch or On special days changed.
    /// </summary>
    public void WriteState(string? pinned, string rotate, bool mixDaily = false,
        MessageSpecialMode specialMode = MessageSpecialMode.Stack, double specialSeconds = MessageSpecialModes.DefaultSeconds)
    {
        var data = MessageProposal.StateJson(pinned, rotate, mixDaily, specialMode, specialSeconds);
        if (_lastState is not null && data.AsSpan().SequenceEqual(_lastState)) return;
        if (TryWrite(Paths.State, data)) _lastState = data;
    }

    private static bool TryWrite(string path, byte[] data)
    {
        try
        {
            MessageFileIo.WriteAtomic(path, data);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
