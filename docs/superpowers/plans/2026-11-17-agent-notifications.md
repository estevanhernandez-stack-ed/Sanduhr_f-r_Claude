# Agent Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sanduhr Windows tells the user when any agent (Claude Code, Codex, the Antigravity CLI) starts, finishes, stops or waits, on a Windows toast, the glow, the taskbar flash and a Desk card.

**Architecture:** Pure readers in Core turn each agent's files into one event shape (`AgentTurnEvent`). `AgentActivityWatcher` (Core, file IO) tails the files and raises events. `AgentNotifier` (Core, pure, delegates for surfaces) applies `NotificationRules` once and calls four surfaces that the App wires to what already exists. The MCP server links none of it.

**Tech Stack:** .NET 10, C#, WPF, xunit. `Microsoft.Toolkit.Uwp.Notifications` for toasts (already referenced). No new package.

**Spec:** `docs/superpowers/specs/2026-11-17-agent-notifications-design.md` (umbrella: `2026-11-17-windows-5-0-umbrella.md`).

## Global Constraints

- Work on branch `feat/windows-5-0-agent-notifications`, created from `docs/windows-5-0-specs`. Never commit to `main`.
- Windows code only: `windows-dotnet/`. Never edit `mac/` or `vscode/`.
- CRLF working tree, LF index. Never `sed -i` a tracked file; use the Edit tool.
- No new package, no network call, no new capability. `Sanduhr.Mcp.csproj` and `TrustBoundaryTests` do not change.
- Never read message text: not Codex's `last_agent_message`, not an Antigravity step's `content`, `thinking` or the value of `tool_calls` (only whether the key exists).
- Agent ids are `AgentSources.Claude` (`"claude"`), `AgentSources.Codex` (`"codex"`), `AgentSources.Antigravity` (`"antigravity"`). Display names come from `AgentSources.BurnName(id)`: "Claude Code", "Codex", "Antigravity".
- Constants, verbatim from the spec: Antigravity quiet time 90 seconds; threshold choices 0 ("Every turn"), 30, 60, 120, 300 seconds, default 60; limiter 20 s per kind and no finished within 5 s of a waiting; signal file `cc-signal-<unix ms>-<pid>.json`, at most 4 KB, dropped after 10 minutes; launch priming reads the last 4 MB of a file changed in the last 10 minutes; a finished Desk card fades after 60 seconds.
- Defaults: toast on, Desk card on, glow on finished off, glow on waiting off, taskbar flash off, "Not while the agent's window is in front" on.
- Every name the App draws follows the streamer convention already in the code: a line that shows a project or folder name masks it through `StreamerMode.Mask` and carries a `// streamer: masked - …` comment; a line that only handles a path for IO carries `// streamer: notname - …`. The App test suite lints this.
- Run every suite before each commit: `cd windows-dotnet && dotnet test` (three suites: `Sanduhr.Tests`, `Sanduhr.Mcp.Tests`, `Sanduhr.App.Tests`). Quit any running dev `Sanduhr.exe` built from this tree first. Baseline on `main` at 9b3dcbd7: Core 5431, MCP 321, App 1650.
- Commit messages end with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.

## Already verified (2026-11-17, shapes only)

- **Codex sub-agents.** All 90 rollouts on the check machine start with `session_meta`. 86 have `parent_thread_id` set (46 `thread_source: "subagent"`, 40 `"guardian_review"`); the 4 top-level ones have neither `parent_thread_id` nor `forked_from_id`. A forked rollout's second `session_meta` is its parent's copy and lacks the keys, so the child mark must stick once set.
- **Codex app process name:** `codex`.
- **Antigravity quiet time:** the 90-second rule covers every look-alike ending in the 10 conversations sampled (the longest gap was 83 s).
- **Not verified:** that the Claude Code hook receives `session_id` and `cwd` on standard input on Windows. Task 7 checks it before changing the hook.

## Review Focus

1. **Two Claude Code sessions end in the same second.** Both notifications arrive; neither signal file overwrites the other. (Task 8.)
2. **A Codex rollout shrinks, is compressed or disappears mid-turn.** No finished event is invented and nothing throws. (Task 8.)
3. **Timestamps out of order** (a completion stamped before its start). The duration is unknown, never negative. (Tasks 2, 3 and 4.)
4. **Streamer mode is switched on while a card is up.** The card's project becomes its codename at once. (Tasks 6 and 11.)
5. **A source is switched off mid-turn.** Its cards go and its later events do nothing; switching it back on replays nothing. (Task 8.)

## File Structure

| File | Responsibility |
|---|---|
| `src/Sanduhr.Core/AgentTurns.cs` (new) | `AgentTurnKind`, `AgentTurnEvent`, `NotificationText` |
| `src/Sanduhr.Core/CodexTurnReader.cs` (new) | Codex rollout lines to events |
| `src/Sanduhr.Core/AntigravityTurnReader.cs` (new) | Antigravity transcript lines to events, with the quiet-time end |
| `src/Sanduhr.Core/ClaudeCodeSignalFile.cs` (new) | The hook's signal file: decode, and `ClaudeCodeTurns` (signals to events) |
| `src/Sanduhr.Core/NotificationRules.cs` (new) | `NotificationSettings`, `NotifySurfaces`, `NotificationLimiter`, `NotificationRules` |
| `src/Sanduhr.Core/AgentActivityWatcher.cs` (new) | File watching, tailing, priming; raises `AgentTurnEvent` |
| `src/Sanduhr.Core/AgentNotifier.cs` (new) | One event in, the surfaces called |
| `src/Sanduhr.Core/AlertToastText.cs` (new) | The limit-alert toast's words, moved out of `AlertService` unchanged |
| `src/Sanduhr.Core/Watchers.cs` | `WatcherSource.Turn`, `ApplyTurn`, `RetitleTurns`, per-card window and fade |
| `src/Sanduhr.Core/ClaudeCodeIntegrations.cs` | Three hook events, the new command, older commands read as Outdated |
| `src/Sanduhr.Core/WhatsNew.cs`, `WhatsNewCards.cs`, `SettingsNames.cs`, `SettingsIndex.cs`, `ResetPlan.cs` | The new page, its card, its names, the signal files in Reset |
| `src/Sanduhr.App/Services/Toasts.cs` (new) | One toast builder and one activation handler |
| `src/Sanduhr.App/Services/AlertService.cs`, `RestartToUpdateService.cs`, `Desk/DeskController.cs` | Use `Toasts` |
| `src/Sanduhr.App/Services/SettingsStore.cs` | `agent_notifications`, migration from `claude_code_glow` |
| `src/Sanduhr.App/Services/ClaudeCodeGlowService.cs` | `Glow(color, flash)`, `FlashOnly()` |
| `src/Sanduhr.App/Desk/DeskHandoffSeams.cs` | `WatcherStore.ApplyTurn`, `ClearTurns`, `RetitleTurns` |
| `src/Sanduhr.App/App.xaml.cs` | Wiring |
| `src/Sanduhr.App/ViewModels/NotificationsViewModel.cs`, `Views/NotificationsPage.xaml(.cs)` (new) | The Notifications page |
| `src/Sanduhr.App/Views/ClaudeCodePage.xaml`, `ViewModels/ClaudeCodeViewModel.cs`, `Views/SettingsWindow.xaml` | The glow switches leave the Claude Code page; the new tab |
| `docs/PRIVACY.md`, `CHANGELOG.md`, `docs/store/hub-privacy-5.0.0.md` (new) | Disclosure |

Tests sit beside their project: `tests/Sanduhr.Tests/` for Core, `tests/Sanduhr.App.Tests/` for the App. Core tests use the existing `TempDir` helper (`using var t = new TempDir(); t.Path`).

**Three places the code map changed the spec's first wording. The spec was amended to match on 2026-11-17, with this plan:**

- Desk cards use a new `WatcherSource.Turn`, not `WatcherSource.Automatic`. `Automatic` is the Mac port's seam for Claude Code's background tasks from the Stop hook, with its own one-hour window and ids.
- The watcher runs its own `FileSystemWatcher` on Codex's `sessions` folder and does not share `CodexUsageService`'s. That service is rebuilt whenever the statusline or MCP server is installed or removed, and exists only while Codex limits are on.
- "Where the glow shows" stays on the Claude Code page, with a link from Notifications. Moving the picker (it carries the Desk piece panel and monitor list) belongs to spec 6's reorganization.

---

### Task 1: The event shape and its words

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentTurns.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/AgentTurnsTests.cs`

**Interfaces:**
- Consumes: `AgentSources.BurnName(string id)`.
- Produces:
  - `enum AgentTurnKind { Started, Finished, Stopped, Waiting }`
  - `sealed record AgentTurnEvent(string Agent, AgentTurnKind Kind, string Session, string? Folder, DateTimeOffset At, TimeSpan? Duration = null)`
  - `static class NotificationText`: `string Title(AgentTurnEvent e)`, `string Body(AgentTurnEvent e, string? project)`, `string Duration(TimeSpan d)`, `string CardTitle(string agent, string? project)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentTurnsTests
{
    private static AgentTurnEvent E(string agent, AgentTurnKind kind, TimeSpan? d = null) =>
        new(agent, kind, "s1", @"C:\proj\alpha", DateTimeOffset.UnixEpoch, d);

    [Theory]
    [InlineData("codex", AgentTurnKind.Finished, "Codex finished")]
    [InlineData("claude", AgentTurnKind.Finished, "Claude Code finished")]
    [InlineData("antigravity", AgentTurnKind.Stopped, "Antigravity stopped")]
    [InlineData("claude", AgentTurnKind.Waiting, "Claude Code is waiting on you")]
    public void Title_names_the_agent_and_what_happened(string agent, AgentTurnKind kind, string expected) =>
        Assert.Equal(expected, NotificationText.Title(E(agent, kind)));

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(124, "2m 04s")]
    [InlineData(252, "4m 12s")]
    [InlineData(3900, "1h 05m")]
    [InlineData(-5, "0s")]
    public void Duration_reads_as_a_person_says_it(int seconds, string expected) =>
        Assert.Equal(expected, NotificationText.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Body_is_the_project_then_the_duration() =>
        Assert.Equal("alpha, 4m 12s", NotificationText.Body(E("codex", AgentTurnKind.Finished, TimeSpan.FromSeconds(252)), "alpha"));

    [Fact]
    public void Body_for_waiting_is_the_project_alone() =>
        Assert.Equal("alpha", NotificationText.Body(E("claude", AgentTurnKind.Waiting, TimeSpan.FromSeconds(9)), "alpha"));

    [Fact]
    public void Body_without_a_project_is_the_duration_and_without_either_is_empty()
    {
        Assert.Equal("45s", NotificationText.Body(E("codex", AgentTurnKind.Finished, TimeSpan.FromSeconds(45)), null));
        Assert.Equal("", NotificationText.Body(E("codex", AgentTurnKind.Finished), " "));
    }

    [Fact]
    public void Card_title_is_the_agent_and_the_project()
    {
        Assert.Equal("Codex · alpha", NotificationText.CardTitle("codex", "alpha"));
        Assert.Equal("Claude Code", NotificationText.CardTitle("claude", null));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentTurnsTests"`
Expected: build error, `AgentTurnEvent` and `NotificationText` do not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Globalization;

namespace Sanduhr.Core;

/// <summary>What an agent's turn did (5.0 spec 4).</summary>
public enum AgentTurnKind
{
    Started,
    Finished,

    /// <summary>The turn ended without finishing: aborted, or an error.</summary>
    Stopped,

    /// <summary>The agent needs the person before it can go on.</summary>
    Waiting,
}

/// <summary>One thing an agent's turn did, the same shape for every agent. <paramref name="Session"/> tells one run from
/// another; <paramref name="Folder"/> is the project folder when the agent's files name one; <paramref name="Duration"/> is
/// how long the turn ran, null when that is not known. Never carries message text.</summary>
public sealed record AgentTurnEvent(string Agent, AgentTurnKind Kind, string Session, string? Folder, DateTimeOffset At,
    TimeSpan? Duration = null);

/// <summary>The words a notification and a Desk card use.</summary>
public static class NotificationText
{
    public static string Title(AgentTurnEvent e) => e.Kind switch
    {
        AgentTurnKind.Waiting => $"{AgentSources.BurnName(e.Agent)} is waiting on you",
        AgentTurnKind.Stopped => $"{AgentSources.BurnName(e.Agent)} stopped",
        _ => $"{AgentSources.BurnName(e.Agent)} finished",
    };

    /// <summary>"alpha, 4m 12s"; the project alone for waiting; whichever part is known; empty with neither.
    /// <paramref name="project"/> arrives already masked for streamer mode.</summary>
    public static string Body(AgentTurnEvent e, string? project)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(project)) parts.Add(project);
        if (e.Kind != AgentTurnKind.Waiting && e.Duration is { } d) parts.Add(Duration(d));
        return string.Join(", ", parts);
    }

    /// <summary>"45s", "4m 12s", "1h 05m".</summary>
    public static string Duration(TimeSpan d)
    {
        long s = (long)Math.Max(0, Math.Floor(d.TotalSeconds));
        if (s < 60) return string.Create(CultureInfo.InvariantCulture, $"{s}s");
        if (s < 3600) return string.Create(CultureInfo.InvariantCulture, $"{s / 60}m {s % 60:00}s");
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600}h {s % 3600 / 60:00}m");
    }

    /// <summary>A Desk card's title: "Codex · alpha", or the agent alone.</summary>
    public static string CardTitle(string agent, string? project) =>
        string.IsNullOrWhiteSpace(project) ? AgentSources.BurnName(agent) : $"{AgentSources.BurnName(agent)} · {project}";
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentTurnsTests"`
Expected: PASS, 13 tests.

- [ ] **Step 5: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/AgentTurns.cs windows-dotnet/tests/Sanduhr.Tests/AgentTurnsTests.cs
git commit -m "feat(windows): the agent turn event and its words"
```

---

### Task 2: The Codex turn reader

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/CodexTurnReader.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/CodexTurnReaderTests.cs`

**Interfaces:**
- Consumes: `AgentTurnEvent`, `AgentTurnKind` (Task 1); `BurnJson.Str(JsonObject, string)` and `BurnJson.ParseIso(string?)` from `BurnParsing.cs`.
- Produces: `sealed class CodexTurnReader(string session)` with `bool IsChild`, `bool TurnOpen`, `AgentTurnEvent? Read(string line)`. One reader per rollout file; feed it every line in order.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexTurnReaderTests
{
    private const string T0 = "2026-11-17T20:00:00.000Z";
    private const string T2m = "2026-11-17T20:02:04.000Z";

    private static string Line(string timestamp, string type, Dictionary<string, object?> payload) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["timestamp"] = timestamp, ["type"] = type, ["payload"] = payload });

    private static string Meta(string? parent = null, string? forkedFrom = null, string cwd = @"C:\proj\alpha") =>
        Line(T0, "session_meta", new() { ["id"] = "s", ["cwd"] = cwd, ["parent_thread_id"] = parent, ["forked_from_id"] = forkedFrom });

    private static string Context(string cwd) => Line(T0, "turn_context", new() { ["cwd"] = cwd, ["model"] = "m" });

    private static string Event(string timestamp, string kind, Dictionary<string, object?>? more = null)
    {
        var p = new Dictionary<string, object?> { ["type"] = kind, ["turn_id"] = "t1" };
        foreach (var kv in more ?? []) p[kv.Key] = kv.Value;
        return Line(timestamp, "event_msg", p);
    }

    [Fact]
    public void A_turn_starts_and_finishes_with_the_logged_duration()
    {
        var r = new CodexTurnReader("rollout-a");
        Assert.Null(r.Read(Meta()));
        var started = r.Read(Event(T0, "task_started"));
        Assert.Equal(new AgentTurnEvent("codex", AgentTurnKind.Started, "rollout-a", @"C:\proj\alpha", DateTimeOffset.Parse(T0)), started);
        Assert.True(r.TurnOpen);

        var done = r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 124000 }));
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(124), done.Duration);
        Assert.False(r.TurnOpen);
    }

    [Fact]
    public void Without_a_logged_duration_it_is_the_time_between_the_markers()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        Assert.Equal(TimeSpan.FromSeconds(124), r.Read(Event(T2m, "task_complete"))!.Duration);
    }

    [Fact]
    public void A_completion_stamped_before_its_start_has_no_duration()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T2m, "task_started"));
        var done = r.Read(Event(T0, "task_complete"));
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Null(done.Duration);
    }

    [Fact]
    public void An_aborted_turn_is_stopped()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        var stopped = r.Read(Event(T2m, "turn_aborted"));
        Assert.Equal(AgentTurnKind.Stopped, stopped!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(124), stopped.Duration);
    }

    [Fact]
    public void The_project_is_the_turn_contexts_folder()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta(cwd: @"C:\proj\alpha"));
        r.Read(Context(@"C:\proj\beta"));
        Assert.Equal(@"C:\proj\beta", r.Read(Event(T0, "task_started"))!.Folder);
    }

    [Theory]
    [InlineData("parent-thread", null)]
    [InlineData(null, "forked-from")]
    public void A_sub_agent_rollout_raises_nothing(string? parent, string? forkedFrom)
    {
        var r = new CodexTurnReader("rollout-child");
        r.Read(Meta(parent, forkedFrom));
        Assert.True(r.IsChild);
        Assert.Null(r.Read(Event(T0, "task_started")));
        Assert.Null(r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 124000 })));
    }

    /// <summary>A forked rollout's second session_meta is its parent's copy, without the keys (seen 2026-11-17).</summary>
    [Fact]
    public void The_child_mark_sticks_after_a_later_meta_without_it()
    {
        var r = new CodexTurnReader("rollout-child");
        r.Read(Meta(parent: "parent-thread"));
        r.Read(Meta());
        Assert.True(r.IsChild);
        Assert.Null(r.Read(Event(T0, "task_started")));
    }

    [Fact]
    public void The_agents_last_message_never_reaches_an_event()
    {
        var r = new CodexTurnReader("rollout-a");
        r.Read(Meta());
        r.Read(Event(T0, "task_started"));
        var done = r.Read(Event(T2m, "task_complete", new() { ["duration_ms"] = 1000, ["last_agent_message"] = "SECRET-REPLY" }));
        Assert.DoesNotContain("SECRET-REPLY", done!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"event_msg\"}")]
    [InlineData("{\"timestamp\":\"nope\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}")]
    [InlineData("{\"type\":\"event_msg\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}")]
    [InlineData("{\"timestamp\":\"2026-11-17T20:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"content\":\"task_complete\"}}")]
    public void A_line_it_cannot_use_is_skipped(string line) =>
        Assert.Null(new CodexTurnReader("rollout-a").Read(line));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~CodexTurnReaderTests"`
Expected: build error, `CodexTurnReader` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Codex's turn markers (5.0 spec 4). One reader per rollout, fed every line in order. <c>task_started</c> opens a turn,
/// <c>task_complete</c> finishes it and <c>turn_aborted</c> stops it; the project is the <c>cwd</c> of the
/// <c>turn_context</c> in force. A rollout whose <c>session_meta</c> names a parent thread is a sub-agent's: it raises
/// nothing, and that mark sticks, because a forked rollout repeats its parent's meta without the keys.
/// Reads timestamps, ids, folders and the duration; never <c>last_agent_message</c> or any other text.
/// </summary>
public sealed class CodexTurnReader(string session)
{
    private string? _cwd;
    private DateTimeOffset? _started;

    public bool IsChild { get; private set; }

    public bool TurnOpen => _started is not null;

