using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's <c>DeskMessageHandoffTests</c> in DeskMessageTests.swift, on temp folders. The
/// Mac's owner-only (0600) check has no Windows equivalent and is replaced by a no-temp-files check.
/// </summary>
public sealed class DeskMessageHandoffTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-msg-" + Guid.NewGuid().ToString("N"));
    private readonly DeskMessagePaths _paths;

    public DeskMessageHandoffTests()
    {
        _paths = new DeskMessagePaths(Path.Combine(_root, "Sanduhr"), Path.Combine(_root, "Desk", "messages.txt"));
        Directory.CreateDirectory(_paths.Support);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.Messages)!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static DateTimeOffset T0 => MessageProposalTests.T0;

    private static byte[] Json(string id = "req1", DateTimeOffset? at = null, params string[] lines) =>
        MessageProposalTests.RequestJson(id: id, at: at, lines: lines.Length == 0 ? null : new JsonArray(lines.Select(l => (JsonNode?)l).ToArray()));

    private static byte[] JsonMode(string mode, params string[] lines) =>
        MessageProposalTests.RequestJson(lines: new JsonArray(lines.Select(l => (JsonNode?)l).ToArray()), mode: mode);

    private DeskMessageHandoff Handoff(bool direct = false, Func<bool>? canWrite = null) =>
        new(_paths, () => direct, () => T0, canWrite);

    private string? Messages() => File.Exists(_paths.Messages) ? Encoding.UTF8.GetString(File.ReadAllBytes(_paths.Messages)) : null;

    private JsonObject? Result() => File.Exists(_paths.Result) ? (JsonObject)JsonNode.Parse(File.ReadAllBytes(_paths.Result))! : null;

    private string? Status() => (string?)((JsonObject?)Result()?["result"])?["status"];

    private void Drop(byte[] data) => File.WriteAllBytes(_paths.Request, data);

    [Fact]
    public void WaitsForApproval_ThenAdds()
    {
        File.WriteAllText(_paths.Messages, "keep building.\n");
        var h = Handoff();
        var suggested = new List<string>();
        int applied = 0;
        h.SuggestionReceived += p => suggested.Add(p.Id);
        h.Applied += () => applied++;
        Drop(Json(lines: "{write} new."));
        h.Check();
        Assert.False(File.Exists(_paths.Request));   // read once, removed
        Assert.Equal("req1", h.Pending?.Id);
        Assert.Equal(["req1"], suggested);
        Assert.Equal("pending_approval", Status());
        Assert.Equal("req1", (string?)Result()?["id"]);
        Assert.Equal("keep building.\n", Messages());   // nothing changes before Add
        h.Approve();
        Assert.Null(h.Pending);
        Assert.Equal("keep building.\n{write} new.\n", Messages());
        Assert.Equal("keep building.\n", Encoding.UTF8.GetString(File.ReadAllBytes(_paths.Backup)));
        Assert.Equal("applied", Status());
        Assert.Equal(1, applied);
        Assert.Equal(1, h.Revision);
        // Windows: nothing but the files themselves is left in either folder.
        Assert.Equal(["desk-messages-result.json"], Directory.GetFiles(_paths.Support).Select(Path.GetFileName));
        Assert.Equal(["messages.txt", "messages.txt.previous"], Directory.GetFiles(Path.GetDirectoryName(_paths.Messages)!).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Dismiss_LeavesTheListAlone()
    {
        var h = Handoff();
        int decided = 0;
        h.Decided += _ => decided++;
        h.Receive(Json(lines: "a."));
        h.Dismiss();
        Assert.Null(h.Pending);
        Assert.Null(Messages());
        Assert.Equal("rejected", Status());
        var reasons = (JsonArray)((JsonObject)Result()!["result"]!)["reasons"]!;
        Assert.Equal(["dismissed by the user"], reasons.Select(r => (string?)r));
        Assert.Equal(1, decided);
        // Nothing waits any more: a second Dismiss or Approve does nothing.
        h.Dismiss();
        h.Approve();
        Assert.Equal(1, decided);
        Assert.Null(Messages());
    }

    [Fact]
    public void Direct_AppliesAtOnce()
    {
        var h = Handoff(direct: true);
        int suggested = 0;
        h.SuggestionReceived += _ => suggested++;
        h.Receive(JsonMode("replace", "Fri: {shimmer} showtime."));
        Assert.Null(h.Pending);
        Assert.Equal(0, suggested);
        Assert.Equal("Fri: {shimmer} showtime.\n", Messages());
        Assert.False(File.Exists(_paths.Backup));   // there was no list before
        Assert.Equal("applied", Status());
    }

    [Fact]
    public void RefusedAndExpiredRequests()
    {
        var h = Handoff(direct: true);
        h.Receive(Json(lines: "{blink} x"));
        Assert.Equal("rejected", Status());
        Assert.Null(Messages());
        File.Delete(_paths.Result);
        h.Receive(Json(at: T0.AddSeconds(-3600)));
        Assert.Null(Result());
        Assert.Null(Messages());
    }

    [Fact]
    public void ANewerSuggestion_ReplacesTheWaitingOne()
    {
        var h = Handoff();
        var decided = new List<string>();
        h.Decided += p => decided.Add(p.Id);
        h.Receive(Json("one"));
        h.Receive(Json("two"));
        Assert.Equal("two", h.Pending?.Id);
        Assert.Equal(["one"], decided);
    }

    [Fact]
    public void AListThatIsNotUtf8_IsLeftAlone()
    {
        File.WriteAllBytes(_paths.Messages, [0xFF, 0xFE, 0x41]);
        var h = Handoff(direct: true);
        h.Receive(Json());
        Assert.Equal("rejected", Status());
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x41 }, File.ReadAllBytes(_paths.Messages));
        Assert.Contains("UTF-8", (string?)((JsonArray)((JsonObject)Result()!["result"]!)["reasons"]!)[0]);
    }

    [Fact]
    public void State_IsWrittenOnlyWhenItChanges()
    {
        var h = Handoff();
        h.WriteState(null, "daily");
        var first = File.ReadAllBytes(_paths.State);
        File.Delete(_paths.State);
        h.WriteState("", "daily");   // same state: not rewritten
        Assert.False(File.Exists(_paths.State));
        h.WriteState("hi.", "daily");
        Assert.NotEqual(first, File.ReadAllBytes(_paths.State));
    }

    // Windows-specific

    [Fact]
    public void AfterReset_NothingIsWrittenToMessages()
    {
        File.WriteAllText(_paths.Messages, "keep.\n");
        var h = Handoff(direct: true, canWrite: () => false);
        int applied = 0;
        h.Applied += () => applied++;
        h.Receive(Json(lines: "new."));
        Assert.Equal("keep.\n", Messages());
        Assert.False(File.Exists(_paths.Backup));
        Assert.Equal(0, applied);
        Assert.Equal(0, h.Revision);
    }

    [Fact]
    public void Check_WithNoRequest_DoesNothing()
    {
        var h = Handoff();
        h.Check();
        Assert.Null(h.Pending);
        Assert.Null(Result());
    }

    [Fact]
    public void Check_TakesAnUnreadableRequestOutOfTheWay()
    {
        var h = Handoff();
        Drop(Encoding.UTF8.GetBytes("{torn"));
        h.Check();
        Assert.False(File.Exists(_paths.Request));
        Assert.False(File.Exists(_paths.Request + ".taken"));
        Assert.Null(Result());
    }

    [Fact]
    public void ARequestFileHeldOpenForWriting_IsLeftForTheNextCheck()
    {
        var h = Handoff();
        using (var writer = new FileStream(_paths.Request, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            writer.Write(Json(lines: "{write} late."));
            h.Check();
            Assert.Null(h.Pending);
        }
        h.Check();
        Assert.Equal("req1", h.Pending?.Id);
    }

    [Fact]
    public void Result_IsOverwrittenByTheNextStep()
    {
        var h = Handoff();
        h.Receive(Json(lines: "a."));
        Assert.Equal("pending_approval", Status());
        h.Approve();
        Assert.Equal("applied", Status());
        Assert.Equal("a.\n", Messages());
        var lines = (int?)((JsonObject)Result()!["result"]!)["lines_added"];
        Assert.Equal(1, lines);
    }

    [Fact]
    public void ApprovingAReplace_KeepsThePreviousFile()
    {
        File.WriteAllText(_paths.Messages, "# header\nold.\n");
        var h = Handoff();
        h.Receive(JsonMode("replace", "new."));
        h.Approve();
        Assert.Equal("# header\n\nnew.\n", Messages());
        Assert.Equal("# header\nold.\n", Encoding.UTF8.GetString(File.ReadAllBytes(_paths.Backup)));
    }

    [Fact]
    public void ARequestOverTheFileCap_IsRejectedWithTheCapInTheReason()
    {
        File.WriteAllText(_paths.Messages, string.Join("\n", Enumerable.Range(0, 1000).Select(i => "l" + i)) + "\n");
        var h = Handoff(direct: true);
        h.Receive(Json(lines: "one more."));
        Assert.Equal("rejected", Status());
        Assert.Contains("1000", (string?)((JsonArray)((JsonObject)Result()!["result"]!)["reasons"]!)[0]);
    }
}
