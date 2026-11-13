# Every Agent's Activity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Codex and Antigravity CLI local burn land in the usage Vault beside Claude Code, and show in the widget footer, the Local burn page's strip, project list and session ledger, and the MCP's `get_local_burn_by_project`.

**Architecture:** Each agent is a Vault root (`codex`, `antigravity`) written by the existing `VaultIngester` through a small `IAgentBurnSource` interface: a source lists files and hands out a line parser; the ingester keeps owning checkpoints, shards, rollups, quarantine and the write order. Claude Code's path is refactored onto the same line-parser seam with no behavior change. Everything downstream reads roots, so agents arrive through `VaultReader` with an `Agent` tag from each root's meta.

**Tech Stack:** .NET 10, WPF, xUnit 2.9, ZstdSharp.Port 0.8.5 (already in the App), Windows' own `winsqlite3.dll` through P/Invoke (no new package).

**Spec:** `docs/superpowers/specs/2026-11-13-every-agents-activity-design.md`. Read it first; this plan argues from it.

## Global Constraints

- No network call and no new credential anywhere in this work.
- `Sanduhr.Core` keeps zero package references; zstd and SQLite arrive as injected callbacks from the App.
- `Sanduhr.Mcp` links no new Core file: `TrustBoundaryTests` must pass unchanged. MCP code mirrors names (`"codex"`, `"antigravity"`) literally, with a comment.
- Readers never read prompt or reply text, titles, previews or `raw_summary`. Antigravity SQL is exactly `SELECT conversation_id, workspace_uris FROM conversation_summaries`.
- Codex: read `rollout-*.jsonl` and `rollout-*.jsonl.zst` under every `CodexHomes.Discover` home; decompression capped at `CodexLimitsReader.MaxDecompressedBytes` (64 MB).
- Antigravity: CLI only, `~/.gemini/antigravity-cli`. Read `brain/<id>/.system_generated/logs/transcript.jsonl` only, never `transcript_full.jsonl`, never the IDE folder.
- Antigravity default color `#7B8CF0`; Claude `#E0875A`, Codex `#2FB5A3` unchanged. Colors must pass `AgentSourceColorTests` on all built-in themes.
- Antigravity is never a limits source: not in `AgentSources.All`, never a widget section, primary, Desk meter or setup tick.
- Logs carry fixed phrases and exception type names only (PRIVACY.md contract). Every path shown on screen passes the existing streamer-mode masks; mark new lines with the `// streamer:` comment convention used in the touched file.
- CRLF working tree, LF index: edit with the Edit tool or Python, never `sed -i`.
- Never edit `docs/appcast.xml` or anything under `mac/`.

## Review Focus

1. **A Codex rollout being written while read.** A torn last line must not be consumed, and the next pass must resume with the parser state in force (segment, model, cwd, last total), so the delta does not restart. Test in Task 4.
2. **A compressed rollout over the 64 MB cap, or a bad zstd frame.** That file counts as failed and logs a type name. Other files and other agents still ingest. Test in Task 4.
3. **Antigravity's database locked or missing** (Antigravity running, or the CLI not installed). No Antigravity file is listed, so no checkpoint moves and nothing is lost. The next pass reads everything. Test in Task 5.
4. **Purge or erase while an agent's switch is on.** The agent root must stay purged (tombstone in `vault_roots`) until the person turns the source on again, which clears it. Test in Task 7.
5. **The same Codex rollout plain and compressed, or seen twice.** Both write the same session key, so the row is replaced and never doubled. Test in Task 4.

---

## File Structure

Core (`windows-dotnet/src/Sanduhr.Core/`):
- Modify `AgentSources.cs`: `Antigravity`, `ActivityOnly`, `Burn`, `ShortName`, `BurnName`.
- Modify `VaultModels.cs`: `VaultRootMeta.Agent`, `VaultCheckpointEntry.ParserState`.
- Create `BurnParsing.cs`: `BurnEvent`, `IBurnLineParser`, `ClaudeLineParser`.
- Create `AgentBurnSource.cs`: `AgentBurnFile`, `IAgentBurnSource`, `CappedReadStream`.
- Modify `VaultIngester.cs`: the parser seam, `IngestOnce(..., agents)`, compressed files, parser state, meta agent.
- Modify `VaultReader.cs`: `VaultSessionInfo.Agent`, `AgentOf(root)`.
- Create `CodexBurnSource.cs`: `CodexBurnSource`, `CodexLineParser`.
- Create `AntigravityBurnSource.cs`: `AntigravityBurnSource` and its nested `Parser`.
- Create `AgentBurnDeltas.cs`: the footer's per-agent change between ingests.
- Create `AgentShares.cs`: "1.2M (Claude Code 900K, Codex 300K)" text.
- Modify `VaultLedgerCsv.cs`: `agent` column.
- Modify `McpSettingsExport.cs`: export `mcp_agents`.
- Modify `SettingsNames.cs`: new strings.

App (`windows-dotnet/src/Sanduhr.App/`):
- Create `Services/AntigravitySummaries.cs` (winsqlite3 read-only lookup).
- Modify `Services/VaultService.cs`, `Services/SettingsStore.cs`, `App.xaml.cs`.
- Modify `ViewModels/SourcesViewModel.cs`, `Views/SourcesPage.xaml`.
- Modify `ViewModels/WidgetViewModel.cs` (footer).
- Modify `ViewModels/LocalCcViewModel.cs`, `Views/LocalCcBarStrip.cs`, `Views/SettingsWindow.xaml(.cs)`.
- Modify `ViewModels/CcLedgerViewModel.cs`.
- Modify `ViewModels/ClaudeCodeViewModel.cs` and its Folders XAML (agent read switches).

MCP (`windows-dotnet/src/Sanduhr.Mcp/`): Modify `McpConfig.cs`, `ToolLogic.cs`.

Tests: `windows-dotnet/tests/Sanduhr.Tests/` (Core), `tests/Sanduhr.App.Tests/`, `tests/Sanduhr.Mcp.Tests/`.

Commands (from `windows-dotnet/`):
- Core tests: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~<Name>"`
- App tests: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~<Name>"` (stop any running dev `Sanduhr.exe` first)
- MCP tests: `dotnet test tests/Sanduhr.Mcp.Tests --filter "FullyQualifiedName~<Name>"`
- Full suites before each commit that touches shared code: `dotnet test tests/Sanduhr.Tests` and so on.

---

### Task 1: Agent identities and the Vault's agent tag

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/AgentSources.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (`VaultRootMeta`, after `WalkVersion`)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultReader.cs` (`VaultSessionInfo`, `ReadSessions`)
- Test: `windows-dotnet/tests/Sanduhr.Tests/AgentSourceColorTests.cs`, create `windows-dotnet/tests/Sanduhr.Tests/AgentSourcesBurnTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/VaultReaderTests.cs`

**Interfaces:**
- Produces: `AgentSources.Antigravity = "antigravity"`, `AgentSources.ActivityOnly`, `AgentSources.Burn` (`[claude, codex, antigravity]`), `AgentSources.ShortName(id)` (`"CC"`, `"Codex"`, `"Agy"`), `AgentSources.BurnName(id)` (`"Claude Code"`, `"Codex"`, `"Antigravity"`); `VaultRootMeta.Agent` (`string?`, JSON `agent`); `VaultSessionInfo.Agent` (`string`, last positional parameter, default `AgentSources.Claude`); `VaultReader.AgentOf(string root)`.

- [ ] **Step 1: Write the failing tests**

Create `AgentSourcesBurnTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSourcesBurnTests
{
    [Fact]
    public void Antigravity_is_a_burn_source_but_never_a_limits_source()
    {
        Assert.Equal(new[] { "claude", "codex", "antigravity" }, AgentSources.Burn);
        Assert.DoesNotContain(AgentSources.Antigravity, AgentSources.All);
        Assert.Equal(new[] { "antigravity" }, AgentSources.ActivityOnly);
    }

    [Theory]
    [InlineData("claude", "CC", "Claude Code")]
    [InlineData("codex", "Codex", "Codex")]
    [InlineData("antigravity", "Agy", "Antigravity")]
    public void Names(string id, string shortName, string burnName)
    {
        Assert.Equal(shortName, AgentSources.ShortName(id));
        Assert.Equal(burnName, AgentSources.BurnName(id));
    }

    [Fact]
    public void Antigravity_has_its_default_color_and_a_display_name()
    {
        Assert.Equal("#7B8CF0", AgentSources.DefaultColor(AgentSources.Antigravity));
        Assert.Equal("Antigravity", AgentSources.Name(AgentSources.Antigravity));
        Assert.Equal("#7B8CF0", SourceSettings.Default.ColorOf(AgentSources.Antigravity));
    }
}
```

In `AgentSourceColorTests.cs`, change `Pairs()` to iterate every burn source:

```csharp
        foreach (var theme in ThemeCatalog.BuiltInOrder)
            foreach (var source in AgentSources.Burn)
                d.Add(theme, source);