    public AgentTurnEvent? Read(string line)
    {
        try { return ReadCore(line); }
        // Duplicate keys throw ArgumentException lazily on access; a malformed line is skipped, never thrown.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private AgentTurnEvent? ReadCore(string line)
    {
        if (!line.Contains("\"session_meta\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_context\"", StringComparison.Ordinal)
            && !line.Contains("\"task_started\"", StringComparison.Ordinal)
            && !line.Contains("\"task_complete\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_aborted\"", StringComparison.Ordinal))
            return null;
        if (JsonNode.Parse(line) is not JsonObject d || d["payload"] is not JsonObject p) return null;

        switch (BurnJson.Str(d, "type"))
        {
            case "session_meta":
                _cwd = BurnJson.Str(p, "cwd") ?? _cwd;
                IsChild |= HasText(p, "parent_thread_id") || HasText(p, "forked_from_id");
                return null;
            case "turn_context":
                _cwd = BurnJson.Str(p, "cwd") ?? _cwd;
                return null;
            case "event_msg":
                break;
            default:
                return null;
        }

        if (BurnJson.ParseIso(BurnJson.Str(d, "timestamp")) is not { } at) return null;
        switch (BurnJson.Str(p, "type"))
        {
            case "task_started":
                _started = at;
                return Raise(AgentTurnKind.Started, at, null);
            case "task_complete":
            {
                var duration = LoggedDuration(p) ?? Since(at);
                _started = null;
                return Raise(AgentTurnKind.Finished, at, duration);
            }
            case "turn_aborted":
            {
                var duration = Since(at);
                _started = null;
                return Raise(AgentTurnKind.Stopped, at, duration);
            }
            default:
                return null;
        }
    }

    private AgentTurnEvent? Raise(AgentTurnKind kind, DateTimeOffset at, TimeSpan? duration) =>
        IsChild ? null : new AgentTurnEvent(AgentSources.Codex, kind, session, _cwd, at, duration);

    private TimeSpan? Since(DateTimeOffset at) => _started is { } s && at >= s ? at - s : null;

    private static TimeSpan? LoggedDuration(JsonObject p) =>
        p["duration_ms"] is JsonValue v && v.TryGetValue<double>(out var ms) && ms >= 0 ? TimeSpan.FromMilliseconds(ms) : null;

    private static bool HasText(JsonObject p, string key) =>
        p[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0;
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~CodexTurnReaderTests"`
Expected: PASS, 15 tests.

- [ ] **Step 5: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/CodexTurnReader.cs windows-dotnet/tests/Sanduhr.Tests/CodexTurnReaderTests.cs
git commit -m "feat(windows): read Codex's turn markers"
```

---

### Task 3: The Antigravity turn reader

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AntigravityTurnReader.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/AntigravityTurnReaderTests.cs`

**Interfaces:**
- Consumes: `AgentTurnEvent`, `AgentTurnKind` (Task 1); `BurnJson`.
- Produces: `sealed class AntigravityTurnReader(string session)` with
  - `static readonly TimeSpan Quiet` (90 seconds)
  - `bool TurnOpen`
  - `DateTimeOffset? DueAt` (when `Due` could next raise; null with nothing pending)
  - `AgentTurnEvent? Read(string line, string? folder, DateTimeOffset now)` (returns `Started` for a `USER_INPUT`, else null; any line cancels a pending end)
  - `void Activity()` (a line arrived that was not read, for example an oversized one: cancels a pending end)
  - `AgentTurnEvent? Due(DateTimeOffset now, string? folder)` (returns `Finished` once a final-looking step has been quiet for `Quiet`)
  - `void Settle()` (after priming at launch: drops a pending end and closes the turn without an event)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AntigravityTurnReaderTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private static string Step(DateTimeOffset createdAt, string type, string status = "DONE", bool toolCalls = false, string? content = null)
    {
        var o = new Dictionary<string, object?>
        {
            ["type"] = type, ["status"] = status, ["created_at"] = createdAt.ToString("o"), ["step_index"] = 1, ["source"] = "MODEL",
        };
        if (toolCalls) o["tool_calls"] = new[] { new { name = "SECRET-TOOL" } };
        if (content is not null) o["content"] = content;
        return JsonSerializer.Serialize(o);
    }

    [Fact]
    public void A_user_input_starts_a_turn()
    {
        var r = new AntigravityTurnReader("conv-1");
        var e = r.Read(Step(T0, "USER_INPUT"), @"C:\proj\alpha", T0);
        Assert.Equal(new AgentTurnEvent("antigravity", AgentTurnKind.Started, "conv-1", @"C:\proj\alpha", T0), e);
        Assert.True(r.TurnOpen);
    }

    [Fact]
    public void A_final_looking_step_finishes_after_ninety_quiet_seconds()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        var end = T0.AddSeconds(200);
        Assert.Null(r.Read(Step(end, "PLANNER_RESPONSE"), null, end));
        Assert.Equal(end + AntigravityTurnReader.Quiet, r.DueAt);
        Assert.Null(r.Due(end.AddSeconds(89), null));

        var done = r.Due(end.AddSeconds(90), @"C:\proj\alpha");
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Equal(end, done.At);
        Assert.Equal(TimeSpan.FromSeconds(200), done.Duration);
        Assert.Equal(@"C:\proj\alpha", done.Folder);
        Assert.False(r.TurnOpen);
        Assert.Null(r.DueAt);
        Assert.Null(r.Due(end.AddSeconds(500), null));
    }

    [Fact]
    public void A_step_that_asks_for_a_tool_is_not_an_ending()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true), null, T0.AddSeconds(5));
        Assert.Null(r.DueAt);
        Assert.Null(r.Due(T0.AddSeconds(900), null));
    }

    /// <summary>19 of 341 mid-turn steps looked final and were followed by a SYSTEM_MESSAGE 5 to 83 s later (2026-11-17).</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(83)]
    public void A_look_alike_ending_followed_by_more_work_raises_nothing(int gapSeconds)
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(30), "PLANNER_RESPONSE"), null, T0.AddSeconds(30));
        var next = T0.AddSeconds(30 + gapSeconds);
        Assert.Null(r.Read(Step(next, "SYSTEM_MESSAGE"), null, next));
        Assert.Null(r.DueAt);
        Assert.Null(r.Due(next.AddSeconds(900), null));
        Assert.True(r.TurnOpen);
    }

    [Fact]
    public void A_line_that_was_not_read_still_cancels_a_pending_end()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(30), "PLANNER_RESPONSE"), null, T0.AddSeconds(30));
        r.Activity();
        Assert.Null(r.DueAt);
    }

    [Fact]
    public void A_running_or_failed_planner_step_is_not_an_ending()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", status: "RUNNING"), null, T0.AddSeconds(5));
        Assert.Null(r.DueAt);
        r.Read(Step(T0.AddSeconds(6), "GENERIC", status: "ERROR"), null, T0.AddSeconds(6));
        Assert.Null(r.DueAt);
    }

    [Fact]
    public void An_ending_stamped_before_its_start_has_no_duration()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0.AddSeconds(50), "USER_INPUT"), null, T0);
        r.Read(Step(T0, "PLANNER_RESPONSE"), null, T0.AddSeconds(60));
        Assert.Null(r.Due(T0.AddSeconds(200), null)!.Duration);
    }

    [Fact]
    public void An_ending_with_no_start_seen_still_finishes_with_no_duration()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "PLANNER_RESPONSE"), null, T0);
        var done = r.Due(T0.AddSeconds(90), null);
        Assert.Equal(AgentTurnKind.Finished, done!.Kind);
        Assert.Null(done.Duration);
    }

    [Fact]
    public void Settle_drops_a_pending_end_and_closes_the_turn_silently()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(30), "PLANNER_RESPONSE"), null, T0.AddSeconds(30));
        r.Settle();
        Assert.Null(r.DueAt);
        Assert.False(r.TurnOpen);
        Assert.Null(r.Due(T0.AddSeconds(900), null));
    }

    [Fact]
    public void Settle_leaves_a_turn_that_is_still_working_open()
    {
        var r = new AntigravityTurnReader("conv-1");
        r.Read(Step(T0, "USER_INPUT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true), null, T0.AddSeconds(5));
        r.Settle();
        Assert.True(r.TurnOpen);
    }

    [Fact]
    public void Step_text_and_tool_names_never_reach_an_event()
    {
        var r = new AntigravityTurnReader("conv-1");
        var started = r.Read(Step(T0, "USER_INPUT", content: "SECRET-PROMPT"), null, T0);
        r.Read(Step(T0.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true, content: "SECRET-REPLY"), null, T0.AddSeconds(5));
        r.Read(Step(T0.AddSeconds(9), "PLANNER_RESPONSE", content: "SECRET-REPLY"), null, T0.AddSeconds(9));
        var done = r.Due(T0.AddSeconds(200), null);
        Assert.DoesNotContain("SECRET", started!.ToString());
        Assert.DoesNotContain("SECRET", done!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"type\":\"USER_INPUT\",\"type\":\"USER_INPUT\"}")]
    [InlineData("{\"type\":\"USER_INPUT\",\"created_at\":\"nope\"}")]
    public void A_line_it_cannot_use_is_skipped(string line) =>
        Assert.Null(new AntigravityTurnReader("conv-1").Read(line, null, T0));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AntigravityTurnReaderTests"`
Expected: build error, `AntigravityTurnReader` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The Antigravity CLI's turns (5.0 spec 4). Its transcript has no turn-end marker, so the end is inferred: a
/// <c>PLANNER_RESPONSE</c> with status <c>DONE</c> and no <c>tool_calls</c> key, followed by <see cref="Quiet"/> with no
/// new line. On the 2026-11-17 sample a step that looked final was followed by more work up to 83 seconds later, so
/// 90 seconds is the shortest wait that raised nothing falsely. Only whether <c>tool_calls</c> exists is read, never
/// its value, and never a step's <c>content</c> or <c>thinking</c>.
/// </summary>
public sealed class AntigravityTurnReader(string session)
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(90);

    private DateTimeOffset? _turnStart;   // the turn's USER_INPUT created_at
    private bool _open;
    private DateTimeOffset? _finalAt;     // created_at of a step that looks final
    private DateTimeOffset? _finalSeen;   // when that line was read

    public bool TurnOpen => _open;

    /// <summary>When <see cref="Due"/> could next raise; null with nothing pending.</summary>
    public DateTimeOffset? DueAt => _finalSeen is { } seen ? seen + Quiet : null;

    /// <summary>A line arrived that was not read (an oversized one): more work, so a pending end is off.</summary>
    public void Activity()
    {
        _finalAt = null;
        _finalSeen = null;
    }

    public AgentTurnEvent? Read(string line, string? folder, DateTimeOffset now)
    {
        Activity();
        try { return ReadCore(line, folder, now); }
        // Duplicate keys throw ArgumentException lazily on access; a malformed line is skipped, never thrown.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private AgentTurnEvent? ReadCore(string line, string? folder, DateTimeOffset now)
    {
        if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject d) return null;
        var type = BurnJson.Str(d, "type");
        if (BurnJson.ParseIso(BurnJson.Str(d, "created_at")) is not { } at) return null;

        if (type == "USER_INPUT")
        {
            _turnStart = at;
            _open = true;
            return new AgentTurnEvent(AgentSources.Antigravity, AgentTurnKind.Started, session, folder, at);
        }
        if (type == "PLANNER_RESPONSE" && BurnJson.Str(d, "status") == "DONE" && !d.ContainsKey("tool_calls"))
        {
            _finalAt = at;
            _finalSeen = now;
        }
        return null;
    }

    /// <summary>Finished, once a step that looks final has had <see cref="Quiet"/> with no new line.</summary>
    public AgentTurnEvent? Due(DateTimeOffset now, string? folder)
    {
        if (_finalSeen is not { } seen || _finalAt is not { } at || now - seen < Quiet) return null;
        TimeSpan? duration = _turnStart is { } s && at >= s ? at - s : null;
        Close();
        return new AgentTurnEvent(AgentSources.Antigravity, AgentTurnKind.Finished, session, folder, at, duration);
    }

    /// <summary>After reading history at launch: a pending end closes its turn with no event, since nothing replays.</summary>
    public void Settle()
    {
        if (_finalSeen is not null) Close();
    }

    private void Close()
    {
        _turnStart = null;
        _open = false;
        Activity();
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AntigravityTurnReaderTests"`
Expected: PASS, 17 tests.

- [ ] **Step 5: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/AntigravityTurnReader.cs windows-dotnet/tests/Sanduhr.Tests/AntigravityTurnReaderTests.cs
git commit -m "feat(windows): read the Antigravity CLI's turns, with the quiet-time end"
```

---

### Task 4: Claude Code's signal file

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/ClaudeCodeSignalFile.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/ClaudeCodeSignalFileTests.cs`

**Interfaces:**
- Consumes: `AgentTurnEvent`, `AgentTurnKind` (Task 1).
- Produces:
  - `static class ClaudeCodeSignalFile`: `const string Prefix = "cc-signal-"`, `const string Pattern = "cc-signal-*.json"`, `const int MaxBytes = 4096`, `static readonly TimeSpan MaxAge` (10 minutes), `const string Prompt = "prompt"`, `Waiting = "waiting"`, `Done = "done"`, `const string NoSession = "-"`, `sealed record Signal(string Event, string Session, string? Folder, DateTimeOffset At)`, `static Signal? Decode(ReadOnlySpan<byte> data, DateTimeOffset now)`
  - `sealed class ClaudeCodeTurns` with `AgentTurnEvent Apply(ClaudeCodeSignalFile.Signal s)`

The file's shape, written by the hook in Task 7: `{"v":1,"event":"prompt"|"waiting"|"done","cwd":"…","session":"…","at":<unix ms>}`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeCodeSignalFileTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private static byte[] File(string ev = "done", string? cwd = @"C:\proj\alpha", string? session = "sess-1", long? at = null, int v = 1) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = v, ["event"] = ev, ["cwd"] = cwd, ["session"] = session, ["at"] = at ?? Now.ToUnixTimeMilliseconds(),
        }));

    [Theory]
    [InlineData("prompt")]
    [InlineData("waiting")]
    [InlineData("done")]
    public void A_signal_decodes(string ev)
    {
        var s = ClaudeCodeSignalFile.Decode(File(ev), Now);
        Assert.Equal(new ClaudeCodeSignalFile.Signal(ev, "sess-1", @"C:\proj\alpha", Now), s);
    }

    [Fact]
    public void An_empty_folder_or_session_is_kept_as_unknown()
    {
        var s = ClaudeCodeSignalFile.Decode(File(cwd: "", session: ""), Now);
        Assert.Null(s!.Folder);
        Assert.Equal(ClaudeCodeSignalFile.NoSession, s.Session);
    }

    [Fact]
    public void A_signal_older_than_ten_minutes_or_from_the_future_is_dropped()
    {
        Assert.Null(ClaudeCodeSignalFile.Decode(File(at: Now.AddMinutes(-11).ToUnixTimeMilliseconds()), Now));
        Assert.Null(ClaudeCodeSignalFile.Decode(File(at: Now.AddMinutes(2).ToUnixTimeMilliseconds()), Now));
        Assert.NotNull(ClaudeCodeSignalFile.Decode(File(at: Now.AddMinutes(-9).ToUnixTimeMilliseconds()), Now));
    }

    [Fact]
    public void An_oversized_file_is_dropped() =>
        Assert.Null(ClaudeCodeSignalFile.Decode(File(cwd: new string('a', 5000)), Now));

    [Fact]
    public void A_folder_over_1024_characters_is_dropped_but_the_signal_stands()
    {
        var s = ClaudeCodeSignalFile.Decode(File(cwd: new string('a', 1100)), Now);
        Assert.NotNull(s);
        Assert.Null(s!.Folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"v\":2,\"event\":\"done\",\"at\":1}")]
    [InlineData("{\"v\":1,\"event\":\"other\",\"at\":1}")]
    [InlineData("{\"v\":1,\"event\":\"done\"}")]
    [InlineData("{\"v\":1,\"v\":1,\"event\":\"done\",\"at\":1}")]
    [InlineData("{\"v\":1,\"event\":\"done\",\"at\":99999999999999999}")]
    public void Anything_else_is_dropped(string text) =>
        Assert.Null(ClaudeCodeSignalFile.Decode(Encoding.UTF8.GetBytes(text), Now));

    [Fact]
    public void A_prompt_then_a_done_is_a_turn_with_its_length()
    {
        var turns = new ClaudeCodeTurns();
        var started = turns.Apply(new("prompt", "sess-1", @"C:\proj\alpha", Now));
        Assert.Equal(new AgentTurnEvent("claude", AgentTurnKind.Started, "sess-1", @"C:\proj\alpha", Now), started);

        var done = turns.Apply(new("done", "sess-1", @"C:\proj\alpha", Now.AddSeconds(252)));
        Assert.Equal(AgentTurnKind.Finished, done.Kind);
        Assert.Equal(TimeSpan.FromSeconds(252), done.Duration);
    }

    [Fact]
    public void A_done_with_no_prompt_seen_has_no_duration() =>
        Assert.Null(new ClaudeCodeTurns().Apply(new("done", "sess-1", null, Now)).Duration);

    [Fact]
    public void A_done_stamped_before_its_prompt_has_no_duration()
    {
        var turns = new ClaudeCodeTurns();
        turns.Apply(new("prompt", "sess-1", null, Now));
        Assert.Null(turns.Apply(new("done", "sess-1", null, Now.AddSeconds(-5))).Duration);
    }

    [Fact]
    public void Waiting_does_not_end_the_turn()
    {
        var turns = new ClaudeCodeTurns();
        turns.Apply(new("prompt", "sess-1", null, Now));
        Assert.Equal(AgentTurnKind.Waiting, turns.Apply(new("waiting", "sess-1", null, Now.AddSeconds(30))).Kind);
        Assert.Equal(TimeSpan.FromSeconds(90), turns.Apply(new("done", "sess-1", null, Now.AddSeconds(90))).Duration);
    }

    [Fact]
    public void Two_sessions_keep_their_own_start_times()
    {
        var turns = new ClaudeCodeTurns();
        turns.Apply(new("prompt", "a", null, Now));
        turns.Apply(new("prompt", "b", null, Now.AddSeconds(60)));
        Assert.Equal(TimeSpan.FromSeconds(100), turns.Apply(new("done", "a", null, Now.AddSeconds(100))).Duration);
        Assert.Equal(TimeSpan.FromSeconds(40), turns.Apply(new("done", "b", null, Now.AddSeconds(100))).Duration);
    }

    [Fact]
    public void Past_the_cap_the_oldest_start_is_forgotten()
    {
        var turns = new ClaudeCodeTurns();
        for (int i = 0; i <= ClaudeCodeTurns.MaxSessions; i++)
            turns.Apply(new("prompt", "s" + i, null, Now.AddSeconds(i)));
        Assert.Null(turns.Apply(new("done", "s0", null, Now.AddSeconds(500))).Duration);
        Assert.NotNull(turns.Apply(new("done", "s1", null, Now.AddSeconds(500))).Duration);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeCodeSignalFileTests"`
Expected: build error, `ClaudeCodeSignalFile` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The file Claude Code's hook writes for each event (5.0 spec 4): <c>cc-signal-&lt;unix ms&gt;-&lt;pid&gt;.json</c> under
/// <c>%USERPROFILE%\.sanduhr</c>, holding the event, the session's folder and its id. One file per event, so two sessions
/// never overwrite each other. Sanduhr deletes each as it reads it. Nothing of the conversation is in it.
/// </summary>
public static class ClaudeCodeSignalFile
{
    public const string Prefix = "cc-signal-";
    public const string Pattern = Prefix + "*.json";
    public const int MaxBytes = 4096;
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxAhead = TimeSpan.FromMinutes(1);

    public const string Prompt = "prompt";
    public const string Waiting = "waiting";
    public const string Done = "done";

    /// <summary>The session of a signal that named none.</summary>
    public const string NoSession = "-";

    public sealed record Signal(string Event, string Session, string? Folder, DateTimeOffset At);

    /// <summary>The signal in <paramref name="data"/>, null for anything that is not one: over the size cap, not this
    /// version, an unknown event, older than <see cref="MaxAge"/> or stamped in the future.</summary>
    public static Signal? Decode(ReadOnlySpan<byte> data, DateTimeOffset now)
    {
        if (data.Length == 0 || data.Length > MaxBytes) return null;
        try
        {
            if (JsonNode.Parse(data) is not JsonObject o) return null;
            if (o["v"] is not JsonValue v || !v.TryGetValue<int>(out var version) || version != 1) return null;
            var ev = Str(o, "event");
            if (ev != Prompt && ev != Waiting && ev != Done) return null;
            if (o["at"] is not JsonValue a || !a.TryGetValue<long>(out var ms)) return null;
            var at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            if (now - at > MaxAge || at - now > MaxAhead) return null;

            var session = Str(o, "session");
            if (string.IsNullOrWhiteSpace(session)) session = NoSession;
            if (session.Length > 128) return null;
            var folder = Str(o, "cwd");
            if (string.IsNullOrWhiteSpace(folder) || folder.Length > 1024) folder = null;
            return new Signal(ev!, session, folder, at);
        }
        // Duplicate keys throw ArgumentException lazily; a time out of range throws ArgumentOutOfRangeException (an ArgumentException).
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException) { return null; }
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>Turns Claude Code's signals into turn events: a prompt starts a turn, done finishes it with the time between
/// the two, waiting is waiting. Start times are kept per session, in memory, for at most <see cref="MaxSessions"/>.</summary>
public sealed class ClaudeCodeTurns
{
    public const int MaxSessions = 64;

    private readonly Dictionary<string, DateTimeOffset> _started = new(StringComparer.Ordinal);

    public AgentTurnEvent Apply(ClaudeCodeSignalFile.Signal s)
    {
        switch (s.Event)
        {
            case ClaudeCodeSignalFile.Prompt:
                if (_started.Count >= MaxSessions && !_started.ContainsKey(s.Session))
                    _started.Remove(_started.MinBy(kv => kv.Value).Key);
                _started[s.Session] = s.At;
                return new AgentTurnEvent(AgentSources.Claude, AgentTurnKind.Started, s.Session, s.Folder, s.At);
            case ClaudeCodeSignalFile.Waiting:
                return new AgentTurnEvent(AgentSources.Claude, AgentTurnKind.Waiting, s.Session, s.Folder, s.At);
            default:
                TimeSpan? duration = _started.Remove(s.Session, out var start) && s.At >= start ? s.At - start : null;
                return new AgentTurnEvent(AgentSources.Claude, AgentTurnKind.Finished, s.Session, s.Folder, s.At, duration);
        }
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeCodeSignalFileTests"`
Expected: PASS, 22 tests.

- [ ] **Step 5: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/ClaudeCodeSignalFile.cs windows-dotnet/tests/Sanduhr.Tests/ClaudeCodeSignalFileTests.cs
git commit -m "feat(windows): Claude Code's signal file and its turns"
```

---

### Task 5: The settings and the rules

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/NotificationRules.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/NotificationRulesTests.cs`

**Interfaces:**
- Consumes: `AgentTurnEvent`, `AgentTurnKind` (Task 1); `GlowSwitches`, `ClaudeCodeGlowLimiter`, `ClaudeCodeEvent`, `ClaudeCodeGlowRules.TerminalProcessNames` from `ClaudeCodeGlow.cs`.
- Produces:
  - `sealed record NotificationSettings(IReadOnlyDictionary<string, bool> Agents, bool Toast, bool GlowFinished, bool GlowWaiting, bool Flash, bool DeskCard, int ThresholdSeconds, bool SkipInFront)` with `const string Key = "agent_notifications"`, `static IReadOnlyList<int> Thresholds`, `const int DefaultThreshold = 60`, `static NotificationSettings Default`, `bool AgentOn(string agent)`, `NotificationSettings WithAgent(string agent, bool on)`, `static NotificationSettings Decode(JsonNode? node, GlowSwitches legacy)`, `JsonObject Encode()`
  - `[Flags] enum NotifySurfaces { None = 0, Toast = 1, Glow = 2, Flash = 4, Card = 8 }`
  - `sealed class NotificationLimiter` with `bool Allow(AgentTurnEvent e, DateTimeOffset now)`
  - `static class NotificationRules`: `NotifySurfaces Decide(AgentTurnEvent e, NotificationSettings s, bool agentInFront, NotificationLimiter limiter, DateTimeOffset now)`, `bool InFront(string agent, string? processName)`, `IReadOnlySet<string> ProcessesFor(string agent)`

`NotificationSettings` holds a dictionary, so two equal-looking records are not `Equal`: tests compare its members.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class NotificationRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");
    private static readonly NotificationSettings AllOn = NotificationSettings.Default with { GlowFinished = true, GlowWaiting = true, Flash = true };
    private const NotifySurfaces Everything = NotifySurfaces.Toast | NotifySurfaces.Glow | NotifySurfaces.Flash | NotifySurfaces.Card;

    private static AgentTurnEvent E(AgentTurnKind kind, int? seconds = null, string agent = "codex", string session = "s1") =>
        new(agent, kind, session, null, Now, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    private static NotifySurfaces Decide(AgentTurnEvent e, NotificationSettings? s = null, bool inFront = false, NotificationLimiter? limiter = null) =>
        NotificationRules.Decide(e, s ?? AllOn, inFront, limiter ?? new NotificationLimiter(), Now);

    // ---- section 4's table ----

    [Fact]
    public void Started_never_interrupts_and_shows_a_card() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Started)));

    [Theory]
    [InlineData(AgentTurnKind.Finished)]
    [InlineData(AgentTurnKind.Stopped)]
    public void A_long_turn_ending_out_of_sight_uses_every_surface(AgentTurnKind kind) =>
        Assert.Equal(Everything, Decide(E(kind, 60)));

    [Theory]
    [InlineData(AgentTurnKind.Finished)]
    [InlineData(AgentTurnKind.Stopped)]
    public void A_short_turn_only_updates_the_card(AgentTurnKind kind) =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(kind, 59)));

    [Fact]
    public void A_long_turn_with_the_agents_window_in_front_only_updates_the_card() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Finished, 600), inFront: true));

    [Fact]
    public void In_front_does_not_matter_once_the_switch_is_off() =>
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished, 600), AllOn with { SkipInFront = false }, inFront: true));

    [Fact]
    public void Waiting_interrupts_at_once_whatever_the_threshold() =>
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Waiting), AllOn with { ThresholdSeconds = 300 }));

    [Fact]
    public void Waiting_with_the_agents_window_in_front_only_updates_the_card() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Waiting), inFront: true));

    [Fact]
    public void An_unknown_duration_never_interrupts() =>
        Assert.Equal(NotifySurfaces.Card, Decide(E(AgentTurnKind.Finished)));

    [Fact]
    public void Every_turn_interrupts_even_with_an_unknown_duration()
    {
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished), AllOn with { ThresholdSeconds = 0 }));
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished, 1, session: "s2"), AllOn with { ThresholdSeconds = 0 }));
    }

    // ---- the switches ----

    [Fact]
    public void An_agent_switched_off_does_nothing_at_all() =>
        Assert.Equal(NotifySurfaces.None, Decide(E(AgentTurnKind.Finished, 600), AllOn.WithAgent("codex", false)));

    [Fact]
    public void An_agent_not_named_in_the_settings_is_on() =>
        Assert.Equal(Everything, Decide(E(AgentTurnKind.Finished, 600, agent: "antigravity")));

    [Fact]
    public void The_defaults_are_a_toast_and_a_card()
    {
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Card, Decide(E(AgentTurnKind.Finished, 600), NotificationSettings.Default));
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Card, Decide(E(AgentTurnKind.Waiting, session: "s2"), NotificationSettings.Default));
    }

    [Fact]
    public void Each_glow_switch_covers_its_own_kind()
    {
        var onlyFinished = AllOn with { GlowWaiting = false };
        Assert.True(Decide(E(AgentTurnKind.Finished, 600), onlyFinished).HasFlag(NotifySurfaces.Glow));
        Assert.False(Decide(E(AgentTurnKind.Waiting, session: "s2"), onlyFinished).HasFlag(NotifySurfaces.Glow));
    }

    [Fact]
    public void With_the_card_off_and_nothing_to_interrupt_nothing_happens() =>
        Assert.Equal(NotifySurfaces.None, Decide(E(AgentTurnKind.Finished, 600),
            AllOn with { DeskCard = false, Toast = false, GlowFinished = false, Flash = false }));

    // ---- the limiter ----

    [Fact]
    public void The_same_kind_from_one_session_waits_twenty_seconds()
    {
        var limiter = new NotificationLimiter();
        var e = E(AgentTurnKind.Finished, 600);
        Assert.Equal(Everything, NotificationRules.Decide(e, AllOn, false, limiter, Now));
        Assert.Equal(NotifySurfaces.Card, NotificationRules.Decide(e, AllOn, false, limiter, Now.AddSeconds(19)));
        Assert.Equal(Everything, NotificationRules.Decide(e, AllOn, false, limiter, Now.AddSeconds(20)));
    }

    [Fact]
    public void A_finished_within_five_seconds_of_a_waiting_only_updates_the_card()
    {
        var limiter = new NotificationLimiter();
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Waiting), AllOn, false, limiter, Now));
        Assert.Equal(NotifySurfaces.Card, NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, false, limiter, Now.AddSeconds(4)));
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, false, limiter, Now.AddSeconds(5)));
    }

    /// <summary>Review focus: many runs ending together each get their own say; one session never stacks.</summary>
    [Fact]
    public void Each_session_and_each_agent_has_its_own_limit()
    {
        var limiter = new NotificationLimiter();
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, session: "a"), AllOn, false, limiter, Now));
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, session: "b"), AllOn, false, limiter, Now));
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, agent: "claude", session: "a"), AllOn, false, limiter, Now));
        Assert.Equal(NotifySurfaces.Card, NotificationRules.Decide(E(AgentTurnKind.Finished, 600, session: "a"), AllOn, false, limiter, Now.AddSeconds(1)));
    }

    [Fact]
    public void An_event_that_interrupts_nothing_does_not_use_up_the_limit()
    {
        var limiter = new NotificationLimiter();
        NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, agentInFront: true, limiter, Now);
        Assert.Equal(Everything, NotificationRules.Decide(E(AgentTurnKind.Finished, 600), AllOn, false, limiter, Now.AddSeconds(1)));
    }

    // ---- in front ----

    [Theory]
    [InlineData("claude", "WindowsTerminal", true)]
    [InlineData("claude", "Code.exe", true)]
    [InlineData("claude", "codex", false)]
    [InlineData("codex", "codex", true)]
    [InlineData("codex", "Codex.exe", true)]
    [InlineData("codex", "pwsh", true)]
    [InlineData("antigravity", "wezterm-gui", true)]
    [InlineData("antigravity", "codex", false)]
    [InlineData("codex", "chrome", false)]
    [InlineData("codex", null, false)]
    [InlineData("codex", " ", false)]
    public void In_front_is_the_agents_own_list(string agent, string? process, bool expected) =>
        Assert.Equal(expected, NotificationRules.InFront(agent, process));

    // ---- the settings ----

    [Fact]
    public void The_defaults_are_the_specs()
    {
        var d = NotificationSettings.Default;
        Assert.True(d.Toast);
        Assert.True(d.DeskCard);
        Assert.False(d.GlowFinished);
        Assert.False(d.GlowWaiting);
        Assert.False(d.Flash);
        Assert.Equal(60, d.ThresholdSeconds);
        Assert.True(d.SkipInFront);
        Assert.Equal([0, 30, 60, 120, 300], NotificationSettings.Thresholds);
    }

    [Fact]
    public void With_nothing_saved_the_old_glow_switches_carry_over()
    {
        var s = NotificationSettings.Decode(null, new GlowSwitches(Waiting: true, Done: false, SkipTerminal: false));
        Assert.True(s.GlowWaiting);
        Assert.False(s.GlowFinished);
        Assert.True(s.Flash);            // on when either glow switch was on
        Assert.False(s.SkipInFront);
        Assert.True(s.Toast);
        Assert.True(s.DeskCard);
        Assert.Equal(60, s.ThresholdSeconds);

        Assert.False(NotificationSettings.Decode(null, new GlowSwitches()).Flash);
    }

    [Fact]
    public void Saved_settings_win_over_the_old_switches_and_round_trip()
    {
        var saved = (NotificationSettings.Default with { Toast = false, GlowFinished = true, ThresholdSeconds = 120 }).WithAgent("codex", false);
        var back = NotificationSettings.Decode(JsonNode.Parse(saved.Encode().ToJsonString()), new GlowSwitches(Waiting: true, Done: true));
        Assert.False(back.Toast);
        Assert.True(back.GlowFinished);
        Assert.False(back.GlowWaiting);
        Assert.Equal(120, back.ThresholdSeconds);
        Assert.False(back.AgentOn("codex"));
        Assert.True(back.AgentOn("claude"));
    }

    [Theory]
    [InlineData("{\"threshold_seconds\":45}", 60)]
    [InlineData("{\"threshold_seconds\":\"120\"}", 60)]
    [InlineData("{\"threshold_seconds\":0}", 0)]
    [InlineData("{\"threshold_seconds\":300}", 300)]
    public void A_threshold_that_is_not_a_choice_reads_as_the_default(string json, int expected) =>
        Assert.Equal(expected, NotificationSettings.Decode(JsonNode.Parse(json), new GlowSwitches()).ThresholdSeconds);

    [Fact]
    public void Unknown_agents_and_wrong_types_are_ignored()
    {
        var s = NotificationSettings.Decode(JsonNode.Parse("{\"agents\":{\"codex\":false,\"nope\":false,\"claude\":\"no\"},\"toast\":\"yes\"}"), new GlowSwitches());
        Assert.False(s.AgentOn("codex"));
        Assert.True(s.AgentOn("claude"));
        Assert.DoesNotContain("nope", s.Agents.Keys);
        Assert.True(s.Toast);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~NotificationRulesTests"`
Expected: build error, `NotificationSettings` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>What the person chose on Settings, Notifications (5.0 spec 4). Stored as <c>agent_notifications</c> in
/// settings.json. An agent with no entry in <see cref="Agents"/> is on.</summary>
public sealed record NotificationSettings(
    IReadOnlyDictionary<string, bool> Agents, bool Toast, bool GlowFinished, bool GlowWaiting, bool Flash, bool DeskCard,
    int ThresholdSeconds, bool SkipInFront)
{
    public const string Key = "agent_notifications";

    /// <summary>The threshold's choices in seconds; 0 is "Every turn".</summary>
    public static IReadOnlyList<int> Thresholds { get; } = [0, 30, 60, 120, 300];

    public const int DefaultThreshold = 60;

    public static NotificationSettings Default { get; } =
        new(new Dictionary<string, bool>(), Toast: true, GlowFinished: false, GlowWaiting: false, Flash: false, DeskCard: true,
            ThresholdSeconds: DefaultThreshold, SkipInFront: true);

    public bool AgentOn(string agent) => Agents.GetValueOrDefault(agent, true);

    public NotificationSettings WithAgent(string agent, bool on) =>
        this with { Agents = new Dictionary<string, bool>(Agents, StringComparer.Ordinal) { [agent] = on } };

    /// <summary>The saved settings; with none saved, the defaults with the old Claude Code glow switches carried over
    /// (the taskbar flash on when either glow was on).</summary>
    public static NotificationSettings Decode(JsonNode? node, GlowSwitches legacy)
    {
        if (node is not JsonObject o)
            return Default with
            {
                GlowFinished = legacy.Done, GlowWaiting = legacy.Waiting, Flash = legacy.Done || legacy.Waiting,
                SkipInFront = legacy.SkipTerminal,
            };

        var agents = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (o["agents"] is JsonObject a)
            foreach (var id in AgentSources.Burn)
                if (a[id] is JsonValue v && v.TryGetValue<bool>(out var on)) agents[id] = on;

        int threshold = o["threshold_seconds"] is JsonValue t && t.TryGetValue<int>(out var seconds) && Thresholds.Contains(seconds)
            ? seconds : DefaultThreshold;
        return new NotificationSettings(agents,
            Toast: Bool(o, "toast", true), GlowFinished: Bool(o, "glow_finished", false), GlowWaiting: Bool(o, "glow_waiting", false),
            Flash: Bool(o, "flash", false), DeskCard: Bool(o, "desk_card", true), ThresholdSeconds: threshold,
            SkipInFront: Bool(o, "skip_in_front", true));
    }

    public JsonObject Encode()
    {
        var agents = new JsonObject();
        foreach (var kv in Agents.OrderBy(kv => kv.Key, StringComparer.Ordinal)) agents[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["agents"] = agents, ["toast"] = Toast, ["glow_finished"] = GlowFinished, ["glow_waiting"] = GlowWaiting,
            ["flash"] = Flash, ["desk_card"] = DeskCard, ["threshold_seconds"] = ThresholdSeconds, ["skip_in_front"] = SkipInFront,
        };
    }

    private static bool Bool(JsonObject o, string key, bool fallback) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
}

/// <summary>Where one event shows.</summary>
[Flags]
public enum NotifySurfaces
{
    None = 0,
    Toast = 1,
    Glow = 2,
    Flash = 4,
    Card = 8,
}

/// <summary>The glow's limiter, kept per agent session: one kind at most every 20 seconds, and no finished within
/// 5 seconds of a waiting. Only an interruption that happens counts.</summary>
public sealed class NotificationLimiter
{
    public const int MaxSessions = 64;

    private readonly Dictionary<(string Agent, string Session), ClaudeCodeGlowLimiter> _bySession = new();

    public bool Allow(AgentTurnEvent e, DateTimeOffset now)
    {
        var key = (e.Agent, e.Session);
        _bySession.TryGetValue(key, out var limiter);
        bool allowed = limiter.Allow(e.Kind == AgentTurnKind.Waiting ? ClaudeCodeEvent.Waiting : ClaudeCodeEvent.Done, now);
        if (_bySession.Count >= MaxSessions && !_bySession.ContainsKey(key)) _bySession.Clear();
        _bySession[key] = limiter;
        return allowed;
    }
}

/// <summary>Section 4 of the spec, in one place: which surfaces an event uses.</summary>
public static class NotificationRules
{
    private static readonly IReadOnlySet<string> CodexProcesses =
        new HashSet<string>(ClaudeCodeGlowRules.TerminalProcessNames, StringComparer.OrdinalIgnoreCase) { "codex" };

    /// <summary>The processes that mean "you are looking at this agent": terminals and editors, plus the Codex app for Codex.</summary>
    public static IReadOnlySet<string> ProcessesFor(string agent) =>
        agent == AgentSources.Codex ? CodexProcesses : ClaudeCodeGlowRules.TerminalProcessNames;

    public static bool InFront(string agent, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var n = processName.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return ProcessesFor(agent).Contains(n);
    }

    public static NotifySurfaces Decide(AgentTurnEvent e, NotificationSettings s, bool agentInFront, NotificationLimiter limiter,
        DateTimeOffset now)
    {
        if (!s.AgentOn(e.Agent)) return NotifySurfaces.None;
        var card = s.DeskCard ? NotifySurfaces.Card : NotifySurfaces.None;
        if (e.Kind == AgentTurnKind.Started) return card;
        if (s.SkipInFront && agentInFront) return card;
        if (e.Kind != AgentTurnKind.Waiting && !LongEnough(e.Duration, s.ThresholdSeconds)) return card;

        var interrupt = NotifySurfaces.None;
        if (s.Toast) interrupt |= NotifySurfaces.Toast;
        if (e.Kind == AgentTurnKind.Waiting ? s.GlowWaiting : s.GlowFinished) interrupt |= NotifySurfaces.Glow;
        if (s.Flash) interrupt |= NotifySurfaces.Flash;
        if (interrupt == NotifySurfaces.None || !limiter.Allow(e, now)) return card;
        return card | interrupt;
    }

    /// <summary>"Every turn" (0) lets anything through; otherwise the turn's length must be known and long enough.</summary>
    private static bool LongEnough(TimeSpan? duration, int thresholdSeconds) =>
        thresholdSeconds <= 0 || (duration is { } d && d.TotalSeconds >= thresholdSeconds);
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~NotificationRulesTests"`
Expected: PASS, 39 tests.

- [ ] **Step 5: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/NotificationRules.cs windows-dotnet/tests/Sanduhr.Tests/NotificationRulesTests.cs
git commit -m "feat(windows): notification settings and the rules that pick the surfaces"
```

---

### Task 6: Turn cards on the watcher board

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/Watchers.cs` (the `WatcherSource` enum near line 66, the `Watcher` record near line 79, `WatcherBoard` from line 244: `Tick`, `NextChange`, `Shown`, and new members)
- Test: `windows-dotnet/tests/Sanduhr.Tests/WatcherTurnCardsTests.cs`

**Interfaces:**
- Consumes: `AgentTurnEvent`, `AgentTurnKind` (Task 1).
- Produces, all on `WatcherBoard` unless noted:
  - `WatcherSource.Turn` (enum member)
  - `Watcher.Folder` (`string? { get; init; }`): the turn's project folder, kept so the title can be rebuilt
  - `static readonly TimeSpan TurnWindow` (1 hour), `AntigravityTurnWindow` (10 minutes), `TurnFadeAfter` (60 seconds)
  - `static string TurnId(string agent, string session)` returning `t:<agent>:<session>`
  - `static TimeSpan? WindowOf(Watcher w)`, `static TimeSpan FadeOf(Watcher w)`
  - `bool ApplyTurn(AgentTurnEvent e, string title, string shortTitle, DateTimeOffset now)`: true when the board changed
  - `bool RetitleTurns(Func<Watcher, (string Title, string Short)> name)`: true when a title changed
  - `bool ClearTurns(string agent)`: true when a card went

A turn card stores the agent id in `Watcher.Kind` and the session in `Watcher.Session`. A stopped turn ends as `WatcherState.Finished` with the note "stopped": `Failed` stays until dismissed and fires the failure glow, which is wrong for a turn the person aborted.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class WatcherTurnCardsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private static AgentTurnEvent E(AgentTurnKind kind, int? seconds = null, string agent = "codex", string session = "s1", string? folder = @"C:\proj\alpha") =>
        new(agent, kind, session, folder, Now, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    private static Watcher Card(WatcherBoard b, string agent = "codex", string session = "s1") =>
        b.Get(WatcherBoard.TurnId(agent, session))!;

    [Fact]
    public void A_started_turn_is_a_running_card()
    {
        var b = new WatcherBoard();
        Assert.True(b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now));
        var w = Card(b);
        Assert.Equal(WatcherSource.Turn, w.Source);
        Assert.Equal(WatcherState.Running, w.State);
        Assert.Equal("Codex · alpha", w.Title);
        Assert.Equal("Codex", w.Short);
        Assert.Equal("codex", w.Kind);
        Assert.Equal("s1", w.Session);
        Assert.Equal(@"C:\proj\alpha", w.Folder);
        Assert.Equal(Now, w.Started);
    }

    [Fact]
    public void A_finished_turn_shows_its_length_and_fades_after_a_minute()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        var end = Now.AddSeconds(300);
        b.ApplyTurn(E(AgentTurnKind.Finished, 252), "Codex · alpha", "Codex", end);

        var w = Card(b);
        Assert.Equal(WatcherState.Finished, w.State);
        Assert.Equal(TimeSpan.FromSeconds(252), w.Elapsed(end.AddSeconds(30)));   // the turn's length, not the wall clock
        Assert.Equal(end + WatcherBoard.TurnFadeAfter, b.NextChange());

        Assert.False(b.Tick(end.AddSeconds(59)));
        Assert.True(b.Tick(end.AddSeconds(60)));
        Assert.Empty(b.Watchers);
    }

    [Fact]
    public void A_finished_turn_with_no_card_still_shows()
    {
        var b = new WatcherBoard();
        Assert.True(b.ApplyTurn(E(AgentTurnKind.Finished, 252), "Codex · alpha", "Codex", Now));
        Assert.Equal(WatcherState.Finished, Card(b).State);
    }

    [Fact]
    public void A_stopped_turn_ends_as_finished_and_says_stopped()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Stopped, 30), "Codex · alpha", "Codex", Now.AddSeconds(30));
        var w = Card(b);
        Assert.Equal(WatcherState.Finished, w.State);
        Assert.Equal("stopped", WatcherText.DeskNote(w));
    }

    [Fact]
    public void Waiting_marks_the_card_and_a_new_prompt_sets_it_running_again()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude"), "Claude Code · alpha", "CC", Now);
        b.ApplyTurn(E(AgentTurnKind.Waiting, agent: "claude"), "Claude Code · alpha", "CC", Now.AddSeconds(20));
        Assert.Equal(WatcherState.Waiting, Card(b, "claude").State);
        Assert.Equal(Now, Card(b, "claude").Started);

        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude"), "Claude Code · alpha", "CC", Now.AddSeconds(40));
        Assert.Equal(WatcherState.Running, Card(b, "claude").State);
        Assert.Equal(Now.AddSeconds(40), Card(b, "claude").Started);
    }

    [Fact]
    public void Waiting_with_no_card_puts_one_up()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Waiting, agent: "claude"), "Claude Code · alpha", "CC", Now);
        Assert.Equal(WatcherState.Waiting, Card(b, "claude").State);
    }

    [Fact]
    public void Two_sessions_and_two_agents_keep_their_own_cards()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started, session: "a"), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, session: "b"), "Codex · beta", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude", session: "a"), "Claude Code · alpha", "CC", Now);
        Assert.Equal(3, b.Watchers.Count);
    }

    [Fact]
    public void A_running_turn_loses_touch_after_an_hour_and_antigravitys_after_ten_minutes()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "antigravity"), "Antigravity · alpha", "Agy", Now);

        b.Tick(Now.AddMinutes(11));
        Assert.Equal(WatcherState.Running, Card(b).State);
        Assert.Equal(WatcherState.LostTouch, Card(b, "antigravity").State);

        b.Tick(Now.AddMinutes(61));
        Assert.Equal(WatcherState.LostTouch, Card(b).State);
    }

    [Fact]
    public void A_card_that_lost_touch_still_finishes()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.Tick(Now.AddMinutes(61));
        b.ApplyTurn(E(AgentTurnKind.Finished, 4000), "Codex · alpha", "Codex", Now.AddMinutes(67));
        Assert.Equal(WatcherState.Finished, Card(b).State);
    }

    /// <summary>Review focus: streamer mode switched on while a card is up.</summary>
    [Fact]
    public void Retitling_renames_turn_cards_only()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.Apply(new WatcherCommand.Start("x", "PR 140", null, null, false), Now);

        Assert.True(b.RetitleTurns(w => ($"Codex · codename-for-{w.Folder}", "Codex")));
        Assert.Equal(@"Codex · codename-for-C:\proj\alpha", Card(b).Title);
        Assert.Equal("PR 140", b.Get(WatcherBoard.AgentId("x"))!.Title);
        Assert.False(b.RetitleTurns(w => (w.Title, w.Short)));
    }

    [Fact]
    public void Clearing_an_agents_turns_leaves_the_other_cards()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        b.ApplyTurn(E(AgentTurnKind.Started, agent: "claude"), "Claude Code · alpha", "CC", Now);
        b.Apply(new WatcherCommand.Start("x", "PR 140", null, null, false), Now);

        Assert.True(b.ClearTurns("codex"));
        Assert.Equal(2, b.Watchers.Count);
        Assert.Null(b.Get(WatcherBoard.TurnId("codex", "s1")));
        Assert.False(b.ClearTurns("codex"));
    }

    [Fact]
    public void Demo_mode_hides_turn_cards()
    {
        var b = new WatcherBoard();
        b.ApplyTurn(E(AgentTurnKind.Started), "Codex · alpha", "Codex", Now);
        Assert.Empty(WatcherBoard.Shown(b.Ordered(), demo: true));
        Assert.Single(WatcherBoard.Shown(b.Ordered(), demo: false));
    }

    [Fact]
    public void The_other_sources_keep_their_windows_and_fade()
    {
        var b = new WatcherBoard();
        b.Apply(new WatcherCommand.Start("x", "PR 140", null, null, false), Now);
        b.Apply(new WatcherCommand.End("x", WatcherState.Passed, null), Now.AddSeconds(10));
        Assert.Equal(Now.AddSeconds(10) + WatcherBoard.FadeAfter, b.NextChange());
        Assert.Equal(WatcherBoard.AgentWindow, WatcherBoard.WindowOf(new Watcher
        {
            Id = "a:y", Source = WatcherSource.Agent, Title = "t", State = WatcherState.Running, Started = Now, Touched = Now,
        }));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~WatcherTurnCardsTests"`
Expected: build error, `WatcherSource.Turn` and `ApplyTurn` do not exist.

- [ ] **Step 3: Add the source and the folder**

In `Watchers.cs`, add a member to `WatcherSource` after `Runner`:

```csharp
    /// <summary>An agent's turn, read by the app from the agent's own files (5.0 spec 4). Not <see cref="Automatic"/>,
    /// which is Claude Code's background tasks from the Stop hook.</summary>
    Turn,
```

In the `Watcher` record, after the `Kind` property, add:

```csharp
    /// <summary>A turn card's project folder, kept so its title can be rebuilt when streamer mode changes.
    /// A turn card keeps its agent's id in <see cref="Kind"/> and the agent's session in <see cref="Session"/>.</summary>
    public string? Folder { get; init; }
```

- [ ] **Step 4: Add the board members**

In `WatcherBoard`, after `FadeAfter`:

```csharp
    /// <summary>A turn card hears its end from the agent's own log, so it waits an hour before losing touch.</summary>
    public static readonly TimeSpan TurnWindow = TimeSpan.FromHours(1);

    /// <summary>The Antigravity CLI writes no end for a turn that errors, so its card loses touch sooner.</summary>
    public static readonly TimeSpan AntigravityTurnWindow = TimeSpan.FromMinutes(10);

    /// <summary>A finished turn stays long enough to be seen on coming back to the desktop.</summary>
    public static readonly TimeSpan TurnFadeAfter = TimeSpan.FromSeconds(60);

    public static string TurnId(string agent, string session) => $"t:{agent}:{session}";

    /// <summary>How long this watcher may go unheard before it loses touch; null when it never does.</summary>
    public static TimeSpan? WindowOf(Watcher w) => w.Source == WatcherSource.Turn
        ? (w.Kind == AgentSources.Antigravity ? AntigravityTurnWindow : TurnWindow)
        : Window(w.Source);

    /// <summary>How long this watcher shows after it passed or finished.</summary>
    public static TimeSpan FadeOf(Watcher w) => w.Source == WatcherSource.Turn ? TurnFadeAfter : FadeAfter;
```

After `ApplyStop`, add:

```csharp
    /// <summary>An agent's turn moved (5.0 spec 4). Started puts up a running card; waiting marks it; finished and stopped
    /// end it, showing the turn's own length. A card that is not there is created, so a turn already running when Sanduhr
    /// started still shows its end. True when the board changed.</summary>
    public bool ApplyTurn(AgentTurnEvent e, string title, string shortTitle, DateTimeOffset now)
    {
        var before = _watchers.ToList();
        var id = TurnId(e.Agent, e.Session);
        var i = _watchers.FindIndex(w => w.Id == id);
        var old = i >= 0 ? _watchers[i] : null;

        Watcher card = e.Kind switch
        {
            AgentTurnKind.Started => Turn(id, e, title, shortTitle, WatcherState.Running, now, now, null, null),
            AgentTurnKind.Waiting => Turn(id, e, title, shortTitle, WatcherState.Waiting, old?.Started ?? now, now, null, null) with
            {
                Folder = e.Folder ?? old?.Folder,
            },
            _ => Turn(id, e, title, shortTitle, WatcherState.Finished,
                e.Duration is { } d ? now - d : old?.Started ?? now, now, now,
                e.Kind == AgentTurnKind.Stopped ? "stopped" : null),
        };
        if (i >= 0) _watchers[i] = card;
        else _watchers.Add(card);
        Trim();
        return !_watchers.SequenceEqual(before);
    }

    private static Watcher Turn(string id, AgentTurnEvent e, string title, string shortTitle, WatcherState state,
        DateTimeOffset started, DateTimeOffset touched, DateTimeOffset? ended, string? note) => new()
    {
        Id = id, Source = WatcherSource.Turn, Title = title, Short = shortTitle, Note = note, State = state,
        Started = started, Touched = touched, Ended = ended, Session = e.Session, Kind = e.Agent, Folder = e.Folder,
    };

    /// <summary>Rebuilds each turn card's title (streamer mode changed). True when one changed.</summary>
    public bool RetitleTurns(Func<Watcher, (string Title, string Short)> name)
    {
        bool changed = false;
        for (var i = 0; i < _watchers.Count; i++)
        {
            var w = _watchers[i];
            if (w.Source != WatcherSource.Turn) continue;
            var (title, shortTitle) = name(w);
            if (title == w.Title && shortTitle == w.Short) continue;
            _watchers[i] = w with { Title = title, Short = shortTitle };
            changed = true;
        }
        return changed;
    }

    /// <summary>Drops one agent's turn cards (its source or its notifications went off). True when a card went.</summary>
    public bool ClearTurns(string agent) =>
        _watchers.RemoveAll(w => w.Source == WatcherSource.Turn && w.Kind == agent) > 0;
```

- [ ] **Step 5: Make the clock per card**

In `Tick`, replace the fade line and the window test:

```csharp
        _watchers.RemoveAll(w => w.State.Fades() && w.Ended is { } ended && now - ended >= FadeOf(w));
        for (var i = 0; i < _watchers.Count; i++)
        {
            var w = _watchers[i];
            if (w.State is WatcherState.Running or WatcherState.Waiting
                && WindowOf(w) is { } window && now - w.Touched > window)
                _watchers[i] = w with { State = WatcherState.LostTouch };
        }
```

In `NextChange`, replace the two lines that pick `at`:

```csharp
            if (w.State.Fades() && w.Ended is { } ended) at = ended + FadeOf(w);
            else if (w.State is WatcherState.Running or WatcherState.Waiting && WindowOf(w) is { } window)
                at = w.Touched + window + TimeSpan.FromSeconds(1);
```

In `Shown`, hide turn cards in demo mode too (they name a project):

```csharp
    public static IReadOnlyList<Watcher> Shown(IReadOnlyList<Watcher> ordered, bool demo) =>
        demo ? ordered.Where(w => !w.Work && w.Source != WatcherSource.Runner && w.Source != WatcherSource.Turn).ToList() : ordered;
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~Watcher"`
Expected: PASS, the 13 new tests and every existing watcher test.

- [ ] **Step 7: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/Watchers.cs windows-dotnet/tests/Sanduhr.Tests/WatcherTurnCardsTests.cs
git commit -m "feat(windows): turn cards on the watcher board"
```

---

### Task 7: The Claude Code hook writes the event, the folder and the session

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/ClaudeCodeIntegrations.cs` (constants near line 99, `HookEvents` at line 104, `HookCommand` at line 367, `IsLegacyHookGroup` at line 374, `HookGroup` at line 386, `IsOurHookCommand` at line 404, `IsOurHookGroup` at line 418)
- Modify: the tests that build hook JSON: `tests/Sanduhr.Tests/ClaudeCodeIntegrationsTests.cs`, `tests/Sanduhr.Tests/ExternalDirMigrationTests.cs`, `tests/Sanduhr.App.Tests/ModsAndCardsPagesTests.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/ResetPlan.cs:179`
- Test: `windows-dotnet/tests/Sanduhr.Tests/ClaudeCodeHookSignalTests.cs` (new)

**Interfaces:**
- Consumes: `ClaudeCodeSignalFile.Prompt`, `Waiting`, `Done`, `Pattern` (Task 4).
- Produces: `ClaudeCodeIntegrations.HookCommand(string word)` (the new command, `word` one of `prompt`, `waiting`, `done`); `ClaudeCodeIntegrations.GlowHookCommand(string signal)` (the 3.5 to 4.9 command, byte for byte what `HookCommand` returned before); three hook events: `UserPromptSubmit`, `Notification`, `Stop`.

- [ ] **Step 1: Verify first, with the owner (the controller does this, not a subagent)**

The spec's first check: does an async PowerShell hook on Windows receive `session_id` and `cwd` on standard input? Ask the owner to run it once; it needs their Claude Code sign-in.

Write `%TEMP%\sanduhr-hook-check\.claude\settings.json`:

```json
{
  "hooks": {
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "shell": "powershell", "async": true, "timeout": 5,
      "command": "$i = $null; try { $i = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { }; @{ 'event' = 'prompt'; 'has_cwd' = [bool]$i.cwd; 'has_session' = [bool]$i.session_id } | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $env:TEMP 'sanduhr-hook-check\\seen.jsonl')" }] }],
    "Stop": [{ "hooks": [{ "type": "command", "shell": "powershell", "async": true, "timeout": 5,
      "command": "$i = $null; try { $i = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { }; @{ 'event' = 'done'; 'has_cwd' = [bool]$i.cwd; 'has_session' = [bool]$i.session_id } | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $env:TEMP 'sanduhr-hook-check\\seen.jsonl')" }] }]
  }
}
```

The owner opens a terminal in `%TEMP%\sanduhr-hook-check`, runs `claude`, trusts the folder, sends `Reply with the word ok.`, waits for the reply and exits. Then read `%TEMP%\sanduhr-hook-check\seen.jsonl`.

Expected: two lines, `prompt` and `done`, each with `has_cwd` and `has_session` true. The file records booleans only, never a path or an id.

**Gate.** Both true: carry on with this task. Either false or no lines: stop, record the result in the ledger, skip the rest of this task (the hook stays as it is in 4.9) and tell the owner. Tasks 8 to 12 still stand: Claude Code keeps today's glow and gets no toast or card, as the spec's fallback says. Delete `%TEMP%\sanduhr-hook-check` either way.

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeCodeHookSignalTests
{
    private static JsonObject Group(string command, string? matcher = null)
    {
        var g = new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["shell"] = "powershell", ["command"] = command }),
        };
        if (matcher is not null) g["matcher"] = matcher;
        return g;
    }

    [Theory]
    [InlineData("prompt")]
    [InlineData("waiting")]
    [InlineData("done")]
    public void The_command_writes_one_file_per_event_naming_the_event_folder_and_session(string word)
    {
        var cmd = ClaudeCodeIntegrations.HookCommand(word);
        Assert.Contains("'.sanduhr'", cmd);
        Assert.Contains("Test-Path -LiteralPath $d", cmd);            // does nothing when Sanduhr's folder is not there
        Assert.Contains($"'event' = '{word}'", cmd);
        Assert.Contains("[string]$i.cwd", cmd);
        Assert.Contains("[string]$i.session_id", cmd);
        Assert.Contains("'" + ClaudeCodeSignalFile.Prefix + "'", cmd);
        Assert.Contains("$PID", cmd);                                 // two sessions in one millisecond still get two names
        Assert.Contains(".tmp", cmd);                                 // written, then renamed: never read half-written
        Assert.Contains("Move-Item", cmd);
    }

    [Fact]
    public void The_command_reads_nothing_of_the_conversation()
    {
        foreach (var word in new[] { "prompt", "waiting", "done" })
        {
            var cmd = ClaudeCodeIntegrations.HookCommand(word);
            foreach (var field in new[] { "prompt", "transcript_path", "message", "tool_input", "tool_response", "last_assistant_message" })
                Assert.DoesNotContain("$i." + field, cmd);
        }
    }

    [Fact]
    public void The_older_command_is_unchanged_byte_for_byte() =>
        Assert.Equal(
            "$d = Join-Path $env:USERPROFILE '.sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d 'glow-done.signal'), '') }",
            ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.DoneSignal));

    [Fact]
    public void Each_generation_of_the_command_is_recognised_as_Sanduhrs()
    {
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("prompt"))));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("done"))));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("waiting"), ClaudeCodeIntegrations.WaitingMatcher)));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.DoneSignal))));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(
            Group(ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.WaitingSignal), ClaudeCodeIntegrations.WaitingMatcher)));
        Assert.True(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.LegacyHookCommand(ClaudeCodeIntegrations.DoneSignal))));
    }

    [Fact]
    public void A_near_copy_is_the_persons_own()
    {
        Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("done") + " ")));
        Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("done"), "something")));
        Assert.False(ClaudeCodeIntegrations.IsOurHookGroup(Group(ClaudeCodeIntegrations.HookCommand("waiting"))));   // no matcher
    }

    [Fact]
    public void The_new_command_reads_as_Sanduhrs_command() =>
        Assert.True(ClaudeCodeIntegrations.IsOurHookCommand(new JsonObject
        {
            ["type"] = "command", ["command"] = ClaudeCodeIntegrations.HookCommand("done"),
        }));
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeCodeHookSignalTests"`
Expected: build error, `GlowHookCommand` does not exist.

