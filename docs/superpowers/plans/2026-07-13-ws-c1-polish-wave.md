# WS-C.1 Polish Wave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Count nested subagent transcripts everywhere (recursive walk + parent-session fold, healed by a one-shot upgrade re-ingest), store and show the sent/received token split, fix the widget's %-blend, and add the Overview rolling calendar — per the approved spec `docs/superpowers/specs/2026-07-13-ws-c1-polish-wave-design.md`.

**Architecture:** Core carries everything testable: both walkers go recursive; every nested file stays its own ingest unit gaining a schema-additive `parent_session`; a `walk_version` marker in per-root meta triggers one checkpoint invalidation so the existing full-re-ingest convergence heals all history still inside CC's ~30-day retention; day buckets gain `input`/`output` with the same re-ingest backfilling them. The read side folds member files into logical sessions (`parent_session ?? uuid`). App work is a thin layer: split line + calendar on the Overview, agents line in the Ledger expansion, a backplate chip on the tier card.

**Tech Stack:** .NET 10 WPF (`windows-dotnet/`), CommunityToolkit.Mvvm, xUnit, System.Text.Json.

## Global Constraints

- Test command: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`. App build: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`. Baseline on main (ad98a8f): 418 green. A running Sanduhr.exe may lock the default build output — verify compilation with a scratch `-o` under %TEMP% and do NOT kill the user's instance.
- **Parity invariant (binding):** vault and live surfaces count the SAME files. Both walkers change in the same task; the recursive live-vs-vault day-total parity test is the gate. The day-one 2–3x jump in live numbers is owner-approved — no transition copy.
- **Schema rules (WS-C, still binding):** additions only, snake_case wire names via `JsonPropertyName`, absent-field reads must behave (legacy bucket → input/output 0; meta without `walk_version` → version 1). Readers accept every `schema_version <= 1` forever; rollups are exempt (full rebuild on the upgrade re-ingest).
- **Parent rule (exact):** a file's `parent_session` is the FIRST path segment under its project directory when (a) the file is nested (relative path has >1 segment) and (b) that segment parses as a `Guid`. Otherwise null. Main transcripts (`{projectDir}\{uuid}.jsonl`) are never their own parent.
- **Upgrade rule (exact):** `VaultIngester.CurrentWalkVersion = 2`; at the START of a root's cycle, effective meta walk-version < 2 ⇒ delete that root's `checkpoints.json` and start from empty checkpoints; the cycle's `UpdateMeta` then stamps `walk_version = 2`. Idempotent: an aborted cycle leaves the old meta, so the next cycle re-invalidates and converges. Rows whose source files are already gone are NEVER touched by the upgrade.
- **Split conservation (binding):** every NEW day bucket satisfies `input + output == total`. Sent = input tokens, received = output tokens. The Overview split line appends `(partial)` when the window's `Σ(input+output) < 0.95 ×` the window's total.
- **Logging:** unchanged WS-C contract — operation + exception TYPE name only; never e.Message/paths/labels/raw lines.
- **Theming:** existing `Sanduhr.Brush.*`/palette keys only; no new resource keys; no literals; frozen brushes in OnRender controls.
- Ledger/Overview virtualization + DataContext arrangements from WS-C are load-bearing — do not disturb the TabItem-level `ClaudeCode` DataContext, the `CcSection*` AncestorType triggers, or the ListBox virtualization attributes.
- Branch: `feat/ws-c1-polish-wave` (Task 1 creates from main at ad98a8f). Main is PR-only — the final task opens a PR, it does not merge.
- Conventional commits; commit at the end of every task.

## File Structure

| File | Role |
|---|---|
| `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (modify) | Recursive `DiscoverLogFiles`; `LocalCcAggregate` gains per-day input/output; both aggregates accumulate them |
| `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (modify) | `VaultSessionRow.ParentSession`, `VaultDayBucket.Input/Output`, `VaultRollupDay.Input/Output`, `VaultRootMeta.WalkVersion`; `VaultRowMath` same-day summing + parent passthrough |
| `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs` (modify) | Recursive walk, parent derivation, split bucketing, walk_version gate |
| `windows-dotnet/src/Sanduhr.Core/VaultStore.cs` (modify) | + `DeleteCheckpoints(rootName)` |
| `windows-dotnet/src/Sanduhr.Core/VaultReader.cs` (modify) | Logical-session fold (`parent ?? uuid`), `VaultSessionInfo.AgentCount/AgentTokens`, `VaultWindow` split fields, `CoveredSet` batch API |
| `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs` (modify) | Split line data + partial rule; calendar data feed |
| `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs` (modify) | "Agents: N · X" expansion line |
| `windows-dotnet/src/Sanduhr.App/Views/CcCalendarControl.cs` (new) | 5×7 rolling calendar, heat + textures + hover |
| `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml(.cs)` (modify) | Split-line TextBlocks, calendar placement + render bridge |
| `windows-dotnet/src/Sanduhr.App/Views/TierCard.xaml` (modify) | % backplate chip |
| `windows-dotnet/tests/Sanduhr.Tests/*` (modify/create) | The upgrade/parity/merge/split batteries |
| `docs/PRIVACY.md`, `docs/smoke-test-plan.md` (modify) | Subagent clause, WS-C.1 smoke |

Suggested per-task models: Tasks 1–3 most capable (schema + convergence semantics); Tasks 4–6 standard; Task 7 cheap.

---

### Task 1: Recursive walk + `parent_session` + `walk_version` upgrade (Core, TDD)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (DiscoverLogFiles)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (ParentSession, WalkVersion, SplitByMonth/Merge passthrough)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs` (walk, parent derivation, upgrade gate, CurrentWalkVersion)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultStore.cs` (DeleteCheckpoints)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultSubagentCoverageTests.cs`; modify `windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs`

