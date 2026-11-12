# The Agent Layer (Claude and Codex) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Codex's 5-hour and weekly plan limits, read from Codex's own local logs, show beside Claude's in the widget, on the Desk, in alerts, the MCP and the statusline, with source colors and a two-tone combined bar.

**Architecture:** A source's limits use the same `JsonObject` tier shape as Claude's fetch (`{tierKey: {utilization, resets_at}}`), so Codex's windows are the tiers `five_hour` and `seven_day` inside the Codex source and `Pacing`, `TierCardViewModel` and `DeskMeters` work unchanged. A pure Core parser and a Core reader (decompressor injected) produce a `CodexLimitsReading`; an App service polls it, writes history under the reserved account `@codex`, writes `snapshot.codex.json`, and raises alerts through its own `AlertEngine`. Pure layout maths in Core places the two-tone bar.

**Tech Stack:** .NET 10, WPF, xUnit (+ Xunit.SkippableFact in App tests), ZstdSharp.Port (App only), PowerShell (statusline script).

**Spec:** `docs/superpowers/specs/2026-11-09-agent-layer-design.md` (read it first; step 0 already passed, verdict in `windows-dotnet/docs/checklist.md`).

## Global Constraints

- Never open `%USERPROFILE%\.codex\auth.json`, never call the network for Codex, never read message text or account ids from a rollout.
- `Sanduhr.Core.csproj` keeps zero PackageReferences: zstd lives in `Sanduhr.App` behind `Func<Stream, Stream>`.
- `Sanduhr.Mcp.csproj`'s source-link list does not change; `tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs` passes untouched.
- `snapshot.json`'s format does not change. Codex writes `snapshot.codex.json` in `%USERPROFILE%\.sanduhr\` (`Paths.ExternalDir`), schema 1 plus root `"source": "codex"`, no `account_ref`.
- Codex history lives at `history.@codex.json` (account `@codex`). Claude labels match `^[A-Za-z0-9 _-]{1,32}$` (`AccountStore.cs:39`), so `@` can't collide.
- Fresh means at most 30 minutes since `as_of`; stale limits are dimmed, drawn on combined bars dimmed, and never alert. A window whose `resets_at` has passed shows 0% and counts as fresh.
- Codex windows map by `window_minutes`: 300 to `five_hour`, 10080 to `seven_day`; any other value is dropped and logged once.
- Read the newest `token_count` event whose `rate_limits` has a non-null `primary` or `secondary` (step 0: `limit_id: "premium"` events with null windows interleave).
- Colors evoke the brands, never their exact brand hexes; color is never the only signal (accessible names and tooltips on every tick and segment).
- CRLF working tree, LF index: never `sed -i` a tracked file. Quit `Sanduhr.exe` before any `dotnet publish` (no task publishes).
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Copy voice: plain, specific, periods at the end, no em-dashes.

## Review Focus

1. **The MCP's history reader enumerates `history.*.json`** (`src/Sanduhr.Mcp/HistoryReader.cs:29`), so `history.@codex.json` would be read as a Claude account and could win "freshest". Expected: it is skipped. Test added in Task 5.
2. **A Codex rollout being written while read** (Codex appends while the widget polls). Expected: a truncated last line is skipped, the previous good event is used, no exception. Test added in Task 2.
3. **Codex not run for weeks, then reset times all passed.** Expected: both windows show 0% "reset at …", fresh, and no alert fires for the old percent. Test added in Task 1 (`ToTiers`) and Task 5 (alerts).
4. **Codex ticked but the source then uninstalled or `CODEX_HOME` pointing at a missing folder.** Expected: "Codex isn't installed on this PC." and no card, no crash on the 30 s poll. Test added in Task 2.
5. **Reset (Settings ▸ Reset) and the vault.** Expected: Reset deletes `snapshot.codex.json` and `history.@codex.json` by name and nothing else in `~\.sanduhr`; the usage vault and Publish never list `@codex` as an account. Test added in Task 5.

---

### Task 1: Codex limits model and line parser (Core, pure)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/CodexLimits.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/CodexLimitsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CodexWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt)`
  - `public sealed record CodexLimitsReading(DateTimeOffset AsOf, CodexWindow? Primary, CodexWindow? Secondary, string? PlanType)`
  - `public static class CodexLimits` with `const string FiveHour = "five_hour"`, `const string SevenDay = "seven_day"`, `static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30)`, `static bool TryParseLine(string line, out CodexLimitsReading reading)`, `static JsonObject ToTiers(CodexLimitsReading reading, DateTimeOffset now, Action<int>? unknownWindow = null)`, `static bool IsFresh(CodexLimitsReading reading, DateTimeOffset now)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexLimitsTests
{
    private const string Good = """{"timestamp":"2026-10-22T23:48:09.060Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","limit_name":null,"primary":{"used_percent":99.0,"window_minutes":300,"resets_at":1792721664},"secondary":{"used_percent":16.0,"window_minutes":10080,"resets_at":1793308464},"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"plan_type":"plus"}}}""";
    private const string Premium = """{"timestamp":"2026-10-22T23:47:55.788Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null,"plan_type":"plus"}}}""";

    [Fact]
    public void Parses_both_windows_and_the_plan()
    {
        Assert.True(CodexLimits.TryParseLine(Good, out var r));
        Assert.Equal(DateTimeOffset.Parse("2026-10-22T23:48:09.060Z"), r.AsOf);
        Assert.Equal(99.0, r.Primary!.UsedPercent);
        Assert.Equal(300, r.Primary.WindowMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792721664), r.Primary.ResetsAt);
        Assert.Equal(10080, r.Secondary!.WindowMinutes);
        Assert.Equal("plus", r.PlanType);
    }

    [Fact]
    public void A_premium_event_with_no_windows_is_not_a_reading() =>
        Assert.False(CodexLimits.TryParseLine(Premium, out _));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"timestamp":"2026-10-22T23:48:09Z","type":"response_item","payload":{"type":"message"}}""")]
    [InlineData("""{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}""")]
    [InlineData("""{"timestamp":"not a date","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}""")]
    public void Anything_else_is_not_a_reading(string line) =>
        Assert.False(CodexLimits.TryParseLine(line, out _));

    [Fact]
    public void A_window_without_a_reset_still_parses()
    {
        const string line = """{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300}}}}""";
        Assert.True(CodexLimits.TryParseLine(line, out var r));
        Assert.Null(r.Primary!.ResetsAt);
        Assert.Null(r.Secondary);
    }

    [Fact]
    public void ToTiers_maps_by_window_length_not_by_position()
    {
        var swapped = new CodexLimitsReading(DateTimeOffset.UnixEpoch,
            Primary: new CodexWindow(16, 10080, null), Secondary: new CodexWindow(99, 300, null), PlanType: null);
        var tiers = CodexLimits.ToTiers(swapped, DateTimeOffset.UnixEpoch);
        Assert.Equal(99, tiers[CodexLimits.FiveHour]!["utilization"]!.GetValue<double>());
        Assert.Equal(16, tiers[CodexLimits.SevenDay]!["utilization"]!.GetValue<double>());
    }

    [Fact]
    public void ToTiers_drops_an_unknown_window_and_reports_it()
    {
        var odd = new CodexLimitsReading(DateTimeOffset.UnixEpoch, new CodexWindow(40, 1440, null), null, null);
        var seen = new List<int>();
        var tiers = CodexLimits.ToTiers(odd, DateTimeOffset.UnixEpoch, seen.Add);
        Assert.Empty(tiers);
        Assert.Equal(new[] { 1440 }, seen);
    }

    [Fact]
    public void A_passed_reset_reads_zero_and_keeps_its_reset_time()
    {
        var reset = DateTimeOffset.Parse("2026-10-23T02:00:00Z");
        var r = new CodexLimitsReading(DateTimeOffset.Parse("2026-10-22T23:00:00Z"), new CodexWindow(99, 300, reset), null, null);
        var tiers = CodexLimits.ToTiers(r, reset.AddMinutes(1));
        Assert.Equal(0, tiers[CodexLimits.FiveHour]!["utilization"]!.GetValue<double>());
        Assert.Equal(reset.ToString("o"), tiers[CodexLimits.FiveHour]!["resets_at"]!.GetValue<string>());
    }

    [Fact]
    public void Fresh_for_thirty_minutes_or_after_any_passed_reset()
    {
        var asOf = DateTimeOffset.Parse("2026-10-22T23:00:00Z");
        var r = new CodexLimitsReading(asOf, new CodexWindow(10, 300, asOf.AddHours(3)), null, null);
        Assert.True(CodexLimits.IsFresh(r, asOf.AddMinutes(30)));
        Assert.False(CodexLimits.IsFresh(r, asOf.AddMinutes(31)));
        Assert.True(CodexLimits.IsFresh(r, asOf.AddHours(3).AddSeconds(1)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter CodexLimitsTests`
Expected: build FAIL, `CodexLimits` not found.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One Codex plan window from a rollout's <c>rate_limits</c>.</summary>
public sealed record CodexWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt);

/// <summary>The newest Codex limits Sanduhr could read, and when Codex wrote them.</summary>
public sealed record CodexLimitsReading(DateTimeOffset AsOf, CodexWindow? Primary, CodexWindow? Secondary, string? PlanType);

/// <summary>
/// Parses Codex's <c>token_count</c> rollout events (shape confirmed 2026-11-09, checklist "Codex limits
/// check") and turns a reading into the same tier object Claude's fetch produces. Reads only percents,
/// window lengths, reset times, the event timestamp and the plan name; never message text or ids.
/// </summary>
public static class CodexLimits
{
    public const string FiveHour = TierModel.FiveHour;
    public const string SevenDay = TierModel.SevenDay;
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