- [ ] **Step 4: Change the hook table and the command**

In `ClaudeCodeIntegrations.cs`, replace the `HookEvents` line:

```csharp
    /// <summary>The three hook events and the word each writes into its signal file (5.0 spec 4). UserPromptSubmit is new in
    /// 5.0: it gives the turn's start, so a finished turn has a length.</summary>
    private static readonly (string Name, string Word)[] HookEvents =
    [
        ("UserPromptSubmit", ClaudeCodeSignalFile.Prompt), ("Notification", ClaudeCodeSignalFile.Waiting), ("Stop", ClaudeCodeSignalFile.Done),
    ];

    /// <summary>The empty signal file an event's older command touched (3.5 to 4.9, and the pre-3.5 one); null for an
    /// event that had no older command.</summary>
    private static string? OldSignal(string eventName) => eventName switch
    {
        "Notification" => WaitingSignal,
        "Stop" => DoneSignal,
        _ => null,
    };
```

Replace `HookCommand` and add `GlowHookCommand` above `LegacyHookCommand`:

```csharp
    /// <summary>
    /// The 5.0 command: reads the JSON Claude Code passes on standard input and writes one small file per event under
    /// <c>%USERPROFILE%\.sanduhr</c> (only if that folder exists), named <c>cc-signal-&lt;unix ms&gt;-&lt;pid&gt;.json</c>,
    /// holding the event, the session's folder and its id. Written under a temporary name and renamed, so Sanduhr never
    /// reads half a file. It takes <c>cwd</c> and <c>session_id</c> from the input and nothing else.
    /// </summary>
    public static string HookCommand(string word)
        => "$d = Join-Path $env:USERPROFILE '.sanduhr'; if (Test-Path -LiteralPath $d) { $i = $null; "
           + "try { $i = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { }; "
           + "$at = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); "
           + "$j = @{ 'v' = 1; 'event' = '" + word + "'; 'cwd' = [string]$i.cwd; 'session' = [string]$i.session_id; 'at' = $at } | ConvertTo-Json -Compress; "
           + "$f = Join-Path $d ('" + ClaudeCodeSignalFile.Prefix + "' + $at + '-' + $PID + '.json'); "
           + "[IO.File]::WriteAllText($f + '.tmp', $j); Move-Item -LiteralPath ($f + '.tmp') -Destination $f -Force }";

    /// <summary>The command 3.5.0 to 4.9.0 wrote (touches an empty <c>%USERPROFILE%\.sanduhr\&lt;signal&gt;</c>). Still
    /// recognised as Sanduhr's: Status reads it Outdated, Install replaces it, Remove takes it out, and until then it
    /// keeps driving the glow as it did.</summary>
    public static string GlowHookCommand(string signal)
        => "$d = Join-Path $env:USERPROFILE '.sanduhr'; if (Test-Path -LiteralPath $d) { [IO.File]::WriteAllText((Join-Path $d '" + signal + "'), '') }";
```