**Interfaces:**
- Consumes: everything WS-C landed — `VaultIngester.IngestOnce(roots, storeFullPaths, nowUtc, stillConsented)`, `VaultStore`, `VaultRootMeta`, the test fixture patterns in `VaultIngesterTests.cs` (fixed CST zone, pinned mtimes, unique mutex names — copy those helpers into the new test file so it reads standalone).
- Produces (Tasks 2–4 rely on these exact names):
  - `VaultSessionRow.ParentSession` (`string?`, `[JsonPropertyName("parent_session")]`, `[JsonIgnore(WhenWritingNull)]`) — copied through `VaultRowMath.SplitByMonth` (all month rows) and `VaultRowMath.Merge` (primary's value).
  - `VaultRootMeta.WalkVersion` (`int`, `[JsonPropertyName("walk_version")]`; absent on legacy meta deserializes 0 — treat `<= 1` as v1: `EffectiveWalkVersion => WalkVersion == 0 ? 1 : WalkVersion` is NOT added; just compare `< CurrentWalkVersion` since 0 < 2 too).
  - `VaultIngester.CurrentWalkVersion = 2` (public const int).
  - `VaultStore.DeleteCheckpoints(string rootName)` — best-effort delete of `checkpoints.json`, logs `"checkpoints-delete"` on failure via the existing `LogBestEffort`.
  - `CcLogReader.DiscoverLogFiles()` returns nested files too (recursive).

- [ ] **Step 1: Create the branch**

```bash
git checkout main && git pull && git checkout -b feat/ws-c1-polish-wave
```

- [ ] **Step 2: Write the failing tests**

Append to `windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs` (reuse that file's existing fixture helpers for writing session files; the assertion is the shape, not the helper names):

```csharp
    [Fact]
    public void DiscoverLogFiles_finds_nested_subagent_transcripts()
    {
        using var home = new TempDir();
        var projectDir = Path.Combine(home.Path, ".claude", "projects", "c--x-api");
        var nested = Path.Combine(projectDir, "11111111-2222-3333-4444-555555555555", "subagents");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(projectDir, "main.jsonl"), "");
        File.WriteAllText(Path.Combine(nested, "agent-abc.jsonl"), "");

        var reader = new CcLogReader(home.Path);
        var files = reader.DiscoverLogFiles();

        Assert.Contains(files, f => f.EndsWith("main.jsonl"));
        Assert.Contains(files, f => f.EndsWith("agent-abc.jsonl"));
    }
```

Create `windows-dotnet/tests/Sanduhr.Tests/VaultSubagentCoverageTests.cs` — copy the `EventLine`/`WriteSession`/`Make`/`MutexName`/`Cst`/`Now` helpers verbatim from `VaultIngesterTests.cs` (standalone-file convention), then add a nested-file writer and these tests:

```csharp
    /// <summary>Writes a NESTED transcript: {projectDir}\{parentUuid}\subagents\{name}.jsonl,
    /// mtime pinned like WriteSession.</summary>
    private static string WriteNested(
        string home, string root, string parentUuid, string name,
        DateTimeOffset? mtimeUtc = null, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", "c--Users-x-Projects-api",
            parentUuid, "subagents");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, (mtimeUtc ?? Now.AddMinutes(-10)).UtcDateTime);
        return path;
    }

    private const string ParentUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Fact]
    public void Nested_file_gets_parent_session_and_its_own_row()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", ParentUuid, EventLine("2026-07-10T15:00:00Z"));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x", null,
            EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(2, shard.Sessions.Count);
        Assert.Null(shard.Sessions[ParentUuid].ParentSession);      // main transcript: no parent
        Assert.Equal(ParentUuid, shard.Sessions["agent-x"].ParentSession);
        Assert.Equal(200, shard.Sessions["agent-x"].Total);
    }

    [Fact]
    public void Non_uuid_subdirectory_yields_no_parent()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var dir = Path.Combine(home.Path, ".claude", "projects", "c--Users-x-Projects-api", "scratch");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stray.jsonl"), EventLine("2026-07-10T15:00:00Z") + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(dir, "stray.jsonl"), Now.AddMinutes(-10).UtcDateTime);

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Null(shard.Sessions["stray"].ParentSession);          // "scratch" is not a Guid
    }

    [Fact]
    public void Walk_version_upgrade_reingests_once_and_matches_a_fresh_vault()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", ParentUuid, EventLine("2026-07-10T15:00:00Z"));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x", null,
            EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Regress the vault to v1 shape: strip walk_version from meta (a WS-C
        // vault never wrote it) and delete the nested file's row — a v1 walk
        // never saw it. The stale checkpoints still cover the flat file, so
        // ONLY an invalidation makes the re-ingest resurrect the nested row.
        var metaPath = Path.Combine(vault.Path, ".claude", "meta.json");
        File.WriteAllText(metaPath, File.ReadAllText(metaPath)
            .Replace(",\"walk_version\":2", "").Replace("\"walk_version\":2,", ""));
        store.TryLoadSessionShard(".claude", "2026-07", out var s1);
        s1.Sessions.Remove("agent-x");
        store.SaveSessionShard(".claude", "2026-07", s1);

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);     // upgrade cycle
        Assert.True(r2.FilesFullParsed >= 2);                          // checkpoints were invalidated

        store.TryLoadSessionShard(".claude", "2026-07", out var upgraded);

        using var freshVault = new TempDir();
        var (ing2, store2) = Make(home.Path, freshVault.Path, mutexName: MutexName());
        ing2.IngestOnce(new[] { ".claude" }, false, Now);
        store2.TryLoadSessionShard(".claude", "2026-07", out var fresh);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(fresh.Sessions),
            System.Text.Json.JsonSerializer.Serialize(upgraded.Sessions));

        var r3 = ing.IngestOnce(new[] { ".claude" }, false, Now);     // NO second invalidation
        Assert.Equal(0, r3.FilesFullParsed + r3.FilesTailParsed);
        Assert.Equal(2, r3.FilesSkipped);
    }

    [Fact]
    public void Upgrade_preserves_rows_for_aged_out_files()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u-old", EventLine("2026-07-01T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        File.Delete(path);                                             // source ages out

        var metaPath = Path.Combine(vault.Path, ".claude", "meta.json");
        File.WriteAllText(metaPath, File.ReadAllText(metaPath)
            .Replace(",\"walk_version\":2", "").Replace("\"walk_version\":2,", ""));

        ing.IngestOnce(new[] { ".claude" }, false, Now);               // upgrade with source gone

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(150, shard.Sessions["u-old"].Total);              // the record outlives its source
    }

    [Fact]
    public void Vault_day_total_matches_live_reader_with_nested_files()
    {
        // The WS-C parity invariant, recursive edition — real clock + local zone,
        // real-clock mtimes (see VaultIngesterTests' parity test for why).
        using var home = new TempDir();
        using var vault = new TempDir();
        var ts = DateTimeOffset.UtcNow.AddDays(-1);
        var iso = ts.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        WriteSession(home.Path, ".claude", ParentUuid,
            mtimeUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            EventLine(iso, input: 700, output: 70));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            EventLine(iso, input: 20, output: 2));

        var store = new VaultStore(vault.Path);
        var ing = new VaultIngester(home.Path, store, "test", null, TimeZoneInfo.Local, MutexName());
        ing.IngestOnce(new[] { ".claude" }, false, DateTimeOffset.UtcNow);

        var reader = new CcLogReader(home.Path);
        var live = reader.AggregateForLocalCcTab(30);
        var day = DateOnly.FromDateTime(ts.ToLocalTime().DateTime);
        var dayKey = day.ToString("yyyy-MM-dd");

        store.TryLoadSessionShard(".claude", dayKey[..7], out var shard);
        long vaultDayTotal = shard.Sessions.Values
            .Where(r => r.ByDay.ContainsKey(dayKey))
            .Sum(r => r.ByDay[dayKey].Total);
        Assert.Equal(live.ByDay[day], vaultDayTotal);
        Assert.Equal(792, vaultDayTotal);                              // 770 main + 22 agent
    }
```

Adjust the `Make` helper copy so `mutexName` is an optional named parameter as in `VaultIngesterTests.cs`. If the upgrade test's checkpoint-key manipulation proves awkward, the simpler regression (delete the row + strip walk_version) is sufficient — the assertion that matters is upgrade-equals-fresh plus no-second-invalidation.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultSubagentCoverage|FullyQualifiedName~DiscoverLogFiles_finds_nested"`
Expected: FAIL — `ParentSession` doesn't exist; nested files not discovered; no walk_version behavior.

- [ ] **Step 4: Implement**

(a) `CcLogReader.DiscoverLogFiles` — one line:

```csharp
            foreach (var projectDir in Directory.GetDirectories(projects))
                files.AddRange(Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories));
```

(b) `VaultModels.cs` — add to `VaultSessionRow` after `Cwd`:

```csharp
    /// <summary>The session this transcript belongs to when it is a NESTED
    /// subagent/workflow transcript ({projectDir}\{parent-uuid}\...\x.jsonl).
    /// Null for main transcripts. Readers fold rows by parent_session ?? uuid.</summary>
    [JsonPropertyName("parent_session")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentSession { get; set; }
```

Add to `VaultRootMeta`:

```csharp
    /// <summary>Discovery-walk generation. Absent (0) or 1 = the one-level
    /// pre-WS-C.1 walk; the ingester invalidates checkpoints ONCE when this is
    /// below its CurrentWalkVersion so the full re-ingest picks up nested
    /// subagent transcripts still inside CC retention.</summary>
    [JsonPropertyName("walk_version")]
    public int WalkVersion { get; set; }
```

In `VaultRowMath.SplitByMonth`, copy `ParentSession = merged.ParentSession` into every month row's initializer; in `Merge`, set `ParentSession = primary.ParentSession` on the merged row.

(c) `VaultStore.cs` — next to `LoadCheckpoints`:

```csharp
    /// <summary>Best-effort standalone checkpoint invalidation — the
    /// walk_version upgrade's tool. The next cycle full-re-ingests everything
    /// still on disk (idempotent by design).</summary>
    public void DeleteCheckpoints(string rootName)
    {
        try
        {
            File.Delete(CheckpointsPath(rootName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("checkpoints-delete", e);
        }
    }
```

(d) `VaultIngester.cs`:

Add the constant near the others:

```csharp
    /// <summary>Bump when the discovery walk widens; a root whose meta carries
    /// an older value gets its checkpoints invalidated once so the re-ingest
    /// covers the newly visible files.</summary>
    public const int CurrentWalkVersion = 2;
```

At the START of `IngestRoot`, before `LoadCheckpoints`:

```csharp
        // walk_version gate: a vault written by an older (narrower) walk
        // re-ingests everything once. Aborted cycles leave the old meta, so
        // this re-fires until a cycle completes — idempotent convergence.
        var existingMeta = _store.LoadMeta(rootName);
        if ((existingMeta?.WalkVersion ?? 0) < CurrentWalkVersion)
        {
            _store.DeleteCheckpoints(rootName);
            Log("walk upgraded — checkpoints invalidated for re-ingest");
        }
```

In `UpdateMeta`, stamp the version:

```csharp
        meta.WalkVersion = CurrentWalkVersion;
```

Make the walk recursive and derive the parent. Replace the per-project enumeration and remember each file's project dir:

```csharp
        var files = new List<(FileInfo File, string ProjectDir)>();
        var projects = new DirectoryInfo(Path.Combine(_homeDir, rootName, "projects"));
        if (projects.Exists)
        {
            foreach (var projectDir in projects.EnumerateDirectories())
                foreach (var fi in projectDir.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                    files.Add((fi, projectDir.FullName));
        }
        files.Sort((a, b) => a.File.LastWriteTimeUtc.CompareTo(b.File.LastWriteTimeUtc));
```

(update the `foreach (var fi in files)` loop to destructure `var (fi, projectDir)` accordingly), and derive the parent where `uuid` is computed:

```csharp
            var uuid = Path.GetFileNameWithoutExtension(fi.Name);
            var parentSession = ParentSessionOf(fi.FullName, projectDir);
```

with the helper (exact rule from the spec — first segment under the project dir, only when nested and a Guid):

```csharp
    /// <summary>parent_session per the WS-C.1 rule: for a NESTED file
    /// ({projectDir}\{seg}\...\x.jsonl) whose first segment parses as a Guid,
    /// that segment; otherwise null. Main transcripts sit directly in the
    /// project dir and never have a parent.</summary>
    internal static string? ParentSessionOf(string fullPath, string projectDir)
    {
        var rel = Path.GetRelativePath(projectDir, fullPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 && Guid.TryParse(parts[0], out _) ? parts[0] : null;
    }
```

Thread `parentSession` into `BuildRows(uuid, agg, storeFullPaths, parentSession)` (new final parameter) and set `ParentSession = parentSession` on the merged row before `SplitByMonth`. The tail-parse seed path preserves it automatically (`SeedFromStored` → `Merge` carries the primary's value; still pass `parentSession` through `BuildRows` on that path too — path-derived and stored values are identical by construction).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS — 418 + 6 new = 424. Watch the two WS-C suites especially: `VaultIngesterTests`/`VaultIngesterHardeningTests` fixtures write only flat files, so recursion must not disturb them; the byte-identical idempotency test now also covers meta's `walk_version` field (same value each cycle — still byte-identical).

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core windows-dotnet/tests/Sanduhr.Tests
git commit -m "feat(vault): recursive walk + parent_session + one-shot walk_version re-ingest — subagent burn counts"
```

---

### Task 2: Sent/received split — schema, ingest, live reader (Core, TDD)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (bucket + rollup fields)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs` (ProcessLine, DayAgg, BuildRows, SeedFromStored, rollup fold)
- Modify: `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (`LocalCcAggregate` + both aggregates)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultSplitTests.cs`; modify `windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs`

**Interfaces:**
- Consumes: Task 1's state (recursive walk irrelevant here; fixtures may stay flat).
- Produces (Tasks 3–4 rely on these exact names):
  - `VaultDayBucket.Input` / `.Output` (longs, `[JsonPropertyName("input")]` / `("output")`, ALWAYS written on new rows; legacy rows deserialize 0/0).
  - `VaultRollupDay.Input` / `.Output` (same wire names; rebuilt by the fold).
  - `LocalCcAggregate` becomes `record LocalCcAggregate(Dictionary<DateOnly, long> ByDay, Dictionary<string, long> ByProject, Dictionary<string, long> BySkill, Dictionary<DateOnly, long> ByDayInput, Dictionary<DateOnly, long> ByDayOutput)` — grep every construct site (`new LocalCcAggregate(`) and update: two in `CcLogReader.cs`; check App + tests for others before assuming.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/VaultSplitTests.cs` (copy the standalone fixture helpers from `VaultIngesterTests.cs` as in Task 1):

```csharp
    [Fact]
    public void New_buckets_conserve_input_plus_output_equals_total()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", input: 700, output: 70),
            EventLine("2026-07-10T16:00:00Z", input: 20, output: 2));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        var bucket = shard.Sessions["u1"].ByDay["2026-07-10"];
        Assert.Equal(720, bucket.Input);
        Assert.Equal(72, bucket.Output);
        Assert.Equal(bucket.Total, bucket.Input + bucket.Output);   // conservation

        store.TryLoadRollupShard(".claude", "2026-07", out var roll);
        Assert.Equal(720, roll.Days["2026-07-10"].Input);
        Assert.Equal(72, roll.Days["2026-07-10"].Output);
    }

    [Fact]
    public void Legacy_buckets_without_split_fields_read_zero()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        // Hand-written WS-C-era shard: no input/output on the bucket.
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"),
            "{\"schema_version\":1,\"writer_version\":\"3.2.0\",\"sessions\":{\"u1\":{" +
            "\"project_key\":\"api~00000000\",\"project_name\":\"api\"," +
            "\"first_ts\":\"2026-07-01T00:00:00+00:00\",\"last_ts\":\"2026-07-01T01:00:00+00:00\"," +
            "\"utc_offset_min\":-300,\"event_count\":1,\"skipped_lines\":0,\"continuation\":false," +
            "\"total\":150,\"by_model\":{\"claude-fable-5\":150}," +
            "\"by_day\":{\"2026-07-01\":{\"total\":150,\"by_model\":{\"claude-fable-5\":150}}}}}}");

        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var shard));
        var bucket = shard.Sessions["u1"].ByDay["2026-07-01"];
        Assert.Equal(150, bucket.Total);
        Assert.Equal(0, bucket.Input);
        Assert.Equal(0, bucket.Output);                               // unsplit, not wrong
    }

    [Fact]
    public void Tail_parse_preserves_split_accumulation()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", input: 100, output: 50));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        File.AppendAllText(path, EventLine("2026-07-10T16:00:00Z", input: 30, output: 3) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesTailParsed);
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        var bucket = shard.Sessions["u1"].ByDay["2026-07-10"];
        Assert.Equal(130, bucket.Input);
        Assert.Equal(53, bucket.Output);
        Assert.Equal(bucket.Total, bucket.Input + bucket.Output);
    }