    public static bool TryParseLine(string line, out CodexLimitsReading reading)
    {
        reading = null!;
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return false;
            if (!p.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() != "token_count") return false;
            if (!p.TryGetProperty("rate_limits", out var rl) || rl.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var asOf))
                return false;
            var primary = Window(rl, "primary");
            var secondary = Window(rl, "secondary");
            if (primary is null && secondary is null) return false;
            string? plan = rl.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null;
            reading = new CodexLimitsReading(asOf, primary, secondary, plan);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CodexWindow? Window(JsonElement rl, string name)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("used_percent", out var up) || up.ValueKind != JsonValueKind.Number) return null;
        if (!w.TryGetProperty("window_minutes", out var wm) || !wm.TryGetInt32(out var minutes)) return null;
        DateTimeOffset? resets = w.TryGetProperty("resets_at", out var ra) && ra.TryGetInt64(out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
        return new CodexWindow(Math.Clamp(up.GetDouble(), 0, 100), minutes, resets);
    }

    public static JsonObject ToTiers(CodexLimitsReading reading, DateTimeOffset now, Action<int>? unknownWindow = null)
    {
        var tiers = new JsonObject();
        foreach (var w in new[] { reading.Primary, reading.Secondary })
        {
            if (w is null) continue;
            string? key = w.WindowMinutes switch { 300 => FiveHour, 10080 => SevenDay, _ => null };
            if (key is null) { unknownWindow?.Invoke(w.WindowMinutes); continue; }
            bool passed = w.ResetsAt is { } r && r <= now;
            tiers[key] = new JsonObject
            {
                ["utilization"] = passed ? 0d : w.UsedPercent,
                ["resets_at"] = w.ResetsAt?.ToString("o", CultureInfo.InvariantCulture),
            };
        }
        return tiers;
    }

    public static bool IsFresh(CodexLimitsReading reading, DateTimeOffset now)
    {
        if (now - reading.AsOf <= FreshFor) return true;
        return Passed(reading.Primary, now) || Passed(reading.Secondary, now);
    }

    private static bool Passed(CodexWindow? w, DateTimeOffset now) => w?.ResetsAt is { } r && r <= now;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter CodexLimitsTests`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/CodexLimits.cs windows-dotnet/tests/Sanduhr.Tests/CodexLimitsTests.cs
git commit -m "feat(codex): parse Codex plan limits from rollout events"
```

---

### Task 2: Codex homes and the rollout reader (Core IO, decompressor injected) and the zstd adapter (App)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/CodexLimitsReader.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Services/ZstdRollouts.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj` (add `<PackageReference Include="ZstdSharp.Port" Version="0.8.5" />` beside the others at lines 14-26; if restore can't find 0.8.5, use the newest 0.8.x on nuget.org that is at least two weeks old and note it in the commit)
- Create: `windows-dotnet/tests/Sanduhr.Tests/fixtures/codex/` (fixture rollouts, built by the test itself, no binary files committed)
- Test: `windows-dotnet/tests/Sanduhr.Tests/CodexLimitsReaderTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/ZstdRolloutsTests.cs`

**Interfaces:**
- Consumes: `CodexLimits.TryParseLine`, `CodexLimitsReading` (Task 1).
- Produces:
  - `public static class CodexHomes { public static IReadOnlyList<string> Discover(string? codexHomeEnv, string userProfile); }` returns existing directories only, `CODEX_HOME` first, de-duplicated case-insensitively.
  - `public enum CodexReadStatus { NotInstalled, NoLimits, Ok, Unreadable }`
  - `public sealed record CodexReadResult(CodexReadStatus Status, CodexLimitsReading? Reading)`
  - `public sealed class CodexLimitsReader { public CodexLimitsReader(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst, Action<string>? log = null); public CodexReadResult Read(); public const long MaxDecompressedBytes = 64L * 1024 * 1024; public const int MaxTailBytes = 4 * 1024 * 1024; public const int MaxFiles = 20; }`
  - `Sanduhr.App.Services.ZstdRollouts.Decompress(Stream compressed) : Stream` (wraps `ZstdSharp.DecompressionStream`).

Rules the reader implements: list `sessions/**/rollout-*.jsonl` and `rollout-*.jsonl.zst` under every home; sort by `LastWriteTimeUtc` descending across homes; read at most `MaxFiles`; for `.jsonl` read the last `MaxTailBytes`; for `.zst` decompress through the injected function into memory, stopping with that file counted unreadable past `MaxDecompressedBytes`; split lines, scan from the end, return the first line `TryParseLine` accepts. Status: no homes, `NotInstalled`; homes but no rollouts or no accepted line in any file, `NoLimits`; every file threw, `Unreadable`. Never throw out of `Read`. Log messages carry no path (streamer mode): "codex: a rollout could not be read (IOException)". A `.zst` with no decompressor is skipped.

- [ ] **Step 1: Write the failing Core tests**

Use Core's `TempDir` from `tests/Sanduhr.Tests/PersistenceTestHelpers.cs:33` (read it first for its member names; below assumes `.Path` and `IDisposable`).

```csharp
using System.IO.Compression;
using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexLimitsReaderTests
{
    private static string Event(string ts, double five, double week) =>
        $$"""{"timestamp":"{{ts}}","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":{{five}},"window_minutes":300,"resets_at":1792721664},"secondary":{"used_percent":{{week}},"window_minutes":10080,"resets_at":1793308464},"plan_type":"plus"}}}""";
    private const string Premium = """{"timestamp":"2026-10-22T23:59:00Z","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null}}}""";

    // Tests use GZip as a stand-in compressor: the reader only sees Func<Stream, Stream>.
    private static Stream Gunzip(Stream s) => new GZipStream(s, CompressionMode.Decompress);

    private static string Rollout(string home, string day, string name, string body, bool compressed = false, DateTime? mtime = null)
    {
        var dir = Path.Combine(home, "sessions", "2026", "10", day);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + (compressed ? ".jsonl.zst" : ".jsonl"));
        if (compressed)
        {
            using var f = File.Create(path);
            using var gz = new GZipStream(f, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(body));
        }
        else File.WriteAllText(path, body);
        if (mtime is { } m) File.SetLastWriteTimeUtc(path, m);
        return path;
    }

    private static CodexLimitsReader Reader(params string[] homes) => new(() => homes, Gunzip);

    [Fact]
    public void No_home_is_not_installed() =>
        Assert.Equal(CodexReadStatus.NotInstalled, Reader().Read().Status);

    [Fact]
    public void A_home_with_no_rollouts_has_no_limits()
    {
        using var t = new TempDir();
        Assert.Equal(CodexReadStatus.NoLimits, Reader(t.Path).Read().Status);
    }

    [Fact]
    public void Skips_a_trailing_premium_event_and_a_torn_last_line()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-a", Event("2026-10-22T23:48:00Z", 40, 10) + "\n" + Premium + "\n" + "{\"timestamp\":\"2026-10-22T23:59:30Z\",\"payl");
        var r = Reader(t.Path).Read();
        Assert.Equal(CodexReadStatus.Ok, r.Status);
        Assert.Equal(40, r.Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void Reads_compressed_rollouts_through_the_injected_decompressor()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", Event("2026-10-22T23:48:00Z", 55, 12) + "\n", compressed: true);
        Assert.Equal(55, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_compressed_rollout_with_no_decompressor_is_skipped()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", Event("2026-10-22T23:48:00Z", 55, 12) + "\n", compressed: true);
        Assert.Equal(CodexReadStatus.NoLimits, new CodexLimitsReader(() => [t.Path], null).Read().Status);
    }

    [Fact]
    public void The_newest_file_across_two_homes_wins()
    {
        using var a = new TempDir();
        using var b = new TempDir();
        Rollout(a.Path, "21", "rollout-old", Event("2026-10-21T10:00:00Z", 10, 1) + "\n", mtime: new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));
        Rollout(b.Path, "22", "rollout-new", Event("2026-10-22T10:00:00Z", 70, 7) + "\n", mtime: new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(70, Reader(a.Path, b.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_newest_file_with_no_limits_falls_back_to_the_next()
    {
        using var t = new TempDir();
        Rollout(t.Path, "21", "rollout-old", Event("2026-10-21T10:00:00Z", 33, 3) + "\n", mtime: new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));
        Rollout(t.Path, "22", "rollout-new", Premium + "\n", mtime: new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(33, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_decompressor_that_throws_on_every_file_is_unreadable_not_a_crash()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", "garbage", compressed: true);
        var r = new CodexLimitsReader(() => [t.Path], _ => throw new InvalidDataException("bad frame")).Read();
        Assert.Equal(CodexReadStatus.Unreadable, r.Status);
    }

    [Fact]
    public void Never_opens_auth_json()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Sanduhr.Core", "CodexLimitsReader.cs"));
        Assert.DoesNotContain("auth", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Homes_keep_existing_folders_only_with_CODEX_HOME_first()
    {
        using var profile = new TempDir();
        using var custom = new TempDir();
        Directory.CreateDirectory(Path.Combine(profile.Path, ".codex"));
        Assert.Equal(new[] { custom.Path, Path.Combine(profile.Path, ".codex") }, CodexHomes.Discover(custom.Path, profile.Path));
        Assert.Equal(new[] { Path.Combine(profile.Path, ".codex") }, CodexHomes.Discover(Path.Combine(custom.Path, "missing"), profile.Path));
        Assert.Single(CodexHomes.Discover(Path.Combine(profile.Path, ".codex"), profile.Path));
    }
}
```

The `Never_opens_auth_json` path walks from `tests/Sanduhr.Tests/bin/<cfg>/net10.0/` up to the repo's `windows-dotnet/`; if the existing tests resolve source paths another way (grep `TrustBoundaryTests` or `StatuslineScriptRunTests` for a `RepoRoot` helper), use that helper instead.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter CodexLimitsReaderTests`
Expected: build FAIL, `CodexLimitsReader` not found.