```

In `VaultReaderTests.cs`, add (reuse the file's existing helpers for writing a shard; if it has none for meta, write meta through `VaultStore.SaveMeta`):

```csharp
    [Fact]
    public void Sessions_carry_the_agent_of_their_root_and_claude_when_meta_has_none()
    {
        using var t = new TempDir();
        var store = new VaultStore(t.Path);
        var row = new VaultSessionRow
        {
            ProjectKey = "api~12345678", ProjectName = "api",
            FirstTs = "2026-11-10T10:00:00.000000+00:00", LastTs = "2026-11-10T11:00:00.000000+00:00",
            EventCount = 1, Total = 10,
            ByModel = new() { ["m"] = 10 },
            ByDay = new() { ["2026-11-10"] = new VaultDayBucket { Total = 10, ByModel = new() { ["m"] = 10 } } },
        };
        foreach (var root in new[] { ".claude", "codex" })
        {
            var shard = new VaultSessionShard();
            shard.Sessions["s1"] = row;
            store.SaveSessionShard(root, "2026-11", shard);
        }
        store.SaveMeta("codex", new VaultRootMeta { Agent = "codex" });

        var sessions = new VaultReader(store).ReadSessions(new[] { ".claude", "codex" });

        Assert.Equal("claude", sessions.Single(s => s.Root == ".claude").Agent);
        Assert.Equal("codex", sessions.Single(s => s.Root == "codex").Agent);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentSourcesBurnTests|FullyQualifiedName~VaultReaderTests|FullyQualifiedName~AgentSourceColorTests"`
Expected: build errors (`AgentSources.Burn`, `VaultRootMeta.Agent`, `VaultSessionInfo.Agent` do not exist).

- [ ] **Step 3: Implement**

`AgentSources.cs`, replace the class body's declarations and switches with:

```csharp
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Antigravity = "antigravity";

    /// <summary>Limits sources: widget sections, primary, Desk meters, setup ticks.</summary>
    public static readonly IReadOnlyList<string> All = [Claude, Codex];

    /// <summary>Activity-only sources (spec 3): local burn in the Vault, never limits.</summary>
    public static readonly IReadOnlyList<string> ActivityOnly = [Antigravity];

    /// <summary>Every source whose local burn the Vault can hold, in ledger and footer order.</summary>
    public static readonly IReadOnlyList<string> Burn = [Claude, Codex, Antigravity];

    public static string Name(string id) => id switch
    {
        Claude => "Claude",
        Codex => "Codex",
        Antigravity => "Antigravity",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    /// <summary>The name a local-burn surface uses: Claude's burn is Claude Code's.</summary>
    public static string BurnName(string id) => id == Claude ? "Claude Code" : Name(id);

    /// <summary>The footer's short label ("CC +12K  ·  Codex +3K  ·  Agy +1K").</summary>
    public static string ShortName(string id) => id switch
    {
        Claude => "CC",
        Codex => "Codex",
        Antigravity => "Agy",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static string DefaultColor(string id) => id switch
    {
        Claude => "#E0875A",
        Codex => "#2FB5A3",
        Antigravity => "#7B8CF0",   // a periwinkle that evokes Google's blue without copying it (Store 10.1.4.4)
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };
```

Keep `Order` as it is (it ranks limits sources).

`VaultModels.cs`, in `VaultRootMeta` after `WalkVersion`:

```csharp
    /// <summary>Which agent's burn this root holds (spec 3): "codex" or "antigravity". Absent means a Claude Code
    /// home, so every vault written before spec 3 reads unchanged.</summary>
    [JsonPropertyName("agent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }
```

`VaultReader.cs`: append a parameter to the record (keeps every existing constructor call compiling):

```csharp
    int AgentCount,
    long AgentTokens,
    string Agent = AgentSources.Claude);
```

Add to `VaultReader`:

```csharp
    /// <summary>The agent a root holds, from its meta; a root without one is a Claude Code home.</summary>
    public string AgentOf(string root) => _store.LoadMeta(root)?.Agent ?? AgentSources.Claude;
```

In `ReadSessions`, at the top of `foreach (var root in roots)` add `var agent = AgentOf(root);`, and pass it as the last argument of `new VaultSessionInfo(...)` (after `agentCount, agentTokens`).

- [ ] **Step 4: Run the tests to verify they pass**

Run the Step 2 command. Expected: PASS, including the Antigravity color on every built-in theme. If a contrast case fails, adjust `#7B8CF0` in small steps (keep it a periwinkle), update the expected value in `AgentSourcesBurnTests` and the spec's section 4, and note the final hex in the commit message.

- [ ] **Step 5: Run the Core suite and commit**

Run: `dotnet test tests/Sanduhr.Tests` (expect all green).

```bash
git add windows-dotnet/src/Sanduhr.Core/AgentSources.cs windows-dotnet/src/Sanduhr.Core/VaultModels.cs windows-dotnet/src/Sanduhr.Core/VaultReader.cs windows-dotnet/tests/Sanduhr.Tests/AgentSourcesBurnTests.cs windows-dotnet/tests/Sanduhr.Tests/AgentSourceColorTests.cs windows-dotnet/tests/Sanduhr.Tests/VaultReaderTests.cs
git commit -m "feat(core): Antigravity as an activity-only source and the Vault's agent tag"
```

---

### Task 2: The line-parser seam in VaultIngester (no behavior change)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/BurnParsing.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs` (`ParseLines`, `ProcessLine`, the call sites in `IngestRoot`)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/ClaudeLineParserTests.cs`; every existing `VaultIngester*Tests` stays green unchanged

**Interfaces:**
- Produces:
  - `public readonly record struct BurnEvent(DateTimeOffset Timestamp, long Input, long Output, long CacheRead, long CacheCreation, string? Model, string? Cwd, string? Skill)`
  - `public interface IBurnLineParser { BurnEvent? Parse(string line, out bool bad); string? SaveState(); }`
  - `public sealed class ClaudeLineParser : IBurnLineParser`
  - `VaultIngester` private `long ParseLines(Stream s, long start, SessionAgg agg, IBurnLineParser parser)` and private `void Fold(BurnEvent e, SessionAgg agg)`.

- [ ] **Step 1: Write the failing parser tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class ClaudeLineParserTests
{
    private static BurnEvent? Parse(string line, out bool bad) => new ClaudeLineParser().Parse(line, out bad);

    [Fact]
    public void An_assistant_line_becomes_an_event_with_cache_model_cwd_and_skill()
    {
        var e = Parse("""{"type":"assistant","timestamp":"2026-11-10T10:00:00Z","cwd":"C:\\p\\api","attributionSkill":"s","message":{"model":"m","usage":{"input_tokens":100,"output_tokens":50,"cache_read_input_tokens":7,"cache_creation_input_tokens":3}}}""", out var bad);
        Assert.False(bad);
        Assert.NotNull(e);
        Assert.Equal((100L, 50L, 7L, 3L), (e!.Value.Input, e.Value.Output, e.Value.CacheRead, e.Value.CacheCreation));
        Assert.Equal(("m", @"C:\p\api", "s"), (e.Value.Model, e.Value.Cwd, e.Value.Skill));
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T10:00:00Z"), e.Value.Timestamp);
    }

    [Theory]
    [InlineData("""{"type":"user","message":"assistant"}""")]
    [InlineData("""{"type":"assistant","message":{"model":"m"}}""")]
    [InlineData("""{"type":"assistant","message":{"usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"summary"}""")]
    public void Lines_without_a_timed_usage_carry_no_event_and_are_not_bad(string line)
    {
        Assert.Null(Parse(line, out var bad));
        Assert.False(bad);
    }

    [Theory]
    [InlineData("""{"type":"assistant", broken""")]
    [InlineData("""["assistant"]""")]
    public void Assistant_looking_lines_that_do_not_parse_are_bad(string line)
    {
        Assert.Null(Parse(line, out var bad));
        Assert.True(bad);
    }

    [Fact]
    public void Claude_parsing_keeps_no_state() => Assert.Null(new ClaudeLineParser().SaveState());
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeLineParserTests"`
Expected: build error, `ClaudeLineParser` not found.

- [ ] **Step 3: Implement `BurnParsing.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One token reading from any agent's log (spec 3). Cache counts are kept apart from Input: Input is fresh
/// input only, so Input + Output is the total every Vault row sums.</summary>
public readonly record struct BurnEvent(
    DateTimeOffset Timestamp, long Input, long Output, long CacheRead, long CacheCreation,
    string? Model, string? Cwd, string? Skill);

/// <summary>Turns one log line into a <see cref="BurnEvent"/>. One instance reads one file, in order.</summary>
public interface IBurnLineParser
{
    /// <summary>Parse one trimmed, non-empty line. Null when the line carries no event. <paramref name="bad"/> is true for a
    /// line that should count in the row's skipped_lines (it looked like an event and did not parse).</summary>
    BurnEvent? Parse(string line, out bool bad);

    /// <summary>State to resume mid-file from the checkpoint, or null for a stateless parser.</summary>
    string? SaveState();
}

/// <summary>Claude Code transcript lines, exactly as VaultIngester read them before spec 3.</summary>
public sealed class ClaudeLineParser : IBurnLineParser
{
    public BurnEvent? Parse(string line, out bool bad)
    {
        bad = false;
        // Perf prefilter only: looser than "type":"assistant" on purpose so formatting drift can never silently drop events.
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal))
            return null;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { bad = true; return null; }
        if (node is not JsonObject d) { bad = true; return null; }
        if (BurnJson.Str(d, "type") != "assistant")
            return null;
        if (d["message"] is not JsonObject msg || msg["usage"] is not JsonObject usage)
            return null;
        if (BurnJson.ParseIso(BurnJson.Str(d, "timestamp")) is not { } ts)
            return null;   // matches the live reader: untimestamped events don't count
        return new BurnEvent(ts,
            BurnJson.Num(usage, "input_tokens"), BurnJson.Num(usage, "output_tokens"),
            BurnJson.Num(usage, "cache_read_input_tokens"), BurnJson.Num(usage, "cache_creation_input_tokens"),
            BurnJson.Str(msg, "model"), BurnJson.Str(d, "cwd"), BurnJson.Str(d, "attributionSkill"));
    }

    public string? SaveState() => null;
}

/// <summary>JSON helpers the burn parsers share.</summary>
internal static class BurnJson
{
    public static string? Str(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static long Num(JsonObject? o, string key)
    {
        if (o?[key] is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return 0;
    }

    public static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return DateTimeOffset.TryParse(s.Replace("Z", "+00:00"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }
}
```

- [ ] **Step 4: Route VaultIngester through the parser**

In `VaultIngester.cs`:

1. Replace `ProcessLine` with these two methods:

```csharp
    private void ProcessLine(string rawLine, SessionAgg agg, IBurnLineParser parser)
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
            return;
        var ev = parser.Parse(line, out bool bad);
        if (bad)
            agg.SkippedLines++;
        if (ev is { } e)
            Fold(e, agg);
    }

    /// <summary>Adds one event to the session. Cache counts land even for a zero-token event (as before spec 3);
    /// everything else needs input + output above zero.</summary>
    private void Fold(BurnEvent e, SessionAgg agg)
    {
        agg.CacheRead += e.CacheRead;
        agg.CacheCreation += e.CacheCreation;
        long tokens = e.Input + e.Output;
        if (tokens <= 0)
            return;   // matches AggregateForLocalCcTab's tokens<=0 skip

        agg.Cwd ??= e.Cwd;
        var model = string.IsNullOrEmpty(e.Model) ? "<none>" : e.Model;   // conservation: sum(by_model) == total
        var ts = e.Timestamp;
        if (agg.FirstTs is null || ts < agg.FirstTs) agg.FirstTs = ts;
        if (agg.LastTs is null || ts > agg.LastTs) agg.LastTs = ts;
        agg.EventCount++;

        var day = LocalDate(ts);
        if (!agg.ByDay.TryGetValue(day, out var bucket))
            agg.ByDay[day] = bucket = new DayAgg();
        bucket.Total += tokens;
        bucket.Input += e.Input;
        bucket.Output += e.Output;
        bucket.ByModel[model] = bucket.ByModel.GetValueOrDefault(model) + tokens;
        if (!string.IsNullOrEmpty(e.Skill))
            bucket.BySkill[e.Skill] = bucket.BySkill.GetValueOrDefault(e.Skill) + tokens;
    }
```

2. Change `ParseLines` to take any stream and a parser. Only the signature, the seek and the `ProcessLine` call change:

```csharp
    private long ParseLines(Stream s, long start, SessionAgg agg, IBurnLineParser parser)
    {
        if (start > 0)
            s.Seek(start, SeekOrigin.Begin);   // only seekable (plain) files ever resume mid-file
        ...
        while ((read = s.Read(buf, 0, buf.Length)) > 0)
        ...
                ProcessLine(line, agg, parser);
```

3. In `IngestRoot`, the two calls become `ParseLines(fs, cp.Offset, agg, new ClaudeLineParser())` and `ParseLines(fs, 0, agg, new ClaudeLineParser())`.

4. Delete the now-unused private `Str` and `Num` helpers at the bottom of `VaultIngester` only if nothing else in the file uses them (`ParseIso` is still used by `SeedFromStored` and pruning; keep it).

- [ ] **Step 5: Run the parser tests and the whole Vault battery**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~ClaudeLineParserTests|FullyQualifiedName~Vault"`
Expected: PASS, with no change to any existing Vault test. A failing existing test means behavior drifted: fix the code, never the test.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/BurnParsing.cs windows-dotnet/src/Sanduhr.Core/VaultIngester.cs windows-dotnet/tests/Sanduhr.Tests/ClaudeLineParserTests.cs
git commit -m "refactor(core): Vault ingest reads lines through a burn parser seam"
```

---

### Task 3: Agent roots in the ingester

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentBurnSource.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (`VaultCheckpointEntry`)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultAgentIngestTests.cs`

**Interfaces:**
- Consumes: `BurnEvent`, `IBurnLineParser` (Task 2); `VaultRootMeta.Agent` (Task 1).
- Produces:
  - `public sealed record AgentBurnFile(FileInfo File, string SessionId, string? Cwd, bool Compressed)`
  - `public interface IAgentBurnSource { string Agent { get; } IReadOnlyList<AgentBurnFile> Discover(); Stream Open(AgentBurnFile file); IBurnLineParser NewParser(string? state); }`
  - `public sealed class CappedReadStream : Stream` (`new CappedReadStream(Stream inner, long maxBytes)`; throws `InvalidDataException` past the cap)
  - `VaultIngester.IngestOnce(IReadOnlyList<string> consentedRootNames, bool storeFullPaths, DateTimeOffset nowUtc, Func<string, bool>? stillConsented = null, IReadOnlyList<IAgentBurnSource>? agents = null)`; an agent's root name is `source.Agent`.
  - `VaultCheckpointEntry.ParserState` (`string?`, JSON `parser_state`).

- [ ] **Step 1: Write the failing tests**

`VaultAgentIngestTests.cs` drives the ingester with a fake source whose parser reads `ts,input,output` lines and keeps a running line count as its state:

```csharp
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class VaultAgentIngestTests
{
    private static readonly TimeZoneInfo Cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+00:00");

    /// <summary>Lines "2026-11-10T10:00:00Z,100,50"; state = how many events this file has produced so far.</summary>
    private sealed class CsvParser(string? state) : IBurnLineParser
    {
        public int Seen = state is null ? 0 : int.Parse(state, CultureInfo.InvariantCulture);
        public BurnEvent? Parse(string line, out bool bad)
        {
            bad = false;
            var p = line.Split(',');
            if (p.Length != 3) { bad = true; return null; }
            Seen++;
            return new BurnEvent(DateTimeOffset.Parse(p[0], CultureInfo.InvariantCulture), long.Parse(p[1], CultureInfo.InvariantCulture),
                long.Parse(p[2], CultureInfo.InvariantCulture), 0, 0, "fake-model", null, null);
        }
        public string? SaveState() => Seen.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class FakeSource(string agent, Func<IReadOnlyList<AgentBurnFile>> files) : IAgentBurnSource
    {
        public readonly List<string?> StatesHandedOut = [];
        public string Agent => agent;
        public IReadOnlyList<AgentBurnFile> Discover() => files();
        public Stream Open(AgentBurnFile f)
        {
            var fs = new FileStream(f.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return f.Compressed ? new GZipStream(fs, CompressionMode.Decompress) : fs;
        }
        public IBurnLineParser NewParser(string? state) { StatesHandedOut.Add(state); return new CsvParser(state); }
    }

    private static string Write(string dir, string name, string body, bool gzip = false)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        if (gzip)
        {
            using var f = File.Create(path);
            using var gz = new GZipStream(f, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(body));
        }
        else File.WriteAllText(path, body);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    private static (VaultIngester, VaultStore) Make(string home, string vault)
    {
        var store = new VaultStore(vault);
        return (new VaultIngester(home, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N")), store);
    }

    [Fact]
    public void An_agent_source_writes_its_own_root_tagged_with_the_agent()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,100,50\n2026-11-11T16:00:00Z,10,5\n");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "sess-a", @"C:\Users\x\Projects\api", false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        var r = ing.IngestOnce([], false, Now, agents: [src]);

        Assert.True(r.Acquired);
        Assert.Equal("codex", store.LoadMeta("codex")!.Agent);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard("codex", "2026-11", out var shard));
        var row = shard.Sessions["sess-a"];
        Assert.Equal(165, row.Total);
        Assert.Equal("api", row.ProjectName);
        Assert.Equal(new[] { "2026-11-10", "2026-11-11" }, row.ByDay.Keys.OrderBy(k => k));
    }

    [Fact]
    public void A_grown_plain_file_resumes_with_the_saved_parser_state()
    {
        using var t = new TempDir();
        var dir = Path.Combine(t.Path, "in");
        var file = Write(dir, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "sess-a", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);

        File.AppendAllText(file, "2026-11-10T17:00:00Z,1,1\n");
        File.SetLastWriteTimeUtc(file, Now.AddMinutes(-5).UtcDateTime);
        ing.IngestOnce([], false, Now, agents: [src]);

        Assert.Equal(new string?[] { null, "1" }, src.StatesHandedOut);   // the tail pass got the first pass's state
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.Equal(152, shard.Sessions["sess-a"].Total);
    }

    [Fact]
    public void A_torn_last_line_is_left_for_the_next_pass()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,100,50\n2026-11-10T16:30:00Z,9");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "sess-a", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("codex", "2026-11", out var first);
        Assert.Equal(150, first.Sessions["sess-a"].Total);

        File.AppendAllText(file, ",1\n");
        File.SetLastWriteTimeUtc(file, Now.AddMinutes(-5).UtcDateTime);
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("codex", "2026-11", out var second);
        Assert.Equal(160, second.Sessions["sess-a"].Total);
    }

    [Fact]
    public void A_compressed_file_is_parsed_whole_and_replaces_its_plain_twin_row()
    {
        using var t = new TempDir();
        var dir = Path.Combine(t.Path, "in");
        const string body = "2026-11-10T16:00:00Z,100,50\n";
        var plain = Write(dir, "a.jsonl", body);
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(plain), "sess-a", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);

        var zst = Write(dir, "a.jsonl.zst", body, gzip: true);
        var both = new FakeSource("codex", () =>
            [new AgentBurnFile(new FileInfo(plain), "sess-a", null, false), new AgentBurnFile(new FileInfo(zst), "sess-a", null, true)]);
        ing.IngestOnce([], false, Now, agents: [both]);
        ing.IngestOnce([], false, Now, agents: [both]);

        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.Equal(150, shard.Sessions["sess-a"].Total);   // replaced, never added to
    }

    [Fact]
    public void A_bad_compressed_file_fails_alone_and_the_other_files_still_land()
    {
        using var t = new TempDir();
        var dir = Path.Combine(t.Path, "in");
        var bad = Write(dir, "bad.jsonl.zst", "not gzip at all");
        var good = Write(dir, "good.jsonl", "2026-11-10T16:00:00Z,1,1\n");
        var src = new FakeSource("codex", () =>
            [new AgentBurnFile(new FileInfo(bad), "sess-bad", null, true), new AgentBurnFile(new FileInfo(good), "sess-good", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        var r = ing.IngestOnce([], false, Now, agents: [src]);

        Assert.Equal(1, r.FilesFailed);
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.True(shard.Sessions.ContainsKey("sess-good"));
        Assert.False(shard.Sessions.ContainsKey("sess-bad"));
    }

    [Fact]
    public void A_source_that_throws_on_discovery_skips_only_its_own_root()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,1,1\n");
        var broken = new FakeSource("antigravity", () => throw new IOException("locked"));
        var ok = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "s", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        var r = ing.IngestOnce([], false, Now, agents: [broken, ok]);

        Assert.Equal(1, r.RootsAborted);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard("codex", "2026-11", out _));
    }

    [Fact]
    public void An_agent_root_revoked_mid_cycle_writes_nothing()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,1,1\n");
        var src = new FakeSource("codex", () => [new AgentBurnFile(new FileInfo(file), "s", null, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));

        ing.IngestOnce([], false, Now, stillConsented: root => root != "codex", agents: [src]);

        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard("codex", "2026-11", out _));
    }

    [Fact]
    public void A_project_that_appears_later_moves_the_whole_session()
    {
        using var t = new TempDir();
        var file = Write(Path.Combine(t.Path, "in"), "a.jsonl", "2026-11-10T16:00:00Z,100,50\n");
        string? cwd = null;
        var src = new FakeSource("antigravity", () => [new AgentBurnFile(new FileInfo(file), "conv", cwd, false)]);
        var (ing, store) = Make(t.Path, Path.Combine(t.Path, "vault"));
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("antigravity", "2026-11", out var before);
        Assert.Equal("(none)", before.Sessions["conv"].ProjectName);

        cwd = @"C:\Users\x\Projects\web";
        File.AppendAllText(file, "2026-11-10T17:00:00Z,1,1\n");
        File.SetLastWriteTimeUtc(file, Now.AddMinutes(-5).UtcDateTime);
        ing.IngestOnce([], false, Now, agents: [src]);
        store.TryLoadSessionShard("antigravity", "2026-11", out var after);
        Assert.Equal("web", after.Sessions["conv"].ProjectName);
        Assert.Equal(152, after.Sessions["conv"].Total);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~VaultAgentIngestTests"`
Expected: build errors (`AgentBurnFile`, `IAgentBurnSource`, the `agents` parameter).

- [ ] **Step 3: Create `AgentBurnSource.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>One log file an agent source hands the Vault. <paramref name="SessionId"/> keys the Vault row (two files with
/// the same id write the same row, replacing it). <paramref name="Cwd"/> is the project when the source knows it up
/// front (Antigravity); null lets the parser's events name it (Codex). A compressed file is never resumed mid-file.</summary>
public sealed record AgentBurnFile(FileInfo File, string SessionId, string? Cwd, bool Compressed);

/// <summary>An agent whose local burn the Vault holds (spec 3). Its root folder in the Vault is <see cref="Agent"/>.</summary>
public interface IAgentBurnSource
{
    string Agent { get; }

    /// <summary>The files to read now. May throw; the ingester then skips this agent's root for the cycle.</summary>
    IReadOnlyList<AgentBurnFile> Discover();

    /// <summary>A readable stream of the file's lines (decompressed for a compressed file). May throw
    /// <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> or <see cref="InvalidDataException"/>.</summary>
    Stream Open(AgentBurnFile file);

    /// <summary>A parser for one file, resumed from a checkpoint's state (null for a fresh read).</summary>
    IBurnLineParser NewParser(string? state);
}

/// <summary>Reads at most <c>maxBytes</c> from an inner stream, then throws <see cref="InvalidDataException"/>: a
/// decompression bomb or an oversized rollout fails that one file instead of the cycle.</summary>
public sealed class CappedReadStream(Stream inner, long maxBytes) : Stream
{
    private long _read;

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = inner.Read(buffer, offset, count);
        _read += n;
        if (_read > maxBytes)
            throw new InvalidDataException("decompressed size over the cap");
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 4: Add the checkpoint's parser state**

In `VaultCheckpointEntry`, after `LastSeenTs`:

```csharp
    /// <summary>The line parser's state after <see cref="Offset"/> (spec 3: Codex's segment, model, cwd and last
    /// total). Null for Claude Code files and other stateless parsers.</summary>
    [JsonPropertyName("parser_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParserState { get; set; }
```

- [ ] **Step 5: Generalize the per-root loop**

In `VaultIngester.cs`:

1. Add a private walk item above `IngestRoot`:

```csharp
    /// <summary>One file of a root's walk: a Claude Code transcript or an agent source's file.</summary>
    private sealed record WalkItem(
        FileInfo File, string Uuid, string? ParentSession, string? Cwd, bool Compressed,
        Func<Stream> Open, Func<string?, IBurnLineParser> Parser);
```

2. Split `IngestRoot(rootName, storeFullPaths, nowUtc, stillConsented)` into a Claude walk plus a shared loop. The Claude walk keeps the existing walk-version gate and file enumeration, then builds items:

```csharp
    private VaultIngestResult IngestClaudeRoot(
        string rootName, bool storeFullPaths, DateTimeOffset nowUtc, Func<string, bool>? stillConsented)
    {
        var existingMeta = _store.LoadMeta(rootName);
        if ((existingMeta?.WalkVersion ?? 0) < CurrentWalkVersion)
        {
            _store.DeleteCheckpoints(rootName);
            Log("walk upgraded — checkpoints invalidated for re-ingest");
        }
        var files = new List<(FileInfo File, string ProjectDir)>();
        var projects = new DirectoryInfo(Path.Combine(_homeDir, rootName, "projects"));
        if (projects.Exists)
        {
            foreach (var projectDir in projects.EnumerateDirectories())
                foreach (var fi in projectDir.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                    files.Add((fi, projectDir.FullName));
        }
        var items = files.Select(f => new WalkItem(
            f.File, Path.GetFileNameWithoutExtension(f.File.Name), ParentSessionOf(f.File.FullName, f.ProjectDir), null, false,
            () => new FileStream(f.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            _ => new ClaudeLineParser())).ToList();
        return IngestRoot(rootName, items, agent: null, storeFullPaths, nowUtc, stillConsented);
    }

    private VaultIngestResult IngestAgentRoot(
        IAgentBurnSource source, bool storeFullPaths, DateTimeOffset nowUtc, Func<string, bool>? stillConsented)
    {
        var items = source.Discover().Select(f => new WalkItem(
            f.File, f.SessionId, null, f.Cwd, f.Compressed, () => source.Open(f), source.NewParser)).ToList();
        return IngestRoot(source.Agent, items, source.Agent, storeFullPaths, nowUtc, stillConsented);
    }
```

3. The shared loop is the existing `IngestRoot` body from `var checkpoints = ...` to the end, with the signature
`private VaultIngestResult IngestRoot(string rootName, List<WalkItem> items, string? agent, bool storeFullPaths, DateTimeOffset nowUtc, Func<string, bool>? stillConsented)` and these changes:

```csharp
        items.Sort((a, b) => a.File.LastWriteTimeUtc.CompareTo(b.File.LastWriteTimeUtc));
        foreach (var item in items)
        {
            var fi = item.File;
            seen++;
            var key = VaultStore.PathKey(fi.FullName);
            long statLen = fi.Length;
            long statMtime = fi.LastWriteTimeUtc.Ticks;
            checkpoints.Entries.TryGetValue(key, out var cp);
            var uuid = item.Uuid;
            var parentSession = item.ParentSession;
            // ... unchanged/quiesced/skip logic unchanged ...
            bool wantTail = !item.Compressed && !unchanged && cp is not null && !cp.Sealed
                && statLen > cp.Length && cp.Offset > 0 && cp.TailGuard.Length > 0;
            // ... seed logic unchanged ...
            IBurnLineParser parser;
            try
            {
                using var s = item.Open();
                if (seed is not null && s is FileStream fs && fs.Length >= cp!.Offset
                    && GuardAt(fs, cp.Offset) == cp.TailGuard)
                {
                    agg = seed;
                    parser = item.Parser(cp.ParserState);
                    if (item.Cwd is not null) agg.Cwd = item.Cwd;   // a project learned since the last pass moves the session
                    endOffset = ParseLines(fs, cp.Offset, agg, parser);
                    usedTail = true;
                }
                else
                {
                    agg = new SessionAgg { Cwd = item.Cwd };
                    parser = item.Parser(null);
                    endOffset = ParseLines(s, 0, agg, parser);
                }
                tailGuard = s is FileStream seekable ? GuardAt(seekable, endOffset) : "";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                if (cp is not null)
                    cp.LastSeenTs = nowIso;
                Log2("file-open", e);
                failed++;
                continue;
            }
```

In the new checkpoint entry add `ParserState = parser.SaveState(),`. Replace `UpdateMeta(rootName, nowUtc, nowIso);` with `UpdateMeta(rootName, nowUtc, nowIso, agent);` and give `UpdateMeta` a `string? agent` parameter that sets `if (agent is not null) meta.Agent = agent;` before saving.

`GuardAt` keeps its `FileStream` parameter. `InvalidDataException` lives in `System.IO`, already imported implicitly.

4. `IngestOnce` gains the optional parameter and loops agents after Claude roots, each isolated:

```csharp
    public VaultIngestResult IngestOnce(
        IReadOnlyList<string> consentedRootNames, bool storeFullPaths, DateTimeOffset nowUtc,
        Func<string, bool>? stillConsented = null, IReadOnlyList<IAgentBurnSource>? agents = null)
    {
        // ... mutex acquisition unchanged ...
            foreach (var rootName in consentedRootNames)
            {
                var r = IngestClaudeRoot(rootName, storeFullPaths, nowUtc, stillConsented);
                seen += r.FilesSeen; full += r.FilesFullParsed; tail += r.FilesTailParsed;
                skipped += r.FilesSkipped; failed += r.FilesFailed; aborted += r.RootsAborted;
            }
            foreach (var source in agents ?? [])
            {
                VaultIngestResult r;
                try
                {
                    r = IngestAgentRoot(source, storeFullPaths, nowUtc, stillConsented);
                }
                catch (Exception e)
                {
                    // One agent failing never stops another agent or Claude Code (spec 3, section 5).
                    Log2("agent-root", e);
                    r = new VaultIngestResult(true, 0, 0, 0, 0, 0, 1);
                }
                seen += r.FilesSeen; full += r.FilesFullParsed; tail += r.FilesTailParsed;
                skipped += r.FilesSkipped; failed += r.FilesFailed; aborted += r.RootsAborted;
            }
```

- [ ] **Step 6: Run the new tests and the whole Vault battery**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~Vault|FullyQualifiedName~ClaudeLineParserTests"`
Expected: PASS. Existing Claude tests unchanged.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/AgentBurnSource.cs windows-dotnet/src/Sanduhr.Core/VaultModels.cs windows-dotnet/src/Sanduhr.Core/VaultIngester.cs windows-dotnet/tests/Sanduhr.Tests/VaultAgentIngestTests.cs
git commit -m "feat(core): agent burn sources ingest into their own Vault roots"
```

---

### Task 4: The Codex burn source

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/CodexBurnSource.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/CodexBurnSourceTests.cs`

**Interfaces:**
- Consumes: `IAgentBurnSource`, `AgentBurnFile`, `CappedReadStream` (Task 3); `BurnEvent`, `IBurnLineParser`, `BurnJson` (Task 2); `CodexLimitsReader.MaxDecompressedBytes`.
- Produces: `public sealed class CodexBurnSource(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst) : IAgentBurnSource` (Agent `"codex"`), `public sealed class CodexLineParser : IBurnLineParser` (`new CodexLineParser(string? state)`), `public static string CodexBurnSource.SessionIdOf(string fileName)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO.Compression;
using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexBurnSourceTests
{
    private static readonly TimeZoneInfo Cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+00:00");

    private static Stream Gunzip(Stream s) => new GZipStream(s, CompressionMode.Decompress);   // stand-in for zstd: the source only sees Func<Stream, Stream>

    private static string Meta(string session, string cwd = @"C:\\Users\\x\\Projects\\api") =>
        $$"""{"timestamp":"2026-11-10T16:00:00Z","type":"session_meta","payload":{"session_id":"{{session}}","cwd":"{{cwd}}","thread_source":"user"}}""";
    private static string Turn(string model, string? cwd = null) =>
        cwd is null
            ? $$"""{"timestamp":"2026-11-10T16:00:01Z","type":"turn_context","payload":{"model":"{{model}}"}}"""
            : $$"""{"timestamp":"2026-11-10T16:00:01Z","type":"turn_context","payload":{"model":"{{model}}","cwd":"{{cwd}}"}}""";
    private static string Tokens(string ts, long input, long cached, long cacheWrite, long output, long lastInput = 0, long lastOutput = 0) =>
        $$"""{"timestamp":"{{ts}}","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":{{input}},"cached_input_tokens":{{cached}},"cache_write_input_tokens":{{cacheWrite}},"output_tokens":{{output}},"reasoning_output_tokens":0,"total_tokens":{{input + output}}},"last_token_usage":{"input_tokens":{{lastInput}},"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":{{lastOutput}},"reasoning_output_tokens":0,"total_tokens":{{lastInput + lastOutput}}}},"rate_limits":null}}""";
    private const string NullInfo = """{"timestamp":"2026-11-10T16:05:00Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":null}}""";

    private static string Rollout(string home, string name, string body, bool compressed = false)
    {
        var dir = Path.Combine(home, "sessions", "2026", "11", "10");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + (compressed ? ".jsonl.zst" : ".jsonl"));
        if (compressed)
        {
            using var f = File.Create(path);
            using var gz = new GZipStream(f, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(body));
        }
        else File.WriteAllText(path, body);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    private static VaultSessionRow Ingest(string home, string vault, string session = "rollout-a")
    {
        var store = new VaultStore(vault);
        var ing = new VaultIngester(home, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N"));
        ing.IngestOnce([], false, Now, agents: [new CodexBurnSource(() => [home], Gunzip)]);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard("codex", "2026-11", out var shard));
        return shard.Sessions[session];
    }

    [Theory]
    [InlineData("rollout-a.jsonl", "rollout-a")]
    [InlineData("rollout-a.jsonl.zst", "rollout-a")]
    public void Plain_and_compressed_names_share_a_session(string name, string id) =>
        Assert.Equal(id, CodexBurnSource.SessionIdOf(name));

    [Fact]
    public void Tokens_are_total_deltas_mapped_to_fresh_input_cache_and_output()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x"),
            Tokens("2026-11-10T16:01:00Z", input: 1000, cached: 600, cacheWrite: 50, output: 100),
            Tokens("2026-11-10T16:02:00Z", input: 1000, cached: 600, cacheWrite: 50, output: 100),   // a repeated reading counts zero
            Tokens("2026-11-10T16:03:00Z", input: 1500, cached: 900, cacheWrite: 50, output: 160)) + "\n");

        var row = Ingest(t.Path, Path.Combine(t.Path, "vault"));

        // fresh input = (1500 - 900) = 600, output = 160 -> total 760
        Assert.Equal(760, row.Total);
        Assert.Equal(900, row.CacheTokens!.Read);
        Assert.Equal(50, row.CacheTokens.Creation);
        Assert.Equal(760, row.ByModel["gpt-x"]);
        Assert.Equal(2, row.EventCount);
        Assert.Equal("api", row.ProjectName);
    }

    [Fact]
    public void A_total_that_drops_falls_back_to_the_last_reading()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x"),
            Tokens("2026-11-10T16:01:00Z", input: 1000, cached: 0, cacheWrite: 0, output: 100),
            Tokens("2026-11-10T16:02:00Z", input: 200, cached: 0, cacheWrite: 0, output: 20, lastInput: 200, lastOutput: 20)) + "\n");

        Assert.Equal(1100 + 220, Ingest(t.Path, Path.Combine(t.Path, "vault")).Total);
    }

    [Fact]
    public void A_new_segment_restarts_the_running_total_and_null_info_counts_nothing()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x"),
            Tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100),
            NullInfo,
            Meta("s2"), Turn("gpt-y"),
            Tokens("2026-11-10T16:04:00Z", 300, 0, 0, 30)) + "\n");

        var row = Ingest(t.Path, Path.Combine(t.Path, "vault"));

        Assert.Equal(1100 + 330, row.Total);
        Assert.Equal(1100, row.ByModel["gpt-x"]);
        Assert.Equal(330, row.ByModel["gpt-y"]);
    }

    [Fact]
    public void The_cwd_in_force_at_the_first_counted_event_names_the_project()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", string.Join("\n",
            Meta("s1"), Turn("gpt-x", @"C:\\Users\\x\\Projects\\web"),
            Tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)) + "\n");

        Assert.Equal("web", Ingest(t.Path, Path.Combine(t.Path, "vault")).ProjectName);
    }

    [Fact]
    public void A_grown_rollout_resumes_mid_segment_without_recounting()
    {
        using var t = new TempDir();
        var path = Rollout(t.Path, "rollout-a", string.Join("\n", Meta("s1"), Turn("gpt-x"), Tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100)) + "\n");
        var vault = Path.Combine(t.Path, "vault");
        Ingest(t.Path, vault);

        File.AppendAllText(path, Tokens("2026-11-10T16:02:00Z", 1200, 0, 0, 120) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);

        Assert.Equal(1320, Ingest(t.Path, vault).Total);   // 1100 + delta 220, not 1100 + 1320
    }

    [Fact]
    public void A_compressed_rollout_reads_like_its_plain_original()
    {
        using var t = new TempDir();
        var body = string.Join("\n", Meta("s1"), Turn("gpt-x"), Tokens("2026-11-10T16:01:00Z", 1000, 400, 0, 100)) + "\n";
        Rollout(t.Path, "rollout-a", body, compressed: true);

        Assert.Equal(700, Ingest(t.Path, Path.Combine(t.Path, "vault")).Total);
    }

    [Fact]
    public void Without_a_decompressor_compressed_rollouts_are_not_listed()
    {
        using var t = new TempDir();
        Rollout(t.Path, "rollout-a", "x\n", compressed: true);
        Assert.Empty(new CodexBurnSource(() => [t.Path], null).Discover());
    }

    [Fact]
    public void A_rollout_over_the_cap_fails_alone()
    {
        using var t = new TempDir();
        var src = new CodexBurnSource(() => [t.Path], s => new CappedReadStream(Gunzip(s), 10));
        Rollout(t.Path, "rollout-big", string.Join("\n", Meta("s1"), Tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)) + "\n", compressed: true);
        Rollout(t.Path, "rollout-ok", string.Join("\n", Meta("s2"), Tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)) + "\n");
        var store = new VaultStore(Path.Combine(t.Path, "vault"));
        var ing = new VaultIngester(t.Path, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N"));

        var r = ing.IngestOnce([], false, Now, agents: [src]);

        Assert.Equal(1, r.FilesFailed);
        store.TryLoadSessionShard("codex", "2026-11", out var shard);
        Assert.True(shard.Sessions.ContainsKey("rollout-ok"));
    }

    [Fact]
    public void The_parser_never_reads_message_text()
    {
        var p = new CodexLineParser(null);
        Assert.Null(p.Parse("""{"timestamp":"2026-11-10T16:00:00Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"secret"}]}}""", out var bad));
        Assert.False(bad);
        Assert.DoesNotContain("secret", p.SaveState() ?? "");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~CodexBurnSourceTests"`
Expected: build error, `CodexBurnSource` not found.

- [ ] **Step 3: Implement `CodexBurnSource.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>
/// Codex local burn (spec 3). Walks <c>sessions/**/rollout-*.jsonl</c> and <c>.jsonl.zst</c> under every Codex home. One
/// rollout is one Vault session, keyed by its name without extensions, so a rollout Codex compresses later writes the same
/// row. Reads only timestamps, segment ids, cwd, model names and token counts; never message text or account ids.
/// </summary>
public sealed class CodexBurnSource(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst) : IAgentBurnSource
{
    private const string Plain = ".jsonl";
    private const string Zst = ".jsonl.zst";

    public string Agent => AgentSources.Codex;

    public static string SessionIdOf(string fileName) =>
        fileName.EndsWith(Zst, StringComparison.OrdinalIgnoreCase) ? fileName[..^Zst.Length]
        : fileName.EndsWith(Plain, StringComparison.OrdinalIgnoreCase) ? fileName[..^Plain.Length]
        : fileName;

    public IReadOnlyList<AgentBurnFile> Discover()
    {
        var files = new List<AgentBurnFile>();
        foreach (var home in homes())
        {
            var sessions = new DirectoryInfo(Path.Combine(home, "sessions"));
            if (!sessions.Exists) continue;
            foreach (var fi in sessions.EnumerateFiles("rollout-*", SearchOption.AllDirectories))
            {
                bool zst = fi.Name.EndsWith(Zst, StringComparison.OrdinalIgnoreCase);
                bool plain = fi.Name.EndsWith(Plain, StringComparison.OrdinalIgnoreCase);
                if (!plain && !zst) continue;
                if (zst && decompressZst is null) continue;   // left unread until a decompressor is available
                files.Add(new AgentBurnFile(fi, SessionIdOf(fi.Name), null, zst));
            }
        }
        return files;
    }

    public Stream Open(AgentBurnFile file)
    {
        var fs = new FileStream(file.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!file.Compressed) return fs;
        try { return new CappedReadStream(decompressZst!(fs), CodexLimitsReader.MaxDecompressedBytes); }
        catch { fs.Dispose(); throw; }
    }

    public IBurnLineParser NewParser(string? state) => new CodexLineParser(state);
}

/// <summary>
/// Codex rollout lines. <c>session_meta</c> opens a segment and resets the running total; <c>turn_context</c> sets the
/// model and cwd in force; a <c>token_count</c> event counts the change in <c>info.total_token_usage</c> since the
/// segment's previous reading (a repeated reading counts zero; a total that drops counts <c>last_token_usage</c>).
/// Fresh input is <c>input_tokens - cached_input_tokens</c>; output already includes reasoning.
/// </summary>
public sealed class CodexLineParser : IBurnLineParser
{
    private State _s;

    public CodexLineParser(string? state)
    {
        _s = new State();
        if (state is null) return;
        try { _s = JsonSerializer.Deserialize<State>(state) ?? new State(); }
        catch (JsonException) { _s = new State(); }   // an unreadable state restarts the segment total, never crashes
    }

    public BurnEvent? Parse(string line, out bool bad)
    {
        bad = false;
        if (!line.Contains("\"session_meta\"", StringComparison.Ordinal)
            && !line.Contains("\"turn_context\"", StringComparison.Ordinal)
            && !line.Contains("\"token_count\"", StringComparison.Ordinal))
            return null;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { bad = true; return null; }
        if (node is not JsonObject d || d["payload"] is not JsonObject p) { bad = true; return null; }

        switch (BurnJson.Str(d, "type"))
        {
            case "session_meta":
                _s.Segment = BurnJson.Str(p, "session_id") ?? BurnJson.Str(p, "id");
                _s.Cwd = BurnJson.Str(p, "cwd") ?? _s.Cwd;
                _s.HasTotal = false;
                _s.Last = new Usage();
                return null;
            case "turn_context":
                _s.Model = BurnJson.Str(p, "model") ?? _s.Model;
                _s.Cwd = BurnJson.Str(p, "cwd") ?? _s.Cwd;
                return null;
            case "event_msg" when BurnJson.Str(p, "type") == "token_count":
                return Count(d, p);
            default:
                return null;
        }
    }

    private BurnEvent? Count(JsonObject d, JsonObject p)
    {
        if (p["info"] is not JsonObject info) return null;
        if (BurnJson.ParseIso(BurnJson.Str(d, "timestamp")) is not { } ts) return null;
        var total = info["total_token_usage"] as JsonObject;
        var last = info["last_token_usage"] as JsonObject;

        Usage u;
        if (total is not null)
        {
            var t = Usage.Of(total);
            u = !_s.HasTotal ? t
                : t.Total >= _s.Last.Total ? t.Minus(_s.Last)
                : last is not null ? Usage.Of(last) : t;
            _s.Last = t;
            _s.HasTotal = true;
        }
        else if (last is not null) u = Usage.Of(last);
        else return null;

        if (u.Input == 0 && u.Output == 0 && u.Cached == 0 && u.CacheWrite == 0) return null;
        return new BurnEvent(ts, Math.Max(0, u.Input - u.Cached), u.Output, u.Cached, u.CacheWrite, _s.Model, _s.Cwd, null);
    }

    public string? SaveState() => JsonSerializer.Serialize(_s);

    private sealed class State
    {
        [JsonPropertyName("segment")] public string? Segment { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("cwd")] public string? Cwd { get; set; }
        [JsonPropertyName("has_total")] public bool HasTotal { get; set; }
        [JsonPropertyName("last")] public Usage Last { get; set; } = new();
    }

    private sealed class Usage
    {
        [JsonPropertyName("in")] public long Input { get; set; }
        [JsonPropertyName("cached")] public long Cached { get; set; }
        [JsonPropertyName("cache_write")] public long CacheWrite { get; set; }
        [JsonPropertyName("out")] public long Output { get; set; }
        [JsonPropertyName("total")] public long Total { get; set; }

        public static Usage Of(JsonObject o) => new()
        {
            Input = BurnJson.Num(o, "input_tokens"),
            Cached = BurnJson.Num(o, "cached_input_tokens"),
            CacheWrite = BurnJson.Num(o, "cache_write_input_tokens"),
            Output = BurnJson.Num(o, "output_tokens"),
            Total = BurnJson.Num(o, "total_tokens"),
        };

        public Usage Minus(Usage b) => new()
        {
            Input = Math.Max(0, Input - b.Input),
            Cached = Math.Max(0, Cached - b.Cached),
            CacheWrite = Math.Max(0, CacheWrite - b.CacheWrite),
            Output = Math.Max(0, Output - b.Output),
            Total = Math.Max(0, Total - b.Total),
        };
    }
}
```

The Codex cwd lives in the parser state and rides on each event. The ingester's `Fold` keeps the cwd of the session's first counted event (`agg.Cwd ??= e.Cwd`), the spec's first-wins rule.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~CodexBurnSourceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/CodexBurnSource.cs windows-dotnet/tests/Sanduhr.Tests/CodexBurnSourceTests.cs
git commit -m "feat(core): Codex burn source reads rollout token totals into the Vault"
```

---

### Task 5: The Antigravity CLI burn source

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AntigravityBurnSource.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/AntigravityBurnSourceTests.cs`

**Interfaces:**
- Consumes: Task 2 and Task 3 types.
- Produces: `public sealed class AntigravityBurnSource(string cliHome, Func<IReadOnlyDictionary<string, string?>?> workspaces) : IAgentBurnSource` (Agent `"antigravity"`). `workspaces()` maps `conversation_id` to the raw `workspace_uris` JSON text; null means the database could not be read this pass. Also `public static string AntigravityBurnSource.DefaultCliHome(string userProfile)`, `public static string? AntigravityBurnSource.ProjectPathOf(string? workspaceUrisJson)`, `public static string AntigravityBurnSource.SummariesPath(string cliHome)`, the nested `public sealed class AntigravityBurnSource.Parser : IBurnLineParser`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AntigravityBurnSourceTests
{
    private static readonly TimeZoneInfo Cst = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+00:00");

    private static string Turn(string ts, long input, long output, long cacheRead) =>
        $$"""{"type":"PLANNER_RESPONSE","created_at":"{{ts}}","step_index":1,"input_tokens":{{input}},"output_tokens":{{output}},"cache_read_tokens":{{cacheRead}},"content":"never read"}""";
    private const string UserLine = """{"type":"USER_INPUT","created_at":"2026-11-10T16:00:00Z","step_index":0,"content":"never read"}""";

    private static string Transcript(string home, string conversation, string body, string name = "transcript.jsonl")
    {
        var dir = Path.Combine(home, "brain", conversation, ".system_generated", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, body);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);
        return path;
    }

    private static (VaultIngester, VaultStore) Make(string home)
    {
        var store = new VaultStore(Path.Combine(home, "vault"));
        return (new VaultIngester(home, store, "test", null, Cst, "SanduhrTest.Vault." + Guid.NewGuid().ToString("N")), store);
    }

    private const string Api = """["file:///c%3A/Users/x/Projects/api"]""";

    [Theory]
    [InlineData("""["file:///c%3A/Users/x/Projects/api"]""", @"c:\Users\x\Projects\api")]
    [InlineData("""["file:///C:/Users/x/My%20App","file:///C:/other"]""", @"C:\Users\x\My App")]
    [InlineData("[]", null)]
    [InlineData(null, null)]
    [InlineData("not json", null)]
    [InlineData("""["https://example.com/x"]""", null)]
    public void Project_path_is_the_first_file_uri(string? json, string? expected) =>
        Assert.Equal(expected, AntigravityBurnSource.ProjectPathOf(json));

    [Fact]
    public void Turns_are_dated_one_by_one_and_land_in_their_project()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", string.Join("\n", UserLine,
            Turn("2026-11-10T16:01:00Z", 1000, 100, 5000),
            Turn("2026-11-11T16:01:00Z", 500, 50, 2000)) + "\n");
        var src = new AntigravityBurnSource(t.Path, () => new Dictionary<string, string?> { ["conv-1"] = Api });
        var (ing, store) = Make(t.Path);

        ing.IngestOnce([], false, Now, agents: [src]);

        store.TryLoadSessionShard("antigravity", "2026-11", out var shard);
        var row = shard.Sessions["conv-1"];
        Assert.Equal(1650, row.Total);
        Assert.Equal(7000, row.CacheTokens!.Read);
        Assert.Equal(1100, row.ByDay["2026-11-10"].Total);
        Assert.Equal(550, row.ByDay["2026-11-11"].Total);
        Assert.Equal("api", row.ProjectName);
        Assert.Equal(1650, row.ByModel["antigravity"]);
        Assert.Equal(0, row.SkippedLines);   // the user line is not bad
    }

    [Fact]
    public void Transcript_full_is_never_read()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n");
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n", name: "transcript_full.jsonl");
        var files = new AntigravityBurnSource(t.Path, () => new Dictionary<string, string?>()).Discover();
        Assert.Single(files);
        Assert.Equal("transcript.jsonl", files[0].File.Name);
    }

    [Fact]
    public void A_conversation_without_a_summaries_row_is_unattributed()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n");
        var files = new AntigravityBurnSource(t.Path, () => new Dictionary<string, string?>()).Discover();
        Assert.Null(files.Single().Cwd);
        Assert.Equal("conv-1", files[0].SessionId);
    }

    [Fact]
    public void An_unreadable_database_lists_nothing_so_no_checkpoint_moves()
    {
        using var t = new TempDir();
        Transcript(t.Path, "conv-1", Turn("2026-11-10T16:01:00Z", 10, 1, 0) + "\n");
        Assert.Empty(new AntigravityBurnSource(t.Path, () => null).Discover());
    }

    [Fact]
    public void A_missing_cli_home_lists_nothing() =>
        Assert.Empty(new AntigravityBurnSource(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")),
            () => new Dictionary<string, string?>()).Discover());

    [Fact]
    public void Bad_json_counts_as_skipped()
    {
        Assert.Null(new AntigravityBurnSource.Parser().Parse("""{"input_tokens": broken""", out var bad));
        Assert.True(bad);
    }

    [Fact]
    public void Default_home_is_the_cli_folder() =>
        Assert.Equal(Path.Combine(@"C:\Users\x", ".gemini", "antigravity-cli"), AntigravityBurnSource.DefaultCliHome(@"C:\Users\x"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AntigravityBurnSourceTests"`
Expected: build error.

- [ ] **Step 3: Implement `AntigravityBurnSource.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Antigravity CLI local burn (spec 3). Each <c>brain/&lt;conversation_id&gt;/.system_generated/logs/transcript.jsonl</c>
/// is one Vault session; its project comes from the summaries database (looked up by the App through
/// <c>workspaces</c>, which reads only <c>conversation_id</c> and <c>workspace_uris</c>). <c>transcript_full.jsonl</c>
/// repeats the same tokens and is never read. The IDE's folder is not read: its tokens are protobuf only.
/// </summary>
public sealed class AntigravityBurnSource(string cliHome, Func<IReadOnlyDictionary<string, string?>?> workspaces) : IAgentBurnSource
{
    public const string Model = "antigravity";   // the transcript names no model

    public string Agent => AgentSources.Antigravity;

    public static string DefaultCliHome(string userProfile) => Path.Combine(userProfile, ".gemini", "antigravity-cli");

    public static string SummariesPath(string cliHome) => Path.Combine(cliHome, "conversation_summaries.db");

    /// <summary>The first <c>file:</c> URI in a <c>workspace_uris</c> JSON array, as a local path; null otherwise.</summary>
    public static string? ProjectPathOf(string? workspaceUrisJson)
    {
        if (string.IsNullOrWhiteSpace(workspaceUrisJson)) return null;
        try
        {
            if (JsonNode.Parse(workspaceUrisJson) is not JsonArray a || a.Count == 0) return null;
            if (a[0] is not JsonValue v || !v.TryGetValue<string>(out var s)) return null;
            return Uri.TryCreate(s, UriKind.Absolute, out var u) && u.IsFile ? u.LocalPath : null;
        }
        catch (JsonException) { return null; }
    }

    public IReadOnlyList<AgentBurnFile> Discover()
    {
        var brain = new DirectoryInfo(Path.Combine(cliHome, "brain"));
        if (!brain.Exists) return [];
        // Unreadable database (locked, missing, damaged): list nothing, so no checkpoint moves and the next pass reads it all.
        if (workspaces() is not { } map) return [];
        var files = new List<AgentBurnFile>();
        foreach (var conv in brain.EnumerateDirectories())
        {
            var fi = new FileInfo(Path.Combine(conv.FullName, ".system_generated", "logs", "transcript.jsonl"));
            if (!fi.Exists) continue;
            var cwd = map.TryGetValue(conv.Name, out var uris) ? ProjectPathOf(uris) : null;
            files.Add(new AgentBurnFile(fi, conv.Name, cwd, false));
        }
        return files;
    }

    public Stream Open(AgentBurnFile file) =>
        new FileStream(file.File.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public IBurnLineParser NewParser(string? state) => new Parser();

    /// <summary>A turn line with token fields becomes an event dated by its own <c>created_at</c>. Input is fresh input
    /// as is: the 2026-11-13 build check found <c>cache_read_tokens</c> kept apart from <c>input_tokens</c>.</summary>
    public sealed class Parser : IBurnLineParser
    {
        public BurnEvent? Parse(string line, out bool bad)
        {
            bad = false;
            if (!line.Contains("_tokens\"", StringComparison.Ordinal)) return null;   // user and tool steps carry none
            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch (JsonException) { bad = true; return null; }
            if (node is not JsonObject d) { bad = true; return null; }
            if (BurnJson.ParseIso(BurnJson.Str(d, "created_at")) is not { } ts) return null;
            return new BurnEvent(ts, BurnJson.Num(d, "input_tokens"), BurnJson.Num(d, "output_tokens"),
                BurnJson.Num(d, "cache_read_tokens"), 0, Model, null, null);
        }

        public string? SaveState() => null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AntigravityBurnSourceTests"`
Expected: PASS. The first `ProjectPathOf` case yields the lowercase drive letter `c:` that the URI carries; `CcLogReader.ProjectDisplayName` names projects by basename, so the case does not reach the screen.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/AntigravityBurnSource.cs windows-dotnet/tests/Sanduhr.Tests/AntigravityBurnSourceTests.cs
git commit -m "feat(core): Antigravity CLI burn source, per-turn and project-attributed"
```

---

### Task 6: Reading Antigravity's summaries through winsqlite3

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Services/AntigravitySummaries.cs`
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/AntigravitySummariesTests.cs`

**Interfaces:**
- Produces: `internal static class AntigravitySummaries` with `public const string Query = "SELECT conversation_id, workspace_uris FROM conversation_summaries";` and `public static IReadOnlyDictionary<string, string?>? Read(string dbPath, int busyTimeoutMs = 2000)` (null when the file is missing or SQLite refuses: locked, damaged, no table). The App test project already sees App internals if `InternalsVisibleTo` is set; check `src/Sanduhr.App/AssemblyInfo.cs`, and if it is not, make the class `public`.

- [ ] **Step 1: Write the failing tests**

The test builds a database with the same system library through its own small P/Invoke:

```csharp
using System.Runtime.InteropServices;
using System.Text;
using Sanduhr.App.Services;
using Xunit;

namespace Sanduhr.App.Tests;

public class AntigravitySummariesTests
{
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr cb, IntPtr arg, IntPtr err);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_close(IntPtr db);

    private static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    private static IntPtr Create(string path, string sql)
    {
        Assert.Equal(0, sqlite3_open_v2(Z(path), out var db, 0x2 | 0x4, IntPtr.Zero));   // READWRITE | CREATE
        Assert.Equal(0, sqlite3_exec(db, Z(sql), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        return db;
    }

    private const string Schema =
        "CREATE TABLE conversation_summaries (conversation_id TEXT, title TEXT, preview TEXT, workspace_uris TEXT, raw_summary TEXT, last_modified_time TEXT);" +
        "INSERT INTO conversation_summaries VALUES ('c1','t','p','[\"file:///C:/x/api\"]','r','2026-11-10');" +
        "INSERT INTO conversation_summaries VALUES ('c2','t','p',NULL,'r','2026-11-10');";

    [Fact]
    public void The_query_selects_only_the_id_and_the_workspace() =>
        Assert.Equal("SELECT conversation_id, workspace_uris FROM conversation_summaries", AntigravitySummaries.Query);

    [Fact]
    public void Reads_each_conversation_and_its_workspace_json()
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var path = Path.Combine(dir.FullName, "conversation_summaries.db");
            sqlite3_close(Create(path, Schema));

            var map = AntigravitySummaries.Read(path);

            Assert.NotNull(map);
            Assert.Equal("[\"file:///C:/x/api\"]", map!["c1"]);
            Assert.Null(map["c2"]);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_missing_file_is_null() =>
        Assert.Null(AntigravitySummaries.Read(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".db")));

    [Fact]
    public void A_database_without_the_table_is_null()
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var path = Path.Combine(dir.FullName, "x.db");
            sqlite3_close(Create(path, "CREATE TABLE other (a TEXT);"));
            Assert.Null(AntigravitySummaries.Read(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_locked_database_is_null_after_the_busy_timeout()
    {
        var dir = Directory.CreateTempSubdirectory("sanduhr-agy-");
        try
        {
            var path = Path.Combine(dir.FullName, "conversation_summaries.db");
            var writer = Create(path, Schema + "BEGIN EXCLUSIVE;");
            try { Assert.Null(AntigravitySummaries.Read(path, busyTimeoutMs: 50)); }
            finally { sqlite3_close(writer); }
        }
        finally { dir.Delete(true); }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Stop any running dev `Sanduhr.exe`. Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~AntigravitySummariesTests"`
Expected: build error, `AntigravitySummaries` not found.

- [ ] **Step 3: Implement `AntigravitySummaries.cs`**

```csharp
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sanduhr.App.Services;

/// <summary>
/// Antigravity CLI's conversation list, read-only through Windows' own SQLite (<c>winsqlite3.dll</c>, no package), for
/// spec 3's project lookup. Selects only <c>conversation_id</c> and <c>workspace_uris</c>: never titles, previews or
/// summaries. Null when the file is missing or SQLite refuses it (locked past the busy timeout, damaged, no table), so the
/// Antigravity source lists nothing that pass.
/// </summary>
internal static class AntigravitySummaries
{
    public const string Query = "SELECT conversation_id, workspace_uris FROM conversation_summaries";

    private const int SqliteOk = 0, SqliteRow = 100, SqliteDone = 101;
    private const int OpenReadOnly = 0x1, OpenUri = 0x40;

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)] private static extern int sqlite3_close(IntPtr db);

    private static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public static IReadOnlyDictionary<string, string?>? Read(string dbPath, int busyTimeoutMs = 2000)
    {
        if (!File.Exists(dbPath)) return null;   // streamer: notname - a file location used for IO, never drawn
        IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
        try
        {
            var uri = new Uri(dbPath).AbsoluteUri + "?mode=ro";
            if (sqlite3_open_v2(Z(uri), out db, OpenReadOnly | OpenUri, IntPtr.Zero) != SqliteOk) return null;
            sqlite3_busy_timeout(db, busyTimeoutMs);
            if (sqlite3_prepare_v2(db, Z(Query), -1, out stmt, IntPtr.Zero) != SqliteOk) return null;
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            int rc;
            while ((rc = sqlite3_step(stmt)) == SqliteRow)
            {
                var id = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, 0));
                if (string.IsNullOrEmpty(id)) continue;
                map[id] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, 1));
            }
            return rc == SqliteDone ? map : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or UriFormatException)
        {
            return null;
        }
        finally
        {
            if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the Step 2 command. Expected: PASS. If `A_locked_database_is_null_after_the_busy_timeout` reads rows anyway (a reader can coexist with a reserved lock in rollback mode), change the test's lock statement to `PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE; UPDATE conversation_summaries SET title=title;` and re-run. The point of the test is that a held exclusive lock gives null, not a crash.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/AntigravitySummaries.cs windows-dotnet/tests/Sanduhr.App.Tests/AntigravitySummariesTests.cs
git commit -m "feat(app): read Antigravity's conversation workspaces read-only via winsqlite3"
```

---

### Task 7: Wiring: consent, ingest and the Sources page

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (add `LoadActivity` / `SaveActivity` near `LoadVaultRoots`)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/VaultService.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (where the vault and the Codex service are built, near line 1488)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SourcesViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/SourcesPage.xaml`, `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs:241`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs` (`VaultRootToggleViewModel.RefreshShown`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs`
- Test: create `windows-dotnet/tests/Sanduhr.App.Tests/VaultServiceAgentTests.cs`; extend `windows-dotnet/tests/Sanduhr.App.Tests/SourcesPageTests.cs`

**Interfaces:**
- Consumes: `IAgentBurnSource`, `CodexBurnSource`, `AntigravityBurnSource` (Tasks 3-5), `AntigravitySummaries.Read` (Task 6), `AgentSources.Burn`/`ActivityOnly`/`BurnName` (Task 1).
- Produces:
  - `SettingsStore.LoadActivity()` → `IReadOnlyDictionary<string, bool>` (settings.json `activity`, e.g. `{"antigravity": true}`), `SettingsStore.SaveActivity(string source, bool on)`.
  - `VaultService.ActiveAgents` (`Func<IReadOnlyList<IAgentBurnSource>>`, set by App; default empty), `VaultService.IsConsented(string root)`, `VaultService.SetAgentVaultOn(string agent)`.
  - `SourcesViewModel.ActivityRows` (`ObservableCollection<ActivityRowViewModel>`) with `ActivityRowViewModel.Source`, `.On` (two-way), `.Installed`, `.Name`, `.Caption`.
  - `SettingsNames.ActivityOnlyHeader`, `SettingsNames.AntigravityActivityCaption`, `SettingsNames.AntigravityNotice`, `SettingsNames.NothingFoundYet`.

- [ ] **Step 1: Write the failing VaultService tests**

`VaultService` builds its own `Paths` and settings, so test the consent rules through a seam. Extract the rule into a pure static method first by writing the test against it:

```csharp
using Sanduhr.App.Services;
using Xunit;

namespace Sanduhr.App.Tests;

public class VaultServiceAgentTests
{
    private static readonly IReadOnlyDictionary<string, bool> None = new Dictionary<string, bool>();

    [Fact]
    public void A_claude_home_needs_an_explicit_yes() =>
        Assert.False(VaultService.Consented(".claude", None, activeAgents: []));

    [Fact]
    public void An_active_agent_is_on_until_purged()
    {
        Assert.True(VaultService.Consented("codex", None, activeAgents: ["codex"]));
        Assert.False(VaultService.Consented("codex", new Dictionary<string, bool> { ["codex"] = false }, activeAgents: ["codex"]));
    }

    [Fact]
    public void An_agent_whose_switch_is_off_is_never_consented() =>
        Assert.False(VaultService.Consented("antigravity", new Dictionary<string, bool> { ["antigravity"] = true }, activeAgents: []));
}
```

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~VaultServiceAgentTests"`. Expected: build error.

- [ ] **Step 2: Implement the VaultService changes**

In `VaultService.cs`:

```csharp
    /// <summary>The agent sources whose switch is on and whose files exist (spec 3), set by App. Read on every cycle, so a
    /// Sources switch takes effect on the next ingest without rebuilding anything.</summary>
    public Func<IReadOnlyList<IAgentBurnSource>> ActiveAgents { get; set; } = () => [];

    /// <summary>A Claude Code home needs an explicit yes in vault_roots; an active agent root is on unless purged (false is
    /// the tombstone Purge and Erase write).</summary>
    internal static bool Consented(string root, IReadOnlyDictionary<string, bool> vaultRoots, IReadOnlyCollection<string> activeAgents)
        => AgentSources.Burn.Contains(root) && root != AgentSources.Claude
            ? activeAgents.Contains(root) && vaultRoots.GetValueOrDefault(root, true)
            : vaultRoots.GetValueOrDefault(root);

    public bool IsConsented(string root)
        => Consented(root, _settings.LoadVaultRoots(), ActiveAgents().Select(a => a.Agent).ToList());

    /// <summary>A source switched on in Sources clears its purge tombstone, so ingest resumes.</summary>
    public void SetAgentVaultOn(string agent) => SetRootConsent(agent, true);
```

Change `DetectedRootNames` to add the active agents after the Claude homes:

```csharp
    public IReadOnlyList<string> DetectedRootNames()   // streamer: key - root names for consent keys, never drawn here
        => _reader.SearchRoots().Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!).Concat(ActiveAgents().Select(a => a.Agent)).ToList();
```

`ConsentedRootNames` becomes:

```csharp
    public IReadOnlyList<string> ConsentedRootNames()
    {
        var consent = _settings.LoadVaultRoots();
        var active = ActiveAgents().Select(a => a.Agent).ToList();
        return DetectedRootNames().Where(r => Consented(r, consent, active)).ToList();   // streamer: key - root names for consent keys, never drawn here
    }
```

`NeedsConsentPrompt` keeps counting Claude homes only: replace `DetectedRootNames().Count > 0` with `_reader.SearchRoots().Count > 0`.

In `TriggerIngest`, compute both lists in the prelude and pass the agents:

```csharp
        IReadOnlyList<string> roots;
        IReadOnlyList<IAgentBurnSource> agents;
        bool fullPaths;
        try
        {
            var consented = ConsentedRootNames();
            agents = ActiveAgents().Where(a => consented.Contains(a.Agent)).ToList();
            roots = consented.Where(r => !agents.Any(a => a.Agent == r)).ToList();
            fullPaths = _settings.LoadVaultStoreFullPaths();
        }
        ...
        if (roots.Count == 0 && agents.Count == 0)
        ...
                _ingester.IngestOnce(roots, fullPaths, DateTimeOffset.UtcNow,
                    stillConsented: IsConsented, agents: agents);
```

`EraseArchive` already writes false for every `DetectedRootNames()` entry, which now includes active agents. `PurgeRoot` writes the tombstone. Both are covered by `Consented`.

- [ ] **Step 3: Settings for the activity switch**

In `SettingsStore.cs`, beside `LoadVaultRoots`:

```csharp
    /// <summary>Activity-only sources switched on in Sources (spec 3): settings.json "activity", {"antigravity": true}.</summary>
    public IReadOnlyDictionary<string, bool> LoadActivity()
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (Read()["activity"] is JsonObject map)
            foreach (var (name, node) in map)
            {
                try { result[name] = node?.GetValue<bool>() ?? false; }
                catch { result[name] = false; }
            }
        return result;
    }

    public void SaveActivity(string source, bool on)
    {
        var root = Read();
        var map = root["activity"] as JsonObject ?? new JsonObject();
        map[source] = on;
        root["activity"] = map;
        Write(root);
    }
```

- [ ] **Step 4: App builds the active agents**

In `App.xaml.cs`, after the widget's `VaultService` exists (find `Vault` in the startup path; the widget exposes it as `vm.Vault`), set:

```csharp
        if (vm.Vault is { } vault)
        {
            var agentPaths = new Paths();
            vault.ActiveAgents = () =>
            {
                var settings = new SettingsStore(agentPaths);
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);   // streamer: notname - user folder handed to the readers, never drawn
                var list = new List<IAgentBurnSource>();
                var codexHomes = CodexHomes.Discover(Environment.GetEnvironmentVariable("CODEX_HOME"), profile);
                if ((settings.LoadSetup()?.CodexUsage ?? false) && codexHomes.Count > 0)
                    list.Add(new CodexBurnSource(() => codexHomes, ZstdRollouts.Decompress));
                var agyHome = AntigravityBurnSource.DefaultCliHome(profile);
                if (settings.LoadActivity().GetValueOrDefault(AgentSources.Antigravity) && Directory.Exists(agyHome))
                    list.Add(new AntigravityBurnSource(agyHome, () => AntigravitySummaries.Read(AntigravityBurnSource.SummariesPath(agyHome))));
                return list;
            };
        }
```

- [ ] **Step 5: The Sources page row**

In `SettingsNames.cs`, beside `CodexNotice`:

```csharp
    public const string ActivityOnlyHeader = "Activity only";
    public const string AntigravityActivityCaption = "Activity only, from the Antigravity CLI. Sanduhr can't see Antigravity's limits or the IDE's usage.";
    public const string NothingFoundYet = "Nothing found yet.";
    public const string AntigravityNotice =
        "Antigravity and Gemini are trademarks of Google LLC. Sanduhr is not affiliated with or endorsed by Google.";
```

In `SourcesViewModel.cs` add a row type and collection. The constructor gains `Func<bool> antigravityInstalled` and `Action<string> agentVaultOn`:

```csharp
public sealed partial class ActivityRowViewModel(SourcesViewModel owner, string source) : ObservableObject
{
    public string Source { get; } = source;
    public string Name => AgentSources.Name(Source);
    public string Caption => Installed ? SettingsNames.AntigravityActivityCaption : SettingsNames.NothingFoundYet;
    [ObservableProperty] private bool _installed;
    [ObservableProperty] private bool _on;
    private bool _loading;

    partial void OnInstalledChanged(bool value) => OnPropertyChanged(nameof(Caption));
    partial void OnOnChanged(bool value) { if (!_loading) owner.SetActivityOn(Source, value); }

    internal void Load(bool installed, bool on) { _loading = true; Installed = installed; On = on; _loading = false; }
}
```

In `SourcesViewModel`:

```csharp
    private readonly Func<bool> _antigravityInstalled;
    private readonly Action<string> _agentVaultOn;

    public ObservableCollection<ActivityRowViewModel> ActivityRows { get; } = [];

    internal void SetActivityOn(string source, bool on)
    {
        _store.SaveActivity(source, on);
        if (on) _agentVaultOn(source);   // clears a purge tombstone, so ingest resumes
    }
```

The constructor stores the two new arguments, adds `foreach (var s in AgentSources.ActivityOnly) ActivityRows.Add(new ActivityRowViewModel(this, s));` before `Reload()`. `Reload(bool)` ends with `foreach (var row in ActivityRows) row.Load(_antigravityInstalled(), _store.LoadActivity().GetValueOrDefault(row.Source));`. In `SetOn`, after saving Codex on (`_store.SaveSetup(setup.WithCodex(on))`), add `if (on) _agentVaultOn(AgentSources.Codex);`.

`SettingsViewModel.cs:241` passes the two new arguments:

```csharp
            () => Directory.Exists(AntigravityBurnSource.DefaultCliHome(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))),   // streamer: notname - only whether the folder exists, never drawn
            agent => widget.Vault?.SetAgentVaultOn(agent));
```

In `SourcesPage.xaml`, after the `</StackPanel>` that closes the limits list (before the `Border Height="1"` separator), add:

```xml
                <TextBlock Text="{x:Static core:SettingsNames.ActivityOnlyHeader}" FontWeight="SemiBold" Margin="0,16,0,6"
                           AutomationProperties.HeadingLevel="Level2" />
                <ItemsControl ItemsSource="{Binding ActivityRows}" Focusable="False">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,8">
                                <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{Binding Name}"
                                          IsChecked="{Binding On, Mode=TwoWay}" IsEnabled="{Binding Installed}"
                                          AutomationProperties.Name="{Binding Name}"
                                          AutomationProperties.HelpText="{Binding Caption}" />
                                <TextBlock Text="{Binding Caption}" FontSize="11" TextWrapping="Wrap" Margin="24,2,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
```

After the existing `CodexNotice` TextBlock, add one for `SettingsNames.AntigravityNotice` with the same attributes.

In `LocalCcViewModel.cs`, `VaultRootToggleViewModel.RefreshShown` shows agent roots by name:

```csharp
        Shown = AgentSources.Burn.Contains(Name) && Name != AgentSources.Claude
            ? AgentSources.BurnName(Name)
            : StreamerMode.Mask.FolderName(Name);   // streamer: masked - agent names are product names, folders pass Mask.FolderName
```

(Keep the existing body's other lines; only the `Shown` assignment changes.)

- [ ] **Step 6: Sources page tests**

In `SourcesPageTests.cs`, using the file's existing harness for building a `SourcesViewModel` (pass `() => true` and a recording `Action<string>` for the new arguments), add:

```csharp
    [Fact]
    public void Antigravity_is_an_activity_row_off_by_default_and_never_a_primary_choice()
    {
        var calls = new List<string>();
        var vm = NewSources(antigravityInstalled: () => true, agentVaultOn: calls.Add);   // the file's builder, extended with the two arguments
        var row = Assert.Single(vm.ActivityRows);
        Assert.Equal("antigravity", row.Source);
        Assert.False(row.On);
        Assert.DoesNotContain(vm.Rows, r => r.Source == "antigravity");

        row.On = true;

        Assert.Equal(new[] { "antigravity" }, calls);
    }

    [Fact]
    public void Without_the_cli_the_row_is_disabled_and_says_nothing_found_yet()
    {
        var vm = NewSources(antigravityInstalled: () => false, agentVaultOn: _ => { });
        var row = Assert.Single(vm.ActivityRows);
        Assert.False(row.Installed);
        Assert.Equal(SettingsNames.NothingFoundYet, row.Caption);
    }
```

If the file has no builder method, add `NewSources(...)` calling the internal constructor with a `SettingsStore` on a temp `Paths` the same way the file's existing tests construct one.

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~VaultServiceAgentTests|FullyQualifiedName~SourcesPage"` then the full App suite. Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/tests/Sanduhr.App.Tests
git commit -m "feat(app): agent roots ingest behind the source switches, Antigravity on Sources"
```

---

### Task 8: The widget footer, per agent

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentBurnDeltas.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`UpdateFooter` at ~2469, the vault hookup)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/AgentBurnDeltasTests.cs`

**Interfaces:**
- Consumes: `AgentSources.Burn`, `AgentSources.ShortName` (Task 1); `VaultService.ActiveAgents`, `VaultReader.ReadWindow` (existing).
- Produces: `public sealed class AgentBurnDeltas` with `void Observe(string agent, DateOnly day, long todayTotal)`, `void Forget(string agent)`, `string FooterPieces()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentBurnDeltasTests
{
    private static readonly DateOnly D1 = new(2026, 11, 12), D2 = new(2026, 11, 13);

    [Fact]
    public void The_first_observation_has_no_before_so_shows_nothing()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 5000);
        Assert.Equal("", d.FooterPieces());
    }

    [Fact]
    public void Each_agent_shows_the_change_since_the_ingest_before_in_burn_order()
    {
        var d = new AgentBurnDeltas();
        d.Observe("antigravity", D1, 100);
        d.Observe("codex", D1, 1000);
        d.Observe("antigravity", D1, 1100);
        d.Observe("codex", D1, 4000);
        Assert.Equal("  ·  Codex +3K  ·  Agy +1K", d.FooterPieces());
    }

    [Fact]
    public void Nothing_new_is_left_out()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 1000);
        d.Observe("codex", D1, 1000);
        Assert.Equal("", d.FooterPieces());
    }

    [Fact]
    public void At_a_day_change_the_earlier_total_counts_as_zero()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 90_000);
        d.Observe("codex", D2, 2000);
        Assert.Equal("  ·  Codex +2K", d.FooterPieces());
    }

    [Fact]
    public void A_forgotten_agent_disappears()
    {
        var d = new AgentBurnDeltas();
        d.Observe("codex", D1, 1000);
        d.Observe("codex", D1, 3000);
        d.Forget("codex");
        Assert.Equal("", d.FooterPieces());
    }
}
```

Check `TokenFormat.Compact(3000)`'s exact output first (`dotnet test` the existing TokenFormat tests or read `TokenFormat.cs`); if it renders `3.0K` or lowercase, change the expected strings above to match the existing footer's `CC +` style. Do not change `TokenFormat`.

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentBurnDeltasTests"`. Expected: build error.

- [ ] **Step 2: Implement `AgentBurnDeltas.cs`**

```csharp
namespace Sanduhr.Core;

/// <summary>
/// The widget footer's agent pieces (spec 3): each agent's Vault total for today after the latest ingest minus the total
/// after the ingest before it. The first observation has no "before" and shows nothing; at a day change the earlier total
/// counts as zero; an agent with nothing new is left out, as the footer's "CC +" is.
/// </summary>
public sealed class AgentBurnDeltas
{
    private readonly Dictionary<string, (DateOnly Day, long Total)> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _delta = new(StringComparer.Ordinal);

    public void Observe(string agent, DateOnly day, long todayTotal)
    {
        _delta[agent] = _last.TryGetValue(agent, out var prev)
            ? Math.Max(0, todayTotal - (prev.Day == day ? prev.Total : 0))
            : 0;
        _last[agent] = (day, todayTotal);
    }

    public void Forget(string agent)
    {
        _last.Remove(agent);
        _delta.Remove(agent);
    }

    public string FooterPieces() => string.Concat(AgentSources.Burn
        .Where(a => _delta.GetValueOrDefault(a) > 0)
        .Select(a => $"  ·  {AgentSources.ShortName(a)} +{TokenFormat.Compact(_delta[a])}"));
}
```

Run the tests. Expected: PASS.

- [ ] **Step 3: Wire the footer**

In `WidgetViewModel.cs`:

```csharp
    private readonly AgentBurnDeltas _agentDeltas = new();
```

Where `_vault` is assigned (find `_vault =`), subscribe once:

```csharp
        if (_vault is not null)
            _vault.IngestCompleted += OnVaultIngestCompleted;
```

Add the handler. `IngestCompleted` fires on a worker thread; the read runs there and the result is marshalled with the view model's dispatcher (use the same dispatcher field the class already uses for `BeginInvoke`; find `Dispatcher` usage in the file):

```csharp
    /// <summary>After each ingest: today's Vault total per active agent root feeds the footer's agent pieces (spec 3).</summary>
    private void OnVaultIngestCompleted()
    {
        if (_vault is not { } vault) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            foreach (var root in vault.ConsentedRootNames().Where(r => r != AgentSources.Claude && AgentSources.Burn.Contains(r)))
                totals[root] = vault.Reader.ReadWindow([root], today, today.AddDays(1)).ByDay.Values.Sum();
        }
        catch
        {
            return;   // a failed read leaves the footer as it was
        }
        _dispatcher.BeginInvoke(() =>
        {
            foreach (var agent in AgentSources.Burn.Where(a => a != AgentSources.Claude))
            {
                if (totals.TryGetValue(agent, out var total)) _agentDeltas.Observe(agent, today, total);
                else _agentDeltas.Forget(agent);
            }
            UpdateFooter();
        });
    }
```

Replace `_dispatcher` with the field name the class actually uses. In `UpdateFooter`, after the `cc` line:

```csharp
        string agents = _agentDeltas.FooterPieces();
        FooterText = $"Updated {ts}  ·  {mode}{cc}{agents}";
```

The demo branch above it returns first, so demo mode shows no agent pieces.

- [ ] **Step 4: An App test for the footer text**

In the App test file that covers the footer (search `FooterText` under `tests/Sanduhr.App.Tests`), add a test that calls the existing seam for footer composition if one exists. If `UpdateFooter` has no test seam, the Core tests above carry the logic and the live smoke in Task 11 checks the composed line; note that in the commit message.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentBurnDeltasTests"` and the full App suite.

```bash
git add windows-dotnet/src/Sanduhr.Core/AgentBurnDeltas.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/tests
git commit -m "feat(widget): footer shows each agent's new tokens beside CC"
```

---

### Task 9: The Local burn page: strip, project shares, ledger and CSV

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentShares.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultLedgerCsv.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/LocalCcBarStrip.cs`, `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs` (`RenderLocalCc`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs` and the ledger's XAML in `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (find the grid bound to `ProjectHeader`)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/AgentSharesTests.cs`; extend `windows-dotnet/tests/Sanduhr.Tests/VaultLedgerCsvTests.cs`; extend the ledger tests under `windows-dotnet/tests/Sanduhr.App.Tests` (search `CcLedgerViewModel`)

**Interfaces:**
- Consumes: `VaultSessionInfo.Agent`, `VaultReader.AgentOf` (Task 1); `AgentSources.Burn`/`BurnName`, `SourceSettings.ColorOf`.
- Produces:
  - `public static class AgentShares { public static string Describe(long total, IReadOnlyDictionary<string, long> byAgent); public static string StripName(IReadOnlyDictionary<string, long> byAgent); }`
  - `VaultLedgerCsv.Row` gains `string Agent` as its last positional field; the header becomes `session,root,agent,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models`.
  - `LocalCcViewModel.ByDayByAgent` (`IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>>`).
  - `LocalCcBarStrip.SetData(byDay, palette, byAgent, colorOf)`.
  - `LedgerRowViewModel.AgentLabel`; `CcLedgerViewModel.AgentFilters`, `.SelectedAgentFilter`, `.ShowAgentFilter`.

- [ ] **Step 1: Write the failing Core tests**

`AgentSharesTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class AgentSharesTests
{
    [Fact]
    public void One_agent_reads_exactly_as_today() =>
        Assert.Equal(TokenFormat.Compact(1_200_000), AgentShares.Describe(1_200_000, new Dictionary<string, long> { ["claude"] = 1_200_000 }));

    [Fact]
    public void Several_agents_list_their_shares_in_burn_order() =>
        Assert.Equal($"{TokenFormat.Compact(1_200_000)} (Claude Code {TokenFormat.Compact(900_000)}, Codex {TokenFormat.Compact(300_000)})",
            AgentShares.Describe(1_200_000, new Dictionary<string, long> { ["codex"] = 300_000, ["claude"] = 900_000, ["antigravity"] = 0 }));

    [Fact]
    public void The_strip_names_each_agent_with_a_figure() =>
        Assert.Equal($"30 days, Claude Code {TokenFormat.Compact(41_000_000)}, Codex {TokenFormat.Compact(6_200_000)}",
            AgentShares.StripName(new Dictionary<string, long> { ["claude"] = 41_000_000, ["codex"] = 6_200_000 }));
}
```

In `VaultLedgerCsvTests.cs`, update the existing header assertion to the new header and add:

```csharp
    [Fact]
    public void The_agent_column_follows_root()
    {
        var csv = VaultLedgerCsv.Build([new VaultLedgerCsv.Row("s", "codex", "api", "a", "b", 1, 2, "m", "Codex")]).Csv;
        Assert.StartsWith("session,root,agent,project,", csv);
        Assert.Contains("s,codex,Codex,api,a,b,1,2,m", csv);
    }
```

(Use the result property name `CsvExport.CsvBuildResult` actually has; read `CsvExport.cs` if `Csv` is wrong.)

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~AgentSharesTests|FullyQualifiedName~VaultLedgerCsvTests"`. Expected: build errors.

- [ ] **Step 2: Implement the Core pieces**

`AgentShares.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>Text for local-burn figures split by agent (spec 3). With one agent the text is exactly today's.</summary>
public static class AgentShares
{
    public static string Describe(long total, IReadOnlyDictionary<string, long> byAgent)
    {
        var parts = Ordered(byAgent);
        var head = TokenFormat.Compact(total);
        return parts.Count <= 1 ? head : $"{head} ({string.Join(", ", parts)})";
    }

    public static string StripName(IReadOnlyDictionary<string, long> byAgent)
        => "30 days, " + string.Join(", ", Ordered(byAgent));

    private static List<string> Ordered(IReadOnlyDictionary<string, long> byAgent) => AgentSources.Burn
        .Where(a => byAgent.GetValueOrDefault(a) > 0)
        .Select(a => $"{AgentSources.BurnName(a)} {TokenFormat.Compact(byAgent[a])}")
        .ToList();
}
```

`VaultLedgerCsv.cs`: add `string Agent` as the last record field, change the header to `session,root,agent,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models`, and write `Append(sb, r.Agent); sb.Append(',');` right after the root field. Update the one call site in `CcLedgerViewModel` (~line 460) to pass `AgentSources.BurnName(x.Info.Agent)` last.

Run the Step 1 command. Expected: PASS.

- [ ] **Step 3: The strip stacks by agent**

`LocalCcViewModel`: in `Compute` (and `ComputePreview`), after the existing window read, read each agent root on its own and keep the per-day split:

```csharp
        var byAgent = new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>(StringComparer.Ordinal);
        foreach (var root in consentedRoots.Where(r => vault.Reader.AgentOf(r) != AgentSources.Claude))
        {
            var agent = vault.Reader.AgentOf(root);
            byAgent[agent] = vault.Reader.ReadWindow([root], from, toExclusive).ByDay;
        }
```

Add `ByDayByAgent` to `OverviewData` (append a positional field with an empty default) and expose `public IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>> ByDayByAgent { get; private set; }` set in `Apply`. Claude Code's share of a day is the day's total minus the agents' shares, so the existing `ByDay` (which already sums every consented root plus today's live Claude Code burn) stays the bar height.

`LocalCcBarStrip.SetData` gains the split and the colors, keeps the old two-argument overload for callers and tests:

```csharp
    private IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>> _byAgent =
        new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>();
    private Func<string, Color>? _colorOf;

    public void SetData(IReadOnlyDictionary<DateOnly, long> byDay, ThemePalette palette)
        => SetData(byDay, palette, new Dictionary<string, IReadOnlyDictionary<DateOnly, long>>(), null);

    public void SetData(IReadOnlyDictionary<DateOnly, long> byDay, ThemePalette palette,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, long>> byAgent, Func<string, Color>? colorOf)
    {
        _byDay = byDay ?? new Dictionary<DateOnly, long>();
        _palette = palette;
        _byAgent = byAgent;
        _colorOf = colorOf;
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (agent, days) in byAgent) totals[agent] = days.Values.Sum();
        totals[AgentSources.Claude] = Math.Max(0, _byDay.Values.Sum() - totals.Values.Sum());
        System.Windows.Automation.AutomationProperties.SetName(this,
            totals.Count(kv => kv.Value > 0) > 1 ? AgentShares.StripName(totals) : "");
        InvalidateVisual();
    }
```

In `OnRender`, replace the single bar rectangle with a stack when any agent has data:

```csharp
            else if (_byAgent.Count == 0 || _colorOf is null)
            {
                double barH = Math.Max(2, v / (double)maxV * (h - 8));
                dc.DrawRectangle(barColor, null, new Rect(x, baselineY - barH, barW, barH));
            }
            else
            {
                double scale = (h - 8) / maxV;
                double y = baselineY;
                long agentsSum = 0;
                foreach (var agent in AgentSources.Burn.Where(a => a != AgentSources.Claude))
                    agentsSum += _byAgent.TryGetValue(agent, out var days) ? days.GetValueOrDefault(d) : 0;
                foreach (var agent in AgentSources.Burn)
                {
                    long part = agent == AgentSources.Claude
                        ? Math.Max(0, v - agentsSum)
                        : (_byAgent.TryGetValue(agent, out var days) ? days.GetValueOrDefault(d) : 0);
                    if (part <= 0) continue;
                    double segH = Math.Max(1, part * scale);
                    y -= segH;
                    dc.DrawRectangle(Frozen(_colorOf(agent)), null, new Rect(x, y, barW, segH));
                }
            }
```

The empty-state text changes from "No local CC activity in the last 30 days." to "No local activity in the last 30 days." only when `_byAgent.Count > 0`; otherwise it stays as it is.

`SettingsWindow.xaml.cs` `RenderLocalCc` passes the split and the source colors:

```csharp
        var colors = ViewModel.Sources.ColorsForStrip();   // see below
        BarStrip.SetData(ViewModel.ClaudeCode.Overview.ByDay, ViewModel.ClaudeCode.Overview.Palette,
            ViewModel.ClaudeCode.Overview.ByDayByAgent, colors);
```

Add to `SourcesViewModel`:

```csharp
    /// <summary>Each burn source's color as saved in Sources (Antigravity's default), for the Local burn page's strip.</summary>
    public Func<string, System.Windows.Media.Color> ColorsForStrip()
    {
        var sources = _store.LoadSources();
        return id => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(sources.ColorOf(id));
    }
```

- [ ] **Step 4: Project rows show agent shares**

In `LocalCcViewModel.Compute`, keep a per-agent project map next to the combined one:

```csharp
        var projectsByAgent = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        foreach (var root in consentedRoots.Where(r => vault.Reader.AgentOf(r) != AgentSources.Claude))
            foreach (var (name, tokens) in vault.Reader.ReadWindow([root], from, toExclusive).ByProjectName)
            {
                var agent = vault.Reader.AgentOf(root);
                if (!projectsByAgent.TryGetValue(name, out var m)) projectsByAgent[name] = m = new(StringComparer.Ordinal);
                m[agent] = m.GetValueOrDefault(agent) + tokens;
            }
```

Pass it to `FillBreakdowns` (add a parameter) and build each project row's tokens text with:

```csharp
        var split = new Dictionary<string, long>(projectsByAgent.GetValueOrDefault(name) ?? new(), StringComparer.Ordinal);
        split[AgentSources.Claude] = Math.Max(0, tokens - split.Values.Sum());
        var text = AgentShares.Describe(tokens, split);
```

`BreakdownRow.ToString()` already reads `"{Name}, {Tokens}"`, so the accessible name carries the shares.

- [ ] **Step 5: Ledger label, filter and CSV**

`LedgerRowViewModel`:

```csharp
    /// <summary>"Claude Code", "Codex" or "Antigravity"; a project group spanning agents reads "Several agents".</summary>
    public string AgentLabel => Members.Select(m => m.Agent).Distinct().Count() > 1
        ? SettingsNames.SeveralAgents
        : AgentSources.BurnName(Info.Agent);
```

Add `public const string SeveralAgents = "Several agents";` and `public const string AllAgents = "All";` to `SettingsNames`.

`CcLedgerViewModel`:

```csharp
    public ObservableCollection<string> AgentFilters { get; } = [SettingsNames.AllAgents];
    [ObservableProperty] private string _selectedAgentFilter = SettingsNames.AllAgents;
    [ObservableProperty] private bool _showAgentFilter;
    partial void OnSelectedAgentFilterChanged(string value) => ApplyFilter();
```

Fold the existing filters into one predicate. `SetDayScope` currently sets `View.Filter = item => item is LedgerRowViewModel r && r.ScopedTokens > 0;` and another path sets `View.Filter = null;`. Replace both with a `_dayScoped` flag and a call to:

```csharp
    private void ApplyFilter()
    {
        bool agentOn = SelectedAgentFilter != SettingsNames.AllAgents;
        View.Filter = !_dayScoped && !agentOn ? null : item => item is LedgerRowViewModel r
            && (!_dayScoped || r.ScopedTokens > 0)
            && (!agentOn || r.AgentLabel == SelectedAgentFilter);
    }
```

At the end of `RefreshAsync`, rebuild the filter choices from the sessions read:

```csharp
        var present = AgentSources.Burn.Where(a => sessions.Any(s => s.Agent == a)).Select(AgentSources.BurnName).ToList();
        ShowAgentFilter = present.Count > 1;
        AgentFilters.Clear();
        AgentFilters.Add(SettingsNames.AllAgents);
        foreach (var name in present) AgentFilters.Add(name);
        if (!AgentFilters.Contains(SelectedAgentFilter)) SelectedAgentFilter = SettingsNames.AllAgents;
```

(`sessions` is the local holding `vault.Reader.ReadSessions(...)`'s result; name it if it is inline today.)

XAML, above the ledger's list in `SettingsWindow.xaml`:

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,6"
            Visibility="{Binding ShowAgentFilter, Converter={StaticResource BoolVis}}">
    <TextBlock Text="Agent" VerticalAlignment="Center" Margin="0,0,8,0" />
    <ComboBox ItemsSource="{Binding AgentFilters}" SelectedItem="{Binding SelectedAgentFilter, Mode=TwoWay}"
              MinWidth="140" AutomationProperties.Name="Agent" />
</StackPanel>
```

And a column (or a second line in the row template, matching how the template shows the project) bound to `AgentLabel`.

- [ ] **Step 6: App tests for the ledger**

In the existing ledger test file, add:

```csharp
    [Fact]
    public void The_agent_filter_shows_only_with_two_agents_and_narrows_rows()
    {
        // Build a CcLedgerViewModel over a vault holding a ".claude" root and a "codex" root (meta Agent = "codex"),
        // with the file's existing fixture helpers; RefreshAsync; then:
        Assert.True(vm.ShowAgentFilter);
        Assert.Equal(new[] { "All", "Claude Code", "Codex" }, vm.AgentFilters);
        vm.SelectedAgentFilter = "Codex";
        Assert.All(vm.View.Cast<LedgerRowViewModel>(), r => Assert.Equal("Codex", r.AgentLabel));
    }

    [Fact]
    public void With_one_agent_the_filter_stays_hidden()
    {
        // Same fixture with only the ".claude" root.
        Assert.False(vm.ShowAgentFilter);
    }
```

Write the fixture code with the helpers the file already has for building a vault-backed ledger; if it has none, construct `VaultStore` shards directly as in Task 1's reader test and point a `VaultService` built for tests at that directory the way the existing ledger tests do.

- [ ] **Step 7: Run and commit**

Run: Core and App suites in full. Expected: PASS.

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(app): Local burn page splits the strip, projects and ledger by agent"
```

---

### Task 10: MCP: agent project totals behind a consent switch

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (`LoadMcpAgents`, `SaveMcpAgent`)
- Modify: `windows-dotnet/src/Sanduhr.Core/McpSettingsExport.cs`
- Modify: `windows-dotnet/src/Sanduhr.Mcp/McpConfig.cs`, `windows-dotnet/src/Sanduhr.Mcp/ToolLogic.cs` (`BuildBurn`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs` and its Folders XAML (find the view bound to `Folders`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (`AgentFolderReadsCaption`, `LetClaudeReadAgentTotals`)
- Test: extend `windows-dotnet/tests/Sanduhr.Tests` (search `McpSettingsExportTests`), `windows-dotnet/tests/Sanduhr.Mcp.Tests` (search `BuildBurn` and `McpConfig` tests); `TrustBoundaryTests` and `AgentConsentsTests` must pass unchanged or with caption-only updates

**Interfaces:**
- Produces: settings.json `mcp_agents` (`{"codex": true}`); projection `mcp_agents` with the same shape (only `codex` and `antigravity` keys); `McpConfig.ConsentedAgents` (`IReadOnlyList<string>`, default empty); `get_local_burn_by_project` response gains `"agents": [{"agent": "codex", "total_tokens": n, "projects": [{"name": ..., "tokens": n}]}]` and `"agents_scanned"`.

- [ ] **Step 1: Write the failing tests**

Core, in the McpSettingsExport tests:

```csharp
    [Fact]
    public void Agent_read_consent_is_projected_for_known_agents_only()
    {
        var settings = new JsonObject
        {
            ["mcp_enabled"] = true,
            ["mcp_agents"] = new JsonObject { ["codex"] = true, ["antigravity"] = false, ["other"] = true },
        };
        var p = McpSettingsExport.Project(settings)!;
        var agents = (JsonObject)p["mcp_agents"]!;
        Assert.True(agents["codex"]!.GetValue<bool>());
        Assert.False(agents["antigravity"]!.GetValue<bool>());
        Assert.Null(agents["other"]);
    }
```

MCP, in the BuildBurn tests (use the file's fixture for a config with a temp vault dir; write a `rollups-<month>.json` for root `codex` with `VaultStore.SaveRollupShard` if the MCP test project can see Core types through the linked files, otherwise write the JSON literally as the existing overlay tests do):

```csharp
    [Fact]
    public void Consented_agents_report_their_project_totals_from_the_vault()
    {
        // config: ConsentedRoots empty, ConsentedAgents ["codex"], VaultDirs [vault]; vault has codex rollups for today with
        // ByProject {"api~12345678": 300}.
        var result = logic.BuildBurn(7, fullPaths: false);
        Assert.Equal("ok", result["status"]!.GetValue<string>());
        var agent = (JsonObject)((JsonArray)result["agents"]!)[0]!;
        Assert.Equal("codex", agent["agent"]!.GetValue<string>());
        Assert.Equal(300, agent["total_tokens"]!.GetValue<long>());
    }

    [Fact]
    public void Without_agent_consent_no_agents_are_reported()
    {
        // config: ConsentedRoots with one root, ConsentedAgents empty.
        var result = logic.BuildBurn(7, fullPaths: false);
        Assert.Empty((JsonArray)result["agents"]!);
    }
```

Run the three test projects' filters. Expected: failures (no `mcp_agents`, no `agents`).

- [ ] **Step 2: Settings and the projection**

`SettingsStore.cs`, beside `SaveMcpRoots`:

```csharp
    /// <summary>Spec 3: whether Claude may read an agent's project totals ("mcp_agents"), off by default.</summary>
    public IReadOnlyDictionary<string, bool> LoadMcpAgents()
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (Read()["mcp_agents"] is JsonObject map)
            foreach (var (name, node) in map)
            {
                try { result[name] = node?.GetValue<bool>() ?? false; }
                catch { result[name] = false; }
            }
        return result;
    }

    public void SaveMcpAgent(string agent, bool on)
    {
        var root = Read();
        var map = root["mcp_agents"] as JsonObject ?? new JsonObject();
        map[agent] = on;
        root["mcp_agents"] = map;
        Write(root);
    }
```

`Write` already re-syncs the projection (`McpSettingsExport.Sync`), so the MCP sees the change.

`McpSettingsExport.Project`, before the return:

```csharp
        var agents = new JsonObject();
        if (settingsRoot["mcp_agents"] is JsonObject agentMap)
            foreach (var (name, node) in agentMap)
                if (name is AgentSources.Codex or AgentSources.Antigravity)
                    agents[name] = ReadBool(node);
```

and add `["mcp_agents"] = agents,` to the returned object.

- [ ] **Step 3: McpConfig and BuildBurn**

`McpConfig.cs`: add the property and read it with the same fail-closed reader shape as `ReadMcpRootsConsent`:

```csharp
    /// <summary>Agents whose project totals Claude may read (spec 3), from the projection's mcp_agents. The names mirror
    /// Core's AgentSources literally: linking that file would widen the trust boundary.</summary>
    public IReadOnlyList<string> ConsentedAgents { get; init; } = [];

    private static readonly string[] KnownAgents = ["codex", "antigravity"];   // mirrors AgentSources.Codex / .Antigravity
```

In the production path next to `var consentMap = ReadMcpRootsConsent(mcpSettingsPath);`, read the `mcp_agents` object the same way (copy `ReadMcpRootsConsent` into `ReadMcpAgentsConsent` reading `root["mcp_agents"]`), then set `ConsentedAgents = KnownAgents.Where(a => agentMap.TryGetValue(a, out var on) && on).ToList(),` in the returned config.

`ToolLogic.BuildBurn`: the disabled check becomes `if (_config.ConsentedRoots.Count == 0 && _config.ConsentedAgents.Count == 0)` (keep the message; add "or an agent" to the remedy text). After the roots loop:

```csharp
        var agents = new JsonArray();
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var from = DateOnly.FromDateTime(since.LocalDateTime);
        foreach (var agent in _config.ConsentedAgents)
        {
            var win = ReadVaultOverlay([agent], from, today.AddDays(1));
            var projects = new JsonArray();
            foreach (var (proj, tokens) in win.ByProjectName.OrderByDescending(p => p.Value))
                projects.Add((JsonNode)new JsonObject { ["name"] = proj, ["tokens"] = tokens });
            agents.Add((JsonNode)new JsonObject
            {
                ["agent"] = agent,
                ["total_tokens"] = win.ByProjectName.Values.Sum(),
                ["projects"] = projects,
            });
        }
```

and add to the response: `["agents_scanned"] = ToArray(_config.ConsentedAgents), ["agents"] = agents,`. Append to `BurnCaveat`'s use here (not the constant) the sentence: `" Agent totals come from the Vault and are as fresh as the widget's last ingest."` If `ByProjectName` keys are display names already (they are, per `VaultWindow`'s doc), use them as is; full paths are never stored for agents unless `store_full_paths` is on, so `fullPaths` does not apply to agent rows.

- [ ] **Step 4: The Folders page switches**

`SettingsNames.cs`:

```csharp
    public static string LetClaudeReadAgentTotals(string agent) => $"Let Claude read {agent}'s project totals";
```

Update `AgentFolderReadsCaption`'s text to also name agents, e.g. append: " The same goes for Codex and Antigravity, each with its own switch." If `AgentConsentsTests` pins the caption text, update that expectation in the same commit.

`ClaudeCodeViewModel.cs`:

```csharp
public sealed partial class AgentReadRowViewModel(string agent, bool on, Action<string, bool> save) : ObservableObject
{
    public string Agent { get; } = agent;
    public string Name => SettingsNames.LetClaudeReadAgentTotals(AgentSources.BurnName(Agent));
    [ObservableProperty] private bool _on = on;
    partial void OnOnChanged(bool value) => save(Agent, value);
}
```

In `ClaudeCodeViewModel`, add `public ObservableCollection<AgentReadRowViewModel> AgentReads { get; } = new();`, and where `Folders` is rebuilt (~line 469), rebuild it too:

```csharp
        AgentReads.Clear();
        var agentReads = _widget.LoadMcpAgents();
        var active = _widget.Vault?.ActiveAgents().Select(a => a.Agent).ToHashSet() ?? [];
        foreach (var agent in AgentSources.Burn.Where(active.Contains))
            AgentReads.Add(new AgentReadRowViewModel(agent, agentReads.GetValueOrDefault(agent), _widget.SaveMcpAgent));
```

`WidgetViewModel` gains `public IReadOnlyDictionary<string, bool> LoadMcpAgents() => _settings.LoadMcpAgents();` and `public void SaveMcpAgent(string agent, bool on) => _settings.SaveMcpAgent(agent, on);` beside `LoadMcpRoots`/`SaveMcpRoots`.

XAML, under the Folders list:

```xml
<ItemsControl ItemsSource="{Binding AgentReads}" Focusable="False" Margin="0,8,0,0">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <CheckBox Style="{DynamicResource ThemedCheckBox}" Content="{Binding Name}" Margin="0,0,0,6"
                      IsChecked="{Binding On, Mode=TwoWay}" AutomationProperties.Name="{Binding Name}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

- [ ] **Step 5: Run every suite, including the trust boundary**

Run: `dotnet test tests/Sanduhr.Tests`, `dotnet test tests/Sanduhr.Mcp.Tests`, `dotnet test tests/Sanduhr.App.Tests`. Expected: all PASS, with `TrustBoundaryTests` unchanged. If it fails, the MCP picked up a new linked file: undo that, mirror the name instead.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(mcp): agent project totals from the Vault behind a consent switch"
```

---

### Task 11: Privacy copy, changelog and the live smoke

**Files:**
- Modify: `docs/PRIVACY.md`, `windows-dotnet/CHANGELOG.md` (or the Windows changelog the release runbook names; check `docs/release-runbook.md`)
- No product code.

- [ ] **Step 1: Privacy and changelog**

`docs/PRIVACY.md`: in the section listing the local files Sanduhr reads, add one paragraph:

> When you switch them on in Settings, Usage, Sources, Sanduhr reads Codex's session logs (`%USERPROFILE%\.codex\sessions`) and the Antigravity CLI's conversation logs and conversation list (`%USERPROFILE%\.gemini\antigravity-cli`) on this PC, to count tokens per project and day. It reads timestamps, folder names, model names and token counts, never prompts or replies, and stores the counts in its usage vault on this PC. No network call, no sign-in. Claude reads an agent's project totals through Sanduhr's MCP server only after you turn on that agent's switch under Claude Code, Folders.

Changelog, under the unreleased Windows heading: "Every agent's activity: Codex and Antigravity CLI local burn in the vault, the widget footer, the Local burn page (strip, projects, sessions with an agent filter, CSV `agent` column) and the MCP's project totals." The Store's What's New, the hub privacy page and the reviewer letter are written at release time from this paragraph (release runbook phases), not in this task.

- [ ] **Step 2: Live smoke on this machine (counts and figures only)**

1. Quit the release Sanduhr. Back up `%LOCALAPPDATA%\Sanduhr` (the vault) and `%APPDATA%\Sanduhr`.
2. `dotnet build` and run the dev build. In Sources, keep Codex on and turn Antigravity on.
3. Wait for two ingest cycles (about ten minutes; run one Codex turn and one Antigravity CLI turn in between).
4. With UI Automation, read the widget footer text: it shows `Codex +…` and `Agy +…` after the second cycle.
5. Open Settings, Claude Code, the Local burn page: the strip's name lists three agents; the ledger's agent filter lists All, Claude Code, Codex, Antigravity; filtering to Codex shows only Codex rows.
6. Compare totals: a throwaway script in the scratchpad sums today's Codex `total_token_usage` deltas and Antigravity's `input + output` from the files (printing two numbers only) and matches the Vault's today rollup for `codex` and `antigravity` within the current cycle's lag.
7. Turn Antigravity off and purge its root on the Local burn page: its folder is gone and stays gone across a cycle; turn it on in Sources: it backfills.
8. Restore both backed-up folders and relaunch the release app.

Record pass or fail per item in the PR description. Never paste file contents or real prompts.

- [ ] **Step 3: Commit**

```bash
git add docs/PRIVACY.md windows-dotnet/CHANGELOG.md
git commit -m "docs: privacy and changelog for every agent's activity"
```