```

Append to `CcLogReaderTests.cs` (its real helpers: `AssistantEvent(tsIso, model, inTok, outTok, cwd, skill)`, `WriteSession(home, root, project, uuid, lines)`, `Iso(DateTimeOffset)`):

```csharp
    [Fact]
    public void Aggregates_carry_per_day_input_and_output()
    {
        using var home = new TempDir();
        var yesterday = DateTimeOffset.Now.AddDays(-1);
        WriteSession(home.Path, ".claude", "p1", "s1",
            AssistantEvent(Iso(yesterday), "claude-fable-5", 700, 70),
            AssistantEvent(Iso(yesterday), "claude-fable-5", 20, 2));

        var reader = new CcLogReader(home.Path);
        var agg = reader.AggregateForLocalCcTab(30);

        var day = DateOnly.FromDateTime(yesterday.LocalDateTime);
        Assert.Equal(720, agg.ByDayInput[day]);
        Assert.Equal(72, agg.ByDayOutput[day]);
        Assert.Equal(792, agg.ByDay[day]);
        Assert.Equal(agg.ByDay[day], agg.ByDayInput[day] + agg.ByDayOutput[day]);
    }

    [Fact]
    public void AggregateTodayOnly_carries_split_too()
    {
        using var home = new TempDir();
        var today = DateTimeOffset.Now;
        WriteSession(home.Path, ".claude", "p1", "s1",
            AssistantEvent(Iso(today), "claude-fable-5", 40, 4));

        var reader = new CcLogReader(home.Path);
        var agg = reader.AggregateTodayOnly();

        // Same midnight-race guard as the existing AggregateTodayOnly test.
        if (DateOnly.FromDateTime(today.LocalDateTime) != DateOnly.FromDateTime(DateTime.Now))
            return;
        var todayKey = DateOnly.FromDateTime(today.LocalDateTime);
        Assert.Equal(40, agg.ByDayInput.GetValueOrDefault(todayKey));
        Assert.Equal(4, agg.ByDayOutput.GetValueOrDefault(todayKey));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultSplit|FullyQualifiedName~Aggregates_carry"`
Expected: FAIL — no `Input`/`Output` members anywhere.

- [ ] **Step 3: Implement**

(a) `VaultDayBucket` gains (after `Total`):

```csharp
    /// <summary>Sent-side tokens (input). Always written on new buckets;
    /// absent on WS-C-era rows — 0 means "unsplit", and readers must treat a
    /// day whose input+output is 0 while total &gt; 0 as legacy, not as zero
    /// traffic (the Overview's "(partial)" rule).</summary>
    [JsonPropertyName("input")] public long Input { get; set; }

    /// <summary>Received-side tokens (output).</summary>
    [JsonPropertyName("output")] public long Output { get; set; }
```

`VaultRollupDay` gains the same pair (`[JsonPropertyName("input")]` / `("output")` after `Total`).

(b) `VaultIngester`: `DayAgg` gains `public long Input; public long Output;`. In `ProcessLine`, split the token read:

```csharp
        long input = Num(usage, "input_tokens");
        long output = Num(usage, "output_tokens");
        long tokens = input + output;
        if (tokens <= 0)
            return;
```

and after `bucket.Total += tokens;`:

```csharp
        bucket.Input += input;
        bucket.Output += output;
```

`BuildRows`' bucket projection adds `Input = kv.Value.Input, Output = kv.Value.Output,`. `SeedFromStored`'s bucket reconstruction adds `Input = bucket.Input, Output = bucket.Output` (via the DayAgg fields: `var d = new DayAgg { Total = bucket.Total, Input = bucket.Input, Output = bucket.Output };`). `RebuildRollups`' fold adds `d.Input += bucket.Input; d.Output += bucket.Output;`.

(c) `CcLogReader`: extend the record as in Interfaces; in BOTH `AggregateForLocalCcTab` and `AggregateTodayOnly`, add `byDayInput`/`byDayOutput` dictionaries accumulated from `ev.Usage.InputTokens` / `ev.Usage.OutputTokens` under the same counted-event gate, and pass them to the two `new LocalCcAggregate(...)` sites. Grep `new LocalCcAggregate(` repo-wide and fix every construct site the same way (App fallbacks construct `OverviewData`, not the aggregate — but verify, don't assume).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS — 424 + 4 new = 428. The WS-C byte-idempotency test must still pass (new fields serialize identically each cycle); `VaultRowMath` round-trip untouched (RecomputeRowAggregates does not touch input/output — they live on buckets only).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core windows-dotnet/tests/Sanduhr.Tests
git commit -m "feat(vault): sent/received split — bucket input/output, rollup fields, live per-day split"
```

---

### Task 3: Logical-session fold + read-side split surfaces (Core, TDD)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (`VaultRowMath.Merge` same-day summing)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultReader.cs` (logical fold, `VaultSessionInfo` agents fields, `VaultWindow` split, `CoveredSet`)
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs` (rollup distinct-logical-sessions count)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultLogicalSessionTests.cs`

**Interfaces:**
- Consumes: Task 1's `ParentSession`, Task 2's bucket `Input`/`Output`.
- Produces (Task 4 relies on these exact names):
  - `VaultSessionInfo` gains two trailing positional params: `int AgentCount, long AgentTokens` (update every construct site — `VaultReader.ReadSessions` and any test constructing it, e.g. the `TokensInScope` test).
  - `VaultWindow` gains two trailing positional params: `Dictionary<DateOnly, long> ByDayInput, Dictionary<DateOnly, long> ByDayOutput` (filled from rollup-day `Input`/`Output`; update construct + destructure sites).
  - `VaultReader.CoveredSet(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toInclusive)` → `HashSet<DateOnly>` — intersection semantics, metas loaded ONCE (the ReadWeeks preload lesson; the per-day public `IsDayCovered` stays for spot checks).
  - `VaultRowMath.Merge`: colliding day keys now SUM into a fresh bucket (`Total`, `ByModel`, `BySkill`, `Input`, `Output`) — never mutate an input row's bucket. Update the stale "no key collides" comment: slices still can't collide; MULTI-FILE session folds collide by design.
  - `ReadSessions` groups per root by `row.ParentSession ?? uuid`; the logical row's identity: `Uuid` = the logical id; project identity (`ProjectKey`/`ProjectName`/`Cwd`) from the member whose file-uuid equals the logical id (the main transcript) or, when the main aged out, the ordinal-first member; `FirstTs` min / `LastTs` max; `Total`/`ByModel`/`BySkill`/`ByDay` summed across members; `Cache` summed (`Read`/`Creation`) across members carrying it; `AgentCount` = members whose file-uuid ≠ logical id; `AgentTokens` = those members' summed `Total`.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/VaultLogicalSessionTests.cs` — fixture style follows `VaultReaderTests.cs` (shards written via `VaultStore` directly; copy its `Row`/`SaveShard` helpers, extending `Row` with optional `parentSession` and per-day `(input, output)`):

```csharp
    [Fact]
    public void Members_fold_into_one_logical_session_with_agent_stats()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("parent-1", Row("api~aaaaaaaa", "2026-07-10T10:00:00+00:00", "2026-07-10T18:00:00+00:00",
                continuation: false, parentSession: null, ("2026-07-10", 100))),
            ("agent-x", Row("api~aaaaaaaa", "2026-07-10T11:00:00+00:00", "2026-07-10T12:00:00+00:00",
                continuation: false, parentSession: "parent-1", ("2026-07-10", 200))),
            ("agent-y", Row("api~aaaaaaaa", "2026-07-10T13:00:00+00:00", "2026-07-10T19:00:00+00:00",
                continuation: false, parentSession: "parent-1", ("2026-07-10", 50))));

        var reader = new VaultReader(store);
        var sessions = reader.ReadSessions(new[] { ".claude" });

        var s = Assert.Single(sessions);
        Assert.Equal("parent-1", s.Uuid);
        Assert.Equal(350, s.Total);
        Assert.Equal(350, s.ByDay["2026-07-10"].Total);           // same-day buckets SUMMED
        Assert.Equal(2, s.AgentCount);
        Assert.Equal(250, s.AgentTokens);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero), s.FirstTs);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 19, 0, 0, TimeSpan.Zero), s.LastTs);  // agent outlived main
    }

    [Fact]
    public void Agent_only_session_survives_when_main_transcript_aged_out()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("agent-x", Row("api~aaaaaaaa", "2026-07-10T11:00:00+00:00", "2026-07-10T12:00:00+00:00",
                continuation: false, parentSession: "parent-gone", ("2026-07-10", 200))));

        var reader = new VaultReader(store);
        var s = Assert.Single(reader.ReadSessions(new[] { ".claude" }));
        Assert.Equal("parent-gone", s.Uuid);
        Assert.Equal("api", s.ProjectName);                        // identity from first member
        Assert.Equal(1, s.AgentCount);
        Assert.Equal(200, s.AgentTokens);
    }

    [Fact]
    public void Sessions_without_parents_behave_exactly_as_before()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("u1", Row("api~aaaaaaaa", "2026-07-10T10:00:00+00:00", "2026-07-10T11:00:00+00:00",
                continuation: false, parentSession: null, ("2026-07-10", 100))));

        var s = Assert.Single(new VaultReader(store).ReadSessions(new[] { ".claude" }));
        Assert.Equal("u1", s.Uuid);
        Assert.Equal(0, s.AgentCount);
        Assert.Equal(0, s.AgentTokens);
    }

    [Fact]
    public void Rollup_sessions_count_is_distinct_logical_sessions()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", ParentUuid, EventLine("2026-07-10T15:00:00Z"));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-x", null,
            EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));
        WriteNested(home.Path, ".claude", ParentUuid, "agent-y", null,
            EventLine("2026-07-10T17:00:00Z", input: 50, output: 0));

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadRollupShard(".claude", "2026-07", out var roll);
        Assert.Equal(1, roll.Days["2026-07-10"].Sessions);         // 3 files, ONE session
        Assert.Equal(400, roll.Days["2026-07-10"].Total);
    }

    [Fact]
    public void VaultWindow_carries_split_and_CoveredSet_batches_coverage()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var roll = new VaultRollupShard { SchemaVersion = 1 };
        roll.Days["2026-07-10"] = new VaultRollupDay
        {
            Total = 130, Input = 100, Output = 30, Sessions = 1,
            ByModel = new() { ["claude-fable-5"] = 130 },
            ByProject = new() { ["api~aaaaaaaa"] = 130 },
        };
        store.SaveRollupShard(".claude", "2026-07", roll);
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            Covered = new List<VaultDateRange> { new() { From = "2026-07-05", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
            WalkVersion = 2,
        });

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));
        Assert.Equal(100, w.ByDayInput[new DateOnly(2026, 7, 10)]);
        Assert.Equal(30, w.ByDayOutput[new DateOnly(2026, 7, 10)]);

        var covered = reader.CoveredSet(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));
        Assert.Contains(new DateOnly(2026, 7, 5), covered);
        Assert.DoesNotContain(new DateOnly(2026, 7, 4), covered);
    }