- [ ] **Step 3: Implement `CodexLimitsReader.cs`**

```csharp
using System.Text;

namespace Sanduhr.Core;

public static class CodexHomes
{
    public static IReadOnlyList<string> Discover(string? codexHomeEnv, string userProfile)
    {
        var found = new List<string>();
        foreach (var candidate in new[] { codexHomeEnv, Path.Combine(userProfile, ".codex") })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string full;
            try { full = Path.GetFullPath(candidate); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!Directory.Exists(full)) continue;
            if (found.Any(f => string.Equals(f.TrimEnd('\\', '/'), full.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))) continue;
            found.Add(full);
        }
        return found;
    }
}

public enum CodexReadStatus { NotInstalled, NoLimits, Ok, Unreadable }

public sealed record CodexReadResult(CodexReadStatus Status, CodexLimitsReading? Reading);

/// <summary>
/// Finds the newest Codex limits in the rollouts under every Codex home. Plain <c>.jsonl</c> is read from its
/// tail; <c>.jsonl.zst</c> goes through the injected decompressor (Core has no package references). Reads
/// nothing outside <c>sessions</c>. Never throws.
/// </summary>
public sealed class CodexLimitsReader
{
    public const long MaxDecompressedBytes = 64L * 1024 * 1024;
    public const int MaxTailBytes = 4 * 1024 * 1024;
    public const int MaxFiles = 20;

    private readonly Func<IReadOnlyList<string>> _homes;
    private readonly Func<Stream, Stream>? _decompressZst;
    private readonly Action<string>? _log;

    public CodexLimitsReader(Func<IReadOnlyList<string>> homes, Func<Stream, Stream>? decompressZst, Action<string>? log = null)
    {
        _homes = homes;
        _decompressZst = decompressZst;
        _log = log;
    }

    public CodexReadResult Read()
    {
        IReadOnlyList<string> homes;
        try { homes = _homes(); } catch (Exception e) { _log?.Invoke($"codex: homes failed ({e.GetType().Name})"); return new(CodexReadStatus.NotInstalled, null); }
        if (homes.Count == 0) return new(CodexReadStatus.NotInstalled, null);

        var files = new List<FileInfo>();
        foreach (var home in homes)
        {
            var sessions = Path.Combine(home, "sessions");
            if (!Directory.Exists(sessions)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(sessions, "rollout-*", SearchOption.AllDirectories))
                    if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase))
                        files.Add(new FileInfo(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"codex: a sessions folder could not be listed ({e.GetType().Name})");
            }
        }
        if (files.Count == 0) return new(CodexReadStatus.NoLimits, null);

        int tried = 0, failed = 0;
        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxFiles))
        {
            tried++;
            string? text;
            try { text = ReadText(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                failed++;
                _log?.Invoke($"codex: a rollout could not be read ({e.GetType().Name})");
                continue;
            }
            if (text is null) continue;
            var lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
                if (CodexLimits.TryParseLine(lines[i].TrimEnd('\r'), out var reading))
                    return new(CodexReadStatus.Ok, reading);
        }
        return new(tried > 0 && failed == tried ? CodexReadStatus.Unreadable : CodexReadStatus.NoLimits, null);
    }

    private string? ReadText(FileInfo file)
    {
        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Name.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
        {
            if (_decompressZst is null) return null;
            using var inflated = _decompressZst(fs);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int n;
            while ((n = inflated.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + n > MaxDecompressedBytes) throw new InvalidDataException("rollout over the size cap");
                buffer.Write(chunk, 0, n);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        long start = Math.Max(0, fs.Length - MaxTailBytes);
        fs.Seek(start, SeekOrigin.Begin);
        var bytes = new byte[fs.Length - start];
        fs.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
}
```

`GZipStream` throws `InvalidDataException` on garbage, and ZstdSharp throws `ZstdException`, which derives from `Exception`. So `ZstdRollouts.Decompress` must translate ZstdSharp's exceptions into `InvalidDataException` (Step 5) to keep the catch list closed.

- [ ] **Step 4: Run the Core tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "CodexLimitsReaderTests|CodexLimitsTests"`
Expected: PASS.

- [ ] **Step 5: The zstd adapter in App, test first**

`tests/Sanduhr.App.Tests/ZstdRolloutsTests.cs`:

```csharp
using System.Text;
using Sanduhr.App.Services;
using Xunit;

namespace Sanduhr.App.Tests;

public class ZstdRolloutsTests
{
    [Fact]
    public void Round_trips_a_zstd_frame()
    {
        var text = "{\"a\":1}\n{\"b\":2}\n";
        using var compressor = new ZstdSharp.Compressor();
        var frame = compressor.Wrap(Encoding.UTF8.GetBytes(text)).ToArray();
        using var s = ZstdRollouts.Decompress(new MemoryStream(frame));
        using var r = new StreamReader(s);
        Assert.Equal(text, r.ReadToEnd());
    }

    [Fact]
    public void Garbage_reads_as_invalid_data()
    {
        using var s = ZstdRollouts.Decompress(new MemoryStream(Encoding.UTF8.GetBytes("not zstd")));
        Assert.Throws<InvalidDataException>(() => s.ReadByte());
    }
}
```

`src/Sanduhr.App/Services/ZstdRollouts.cs`:

```csharp
namespace Sanduhr.App.Services;

/// <summary>The zstd decompressor <see cref="Sanduhr.Core.CodexLimitsReader"/> takes; Core has no packages.</summary>
public static class ZstdRollouts
{
    public static Stream Decompress(Stream compressed) => new Translating(new ZstdSharp.DecompressionStream(compressed));