In `IsLegacyHookGroup`, the last line becomes:

```csharp
        return new[] { WaitingSignal, DoneSignal }.Any(s => string.Equals(cmd, LegacyHookCommand(s), StringComparison.Ordinal));
```

In `HookGroup`, the first line and the command pair become:

```csharp
        var word = HookEvents.First(e => e.Name == eventName).Word;
```
```csharp
            new JsonEdit.Pair("command", JsonEdit.Value.Str(HookCommand(word))),
```

`IsOurHookCommand` becomes:

```csharp
    public static bool IsOurHookCommand(JsonNode? hook)
    {
        if (hook is not JsonObject o || !(Str(o["type"]) == "command") || Str(o["command"]) is not { } cmd) return false;
        return (cmd.Contains(WaitingSignal, StringComparison.Ordinal) || cmd.Contains(DoneSignal, StringComparison.Ordinal)
                || cmd.Contains(ClaudeCodeSignalFile.Prefix, StringComparison.Ordinal))
            && cmd.Contains("Sanduhr", StringComparison.OrdinalIgnoreCase);
    }
```

In `IsOurHookGroup`, the loop becomes:

```csharp
        foreach (var (name, word) in HookEvents)
        {
            bool ours = string.Equals(cmd, HookCommand(word), StringComparison.Ordinal)
                || (OldSignal(name) is { } signal
                    && (string.Equals(cmd, GlowHookCommand(signal), StringComparison.Ordinal)
                        || string.Equals(cmd, LegacyHookCommand(signal), StringComparison.Ordinal)));
            if (!ours) continue;
            var matcher = o["matcher"];
            return name == "Notification" ? Str(matcher) == WaitingMatcher : matcher is null;
        }
```

Its summary comment gains: "A group carrying the 3.5 to 4.9 command or the pre-3.5 one is still Sanduhr's, and reads Outdated."

Every other use of `HookEvents` in the file reads `e.Name` or deconstructs `(name, _)` and needs no change. Build to confirm: `cd windows-dotnet && dotnet build src/Sanduhr.Core`.

- [ ] **Step 5: Bring the existing hook tests along**

Eight existing uses of `HookCommand(` sit in `ClaudeCodeIntegrationsTests.cs` (4), `ExternalDirMigrationTests.cs` (3) and `ModsAndCardsPagesTests.cs` (1). For each, read what the test sets up:

- It builds what a **current** install writes or expects to find after Install: keep `HookCommand`, and pass the word (`ClaudeCodeSignalFile.Waiting` for Notification, `ClaudeCodeSignalFile.Done` for Stop). Where it lists the events an install creates, add `UserPromptSubmit` with `ClaudeCodeSignalFile.Prompt`.
- It builds a file **as an older Sanduhr left it** (a 3.5 to 4.9 install to update, repoint or remove): change the call to `GlowHookCommand` with the same signal constant.

Then add these to `ClaudeCodeIntegrationsTests.cs`. They use that class's own helpers (`Sut()`, `Folder()`, `Put(folder, text)`, `Json(folder)`, `Bytes(folder)`, `B(text)`, `Quote(text)`, `Ok(result)`):

```csharp
    private static readonly (string Event, string Word)[] HookWords =
        [("UserPromptSubmit", "prompt"), ("Notification", "waiting"), ("Stop", "done")];

    /// <summary>A settings.json as 4.9 left it: Sanduhr's two groups with the empty-file command, beside a Stop hook of the person's own.</summary>
    private static string SettingsFrom49() =>
        "{\"hooks\":{"
        + "\"Notification\":[{\"matcher\":" + Quote(ClaudeCodeIntegrations.WaitingMatcher) + ",\"hooks\":[{\"type\":\"command\",\"shell\":\"powershell\",\"command\":"
        + Quote(ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.WaitingSignal)) + ",\"async\":true,\"timeout\":5}]}],"
        + "\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo mine\"}]},{\"hooks\":[{\"type\":\"command\",\"shell\":\"powershell\",\"command\":"
        + Quote(ClaudeCodeIntegrations.GlowHookCommand(ClaudeCodeIntegrations.DoneSignal)) + ",\"async\":true,\"timeout\":5}]}]}}";

    private static List<JsonObject> OurGroups(JsonObject root, string ev) =>
        root["hooks"]?[ev] is JsonArray list ? [.. list.Where(ClaudeCodeIntegrations.IsOurHookGroup).Select(g => (JsonObject)g!)] : [];

    private static bool HasThePersonsStopHook(JsonObject root) =>
        root["hooks"]?["Stop"] is JsonArray list && list.Any(g => (string?)g?["hooks"]?[0]?["command"] == "echo mine");

    [Fact]
    public void A_4_9_hook_install_reads_outdated()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, SettingsFrom49());
        Assert.Equal(IntegrationStatus.Outdated, sut.Status(IntegrationKind.Hooks, folder));
    }

    [Fact]
    public void Updating_a_4_9_hook_install_writes_all_three_events_and_keeps_the_persons_hook()
    {
        var sut = Sut();
        var folder = Folder();
        Put(folder, SettingsFrom49());

        Ok(sut.Install(IntegrationKind.Hooks, folder));
        var root = Json(folder);
        foreach (var (ev, word) in HookWords)
        {
            var mine = Assert.Single(OurGroups(root, ev));
            Assert.Equal(ClaudeCodeIntegrations.HookCommand(word), (string?)mine["hooks"]![0]!["command"]);
            Assert.True((bool)mine["hooks"]![0]!["async"]!);
        }
        Assert.True(HasThePersonsStopHook(root));
        Assert.Equal(IntegrationStatus.Installed, sut.Status(IntegrationKind.Hooks, folder));

        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        var after = Json(folder);
        foreach (var (ev, _) in HookWords) Assert.Empty(OurGroups(after, ev));
        Assert.True(HasThePersonsStopHook(after));
        Assert.Equal(IntegrationStatus.NotInstalled, sut.Status(IntegrationKind.Hooks, folder));
    }

    [Fact]
    public void A_fresh_hook_install_writes_three_events_and_remove_restores_the_file_byte_for_byte()
    {
        var sut = Sut();
        var folder = Folder();
        const string original = "{\n  \"model\": \"x\"\n}\n";
        Put(folder, original);

        Ok(sut.Install(IntegrationKind.Hooks, folder));
        var root = Json(folder);
        foreach (var (ev, word) in HookWords)
            Assert.Equal(ClaudeCodeIntegrations.HookCommand(word), (string?)Assert.Single(OurGroups(root, ev))["hooks"]![0]!["command"]);

        Ok(sut.Remove(IntegrationKind.Hooks, folder));
        Assert.Equal(B(original), Bytes(folder));
    }
```

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeCodeIntegrations|FullyQualifiedName~ExternalDirMigration|FullyQualifiedName~ClaudeCodeHookSignal"`
Expected: PASS.

- [ ] **Step 6: Name the signal files in Reset**

In `ResetPlan.cs`, after the line `AddMatchingFiles(history, dir, "glow-*.signal");` add:

```csharp
        AddMatchingFiles(history, dir, ClaudeCodeSignalFile.Pattern);
```

Find the test that pins this list (`grep -rn "glow-\*.signal" windows-dotnet/tests`) and add a `cc-signal-1-2.json` file to its fixture and its expected removals, beside the `glow-*.signal` case.

- [ ] **Step 7: Update the words that describe the hook**

Run `grep -rn "Two hooks that light" windows-dotnet/src` and replace the sentence, wherever it is defined, with:

```
Three hooks that tell Sanduhr when Claude Code starts a turn, waits on you or finishes.
```

Run `grep -rn "signal" windows-dotnet/src/Sanduhr.App/Views windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/IntegrationConsent*.cs` and, in any sentence shown to the person that says the hook only touches an empty file, say instead:

```
Each hook writes a small file under %USERPROFILE%\.sanduhr naming the event, the folder and the session. Sanduhr deletes it as it reads it.
```

Update the tests that pin either sentence.

- [ ] **Step 8: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core windows-dotnet/tests
git commit -m "feat(windows): the Claude Code hook writes the event, the folder and the session"
```

---

### Task 8: The activity watcher

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentActivityWatcher.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/AgentActivityWatcherTests.cs`