```

(The rollup test needs the `WriteNested`/`ParentUuid` helpers — copy them from Task 1's test file; the standalone-file convention holds.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultLogicalSession"`
Expected: FAIL — `AgentCount` etc. missing; three files produce three ledger sessions; rollup counts 3.

- [ ] **Step 3: Implement**

(a) `VaultRowMath.Merge` — replace the day-copy loop:

```csharp
        foreach (var row in rows)
        {
            foreach (var (day, bucket) in row.ByDay)
            {
                // Slices of ONE file can't collide (day-in-own-month invariant),
                // but multi-file logical folds reuse this merge and DO collide:
                // same-day buckets sum into a fresh bucket, never mutating inputs.
                if (!merged.ByDay.TryGetValue(day, out var acc))
                {
                    merged.ByDay[day] = acc = new VaultDayBucket();
                }
                acc.Total += bucket.Total;
                acc.Input += bucket.Input;
                acc.Output += bucket.Output;
                foreach (var (m, v) in bucket.ByModel)
                    acc.ByModel[m] = acc.ByModel.GetValueOrDefault(m) + v;
                if (bucket.BySkill is not null)
                {
                    acc.BySkill ??= new Dictionary<string, long>();
                    foreach (var (s, v) in bucket.BySkill)
                        acc.BySkill[s] = acc.BySkill.GetValueOrDefault(s) + v;
                }
            }
        }
```