    private sealed class Translating(Stream inner) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            try { return inner.Read(buffer, offset, count); }
            catch (ZstdSharp.ZstdException e) { throw new InvalidDataException("zstd: " + e.Message, e); }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
```

If ZstdSharp's garbage-input exception is not `ZstdException` in the version restored, catch the type the test shows and translate that.

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter ZstdRolloutsTests`
Expected: PASS.

- [ ] **Step 6: Live check on this machine (no commit of data)**

Write a scratch test or a one-off `dotnet test --filter` run that is NOT committed: construct `new CodexLimitsReader(() => CodexHomes.Discover(Environment.GetEnvironmentVariable("CODEX_HOME"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), ZstdRollouts.Decompress)` and print `Status` and the two percents. Expected on Neb: `Ok`, a five-hour and weekly percent matching the step 0 probe (99 and 16 at 2026-10-22T23:48:09Z). Delete the scratch file.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/CodexLimitsReader.cs windows-dotnet/src/Sanduhr.App/Services/ZstdRollouts.cs windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj windows-dotnet/tests/Sanduhr.Tests/CodexLimitsReaderTests.cs windows-dotnet/tests/Sanduhr.App.Tests/ZstdRolloutsTests.cs
git commit -m "feat(codex): read the newest limits from plain and zstd rollouts"
```

---

### Task 3: Agent sources, colors and the two-tone bar maths (Core, pure)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AgentSources.cs`
- Create: `windows-dotnet/src/Sanduhr.Core/SourceBar.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SourceBarTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/AgentSourceColorTests.cs`

**Interfaces:**
- Produces:
  - `public static class AgentSources { public const string Claude = "claude"; public const string Codex = "codex"; public static readonly IReadOnlyList<string> All = [Claude, Codex]; public static string Name(string id); public static string DefaultColor(string id); }` Names: "Claude", "Codex". Default colors: Claude `#E0875A`, Codex `#2FB5A3` (adjust only if Step 4's contrast test fails, by the smallest HSL lightness change that passes, hue and saturation kept).
  - `public sealed record SourcePercent(string Source, double Percent, bool Stale)`
  - `public sealed record BarSegment(string Source, double From, double To, bool Stale)` (fractions 0..1)
  - `public sealed record BarTick(string Source, double At, bool Stale)`
  - `public sealed record SourceBarLayout(BarSegment? Primary, BarSegment? Beyond, IReadOnlyList<BarTick> Ticks)`
  - `public static class SourceBar { public static SourceBarLayout Layout(string primary, IReadOnlyList<SourcePercent> sources); public static string AccessibleName(string windowLabel, IReadOnlyList<SourcePercent> sources); }`

Rules: the primary fills `[0, p]`. Among the others, the one with the highest percent strictly greater than `p` (ties broken by `AgentSources.All` order) gets `Beyond = [p, q]`. Every other non-primary source, including the beyond one, gets a tick at its percent. If the primary is absent from `sources`, the highest source is drawn as the primary. Percents clamp to 0..100. `AccessibleName("Weekly", …)` reads "Weekly. Claude 40%. Codex 70%, as of earlier." with "as of earlier" only on stale entries, in `All` order.

- [ ] **Step 1: Write the failing tests**

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SourceBarTests
{
    private static SourcePercent S(string s, double p, bool stale = false) => new(s, p, stale);

    [Fact]
    public void Primary_fills_and_a_source_further_out_continues_the_bar()
    {
        var l = SourceBar.Layout("claude", [S("claude", 40), S("codex", 70)]);
        Assert.Equal(new BarSegment("claude", 0, 0.40, false), l.Primary);
        Assert.Equal(new BarSegment("codex", 0.40, 0.70, false), l.Beyond);
        Assert.Equal(new[] { new BarTick("codex", 0.70, false) }, l.Ticks);
    }

    [Fact]
    public void A_source_behind_the_primary_is_only_a_tick()
    {
        var l = SourceBar.Layout("claude", [S("claude", 80), S("codex", 30)]);
        Assert.Null(l.Beyond);
        Assert.Equal(new[] { new BarTick("codex", 0.30, false) }, l.Ticks);
    }

    [Fact]
    public void Equal_percents_draw_no_beyond_segment()
    {
        Assert.Null(SourceBar.Layout("claude", [S("claude", 50), S("codex", 50)]).Beyond);
    }

    [Fact]
    public void A_missing_primary_draws_the_highest_as_the_fill()
    {
        var l = SourceBar.Layout("claude", [S("codex", 25)]);
        Assert.Equal("codex", l.Primary!.Source);
        Assert.Empty(l.Ticks);
    }

    [Fact]
    public void Percents_clamp_and_staleness_carries()
    {
        var l = SourceBar.Layout("codex", [S("codex", 120, stale: true), S("claude", -5)]);
        Assert.Equal(new BarSegment("codex", 0, 1, true), l.Primary);
        Assert.Equal(new[] { new BarTick("claude", 0, false) }, l.Ticks);
    }

    [Fact]
    public void No_sources_is_an_empty_bar()
    {
        var l = SourceBar.Layout("claude", []);
        Assert.Null(l.Primary);
        Assert.Null(l.Beyond);
        Assert.Empty(l.Ticks);
    }

    [Fact]
    public void Accessible_name_lists_every_source_in_a_fixed_order()
    {
        Assert.Equal("Weekly. Claude 40%. Codex 70%, as of earlier.",
            SourceBar.AccessibleName("Weekly", [S("codex", 70, stale: true), S("claude", 40)]));
    }
}

public class AgentSourceColorTests
{
    public static TheoryData<string, string> Pairs()
    {
        var d = new TheoryData<string, string>();
        foreach (var theme in ThemeCatalog.BuiltInOrder)
            foreach (var source in AgentSources.All)
                d.Add(theme, source);
        return d;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Default_colors_read_on_every_built_in_glass(string theme, string source)
    {
        var def = ThemeCatalog.BuiltIns[theme];
        Assert.True(ThemeLint.TryParseHex(def.Glass, out var glass));
        Assert.True(ThemeContrast.Ratio(AgentSources.DefaultColor(source), glass) >= ThemeContrast.NonTextMin,
            $"{source} on {theme} glass");
    }

    [Fact]
    public void Defaults_are_not_the_brands_exact_hexes()
    {
        Assert.NotEqual("#D97757", AgentSources.DefaultColor(AgentSources.Claude), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("#10A37F", AgentSources.DefaultColor(AgentSources.Codex), StringComparer.OrdinalIgnoreCase);
    }
}
```

`ThemeCatalog.BuiltInOrder`, `ThemeCatalog.BuiltIns[key].Glass`, `ThemeLint.TryParseHex` and `ThemeContrast.Ratio(string, (double,double,double))` / `NonTextMin` exist (`ThemeContrastTests.cs:15`, `:117-121`; `ThemeContrast.cs:20,46`). Match their exact signatures when compiling.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "SourceBarTests|AgentSourceColorTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`AgentSources.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>The usage sources Sanduhr can track. Colors evoke each brand without copying its hex (Store 10.1.4.4).</summary>
public static class AgentSources
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public static readonly IReadOnlyList<string> All = [Claude, Codex];

    public static string Name(string id) => id switch
    {
        Claude => "Claude",
        Codex => "Codex",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static string DefaultColor(string id) => id switch
    {
        Claude => "#E0875A",
        Codex => "#2FB5A3",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown source"),
    };

    public static int Order(string id)
    {
        for (int i = 0; i < All.Count; i++) if (All[i] == id) return i;
        return int.MaxValue;
    }
}
```

`SourceBar.cs`:

```csharp
using System.Globalization;

namespace Sanduhr.Core;

public sealed record SourcePercent(string Source, double Percent, bool Stale);
public sealed record BarSegment(string Source, double From, double To, bool Stale);
public sealed record BarTick(string Source, double At, bool Stale);
public sealed record SourceBarLayout(BarSegment? Primary, BarSegment? Beyond, IReadOnlyList<BarTick> Ticks);

/// <summary>
/// The two-tone combined bar (spec section 3): the primary fills to its percent; the furthest source past it
/// continues the bar in its own color; every other source is a tick.
/// </summary>
public static class SourceBar
{
    public static SourceBarLayout Layout(string primary, IReadOnlyList<SourcePercent> sources)
    {
        if (sources.Count == 0) return new(null, null, []);
        var ordered = sources
            .Select(s => s with { Percent = Math.Clamp(s.Percent, 0, 100) })
            .OrderBy(s => AgentSources.Order(s.Source))
            .ToList();
        var main = ordered.FirstOrDefault(s => s.Source == primary)
            ?? ordered.OrderByDescending(s => s.Percent).ThenBy(s => AgentSources.Order(s.Source)).First();
        double p = main.Percent / 100;
        var others = ordered.Where(s => s.Source != main.Source).ToList();
        var beyond = others.Where(s => s.Percent / 100 > p)
            .OrderByDescending(s => s.Percent).ThenBy(s => AgentSources.Order(s.Source)).FirstOrDefault();
        return new(
            new BarSegment(main.Source, 0, p, main.Stale),
            beyond is null ? null : new BarSegment(beyond.Source, p, beyond.Percent / 100, beyond.Stale),
            others.Select(s => new BarTick(s.Source, s.Percent / 100, s.Stale)).ToList());
    }

    public static string AccessibleName(string windowLabel, IReadOnlyList<SourcePercent> sources)
    {
        var parts = sources.OrderBy(s => AgentSources.Order(s.Source)).Select(s =>
            string.Create(CultureInfo.InvariantCulture, $"{AgentSources.Name(s.Source)} {Math.Round(Math.Clamp(s.Percent, 0, 100))}%{(s.Stale ? ", as of earlier" : "")}."));
        return windowLabel + ". " + string.Join(" ", parts);
    }
}
```

`BarTick` list equality in the tests compares element by element via `Assert.Equal(IEnumerable)`; records compare by value, and the doubles are exact divisions here (0.40 is `40/100`). If an assertion fails on floating point, compare with `Assert.Equal(expected, actual, precision: 9)` per field instead of changing the maths.

- [ ] **Step 4: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "SourceBarTests|AgentSourceColorTests"`
Expected: PASS. If a color fails on one theme, raise or lower its HSL lightness by the smallest step that passes on all six, keep hue and saturation, and record the final hexes in the commit message.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/AgentSources.cs windows-dotnet/src/Sanduhr.Core/SourceBar.cs windows-dotnet/tests/Sanduhr.Tests/SourceBarTests.cs windows-dotnet/tests/Sanduhr.Tests/AgentSourceColorTests.cs
git commit -m "feat(usage): source colors and the two-tone combined bar layout"
```

---

### Task 4: Source settings (setup, primary, colors, alerts)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SetupSettings.cs` (`SetupSettings` :10, `SetupChoices` :45, `ToSettings` :59)
- Create: `windows-dotnet/src/Sanduhr.Core/SourceSettings.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (beside `LoadSetup` :884 / `SaveSetup` :892)
- Test: `windows-dotnet/tests/Sanduhr.Tests/SetupSettingsTests.cs` (extend), `windows-dotnet/tests/Sanduhr.Tests/SourceSettingsTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/SetupStoreTests.cs` (extend)

**Interfaces:**
- Consumes: `AgentSources` (Task 3).
- Produces:
  - `SetupSettings.Codex = "codex"`, `bool CodexUsage`, `SetupSettings WithCodex(bool on)`; `UsageOn` stays "any source on".
  - `SetupChoices` gains `bool CodexUsage` (constructor parameter added last with default `false` so existing call sites compile); `ToSettings` writes both sources and keeps unknown ones.
  - `public sealed record SourceSettings(string Primary, IReadOnlyDictionary<string, string> Colors, IReadOnlyDictionary<string, bool> Alerts, IReadOnlySet<string> Collapsed)` with `static SourceSettings Default`, `string ColorOf(string source)`, `bool AlertsOn(string source)`, `string PrimaryFor(SetupSettings setup)` (the saved primary if that source is on, else the first ticked source in `AgentSources.All`, else `Claude`), `JsonObject Encode()`, `static SourceSettings Decode(JsonNode?)`. Stored at settings.json key `sources`: `{"primary":"claude","colors":{"codex":"#2FB5A3"},"alerts":{"codex":true},"collapsed":["codex"]}`. Colors absent or not `#RRGGBB` fall back to the default. Alerts absent mean on.
  - `SettingsStore.LoadSources()` / `SaveSources(SourceSettings)`.

- [ ] **Step 1: Failing tests**

`SourceSettingsTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SourceSettingsTests
{
    private static SetupSettings Setup(bool claude, bool codex) =>
        new(true, new Dictionary<string, bool> { ["claude"] = claude, ["codex"] = codex });

    [Fact]
    public void Defaults_are_claude_primary_brand_colors_and_alerts_on()
    {
        var s = SourceSettings.Default;
        Assert.Equal("claude", s.Primary);
        Assert.Equal(AgentSources.DefaultColor("codex"), s.ColorOf("codex"));
        Assert.True(s.AlertsOn("codex"));
    }

    [Fact]
    public void Round_trips()
    {
        var s = SourceSettings.Default with
        {
            Primary = "codex",
            Colors = new Dictionary<string, string> { ["codex"] = "#123456" },
            Alerts = new Dictionary<string, bool> { ["claude"] = false },
            Collapsed = new HashSet<string> { "codex" },
        };
        var back = SourceSettings.Decode(JsonNode.Parse(s.Encode().ToJsonString()));
        Assert.Equal("codex", back.Primary);
        Assert.Equal("#123456", back.ColorOf("codex"));
        Assert.False(back.AlertsOn("claude"));
        Assert.Contains("codex", back.Collapsed);
    }

    [Theory]
    [InlineData("""{"colors":{"codex":"teal"}}""")]
    [InlineData("""{"colors":{"codex":"#12345"}}""")]
    [InlineData("""{"colors":7}""")]
    [InlineData("""[]""")]
    public void Bad_values_fall_back(string json) =>
        Assert.Equal(AgentSources.DefaultColor("codex"), SourceSettings.Decode(JsonNode.Parse(json)).ColorOf("codex"));

    [Fact]
    public void An_unknown_primary_decodes_to_claude() =>
        Assert.Equal("claude", SourceSettings.Decode(JsonNode.Parse("""{"primary":"gemini"}""")).Primary);

    [Fact]
    public void Primary_follows_what_is_ticked()
    {
        var s = SourceSettings.Default with { Primary = "claude" };
        Assert.Equal("codex", s.PrimaryFor(Setup(claude: false, codex: true)));
        Assert.Equal("claude", s.PrimaryFor(Setup(claude: true, codex: true)));
        Assert.Equal("claude", s.PrimaryFor(Setup(claude: false, codex: false)));
    }
}
```

Extend `SetupSettingsTests.cs`:

```csharp
[Fact]
public void Codex_usage_round_trips_and_counts_as_usage_on()
{
    var s = SetupSettings.Decode(null, new SetupSettings(true, new Dictionary<string, bool>())).WithCodex(true);
    var back = SetupSettings.Decode(System.Text.Json.Nodes.JsonNode.Parse(s.Encode().ToJsonString()), s);
    Assert.True(back.CodexUsage);
    Assert.False(back.ClaudeUsage);
    Assert.True(back.UsageOn);
}

[Fact]
public void Choices_write_both_sources()
{
    var before = new SetupSettings(true, new Dictionary<string, bool> { ["claude"] = true, ["gemini"] = true });
    var after = new SetupChoices(Desktop: true, Drawers: false, ClaudeUsage: false, CodexUsage: true).ToSettings(before);
    Assert.False(after.ClaudeUsage);
    Assert.True(after.CodexUsage);
    Assert.True(after.Usage["gemini"]);
}
```

Read `SetupSettings.cs` before writing these: if `Encode()` returns a `JsonNode` rather than an object with `ToJsonString`, or `Decode`'s fallback parameter differs, adapt the two calls to the real signatures (`:23`, `:32`), keeping the assertions.

Extend `SetupStoreTests.cs` with a test that `SaveSources` then `LoadSources` on a temp settings file returns the same `Primary`, and that a settings.json with no `sources` key loads `SourceSettings.Default`. Follow the file's existing temp-store construction.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "SourceSettingsTests|SetupSettingsTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement `SourceSettings.cs` and the `SetupSettings` additions**

```csharp
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

public sealed partial record SourceSettings(
    string Primary,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyDictionary<string, bool> Alerts,
    IReadOnlySet<string> Collapsed)
{
    public const string Key = "sources";

    public static SourceSettings Default { get; } = new(AgentSources.Claude,
        new Dictionary<string, string>(), new Dictionary<string, bool>(), new HashSet<string>());

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexRe();

    public string ColorOf(string source) =>
        Colors.TryGetValue(source, out var c) && HexRe().IsMatch(c) ? c : AgentSources.DefaultColor(source);

    public bool AlertsOn(string source) => !Alerts.TryGetValue(source, out var on) || on;

    public string PrimaryFor(SetupSettings setup)
    {
        if (setup.Usage.TryGetValue(Primary, out var on) && on) return Primary;
        return AgentSources.All.FirstOrDefault(s => setup.Usage.TryGetValue(s, out var o) && o) ?? AgentSources.Claude;
    }

    public JsonObject Encode()
    {
        var colors = new JsonObject();
        foreach (var (k, v) in Colors) colors[k] = v;
        var alerts = new JsonObject();
        foreach (var (k, v) in Alerts) alerts[k] = v;
        var collapsed = new JsonArray();
        foreach (var s in Collapsed.OrderBy(AgentSources.Order)) collapsed.Add(s);
        return new JsonObject { ["primary"] = Primary, ["colors"] = colors, ["alerts"] = alerts, ["collapsed"] = collapsed };
    }

    public static SourceSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        string primary = o["primary"] is JsonValue pv && pv.TryGetValue<string>(out var p) && AgentSources.All.Contains(p) ? p : AgentSources.Claude;
        var colors = new Dictionary<string, string>();
        if (o["colors"] is JsonObject co)
            foreach (var (k, v) in co)
                if (AgentSources.All.Contains(k) && v is JsonValue cv && cv.TryGetValue<string>(out var c) && HexRe().IsMatch(c)) colors[k] = c;
        var alerts = new Dictionary<string, bool>();
        if (o["alerts"] is JsonObject ao)
            foreach (var (k, v) in ao)
                if (AgentSources.All.Contains(k) && v is JsonValue av && av.TryGetValue<bool>(out var b)) alerts[k] = b;
        var collapsed = new HashSet<string>();
        if (o["collapsed"] is JsonArray ca)
            foreach (var v in ca)
                if (v is JsonValue sv && sv.TryGetValue<string>(out var s) && AgentSources.All.Contains(s)) collapsed.Add(s);
        return new(primary, colors, alerts, collapsed);
    }
}
```

In `SetupSettings.cs`, add beside `Claude` / `ClaudeUsage` / `WithClaude` (:13-20), copying their exact style:

```csharp
public const string Codex = AgentSources.Codex;
public bool CodexUsage => Usage.TryGetValue(Codex, out var on) && on;
public SetupSettings WithCodex(bool on) { var u = new Dictionary<string, bool>(Usage) { [Codex] = on }; return this with { Usage = u }; }
```

and extend `SetupChoices` with `bool CodexUsage = false` as its last positional parameter, with `ToSettings` setting `[Codex] = CodexUsage` next to the Claude line.

- [ ] **Step 4: `SettingsStore.LoadSources` / `SaveSources`**

Add after `SaveSetup` (:892), mirroring how `LoadSetup`/`SaveSetup` read and write their key (same lock, same atomic save):

```csharp
public SourceSettings LoadSources() => SourceSettings.Decode(ReadNode(SourceSettings.Key));
public void SaveSources(SourceSettings value) => WriteNode(SourceSettings.Key, value.Encode());
```

where `ReadNode`/`WriteNode` are whatever `LoadSetup`/`SaveSetup` use to reach a key's `JsonNode` (read those two methods and call the same helpers; if they inline the access, inline it the same way).

- [ ] **Step 5: Run the tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "SourceSettingsTests|SetupSettingsTests" && dotnet test tests/Sanduhr.App.Tests --filter SetupStoreTests`
Expected: PASS, and the existing `SetupSettingsTests` / `SetupStoreTests` still pass.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SourceSettings.cs windows-dotnet/src/Sanduhr.Core/SetupSettings.cs windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs windows-dotnet/tests/Sanduhr.Tests/SourceSettingsTests.cs windows-dotnet/tests/Sanduhr.Tests/SetupSettingsTests.cs windows-dotnet/tests/Sanduhr.App.Tests/SetupStoreTests.cs
git commit -m "feat(usage): settings for Codex, the primary source, colors and per-source alerts"
```

---

### Task 5: The Codex usage service (poll, history, snapshot, alerts) and the history-name guards

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Services/CodexUsageService.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/SnapshotWriter.cs` (ctor :35, root fields :100-110: optional `source`)
- Modify: `windows-dotnet/src/Sanduhr.Core/Paths.cs` (beside `SnapshotFile` :182: `CodexSnapshotFile => Path.Combine(ExternalDir, "snapshot.codex.json")`)
- Modify: `windows-dotnet/src/Sanduhr.Mcp/HistoryReader.cs` (:29 enumeration: skip names whose account part starts with `@`)
- Modify: the vault and Publish account listings and Reset (find them: `grep -rn "HistoryFileFor\|history\." src/Sanduhr.App/Services src/Sanduhr.Core/Vault*`; the vault lists accounts from `AccountStore`, so check whether any listing enumerates history files instead) and Reset's file list (`grep -rn "snapshot.json\|SnapshotFile" src/Sanduhr.App/Services/*Reset*`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/CodexUsageServiceTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/SnapshotWriterTests.cs` (extend), `windows-dotnet/tests/Sanduhr.Mcp.Tests/HistoryReaderTests.cs` (extend or create beside the existing history tests), Reset's existing test file (extend)

**Interfaces:**
- Consumes: `CodexLimitsReader`, `CodexHomes`, `CodexReadResult`, `CodexLimits.ToTiers/IsFresh` (Tasks 1-2); `ZstdRollouts.Decompress`; `SourceSettings.AlertsOn` (Task 4); `UsageHistory.AppendForAccount(string tierKey, int utilization, string? account, string? resetsAt)` (`UsageHistory.cs:128`); `AlertEngine.Evaluate(IReadOnlyList<TierAlertSnapshot>, AlertConfig, DateTimeOffset)` (`AlertEngine.cs:61`); `SnapshotWriter`.
- Produces:
  - `public const string CodexHistoryAccount = "@codex";` on `CodexUsageService`.
  - `public sealed record CodexState(CodexReadStatus Status, JsonObject Tiers, DateTimeOffset? AsOf, bool Fresh, string? PlanType)` with `static CodexState Off`.
  - `public sealed class CodexUsageService : IDisposable` with ctor `(CodexLimitsReader reader, UsageHistory history, SnapshotWriter? snapshot, Func<DateTimeOffset> clock, Action<string>? log)`, `CodexState Current`, `event Action<CodexState>? Changed`, `CodexState Poll()` (synchronous, for tests and the timer), `void Start(IReadOnlyList<string> homes)` (30 s `System.Threading.Timer` plus a `FileSystemWatcher` per `sessions` folder, `IncludeSubdirectories = true`, filter `rollout-*`, debounced 500 ms), `void Stop()`, and `IReadOnlyList<AlertEvent> EvaluateAlerts(AlertConfig config, bool alertsOn)`.
  - `SnapshotWriter` ctor gains `string? source = null` as the last parameter; when set, `WriteOk` writes root `"source": source` and omits `account_ref`.

Behavior of `Poll()`: read; build tiers with `CodexLimits.ToTiers(reading, now, m => log once per minutes value)`; `Fresh = CodexLimits.IsFresh`; append each tier to history under `@codex` only when `AsOf` is newer than the last appended `AsOf` (keep it in a field, and seed it from the last history point's time on first poll); write `snapshot.codex.json` with `WriteOk(tiers, plan, accountLabel: null, now)` on `Ok` (snapshot writer may be null when neither the MCP nor the statusline is on; gate exactly like `SnapshotConsumers.SnapshotWanted` at `WidgetViewModel:802`); on `NotInstalled`/`NoLimits`/`Unreadable`, `Delete()` the Codex snapshot; raise `Changed` only when the state differs from `Current` (status, `AsOf`, `Fresh` or a tier value). `EvaluateAlerts` returns nothing when `!alertsOn`, `!Current.Fresh` or status is not `Ok`; otherwise maps tiers to `TierAlertSnapshot(key, (int)Math.Round(util), null, null, resetsAt)` and runs the service's own `AlertEngine`, so Claude's per-tier alert state (keyed by the same tier names) is untouched.

- [ ] **Step 1: Failing tests**

`CodexUsageServiceTests.cs` (App tests; use the App tests' existing temp `Paths`/`UsageHistory` construction from `WidgetUsageTests.cs` and feed the reader a fake home with plain `.jsonl` rollouts written as in Task 2):

```csharp
[Fact] public void Not_installed_reports_off_and_deletes_the_codex_snapshot() { /* reader over no homes; Poll(); Assert.Equal(CodexReadStatus.NotInstalled, svc.Current.Status); Assert.False(File.Exists(paths.CodexSnapshotFile)); */ }
[Fact] public void A_reading_writes_history_once_per_event_not_once_per_poll() { /* one rollout; Poll() twice; history.Load("five_hour", "@codex") has exactly 1 point */ }
[Fact] public void A_new_event_appends_a_second_point() { /* append a newer event line to the rollout; Poll(); 2 points */ }
[Fact] public void The_snapshot_carries_source_codex_and_no_account_ref() { /* parse paths.CodexSnapshotFile: root["source"]=="codex", no "account_ref", tiers contain five_hour and seven_day with the percents */ }
[Fact] public void Stale_limits_raise_no_alert() { /* clock = AsOf + 31 min, percent 99, EvaluateAlerts(enabled config, true) is empty */ }
[Fact] public void Fresh_limits_over_the_warn_threshold_alert_and_claude_state_is_separate() { /* clock = AsOf + 1 min, percent 85, warn 80: one Warn event for five_hour; a separate AlertEngine evaluating a Claude five_hour at 85 still fires its own Warn */ }
[Fact] public void Passed_resets_read_zero_and_do_not_alert_on_the_old_percent() { /* percent 99, resets_at in the past, clock now: tiers five_hour utilization 0, Fresh true, EvaluateAlerts returns no Warn/Urgent/Full */ }
[Fact] public void Alerts_off_for_codex_raise_nothing() { /* EvaluateAlerts(config, alertsOn: false) is empty */ }
[Fact] public void Changed_fires_only_on_a_real_change() { /* subscribe, Poll() twice with no new data: 1 Changed */ }
```

Write each body out fully in the test file (the comments above name the arrange, act and assert for each; no test may be left as a comment).

Extend `SnapshotWriterTests.cs`:

```csharp
[Fact]
public void A_source_writer_stamps_source_and_omits_account_ref()
{
    using var t = new TempDir();
    var path = Path.Combine(t.Path, "snapshot.codex.json");
    var w = new SnapshotWriter(path, null, "5.0.0", source: "codex");
    w.WriteOk(new System.Text.Json.Nodes.JsonObject { ["five_hour"] = new System.Text.Json.Nodes.JsonObject { ["utilization"] = 40.0, ["resets_at"] = null } },
        plan: "plus", accountLabel: null, now: DateTimeOffset.Parse("2026-11-09T12:00:00Z"));
    var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    Assert.Equal("codex", root["source"]!.GetValue<string>());
    Assert.False(root.ContainsKey("account_ref"));
}
```

Extend the MCP history tests: write `history.work.json` and `history.@codex.json` (the Codex one newer) into a temp dir; `HistoryReader.ReadFreshest(dir, null, now, 900)` returns the `work` account's latest, never the Codex one.

Extend Reset's test (find the existing one with `grep -rln "Reset" tests/Sanduhr.App.Tests`): after Reset, `snapshot.codex.json` and `history.@codex.json` are gone and an unrelated file in `~\.sanduhr\time\` remains.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter CodexUsageServiceTests`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`SnapshotWriter`: add the `source` parameter and, in the root-field block (:100-110), `if (_source is not null) root["source"] = _source; else root["account_ref"] = …` (keep the existing `account_ref` line inside the else). Update `SnapshotContract` docs only if it lists root fields.

`Paths.CodexSnapshotFile` as above.

`HistoryReader.cs`: where it enumerates `Prefix + "*.json"` files (around :29-60), skip a file when `LabelFromFileName(path)` starts with `'@'`.

Reset: add `paths.CodexSnapshotFile` and `paths.HistoryFileFor(CodexUsageService.CodexHistoryAccount)` to the files it deletes by name.

`CodexUsageService.cs`: write the class to the interface above. Use `System.Threading.Timer` (period 30 s) and marshal nothing: `Changed` is raised on the timer thread, and Task 7's consumer dispatches to the UI. Guard `Poll` with a `lock` so the timer and the watcher can't overlap. The watcher's `Error` event logs and keeps the timer as the fallback. `Dispose` stops both.

- [ ] **Step 4: Run all affected tests**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter SnapshotWriter && dotnet test tests/Sanduhr.App.Tests --filter "CodexUsageServiceTests|Reset" && dotnet test tests/Sanduhr.Mcp.Tests`
Expected: PASS, including `TrustBoundaryTests` untouched.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(codex): poll Codex limits, keep their history and snapshot, alert on fresh ones"
```

---

### Task 6: The MCP's `codex` block and statusline version 7

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Mcp/McpConfig.cs` (beside :22/:176: `CodexSnapshotPath`, `Path.Combine(externalDir, "snapshot.codex.json")`)
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolLogic.cs` (`BuildUsage` :116, output assembly near :202)
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs` (the `get_usage` description: one sentence naming the `codex` block)
- Modify: `windows-dotnet/src/Sanduhr.Core/StatuslineScript.cs` (`Version` :23 to 7; script after Claude's tier loop :190-219)
- Test: `windows-dotnet/tests/Sanduhr.Mcp.Tests/` (the existing `BuildUsage` tests; find with `grep -rln BuildUsage tests/Sanduhr.Mcp.Tests`), `windows-dotnet/tests/Sanduhr.Tests/StatuslineScriptRunTests.cs`

**Interfaces:**
- Consumes: the `snapshot.codex.json` format from Task 5.
- Produces: `get_usage` output gains `"codex": {"status":"ok"|"stale"|"absent", "captured_at":…, "plan":…, "tiers":[… same per-tier shape BuildTier emits …]}`; absent when the file is missing, `stale` past `SnapshotContract.FreshSeconds` (450) by `captured_at`. Statusline appends ` · cx 5h 40% wk 12%` when the Codex file is fresh (`captured_at` within 900 s, same dead rule as Claude's at :183), nothing otherwise.

Note: the Codex snapshot's `captured_at` is when the widget wrote it, which follows Codex's own `AsOf` by at most 30 s while Codex runs. Write the Codex `AsOf` into the snapshot as `captured_at` (pass it as `now` to `WriteOk` in Task 5) so "stale" here means Codex has been idle, matching the widget. If Task 5 was implemented with wall-clock `now`, change that call to `reading.AsOf` in this task and adjust its test.

- [ ] **Step 1: Failing tests**

MCP: with the existing test helper's snapshot template (`TestHelpers.cs:46`), write a Claude `snapshot.json` plus a `snapshot.codex.json` with `source: "codex"`, `captured_at` now - 60 s, tiers five_hour 40 and seven_day 12. Assert `BuildUsage()["codex"]["status"] == "ok"` and its `tiers[0]["utilization_pct"] == 40`. Second test: no Codex file, `codex` key absent. Third: `captured_at` now - 600 s, status `stale`. Fourth: a Codex file with `schema_version: 2` is refused the same way Claude's is (absent, not an exception).

Statusline (follow `StatuslineScriptRunTests`' existing run harness): with both files fresh, output contains `cx 5h 40% wk 12%`; with the Codex file 1000 s old, output does not contain `cx`; with no Codex file, output equals what version 6 printed for the same Claude snapshot. Update any test that pins `StatuslineScript.Version == 6` to 7.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Mcp.Tests --filter BuildUsage && dotnet test tests/Sanduhr.Tests --filter StatuslineScript`
Expected: FAIL on the new assertions.

- [ ] **Step 3: Implement**

In `ToolLogic.BuildUsage`, after the Claude block, read `CodexSnapshotPath` with `SnapshotReader.Read` (same schema check as :139-144), compute status from `captured_at` against `SnapshotContract.FreshSeconds`, and build tiers with the existing `BuildTier` for each entry (it already derives `resets_in_seconds` and `reset_crossed`). In the statusline script, after Claude's loop, a block that reads `snapshot.codex.json` beside `snapshot.json`, checks `schema_version -le 1` and age `-le 900`, and appends ` · cx` plus the `five_hour` and `seven_day` entries with the same `5h`/`wk` labels and rounding Claude's loop uses. Bump `Version` to 7 so `StatuslineInstaller` rewrites installed copies.

- [ ] **Step 4: Run**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Mcp.Tests && dotnet test tests/Sanduhr.Tests --filter StatuslineScript`
Expected: PASS; `TrustBoundaryTests` unchanged and green.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Mcp windows-dotnet/src/Sanduhr.Core/StatuslineScript.cs windows-dotnet/tests
git commit -m "feat(mcp): get_usage and the statusline show Codex limits"
```

---

### Task 7: The widget: combined bars, stacked sections, the Codex card and the tray

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/SourceSectionViewModel.cs`
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/CombinedBarViewModel.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/CombinedBar.xaml(.cs)`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (fields near :37-59, ctor :773, `RenderCards` :1841, tray inputs :1911-1913, `RaiseTray` :363)
- Modify: `windows-dotnet/src/Sanduhr.App/MainWindow.xaml` (the `Tiers` items control)
- Modify: `windows-dotnet/src/Sanduhr.Core/TrayReadout.cs` (`Glyph` :92 tooltip gains an optional "beyond" clause)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (create `CodexUsageService` with `CodexHomes.Discover(Environment.GetEnvironmentVariable("CODEX_HOME"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))` and `ZstdRollouts.Decompress`, start it when `setup.CodexUsage`, stop when off, dispose on exit; deliver its alerts through `AlertService.Deliver` with label "Codex " + `TierModel.Label(key)`)
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/WidgetSourcesTests.cs`, `windows-dotnet/tests/Sanduhr.Tests/TrayReadoutTests.cs` (extend)

**Interfaces:**
- Consumes: `CodexState`, `CodexUsageService.Changed` (Task 5); `SourceBar.Layout/AccessibleName`, `AgentSources` (Task 3); `SourceSettings` (Task 4); `TierCardViewModel(string tierKey, ThemePalette)` and `Update(...)` (`TierCardViewModel.cs:65,73`).
- Produces:
  - `SourceSectionViewModel { string Source; string Name; Brush Color; bool Collapsed; string AsOfText; bool Stale; string? EmptyText; ObservableCollection<TierCardViewModel> Tiers; string CollapsedSummary }` (`CollapsedSummary`: "5h 40% · wk 12%"; `EmptyText`: "Run Codex once to see your limits." for `NoLimits`, "Couldn't read Codex's logs." for `Unreadable`; `AsOfText`: "as of 3:42 PM", with the date when older than 7 days).
  - `CombinedBarViewModel { string WindowLabel; SourceBarLayout Layout; string AccessibleName; Brush PrimaryBrush; Brush? BeyondBrush; IReadOnlyList<(double At, Brush Brush, bool Stale, string Tooltip)> Ticks }`.
  - `WidgetViewModel.Sections` (`ObservableCollection<SourceSectionViewModel>`, in `AgentSources.All` order, ticked sources only) and `WidgetViewModel.CombinedBars` (empty unless two or more sources are ticked; one per window kind present in at least two sources: "Session (5hr)" for `five_hour`, "Weekly" for `seven_day`).
  - `TrayReadout.Glyph(bool usageOn, TrayReading? reading, bool demo, string? beyond = null)`: when `beyond` is set, the tooltip appends `" " + beyond` (for example "Codex week 70%.").

Rules: the existing `Tiers` collection becomes the Claude section's `Tiers` (keep `Tiers` as a property that returns it so existing bindings and tests keep working). The Codex section's cards are `TierCardViewModel`s keyed `five_hour`/`seven_day`, updated from `CodexState.Tiers` with history from `_history.Load(key, "@codex")`; their labels are `TierModel.Label(key)`. A stale Codex section sets `Opacity` 0.55 on its cards (bind in XAML). Collapse toggles `SourceSettings.Collapsed` and saves. The tray's `_traySession`/`_trayWeekly`/`_trayHighest` (:1911-1913) read the primary source's tiers (`SourceSettings.PrimaryFor(setup)`); `beyond` is the furthest non-primary fresh source for the tray mode's window, formatted "{Name} {week|5h} {pct}%.", only when it is further out.

- [ ] **Step 1: Failing tests**

`WidgetSourcesTests.cs` (STA, follow `WidgetUsageTests.cs` for constructing a `WidgetViewModel` with fakes; feed Claude data through the same seam those tests use and Codex through a `CodexState` pushed via the service's `Changed`, or a test-only `ApplyCodex(CodexState)` method on the view model if the existing tests construct the VM without services):

```csharp
[Fact] public void Claude_only_has_one_section_and_no_combined_bars() { /* Sections.Count == 1, CombinedBars empty, Tiers is Sections[0].Tiers */ }
[Fact] public void Both_ticked_stack_claude_then_codex_with_two_combined_bars() { /* Sections ["claude","codex"], CombinedBars window labels ["Session (5hr)","Weekly"] */ }
[Fact] public void Combined_weekly_bar_is_two_tone_when_codex_is_further_out() { /* Claude seven_day 40, Codex 70, primary claude: Layout.Beyond.Source == "codex", AccessibleName == "Weekly. Claude 40%. Codex 70%." */ }
[Fact] public void A_stale_codex_section_dims_and_says_as_of() { /* CodexState Fresh false, AsOf 3:42 PM local: Stale true, AsOfText "as of 3:42 PM" */ }
[Fact] public void No_limits_shows_the_run_codex_line_and_no_cards() { /* EmptyText == "Run Codex once to see your limits.", Tiers empty, CombinedBars has no codex entries */ }
[Fact] public void Collapsing_a_source_saves_and_shows_a_one_line_summary() { /* toggle; store.LoadSources().Collapsed contains "codex"; CollapsedSummary "5h 40% · wk 12%" */ }
[Fact] public void The_tray_follows_the_primary_and_names_a_source_further_out() { /* primary codex, tray mode Weekly: CurrentTrayReading.Percent == codex weekly; with Claude further out the tooltip contains "Claude week" */ }
```

Write each body fully. `TrayReadoutTests.cs`: `Glyph(true, new TrayReading(40, "Week"), false, beyond: "Codex week 70%.").Tooltip` ends with "Codex week 70%.", and with `beyond: null` it equals today's tooltip exactly.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter WidgetSourcesTests && dotnet test tests/Sanduhr.Tests --filter TrayReadoutTests`
Expected: FAIL.

- [ ] **Step 3: Implement the view models and `WidgetViewModel` changes**

Keep `RenderCards`' Claude path as it is, writing into the Claude section. Add `RenderCodex(CodexState state, DateTimeOffset now)` (called on the dispatcher from the `Changed` handler and from the 1 s tick that already refreshes time-derived text) and `RebuildCombined()` (called at the end of both). Brushes come from `SourceSettings.ColorOf` via a frozen `SolidColorBrush`; stale segments and ticks use the same brush at 0.55 opacity.

- [ ] **Step 4: XAML**

`MainWindow.xaml`: replace the single `ItemsControl ItemsSource="{Binding Tiers}"` with: an `ItemsControl` over `CombinedBars` (template: `views:CombinedBar`), then an `ItemsControl` over `Sections` whose template is a header row (a 10x10 color swatch `Rectangle` filled with `Color`, the `Name` in the existing card label style, `AsOfText` in TextDim, a chevron `Button` toggling collapse with `AutomationProperties.Name` "Collapse Codex" / "Expand Codex"), then either the `CollapsedSummary` text (collapsed), the `EmptyText` text (no cards), or the existing tier card `ItemsControl` over the section's `Tiers` (cards' `Opacity` bound to `Stale` through a converter returning 0.55 or 1). When only one section exists, hide its header so the widget looks exactly as it does today.

`CombinedBar.xaml`: a 6 px tall track `Border` (the card track style), a `Canvas` drawing `Primary` and `Beyond` as `Rectangle`s sized by `ActualWidth * fraction`, ticks as 2 px wide `Rectangle`s with `ToolTip` and `AutomationProperties.Name`, the control's own `AutomationProperties.Name` bound to `AccessibleName`, and the window label above in the card label style. Theme tokens only for the track and text.

- [ ] **Step 5: App wiring**

In `App.xaml.cs`, construct `CodexUsageService` where `AlertService` is created (:148), start or stop it in the same places `SetUsage` is applied (:101-164, :1067-1077), subscribe `Changed` with `Dispatcher.BeginInvoke(() => widgetVm.ApplyCodex(state))`, and after each change call `foreach (var e in codex.EvaluateAlerts(store.LoadAlertConfig(), store.LoadSources().AlertsOn("codex"))) alerts.Deliver(e, "Codex " + TierModel.Label(e.TierKey), sound, snake)`, reading `sound` and `snake` the same way `WidgetViewModel.EvaluateAlerts` (:1979) does.

- [ ] **Step 6: Run the full App and Core suites**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests && dotnet test tests/Sanduhr.App.Tests`
Expected: PASS. Existing widget tests pass unchanged (the Claude-only widget is the same).

- [ ] **Step 7: Live check**

`cd windows-dotnet && dotnet run --project src/Sanduhr.App` (quit any running `Sanduhr.exe` first). Tick Codex in Set up Sanduhr. Expected: two sections, two combined bars, Codex marked stale "as of" 22 Oct (no Codex run since), its passed resets at 0%. Run one Codex turn in a terminal; within a few seconds the Codex section turns fresh with new percents. Check obsidian, aurora and a high-contrast Windows theme: swatches, segments and ticks visible, chevron reachable by Tab, Narrator reads the combined bar's name.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(widget): stack sources, combined two-tone bars, the Codex card and a primary-driven tray"
```

---

### Task 8: Settings ▸ Usage ▸ Sources and the setup screen's Codex row

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNew.cs:7` (`SettingsPage` gains `UsageSources`)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (:29 Usage group becomes `[SettingsPage.UsageSources, SettingsPage.ClaudeUsage, SettingsPage.Publish]`; title "Sources" near :59; strings below)
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs` (:217-226, add search entries "sources", "codex", "primary", "colors")
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/SourcesViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/SourcesPage.xaml(.cs)`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (a `TabItem` with `Tag="UsageSources"` before :1403) and `SettingsWindow.xaml.cs:234-238`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SetupViewModel.cs` (:27, :62, :73) and the setup view's "Track my AI usage" list
- Test: `windows-dotnet/tests/Sanduhr.App.Tests/SourcesPageTests.cs`, `windows-dotnet/tests/Sanduhr.App.Tests/SetupServiceTests.cs` (extend), and whatever test pins the sidebar groups and page count (`grep -rn "SettingsPage\." tests | grep -i "group\|count"`), updated for the new page

**Interfaces:**
- Consumes: `SourceSettings`, `SetupSettings.WithCodex`, `SetupChoices.CodexUsage` (Task 4); `CodexHomes.Discover` (Task 2).
- Produces: `SourcesViewModel { ObservableCollection<SourceRowViewModel> Rows }`, `SourceRowViewModel { string Source; string Name; bool On; bool Installed; string? NotInstalledText; string Color; bool IsPrimary; ICommand ResetColor }`.

Strings (in `SettingsNames.cs`, plain and with periods):
- Page title "Sources". Intro: "The AI plans Sanduhr tracks. Codex is read from its own logs on this PC; nothing is sent anywhere."
- Codex not installed: "Codex isn't installed on this PC."
- Primary radio name: "Primary". Its caption: "The tray and the combined bars' fill follow the primary."
- Color: "Color" with a "Reset" button; accessible name "Codex color".
- Alerts switch: "Alerts".

Rules: `On` writes `setup.usage.<source>` through the same path `WidgetViewModel.SetUsageOn` (:225) uses for Claude, and starts or stops the Codex service. A not-installed source is disabled (On off and greyed) with the line above. Primary is a radio group over sources that are on; turning the primary off moves primary to `PrimaryFor`. Color is a hex text box validated by `SourceSettings`' regex, refused input keeps the old value and says "Use a color like #2FB5A3.", plus `ResetColor`. The setup screen's usage choice lists Claude and Codex as checkboxes with the same not-installed rule; ticking Codex never asks to sign in.

- [ ] **Step 1: Failing tests**

`SourcesPageTests.cs` (STA): rows in `AgentSources.All` order; a temp profile with no `.codex` makes Codex `Installed == false`, `On == false`, `NotInstalledText == "Codex isn't installed on this PC."`; with a `.codex` folder, turning Codex on writes `setup.usage.codex == true`; making Codex primary then turning it off leaves `Primary == "claude"`; a color "teal" is refused with the message and "#2FB5A3" saved; every interactive control has a non-empty `AutomationProperties.Name` (follow the pattern in `SettingsWindowNamesTests.cs`). `SetupServiceTests`: choices with `CodexUsage: true` save `setup.usage.codex` and do not show the Claude sign-in card when Claude is unticked.

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests --filter "SourcesPageTests|SetupServiceTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the page, the view model, the setup row and the sidebar entry**

Follow an existing simple page (the Publish page is in the same group) for layout, theme tokens and the light-mode dialog rule. Tab order: rows top to bottom, within a row On, Primary, Color, Reset, Alerts.

- [ ] **Step 4: Run**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.App.Tests`
Expected: PASS, including the sidebar and search-index tests after their counts are updated.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(settings): a Sources page and Codex in Set up Sanduhr"
```

---

### Task 9: Desk meter pieces take a source

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs` (`DeskPlacement` :77 gains `string Source = "combined"`; the layout string parser and writer near `Standard` :288 / `Known` :292 encode it, e.g. `meters:bl:src=codex`; absent means combined)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskMeters.cs` (`DeskUsage` :21 gains `IReadOnlyDictionary<string, IReadOnlyList<DeskTier>>? SourceTiers = null`, `IReadOnlyDictionary<string, bool>? SourceStale = null`; new `DeskMeters.CombinedRows(DeskUsage, string primary, now, zone, warnAt, urgentAt)` returning `(DeskMeterRow Row, SourceBarLayout Layout)` per window)
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskView.Styles.cs` (`StyledMeters` :22, `SlimRow` :137, `Ring` :272 draw the two-tone fill and ticks when the placement's source is combined and two sources exist), `DeskView.cs:503-504` (pass the placement's source)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`CurrentDeskUsage` :1928 fills `SourceTiers`/`SourceStale`)
- Modify: the Desk settings page where a piece's style is chosen (find with `grep -rn "DeskMeterStyle" src/Sanduhr.App/Views`): add a "Shows" choice (Combined, Claude, Codex) for the `meters` and `claude` pieces
- Test: `windows-dotnet/tests/Sanduhr.Tests/DeskMetersTests.cs`, `DeskMeterStylesTests.cs` and the arrangement round-trip tests (`grep -rln "DeskPlacement" tests/Sanduhr.Tests`), extended

**Interfaces:**
- Consumes: `SourceBar.Layout` (Task 3), `SourceSettings.PrimaryFor` (Task 4), `CodexState` via `WidgetViewModel` (Task 7).
- Produces: `DeskPlacement.Source`, `DeskUsage.SourceTiers`, `DeskMeters.CombinedRows`.

Rules: a layout string with no `src=` parses to `combined` and writes back without `src=` (old layouts round-trip byte for byte). A `codex` piece with Codex off or not installed draws nothing (the piece hides, like a Claude piece signed out). Combined with one source draws exactly today's rows. The `claude` piece's line text (`DeskClaudeText` :126) is unchanged for Claude; with source `codex` it reads "Codex 5h 40% · wk 12%".

- [ ] **Step 1: Failing tests**

Round-trip: `"clock:bl claude:bl"` parses and writes back identically; `"meters:bl:src=codex"` round-trips and gives `Source == "codex"`; an unknown `src=gemini` parses to `combined`. `CombinedRows`: Claude seven_day 40 and Codex 70, primary claude, returns a Weekly row whose `Layout.Beyond.Source == "codex"`; with only Claude, `CombinedRows` equals `Rows` mapped with a single-segment layout. Styles: `StyledMeters` for a combined placement with two sources produces a beyond segment (assert through whatever the style tests already inspect, e.g. the gauge read list `DeskGaugeRead`; add a `Beyond` fraction to `DeskGaugeRead` if needed and assert it).

- [ ] **Step 2: Run to verify failure**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests --filter "DeskMeters|DeskMeterStyles|DeskArrangement"`
Expected: FAIL.

- [ ] **Step 3: Implement**, keeping every existing Desk test green.

- [ ] **Step 4: Run**

Run: `cd windows-dotnet && dotnet test tests/Sanduhr.Tests && dotnet test tests/Sanduhr.App.Tests --filter Desk`
Expected: PASS.

- [ ] **Step 5: Live check**: Desk on, meters piece Combined, slim and ring styles: the two-tone fill and the Codex tick show; switch the piece to Codex: Codex rows only; Explorer restart: the piece survives (existing Desk behavior).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src windows-dotnet/tests
git commit -m "feat(desk): meter pieces show combined, Claude or Codex"
```

---

### Task 10: What's New, CHANGELOG, PRIVACY and the Sanduhr Time issue

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs` and its test (the card's version key: **ask the owner which version this ships as before writing it**, since 5.0 also carries specs 3 and 4; do not guess)
- Modify: `CHANGELOG.md` under `## Unreleased (windows)` ▸ `### New`
- Modify: `docs/PRIVACY.md` (a local-read row: Codex's session logs under `%USERPROFILE%\.codex\sessions`, read for plan percents and reset times only, never sent; `auth.json` never opened)
- Modify: `docs/BACKLOG.md` only if it tracks the agent layer (grep "agent layer")

- [ ] **Step 1: CHANGELOG bullet**

```markdown
- **Codex limits, beside Claude's.** Tick Codex in Set up Sanduhr or Settings ▸ Usage ▸ Sources and the widget shows its 5-hour and weekly limits under Claude's, with the same pace marker and burn projection. Sanduhr reads them from Codex's own logs on this PC, so there's no sign-in and nothing is sent anywhere; they update while Codex runs and show "as of" the last time it did. Each source has a color, and with both on, combined bars show your primary plan's fill and where the other one is. The tray follows the primary, alerts can be turned on per source, Desk meters can show either or both, and the MCP and Claude Code statusline include Codex too.
```

- [ ] **Step 2: PRIVACY.md row**, matching the file's existing local-read rows' format.

- [ ] **Step 3: What's New card** once the owner names the version: title "Codex limits, beside Claude's", Show me lands on `SettingsPage.UsageSources`. Update the WhatsNew test's count and ids per its existing pattern.

- [ ] **Step 4: Sanduhr Time issue**

```bash
gh issue create --title "Sanduhr Time: the Codex reader misses compressed rollouts (.jsonl.zst)" --body "Codex now keeps rollouts as sessions/**/rollout-*.jsonl.zst (Neb, 2026-11-09: 65 files, none plain). vscode/src/codex/homes.ts lists rollout-*.jsonl only, so Sanduhr Time reads no Codex activity on that machine. The shape inside is unchanged (verdict in windows-dotnet/docs/checklist.md, Codex limits check). Node 20 has no zstd; options: Node 22.15+/23.8+ zlib zstd, or a WASM decompressor. Found while building the Windows agent layer (docs/superpowers/specs/2026-11-09-agent-layer-design.md)."
```

- [ ] **Step 5: Screenshots**: harness captures of the widget with both sources in obsidian and aurora (demo mode, made-up numbers, no identities) into `docs/screenshots/windows/`, following the setup spec's harness screenshot task.

- [ ] **Step 6: Full suite and commit**

Run: `cd windows-dotnet && dotnet test`
Expected: PASS across Core, App and Mcp.

```bash
git add CHANGELOG.md docs/PRIVACY.md windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs windows-dotnet/tests
git commit -m "docs(codex): CHANGELOG, privacy and What's New for Codex limits"
```