**Interfaces:**
- Consumes: `CodexTurnReader` (Task 2), `AntigravityTurnReader` (Task 3), `ClaudeCodeSignalFile`, `ClaudeCodeTurns` (Task 4), `CodexBurnSource.SessionIdOf(string fileName)`.
- Produces: `sealed class AgentActivityWatcher : IDisposable`
  - constructor `(Func<IReadOnlyList<string>> codexHomes, Func<string?> antigravityHome, Func<string, string?> antigravityFolder, Func<bool> claudeOn, string signalDir, Func<DateTimeOffset>? clock = null, Action<string>? log = null)`. `codexHomes` returns an empty list while Codex is off; `antigravityHome` returns null while Antigravity is off; `antigravityFolder` maps a conversation id to its project folder.
  - `event Action<AgentTurnEvent>? Turn` and `event Action<string>? AgentOff` (an agent's source went off: its cards should go). Both are raised on a worker thread.
  - `void Start()`, `void Refresh()` (the sources changed: watch the folders that are on now and scan soon), `void Scan()` (one synchronous pass; the timers call it, and so do tests), `void Dispose()`
  - constants `PollEvery` (30 s), `Debounce` (300 ms), `PrimeWindow` (10 min), `PrimeTailBytes` (4 MB), `MaxLineBytes` (1 MB)

How it decides what is history: the first pass over an agent reads its files for state and raises nothing. After that, a file created since that first pass is read from its start and raises events; a file that already existed but is seen late is history too. When an agent goes off its state is dropped, and coming back on is a first pass again.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public sealed class AgentActivityWatcherTests : IDisposable
{
    private readonly TempDir _t = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow.AddSeconds(-2);   // a little behind the file system, so new files are newer
    private bool _codexOn = true, _antigravityOn = true, _claudeOn = true;
    private readonly List<AgentTurnEvent> _events = [];
    private readonly List<string> _off = [];

    private string CodexHome => Path.Combine(_t.Path, "codex");
    private string Sessions => Path.Combine(CodexHome, "sessions", "2026", "11", "17");
    private string AgyHome => Path.Combine(_t.Path, "agy");
    private string Signals => Path.Combine(_t.Path, "signals");

    public AgentActivityWatcherTests()
    {
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Path.Combine(AgyHome, "brain"));
        Directory.CreateDirectory(Signals);
    }

    public void Dispose() => _t.Dispose();

    private AgentActivityWatcher Sut()
    {
        var w = new AgentActivityWatcher(
            () => _codexOn ? [CodexHome] : [],
            () => _antigravityOn ? AgyHome : null,
            conversation => @"C:\proj\" + conversation,
            () => _claudeOn, Signals, () => _now);
        w.Turn += _events.Add;
        w.AgentOff += _off.Add;
        return w;
    }

    private static string Iso(DateTimeOffset t) => t.ToString("o");

    private static string CodexLine(DateTimeOffset at, string type, Dictionary<string, object?> payload) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["timestamp"] = Iso(at), ["type"] = type, ["payload"] = payload }) + "\n";

    private string Meta(string? parent = null) =>
        CodexLine(_now, "session_meta", new() { ["id"] = "s", ["cwd"] = @"C:\proj\alpha", ["parent_thread_id"] = parent });

    private string Started(DateTimeOffset at) => CodexLine(at, "event_msg", new() { ["type"] = "task_started", ["turn_id"] = "t" });

    private string Complete(DateTimeOffset at, int ms = 124000) =>
        CodexLine(at, "event_msg", new() { ["type"] = "task_complete", ["turn_id"] = "t", ["duration_ms"] = ms });

    private string Rollout(string name, string text, bool newFile = false)
    {
        var path = Path.Combine(Sessions, name + ".jsonl");
        File.AppendAllText(path, text);
        if (newFile) File.SetCreationTimeUtc(path, _now.UtcDateTime.AddSeconds(5));
        return path;
    }

    private static string Step(DateTimeOffset at, string type, bool toolCalls = false)
    {
        var o = new Dictionary<string, object?> { ["type"] = type, ["status"] = "DONE", ["created_at"] = Iso(at) };
        if (toolCalls) o["tool_calls"] = new[] { 1 };
        return JsonSerializer.Serialize(o) + "\n";
    }

    private string Transcript(string conversation, string text, bool newFile = false)
    {
        var dir = Path.Combine(AgyHome, "brain", conversation, ".system_generated", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "transcript.jsonl");
        File.AppendAllText(path, text);
        if (newFile) File.SetCreationTimeUtc(path, _now.UtcDateTime.AddSeconds(5));
        return path;
    }

    private void Signal(string ev, string session, long? atMs = null, int pid = 100, string? raw = null)
    {
        long at = atMs ?? _now.ToUnixTimeMilliseconds();
        var json = raw ?? JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1, ["event"] = ev, ["cwd"] = @"C:\proj\alpha", ["session"] = session, ["at"] = at,
        });
        File.WriteAllText(Path.Combine(Signals, $"cc-signal-{at}-{pid}.json"), json);
    }

    // ---- Codex ----

    [Fact]
    public void History_is_not_replayed_and_new_lines_are_events()
    {
        Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-9)) + Complete(_now.AddMinutes(-7)));
        using var w = Sut();
        w.Scan();
        Assert.Empty(_events);

        Rollout("rollout-a", Started(_now) + Complete(_now.AddSeconds(124)));
        w.Scan();
        Assert.Equal([AgentTurnKind.Started, AgentTurnKind.Finished], _events.Select(e => e.Kind));
        Assert.All(_events, e => Assert.Equal(("codex", "rollout-a", @"C:\proj\alpha"), (e.Agent, e.Session, e.Folder)));
        Assert.Equal(TimeSpan.FromSeconds(124), _events[1].Duration);
    }

    [Fact]
    public void A_turn_already_running_at_launch_still_finishes()
    {
        Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-3)));
        using var w = Sut();
        w.Scan();
        Rollout("rollout-a", Complete(_now));
        w.Scan();
        Assert.Equal(AgentTurnKind.Finished, Assert.Single(_events).Kind);
    }

    [Fact]
    public void A_rollout_created_after_launch_is_read_from_its_start()
    {
        using var w = Sut();
        w.Scan();
        Rollout("rollout-new", Meta() + Started(_now) + Complete(_now.AddSeconds(124)), newFile: true);
        w.Scan();
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public void An_old_rollout_first_seen_later_is_history()
    {
        using var w = Sut();
        w.Scan();
        var path = Rollout("rollout-old", Meta() + Started(_now) + Complete(_now.AddSeconds(124)));
        File.SetCreationTimeUtc(path, _now.UtcDateTime.AddDays(-2));
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void A_sub_agents_rollout_is_silent_even_past_the_priming_tail()
    {
        var filler = CodexLine(_now, "response_item", new() { ["type"] = "message", ["content"] = new string('x', 4000) });
        var sb = new StringBuilder(Meta(parent: "parent-thread"));
        while (sb.Length <= AgentActivityWatcher.PrimeTailBytes + 8000) sb.Append(filler);
        Rollout("rollout-child", sb.ToString() + Started(_now.AddMinutes(-1)));
        using var w = Sut();
        w.Scan();
        Rollout("rollout-child", Complete(_now));
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void A_line_written_in_two_halves_is_read_once_it_is_whole()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.Scan();
        var line = Started(_now);
        Rollout("rollout-a", line[..20]);
        w.Scan();
        Assert.Empty(_events);
        Rollout("rollout-a", line[20..]);
        w.Scan();
        Assert.Equal(AgentTurnKind.Started, Assert.Single(_events).Kind);
    }

    [Fact]
    public void A_line_over_a_megabyte_is_skipped_and_the_next_is_read()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.Scan();
        Rollout("rollout-a", CodexLine(_now, "response_item", new() { ["type"] = "message", ["content"] = new string('x', AgentActivityWatcher.MaxLineBytes + 10) }) + Started(_now));
        w.Scan();
        Assert.Equal(AgentTurnKind.Started, Assert.Single(_events).Kind);
    }

    /// <summary>Review focus: a rollout that shrinks or disappears mid-turn invents no ending.</summary>
    [Fact]
    public void A_rollout_that_shrinks_or_disappears_mid_turn_raises_nothing()
    {
        var path = Rollout("rollout-a", Meta() + Started(_now.AddMinutes(-1)));
        using var w = Sut();
        w.Scan();

        File.WriteAllText(path, Meta());            // rewritten shorter
        w.Scan();
        Assert.Empty(_events);

        Rollout("rollout-a", Started(_now) + Complete(_now.AddSeconds(124)));   // it carries on from its new end
        w.Scan();
        Assert.Equal(2, _events.Count);

        _events.Clear();
        Rollout("rollout-a", Started(_now.AddMinutes(5)));
        w.Scan();
        File.Delete(path);                          // compressed away mid-turn
        w.Scan();
        Assert.Equal(AgentTurnKind.Started, Assert.Single(_events).Kind);
    }

    /// <summary>Review focus: a source switched off mid-turn; switching it back on replays nothing.</summary>
    [Fact]
    public void A_source_switched_off_goes_quiet_and_coming_back_replays_nothing()
    {
        Rollout("rollout-a", Meta());
        using var w = Sut();
        w.Scan();
        Rollout("rollout-a", Started(_now));
        w.Scan();
        Assert.Single(_events);

        _codexOn = false;
        w.Scan();
        w.Scan();
        Assert.Equal(["codex"], _off);              // said once
        Rollout("rollout-a", Complete(_now.AddSeconds(124)) + Started(_now.AddMinutes(4)));
        w.Scan();
        Assert.Single(_events);                     // nothing while off

        _codexOn = true;
        w.Scan();
        Assert.Single(_events);                     // the first pass back is history
        Rollout("rollout-a", Complete(_now.AddMinutes(6)));
        w.Scan();
        Assert.Equal(AgentTurnKind.Finished, _events[^1].Kind);
    }

    // ---- Antigravity ----

    [Fact]
    public void An_antigravity_turn_starts_and_finishes_ninety_seconds_after_its_last_step()
    {
        using var w = Sut();
        w.Scan();
        Transcript("conv-1", Step(_now, "USER_INPUT") + Step(_now.AddSeconds(5), "PLANNER_RESPONSE", toolCalls: true)
            + Step(_now.AddSeconds(200), "PLANNER_RESPONSE"), newFile: true);
        w.Scan();
        var started = Assert.Single(_events);
        Assert.Equal((AgentTurnKind.Started, "antigravity", "conv-1", @"C:\proj\conv-1"), (started.Kind, started.Agent, started.Session, started.Folder));

        _now = _now.AddSeconds(89);
        w.Scan();
        Assert.Single(_events);

        _now = _now.AddSeconds(1);
        w.Scan();
        Assert.Equal(AgentTurnKind.Finished, _events[^1].Kind);
        Assert.Equal(TimeSpan.FromSeconds(200), _events[^1].Duration);
        Assert.Equal(@"C:\proj\conv-1", _events[^1].Folder);
    }

    [Fact]
    public void An_antigravity_conversation_that_ended_before_launch_raises_nothing_later()
    {
        Transcript("conv-1", Step(_now.AddMinutes(-2), "USER_INPUT") + Step(_now.AddMinutes(-1), "PLANNER_RESPONSE"));
        using var w = Sut();
        w.Scan();
        _now = _now.AddMinutes(5);
        w.Scan();
        Assert.Empty(_events);
    }

    [Fact]
    public void With_antigravity_off_its_files_are_not_read()
    {
        _antigravityOn = false;
        using var w = Sut();
        w.Scan();
        Transcript("conv-1", Step(_now, "USER_INPUT"), newFile: true);
        w.Scan();
        Assert.Empty(_events);
        Assert.Empty(_off);                         // it was never on
    }

    // ---- Claude Code ----

    /// <summary>Review focus: two sessions end in the same millisecond.</summary>
    [Fact]
    public void Two_sessions_signals_in_the_same_millisecond_both_arrive_and_the_files_go()
    {
        using var w = Sut();
        long at = _now.ToUnixTimeMilliseconds();
        Signal("done", "sess-a", at, pid: 100);
        Signal("done", "sess-b", at, pid: 200);
        w.Scan();
        Assert.Equal(["sess-a", "sess-b"], _events.Select(e => e.Session).Order());
        Assert.All(_events, e => Assert.Equal(("claude", AgentTurnKind.Finished), (e.Agent, e.Kind)));
        Assert.Empty(Directory.GetFiles(Signals));
    }

    [Fact]
    public void A_prompt_then_a_done_gives_the_turns_length()
    {
        using var w = Sut();
        Signal("prompt", "sess-a", _now.ToUnixTimeMilliseconds());
        w.Scan();
        _now = _now.AddSeconds(252);
        Signal("done", "sess-a", _now.ToUnixTimeMilliseconds());
        w.Scan();
        Assert.Equal(TimeSpan.FromSeconds(252), _events[^1].Duration);
    }

    [Fact]
    public void A_stale_malformed_or_oversized_signal_is_deleted_unread()
    {
        using var w = Sut();
        Signal("done", "old", _now.AddMinutes(-11).ToUnixTimeMilliseconds());
        Signal("done", "bad", pid: 300, raw: "not json");
        Signal("done", "big", pid: 400, raw: new string('x', ClaudeCodeSignalFile.MaxBytes + 1));
        w.Scan();
        Assert.Empty(_events);
        Assert.Empty(Directory.GetFiles(Signals));
    }

    [Fact]
    public void With_claude_code_off_a_signal_is_deleted_and_raises_nothing()
    {
        using var w = Sut();
        w.Scan();
        _claudeOn = false;
        Signal("done", "sess-a");
        w.Scan();
        Assert.Empty(_events);
        Assert.Empty(Directory.GetFiles(Signals));
        Assert.Equal(["claude"], _off);
    }

    // ---- the service ----

    [Fact]
    public void A_listener_that_throws_does_not_stop_the_others()
    {
        using var w = new AgentActivityWatcher(() => [], () => null, _ => null, () => true, Signals, () => _now);
        w.Turn += _ => throw new InvalidOperationException("boom");
        w.Turn += _events.Add;
        Signal("done", "sess-a");
        w.Scan();
        Assert.Single(_events);
    }

    [Fact]
    public void Missing_folders_are_simply_empty()
    {
        using var w = new AgentActivityWatcher(() => [Path.Combine(_t.Path, "nope")], () => Path.Combine(_t.Path, "nope2"), _ => null,
            () => true, Path.Combine(_t.Path, "nope3"), () => _now);
        w.Scan();
        w.Start();
        w.Refresh();
        Assert.Empty(_events);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentActivityWatcherTests"`
Expected: build error, `AgentActivityWatcher` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Watches the files that say what each agent's turn is doing (5.0 spec 4) and raises one <see cref="AgentTurnEvent"/> per
/// change: Codex's plain rollouts, the Antigravity CLI's transcripts and the signal files Claude Code's hook writes. It
/// tails each file from an offset kept in memory and stores nothing. The first pass over an agent reads for state and
/// raises nothing, so starting Sanduhr never replays old turns. An agent that is off is not read at all.
/// </summary>
public sealed class AgentActivityWatcher : IDisposable
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>At the first pass, a file changed this recently is read (its tail) to learn whether a turn is open.</summary>
    public static readonly TimeSpan PrimeWindow = TimeSpan.FromMinutes(10);

    public const long PrimeTailBytes = 4L * 1024 * 1024;

    /// <summary>A longer line is skipped whole: turn markers are small, and tool output can be huge.</summary>
    public const int MaxLineBytes = 1024 * 1024;

    private readonly Func<IReadOnlyList<string>> _codexHomes;
    private readonly Func<string?> _antigravityHome;
    private readonly Func<string, string?> _antigravityFolder;
    private readonly Func<bool> _claudeOn;
    private readonly string _signalDir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;

    private readonly object _gate = new();
    private readonly Dictionary<string, Tail> _tails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _firstPass = new(StringComparer.Ordinal);   // agent -> when its first pass ran
    private readonly ClaudeCodeTurns _claude = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _claudeWasOn;
    private Timer? _poll;
    private Timer? _soon;
    private Timer? _due;
    private bool _disposed;

    public AgentActivityWatcher(Func<IReadOnlyList<string>> codexHomes, Func<string?> antigravityHome,
        Func<string, string?> antigravityFolder, Func<bool> claudeOn, string signalDir,
        Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _codexHomes = codexHomes;
        _antigravityHome = antigravityHome;
        _antigravityFolder = antigravityFolder;
        _claudeOn = claudeOn;
        _signalDir = signalDir;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
    }

    /// <summary>A turn started, finished, stopped or is waiting. Raised on a worker thread.</summary>
    public event Action<AgentTurnEvent>? Turn;

    /// <summary>An agent's source went off: whatever shows its turns should clear them. Raised on a worker thread.</summary>
    public event Action<string>? AgentOff;

    private sealed class Tail(string agent, string session)
    {
        public readonly string Agent = agent;
        public readonly string Session = session;
        public long Offset;
        public readonly MemoryStream Partial = new();
        public bool Oversized;
        public CodexTurnReader? Codex = agent == AgentSources.Codex ? new CodexTurnReader(session) : null;
        public AntigravityTurnReader? Antigravity = agent == AgentSources.Antigravity ? new AntigravityTurnReader(session) : null;

        public void Reset(long offset)
        {
            Offset = offset;
            Partial.SetLength(0);
            Oversized = false;
            if (Codex is not null) Codex = new CodexTurnReader(Session);
            if (Antigravity is not null) Antigravity = new AntigravityTurnReader(Session);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _poll is not null) return;
            _soon = new Timer(_ => SafeScan(), null, Timeout.Infinite, Timeout.Infinite);
            _due = new Timer(_ => SafeScan(), null, Timeout.Infinite, Timeout.Infinite);
            Watch();
            _poll = new Timer(_ => SafeScan(), null, TimeSpan.Zero, PollEvery);
        }
    }

    /// <summary>The sources changed: watch the folders that are on now, and scan soon.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            if (_disposed || _poll is null) return;
            Unwatch();
            Watch();
            _soon?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Unwatch();
            _poll?.Dispose();
            _soon?.Dispose();
            _due?.Dispose();
            _poll = _soon = _due = null;
        }
    }

    /// <summary>One pass over everything that is on. The timers call it; a test calls it directly.</summary>
    public void Scan()
    {
        var events = new List<AgentTurnEvent>();
        var off = new List<string>();
        lock (_gate)
        {
            if (_disposed) return;
            var now = _clock();
            ScanAgent(AgentSources.Codex, CodexFiles(), now, events, off);
            ScanAgent(AgentSources.Antigravity, AntigravityFiles(), now, events, off);
            ScanSignals(now, events, off);
            ArmDue(now);
        }
        foreach (var agent in off) Raise(() => AgentOff?.Invoke(agent));
        foreach (var e in events)
            foreach (var handler in Turn?.GetInvocationList() ?? [])
                Raise(() => ((Action<AgentTurnEvent>)handler)(e));
    }

    private void SafeScan()
    {
        try { Scan(); }
        catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"agent activity: scan failed ({e.GetType().Name})"); }
    }

    private void Raise(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is not OutOfMemoryException) { _log?.Invoke($"agent activity: a listener failed ({e.GetType().Name})"); }
    }

    // ---- files ----

    /// <summary>Codex's plain rollouts, or null while Codex is off. Compressed rollouts are finished history.</summary>
    private List<(string Path, string Session)>? CodexFiles()
    {
        var homes = _codexHomes();
        if (homes.Count == 0) return null;
        var files = new List<(string, string)>();
        foreach (var home in homes)
        {
            try
            {
                var sessions = new DirectoryInfo(Path.Combine(home, "sessions"));
                if (!sessions.Exists) continue;
                foreach (var fi in sessions.EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories))
                    files.Add((fi.FullName, CodexBurnSource.SessionIdOf(fi.Name)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"agent activity: codex folder not read ({e.GetType().Name})");
            }
        }
        return files;
    }

    /// <summary>The Antigravity CLI's transcripts, or null while Antigravity is off.</summary>
    private List<(string Path, string Session)>? AntigravityFiles()
    {
        if (_antigravityHome() is not { } home) return null;
        var files = new List<(string, string)>();
        try
        {
            var brain = new DirectoryInfo(Path.Combine(home, "brain"));
            if (!brain.Exists) return files;
            foreach (var conversation in brain.EnumerateDirectories())
            {
                var path = Path.Combine(conversation.FullName, ".system_generated", "logs", "transcript.jsonl");
                if (File.Exists(path)) files.Add((path, conversation.Name));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"agent activity: antigravity folder not read ({e.GetType().Name})");
        }
        return files;
    }

    private void ScanAgent(string agent, List<(string Path, string Session)>? files, DateTimeOffset now,
        List<AgentTurnEvent> events, List<string> off)
    {
        if (files is null)
        {
            if (_firstPass.Remove(agent))
            {
                DropTails(t => t.Agent == agent);
                off.Add(agent);
            }
            return;
        }

        bool first = !_firstPass.TryGetValue(agent, out var firstPassAt);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, session) in files)
        {
            seen.Add(path);
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) continue;
                if (!_tails.TryGetValue(path, out var tail))
                {
                    _tails[path] = tail = new Tail(agent, session);
                    // A file that was there before this agent's first pass is history: read it for state, raise nothing.
                    if (first || fi.CreationTimeUtc < firstPassAt.UtcDateTime)
                    {
                        Prime(tail, fi, now);
                        continue;
                    }
                }
                if (fi.Length < tail.Offset) tail.Reset(fi.Length);        // rewritten shorter: carry on from its new end
                else if (fi.Length > tail.Offset) ReadNew(tail, path, now, events);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"agent activity: a file was not read ({e.GetType().Name})");
            }
        }
        // A file that is gone (compressed, deleted) takes its open turn with it: no ending is invented.
        DropTails(t => t.Agent == agent, keep: seen);
        if (first) _firstPass[agent] = now;

        if (agent != AgentSources.Antigravity) return;
        foreach (var tail in _tails.Values)
            if (tail.Antigravity is { DueAt: not null } reader && reader.Due(now, _antigravityFolder(tail.Session)) is { } e)
                events.Add(e);
    }

    private void DropTails(Func<Tail, bool> which, HashSet<string>? keep = null)
    {
        foreach (var key in _tails.Where(kv => which(kv.Value) && keep?.Contains(kv.Key) != true).Select(kv => kv.Key).ToList())
            _tails.Remove(key);
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>History: Codex's first line (the meta that marks a sub-agent), then the tail of a file that changed
    /// lately, with every event dropped. Afterwards the offset is the file's end.</summary>
    private void Prime(Tail tail, FileInfo fi, DateTimeOffset now)
    {
        using var fs = Open(fi.FullName);
        long length = fs.Length;
        if (tail.Codex is not null) ReadLines(tail, fs, Math.Min(length, MaxLineBytes), false, now, null, null, firstLineOnly: true);
        tail.Partial.SetLength(0);
        tail.Oversized = false;

        if (now.UtcDateTime - fi.LastWriteTimeUtc <= PrimeWindow)
        {
            long start = Math.Max(0, length - PrimeTailBytes);
            fs.Seek(start, SeekOrigin.Begin);
            ReadLines(tail, fs, length - start, skipFirstPartial: start > 0, now, null, null);
            tail.Antigravity?.Settle();
        }
        tail.Offset = length;
    }

    private void ReadNew(Tail tail, string path, DateTimeOffset now, List<AgentTurnEvent> events)
    {
        using var fs = Open(path);
        long length = fs.Length;
        if (length <= tail.Offset) return;
        fs.Seek(tail.Offset, SeekOrigin.Begin);
        var folder = tail.Antigravity is null ? null : _antigravityFolder(tail.Session);
        ReadLines(tail, fs, length - tail.Offset, false, now, events, folder);
        tail.Offset = length;
    }

    /// <summary>Feeds every complete line in the next <paramref name="count"/> bytes to the tail's reader. A line with no
    /// newline yet stays in the tail for the next read. <paramref name="sink"/> null drops the events (history).</summary>
    private static void ReadLines(Tail tail, FileStream fs, long count, bool skipFirstPartial, DateTimeOffset now,
        List<AgentTurnEvent>? sink, string? folder, bool firstLineOnly = false)
    {
        var buf = new byte[64 * 1024];
        long left = count;
        bool skipping = skipFirstPartial;
        while (left > 0)
        {
            int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) break;
            left -= n;
            int lineStart = 0;
            for (int i = 0; i < n; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                if (skipping) skipping = false;
                else
                {
                    Collect(tail, buf, lineStart, i - lineStart);
                    EndLine(tail, now, sink, folder);
                    if (firstLineOnly) return;
                }
                lineStart = i + 1;
            }
            if (!skipping) Collect(tail, buf, lineStart, n - lineStart);
        }
    }

    private static void Collect(Tail tail, byte[] buf, int start, int count)
    {
        if (count <= 0 || tail.Oversized) return;
        if (tail.Partial.Length + count > MaxLineBytes)
        {
            tail.Oversized = true;
            tail.Partial.SetLength(0);
            return;
        }
        tail.Partial.Write(buf, start, count);
    }

    private static void EndLine(Tail tail, DateTimeOffset now, List<AgentTurnEvent>? sink, string? folder)
    {
        if (tail.Oversized)
        {
            tail.Oversized = false;
            tail.Antigravity?.Activity();   // still a line: the turn is going on
            return;
        }
        var line = Encoding.UTF8.GetString(tail.Partial.GetBuffer(), 0, (int)tail.Partial.Length).Trim();
        tail.Partial.SetLength(0);
        if (line.Length == 0) return;
        var e = tail.Codex is { } codex ? codex.Read(line) : tail.Antigravity?.Read(line, folder, now);
        if (e is not null) sink?.Add(e);
    }

    // ---- Claude Code's signal files ----

    private void ScanSignals(DateTimeOffset now, List<AgentTurnEvent> events, List<string> off)
    {
        bool on = _claudeOn();
        if (_claudeWasOn && !on) off.Add(AgentSources.Claude);
        _claudeWasOn = on;

        List<string> files;
        try { files = [.. Directory.EnumerateFiles(_signalDir, ClaudeCodeSignalFile.Pattern).OrderBy(f => f, StringComparer.Ordinal)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            byte[]? data = null;
            try
            {
                // Read only while Claude Code's notifications are on and the file is small; delete it either way.
                if (on && new FileInfo(file).Length <= ClaudeCodeSignalFile.MaxBytes) data = File.ReadAllBytes(file);
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (data is not null && ClaudeCodeSignalFile.Decode(data, now) is { } signal) events.Add(_claude.Apply(signal));
        }
    }

    // ---- timers and folder watchers ----

    /// <summary>Wakes when the earliest pending Antigravity ending is due.</summary>
    private void ArmDue(DateTimeOffset now)
    {
        DateTimeOffset? next = null;
        foreach (var tail in _tails.Values)
            if (tail.Antigravity?.DueAt is { } at && (next is null || at < next)) next = at;
        if (next is not { } due) return;
        var wait = due - now + TimeSpan.FromMilliseconds(200);
        _due?.Change(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, Timeout.InfiniteTimeSpan);
    }

    private void Watch()
    {
        foreach (var home in _codexHomes()) AddWatcher(Path.Combine(home, "sessions"), "rollout-*", subfolders: true);
        if (_antigravityHome() is { } agy) AddWatcher(Path.Combine(agy, "brain"), "transcript.jsonl", subfolders: true);
        AddWatcher(_signalDir, ClaudeCodeSignalFile.Pattern, subfolders: false);
    }

    private void AddWatcher(string dir, string filter, bool subfolders)
    {
        try
        {
            if (!Directory.Exists(dir)) return;   // the poll covers a folder that appears later
            var w = new FileSystemWatcher(dir, filter) { IncludeSubdirectories = subfolders };
            FileSystemEventHandler soon = (_, _) => Soon();
            w.Changed += soon;
            w.Created += soon;
            w.Renamed += (_, _) => Soon();
            w.Error += (_, e) => _log?.Invoke($"agent activity: watcher error ({e.GetException().GetType().Name}), the timer covers it");
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _log?.Invoke($"agent activity: watcher not started ({e.GetType().Name})");
        }
    }

    private void Soon()
    {
        try { _soon?.Change(Debounce, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    private void Unwatch()
    {
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* best-effort teardown */ }
        }
        _watchers.Clear();
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentActivityWatcherTests"`
Expected: PASS, 18 tests.

- [ ] **Step 5: Confirm the MCP server still links none of it**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Mcp.Tests --filter "FullyQualifiedName~TrustBoundaryTests"`
Expected: PASS with no change to the test or to `Sanduhr.Mcp.csproj`. `git diff --stat main -- windows-dotnet/src/Sanduhr.Mcp` prints nothing.

- [ ] **Step 6: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/AgentActivityWatcher.cs windows-dotnet/tests/Sanduhr.Tests/AgentActivityWatcherTests.cs
git commit -m "feat(windows): watch each agent's files for its turns"
```

---

### Task 9: The notifier

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentNotifier.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/AgentNotifierTests.cs`

**Interfaces:**
- Consumes: `NotificationRules`, `NotificationSettings`, `NotifySurfaces`, `NotificationLimiter` (Task 5); `NotificationText` (Task 1); `AgentSources.ShortName(string id)`.
- Produces: `sealed class AgentNotifier`
  - constructor `(Func<NotificationSettings> settings, Func<bool> demo, Func<string?> foregroundProcess, Func<string?, string?> project, Func<DateTimeOffset> clock)`. `project` turns a folder into the name to show, already masked for streamer mode; null in, null out.
  - surface delegates, each settable: `Action<string, string, string>? Toast` (title, body, tag), `Action<AgentTurnEvent, bool>? Glow` (the event; whether to flash the taskbar too), `Action? Flash` (the flash without the light), `Action<AgentTurnEvent, string, string>? Card` (the event, the card's title, its short title), `Action<string>? Log`
  - `NotifySurfaces Handle(AgentTurnEvent e)`: call on the UI thread
  - `static string Tag(AgentTurnEvent e)`: 16 hex characters from the agent and session

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentNotifierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T20:00:00Z");

    private NotificationSettings _settings = NotificationSettings.Default with { GlowFinished = true, GlowWaiting = true, Flash = true };
    private bool _demo;
    private string? _foreground;
    private readonly List<string> _calls = [];

    private AgentNotifier Sut() => new(() => _settings, () => _demo, () => _foreground,
        folder => folder is null ? null : "masked:" + Path.GetFileName(folder), () => Now)
    {
        Toast = (title, body, tag) => _calls.Add($"toast|{title}|{body}|{tag}"),
        Glow = (e, flash) => _calls.Add($"glow|{e.Agent}|{e.Kind}|{flash}"),
        Flash = () => _calls.Add("flash"),
        Card = (e, title, shortTitle) => _calls.Add($"card|{title}|{shortTitle}|{e.Kind}"),
    };

    private static AgentTurnEvent E(AgentTurnKind kind, int? seconds = 252, string agent = "codex") =>
        new(agent, kind, "s1", @"C:\proj\alpha", Now, seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    [Fact]
    public void A_long_finished_turn_reaches_every_surface_with_the_masked_project()
    {
        var e = E(AgentTurnKind.Finished);
        var surfaces = Sut().Handle(e);
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Glow | NotifySurfaces.Flash | NotifySurfaces.Card, surfaces);
        Assert.Equal(
        [
            "card|Codex · masked:alpha|Codex|Finished",
            $"toast|Codex finished|masked:alpha, 4m 12s|{AgentNotifier.Tag(e)}",
            "glow|codex|Finished|True",
        ], _calls);
    }

    [Fact]
    public void With_the_glow_off_the_flash_goes_out_alone()
    {
        _settings = _settings with { GlowFinished = false };
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Contains("flash", _calls);
        Assert.DoesNotContain(_calls, c => c.StartsWith("glow"));
    }

    [Fact]
    public void With_the_flash_off_the_glow_does_not_flash()
    {
        _settings = _settings with { Flash = false };
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Contains("glow|codex|Finished|False", _calls);
        Assert.DoesNotContain("flash", _calls);
    }

    [Fact]
    public void A_started_turn_only_puts_up_its_card()
    {
        Sut().Handle(E(AgentTurnKind.Started, null, "claude"));
        Assert.Equal(["card|Claude Code · masked:alpha|CC|Started"], _calls);
    }

    [Fact]
    public void Waiting_names_what_it_is_and_carries_no_duration()
    {
        Sut().Handle(E(AgentTurnKind.Waiting, 9, "claude"));
        Assert.Contains(_calls, c => c.StartsWith("toast|Claude Code is waiting on you|masked:alpha|"));
    }

    [Fact]
    public void The_agents_window_in_front_leaves_only_the_card()
    {
        _foreground = "codex";
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Equal(["card|Codex · masked:alpha|Codex|Finished"], _calls);
    }

    [Fact]
    public void Another_agents_window_in_front_does_not_count()
    {
        _foreground = "codex";
        Sut().Handle(E(AgentTurnKind.Finished, agent: "claude"));
        Assert.Contains(_calls, c => c.StartsWith("toast|"));
    }

    [Fact]
    public void Demo_mode_does_nothing()
    {
        _demo = true;
        Assert.Equal(NotifySurfaces.None, Sut().Handle(E(AgentTurnKind.Finished)));
        Assert.Empty(_calls);
    }

    [Fact]
    public void An_agent_switched_off_does_nothing()
    {
        _settings = _settings.WithAgent("codex", false);
        Sut().Handle(E(AgentTurnKind.Finished));
        Assert.Empty(_calls);
    }

    [Fact]
    public void Settings_are_read_for_each_event()
    {
        var sut = Sut();
        _settings = _settings with { Toast = false };
        sut.Handle(E(AgentTurnKind.Finished));
        Assert.DoesNotContain(_calls, c => c.StartsWith("toast"));
    }

    [Fact]
    public void A_surface_that_throws_does_not_stop_the_others()
    {
        var sut = Sut();
        var logged = new List<string>();
        sut.Log = logged.Add;
        sut.Toast = (_, _, _) => throw new InvalidOperationException("boom");
        sut.Handle(E(AgentTurnKind.Finished));
        Assert.Contains(_calls, c => c.StartsWith("card"));
        Assert.Contains(_calls, c => c.StartsWith("glow"));
        Assert.Equal(["agent notifications: toast failed (InvalidOperationException)"], logged);
    }

    [Fact]
    public void A_missing_surface_is_skipped()
    {
        var sut = new AgentNotifier(() => _settings, () => false, () => null, _ => null, () => Now);
        Assert.Equal(NotifySurfaces.Toast | NotifySurfaces.Glow | NotifySurfaces.Flash | NotifySurfaces.Card, sut.Handle(E(AgentTurnKind.Finished)));
    }

    [Fact]
    public void The_tag_is_sixteen_hex_characters_and_one_per_session()
    {
        var a = AgentNotifier.Tag(E(AgentTurnKind.Finished));
        Assert.Matches("^[0-9A-F]{16}$", a);
        Assert.Equal(a, AgentNotifier.Tag(E(AgentTurnKind.Waiting)));
        Assert.NotEqual(a, AgentNotifier.Tag(E(AgentTurnKind.Finished, agent: "claude")));
        Assert.NotEqual(a, AgentNotifier.Tag(E(AgentTurnKind.Finished) with { Session = "s2" }));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentNotifierTests"`
Expected: build error, `AgentNotifier` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// One agent turn event in, the surfaces called (5.0 spec 4). The rules are <see cref="NotificationRules"/>; the app
/// plugs the four surfaces in as delegates, so this stays testable without a window. A surface that throws is logged by
/// its exception's type and never stops the others. Call <see cref="Handle"/> on the UI thread.
/// </summary>
public sealed class AgentNotifier(Func<NotificationSettings> settings, Func<bool> demo, Func<string?> foregroundProcess,
    Func<string?, string?> project, Func<DateTimeOffset> clock)
{
    private readonly NotificationLimiter _limiter = new();

    /// <summary>Title, body and a tag: a session's newer toast replaces its older one.</summary>
    public Action<string, string, string>? Toast { get; set; }

    /// <summary>The event, and whether the taskbar flashes with the light.</summary>
    public Action<AgentTurnEvent, bool>? Glow { get; set; }

    /// <summary>The taskbar flash without the light.</summary>
    public Action? Flash { get; set; }

    /// <summary>The event, the card's title and its short title.</summary>
    public Action<AgentTurnEvent, string, string>? Card { get; set; }

    public Action<string>? Log { get; set; }

    public NotifySurfaces Handle(AgentTurnEvent e)
    {
        if (demo()) return NotifySurfaces.None;
        var surfaces = NotificationRules.Decide(e, settings(), NotificationRules.InFront(e.Agent, foregroundProcess()), _limiter, clock());
        if (surfaces == NotifySurfaces.None) return surfaces;

        var name = project(e.Folder);
        if (surfaces.HasFlag(NotifySurfaces.Card))
            Try("card", () => Card?.Invoke(e, NotificationText.CardTitle(e.Agent, name), AgentSources.ShortName(e.Agent)));
        if (surfaces.HasFlag(NotifySurfaces.Toast))
            Try("toast", () => Toast?.Invoke(NotificationText.Title(e), NotificationText.Body(e, name), Tag(e)));
        if (surfaces.HasFlag(NotifySurfaces.Glow))
            Try("glow", () => Glow?.Invoke(e, surfaces.HasFlag(NotifySurfaces.Flash)));
        else if (surfaces.HasFlag(NotifySurfaces.Flash))
            Try("flash", () => Flash?.Invoke());
        return surfaces;
    }

    /// <summary>What ties a session's toasts together: 16 hex characters, well inside a toast tag's limit.</summary>
    public static string Tag(AgentTurnEvent e) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.Agent + "\n" + e.Session)))[..16];

    private void Try(string surface, Action call)
    {
        try { call(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log?.Invoke($"agent notifications: {surface} failed ({ex.GetType().Name})");
        }
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentNotifierTests"`
Expected: PASS, 13 tests.

- [ ] **Step 5: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src/Sanduhr.Core/AgentNotifier.cs windows-dotnet/tests/Sanduhr.Tests/AgentNotifierTests.cs
git commit -m "feat(windows): the notifier that calls the surfaces"
```

---

### Task 10: One toast builder and one activation handler

A behaviour-preserving move. The three toasts that exist keep their words, tags, groups and buttons.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AlertToastText.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Services/Toasts.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/AlertService.cs` (the constructor's activation hook at lines 26 to 35; `ShowToast`, `JoinBody` and `ResetLine` at lines 81 to 121)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/RestartToUpdateService.cs` (the activation hook at lines 25 to 40; `ShowToast` at lines 96 to 111)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskController.cs` (`ShowOpenFailed` at line 273)
- Test: `windows-dotnet/tests/Sanduhr.Tests/AlertToastTextTests.cs`

**Interfaces:**
- Consumes: `AlertEvent`, `AlertKind`, `Pacing.TimeUntil` (Core).
- Produces:
  - `static class AlertToastText` with `(string Headline, string Body) For(AlertEvent e, string tierLabel)`
  - `internal static class Toasts` with `void OnActivated(Action<string> handler)` (the handler gets the toast's argument string, empty for a body click) and `void Show(string title, string? body = null, string? group = null, string? tag = null, Action<ToastContentBuilder>? more = null)`

- [ ] **Step 1: Pin today's alert words with a failing test**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>The limit alert's toast says exactly what it said in 4.9 (pinned before the toast moved, 5.0 spec 4).</summary>
public class AlertToastTextTests
{
    private static AlertEvent E(AlertKind kind, int pct) => new(kind, "seven_day", pct, null);

    [Fact]
    public void Full() =>
        Assert.Equal(("Weekly at 100%", "Limit reached."), AlertToastText.For(E(AlertKind.Full, 100), "Weekly"));

    [Fact]
    public void Urgent() =>
        Assert.Equal(("Weekly at 95%", "Nearly out of headroom."), AlertToastText.For(E(AlertKind.Urgent, 95), "Weekly"));

    [Fact]
    public void Warn_with_no_reset_time_has_no_body() =>
        Assert.Equal(("Weekly at 80%", ""), AlertToastText.For(E(AlertKind.Warn, 80), "Weekly"));

    [Fact]
    public void Projection() =>
        Assert.Equal(("Codex Session (5hr) on pace to hit the cap", "Current burn rate exhausts this tier before it resets."),
            AlertToastText.For(E(AlertKind.Projection, 60), "Codex Session (5hr)"));

    [Fact]
    public void Reset_carries_the_previous_peak() =>
        Assert.Equal(("Weekly reset", "Fresh window after peaking at 80%."), AlertToastText.For(E(AlertKind.Reset, 80), "Weekly"));
}
```

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AlertToastTextTests"`
Expected: build error, `AlertToastText` does not exist.

- [ ] **Step 2: Move the words to Core, unchanged**

Create `AlertToastText.cs`. The `switch` and the two helpers are `AlertService`'s own, moved byte for byte except for the names:

```csharp
namespace Sanduhr.Core;

/// <summary>The words a limit alert's toast shows. Moved here from the app's AlertService so they are pinned by a test.</summary>
public static class AlertToastText
{
    public static (string Headline, string Body) For(AlertEvent e, string tierLabel) => e.Kind switch
    {
        AlertKind.Full => ($"{tierLabel} at 100%",
            JoinBody("Limit reached.", ResetLine(e))),
        AlertKind.Urgent => ($"{tierLabel} at {e.UtilizationPct}%",
            JoinBody("Nearly out of headroom.", ResetLine(e))),
        AlertKind.Warn => ($"{tierLabel} at {e.UtilizationPct}%",
            JoinBody(ResetLine(e))),
        AlertKind.Projection => ($"{tierLabel} on pace to hit the cap",
            "Current burn rate exhausts this tier before it resets."),
        // Reset events carry the PREVIOUS window's peak in UtilizationPct
        // (the number that made the reset newsworthy) — render it.
        _ => ($"{tierLabel} reset",
            $"Fresh window after peaking at {e.UtilizationPct}%."),
    };

    /// <summary>Joins non-empty body fragments with a space — keeps a blank
    /// ResetLine from leaving an empty second toast line or a trailing space.</summary>
    private static string JoinBody(params string[] parts) =>
        string.Join(" ", parts.Where(p => p.Length > 0));

    private static string ResetLine(AlertEvent e)
    {
        var until = Pacing.TimeUntil(e.ResetsAt);
        return until is "--" or "now" ? "" : $"Resets in {until}.";
    }
}
```

Run the test from Step 1. Expected: PASS, 5 tests. If one fails, the words were not copied exactly: fix the copy, never the test.

- [ ] **Step 3: Write `Toasts`**

```csharp
using Microsoft.Toolkit.Uwp.Notifications;

namespace Sanduhr.App.Services;

/// <summary>
/// Every Windows toast Sanduhr shows goes through here (5.0 spec 4): one builder and one activation handler in place of
/// three builders and two handlers. ToastNotificationManagerCompat resolves identity for the Store package and registers
/// one for the Velopack build. Callers keep their own try/catch: a toast that cannot show must never break its feature.
/// </summary>
internal static class Toasts
{
    private static readonly object Gate = new();
    private static readonly List<Action<string>> Handlers = [];
    private static bool _hooked;

    /// <summary>Adds a handler for a clicked toast. It gets the toast's argument string: empty for a click on the body,
    /// the button's arguments for a button. Handlers run on the activation thread, so each one dispatches for itself.</summary>
    public static void OnActivated(Action<string> handler)
    {
        lock (Gate)
        {
            Handlers.Add(handler);
            if (_hooked) return;
            ToastNotificationManagerCompat.OnActivated += Route;
            _hooked = true;
        }
    }

    private static void Route(ToastNotificationActivatedEventArgsCompat args)
    {
        Action<string>[] handlers;
        lock (Gate) handlers = [.. Handlers];
        var argument = args.Argument ?? "";
        foreach (var handler in handlers)
        {
            try { handler(argument); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine($"[Sanduhr] toast handler failed: {e.GetType().Name}");
            }
        }
    }

    /// <summary>Shows a toast. A <paramref name="tag"/> within a <paramref name="group"/> makes a newer toast replace an
    /// older one; <paramref name="more"/> adds buttons.</summary>
    public static void Show(string title, string? body = null, string? group = null, string? tag = null,
        Action<ToastContentBuilder>? more = null)
    {
        var builder = new ToastContentBuilder().AddText(title);
        if (!string.IsNullOrEmpty(body)) builder.AddText(body);
        more?.Invoke(builder);
        builder.Show(t =>
        {
            if (tag is not null) t.Tag = tag;
            if (group is not null) t.Group = group;
        });
    }
}
```

- [ ] **Step 4: Move the three call sites**

`AlertService.cs`: in the constructor, the hook becomes

```csharp
            // Toast body clicks re-activate the app; bring the widget forward.
            Toasts.OnActivated(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(activateWidget));
```

and `ShowToast` becomes (delete `JoinBody` and `ResetLine` from this file, and the `using` lines only they needed):

```csharp
    private static void ShowToast(AlertEvent e, string tierLabel)
    {
        var (headline, body) = AlertToastText.For(e, tierLabel);
        // Threshold alerts supersede each other per tier; tag so a newer
        // alert replaces a stale one instead of stacking.
        Toasts.Show(headline, body, group: "sanduhr-alerts", tag: e.TierKey);
    }
```

`RestartToUpdateService.cs`: the hook becomes

```csharp
            // The toast's button carries the action; the body click does not, and only shows the widget.
            // A 3.4 to 3.7 "Quit and update" toast still in the notification centre lands here too.
            Toasts.OnActivated(argument =>
            {
                if (argument.Contains(RestartToUpdate.RestartAction, StringComparison.Ordinal)
                    || argument.Contains(StoreUpdate.LegacyQuitAction, StringComparison.Ordinal))
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(Accept);
            });
```

and `ShowToast`'s body becomes

```csharp
            Toasts.Show(RestartToUpdate.ReadyText(version), RestartToUpdate.ToastBody, more: b => b
                .AddButton(new ToastButton().SetContent(RestartToUpdate.ButtonText).AddArgument("action", RestartToUpdate.RestartAction))
                .AddButton(new ToastButtonDismiss(RestartToUpdate.LaterText)));
```

`DeskController.cs`: `ShowOpenFailed`'s `try` body becomes

```csharp
            Services.Toasts.Show($"Couldn't open {name}", "Check that it is still there, then try again.");
```

(use the namespace form the file's other references to `Sanduhr.App.Services` use). Leave every surrounding `try`/`catch` and log line as it is.

- [ ] **Step 5: Check nothing else builds a toast**

Run: `grep -rn "ToastContentBuilder\|ToastNotificationManagerCompat.OnActivated" windows-dotnet/src`
Expected: matches in `Toasts.cs` only, plus `RestartToUpdateService.cs`'s `ToastButton` lines (which use `ToastButton`, not the builder). `ToastNotificationManagerCompat.Uninstall()` in `Program.cs` stays.

- [ ] **Step 6: Run every suite, then commit**

```bash
cd windows-dotnet && dotnet test
git add windows-dotnet/src windows-dotnet/tests/Sanduhr.Tests/AlertToastTextTests.cs
git commit -m "refactor(windows): one toast builder and one activation handler"
```

---

### Task 11: The app wires it up

After this task a real Codex, Antigravity or Claude Code turn reaches the toast, the glow, the flash and the Desk card, with the defaults. The page that changes them is Task 12.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (`LoadClaudeCodeGlow` and `SaveClaudeCodeGlow` at lines 689 to 711; new members beside them)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/ClaudeCodeGlowService.cs` (`Reload`, `Preview`, `Fire`, `Show`; new `Glow`, `FlashOnly`, `ForegroundProcess`)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskHandoffSeams.cs` (`WatcherStore`, after `ClearRunners`)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (after `StartGlow();` at line 161; `OnStreamerChanged` at line 1200; the `svm.Sources.OnOffChanged` handler at line 866; beside each `_glow?.Dispose();`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (the `agent => widget.Vault?.SetAgentVaultOn(agent)` argument near line 241)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/AgentNotificationWiringTests.cs`

**Interfaces:**
- Consumes: `NotificationSettings` (Task 5), `WatcherBoard.ApplyTurn`, `RetitleTurns`, `ClearTurns` (Task 6), `AgentActivityWatcher` (Task 8), `AgentNotifier` (Task 9), `Toasts.Show` (Task 10).
- Produces:
  - `SettingsStore.LoadAgentNotifications()` returning `NotificationSettings`; `SettingsStore.SaveAgentNotifications(NotificationSettings s)`
  - `ClaudeCodeGlowService.Glow(System.Windows.Media.Color color, bool flash)`, `FlashOnly()`, `string? ForegroundProcess()`
  - `WatcherStore.ApplyTurn(AgentTurnEvent e, string title, string shortTitle)`, `ClearTurns(string agent)`, `RetitleTurns(Func<Watcher, (string Title, string Short)> name)`
  - In `App`: fields `_agentActivity` (`AgentActivityWatcher?`) and `_agentNotifier` (`AgentNotifier?`), and `static System.Windows.Media.Color AgentColor(SettingsStore store, string agent)`, which Task 12 uses.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>5.0 spec 4: the notification settings in settings.json, and turn cards through the watcher store.</summary>
[Collection("SettingsWindow")]
public sealed class AgentNotificationWiringTests : AppWindowFixture
{
    private SettingsStore StoreOver(string settingsJson)
    {
        var paths = ScratchPaths();
        Directory.CreateDirectory(paths.AppDataDir);
        File.WriteAllText(paths.SettingsFile, settingsJson);
        return new SettingsStore(paths);
    }

    [Fact]
    public void With_nothing_saved_the_defaults_apply()
    {
        var s = StoreOver("{}").LoadAgentNotifications();
        Assert.True(s.Toast);
        Assert.True(s.DeskCard);
        Assert.False(s.GlowFinished);
        Assert.False(s.GlowWaiting);
        Assert.False(s.Flash);
        Assert.Equal(60, s.ThresholdSeconds);
        Assert.True(s.SkipInFront);
    }

    [Fact]
    public void The_old_glow_switches_carry_over_until_the_new_settings_are_saved()
    {
        var store = StoreOver("{\"claude_code_glow\":{\"waiting\":true,\"done\":false,\"skip_terminal\":false}}");
        var s = store.LoadAgentNotifications();
        Assert.True(s.GlowWaiting);
        Assert.False(s.GlowFinished);
        Assert.True(s.Flash);
        Assert.False(s.SkipInFront);

        var glow = store.LoadClaudeCodeGlow();
        Assert.Equal((true, false, false), (glow.Waiting, glow.Done, glow.SkipTerminal));

        store.SaveAgentNotifications(s with { GlowWaiting = false, Flash = false });
        Assert.False(store.LoadAgentNotifications().GlowWaiting);          // the saved settings win over the old switches
        Assert.False(store.LoadClaudeCodeGlow().Waiting);
    }

    [Fact]
    public void Saving_round_trips_and_leaves_where_the_glow_shows_alone()
    {
        var store = StoreOver("{}");
        var target = store.LoadGlowTarget();
        store.SaveGlowTarget(target);
        store.SaveAgentNotifications((NotificationSettings.Default with { Toast = false, ThresholdSeconds = 300 }).WithAgent("codex", false));

        var back = store.LoadAgentNotifications();
        Assert.False(back.Toast);
        Assert.Equal(300, back.ThresholdSeconds);
        Assert.False(back.AgentOn("codex"));
        Assert.True(back.AgentOn("antigravity"));
        Assert.Equal(target, store.LoadGlowTarget());
    }

    [Fact]
    public void Saving_the_glow_switches_keeps_the_other_notification_settings()
    {
        var store = StoreOver("{}");
        store.SaveAgentNotifications(NotificationSettings.Default with { Toast = false });
        store.SaveClaudeCodeGlow(new GlowSwitches(Waiting: true, Done: true, SkipTerminal: false));

        var s = store.LoadAgentNotifications();
        Assert.False(s.Toast);
        Assert.True(s.GlowWaiting);
        Assert.True(s.GlowFinished);
        Assert.False(s.SkipInFront);
    }

    [Fact]
    public void A_turn_card_goes_up_through_the_store_is_renamed_and_clears_with_its_agent() => AppHost.Run(() =>
    {
        using var store = new Sanduhr.App.Desk.WatcherStore(ScratchPaths(), () => false);
        int changed = 0;
        store.Changed += () => changed++;

        store.ApplyTurn(new AgentTurnEvent("codex", AgentTurnKind.Started, "s1", @"C:\proj\alpha", DateTimeOffset.UtcNow), "Codex · alpha", "Codex");
        Assert.Equal("Codex · alpha", Assert.Single(store.Shown).Title);      // no "Let Claude show watchers" needed

        store.RetitleTurns(_ => ("Codex · Dune Fox", "Codex"));
        Assert.Equal("Codex · Dune Fox", store.Shown[0].Title);

        store.ClearTurns("claude");
        Assert.Single(store.Shown);
        store.ClearTurns("codex");
        Assert.Empty(store.Shown);
        Assert.Equal(3, changed);
    });
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AgentNotificationWiringTests"`
Expected: build error, `LoadAgentNotifications` does not exist.

- [ ] **Step 3: The settings store**

In `SettingsStore.cs`, replace `LoadClaudeCodeGlow` and `SaveClaudeCodeGlow` with:

```csharp
    /// <summary>Settings, Notifications (5.0 spec 4): settings.json "agent_notifications". With none saved, the defaults
    /// with the old Claude Code glow switches ("claude_code_glow" {"waiting", "done", "skip_terminal"}) carried over.</summary>
    public NotificationSettings LoadAgentNotifications()
    {
        var root = Read();
        var old = root["claude_code_glow"] as JsonObject;
        return NotificationSettings.Decode(root[NotificationSettings.Key],
            new GlowSwitches(Waiting: ReadBool(old, "waiting", false), Done: ReadBool(old, "done", false), SkipTerminal: ReadBool(old, "skip_terminal", true)));
    }

    public void SaveAgentNotifications(NotificationSettings settings)
    {
        var root = Read();
        root[NotificationSettings.Key] = settings.Encode();
        Write(root);
    }

    /// <summary>The glow switches as the glow service reads them. Since 5.0 they live in "agent_notifications".</summary>
    public GlowSwitches LoadClaudeCodeGlow()
    {
        var n = LoadAgentNotifications();
        return new GlowSwitches(Waiting: n.GlowWaiting, Done: n.GlowFinished, SkipTerminal: n.SkipInFront);
    }

    public void SaveClaudeCodeGlow(GlowSwitches switches) =>
        SaveAgentNotifications(LoadAgentNotifications() with
        {
            GlowWaiting = switches.Waiting, GlowFinished = switches.Done, SkipInFront = switches.SkipTerminal,
        });
```

`LoadGlowTarget` and `SaveGlowTarget` stay as they are: the target members remain under `claude_code_glow`.

- [ ] **Step 4: The glow service takes a color**

In `ClaudeCodeGlowService.cs`:

`Reload` also reads the flash switch. Add the field and replace the method:

```csharp
    private bool _flash;

    /// <summary>Re-read the switches (Settings changed them).</summary>
    public void Reload()
    {
        var n = _store.LoadAgentNotifications();
        _switches = new GlowSwitches(Waiting: n.GlowWaiting, Done: n.GlowFinished, SkipTerminal: n.SkipInFront);
        _flash = n.Flash;
        _target = _store.LoadGlowTarget();
    }
```

`Preview`'s last line becomes `Show(GlowOverlayContent.ColorFor(e), flash: false);`

`Fire`'s last line becomes `Show(GlowOverlayContent.ColorFor(kind), _flash);` with this comment above it: `// An older hook's signal (3.5 to 4.9): it glows as it did, and flashes when Settings, Notifications says so.`

`Show` takes the color. Its signature becomes `private void Show(System.Windows.Media.Color color, bool flash)` and each of its three `GlowOverlayContent.ColorFor(kind)` becomes `color`.

Add after `Preview`:

```csharp
    /// <summary>Lights the current target in <paramref name="color"/> (an agent's Sources color for a finished turn),
    /// flashing the widget's taskbar button too when <paramref name="flash"/> is set. The notifier has already applied
    /// the switches, the in-front rule and the limiter (5.0 spec 4).</summary>
    public void Glow(System.Windows.Media.Color color, bool flash)
    {
        if (_disposed) return;
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => Glow(color, flash)); return; }
        Show(color, flash);
    }

    /// <summary>The taskbar flash without the light.</summary>
    public void FlashOnly()
    {
        if (_disposed) return;
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(FlashOnly); return; }
        try { GlowNative.FlashTray(new WindowInteropHelper(_widget).Handle); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception
                                      or DllNotFoundException or EntryPointNotFoundException)
        {
            // A flash that cannot show is not worth a crash.
        }
    }

    /// <summary>The process name of the window in front, null when there is none or it cannot be read.</summary>
    public string? ForegroundProcess() => ProcessNameOf(GlowNative.GetForegroundWindow(), out _);
```

- [ ] **Step 5: The watcher store takes turn cards**

In `DeskHandoffSeams.cs`, in `WatcherStore` after `ClearRunners`:

```csharp
    /// <summary>An agent's turn moved (5.0 spec 4). Not gated by "Let Claude show watchers", which covers the cards the
    /// MCP tools put up: these come from the app's own reading, behind Settings, Notifications.</summary>
    public void ApplyTurn(AgentTurnEvent e, string title, string shortTitle)
    {
        if (_disposed) return;
        if (Board.ApplyTurn(e, title, shortTitle, DateTimeOffset.Now)) Changed?.Invoke();
        Schedule();
    }

    /// <summary>An agent's source or its notifications went off: its turn cards go.</summary>
    public void ClearTurns(string agent)
    {
        if (_disposed) return;
        if (Board.ClearTurns(agent)) Changed?.Invoke();
    }

    /// <summary>Streamer mode changed: each turn card's title is rebuilt.</summary>
    public void RetitleTurns(Func<Watcher, (string Title, string Short)> name)
    {
        if (_disposed) return;
        if (Board.RetitleTurns(name)) Changed?.Invoke();
    }
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AgentNotificationWiringTests"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Start the watcher and the notifier in `App`**

In `App.xaml.cs`, add the fields beside `_glow`'s declaration:

```csharp
    private AgentActivityWatcher? _agentActivity;
    private AgentNotifier? _agentNotifier;
```

Right after the `StartGlow();` call at line 161, add `StartAgentNotifications();`. Then add these members after `StartGlow`:

```csharp
    /// <summary>Agent notifications (5.0 spec 4): watches each agent's own files for its turns and shows them on the toast,
    /// the glow, the taskbar flash and a Desk card. Codex and the Antigravity CLI are read only while their source is on.</summary>
    private void StartAgentNotifications()
    {
        if (_vm is not { } vm) return;
        var paths = new Sanduhr.Core.Paths();
        var store = new SettingsStore(paths);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);   // streamer: notname - user folder handed to the readers, never drawn
        var agyHome = AntigravityBurnSource.DefaultCliHome(profile);   // streamer: notname - the CLI folder the reader joins its names to, never drawn

        // The conversation list, read at most once a minute. Called by the watcher under its own lock, one call at a time.
        IReadOnlyDictionary<string, string?>? workspaces = null;
        var workspacesAt = DateTimeOffset.MinValue;
        string? AntigravityFolder(string conversation)
        {
            if (DateTimeOffset.UtcNow - workspacesAt > TimeSpan.FromSeconds(60))
            {
                workspaces = AntigravitySummaries.Read(AntigravityBurnSource.SummariesPath(agyHome));
                workspacesAt = DateTimeOffset.UtcNow;
            }
            return workspaces is not null && workspaces.TryGetValue(conversation, out var uris)
                ? AntigravityBurnSource.ProjectPathOf(uris) : null;
        }

        _agentNotifier = new AgentNotifier(
            store.LoadAgentNotifications,
            () => vm.IsDemo,
            () => _glow?.ForegroundProcess(),
            TurnProject,
            () => DateTimeOffset.UtcNow)
        {
            Toast = (title, body, tag) => Toasts.Show(title, body, group: "sanduhr-agents", tag: tag),
            Glow = (e, flash) => _glow?.Glow(
                e.Kind == AgentTurnKind.Waiting ? GlowOverlayContent.ColorFor(ClaudeCodeEvent.Waiting) : AgentColor(store, e.Agent), flash),
            Flash = () => _glow?.FlashOnly(),
            Card = (e, title, shortTitle) => _watchers?.ApplyTurn(e, title, shortTitle),
            Log = message => LogLine(paths, message),
        };

        _agentActivity = new AgentActivityWatcher(
            codexHomes: () => (new SettingsStore(paths).LoadSetup()?.CodexUsage ?? false)
                ? CodexHomes.Discover(Environment.GetEnvironmentVariable("CODEX_HOME"), profile)
                : [],
            antigravityHome: () => new SettingsStore(paths).LoadActivity().GetValueOrDefault(AgentSources.Antigravity)
                && System.IO.Directory.Exists(agyHome) ? agyHome : null,
            antigravityFolder: AntigravityFolder,
            claudeOn: () => true,
            signalDir: paths.ExternalDir,
            log: message => LogLine(paths, message));
        _agentActivity.Turn += e => Dispatcher.BeginInvoke(() =>
        {
            if (!_quitting) _agentNotifier?.Handle(e);
        });
        _agentActivity.AgentOff += agent => Dispatcher.BeginInvoke(() => _watchers?.ClearTurns(agent));
        _agentActivity.Start();
    }

    /// <summary>A turn's project as every surface shows it: the folder's name, or its codename while streamer mode hides it.</summary>
    private static string? TurnProject(string? folder) =>
        string.IsNullOrWhiteSpace(folder) ? null
            : StreamerMode.Mask.Name(CodenameCategory.Project, CcLogReader.ProjectDisplayName(folder));   // streamer: masked - a turn's project name goes through Mask.Name on this line

    /// <summary>An agent's color as saved in Sources.</summary>
    internal static System.Windows.Media.Color AgentColor(SettingsStore store, string agent) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(store.LoadSources().ColorOf(agent));

    private static void LogLine(Sanduhr.Core.Paths paths, string message)
    {
        try { System.IO.File.AppendAllText(paths.LogFile, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}"); }   // streamer: notname - written to the log file, never drawn
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { }
    }
```

If `CodexHomes.Discover` does not return an `IReadOnlyList<string>`, wrap it: `[.. CodexHomes.Discover(…)]`, as `SyncCodex` does at line 1504.

- [ ] **Step 8: Streamer mode, Sources switches and quitting**

In `OnStreamerChanged` (line 1200), add at the end:

```csharp
        // Turn cards name a project: rebuild their titles so a codename shows at once (5.0 spec 4).
        _watchers?.RetitleTurns(w => (NotificationText.CardTitle(w.Kind ?? AgentSources.Claude, TurnProject(w.Folder)), w.Short));
```

In the `svm.Sources.OnOffChanged = (wasAny, nowAny) =>` handler (line 866), add as its last statement:

```csharp
            _agentActivity?.Refresh();   // a source went on or off: watch the folders that are on now
```

In `SettingsViewModel.cs`, the Antigravity switch reaches the app through the last argument of `new SourcesViewModel(…)`. Add a settable hook to `SettingsViewModel`:

```csharp
    /// <summary>Set by App: an activity-only source was switched on (5.0 spec 4: the turn watcher looks at its folder at once).</summary>
    public Action? ActivitySourceChanged { get; set; }
```

and change that argument to:

```csharp
            agent => { widget.Vault?.SetAgentVaultOn(agent); ActivitySourceChanged?.Invoke(); });
```

Back in `App.xaml.cs`, beside `svm.Sources.DisplayChanged = …` (line 876): `svm.ActivitySourceChanged = () => _agentActivity?.Refresh();`. Switching Antigravity off is picked up by the 30-second poll, which then clears its cards.

Beside each of the two `_glow?.Dispose();` calls (lines 1431 and 1569), add:

```csharp
        _agentActivity?.Dispose();
        _agentActivity = null;
```

- [ ] **Step 9: Build, run every suite, commit**

```bash
cd windows-dotnet && dotnet build && dotnet test
git add windows-dotnet/src windows-dotnet/tests/Sanduhr.App.Tests/AgentNotificationWiringTests.cs
git commit -m "feat(windows): agent turns reach the toast, the glow, the flash and the Desk"
```

If the streamer lint in the App suite flags a new line, annotate it per the convention in Global Constraints; never loosen the lint.

---

### Task 12: The Notifications page and its What's New card

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs:7` (the `SettingsPage` enum)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (the sidebar at line 36, `PageTitle` at line 60, the glow names near line 439, new names)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (anchor constants, `Anchors` near line 219, `PageSynonyms` at line 475, the glow names' entries)
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/NotificationsViewModel.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/NotificationsPage.xaml` and `NotificationsPage.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (a tab after Alerts), `SettingsWindow.xaml.cs` (near line 248)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, `ClaudeCodeViewModel.cs` (lines 257 to 290 and the try-it commands), `Views/ClaudeCodePage.xaml` (lines 257 to 271)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (beside line 861)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/NotificationsPageTests.cs`; `windows-dotnet/tests/Sanduhr.Tests/WhatsNewCardsTests.cs`

**Interfaces:**
- Consumes: `NotificationSettings` (Task 5), `SettingsStore.LoadAgentNotifications` / `SaveAgentNotifications`, `App.AgentColor` (Task 11), `Toasts.Show` (Task 10).
- Produces: `SettingsPage.Notifications`; `SettingsIndex.NotifyAgents`, `NotifyShow`, `NotifyWhen`; `NotificationsViewModel`; `enum AgentRowState { Ready, SourceOff, HookMissing, HookOutdated }`; `SettingsViewModel.Notifications`.

- [ ] **Step 1: Names, in Core**

In `WhatsNew.cs`, add `Notifications` at the end of the `SettingsPage` enum.

In `SettingsNames.cs`: the first sidebar group becomes `new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts, SettingsPage.Notifications, SettingsPage.Privacy]),` and `PageTitle` gains `SettingsPage.Notifications => NotificationsTitle,`.

The three glow names near line 439 keep their constant names, so every table that lists them still compiles, and take words that fit every agent:

```csharp
    public const string GlowWhenWaiting = "Glow when an agent is waiting on you";
    public const string GlowWhenDone = "Glow when an agent finishes";
    public const string GlowNotInTerminal = "Not while the agent's window is in front";
```

Add:

```csharp
    // ---- Settings, Notifications (5.0 spec 4) ----
    public const string NotificationsTitle = "Notifications";
    public const string NotificationsIntro =
        "Know when an agent finishes a long turn or needs you. Sanduhr reads this from each agent's own files on this PC; nothing is sent anywhere.";
    public const string NotifyAgentsHeader = "Agents";
    public const string NotifyShowHeader = "Where it shows";
    public const string NotifyWhenHeader = "When";
    public const string NotifyToast = "Show a Windows notification";
    public const string NotifyDeskCard = "Show a card on the Desk";
    public const string NotifyDeskCardCaption = "Shows where the watcher cards piece is placed.";
    public const string NotifyFlash = "Flash Sanduhr in the taskbar";
    public const string NotifyThreshold = "Only for turns longer than";
    public const string NotifyInFrontCaption =
        "A terminal or an editor in front counts for Claude Code and Antigravity. The Codex app counts for Codex.";
    public const string NotifyGlowWhere = "Where the glow shows…";
    public const string NotifyTryFinished = "Try it: finished";
    public const string NotifyTryWaiting = "Try it: waiting";
    public const string NotifySourceOff = "Turn it on in Sources first.";
    public const string NotifyHookMissing = "Needs the Claude Code hook. Install it on the Claude Code page.";
    public const string NotifyHookOutdated =
        "The Claude Code hook here is an older one. Update it on the Claude Code page to get the notification and the Desk card.";

    /// <summary>What an agent's files can tell Sanduhr (spec 4, section 6).</summary>
    public static string NotifySupports(string agent) => agent switch
    {
        AgentSources.Claude => "Working, finished and waiting.",
        AgentSources.Codex => "Working and finished.",
        AgentSources.Antigravity => "Working, and finished about a minute and a half late.",
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "unknown agent"),
    };

    /// <summary>The accessible name of an agent's switch.</summary>
    public static string NotifyAgentSwitch(string agent) => $"Notify me about {AgentSources.BurnName(agent)}";
```

In `SettingsIndex.cs`, add the anchor ids beside the other constants:

```csharp
    public const string NotifyAgents = "notify-agents";
    public const string NotifyShow = "notify-show";
    public const string NotifyWhen = "notify-when";
```

add to `Anchors`, after the Alerts page's entries (sidebar order):

```csharp
        E(SettingsNames.NotifyAgentsHeader, SettingsPage.Notifications, NotifyAgents,
            ["agent notifications", "notify", "agent", "codex", "antigravity", "claude code", "finished", "done", "working"]),
        E(SettingsNames.NotifyShowHeader, SettingsPage.Notifications, NotifyShow,
            ["toast", "windows notification", "desk card", "glow", "flash", "taskbar"]),
        E(SettingsNames.NotifyWhenHeader, SettingsPage.Notifications, NotifyWhen,
            ["threshold", "longer than", "every turn", "in front", "terminal", "do not disturb"]),
```

and to `PageSynonyms`: `[SettingsPage.Notifications] = ["agent notifications", "toast", "finished", "waiting on you"],`.

The three glow names leave the Claude Code page: wherever `SettingsIndex` maps `SettingsNames.GlowWhenWaiting`, `GlowWhenDone` or `GlowNotInTerminal` to the Claude Code page's `glow` anchor (search the file for each name), point the entry at `NotifyShow` for the two glow names and `NotifyWhen` for the third, on `SettingsPage.Notifications`. `SettingsNames.GlowWhere` stays where it is.

Run `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~Settings"`. Tests that enumerate every page or every name will fail until the new page and its names are in their tables: add the new entries to each such table. Fix a pin by adding or moving an entry, never by deleting an assertion.

- [ ] **Step 2: The What's New card, test first**

In `WhatsNewCardsTests.cs`: put `"agent-notifications",` first in `Table_HasEveryReleaseNewestFirst`; change `Only_the_Codex_cards_name_Codex` to expect `["agent-notifications", "agent-activity", "codex-limits"]`; add:

```csharp
    [Fact]
    public void Release500_ShowsTheNotificationsCard()
    {
        var cards = WhatsNew.Cards("4.9.0", "5.0.0");
        var card = Assert.Single(cards);
        Assert.Equal("agent-notifications", card.Id);
        Assert.Equal("Know when your agent is done", card.Title);
        Assert.StartsWith("A notification, a Desk card or a glow when Claude Code, Codex or another agent finishes a long turn.", card.Body);
        Assert.Equal(SettingsPage.Notifications, card.Destination);
        Assert.Equal(SettingsIndex.NotifyAgents, card.Anchor);
    }
```

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~WhatsNewCardsTests"`. Expected: FAIL.

In `WhatsNewCards.cs`: `Table` starts `Anchored([.. Release500(), .. Release490(), …`; add

```csharp
    // 5.0.0 (spec 4, agent notifications). Names Codex (the window carries that trademark line) and not Antigravity
    // (it carries no Google line; No_card_names_Antigravity). Show me lands on Settings ▸ Notifications. More 5.0 cards
    // join this list as the release's other parts land.
    private static WhatsNewCard[] Release500() =>
    [
        WhatsNewCard.Of("5.0.0", "agent-notifications", "Know when your agent is done",
            "A notification, a Desk card or a glow when Claude Code, Codex or another agent finishes a long turn. Choose what shows in Settings, Notifications.",
            "\uEA8F", // Ringer
            WhatsNewPreview.None, SettingsPage.Notifications),
    ];
```

in `Anchored`, `NamesCodex = c.Id is "codex-limits" or "agent-activity" or "agent-notifications"`; in `AnchorFor`, add `"agent-notifications" => SettingsIndex.NotifyAgents,`.

Run the card tests again. Expected: PASS. `\uEA8F` is the Segoe Fluent Icons Ringer glyph, written as an escape so the invisible character cannot be lost in an edit.

- [ ] **Step 3: The view model, test first**

Create `NotificationsPageTests.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Notifications (5.0 spec 4), over a scratch profile.</summary>
[Collection("SettingsWindow")]
public sealed class NotificationsPageTests : AppWindowFixture
{
    private (NotificationsViewModel Vm, SettingsStore Store) Build(Func<string, AgentRowState>? state = null)
    {
        var store = new SettingsStore(ScratchPaths());
        return (new NotificationsViewModel(store, state ?? (_ => AgentRowState.Ready)), store);
    }

    [Fact]
    public void It_opens_on_the_defaults_with_a_row_per_agent() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        Assert.Equal(["claude", "codex", "antigravity"], vm.Agents.Select(a => a.Agent));
        Assert.Equal(["Claude Code", "Codex", "Antigravity"], vm.Agents.Select(a => a.Name));
        Assert.All(vm.Agents, a => Assert.True(a.On));
        Assert.True(vm.Toast);
        Assert.True(vm.DeskCard);
        Assert.False(vm.GlowFinished);
        Assert.False(vm.GlowWaiting);
        Assert.False(vm.Flash);
        Assert.True(vm.SkipInFront);
        Assert.Equal(60, vm.Threshold.Seconds);
        Assert.Equal(["Every turn", "30 seconds", "1 minute", "2 minutes", "5 minutes"], NotificationsViewModel.ThresholdChoices.Select(c => c.Name));
    });

    [Fact]
    public void Every_change_saves_and_tells_the_app() => AppHost.Run(() =>
    {
        var (vm, store) = Build();
        int changed = 0;
        vm.Changed = () => changed++;

        vm.Toast = false;
        vm.GlowFinished = true;
        vm.Flash = true;
        vm.SkipInFront = false;
        vm.Threshold = NotificationsViewModel.ThresholdChoices.Single(c => c.Seconds == 300);

        var s = store.LoadAgentNotifications();
        Assert.False(s.Toast);
        Assert.True(s.GlowFinished);
        Assert.True(s.Flash);
        Assert.False(s.SkipInFront);
        Assert.Equal(300, s.ThresholdSeconds);
        Assert.Equal(5, changed);
    });

    [Fact]
    public void Reloading_does_not_save_or_tell_the_app() => AppHost.Run(() =>
    {
        var (vm, store) = Build();
        store.SaveAgentNotifications(NotificationSettings.Default with { Toast = false, ThresholdSeconds = 120 });
        int changed = 0;
        vm.Changed = () => changed++;
        vm.Reload();
        Assert.False(vm.Toast);
        Assert.Equal(120, vm.Threshold.Seconds);
        Assert.Equal(0, changed);
    });

    [Fact]
    public void Switching_an_agent_off_saves_it_and_clears_its_cards() => AppHost.Run(() =>
    {
        var (vm, store) = Build();
        var cleared = new List<string>();
        vm.AgentSwitchedOff = cleared.Add;

        vm.Agents.Single(a => a.Agent == "codex").On = false;

        Assert.False(store.LoadAgentNotifications().AgentOn("codex"));
        Assert.True(store.LoadAgentNotifications().AgentOn("claude"));
        Assert.Equal(["codex"], cleared);
    });

    [Theory]
    [InlineData(AgentRowState.Ready, true, "")]
    [InlineData(AgentRowState.SourceOff, false, "Turn it on in Sources first.")]
    [InlineData(AgentRowState.HookMissing, false, "Needs the Claude Code hook. Install it on the Claude Code page.")]
    [InlineData(AgentRowState.HookOutdated, true,
        "The Claude Code hook here is an older one. Update it on the Claude Code page to get the notification and the Desk card.")]
    public void A_row_says_what_stands_in_its_way(AgentRowState state, bool canToggle, string text) => AppHost.Run(() =>
    {
        var (vm, _) = Build(_ => state);
        var row = vm.Agents[0];
        Assert.Equal(canToggle, row.CanToggle);
        Assert.Equal(text, row.StateText);
        Assert.Equal(text.Length > 0, row.HasStateText);
    });

    [Fact]
    public void Each_agents_row_says_what_it_supports() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        Assert.Equal(
        [
            "Working, finished and waiting.", "Working and finished.", "Working, and finished about a minute and a half late.",
        ], vm.Agents.Select(a => a.Supports));
    });

    [Fact]
    public void Try_it_goes_to_the_app_and_the_glow_link_lands_on_the_picker() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        var tried = new List<AgentTurnKind>();
        vm.TryIt = tried.Add;

        vm.TryFinishedCommand.Execute(null);
        vm.TryWaitingCommand.Execute(null);

        Assert.Equal([AgentTurnKind.Finished, AgentTurnKind.Waiting], tried);
        Assert.Equal(SettingsPage.ClaudeCode, vm.GlowWhereLink.Page);
        Assert.Equal("glow-where", vm.GlowWhereLink.Anchor);
    });

    [Fact]
    public void Every_control_on_the_page_reads_as_words_and_the_trademarks_show() => AppHost.Run(() =>
    {
        var (vm, _) = Build();
        var window = new Window { Content = new NotificationsPage { DataContext = vm }, Width = 640, Height = 900 };
        OffScreen(window);
        window.Show();
        try
        {
            window.UpdateLayout();
            var names = NamesOf(window);
            Assert.Empty(LeaksIn(names));
            foreach (var name in new[]
                     {
                         "Notify me about Claude Code", "Notify me about Codex", "Notify me about Antigravity",
                         SettingsNames.NotifyToast, SettingsNames.NotifyDeskCard, SettingsNames.GlowWhenDone, SettingsNames.GlowWhenWaiting,
                         SettingsNames.NotifyFlash, SettingsNames.NotifyThreshold, SettingsNames.GlowNotInTerminal,
                         SettingsNames.NotifyGlowWhere, SettingsNames.NotifyTryFinished, SettingsNames.NotifyTryWaiting,
                         SettingsNames.NotificationsIntro, SettingsNames.TrademarkNotice, SettingsNames.CodexNotice, SettingsNames.AntigravityNotice,
                     })
                Assert.Contains(name, names);

            var controls = Walk(UIElementAutomationPeer.CreatePeerForElement((UIElement)window.Content))
                .Where(p => p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox
                    or AutomationControlType.ComboBox)
                .ToList();
            // Three agents; toast, card, two glows, flash, in front; the threshold; the link and two Try it. If the count is off,
            // print each peer's type and name before touching the number: a control with no name is what this test is for.
            Assert.Equal(3 + 6 + 1 + 3, controls.Count);
            Assert.All(controls, p => Assert.False(string.IsNullOrWhiteSpace(p.GetName())));
        }
        finally { window.Close(); }
    });
}
```

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~NotificationsPageTests"`. Expected: build error.

- [ ] **Step 4: Write the view model**

```csharp
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>What stands between an agent and its notifications.</summary>
public enum AgentRowState
{
    Ready,

    /// <summary>Codex or Antigravity is off in Sources: nothing of it is read.</summary>
    SourceOff,

    /// <summary>Claude Code's hook is in no folder.</summary>
    HookMissing,

    /// <summary>Claude Code's hook is the 3.5 to 4.9 one: the glow works, the notification and the card do not.</summary>
    HookOutdated,
}

/// <summary>Settings, Notifications (5.0 spec 4): which agents notify, where it shows and when. Every change saves to
/// "agent_notifications" at once and tells the app, which re-reads the glow's switches.</summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    public sealed record ThresholdChoice(int Seconds, string Name)
    {
        public override string ToString() => Name;
    }

    public static IReadOnlyList<ThresholdChoice> ThresholdChoices { get; } =
    [
        new(0, "Every turn"), new(30, "30 seconds"), new(60, "1 minute"), new(120, "2 minutes"), new(300, "5 minutes"),
    ];

    private readonly SettingsStore _store;
    private readonly Func<string, AgentRowState> _agentState;
    private bool _loading;

    internal NotificationsViewModel(SettingsStore store, Func<string, AgentRowState> agentState)
    {
        _store = store;
        _agentState = agentState;
        _threshold = ThresholdChoices.Single(c => c.Seconds == NotificationSettings.DefaultThreshold);
        foreach (var agent in AgentSources.Burn) Agents.Add(new NotifyAgentRowViewModel(this, agent));
        Reload();
    }

    public ObservableCollection<NotifyAgentRowViewModel> Agents { get; } = [];

    [ObservableProperty] private bool _toast;
    [ObservableProperty] private bool _deskCard;
    [ObservableProperty] private bool _glowFinished;
    [ObservableProperty] private bool _glowWaiting;
    [ObservableProperty] private bool _flash;
    [ObservableProperty] private bool _skipInFront;
    [ObservableProperty] private ThresholdChoice _threshold;

    /// <summary>Set by App: a setting was saved (the glow service re-reads its switches).</summary>
    public Action? Changed { get; set; }

    /// <summary>Set by App: an agent's notifications went off (its Desk cards go).</summary>
    public Action<string>? AgentSwitchedOff { get; set; }

    /// <summary>Set by App: show a sample on the toast and the glow.</summary>
    public Action<AgentTurnKind>? TryIt { get; set; }

    /// <summary>Where the glow shows: the picker stays on the Claude Code page until spec 6 moves it.</summary>
    public SettingsLink GlowWhereLink { get; } = SettingsIndex.Check(SettingsPage.ClaudeCode, "glow-where");

    /// <summary>Reads the saved settings and each agent's state again (Settings does this on each visit).</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var s = _store.LoadAgentNotifications();
            Toast = s.Toast;
            DeskCard = s.DeskCard;
            GlowFinished = s.GlowFinished;
            GlowWaiting = s.GlowWaiting;
            Flash = s.Flash;
            SkipInFront = s.SkipInFront;
            Threshold = ThresholdChoices.Single(c => c.Seconds == s.ThresholdSeconds);
            foreach (var row in Agents) row.Load(s.AgentOn(row.Agent), _agentState(row.Agent));
        }
        finally { _loading = false; }
    }

    partial void OnToastChanged(bool value) => Save();
    partial void OnDeskCardChanged(bool value) => Save();
    partial void OnGlowFinishedChanged(bool value) => Save();
    partial void OnGlowWaitingChanged(bool value) => Save();
    partial void OnFlashChanged(bool value) => Save();
    partial void OnSkipInFrontChanged(bool value) => Save();
    partial void OnThresholdChanged(ThresholdChoice value) => Save();

    internal void AgentToggled(string agent, bool on)
    {
        if (_loading) return;
        Save();
        if (!on) AgentSwitchedOff?.Invoke(agent);
    }

    private void Save()
    {
        if (_loading) return;
        try
        {
            var agents = Agents.ToDictionary(a => a.Agent, a => a.On, StringComparer.Ordinal);
            _store.SaveAgentNotifications(new NotificationSettings(agents, Toast, GlowFinished, GlowWaiting, Flash, DeskCard,
                Threshold.Seconds, SkipInFront));
            Changed?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Sanduhr.Notifications] not saved: {e.GetType().Name}");
            Reload();   // the switches snap back to what is saved
        }
    }

    [RelayCommand] private void TryFinished() => TryIt?.Invoke(AgentTurnKind.Finished);
    [RelayCommand] private void TryWaiting() => TryIt?.Invoke(AgentTurnKind.Waiting);
}

/// <summary>One agent's row: its name, what its files can tell Sanduhr, its switch, and what stands in its way.</summary>
public sealed partial class NotifyAgentRowViewModel : ObservableObject
{
    private readonly NotificationsViewModel _owner;

    internal NotifyAgentRowViewModel(NotificationsViewModel owner, string agent)
    {
        _owner = owner;
        Agent = agent;
    }

    public string Agent { get; }
    public string Name => AgentSources.BurnName(Agent);
    public string Supports => SettingsNames.NotifySupports(Agent);
    public string SwitchName => SettingsNames.NotifyAgentSwitch(Agent);

    [ObservableProperty] private bool _on;
    [ObservableProperty] private bool _canToggle = true;
    [ObservableProperty] private string _stateText = "";

    public bool HasStateText => StateText.Length > 0;

    partial void OnStateTextChanged(string value) => OnPropertyChanged(nameof(HasStateText));
    partial void OnOnChanged(bool value) => _owner.AgentToggled(Agent, value);

    internal void Load(bool on, AgentRowState state)
    {
        On = on;
        CanToggle = state is AgentRowState.Ready or AgentRowState.HookOutdated;
        StateText = state switch
        {
            AgentRowState.SourceOff => SettingsNames.NotifySourceOff,
            AgentRowState.HookMissing => SettingsNames.NotifyHookMissing,
            AgentRowState.HookOutdated => SettingsNames.NotifyHookOutdated,
            _ => "",
        };
    }
}
```

- [ ] **Step 5: Write the page**

`NotificationsPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Notifications (5.0 spec 4). DataContext: the NotificationsViewModel.</summary>
public partial class NotificationsPage : UserControl
{
    public NotificationsPage() => InitializeComponent();
}
```

`NotificationsPage.xaml` (the frame, styles and footer follow `SourcesPage.xaml`):

```xml
<UserControl x:Class="Sanduhr.App.Views.NotificationsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:Sanduhr.App.Views"
             xmlns:vm="clr-namespace:Sanduhr.App.ViewModels"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- Settings, Notifications (5.0 spec 4): which agents notify, where it shows and when.
         Hosted in SettingsWindow. DataContext: the NotificationsViewModel. -->
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolVis" />
    </UserControl.Resources>
    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="8">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <ScrollViewer.Resources>
                <Style TargetType="ScrollBar" BasedOn="{StaticResource SlimThemeScrollBar}" />
            </ScrollViewer.Resources>
            <StackPanel x:Name="PageBody" Margin="20">
                <TextBlock Text="{x:Static core:SettingsNames.NotificationsTitle}" FontSize="14" FontWeight="SemiBold"
                           AutomationProperties.HeadingLevel="Level1"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,6" />
                <TextBlock Text="{x:Static core:SettingsNames.NotificationsIntro}" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,12"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />

                <!-- Agents: a switch each, what its files can tell Sanduhr, and what stands in its way. -->
                <StackPanel views:SettingsAnchor.Id="notify-agents">
                    <TextBlock Text="{x:Static core:SettingsNames.NotifyAgentsHeader}" FontWeight="SemiBold" Margin="0,0,0,6"
                               AutomationProperties.HeadingLevel="Level2" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                    <ItemsControl ItemsSource="{Binding Agents}" Focusable="False"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.NotifyAgentsHeader}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Margin="0,0,0,10">
                                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{Binding Name}"
                                              IsChecked="{Binding On, Mode=TwoWay}" IsEnabled="{Binding CanToggle}"
                                              AutomationProperties.Name="{Binding SwitchName}"
                                              AutomationProperties.HelpText="{Binding Supports}" />
                                    <TextBlock Text="{Binding Supports}" FontSize="11" TextWrapping="Wrap" Margin="24,2,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                                    <TextBlock Text="{Binding StateText}" FontSize="11" TextWrapping="Wrap" Margin="24,2,0,0"
                                               Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                                               Visibility="{Binding HasStateText, Converter={StaticResource BoolVis}}" />
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

                <!-- Where it shows: the four surfaces, each with its own switch. -->
                <StackPanel views:SettingsAnchor.Id="notify-show" Margin="0,10,0,0">
                    <TextBlock Text="{x:Static core:SettingsNames.NotifyShowHeader}" FontWeight="SemiBold" Margin="0,0,0,6"
                               AutomationProperties.HeadingLevel="Level2" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,6" Content="{x:Static core:SettingsNames.NotifyToast}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.NotifyToast}"
                              IsChecked="{Binding Toast, Mode=TwoWay}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.NotifyDeskCard}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.NotifyDeskCard}"
                              AutomationProperties.HelpText="{x:Static core:SettingsNames.NotifyDeskCardCaption}"
                              IsChecked="{Binding DeskCard, Mode=TwoWay}" />
                    <TextBlock Text="{x:Static core:SettingsNames.NotifyDeskCardCaption}" FontSize="11" TextWrapping="Wrap" Margin="24,2,0,6"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,6" Content="{x:Static core:SettingsNames.GlowWhenDone}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.GlowWhenDone}"
                              IsChecked="{Binding GlowFinished, Mode=TwoWay}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,6" Content="{x:Static core:SettingsNames.GlowWhenWaiting}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.GlowWhenWaiting}"
                              IsChecked="{Binding GlowWaiting, Mode=TwoWay}" />
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Margin="0,0,0,8" Content="{x:Static core:SettingsNames.NotifyFlash}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.NotifyFlash}"
                              IsChecked="{Binding Flash, Mode=TwoWay}" />
                    <StackPanel Orientation="Horizontal">
                        <Button Style="{DynamicResource SettingsLinkButton}" VerticalAlignment="Center" Content="{x:Static core:SettingsNames.NotifyGlowWhere}"
                                views:SettingsLinkTo.Link="{Binding GlowWhereLink}" AutomationProperties.Name="{x:Static core:SettingsNames.NotifyGlowWhere}" />
                        <Button Style="{DynamicResource FlatButton}" Padding="10,5" Margin="6,0,0,0" Content="{x:Static core:SettingsNames.NotifyTryFinished}"
                                Command="{Binding TryFinishedCommand}" AutomationProperties.Name="{x:Static core:SettingsNames.NotifyTryFinished}" />
                        <Button Style="{DynamicResource FlatButton}" Padding="10,5" Margin="6,0,0,0" Content="{x:Static core:SettingsNames.NotifyTryWaiting}"
                                Command="{Binding TryWaitingCommand}" AutomationProperties.Name="{x:Static core:SettingsNames.NotifyTryWaiting}" />
                    </StackPanel>
                </StackPanel>

                <!-- When: the threshold, and not while you are already looking at the agent. -->
                <StackPanel views:SettingsAnchor.Id="notify-when" Margin="0,16,0,0">
                    <TextBlock Text="{x:Static core:SettingsNames.NotifyWhenHeader}" FontWeight="SemiBold" Margin="0,0,0,6"
                               AutomationProperties.HeadingLevel="Level2" Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <TextBlock Text="{x:Static core:SettingsNames.NotifyThreshold}" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"
                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                        <ComboBox Style="{DynamicResource ThemedComboBox}" Width="140"
                                  ItemsSource="{x:Static vm:NotificationsViewModel.ThresholdChoices}"
                                  SelectedItem="{Binding Threshold, Mode=TwoWay}"
                                  AutomationProperties.Name="{x:Static core:SettingsNames.NotifyThreshold}" />
                    </StackPanel>
                    <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.GlowNotInTerminal}"
                              AutomationProperties.Name="{x:Static core:SettingsNames.GlowNotInTerminal}"
                              AutomationProperties.HelpText="{x:Static core:SettingsNames.NotifyInFrontCaption}"
                              IsChecked="{Binding SkipInFront, Mode=TwoWay}" />
                    <TextBlock Text="{x:Static core:SettingsNames.NotifyInFrontCaption}" FontSize="11" TextWrapping="Wrap" Margin="24,2,0,0"
                               Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                </StackPanel>

                <Border Height="1" Background="{DynamicResource Sanduhr.Brush.Border}" Margin="0,16,0,16" />
                <!-- Store test 10.1.4.4: the page names Claude, Codex and Antigravity, so all three disclaimers show. -->
                <views:TrademarkFooter />
                <TextBlock Text="{x:Static core:SettingsNames.CodexNotice}" TextWrapping="Wrap" FontSize="11" Margin="0,6,0,0"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                <TextBlock Text="{x:Static core:SettingsNames.AntigravityNotice}" TextWrapping="Wrap" FontSize="11" Margin="0,6,0,0"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            </StackPanel>
        </ScrollViewer>
    </Border>
</UserControl>
```

Run the page tests. Expected: PASS, 11 tests.

- [ ] **Step 6: Put the page in Settings**

`SettingsViewModel.cs`: add `public NotificationsViewModel Notifications { get; }` beside `Sources`, and in the constructor after `Sources = …`:

```csharp
        Notifications = new NotificationsViewModel(new SettingsStore(new Paths()), agent => agent switch
        {
            AgentSources.Codex => (new SettingsStore(new Paths()).LoadSetup()?.CodexUsage ?? false) ? AgentRowState.Ready : AgentRowState.SourceOff,
            AgentSources.Antigravity => new SettingsStore(new Paths()).LoadActivity().GetValueOrDefault(AgentSources.Antigravity)
                ? AgentRowState.Ready : AgentRowState.SourceOff,
            _ => ClaudeCodePage.HookState(),
        });
```

`ClaudeCodeViewModel.cs`: each folder row (`CcFolderRowViewModel`) has a `Hooks` piece whose `IsInstalled` is set from the integrations engine's `Status(IntegrationKind.Hooks, folder)` (search the file for `IntegrationKind.Hooks`). The piece keeps only a bool, so keep the status too. On `CcFolderRowViewModel` add:

```csharp
    /// <summary>The hook's status in this folder as the engine last reported it (Settings, Notifications reads it).</summary>
    internal IntegrationStatus HooksStatus { get; set; } = IntegrationStatus.NotInstalled;
```

set it on the line that reads that status for the row, and add to `ClaudeCodeViewModel`:

```csharp
    /// <summary>What Settings, Notifications says on Claude Code's row: ready when any folder has the current hook, an older
    /// hook when none has but one has the 3.5 to 4.9 one, else missing.</summary>
    internal AgentRowState HookState()
    {
        if (Folders.Any(f => f.HooksStatus == IntegrationStatus.Installed)) return AgentRowState.Ready;
        return Folders.Any(f => f.HooksStatus == IntegrationStatus.Outdated) ? AgentRowState.HookOutdated : AgentRowState.HookMissing;
    }
```

Add a test beside the page's existing folder tests: one folder whose `settings.json` carries the 4.9 hook (use `ClaudeCodeIntegrations.GlowHookCommand`) gives `HookState() == AgentRowState.HookOutdated`; after the page's Update for that folder it gives `Ready`; with no hook anywhere it gives `HookMissing`.

`SettingsWindow.xaml`: right after the Alerts `TabItem` closes, add

```xml
                <!-- Notifications (5.0 spec 4): which agents notify, where it shows and when. -->
                <TabItem Header="Notifications" Tag="Notifications" AutomationProperties.Name="Notifications" DataContext="{Binding Notifications}">
                    <views:NotificationsPage />
                </TabItem>
```

`SettingsWindow.xaml.cs`, beside the Sources line near 248:

```csharp
        // Notifications reads the saved switches and each agent's state again: Sources and the Claude Code page change them.
        if (page == SettingsPage.Notifications)
            ViewModel.Notifications.Reload();
```

- [ ] **Step 7: The glow switches leave the Claude Code page**

`ClaudeCodePage.xaml`: delete the three `CheckBox`es and the two Try it buttons (lines 257 to 271). In their place, under the `GlowHookStatus` text, put a link to the new page, in the form the page's own "Mods & Widgets settings…" link uses (line 135):

```xml
                    <Button Style="{DynamicResource SettingsLinkButton}" HorizontalAlignment="Left" Margin="0,0,0,4"
                            Content="Notification settings…" views:SettingsLinkTo.Link="{Binding NotificationsLink}"
                            AutomationProperties.Name="Notification settings…" />
```

`ClaudeCodeViewModel.cs`: remove `GlowWaiting`, `GlowDone`, `GlowSkipTerminal`, their change handlers and save (near line 883), the three lines that load them (288 to 290), and `TryWaitingCommand` / `TryDoneCommand`. Add:

```csharp
    /// <summary>The glow's switches live on Settings, Notifications since 5.0 (spec 4).</summary>
    public SettingsLink NotificationsLink { get; } = SettingsIndex.Check(SettingsPage.Notifications, SettingsIndex.NotifyShow);
```

`GlowReload` and `GlowPreview` stay: the glow target picker on this page still uses them.

The words in `SettingsNames.GlowNeedsHook` and the text at line 290 ("The taskbar button flashes in every choice. Only flash skips the light.") describe the picker and stay.

Tests that drove the removed checkboxes or Try it buttons through this page: move each assertion onto `NotificationsPageTests` if it is not already covered there, then delete the old test. Run `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~ClaudeCode|FullyQualifiedName~ModsAndCards"` and fix what the removal broke.

- [ ] **Step 8: The app's side of the page**

In `App.xaml.cs`, beside `svm.ClaudeCodePage.GlowReload = …` (line 861), add:

```csharp
        // Settings, Notifications (5.0 spec 4).
        svm.Notifications.Changed = () => _glow?.Reload();
        svm.Notifications.AgentSwitchedOff = agent => _watchers?.ClearTurns(agent);
        svm.Notifications.TryIt = kind =>
        {
            // A sample through the real toast and the real glow, whatever the switches say: Try it shows what they would do.
            var store = new SettingsStore(new Sanduhr.Core.Paths());
            var sample = new AgentTurnEvent(AgentSources.Claude, kind, "try-it", null, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(252));
            try { Toasts.Show(NotificationText.Title(sample), NotificationText.Body(sample, "your project"), group: "sanduhr-agents", tag: "try-it"); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* a toast that cannot show is not worth a crash */ }
            _glow?.Glow(kind == AgentTurnKind.Waiting
                ? GlowOverlayContent.ColorFor(ClaudeCodeEvent.Waiting) : AgentColor(store, AgentSources.Claude), flash: false);
        };
```

The two links between the pages need nothing here: `SettingsLinkTo.Link` buttons already route through the window. `"glow-where"` is the anchor id on the Claude Code page (`views:SettingsAnchor.Id="glow-where"`); if `SettingsIndex` has a constant for it, use the constant in `GlowWhereLink` and in its test.

- [ ] **Step 9: Build, run every suite, commit**

```bash
cd windows-dotnet && dotnet build && dotnet test
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(windows): the Notifications page and its What's New card"
```

---

### Task 13: Disclosure, the changelog and the live check

**Files:**
- Modify: `docs/PRIVACY.md` (the Windows data table, after the "Codex and Antigravity CLI activity" row; "Last updated")
- Modify: `CHANGELOG.md` (a new `## Unreleased` section above `## v4.9.0`)
- Create: `docs/store/hub-privacy-5.0.0.md`

- [ ] **Step 1: The privacy policy**

Set `**Last updated:**` to the day this lands. Add this row to the Windows table, right after the row that starts `| **Codex and Antigravity CLI activity**`:

```markdown
| **Agent notifications** (Windows 5.0; for Codex and the Antigravity CLI only while their source is on, for Claude Code only while its hook is installed; each switch is in Settings ▸ Notifications) | Nothing is kept. While Claude Code's hook is installed it writes small files, `cc-signal-*.json`, under `%USERPROFILE%\.sanduhr`, and Sanduhr deletes each one as it reads it | Only your Windows user account | Sanduhr watches Codex's session logs and the Antigravity CLI's conversation logs (the files the row above reads) for when a turn starts and ends, and Claude Code's hook writes one file per event naming the event, the session's folder and its id. From all of them Sanduhr takes times, folder names and ids, never a prompt or a reply. What a turn is doing is held in memory only. A Windows notification shows the agent, the project's name (its codename in streamer mode) and how long the turn ran; Windows keeps it in its notification history until you clear it. The Desk card and the glow show the same and nothing more. No network call. Reset Sanduhr… removes any signal file left behind. |
```

- [ ] **Step 2: The changelog**

Above `## v4.9.0 — 2026-11-17`, add:

```markdown
## Unreleased

### New

- **Know when your agent is done.** A Windows notification, a card on the Desk, the glow or a taskbar flash when Claude Code, Codex or the Antigravity CLI finishes a long turn, and when Claude Code is waiting on you. It names the agent, the project and how long the turn ran ("Codex finished", "your-project, 4m 12s"), never anything that was said. By default the notification and the Desk card are on, the glow and the flash are off, and a finished turn only interrupts when it ran at least a minute and the agent's own window isn't in front; all of it is in the new Settings ▸ Notifications. Codex and Antigravity notify while their source is on in Sources. Antigravity's "finished" arrives about a minute and a half after its last step, because its log has no end marker. A finished glow takes the agent's color from Sources.
- **The Claude Code hook is updated.** Update it in Settings ▸ Claude Code to get the notification and the Desk card: the new hook tells Sanduhr which folder and session an event came from, and when a turn started. The older hook keeps lighting the glow as before.

### Changed

- **The glow's switches moved to Settings ▸ Notifications** and now cover every agent. Where the glow shows stays on the Claude Code page. With the updated hook, a finished glow waits for a turn of at least a minute; choose "Every turn" under "Only for turns longer than" for the old behaviour.
```

- [ ] **Step 3: The hosted privacy page's text**

Create `docs/store/hub-privacy-5.0.0.md`, modeled on `docs/store/hub-privacy-4.9.0.md`: the same header (repo, page, section, "Must be live before the 5.0.0.0 Store submission is committed", a `curl … | grep -c -e 'cc-signal' -e 'Agent notifications'` check expecting 2 or more) and one paragraph to place after the 4.9.0 paragraph:

```html
<p>
  <strong>Agent notifications on Windows (5.0.0).</strong> Sanduhr can tell you when an AI agent finishes a long turn or needs you, on a Windows notification, a card on the Desk, the glow or a taskbar flash; each has its own switch in <strong>Settings, Notifications</strong>. It adds no network request and needs no sign-in. For Codex and the Antigravity CLI, only while their source is on in Sources, it watches the same logs the activity count reads (<code>%USERPROFILE%\.codex\sessions</code> and <code>%USERPROFILE%\.gemini\antigravity-cli</code>) for when a turn starts and ends. For Claude Code, only while Sanduhr's hook is installed, the hook writes one small file per event under <code>%USERPROFILE%\.sanduhr</code> (<code>cc-signal-*.json</code>) naming the event, the session's folder and its id, and Sanduhr deletes each file as it reads it. From all of these it takes times, folder names and ids, never a prompt or a reply, and it keeps nothing: what a turn is doing is held in memory only. A notification shows the agent, the project's name (its codename in streamer mode) and how long the turn ran, and Windows keeps it in its notification history until you clear it. Reset Sanduhr removes any signal file left behind. "Codex" is a trademark of OpenAI and "Antigravity" is a trademark of Google LLC, used nominatively; Sanduhr für Claude is not affiliated with, endorsed by, or associated with OpenAI or Google LLC.
</p>
```

Add a line under the header: "5.0 has more parts (specs 5 to 7); each adds its own paragraph to this file before the release."

- [ ] **Step 4: Run every suite and commit**

```bash
cd windows-dotnet && dotnet test
git add docs/PRIVACY.md CHANGELOG.md docs/store/hub-privacy-5.0.0.md
git commit -m "docs(windows): agent notifications in the privacy policy and the changelog"
```

- [ ] **Step 5: The live check (the controller, with the owner)**

A dev build shares its data folders with the installed app. Before launching it: quit the Store app, back up `%LOCALAPPDATA%\Sanduhr`, `%APPDATA%\Sanduhr` (the WebView2 cache folders can be skipped) and `%USERPROFILE%\.sanduhr`. Afterwards restore all three and relaunch the Store app. Read figures and names from the UI only; never paste a file's contents.

1. **Nothing replays.** Launch with Codex on in Sources and recent Codex history. No toast and no card appear for old turns.
2. **Codex, out of sight.** With another window in front, a Codex turn of over a minute ends in a toast "Codex finished" with the project and the length, and a card on the Desk that fades about a minute later.
3. **Codex, in front.** The same with the Codex app in front: the card only.
4. **Sub-agents.** A Codex run that spawns sub-agents gives one notification.
5. **Antigravity.** With its switch on, an `agy -p` turn of over a minute gives a toast about a minute and a half after it ends, with the right length.
6. **Claude Code, updated hook.** Update the hook in Settings ▸ Claude Code. A turn of over a minute in a terminal that is not in front gives the toast with the folder's name; a permission prompt gives "Claude Code is waiting on you" at once.
7. **Claude Code, older hook.** In a second folder left on the 4.9 hook, the glow fires as before (with its switch on) and no toast appears; Notifications says the hook is an older one.
8. **Streamer mode.** On, with a card up: the card and the next toast show a codename.
9. **Do Not Disturb.** On: no toast pops; the Desk card still shows.
10. **The switches.** Each switch in Settings ▸ Notifications does what it says; "Every turn" makes a short turn notify; Try it shows the toast and the glow.
11. **Restart mid-turn.** Quit and relaunch during a long Codex turn: its end still notifies.

Record each result in the PR description. A failure on 5 that is a false "finished" drops Antigravity to working only, as the spec says: tell the owner before changing anything.