(b) `VaultReader.ReadSessions` — after the existing per-uuid file merge, add the logical fold:

```csharp
            // Logical fold: a session = its main transcript + nested subagent
            // transcripts (parent_session ?? uuid). Identity comes from the
            // main member when present; an aged-out main leaves the ordinal-
            // first member to speak for the group.
            var byLogical = new Dictionary<string, List<(string FileUuid, VaultSessionRow Row)>>(StringComparer.Ordinal);
            foreach (var (uuid, rows) in byUuid)
            {
                var merged = VaultRowMath.Merge(rows);
                var logicalId = merged.ParentSession ?? uuid;
                if (!byLogical.TryGetValue(logicalId, out var list))
                    byLogical[logicalId] = list = new List<(string, VaultSessionRow)>();
                list.Add((uuid, merged));
            }
            foreach (var (logicalId, members) in byLogical)
            {
                members.Sort((a, b) => string.CompareOrdinal(a.FileUuid, b.FileUuid));
                var identity = members.FirstOrDefault(m => m.FileUuid == logicalId).Row
                               ?? members[0].Row;
                var fold = VaultRowMath.Merge(members.Select(m => m.Row).ToList());
                // Merge() takes first_ts/last_ts/cache from the primary; a
                // logical fold needs min/max/sums across members instead.
                var firstTs = members.Min(m => ParseTsOrMax(m.Row.FirstTs));
                var lastTs = members.Max(m => ParseTsOrMin(m.Row.LastTs));
                long cacheRead = members.Sum(m => m.Row.CacheTokens?.Read ?? 0);
                long cacheCreation = members.Sum(m => m.Row.CacheTokens?.Creation ?? 0);
                int agentCount = members.Count(m => m.FileUuid != logicalId);
                long agentTokens = members.Where(m => m.FileUuid != logicalId).Sum(m => m.Row.Total);
                if (firstTs == DateTimeOffset.MaxValue || lastTs == DateTimeOffset.MinValue)
                    continue;
                result.Add(new VaultSessionInfo(
                    logicalId, root, identity.ProjectKey, identity.ProjectName, identity.Cwd,
                    firstTs, lastTs, fold.Total, fold.ByModel, fold.BySkill, fold.ByDay,
                    (cacheRead + cacheCreation) > 0
                        ? new VaultCacheTokens { Read = cacheRead, Creation = cacheCreation }
                        : null,
                    agentCount, agentTokens));
            }
```

with the two small parse helpers (`ParseTsOrMax`/`ParseTsOrMin` wrapping the existing `TryTs`, returning `DateTimeOffset.MaxValue`/`MinValue` on failure). Note `members.FirstOrDefault(...)` on a value-tuple list: use `members.Where(m => m.FileUuid == logicalId).Select(m => m.Row).FirstOrDefault()` to keep null semantics — implement it that way.

(c) `VaultSessionInfo` and `VaultWindow` records — append the new positional params per Interfaces; `ReadWindow` fills `ByDayInput`/`ByDayOutput` from `day.Input`/`day.Output` in the same loop that fills `ByDay`.

(d) `CoveredSet`:

```csharp
    /// <summary>Batch coverage: every day in [from, to] covered by EVERY
    /// consented root. Metas load once — the per-day IsDayCovered would be
    /// two file reads per cell on a UI path (the ReadWeeks lesson).</summary>
    public HashSet<DateOnly> CoveredSet(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toInclusive)
    {
        var result = new HashSet<DateOnly>();
        if (roots.Count == 0)
            return result;
        var metas = roots.Select(r => _store.LoadMeta(r)).ToList();
        for (var d = fromInclusive; d <= toInclusive; d = d.AddDays(1))
        {
            if (IsDayCovered(metas, d))
                result.Add(d);
        }
        return result;
    }
```

(e) `VaultIngester.RebuildRollups` — the fold loop is fully replaced (the method's shard-sourcing head stays as-is). New fold body:

```csharp
        var days = new Dictionary<string, VaultRollupDay>(StringComparer.Ordinal);
        var sessionsPerDay = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (uuid, row) in shard.Sessions)
        {
            var logicalId = row.ParentSession ?? uuid;
            foreach (var (day, bucket) in row.ByDay)
            {
                if (!days.TryGetValue(day, out var d))
                    days[day] = d = new VaultRollupDay();
                d.Total += bucket.Total;
                d.Input += bucket.Input;
                d.Output += bucket.Output;
                foreach (var (m, v) in bucket.ByModel)
                    d.ByModel[m] = d.ByModel.GetValueOrDefault(m) + v;
                d.ByProject[row.ProjectKey] = d.ByProject.GetValueOrDefault(row.ProjectKey) + bucket.Total;
                if (bucket.BySkill is not null)
                    foreach (var (s, v) in bucket.BySkill)
                        d.BySkill[s] = d.BySkill.GetValueOrDefault(s) + v;

                // Sessions = DISTINCT LOGICAL sessions touching the day — a
                // main transcript plus its N agent files is ONE session.
                if (!sessionsPerDay.TryGetValue(day, out var set))
                    sessionsPerDay[day] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(logicalId);
            }
        }
        foreach (var (day, set) in sessionsPerDay)
            days[day].Sessions = set.Count;
```

(the old `d.Sessions++` line is gone; iteration switches from `shard.Sessions.Values` to the keyed `shard.Sessions` pairs so `uuid` is in scope; the trailing `SaveRollupShard` call stays unchanged).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS — 428 + 5 new = 433, AND the existing `VaultReaderTests` (sessions merge by uuid, same-uuid-two-roots) still pass — parentless sessions must be bit-for-bit unaffected. Existing `VaultSessionInfo` construct sites in tests gain `, 0, 0`.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core windows-dotnet/tests/Sanduhr.Tests
git commit -m "feat(vault): logical-session fold (parent ?? uuid), agent stats, window split, batched coverage"
```

---

### Task 4: Overview split line + Ledger agents line (App)

App layer has no tests — build + reviewer diff are the gates. A running Sanduhr.exe may lock the default output: verify with a scratch `-o` build, do NOT kill it.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`

**Interfaces:**
- Consumes: `LocalCcAggregate.ByDayInput/ByDayOutput` (Task 2), `VaultWindow.ByDayInput/ByDayOutput` and `VaultSessionInfo.AgentCount/AgentTokens` (Task 3), `TokenFormat.Compact`, the existing `StrVis` converter.
- Produces: `LocalCcViewModel.TodaySplitText` / `MonthSplitText` (observable strings, empty when the figure is zero); the Ledger expansion's agents line.

- [ ] **Step 1: `LocalCcViewModel` — extend `OverviewData` and `Compute`**

`OverviewData` gains four longs + a flag:

```csharp
    private sealed record OverviewData(
        Dictionary<DateOnly, long> ByDay,
        Dictionary<string, long> Projects,
        Dictionary<string, long> Skills,
        string StatusLine,
        long SentToday,
        long ReceivedToday,
        long SentWindow,
        long ReceivedWindow,
        bool WindowSplitPartial);
```

In `Compute`'s live/degraded branch, the split comes entirely from the live aggregate:

```csharp
            long sentW = agg.ByDayInput.Values.Sum();
            long recvW = agg.ByDayOutput.Values.Sum();
            long sentT = agg.ByDayInput.GetValueOrDefault(today);
            long recvT = agg.ByDayOutput.GetValueOrDefault(today);
            long liveTotal = agg.ByDay.Values.Sum();
            return new OverviewData(
                new Dictionary<DateOnly, long>(agg.ByDay), byName,
                new Dictionary<string, long>(agg.BySkill),
                !vaultOn ? "history vault off — these numbers are the live logs, not an archive"
                         : "history vault paused — showing live logs only",
                sentT, recvT, sentW, recvW,
                liveTotal > 0 && (sentW + recvW) < (long)(liveTotal * 0.95));
```

In the vault branch, closed days come from `win.ByDayInput/ByDayOutput`, hot days from `live.ByDayInput/ByDayOutput` (same hot-boundary loop the totals use), today's pair from `todayAgg`:

```csharp
        long sentWindow = win.ByDayInput.Values.Sum();
        long recvWindow = win.ByDayOutput.Values.Sum();
        for (var d = hotStart; d <= today; d = d.AddDays(1))
        {
            sentWindow += live.ByDayInput.GetValueOrDefault(d);
            recvWindow += live.ByDayOutput.GetValueOrDefault(d);
        }
        long sentToday = todayAgg.ByDayInput.GetValueOrDefault(today);
        long recvToday = todayAgg.ByDayOutput.GetValueOrDefault(today);
        long windowTotal = byDay.Values.Sum();
        bool partial = windowTotal > 0 && (sentWindow + recvWindow) < (long)(windowTotal * 0.95);
```

and the branch's `return` passes the five new values. In `Apply`, build the two texts (new `[ObservableProperty] string _todaySplitText = ""` / `_monthSplitText = ""`):

```csharp
        TodaySplitText = todayTotal > 0
            ? $"↑ {TokenFormat.Compact(data.SentToday)} sent · ↓ {TokenFormat.Compact(data.ReceivedToday)} received"
            : "";
        MonthSplitText = monthTotal > 0
            ? $"↑ {TokenFormat.Compact(data.SentWindow)} sent · ↓ {TokenFormat.Compact(data.ReceivedWindow)} received"
              + (data.WindowSplitPartial ? " (partial)" : "")
            : "";
```

(`todayTotal`/`monthTotal` already exist in `Apply`.) Every construct site of `OverviewData` — including `RefreshAsync`'s catch fallback — gains the five new arguments (`0, 0, 0, 0, false` for the fallback).

- [ ] **Step 2: XAML — the two split lines**

In the Overview's totals row (SettingsWindow.xaml, the two StackPanels holding Today / Last 30 days), add under each big TextBlock:

```xml
                                        <TextBlock Text="{Binding TodaySplitText}" FontSize="10"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                                   Visibility="{Binding TodaySplitText, Converter={StaticResource StrVis}}" />
```

(and the `MonthSplitText` twin in the second StackPanel).

- [ ] **Step 3: Ledger agents line**

In `CcLedgerViewModel.BuildDetail`, after the `Span (wall-clock) · Lifetime` line append:

```csharp
        if (Info.AgentCount > 0)
        {
            sb.Append("\nAgents: ").Append(Info.AgentCount)
              .Append(" · ").Append(TokenFormat.Compact(Info.AgentTokens)).Append(" tokens");
        }
```

- [ ] **Step 4: Build + suite + commit**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
git add windows-dotnet/src/Sanduhr.App
git commit -m "feat(vault): Overview sent/received lines with (partial) honesty; Ledger agents line"
```

Expected: 0 errors, 433 green.

---

### Task 5: Rolling calendar (App)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Views/CcCalendarControl.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs` (uncovered-days feed)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (+ `.xaml.cs` render bridge)

**Interfaces:**
- Consumes: `VaultReader.CoveredSet` (Task 3), the Overview's merged `ByDay` (existing `LocalCcViewModel.ByDay`), `TokenFormat.Compact`, palette colors.
- Produces: `CcCalendarControl.SetData(IReadOnlyDictionary<DateOnly, long> byDay, IReadOnlySet<DateOnly> uncovered, ThemePalette palette)`; `LocalCcViewModel.UncoveredDays` (`IReadOnlySet<DateOnly>`, recomputed each `RefreshAsync`).

- [ ] **Step 1: `LocalCcViewModel` uncovered-days feed**

`OverviewData` gains one more member `HashSet<DateOnly> Uncovered` (recompute in `Compute`; catch-fallback passes an empty set). In `Compute` (both branches, before returning):

```csharp
        var calFrom = today.AddDays(-34);
        var uncovered = new HashSet<DateOnly>();
        var covered = (vault is not null && roots.Count > 0)
            ? vault.Reader.CoveredSet(roots, calFrom, today)
            : new HashSet<DateOnly>();
        for (var d = calFrom; d <= today; d = d.AddDays(1))
        {
            if (!covered.Contains(d))
                uncovered.Add(d);
        }
```

Expose `private HashSet<DateOnly> _uncovered = new(); public IReadOnlySet<DateOnly> UncoveredDays => _uncovered;` and assign in `Apply` before `Changed?.Invoke()`. (Vault off ⇒ every day uncovered ⇒ the calendar renders all-texture — honest: there is no archive.)

- [ ] **Step 2: `CcCalendarControl`**

Create `windows-dotnet/src/Sanduhr.App/Views/CcCalendarControl.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Rolling 5-week calendar under the Overview bar strip: Monday-start weekday
/// columns, the last 35 local days ending today. Heat = accent alpha in four
/// perceptual steps (any nonzero day stays visibly nonzero); covered zero days
/// get a faint tick dot; uncovered days get the dotted no-record texture
/// (never a blank that reads as zero). Today wears a 1px accent outline.
/// Hover shows "{MMM d} — {compact}" via mouse-move hit-testing.
/// </summary>
public sealed class CcCalendarControl : FrameworkElement
{
    private const int DaysBack = 34;   // 35 days inclusive of today
    private const int Rows = 5;
    private const int Cols = 7;
    private const double HeaderBand = 14;

    private IReadOnlyDictionary<DateOnly, long> _byDay = new Dictionary<DateOnly, long>();
    private IReadOnlySet<DateOnly> _uncovered = new HashSet<DateOnly>();
    private ThemePalette _palette = ThemePalette.Obsidian;

    public CcCalendarControl()
    {
        MinHeight = 96;
    }

    public void SetData(
        IReadOnlyDictionary<DateOnly, long> byDay,
        IReadOnlySet<DateOnly> uncovered,
        ThemePalette palette)
    {
        _byDay = byDay ?? new Dictionary<DateOnly, long>();
        _uncovered = uncovered ?? new HashSet<DateOnly>();
        _palette = palette;
        InvalidateVisual();
        UpdateTooltip(Mouse.GetPosition(this));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var gridTop = HeaderBand;
        double cellW = (w - (Cols - 1) * 2) / Cols;
        double cellH = (h - gridTop - (Rows - 1) * 2) / Rows;

        // Weekday header (Monday-start initials).
        string[] initials = { "M", "T", "W", "T", "F", "S", "S" };
        var dim = Frozen(_palette.TextDim);
        for (int c = 0; c < Cols; c++)
        {
            var ft = new FormattedText(initials[c], CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, dim, dpi)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = cellW,
            };
            dc.DrawText(ft, new Point(c * (cellW + 2), 0));
        }

        long maxV = 0;
        foreach (var v in _byDay.Values)
            if (v > maxV) maxV = v;

        var gapBrush = NoRecordBrush();
        var tickBrush = Frozen(WithAlpha(_palette.TextDim, 80));
        var outlinePen = new Pen(Frozen(_palette.Accent), 1);
        outlinePen.Freeze();

        foreach (var (day, rect) in Cells(today, w, h))
        {
            if (day > today)
                continue;
            if (_uncovered.Contains(day))
            {
                dc.DrawRectangle(gapBrush, null, rect);
                continue;
            }
            long v = _byDay.GetValueOrDefault(day);
            if (v == 0)
            {
                dc.DrawRectangle(tickBrush, null,
                    new Rect(rect.X + rect.Width / 2 - 1, rect.Y + rect.Height / 2 - 1, 2, 2));
            }
            else
            {
                // Four perceptual steps of accent — quartiles of the window max.
                byte alpha = v >= maxV * 0.75 ? (byte)230
                           : v >= maxV * 0.50 ? (byte)170
                           : v >= maxV * 0.25 ? (byte)110
                           : (byte)60;
                dc.DrawRectangle(Frozen(WithAlpha(_palette.Accent, alpha)), null, rect);
            }
            if (day == today)
                dc.DrawRectangle(null, outlinePen, rect);
        }
    }

    /// <summary>Cell geometry: 5 rows x 7 Monday-start columns, bottom row ends
    /// at today's week; the top row leads with empty cells before day 1.</summary>
    private IEnumerable<(DateOnly Day, Rect Rect)> Cells(DateOnly today, double w, double h)
    {
        double cellW = (w - (Cols - 1) * 2) / Cols;
        double cellH = (h - HeaderBand - (Rows - 1) * 2) / Rows;
        var first = today.AddDays(-DaysBack);
        // Monday-align the grid start (may precede `first`; those cells skip).
        int firstDow = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-firstDow);
        for (int i = 0; i < Rows * Cols; i++)
        {
            var day = gridStart.AddDays(i);
            if (day < first)
                continue;
            int row = i / Cols, col = i % Cols;
            yield return (day, new Rect(
                col * (cellW + 2), HeaderBand + row * (cellH + 2), cellW, cellH));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateTooltip(e.GetPosition(this));
    }

    private void UpdateTooltip(Point p)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (day, rect) in Cells(today, ActualWidth, ActualHeight))
        {
            if (day <= today && rect.Contains(p))
            {
                long v = _byDay.GetValueOrDefault(day);
                ToolTip = _uncovered.Contains(day)
                    ? $"{day.ToString("MMM d", CultureInfo.InvariantCulture)} — no record"
                    : $"{day.ToString("MMM d", CultureInfo.InvariantCulture)} — {TokenFormat.Compact(v)} tokens";
                return;
            }
        }
        ToolTip = null;
    }

    /// <summary>Dotted TextDim texture — identical recipe to CcTrendsControl's
    /// "no record" brush so the two surfaces speak one language.</summary>
    private Brush NoRecordBrush()
    {
        var dot = Frozen(WithAlpha(_palette.TextDim, 110));
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 6, 6));
            ctx.DrawRectangle(dot, null, new Rect(2, 2, 2, 2));
        }
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
```

- [ ] **Step 3: Placement + bridge**

In SettingsWindow.xaml's Overview grid: add a `RowDefinition Height="Auto"` after the bar strip's row, insert

```xml
                                <views:CcCalendarControl x:Name="CalendarStrip" Grid.Row="4"
                                                         Height="96" Margin="0,0,0,8" />
```

and renumber every later `Grid.Row` in that grid (+1: the breakdown checkbox, the tables row, the stewardship strip). In `SettingsWindow.xaml.cs`, extend `RenderLocalCc`:

```csharp
    private void RenderLocalCc()
    {
        BarStrip.SetData(ViewModel.ClaudeCode.Overview.ByDay, ViewModel.ClaudeCode.Overview.Palette);
        CalendarStrip.SetData(
            ViewModel.ClaudeCode.Overview.ByDay,
            ViewModel.ClaudeCode.Overview.UncoveredDays,
            ViewModel.ClaudeCode.Overview.Palette);
    }
```

- [ ] **Step 4: Build + suite + commit**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
git add windows-dotnet/src/Sanduhr.App
git commit -m "feat(vault): rolling 5-week calendar under the Overview strip — heat, textures, hover"
```

---

### Task 6: Tier-card % backplate chip (App)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/TierCard.xaml`

**Interfaces:** consumes nothing new; visual-only.

**Spec refinement (documented deviation):** the spec says "bold accent ink"; the % currently carries `BarColor` — the utilization severity signal (green/amber/red). Severity information outranks ink uniformity, so the chip keeps the `BarColor` foreground; the chip background is what guarantees contrast. Reviewer: this is a deliberate, controller-approved deviation — assess the contrast outcome, not conformance to the ink wording.

- [ ] **Step 1: Wrap the % in a chip**

In `TierCard.xaml`, replace the Row-1 `ValueText` TextBlock (Grid.Column 3):

```xml
                <!-- % backplate chip: the value used to sit bare over the
                     sparkline and blended in several themes. The chip carries
                     the contrast; the ink keeps the severity color. -->
                <Border Grid.Column="3" VerticalAlignment="Center" CornerRadius="4"
                        Padding="6,1"
                        Background="{DynamicResource Sanduhr.Brush.Bg}"
                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                        BorderThickness="1">
                    <TextBlock Text="{Binding ValueText}" VerticalAlignment="Center"
                               FontSize="15" FontWeight="Bold"
                               FontFamily="{DynamicResource Sanduhr.Font.Value}"
                               Effect="{DynamicResource Sanduhr.Effect.ValueBloom}"
                               Foreground="{Binding BarColor, Converter={StaticResource ColorBrush}}" />
                </Border>
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
git add windows-dotnet/src/Sanduhr.App/Views/TierCard.xaml
git commit -m "fix(widget): tier-card % gets a backplate chip — it blended into the sparkline"
```

---

### Task 7: Docs + PR

**Files:**
- Modify: `docs/PRIVACY.md`, `docs/smoke-test-plan.md`

- [ ] **Step 1: PRIVACY.md** — in the vault row, change "one summary row per session" to "one summary row per session (including its subagent transcripts)". Update `**Last updated:**` to the current date.

- [ ] **Step 2: Smoke additions** — append to `docs/smoke-test-plan.md`:

```markdown
---

## WS-C.1 — subagent coverage, split, chip, calendar (2026-07-13)

1. **The jump.** First launch on this build: within a cycle, Overview's 30-day total rises sharply (subagent transcripts now count) and `sanduhr.log` shows one "walk upgraded" line per home. Second launch: no further jump, no repeated upgrade line.
2. **Ledger folds agents.** A subagent-heavy session shows ONE ledger row; its expansion carries "Agents: N · X tokens"; the flat list has no agent-* rows.
3. **Split lines.** Overview shows "↑ … sent · ↓ … received" under both figures; the 30-day line carries "(partial)" while pre-upgrade days remain in the window, and today's line never does.
4. **Calendar honesty.** 5-week grid below the strip: heat where there's history, dotted no-record texture on uncovered days, faint tick on covered-zero days, today outlined; hover names the day + count ("no record" on textured cells). Vault off → all texture.
5. **% chip.** Widget tier cards: the % sits on a chip and reads clearly over BOTH sparkline styles in default/dark/light/Matrix.
6. **Regression trio.** Stack toggle still stacks; erase dialogs still say "Erase it / Keep data"; the paused/off status lines still show in their states.
```

- [ ] **Step 3: Suite + push + PR**

```bash
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
git add docs/PRIVACY.md docs/smoke-test-plan.md
git commit -m "docs(vault): subagent clause in PRIVACY, WS-C.1 smoke scenarios"
git push -u origin feat/ws-c1-polish-wave
gh pr create --title "feat: WS-C.1 — subagent coverage, sent/received split, % chip, rolling calendar" --body "$(cat <<'EOF'
## Summary
- Recursive walk everywhere: nested subagent/workflow transcripts finally count (vault AND live — the day-one jump is the correction). parent_session folds them into their spawning session; one-shot walk_version re-ingest heals everything inside CC retention
- Sent/received split stored per day bucket (conservation-tested) with Overview split lines + "(partial)" honesty marker
- Widget tier-card % backplate chip; Overview rolling 5-week calendar with heat/no-record/hover

Spec: docs/superpowers/specs/2026-07-13-ws-c1-polish-wave-design.md

## Test plan
- [ ] Full suite green (baseline 418 -> ~433)
- [ ] Smoke: docs/smoke-test-plan.md WS-C.1 scenarios 1-6

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR opens against main. Do NOT merge — human smoke first.

---

## Execution notes for the controller

- Suggested dispatch models: Tasks 1–3 most capable; Tasks 4–6 standard; Task 7 cheap. Final whole-branch review: most capable.
- Task 1's upgrade test manipulates meta/checkpoint files by hand — if the implementer finds a simpler faithful regression setup, the binding assertions are: upgrade-equals-fresh (same nowUtc), exactly-once invalidation, aged-out rows untouched.
- The user runs a debug Sanduhr.exe most of the day — every build step assumes scratch `-o` verification; never kill the running instance.
- Test-count checkpoints: 418 → 424 (T1) → 428 (T2) → 433 (T3); Tasks 4–6 hold 433.


