# WS-C Usage Vault + Claude Code Trends + Session Ledger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A durable local vault of Claude Code session usage that outlives CC's ~30-day log retention, plus a reworked Settings tab ("Claude Code": Overview / Trends / Sessions) that reads it — per the approved spec `docs/superpowers/specs/2026-07-12-usage-vault-design.md`.

**Architecture:** Core owns everything testable: `VaultStore` (atomic per-root shard IO, quarantine, checkpoints), `VaultIngester` (checkpointed walk, guarded tail-parse, month slicing, rollup fold, cross-process mutex), `VaultReader` (closed-day windows, weekly buckets, session merge, coverage), and `VaultLedgerCsv`. The App layer is thin wiring: a `VaultService` (consent + fire-and-forget trigger), a consent dialog, and three sub-sections in the renamed Claude Code tab. Session shards are the irreplaceable primary record (raw model strings, unconditional totals); rollups are a rebuildable derived cache; checkpoints are disposable bookkeeping written LAST.

**Tech Stack:** .NET 10 WPF (`windows-dotnet/`), CommunityToolkit.Mvvm, xUnit, System.Text.Json (`JsonNode` for parsing, `JsonSerializer` + `JsonPropertyName` for shards), `System.Security.Cryptography.SHA256`, named `Mutex`.

## Global Constraints

- Test command: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`. App build: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`. Baseline: 360 tests green before Task 1 (verified by running the suite; if the number on disk differs, trust the suite and carry the delta through every gate below).
- **Vault location (exact):** `%LOCALAPPDATA%\Sanduhr\vault\{root-basename}\` (`Paths.VaultDir` on `Environment.SpecialFolder.LocalApplicationData`). NEVER roaming AppData — enterprise roaming profiles would sync a work-activity archive to employer infrastructure.
- **Raw model strings, never tiers, in anything persisted.** Tier is a read-time projection through `CcLogReader.TierForModel`. 49% of live events are `claude-fable-5` (unmapped) — tier-keyed storage would permanently destroy them.
- **Unconditional `total`** at session-row, day-bucket, and rollup levels. Sums of filtered maps are display math, never the stored truth. Row invariant: `row.total == sum(row.by_day[*].total)`.
- **Write ordering invariant:** session shard(s) → rollup shard(s) → checkpoints LAST. A session-shard write failure aborts that root's cycle before rollups/checkpoints. Checkpoint stat is taken BEFORE opening the file.
- **File opens:** ingester-owned `FileStream` with `FileShare.ReadWrite | FileShare.Delete`. Never `CcLogReader.IterUsageEvents` (silent empty-on-failure is fatal to a checkpointed ingest). "Read failed" advances NO checkpoint and replaces NO row.
- **Cross-process writer exclusion:** named mutex `Global\Sanduhr.VaultWriter`, try-acquire (0 timeout), holder-skip + one log line.
- **Logging (all sinks — `sanduhr.log`):** operation + exception TYPE name + counts only. Never raw JSONL lines, never `e.Message`, never `e.ToString()`, never paths, project names, skill names, or account labels. A test asserts a failed ingest's log output contains no `\` or `/`.
- **Schema evolution:** meaning changes take a NEW field name; readers accept every `schema_version <= 1` forever; no in-place migration of session shards. Rollups exempt (any change = full rebuild).
- **Theming:** every new surface uses `{DynamicResource Sanduhr.Brush.*}` only; dialogs follow the `ThemedDialog` pattern; sort direction shown by glyph (▲/▼) not color alone; tier/model badges carry text.
- **Ledger virtualization (binding — the App layer has no tests, this is the gate):** the list owns its scrolling (star-sized row, NO ancestor ScrollViewer); `VirtualizationMode=Recycling` + `ScrollUnit=Pixel` asserted in XAML; `IsExpanded` lives on the row VM; sorting via `ListCollectionView.CustomSort` with a typed comparer; live refresh diffs rows by uuid (never `Clear()`+re-add). Control is `ListBox` + `VirtualizingStackPanel`, NOT `DataGrid`.
- **Copy (verbatim from spec):** Overview blurb *"Claude Code deletes session logs after ~30 days. Sanduhr keeps a local history vault so your trends survive — never uploaded, per-home opt-in, erase any time below."*; degraded status line *"history vault paused — showing live logs only"*; Trends footer *"history preserved since {MMMM d, yyyy}"*.
- **Settings keys (exact):** `vault_prompted` (bool, default false), `vault_roots` (object `{".claude": true, ...}`), `vault_store_full_paths` (bool, default false, no UI).
- Ingestion must never break the fetch loop or the UI: fire-and-forget `Task.Run`, single-flight, every path try/caught.
- Branch: `feat/ws-c-usage-vault` (Task 1 creates). Main is PR-only — the final task opens a PR, it does not merge.
- Conventional commits; commit at the end of every task step that says commit.
- Date/month key formats (exact, InvariantCulture): day `yyyy-MM-dd`, month `yyyy-MM`, both LOCAL time at ingest (`TimeZoneInfo` injectable for tests).

## File Structure

| File | Role |
|---|---|
| `windows-dotnet/src/Sanduhr.Core/Paths.cs` (modify) | + `localAppDataBase` ctor param, + `VaultDir` |
| `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (new) | Shard/checkpoint/meta schema types + `VaultRowMath` (row build/merge shared by ingester and reader) |
| `windows-dotnet/src/Sanduhr.Core/VaultStore.cs` (new) | Per-root atomic shard IO, quarantine + checkpoint invalidation, meta IO, purge |
| `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs` (new) | Checkpointed walk, parse, tail-parse, slicing, rollup fold, mutex |
| `windows-dotnet/src/Sanduhr.Core/VaultReader.cs` (new) | Closed-day window, weekly buckets, session merge, coverage, birth date, staleness |
| `windows-dotnet/src/Sanduhr.Core/VaultLedgerCsv.cs` (new) | Ledger CSV builder (Core-tested, IO-free) |
| `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (modify) | + static `TierForModel`, + `AggregateTodayOnly()` |
| `windows-dotnet/src/Sanduhr.App/Services/VaultService.cs` (new) | Consent state, single-flight trigger, purge/erase, IngestCompleted event |
| `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (modify) | + vault consent keys |
| `windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml(.cs)` (new) | Themed first-run per-root consent |
| `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (modify) | Vault trigger on fetch cycle + startup + day-rollover; 30s-tick hygiene |
| `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs` (new) | Sub-nav parent (Overview / Trends / Sessions) |
| `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs` (modify) | Overview: vault+live merge, degraded mode, stewardship |
| `windows-dotnet/src/Sanduhr.App/ViewModels/CcTrendsViewModel.cs` (new) | Weekly buckets + range + footer |
| `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs` (new) | Ledger rows, scope chips, sort, CSV export |
| `windows-dotnet/src/Sanduhr.App/Views/CcTrendsControl.cs` (new) | OnRender weekly bars (hatched current week, no-record texture) |
| `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml(.cs)` (modify) | Tab rename + sub-nav + three sections |
| `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (modify) | Compose new VMs |
| `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (modify) | VaultService construction + consent prompt |
| `windows-dotnet/tests/Sanduhr.Tests/VaultStoreTests.cs` / `VaultIngesterTests.cs` / `VaultIngesterHardeningTests.cs` / `VaultReaderTests.cs` / `VaultLedgerCsvTests.cs` (new), `PathsTests.cs` + `CcLogReaderTests.cs` (modify) | The adversarial battery |
| `docs/PRIVACY.md`, `docs/smoke-test-plan.md`, `docs/roadmap-2026-07-11.md` (modify) | Privacy rows, smoke scenarios, triple-parse retirement bank note |

Suggested per-task models: Tasks 1, 4 standard; Tasks 2, 3 most capable (the ingester is the bulk and the risk); Tasks 5–8 standard; Task 9 cheap.

---

### Task 1: `Paths.VaultDir` + vault schema types + `VaultStore` (Core, TDD)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/Paths.cs`
- Create: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs`
- Create: `windows-dotnet/src/Sanduhr.Core/VaultStore.cs`
- Test: modify `windows-dotnet/tests/Sanduhr.Tests/PathsTests.cs`, create `windows-dotnet/tests/Sanduhr.Tests/VaultStoreTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (Tasks 2–8 rely on these exact names):
  - `Paths` gains ctor param `string? localAppDataBase = null` (defaults to `Environment.SpecialFolder.LocalApplicationData`) and `string VaultDir` (`{localAppDataBase}\Sanduhr\vault`, NOT auto-created).
  - `VaultDayBucket { long Total; Dictionary<string,long> ByModel; Dictionary<string,long>? BySkill }`
  - `VaultCacheTokens { long Read; long Creation }`
  - `VaultSessionRow { string ProjectKey; string ProjectName; string? Cwd; string FirstTs; string LastTs; int UtcOffsetMin; long EventCount; long SkippedLines; bool Continuation; long Total; Dictionary<string,long> ByModel; VaultCacheTokens? CacheTokens; Dictionary<string,long>? BySkill; Dictionary<string,VaultDayBucket> ByDay }`
  - `VaultSessionShard { int SchemaVersion; string WriterVersion; Dictionary<string,VaultSessionRow> Sessions }`
  - `VaultRollupDay { long Total; Dictionary<string,long> ByModel; Dictionary<string,long> ByProject; Dictionary<string,long> BySkill; int Sessions }`
  - `VaultRollupShard { int SchemaVersion; Dictionary<string,VaultRollupDay> Days }`
  - `VaultCheckpointEntry { long MtimeTicks; long Length; long Offset; string TailGuard; long RowTotal; long RowEvents; long RowCacheRead; long RowCacheCreation; List<string> Months; bool Sealed; string LastSeenTs }` — the `Row*` quartet fingerprints the stored rows this checkpoint corresponds to (crash-vs-tail guard, see Task 3; cache counters included because a cache-only tail changes no token totals)
  - `VaultCheckpointFile { int SchemaVersion; Dictionary<string,VaultCheckpointEntry> Entries }` (keyed by SHA-256 hex of the lowercased absolute path)
  - `VaultDateRange { string From; string To }`, `VaultRootMeta { string Since; List<VaultDateRange> Covered; string LastIngestTs }`
  - `ShardLoadResult { Ok, Missing, Corrupt }`
  - `VaultStore(string vaultDir, string? logFile = null)` with:
    - `string RootDir(string rootName)`
    - `ShardLoadResult TryLoadSessionShard(string rootName, string month, out VaultSessionShard shard)`
    - `void SaveSessionShard(string rootName, string month, VaultSessionShard shard)` (throws `IOException`/`UnauthorizedAccessException` after retries — callers abort the cycle)
    - `ShardLoadResult TryLoadRollupShard(string rootName, string month, out VaultRollupShard shard)`
    - `void SaveRollupShard(string rootName, string month, VaultRollupShard shard)`
    - `VaultCheckpointFile LoadCheckpoints(string rootName)` (corrupt → delete file, return empty)
    - `void SaveCheckpoints(string rootName, VaultCheckpointFile file)`
    - `VaultRootMeta? LoadMeta(string rootName)` / `void SaveMeta(string rootName, VaultRootMeta meta)`
    - `IReadOnlyList<string> ListSessionShardMonths(string rootName)` (sorted ascending, parses `sessions-YYYY-MM.json` names)
    - `void QuarantineSessionShard(string rootName, string month, DateTimeOffset nowUtc)` (rename to `sessions-{month}.json.{yyyyMMddTHHmmss}.bad` + delete `checkpoints.json` — atomically coupled)
    - `void DeleteRollupShard(string rootName, string month)`
    - `void PurgeRoot(string rootName)` / `void PurgeAll()` (recursive best-effort delete, logged on failure)
    - `static string PathKey(string absolutePath)` (SHA-256 hex, lowercase, of `absolutePath.ToLowerInvariant()`, UTF-8)
- File names inside a root dir (exact): `sessions-{yyyy-MM}.json`, `rollups-{yyyy-MM}.json`, `checkpoints.json`, `meta.json`.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/ws-c-usage-vault
```

- [ ] **Step 2: Write the failing tests**

Append to `windows-dotnet/tests/Sanduhr.Tests/PathsTests.cs` (follow the file's existing style — it constructs `new Paths(tempAppData, tempHome)`; the new ctor arg is third):

```csharp
    [Fact]
    public void VaultDir_lives_under_local_appdata_not_roaming()
    {
        using var roaming = new TempDir();
        using var home = new TempDir();
        using var local = new TempDir();
        var paths = new Paths(roaming.Path, home.Path, local.Path);
        Assert.Equal(Path.Combine(local.Path, "Sanduhr", "vault"), paths.VaultDir);
        Assert.StartsWith(local.Path, paths.VaultDir);
        Assert.False(paths.VaultDir.StartsWith(roaming.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void VaultDir_is_not_auto_created()
    {
        using var roaming = new TempDir();
        using var home = new TempDir();
        using var local = new TempDir();
        var paths = new Paths(roaming.Path, home.Path, local.Path);
        _ = paths.VaultDir;
        Assert.False(Directory.Exists(paths.VaultDir));
    }
```

Create `windows-dotnet/tests/Sanduhr.Tests/VaultStoreTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// VaultStore IO contract (spec 2026-07-12-usage-vault-design.md, Storage):
/// atomic writes, per-root dirs, quarantine coupled to checkpoint invalidation,
/// corrupt-tolerant loads, and the hashed checkpoint path key.
/// </summary>
public class VaultStoreTests
{
    private static VaultSessionShard Shard(params (string Uuid, long Total)[] rows)
    {
        var shard = new VaultSessionShard
        {
            SchemaVersion = 1,
            WriterVersion = "test",
            Sessions = new Dictionary<string, VaultSessionRow>(),
        };
        foreach (var (uuid, total) in rows)
        {
            shard.Sessions[uuid] = new VaultSessionRow
            {
                ProjectKey = "api~00000000",
                ProjectName = "api",
                FirstTs = "2026-07-01T00:00:00+00:00",
                LastTs = "2026-07-01T01:00:00+00:00",
                UtcOffsetMin = -300,
                EventCount = 1,
                Total = total,
                ByModel = new Dictionary<string, long> { ["claude-fable-5"] = total },
                ByDay = new Dictionary<string, VaultDayBucket>
                {
                    ["2026-07-01"] = new VaultDayBucket
                    {
                        Total = total,
                        ByModel = new Dictionary<string, long> { ["claude-fable-5"] = total },
                    },
                },
            };
        }
        return shard;
    }

    [Fact]
    public void Session_shard_round_trips_and_lands_in_per_root_dir()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 100)));

        Assert.True(File.Exists(Path.Combine(tmp.Path, ".claude", "sessions-2026-07.json")));
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var loaded));
        Assert.Equal(100, loaded.Sessions["u1"].Total);
        Assert.Equal(1, loaded.SchemaVersion);
    }

    [Fact]
    public void Save_replaces_atomically_leaving_no_tmp_file()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 100)));
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 200)));

        var files = Directory.GetFiles(Path.Combine(tmp.Path, ".claude"));
        Assert.Single(files); // no .tmp residue
        store.TryLoadSessionShard(".claude", "2026-07", out var loaded);
        Assert.Equal(200, loaded.Sessions["u1"].Total);
    }

    [Fact]
    public void Missing_shard_reports_missing_not_corrupt()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-07", out _));
    }

    [Fact]
    public void Corrupt_session_shard_reports_corrupt_and_does_not_mutate()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{not json");

        Assert.Equal(ShardLoadResult.Corrupt, store.TryLoadSessionShard(".claude", "2026-07", out _));
        // Read path never quarantines — the file is untouched.
        Assert.True(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));
    }

    [Fact]
    public void Quarantine_renames_timestamped_and_deletes_checkpoints()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{not json");
        store.SaveCheckpoints(".claude", new VaultCheckpointFile
        {
            SchemaVersion = 1,
            Entries = new Dictionary<string, VaultCheckpointEntry>(),
        });
        Assert.True(File.Exists(Path.Combine(dir, "checkpoints.json")));

        var now = DateTimeOffset.Parse("2026-07-15T03:15:00+00:00");
        store.QuarantineSessionShard(".claude", "2026-07", now);

        Assert.False(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));
        Assert.True(File.Exists(Path.Combine(dir, "sessions-2026-07.json.20260715T031500.bad")));
        Assert.False(File.Exists(Path.Combine(dir, "checkpoints.json")));
    }

    [Fact]
    public void Quarantine_never_overwrites_an_existing_bad_file()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Parse("2026-07-15T03:15:00+00:00");
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json.20260715T031500.bad"), "earlier");
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{not json");

        store.QuarantineSessionShard(".claude", "2026-07", now);

        // Existing .bad kept; new one gets a uniquified name.
        Assert.Equal("earlier", File.ReadAllText(Path.Combine(dir, "sessions-2026-07.json.20260715T031500.bad")));
        Assert.False(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));
        Assert.Equal(2, Directory.GetFiles(dir, "*.bad").Length);
    }

    [Fact]
    public void Corrupt_checkpoints_load_empty_and_delete_the_file()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "checkpoints.json"), "][");

        var cp = store.LoadCheckpoints(".claude");
        Assert.Empty(cp.Entries);
        Assert.False(File.Exists(Path.Combine(dir, "checkpoints.json")));
    }

    [Fact]
    public void ListSessionShardMonths_sorted_and_ignores_bad_and_rollups()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 1)));
        store.SaveSessionShard(".claude", "2026-06", Shard(("u2", 1)));
        store.SaveRollupShard(".claude", "2026-06", new VaultRollupShard
        {
            SchemaVersion = 1,
            Days = new Dictionary<string, VaultRollupDay>(),
        });
        File.WriteAllText(Path.Combine(tmp.Path, ".claude", "sessions-2026-05.json.20260715T031500.bad"), "x");

        Assert.Equal(new[] { "2026-06", "2026-07" }, store.ListSessionShardMonths(".claude"));
    }

    [Fact]
    public void Meta_round_trips_and_missing_is_null()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        Assert.Null(store.LoadMeta(".claude"));
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-12",
            Covered = new List<VaultDateRange> { new() { From = "2026-06-17", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T09:00:00+00:00",
        });
        var meta = store.LoadMeta(".claude");
        Assert.NotNull(meta);
        Assert.Equal("2026-07-12", meta!.Since);
        Assert.Single(meta.Covered);
    }

    [Fact]
    public void PurgeRoot_removes_only_that_root()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 1)));
        store.SaveSessionShard(".claude-personal", "2026-07", Shard(("u2", 1)));

        store.PurgeRoot(".claude");

        Assert.False(Directory.Exists(Path.Combine(tmp.Path, ".claude")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Path, ".claude-personal")));
    }

    [Fact]
    public void PathKey_is_case_insensitive_and_hex()
    {
        var a = VaultStore.PathKey(@"C:\Users\X\.claude\projects\p\u.jsonl");
        var b = VaultStore.PathKey(@"c:\users\x\.claude\projects\p\u.jsonl");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Serialized_shard_uses_snake_case_wire_names()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveSessionShard(".claude", "2026-07", Shard(("u1", 100)));
        var raw = File.ReadAllText(Path.Combine(tmp.Path, ".claude", "sessions-2026-07.json"));
        Assert.Contains("\"schema_version\"", raw);
        Assert.Contains("\"by_model\"", raw);
        Assert.Contains("\"by_day\"", raw);
        Assert.Contains("\"project_key\"", raw);
        Assert.DoesNotContain("\"ProjectKey\"", raw);
        // cwd omitted when null (store_full_paths off) — no path leaks by default.
        Assert.DoesNotContain("\"cwd\"", raw);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultStore|FullyQualifiedName~Paths"`
Expected: FAIL — `VaultStore`, `VaultSessionShard` etc. do not exist; `Paths` has no 3-arg ctor.

- [ ] **Step 4: Implement `Paths` changes**

In `windows-dotnet/src/Sanduhr.Core/Paths.cs`, extend the ctor and add `VaultDir`:

```csharp
    private readonly string _appDataBase;
    private readonly string _homeDir;
    private readonly string _localAppDataBase;
```

Replace the constructor with (keep the existing XML doc, add a `localAppDataBase` param note):

```csharp
    public Paths(string? appDataBase = null, string? homeDir = null, string? localAppDataBase = null)
    {
        _appDataBase = appDataBase
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _homeDir = homeDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localAppDataBase = localAppDataBase
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
```

Add after `LastErrorFile`:

```csharp
    /// <summary>Usage-vault root: <c>%LOCALAPPDATA%\Sanduhr\vault</c>. LOCAL, not
    /// roaming, by design — enterprise roaming profiles must never sync the
    /// work-activity archive off the machine. Not auto-created; the VaultStore
    /// creates per-root dirs lazily on first write.</summary>
    public string VaultDir => Path.Combine(_localAppDataBase, "Sanduhr", "vault");
```

- [ ] **Step 5: Implement `VaultModels.cs`**

Create `windows-dotnet/src/Sanduhr.Core/VaultModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Sanduhr.Core;

/// <summary>
/// Wire schema for the usage vault (spec 2026-07-12-usage-vault-design.md).
/// Session shards are the irreplaceable primary record: raw model strings
/// (never tiers), unconditional totals, per-local-day buckets. Meaning changes
/// take a NEW field name; readers accept every schema_version &lt;= CurrentSchemaVersion
/// forever — the source to re-derive old shards is gone.
/// </summary>
public static class VaultSchema
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>One local calendar day inside a session row. Total is unconditional
/// (every timestamped token-bearing event); by_model keys are raw CC model strings.</summary>
public sealed class VaultDayBucket
{
    [JsonPropertyName("total")] public long Total { get; set; }

    [JsonPropertyName("by_model")]
    public Dictionary<string, long> ByModel { get; set; } = new();

    /// <summary>Per-day skill split — needed so the rollup fold (per-day by_skill)
    /// reads exactly one shard. Omitted when the day had no attributed events.</summary>
    [JsonPropertyName("by_skill")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, long>? BySkill { get; set; }
}

public sealed class VaultCacheTokens
{
    [JsonPropertyName("read")] public long Read { get; set; }
    [JsonPropertyName("creation")] public long Creation { get; set; }
}

/// <summary>
/// One session's aggregates (or one month-slice of a session when
/// <see cref="Continuation"/> is true). Row invariant:
/// <c>Total == sum(ByDay[*].Total)</c> — every row is self-consistent, so the
/// rollup fold for a day reads exactly one month's shard. EventCount,
/// SkippedLines and CacheTokens live on the PRIMARY row only (slices carry 0 /
/// null — they cannot be split by month from day buckets).
/// </summary>
public sealed class VaultSessionRow
{
    [JsonPropertyName("project_key")] public string ProjectKey { get; set; } = "";
    [JsonPropertyName("project_name")] public string ProjectName { get; set; } = "";

    /// <summary>Populated ONLY when the store_full_paths setting (off by default) is on.</summary>
    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    [JsonPropertyName("first_ts")] public string FirstTs { get; set; } = "";
    [JsonPropertyName("last_ts")] public string LastTs { get; set; } = "";
    [JsonPropertyName("utc_offset_min")] public int UtcOffsetMin { get; set; }
    [JsonPropertyName("event_count")] public long EventCount { get; set; }
    [JsonPropertyName("skipped_lines")] public long SkippedLines { get; set; }
    [JsonPropertyName("continuation")] public bool Continuation { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }

    [JsonPropertyName("by_model")]
    public Dictionary<string, long> ByModel { get; set; } = new();

    [JsonPropertyName("cache_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VaultCacheTokens? CacheTokens { get; set; }

    [JsonPropertyName("by_skill")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, long>? BySkill { get; set; }

    [JsonPropertyName("by_day")]
    public Dictionary<string, VaultDayBucket> ByDay { get; set; } = new();
}

public sealed class VaultSessionShard
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = VaultSchema.CurrentSchemaVersion;
    [JsonPropertyName("writer_version")] public string WriterVersion { get; set; } = "";
    [JsonPropertyName("sessions")] public Dictionary<string, VaultSessionRow> Sessions { get; set; } = new();
}

/// <summary>Rollups are a DECLARED DERIVED CACHE — deletable at any time,
/// rebuilt by folding the month's session shard. Never a second truth.</summary>
public sealed class VaultRollupDay
{
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("by_model")] public Dictionary<string, long> ByModel { get; set; } = new();
    [JsonPropertyName("by_project")] public Dictionary<string, long> ByProject { get; set; } = new();
    [JsonPropertyName("by_skill")] public Dictionary<string, long> BySkill { get; set; } = new();
    [JsonPropertyName("sessions")] public int Sessions { get; set; }
}

public sealed class VaultRollupShard
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = VaultSchema.CurrentSchemaVersion;
    [JsonPropertyName("days")] public Dictionary<string, VaultRollupDay> Days { get; set; } = new();
}

/// <summary>Bookkeeping only — the "vault outlives its sources" property belongs
/// to shards, not checkpoints. Keyed by SHA-256 of the lowercased absolute path
/// so this file is never a readable path ledger.</summary>
public sealed class VaultCheckpointEntry
{
    [JsonPropertyName("mtime_ticks")] public long MtimeTicks { get; set; }
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("offset")] public long Offset { get; set; }
    [JsonPropertyName("tail_guard")] public string TailGuard { get; set; } = "";

    /// <summary>Fingerprint of the stored rows this checkpoint corresponds to
    /// (sum of the session's row totals / event counts). A tail parse is only
    /// trusted when the stored rows MATCH this fingerprint — after a crash
    /// between the shard write and the checkpoint write, the rows are newer
    /// than the checkpoint and a seeded tail parse would double-count; the
    /// mismatch forces the idempotent full reparse instead.</summary>
    [JsonPropertyName("row_total")] public long RowTotal { get; set; }
    [JsonPropertyName("row_events")] public long RowEvents { get; set; }
    [JsonPropertyName("row_cache_read")] public long RowCacheRead { get; set; }
    [JsonPropertyName("row_cache_creation")] public long RowCacheCreation { get; set; }

    [JsonPropertyName("months")] public List<string> Months { get; set; } = new();
    [JsonPropertyName("sealed")] public bool Sealed { get; set; }
    [JsonPropertyName("last_seen")] public string LastSeenTs { get; set; } = "";
}

public sealed class VaultCheckpointFile
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = VaultSchema.CurrentSchemaVersion;
    [JsonPropertyName("entries")] public Dictionary<string, VaultCheckpointEntry> Entries { get; set; } = new();
}

public sealed class VaultDateRange
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
}

/// <summary>Per-root vault metadata: birth date (Trends footer), ingest-coverage
/// ranges (the "no record" texture), and the last successful ingest stamp
/// (degraded-mode gate). Lives inside the root's folder so purging the folder
/// purges the bookkeeping with it.</summary>
public sealed class VaultRootMeta
{
    [JsonPropertyName("since")] public string Since { get; set; } = "";
    [JsonPropertyName("covered")] public List<VaultDateRange> Covered { get; set; } = new();
    [JsonPropertyName("last_ingest_ts")] public string LastIngestTs { get; set; } = "";
}

public enum ShardLoadResult
{
    Ok,
    Missing,
    Corrupt,
}
```

- [ ] **Step 6: Implement `VaultStore.cs`**

Create `windows-dotnet/src/Sanduhr.Core/VaultStore.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sanduhr.Core;

/// <summary>
/// Per-root vault file IO (spec: Storage). One directory per CC home under
/// <see cref="Paths.VaultDir"/> — tenant separation is structural: per-root
/// purge is a folder delete, audit handover is a folder handover.
///
/// Writes serialize to a .tmp sibling then atomically replace (2–3 retries).
/// A session-shard save that still fails THROWS — the ingester must abort that
/// root's cycle before rollups/checkpoints (write-ordering invariant).
/// Quarantine (ingest-side only) renames a corrupt session shard to a
/// timestamped .bad — never overwritten, never auto-deleted — and atomically
/// deletes that root's checkpoints so the next cycle's full re-ingest rebuilds
/// everything still on disk.
/// </summary>
public sealed class VaultStore
{
    private const int WriteRetries = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    private readonly string _vaultDir;
    private readonly string? _logFile;

    public VaultStore(string vaultDir, string? logFile = null)
    {
        _vaultDir = vaultDir;
        _logFile = logFile;
    }

    public string RootDir(string rootName) => Path.Combine(_vaultDir, rootName);

    private string SessionShardPath(string rootName, string month)
        => Path.Combine(RootDir(rootName), $"sessions-{month}.json");

    private string RollupShardPath(string rootName, string month)
        => Path.Combine(RootDir(rootName), $"rollups-{month}.json");

    private string CheckpointsPath(string rootName)
        => Path.Combine(RootDir(rootName), "checkpoints.json");

    private string MetaPath(string rootName)
        => Path.Combine(RootDir(rootName), "meta.json");

    /// <summary>SHA-256 hex (lowercase) of the LOWERCASED absolute path — the
    /// checkpoint key. Case-folded because NTFS paths are case-insensitive.</summary>
    public static string PathKey(string absolutePath)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(absolutePath.ToLowerInvariant())))
            .ToLowerInvariant();

    // -- session shards --------------------------------------------------------

    public ShardLoadResult TryLoadSessionShard(string rootName, string month, out VaultSessionShard shard)
        => TryLoad(SessionShardPath(rootName, month), out shard);

    /// <summary>Throws (IOException or UnauthorizedAccessException) when the
    /// atomic replace still fails after retries — the caller aborts the root's
    /// cycle (checkpoints must not advance past a shard that never landed).</summary>
    public void SaveSessionShard(string rootName, string month, VaultSessionShard shard)
        => WriteAtomic(SessionShardPath(rootName, month), JsonSerializer.Serialize(shard, JsonOpts), throwOnFailure: true);

    // -- rollup shards (derived cache) -----------------------------------------

    public ShardLoadResult TryLoadRollupShard(string rootName, string month, out VaultRollupShard shard)
        => TryLoad(RollupShardPath(rootName, month), out shard);

    public void SaveRollupShard(string rootName, string month, VaultRollupShard shard)
        => WriteAtomic(RollupShardPath(rootName, month), JsonSerializer.Serialize(shard, JsonOpts), throwOnFailure: false);

    public void DeleteRollupShard(string rootName, string month)
    {
        try { File.Delete(RollupShardPath(rootName, month)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("rollup-delete", e);
        }
    }

    // -- checkpoints ------------------------------------------------------------

    /// <summary>Corrupt checkpoints are disposable: delete the file, return empty —
    /// the resulting full re-ingest converges (idempotent by design).</summary>
    public VaultCheckpointFile LoadCheckpoints(string rootName)
    {
        var p = CheckpointsPath(rootName);
        if (!File.Exists(p))
            return new VaultCheckpointFile();
        try
        {
            return JsonSerializer.Deserialize<VaultCheckpointFile>(File.ReadAllText(p))
                   ?? new VaultCheckpointFile();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogBestEffort("checkpoints-load", e);
            try { File.Delete(p); }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException)
            {
                LogBestEffort("checkpoints-delete", e2);
            }
            return new VaultCheckpointFile();
        }
    }

    public void SaveCheckpoints(string rootName, VaultCheckpointFile file)
        => WriteAtomic(CheckpointsPath(rootName), JsonSerializer.Serialize(file, JsonOpts), throwOnFailure: false);

    // -- meta --------------------------------------------------------------------

    public VaultRootMeta? LoadMeta(string rootName)
    {
        var p = MetaPath(rootName);
        if (!File.Exists(p))
            return null;
        try
        {
            return JsonSerializer.Deserialize<VaultRootMeta>(File.ReadAllText(p));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogBestEffort("meta-load", e);
            return null;
        }
    }

    public void SaveMeta(string rootName, VaultRootMeta meta)
        => WriteAtomic(MetaPath(rootName), JsonSerializer.Serialize(meta, JsonOpts), throwOnFailure: false);

    // -- discovery / quarantine / purge ------------------------------------------

    /// <summary>Months (yyyy-MM, ascending) that have a session shard on disk.
    /// Ignores rollups, quarantined .bad files, and anything misnamed.</summary>
    public IReadOnlyList<string> ListSessionShardMonths(string rootName)
    {
        var dir = RootDir(rootName);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        var months = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "sessions-*.json"))
        {
            var name = Path.GetFileName(f);
            // Exactly "sessions-YYYY-MM.json" (quarantine adds a suffix after .json).
            if (name.Length == "sessions-YYYY-MM.json".Length
                && name.EndsWith(".json", StringComparison.Ordinal))
            {
                months.Add(name.Substring("sessions-".Length, 7));
            }
        }
        months.Sort(StringComparer.Ordinal);
        return months;
    }

    /// <summary>Ingest-side only (the read path never mutates). Rename to a
    /// timestamped .bad (uniquified, never overwritten — for months older than
    /// JSONL retention the .bad IS the archive) and delete checkpoints.json so
    /// the next cycle re-ingests everything still on disk.</summary>
    public void QuarantineSessionShard(string rootName, string month, DateTimeOffset nowUtc)
    {
        var src = SessionShardPath(rootName, month);
        var stamp = nowUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var dest = $"{src}.{stamp}.bad";
        int n = 1;
        while (File.Exists(dest))
            dest = $"{src}.{stamp}-{n++}.bad";
        try
        {
            File.Move(src, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("quarantine", e);
        }
        try
        {
            File.Delete(CheckpointsPath(rootName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("quarantine-checkpoints", e);
        }
    }

    public void PurgeRoot(string rootName)
    {
        try
        {
            if (Directory.Exists(RootDir(rootName)))
                Directory.Delete(RootDir(rootName), recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("purge-root", e);
        }
    }

    public void PurgeAll()
    {
        try
        {
            if (Directory.Exists(_vaultDir))
                Directory.Delete(_vaultDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogBestEffort("purge-all", e);
        }
    }

    // -- plumbing -----------------------------------------------------------------

    private ShardLoadResult TryLoad<T>(string path, out T value) where T : new()
    {
        value = new T();
        if (!File.Exists(path))
            return ShardLoadResult.Missing;
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            if (parsed is null)
                return ShardLoadResult.Corrupt;
            value = parsed;
            return ShardLoadResult.Ok;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LogBestEffort("shard-load", e);
            return ShardLoadResult.Corrupt;
        }
    }

    private void WriteAtomic(string path, string json, bool throwOnFailure)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        Exception? last = null;
        for (int attempt = 0; attempt < WriteRetries; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                last = e;
                Thread.Sleep(25);
            }
        }
        try { File.Delete(tmp); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LogBestEffort("tmp-cleanup", e); }
        LogBestEffort("write", last!);
        if (throwOnFailure)
            throw last!;
    }

    // PRIVACY.md contract: operation + exception TYPE only. Never e.Message
    // (file-op messages embed paths), never file contents, never root names
    // beyond the fixed ".claude"/".claude-personal" vocabulary — and we skip
    // even those to keep the rule mechanical.
    private void LogBestEffort(string operation, Exception e)
    {
        if (_logFile is null)
            return;
        try
        {
            File.AppendAllText(_logFile,
                $"{DateTime.UtcNow:o} vault {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
            // Swallow — logging must never break vault IO.
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS, 360 + 14 new = 374 (Step 2 added 2 Paths + 12 VaultStore tests).

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/Paths.cs windows-dotnet/src/Sanduhr.Core/VaultModels.cs windows-dotnet/src/Sanduhr.Core/VaultStore.cs windows-dotnet/tests/Sanduhr.Tests/PathsTests.cs windows-dotnet/tests/Sanduhr.Tests/VaultStoreTests.cs
git commit -m "feat(vault): Paths.VaultDir, wire schema types, and VaultStore atomic IO"
```

---

### Task 2: `VaultIngester` — walk, parse, month slicing, rollup fold, mutex (Core, TDD)

The bulk. Full-file ingestion only — the guarded tail-parse, sealing, quarantine and prune land in Task 3 on top of this structure (Task 3's hooks are called out inline below so nothing here needs rework).

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultModels.cs` (append `VaultRowMath`)
- Modify: `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (add static `TierForModel`, delegate `ModelToTierKey`)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultIngesterTests.cs`

**Interfaces:**
- Consumes (Task 1): `VaultStore` (all methods), `VaultSessionShard`/`VaultSessionRow`/`VaultDayBucket`/`VaultRollupShard`/`VaultRollupDay`/`VaultCheckpointFile`/`VaultCheckpointEntry`/`VaultRootMeta`/`VaultDateRange`, `ShardLoadResult`, `VaultStore.PathKey`.
- Produces (Tasks 3–5, 8 rely on these exact names):
  - `sealed record VaultIngestResult(bool Acquired, int FilesSeen, int FilesFullParsed, int FilesTailParsed, int FilesSkipped, int FilesFailed, int RootsAborted)`
  - `sealed class VaultIngester` with ctor `VaultIngester(string homeDir, VaultStore store, string writerVersion, string? logFile = null, TimeZoneInfo? timeZone = null, string? mutexName = null)` (mutexName defaults to `"Global\\Sanduhr.VaultWriter"`; tests inject a unique name) and `VaultIngestResult IngestOnce(IReadOnlyList<string> consentedRootNames, bool storeFullPaths, DateTimeOffset nowUtc)`
  - `static class VaultRowMath` with `VaultSessionRow Merge(IReadOnlyList<VaultSessionRow> rowsOfOneSession)` and `Dictionary<string, VaultSessionRow> SplitByMonth(VaultSessionRow merged)`
  - `CcLogReader.TierForModel(string? model)` — public static, same prefix table; instance `ModelToTierKey` delegates to it.
- Coverage margin constant: each successful cycle merges `[todayLocal-25, todayLocal]` into `meta.Covered` (25 = CC's ~30-day retention minus safety margin — days re-ingestable from still-existing JSONLs count as covered).

**Design invariants the implementation below encodes (from the spec, binding):**
- Stat (`FileInfo.Length` / `LastWriteTimeUtc`) comes from the walk's `FileSystemInfo`, captured BEFORE the file is opened. Parsing past that length is safe; a post-parse stat is not.
- Opens are explicit `FileStream` with `FileShare.ReadWrite | FileShare.Delete`; open/read failure advances no checkpoint and replaces no row, but does touch `LastSeenTs` (a locked file must not get pruned).
- Only complete lines (terminated by `\n`) are consumed; the byte offset after the last consumed `\n` is the checkpoint offset. A torn final line is re-read next cycle.
- `total` counts exactly what `CcLogReader.AggregateForLocalCcTab` counts: `type==assistant`, non-null timestamp, `input_tokens+output_tokens > 0`. Null model buckets under `"<none>"` so `sum(by_model) == total` conserves.
- Line prefilter is `line.Contains("\"assistant\"")` — deliberately LOOSER than the spec's `"type":"assistant"` example so a hypothetical `"type": "assistant"` (space) can never be silently dropped; false positives just get parsed and rejected. `skipped_lines` counts prefilter-passing lines that fail to parse.
- Write order per root: session shards → rollups → checkpoints → meta. A session-shard save failure aborts the root's remaining writes (`RootsAborted`).
- Rollups for every dirty month are rebuilt by a full fold of that month's session shard — the fold is the only rollup writer. The current month is always rebuilt when its shard exists (subsumes the startup self-check).

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/VaultIngesterTests.cs`:

```csharp
using System.Globalization;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// VaultIngester core battery (spec: Ingestion + Testing). A fake CC home is
/// built in a temp dir: {home}\{root}\projects\{projDir}\{uuid}.jsonl. All
/// tests inject a unique mutex name and a FIXED time zone (Central Standard
/// Time) except the reader-parity test, which must share the machine-local
/// zone with CcLogReader.
/// </summary>
public class VaultIngesterTests
{
    private static readonly TimeZoneInfo Cst =
        TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T18:00:00+00:00");

    private static string MutexName() => "SanduhrTest.Vault." + Guid.NewGuid().ToString("N");

    private static string EventLine(
        string ts, string model = "claude-fable-5", long input = 100, long output = 50,
        string? cwd = @"C:\Users\x\Projects\api", string? skill = null,
        long cacheRead = 0, long cacheCreation = 0)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"assistant\",\"timestamp\":\"").Append(ts).Append('"');
        if (cwd is not null)
            sb.Append(",\"cwd\":").Append(System.Text.Json.JsonSerializer.Serialize(cwd));
        if (skill is not null)
            sb.Append(",\"attributionSkill\":\"").Append(skill).Append('"');
        sb.Append(",\"message\":{\"model\":\"").Append(model)
          .Append("\",\"usage\":{\"input_tokens\":").Append(input)
          .Append(",\"output_tokens\":").Append(output);
        if (cacheRead > 0) sb.Append(",\"cache_read_input_tokens\":").Append(cacheRead);
        if (cacheCreation > 0) sb.Append(",\"cache_creation_input_tokens\":").Append(cacheCreation);
        sb.Append("}}}");
        return sb.ToString();
    }

    /// <summary>Writes a fixture session and PINS its mtime relative to the
    /// fixed test clock (default Now-10min) — real-clock mtimes would make the
    /// Task 3 quiesce/seal branch engage nondeterministically.</summary>
    private static string WriteSession(
        string home, string root, string uuid, DateTimeOffset? mtimeUtc = null, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", "c--Users-x-Projects-api");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, uuid + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, (mtimeUtc ?? Now.AddMinutes(-10)).UtcDateTime);
        return path;
    }

    private static string WriteSession(string home, string root, string uuid, params string[] lines)
        => WriteSession(home, root, uuid, mtimeUtc: null, lines);

    private static (VaultIngester Ingester, VaultStore Store) Make(
        string home, string vaultDir, TimeZoneInfo? tz = null, string? logFile = null, string? mutexName = null)
    {
        var store = new VaultStore(vaultDir, logFile);
        var ing = new VaultIngester(home, store, "test", logFile, tz ?? Cst, mutexName ?? MutexName());
        return (ing, store);
    }

    [Fact]
    public void Happy_path_writes_raw_models_cache_and_unconditional_totals()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", model: "claude-fable-5", input: 1000, output: 100,
                skill: "code-review", cacheRead: 5000, cacheCreation: 200),
            EventLine("2026-07-10T16:00:00Z", model: "<synthetic>", input: 0, output: 4),
            EventLine("2026-07-10T17:00:00Z", model: "claude-sonnet-5", input: 300, output: 30));

        var (ing, store) = Make(home.Path, vault.Path);
        var result = ing.IngestOnce(new[] { ".claude" }, storeFullPaths: false, Now);

        Assert.True(result.Acquired);
        Assert.Equal(1, result.FilesFullParsed);
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var shard));
        var row = shard.Sessions["u1"];
        Assert.Equal(1434, row.Total);                       // 1100 + 4 + 330 — every timestamped event
        Assert.Equal(1100, row.ByModel["claude-fable-5"]);   // raw strings, never tiers
        Assert.Equal(4, row.ByModel["<synthetic>"]);
        Assert.Equal(330, row.ByModel["claude-sonnet-5"]);
        Assert.Equal(row.Total, row.ByModel.Values.Sum());
        Assert.Equal(5000, row.CacheTokens!.Read);
        Assert.Equal(200, row.CacheTokens.Creation);
        Assert.Equal(1100, row.BySkill!["code-review"]);
        Assert.Equal(3, row.EventCount);
        Assert.False(row.Continuation);
        Assert.Equal("api", row.ProjectName);
        Assert.Matches("^api~[0-9a-f]{8}$", row.ProjectKey);
        Assert.Null(row.Cwd);                                // store_full_paths off
        Assert.Equal(row.Total, row.ByDay.Values.Sum(d => d.Total));  // row invariant
        Assert.Equal("2026-07-10", row.ByDay.Keys.Single()); // 15:00Z = 10:00 CDT
        Assert.Equal("test", shard.WriterVersion);
    }

    [Fact]
    public void Store_full_paths_on_persists_cwd()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, storeFullPaths: true, Now);
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(@"C:\Users\x\Projects\api", shard.Sessions["u1"].Cwd);
    }

    [Fact]
    public void Reingest_with_same_now_is_byte_identical()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z"), EventLine("2026-07-10T16:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path);

        ing.IngestOnce(new[] { ".claude" }, false, Now);
        var snapshot = Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p, File.ReadAllBytes);

        ing.IngestOnce(new[] { ".claude" }, false, Now);
        var after = Directory.GetFiles(vault.Path, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p, File.ReadAllBytes);

        Assert.Equal(snapshot.Keys, after.Keys);
        foreach (var (path, bytes) in snapshot)
            Assert.Equal(bytes, after[path]);
    }

    [Fact]
    public void Vault_day_total_matches_live_reader_even_for_unmapped_models()
    {
        // THE fable-5 invariant. Shares the machine-local zone with CcLogReader.
        using var home = new TempDir();
        using var vault = new TempDir();
        // Yesterday, REAL clock — this test intentionally shares the machine
        // clock and zone with CcLogReader, so the file mtime must track the
        // real clock too (the reader prefilters on mtime >= UtcNow-30d; the
        // fixed test-clock default would rot into a time-bomb failure ~30
        // days after authoring).
        var ts = DateTimeOffset.UtcNow.AddDays(-1);
        var iso = ts.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        WriteSession(home.Path, ".claude", "u1",
            mtimeUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            EventLine(iso, model: "claude-fable-5", input: 700, output: 70),
            EventLine(iso, model: "claude-mystery-99", input: 20, output: 2));

        var (ing, store) = Make(home.Path, vault.Path, tz: TimeZoneInfo.Local);
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
        Assert.Equal(792, vaultDayTotal);
    }

    [Fact]
    public void Month_spanning_session_slices_with_continuation_rows()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-31T20:00:00Z", input: 100, output: 0),   // Jul 31 15:00 CDT
            EventLine("2026-08-01T20:00:00Z", input: 200, output: 0));  // Aug 1 15:00 CDT
        var (ing, store) = Make(home.Path, vault.Path, tz: Cst);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var jul);
        store.TryLoadSessionShard(".claude", "2026-08", out var aug);
        var primary = jul.Sessions["u1"];
        var slice = aug.Sessions["u1"];

        Assert.False(primary.Continuation);
        Assert.True(slice.Continuation);
        Assert.Equal(100, primary.Total);                    // row total == own buckets only
        Assert.Equal(200, slice.Total);
        Assert.Equal(new[] { "2026-07-31" }, primary.ByDay.Keys.ToArray());
        Assert.Equal(new[] { "2026-08-01" }, slice.ByDay.Keys.ToArray());
        Assert.NotNull(primary.CacheTokens);                 // primary carries cache + counters
        Assert.Null(slice.CacheTokens);                      // slices don't (not splittable by month)
        Assert.Equal(0, slice.EventCount);
        Assert.Equal(primary.FirstTs, slice.FirstTs);        // session identity duplicated for standalone reads
        Assert.Equal(primary.ProjectKey, slice.ProjectKey);
    }

    [Fact]
    public void Utc_month_boundary_places_by_local_time()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        // 2026-08-01T02:00Z = 2026-07-31 21:00 CDT — belongs to July.
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-08-01T02:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path, tz: Cst);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(".claude", "2026-07", out var jul));
        Assert.True(jul.Sessions.ContainsKey("u1"));
        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-08", out _));
    }

    [Fact]
    public void Dst_fall_back_25_hour_day_buckets_by_local_date()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        // US DST ends 2026-11-01. 06:30Z = 01:30 CDT; 23:30 CST is 2026-11-02T05:30Z.
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-11-01T06:30:00Z", input: 10, output: 0),
            EventLine("2026-11-02T05:30:00Z", input: 20, output: 0));
        var (ing, store) = Make(home.Path, vault.Path, tz: Cst);
        ing.IngestOnce(new[] { ".claude" }, false, DateTimeOffset.Parse("2026-11-03T00:00:00+00:00"));

        store.TryLoadSessionShard(".claude", "2026-11", out var shard);
        var row = shard.Sessions["u1"];
        Assert.Equal(30, row.ByDay["2026-11-01"].Total);     // both land on the 25-hour local day
        Assert.False(row.ByDay.ContainsKey("2026-11-02"));
    }

    [Fact]
    public void Second_writer_holding_the_mutex_skips_cleanly()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var name = MutexName();

        // Named mutexes are REENTRANT per thread — holding it on the test
        // thread would let the ingester's WaitOne(0) succeed recursively and
        // the test would fail against a correct implementation. Hold it on a
        // dedicated thread instead.
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holderThread = new Thread(() =>
        {
            using var holder = new Mutex(initiallyOwned: true, name);
            acquired.Set();
            release.Wait();
            holder.ReleaseMutex();
        });
        holderThread.Start();
        acquired.Wait();
        try
        {
            var (ing, store) = Make(home.Path, vault.Path, mutexName: name);
            var result = ing.IngestOnce(new[] { ".claude" }, false, Now);
            Assert.False(result.Acquired);
            Assert.False(Directory.Exists(Path.Combine(vault.Path, ".claude")));
        }
        finally
        {
            release.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public void Consent_filter_never_touches_unconsented_roots()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        WriteSession(home.Path, ".claude-personal", "u2", EventLine("2026-07-10T15:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude-personal" }, false, Now);

        Assert.False(Directory.Exists(Path.Combine(vault.Path, ".claude")));
        Assert.True(File.Exists(Path.Combine(vault.Path, ".claude-personal", "sessions-2026-07.json")));
    }

    [Fact]
    public void Zero_assistant_event_session_writes_no_row_but_checkpoints()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", "{\"type\":\"user\",\"text\":\"hi\"}");
        var (ing, store) = Make(home.Path, vault.Path);
        var result = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-07", out _));
        Assert.Single(store.LoadCheckpoints(".claude").Entries);
        Assert.Equal(1, result.FilesFullParsed);
    }

    [Fact]
    public void Malformed_assistant_lines_counted_never_fatal()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z"),
            "{\"type\":\"assistant\", TRUNCATED GARBAGE",
            EventLine("2026-07-10T16:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        var row = shard.Sessions["u1"];
        Assert.Equal(2, row.EventCount);
        Assert.Equal(1, row.SkippedLines);
        Assert.Equal(300, row.Total);
    }

    [Fact]
    public void Rollups_fold_per_day_project_skill_and_sessions_count()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1",
            EventLine("2026-07-10T15:00:00Z", input: 100, output: 0, skill: "code-review"),
            EventLine("2026-07-11T15:00:00Z", input: 200, output: 0));
        WriteSession(home.Path, ".claude", "u2",
            EventLine("2026-07-10T16:00:00Z", input: 40, output: 0));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(ShardLoadResult.Ok, store.TryLoadRollupShard(".claude", "2026-07", out var roll));
        var d10 = roll.Days["2026-07-10"];
        Assert.Equal(140, d10.Total);
        Assert.Equal(2, d10.Sessions);
        Assert.Equal(100, d10.BySkill["code-review"]);
        Assert.Equal(140, d10.ByProject.Values.Sum());
        Assert.Single(d10.ByProject);                        // both sessions share one project_key
        var d11 = roll.Days["2026-07-11"];
        Assert.Equal(200, d11.Total);
        Assert.Equal(1, d11.Sessions);
    }

    [Fact]
    public void Crash_between_shard_and_checkpoint_converges_without_double_count()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Simulate: file grows, next cycle writes the shard but CRASHES before
        // the checkpoint lands — restore the stale checkpoint file afterwards.
        var cpPath = Path.Combine(vault.Path, ".claude", "checkpoints.json");
        var staleCheckpoint = File.ReadAllBytes(cpPath);
        File.AppendAllText(path, EventLine("2026-07-10T16:00:00Z") + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);   // pinned, not quiesced
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        File.WriteAllBytes(cpPath, staleCheckpoint);         // the "crash"

        ing.IngestOnce(new[] { ".claude" }, false, Now);     // recovery cycle

        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(300, shard.Sessions["u1"].Total);       // 2 events, no double count
        Assert.Equal(2, shard.Sessions["u1"].EventCount);
        store.TryLoadRollupShard(".claude", "2026-07", out var roll);
        Assert.Equal(300, roll.Days["2026-07-10"].Total);    // fold==rollups after recovery
    }

    [Fact]
    public void Meta_records_since_coverage_and_last_ingest()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        var meta = store.LoadMeta(".claude");
        Assert.NotNull(meta);
        Assert.Equal("2026-07-12", meta!.Since);             // local date of Now (13:00 CDT)
        var range = meta.Covered.Single();
        Assert.Equal("2026-06-17", range.From);              // today-25
        Assert.Equal("2026-07-12", range.To);
        Assert.Equal(
            Now.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture),
            meta.LastIngestTs);

        // A later cycle extends the range instead of stacking a duplicate.
        var later = Now.AddDays(3);
        ing.IngestOnce(new[] { ".claude" }, false, later);
        meta = store.LoadMeta(".claude");
        Assert.Equal("2026-07-12", meta!.Since);             // birth date never moves
        Assert.Equal("2026-07-15", meta.Covered.Single().To);
        Assert.Equal("2026-06-17", meta.Covered.Single().From);
    }

    [Fact]
    public void RowMath_split_then_merge_round_trips()
    {
        var merged = new VaultSessionRow
        {
            ProjectKey = "api~12345678",
            ProjectName = "api",
            FirstTs = "2026-07-31T20:00:00+00:00",
            LastTs = "2026-08-02T10:00:00+00:00",
            UtcOffsetMin = -300,
            EventCount = 3,
            SkippedLines = 1,
            Total = 600,
            ByModel = new() { ["claude-fable-5"] = 600 },
            CacheTokens = new VaultCacheTokens { Read = 42, Creation = 7 },
            BySkill = new() { ["code-review"] = 100 },
            ByDay = new()
            {
                ["2026-07-31"] = new VaultDayBucket
                {
                    Total = 100,
                    ByModel = new() { ["claude-fable-5"] = 100 },
                    BySkill = new() { ["code-review"] = 100 },
                },
                ["2026-08-01"] = new VaultDayBucket { Total = 200, ByModel = new() { ["claude-fable-5"] = 200 } },
                ["2026-08-02"] = new VaultDayBucket { Total = 300, ByModel = new() { ["claude-fable-5"] = 300 } },
            },
        };

        var byMonth = VaultRowMath.SplitByMonth(merged);
        Assert.Equal(2, byMonth.Count);
        Assert.False(byMonth["2026-07"].Continuation);
        Assert.True(byMonth["2026-08"].Continuation);
        Assert.Equal(100, byMonth["2026-07"].Total);
        Assert.Equal(500, byMonth["2026-08"].Total);
        Assert.Null(byMonth["2026-08"].CacheTokens);

        var roundTrip = VaultRowMath.Merge(byMonth.Values.ToList());
        Assert.Equal(600, roundTrip.Total);
        Assert.Equal(3, roundTrip.EventCount);
        Assert.Equal(42, roundTrip.CacheTokens!.Read);
        Assert.Equal(600, roundTrip.ByModel["claude-fable-5"]);
        Assert.Equal(100, roundTrip.BySkill!["code-review"]);
        Assert.Equal(3, roundTrip.ByDay.Count);
        Assert.Equal("2026-07-31T20:00:00+00:00", roundTrip.FirstTs);
    }

    [Fact]
    public void TierForModel_is_static_and_matches_instance_mapping()
    {
        Assert.Equal("seven_day_opus", CcLogReader.TierForModel("claude-opus-4-8"));
        Assert.Equal("seven_day_sonnet", CcLogReader.TierForModel("claude-sonnet-5"));
        Assert.Equal("seven_day", CcLogReader.TierForModel("claude-haiku-4-5-20251001"));
        Assert.Null(CcLogReader.TierForModel("claude-fable-5"));
        Assert.Null(CcLogReader.TierForModel("<synthetic>"));
        Assert.Null(CcLogReader.TierForModel(null));
        Assert.Equal(CcLogReader.TierForModel("claude-sonnet-5"), new CcLogReader().ModelToTierKey("claude-sonnet-5"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultIngester"`
Expected: FAIL — `VaultIngester`, `VaultRowMath`, `CcLogReader.TierForModel` do not exist.

- [ ] **Step 3: Add `TierForModel` to `CcLogReader`**

In `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs`, replace the `ModelToTierKey` method body with a delegation and add the static:

```csharp
    /// <summary>Static tier projection over a raw CC model string — the vault's
    /// READ-TIME tier mapping (raw strings are stored; a mapping fix here
    /// retroactively heals all history). Null for unrecognized models —
    /// callers must keep unmapped tokens visible, never drop them.</summary>
    public static string? TierForModel(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return null;
        foreach (var (prefix, tier) in ModelTierPrefixes)
        {
            if (model.StartsWith(prefix, StringComparison.Ordinal))
                return tier;
        }
        return null;
    }

    /// <summary>Map a CC <c>message.model</c> string to a Sanduhr tier key, or
    /// null for unrecognized models.</summary>
    public string? ModelToTierKey(string? model) => TierForModel(model);
```

- [ ] **Step 4: Append `VaultRowMath` to `VaultModels.cs`**

```csharp
/// <summary>
/// Row algebra shared by the ingester (split a parsed session into per-month
/// rows) and every reader (merge a session's primary + continuation slices
/// back into one logical session). Merge(SplitByMonth(x)) == x.
/// </summary>
public static class VaultRowMath
{
    /// <summary>Split a merged session row into per-month rows keyed yyyy-MM.
    /// The primary month is the month of the earliest by_day key (== the local
    /// date of first_ts, since the first counted event defines both). Each row's
    /// Total/ByModel/BySkill are recomputed from ITS OWN day buckets (row
    /// invariant); EventCount/SkippedLines/CacheTokens ride the primary only.</summary>
    public static Dictionary<string, VaultSessionRow> SplitByMonth(VaultSessionRow merged)
    {
        var result = new Dictionary<string, VaultSessionRow>();
        if (merged.ByDay.Count == 0)
            return result;
        var primaryMonth = merged.ByDay.Keys.Min(StringComparer.Ordinal)![..7];

        foreach (var group in merged.ByDay.GroupBy(kv => kv.Key[..7]))
        {
            bool primary = group.Key == primaryMonth;
            var row = new VaultSessionRow
            {
                ProjectKey = merged.ProjectKey,
                ProjectName = merged.ProjectName,
                Cwd = merged.Cwd,
                FirstTs = merged.FirstTs,
                LastTs = merged.LastTs,
                UtcOffsetMin = merged.UtcOffsetMin,
                EventCount = primary ? merged.EventCount : 0,
                SkippedLines = primary ? merged.SkippedLines : 0,
                Continuation = !primary,
                CacheTokens = primary ? merged.CacheTokens : null,
                ByDay = group.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
            RecomputeRowAggregates(row);
            result[group.Key] = row;
        }
        return result;
    }

    /// <summary>Merge one session's rows (primary + slices, any order) into a
    /// single logical row. Continuation is false on the result.</summary>
    public static VaultSessionRow Merge(IReadOnlyList<VaultSessionRow> rows)
    {
        var primary = rows.FirstOrDefault(r => !r.Continuation) ?? rows[0];
        var merged = new VaultSessionRow
        {
            ProjectKey = primary.ProjectKey,
            ProjectName = primary.ProjectName,
            Cwd = primary.Cwd,
            FirstTs = primary.FirstTs,
            LastTs = primary.LastTs,
            UtcOffsetMin = primary.UtcOffsetMin,
            EventCount = rows.Sum(r => r.EventCount),
            SkippedLines = rows.Sum(r => r.SkippedLines),
            Continuation = false,
            CacheTokens = rows.Select(r => r.CacheTokens).FirstOrDefault(c => c is not null),
            ByDay = new Dictionary<string, VaultDayBucket>(),
        };
        foreach (var row in rows)
            foreach (var (day, bucket) in row.ByDay)
                merged.ByDay[day] = bucket;   // day-in-own-month invariant: no key collides
        RecomputeRowAggregates(merged);
        return merged;
    }

    /// <summary>Total/ByModel/BySkill := sums of the row's own day buckets.</summary>
    public static void RecomputeRowAggregates(VaultSessionRow row)
    {
        long total = 0;
        var byModel = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();
        foreach (var bucket in row.ByDay.Values)
        {
            total += bucket.Total;
            foreach (var (m, v) in bucket.ByModel)
                byModel[m] = byModel.GetValueOrDefault(m) + v;
            if (bucket.BySkill is not null)
                foreach (var (s, v) in bucket.BySkill)
                    bySkill[s] = bySkill.GetValueOrDefault(s) + v;
        }
        row.Total = total;
        row.ByModel = byModel;
        row.BySkill = bySkill.Count > 0 ? bySkill : null;
    }
}
```

- [ ] **Step 5: Implement `VaultIngester.cs`**

Create `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Counts for one ingest cycle. Acquired=false means another Sanduhr
/// process held the writer mutex and this cycle skipped entirely.</summary>
public sealed record VaultIngestResult(
    bool Acquired,
    int FilesSeen,
    int FilesFullParsed,
    int FilesTailParsed,
    int FilesSkipped,
    int FilesFailed,
    int RootsAborted);

/// <summary>
/// The vault's only writer (spec: Ingestion). Walks each CONSENTED CC root's
/// projects tree, parses session JSONLs into per-month session shards, folds
/// rollups, and advances checkpoints — in that order, checkpoints LAST, so a
/// crash at any point leaves a stale checkpoint and the next cycle converges.
///
/// Single-threaded and synchronous by design (callers Task.Run it); cross-
/// process exclusion via the named mutex; per-file stat captured from the
/// walk's FileInfo BEFORE any open. This class never uses
/// CcLogReader.IterUsageEvents — its silent empty-on-failure would advance a
/// checkpoint past unread data.
/// </summary>
public sealed class VaultIngester
{
    private const int CoverageMarginDays = 25;      // CC retention (~30d) minus safety
    private const int CheckpointPruneDays = 7;
    internal const int TailGuardBytes = 64;

    private readonly string _homeDir;
    private readonly VaultStore _store;
    private readonly string _writerVersion;
    private readonly string? _logFile;
    private readonly TimeZoneInfo _tz;
    private readonly string _mutexName;

    public VaultIngester(
        string homeDir,
        VaultStore store,
        string writerVersion,
        string? logFile = null,
        TimeZoneInfo? timeZone = null,
        string? mutexName = null)
    {
        _homeDir = homeDir;
        _store = store;
        _writerVersion = writerVersion;
        _logFile = logFile;
        _tz = timeZone ?? TimeZoneInfo.Local;
        _mutexName = mutexName ?? "Global\\Sanduhr.VaultWriter";
    }

    public VaultIngestResult IngestOnce(
        IReadOnlyList<string> consentedRootNames, bool storeFullPaths, DateTimeOffset nowUtc)
    {
        using var mutex = new Mutex(initiallyOwned: false, _mutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;   // previous holder died; state converges by design
        }
        if (!acquired)
        {
            Log("ingest skipped (writer mutex held)");
            return new VaultIngestResult(false, 0, 0, 0, 0, 0, 0);
        }
        try
        {
            int seen = 0, full = 0, tail = 0, skipped = 0, failed = 0, aborted = 0;
            foreach (var rootName in consentedRootNames)
            {
                var r = IngestRoot(rootName, storeFullPaths, nowUtc);
                seen += r.FilesSeen; full += r.FilesFullParsed; tail += r.FilesTailParsed;
                skipped += r.FilesSkipped; failed += r.FilesFailed; aborted += r.RootsAborted;
            }
            return new VaultIngestResult(true, seen, full, tail, skipped, failed, aborted);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private VaultIngestResult IngestRoot(string rootName, bool storeFullPaths, DateTimeOffset nowUtc)
    {
        int seen = 0, full = 0, tailParsed = 0, skipped = 0, failed = 0;

        var checkpoints = _store.LoadCheckpoints(rootName);
        var shardCache = new Dictionary<string, VaultSessionShard>(StringComparer.Ordinal);
        var dirtyMonths = new HashSet<string>(StringComparer.Ordinal);
        var nowIso = IsoUtc(nowUtc);

        // Oldest-first, single-threaded; Length/LastWriteTimeUtc come off the
        // FileInfo the walk already produced (no per-file stat pass) and are
        // the PRE-OPEN stat the checkpoint will record.
        var files = new List<FileInfo>();
        var projects = new DirectoryInfo(Path.Combine(_homeDir, rootName, "projects"));
        if (projects.Exists)
        {
            foreach (var projectDir in projects.EnumerateDirectories())
                files.AddRange(projectDir.EnumerateFiles("*.jsonl"));
        }
        files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

        foreach (var fi in files)
        {
            seen++;
            var key = VaultStore.PathKey(fi.FullName);
            long statLen = fi.Length;
            long statMtime = fi.LastWriteTimeUtc.Ticks;
            checkpoints.Entries.TryGetValue(key, out var cp);

            if (cp is not null && cp.MtimeTicks == statMtime && cp.Length == statLen)
            {
                // Task 3 adds the quiesce-then-seal verify pass here.
                cp.LastSeenTs = nowIso;
                skipped++;
                continue;
            }

            // Task 3 adds the guarded tail-parse branch here (grown + guard match).
            var uuid = Path.GetFileNameWithoutExtension(fi.Name);
            SessionAgg agg;
            long endOffset;
            string tailGuard;
            try
            {
                using var fs = new FileStream(
                    fi.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                agg = new SessionAgg();
                endOffset = ParseLines(fs, 0, agg);
                tailGuard = GuardAt(fs, endOffset);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Read failed: advance NO checkpoint, replace NO row — but touch
                // LastSeen so a merely-locked file never gets pruned.
                if (cp is not null)
                    cp.LastSeenTs = nowIso;
                Log2("file-open", e);
                failed++;
                continue;
            }

            var newRows = BuildRows(uuid, agg, storeFullPaths);
            var oldMonths = cp?.Months ?? new List<string>();
            var touched = new HashSet<string>(oldMonths, StringComparer.Ordinal);
            touched.UnionWith(newRows.Keys);

            bool corrupt = false;
            foreach (var month in touched)
            {
                var shard = GetShard(shardCache, rootName, month, nowUtc, ref corrupt);
                if (corrupt)
                    break;
                shard.Sessions.Remove(uuid);
                if (newRows.TryGetValue(month, out var row))
                    shard.Sessions[uuid] = row;
                dirtyMonths.Add(month);
            }
            if (corrupt)
            {
                // Quarantine already invalidated this root's checkpoints — abort
                // the root's cycle; the next cycle full-re-ingests and converges.
                return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);
            }

            checkpoints.Entries[key] = new VaultCheckpointEntry
            {
                MtimeTicks = statMtime,
                Length = statLen,
                Offset = endOffset,
                TailGuard = tailGuard,
                RowTotal = newRows.Values.Sum(r => r.Total),
                RowEvents = newRows.Values.Sum(r => r.EventCount),
                RowCacheRead = newRows.Values.Sum(r => r.CacheTokens?.Read ?? 0),
                RowCacheCreation = newRows.Values.Sum(r => r.CacheTokens?.Creation ?? 0),
                Months = newRows.Keys.OrderBy(m => m, StringComparer.Ordinal).ToList(),
                Sealed = false,
                LastSeenTs = nowIso,
            };
            full++;
        }

        // Prune bookkeeping for files missing from the walk for > 7 days —
        // bounds checkpoints.json at ~files-on-disk; a resurrected file simply
        // re-ingests (idempotent).
        var pruneBefore = nowUtc.AddDays(-CheckpointPruneDays);
        foreach (var (k, entry) in checkpoints.Entries.ToList())
        {
            // An unparseable LastSeenTs counts as stale — otherwise a malformed
            // stamp becomes immortal bookkeeping and defeats the size bound.
            if (ParseIso(entry.LastSeenTs) is not { } lastSeen || lastSeen < pruneBefore)
                checkpoints.Entries.Remove(k);
        }

        // WRITE ORDER (binding): session shards -> rollups -> checkpoints -> meta.
        try
        {
            foreach (var month in dirtyMonths.OrderBy(m => m, StringComparer.Ordinal))
                _store.SaveSessionShard(rootName, month, shardCache[month]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Shard never landed: abort before rollups/checkpoints. Stale
            // checkpoints re-ingest next cycle; checkpoint-first would make
            // this crash permanent.
            Log2("shard-write", e);
            return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);
        }

        var rollupMonths = new HashSet<string>(dirtyMonths, StringComparer.Ordinal);
        var currentMonth = LocalDate(nowUtc).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (_store.ListSessionShardMonths(rootName).Contains(currentMonth))
            rollupMonths.Add(currentMonth);   // always-rebuild subsumes the startup self-check
        foreach (var month in rollupMonths)
            RebuildRollups(rootName, month, shardCache, nowUtc);

        _store.SaveCheckpoints(rootName, checkpoints);

        UpdateMeta(rootName, nowUtc, nowIso);
        return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 0);
    }

    private VaultSessionShard GetShard(
        Dictionary<string, VaultSessionShard> cache, string rootName, string month,
        DateTimeOffset nowUtc, ref bool corrupt)
    {
        if (cache.TryGetValue(month, out var cached))
            return cached;
        var result = _store.TryLoadSessionShard(rootName, month, out var shard);
        if (result == ShardLoadResult.Corrupt)
        {
            _store.QuarantineSessionShard(rootName, month, nowUtc);
            Log("session shard quarantined; checkpoints invalidated");
            corrupt = true;
            return shard;
        }
        shard.SchemaVersion = VaultSchema.CurrentSchemaVersion;
        shard.WriterVersion = _writerVersion;
        cache[month] = shard;
        return shard;
    }

    private void RebuildRollups(
        string rootName, string month, Dictionary<string, VaultSessionShard> shardCache, DateTimeOffset nowUtc)
    {
        VaultSessionShard shard;
        if (shardCache.TryGetValue(month, out var cached))
        {
            shard = cached;
        }
        else if (_store.TryLoadSessionShard(rootName, month, out var loaded) != ShardLoadResult.Ok)
        {
            _store.DeleteRollupShard(rootName, month);
            return;
        }
        else
        {
            shard = loaded;
        }

        var days = new Dictionary<string, VaultRollupDay>(StringComparer.Ordinal);
        foreach (var row in shard.Sessions.Values)
        {
            foreach (var (day, bucket) in row.ByDay)
            {
                if (!days.TryGetValue(day, out var d))
                    days[day] = d = new VaultRollupDay();
                d.Total += bucket.Total;
                d.Sessions++;
                foreach (var (m, v) in bucket.ByModel)
                    d.ByModel[m] = d.ByModel.GetValueOrDefault(m) + v;
                d.ByProject[row.ProjectKey] = d.ByProject.GetValueOrDefault(row.ProjectKey) + bucket.Total;
                if (bucket.BySkill is not null)
                    foreach (var (s, v) in bucket.BySkill)
                        d.BySkill[s] = d.BySkill.GetValueOrDefault(s) + v;
            }
        }
        _store.SaveRollupShard(rootName, month, new VaultRollupShard
        {
            SchemaVersion = VaultSchema.CurrentSchemaVersion,
            Days = days,
        });
    }

    private void UpdateMeta(string rootName, DateTimeOffset nowUtc, string nowIso)
    {
        var meta = _store.LoadMeta(rootName) ?? new VaultRootMeta();
        var today = LocalDate(nowUtc);
        if (string.IsNullOrEmpty(meta.Since))
            meta.Since = DayKey(today);
        MergeCoverage(meta.Covered, today.AddDays(-CoverageMarginDays), today);
        meta.LastIngestTs = nowIso;
        _store.SaveMeta(rootName, meta);
    }

    /// <summary>Merge [from, to] into the sorted disjoint range list.</summary>
    internal static void MergeCoverage(List<VaultDateRange> ranges, DateOnly from, DateOnly to)
    {
        var parsed = ranges
            .Select(r => (From: DateOnly.Parse(r.From, CultureInfo.InvariantCulture),
                          To: DateOnly.Parse(r.To, CultureInfo.InvariantCulture)))
            .Append((From: from, To: to))
            .OrderBy(r => r.From)
            .ToList();
        var merged = new List<(DateOnly From, DateOnly To)>();
        foreach (var r in parsed)
        {
            if (merged.Count > 0 && r.From <= merged[^1].To.AddDays(1))
                merged[^1] = (merged[^1].From, r.To > merged[^1].To ? r.To : merged[^1].To);
            else
                merged.Add(r);
        }
        ranges.Clear();
        ranges.AddRange(merged.Select(r => new VaultDateRange { From = DayKey(r.From), To = DayKey(r.To) }));
    }

    // -- parsing ------------------------------------------------------------------

    private sealed class DayAgg
    {
        public long Total;
        public readonly Dictionary<string, long> ByModel = new();
        public readonly Dictionary<string, long> BySkill = new();
    }

    private sealed class SessionAgg
    {
        public DateTimeOffset? FirstTs;
        public DateTimeOffset? LastTs;
        public long EventCount;
        public long SkippedLines;
        public string? Cwd;
        public long CacheRead;
        public long CacheCreation;
        public readonly Dictionary<DateOnly, DayAgg> ByDay = new();
    }

    /// <summary>Parse complete lines from <paramref name="start"/>; returns the
    /// byte offset AFTER the last consumed '\n'. A torn trailing line (no
    /// terminator) is NOT consumed — the next cycle re-reads it, so a line that
    /// completes after this checkpoint converges instead of being lost.</summary>
    private long ParseLines(FileStream fs, long start, SessionAgg agg)
    {
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[64 * 1024];
        using var pending = new MemoryStream();
        long pos = start;       // absolute offset of buf[0]
        long consumed = start;  // absolute offset after the last '\n' processed
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
            int lineStart = 0;
            for (int i = 0; i < read; i++)
            {
                if (buf[i] != (byte)'\n')
                    continue;
                string line;
                if (pending.Length > 0)
                {
                    pending.Write(buf, lineStart, i - lineStart);
                    line = Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length);
                    pending.SetLength(0);
                }
                else
                {
                    line = Encoding.UTF8.GetString(buf, lineStart, i - lineStart);
                }
                ProcessLine(line, agg);
                consumed = pos + i + 1;
                lineStart = i + 1;
            }
            if (lineStart < read)
                pending.Write(buf, lineStart, read - lineStart);
            pos += read;
        }
        return consumed;
    }

    private void ProcessLine(string rawLine, SessionAgg agg)
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
            return;
        // Perf prefilter only — looser than "type":"assistant" on purpose so
        // formatting drift can never silently drop events. False positives are
        // parsed and rejected below.
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal))
            return;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            agg.SkippedLines++;   // counts assistant-looking lines that fail to parse
            return;
        }
        if (node is not JsonObject d)
        {
            agg.SkippedLines++;
            return;
        }
        if (Str(d, "type") != "assistant")
            return;
        if (d["message"] is not JsonObject msg || msg["usage"] is not JsonObject usage)
            return;
        var ts = ParseIso(Str(d, "timestamp"));
        if (ts is null)
            return;   // matches the live reader: untimestamped events don't count

        agg.CacheRead += Num(usage, "cache_read_input_tokens");
        agg.CacheCreation += Num(usage, "cache_creation_input_tokens");

        long tokens = Num(usage, "input_tokens") + Num(usage, "output_tokens");
        if (tokens <= 0)
            return;   // matches AggregateForLocalCcTab's tokens<=0 skip

        agg.Cwd ??= Str(d, "cwd");
        var model = Str(msg, "model");
        model = string.IsNullOrEmpty(model) ? "<none>" : model;   // conservation: sum(by_model) == total
        var skill = Str(d, "attributionSkill");

        if (agg.FirstTs is null || ts < agg.FirstTs) agg.FirstTs = ts;
        if (agg.LastTs is null || ts > agg.LastTs) agg.LastTs = ts;
        agg.EventCount++;

        var day = LocalDate(ts.Value);
        if (!agg.ByDay.TryGetValue(day, out var bucket))
            agg.ByDay[day] = bucket = new DayAgg();
        bucket.Total += tokens;
        bucket.ByModel[model] = bucket.ByModel.GetValueOrDefault(model) + tokens;
        if (!string.IsNullOrEmpty(skill))
            bucket.BySkill[skill] = bucket.BySkill.GetValueOrDefault(skill) + tokens;
    }

    /// <summary>Per-month rows for a parsed session; empty when it had no
    /// counted events (zero-assistant-event sessions get no row).</summary>
    private Dictionary<string, VaultSessionRow> BuildRows(string uuid, SessionAgg agg, bool storeFullPaths)
    {
        _ = uuid;
        if (agg.EventCount == 0 || agg.FirstTs is null || agg.LastTs is null)
            return new Dictionary<string, VaultSessionRow>();

        var projectKey = agg.Cwd is null
            ? "(none)"
            : $"{CcLogReader.ProjectDisplayName(agg.Cwd)}~{VaultStore.PathKey(agg.Cwd)[..8]}";
        var merged = new VaultSessionRow
        {
            ProjectKey = projectKey,
            ProjectName = agg.Cwd is null ? "(none)" : CcLogReader.ProjectDisplayName(agg.Cwd),
            Cwd = storeFullPaths ? agg.Cwd : null,
            FirstTs = IsoUtc(agg.FirstTs.Value),
            LastTs = IsoUtc(agg.LastTs.Value),
            UtcOffsetMin = (int)_tz.GetUtcOffset(agg.FirstTs.Value).TotalMinutes,
            EventCount = agg.EventCount,
            SkippedLines = agg.SkippedLines,
            CacheTokens = new VaultCacheTokens { Read = agg.CacheRead, Creation = agg.CacheCreation },
            ByDay = agg.ByDay.ToDictionary(
                kv => DayKey(kv.Key),
                kv => new VaultDayBucket
                {
                    Total = kv.Value.Total,
                    ByModel = new Dictionary<string, long>(kv.Value.ByModel),
                    BySkill = kv.Value.BySkill.Count > 0
                        ? new Dictionary<string, long>(kv.Value.BySkill)
                        : null,
                }),
        };
        return VaultRowMath.SplitByMonth(merged);
    }

    /// <summary>SHA-256 hex of the up-to-64 bytes ENDING at <paramref name="offset"/> —
    /// the tail guard Task 3's grown-file branch verifies before trusting a
    /// stored offset.</summary>
    internal static string GuardAt(FileStream fs, long offset)
    {
        int len = (int)Math.Min(TailGuardBytes, offset);
        if (len <= 0)
            return "";
        var buf = new byte[len];
        fs.Seek(offset - len, SeekOrigin.Begin);
        fs.ReadExactly(buf, 0, len);
        return Convert.ToHexString(SHA256.HashData(buf)).ToLowerInvariant();
    }

    // -- helpers ------------------------------------------------------------------

    private DateOnly LocalDate(DateTimeOffset ts)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ts, _tz).DateTime);

    private static string DayKey(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string IsoUtc(DateTimeOffset ts)
        => ts.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseIso(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        return DateTimeOffset.TryParse(
            s.Replace("Z", "+00:00"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }

    private static string? Str(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long Num(JsonObject o, string key)
    {
        if (o[key] is not JsonValue v)
            return 0;
        if (v.TryGetValue<long>(out var l))
            return l;
        if (v.TryGetValue<double>(out var d))
            return (long)d;
        return 0;
    }

    // PRIVACY.md contract: fixed phrases + exception type names only.
    private void Log(string message)
    {
        if (_logFile is null)
            return;
        try
        {
            File.AppendAllText(_logFile, $"{DateTime.UtcNow:o} vault {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break ingestion.
        }
    }

    private void Log2(string operation, Exception e)
        => Log($"{operation} failed ({e.GetType().Name})");
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS — 374 + 16 new = 390. The full suite must stay green (the `ModelToTierKey` delegation must not change behavior — `CcLogReaderTests` cover it).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/VaultIngester.cs windows-dotnet/src/Sanduhr.Core/VaultModels.cs windows-dotnet/src/Sanduhr.Core/CcLogReader.cs windows-dotnet/tests/Sanduhr.Tests/VaultIngesterTests.cs
git commit -m "feat(vault): VaultIngester core — checkpointed walk, month slicing, rollup fold, writer mutex"
```

---

### Task 3: `VaultIngester` hardening — guarded tail parse, seal, torn lines, quarantine, prune (Core, TDD)

Fills in the two hooks Task 2 marked in `IngestRoot`. The adversarial fixtures are the point of this workstream — do not soften an assertion to make it pass; if one fails, the implementation is wrong.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs`
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultIngesterHardeningTests.cs`

**Interfaces:**
- Consumes: everything Task 2 produced. No new public surface except `VaultIngester.QuiesceAfter` (internal static readonly `TimeSpan`, 1 hour).
- Produces: behavior only — Tasks 4–8 depend on the invariants, not new names.

**The complete per-file decision tree (replaces Task 2's simpler one):**

1. `unchanged` (stat == checkpoint) and `Sealed` → touch `LastSeenTs`, skip.
2. `unchanged`, not sealed, NOT quiesced (`nowUtc - mtime < 1h`) → touch, skip.
3. `unchanged`, not sealed, quiesced → one final whole-file VERIFY parse; rows replaced unconditionally (one extra shard write per file lifetime, then sealed skips forever); checkpoint entry written with `Sealed = true`.
4. Grown (`statLen > cp.Length`), not sealed, `cp.Offset > 0`, guard non-empty, stored rows loadable AND matching the checkpoint's row fingerprint (`RowTotal`/`RowEvents` — a crash between shard write and checkpoint write leaves rows NEWER than the checkpoint; seeding a tail from them would double-count), live length ≥ offset, AND the 64 bytes before `cp.Offset` hash to `cp.TailGuard` → TAIL parse from `cp.Offset` seeded with the merged stored rows; `Sealed = false`.
5. Everything else (new file, shrink, guard mismatch, fingerprint mismatch, sealed-but-changed, seed missing) → full reparse; `Sealed = false`. This is what keeps `VaultIngesterTests.Crash_between_shard_and_checkpoint_converges` green once the tail branch exists.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/VaultIngesterHardeningTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// The adversarial ingestion battery (spec: Testing). Uses the same fixture
/// helpers as VaultIngesterTests — keep the two files' helpers identical; they
/// are duplicated so each file reads standalone.
/// </summary>
public class VaultIngesterHardeningTests
{
    private static readonly TimeZoneInfo Cst =
        TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T18:00:00+00:00");

    private static string MutexName() => "SanduhrTest.Vault." + Guid.NewGuid().ToString("N");

    private static string EventLine(string ts, string model = "claude-fable-5",
        long input = 100, long output = 50, string? cwd = @"C:\Users\x\Projects\api")
    {
        var cwdJson = cwd is null ? "" : ",\"cwd\":" + System.Text.Json.JsonSerializer.Serialize(cwd);
        return "{\"type\":\"assistant\",\"timestamp\":\"" + ts + "\"" + cwdJson +
               ",\"message\":{\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + input +
               ",\"output_tokens\":" + output + "}}}";
    }

    private static string WriteSession(
        string home, string root, string uuid, DateTimeOffset? mtimeUtc = null, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", "c--Users-x-Projects-api");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, uuid + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, (mtimeUtc ?? Now.AddMinutes(-10)).UtcDateTime);
        return path;
    }

    private static (VaultIngester Ingester, VaultStore Store) Make(
        string home, string vaultDir, string? logFile = null)
    {
        var store = new VaultStore(vaultDir, logFile);
        var ing = new VaultIngester(home, store, "test", logFile, Cst, MutexName());
        return (ing, store);
    }

    private static void Append(string path, DateTimeOffset mtimeUtc, params string[] lines)
    {
        File.AppendAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, mtimeUtc.UtcDateTime);
    }

    private static long RowTotal(VaultStore store, string month = "2026-07", string uuid = "u1")
    {
        store.TryLoadSessionShard(".claude", month, out var shard);
        return shard.Sessions[uuid].Total;
    }

    /// <summary>Spec invariant: fold(session shard) == stored rollup shard,
    /// after EVERY ingest — including tail parses, month-moves, and
    /// crash/quarantine recovery. Called at the end of the recovery tests.</summary>
    private static void AssertFoldMatchesRollups(VaultStore store, string root, string month)
    {
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadSessionShard(root, month, out var sessions));
        Assert.Equal(ShardLoadResult.Ok, store.TryLoadRollupShard(root, month, out var rollups));
        var expected = new Dictionary<string, long>();
        foreach (var row in sessions.Sessions.Values)
            foreach (var (day, bucket) in row.ByDay)
                expected[day] = expected.GetValueOrDefault(day) + bucket.Total;
        Assert.Equal(expected.Count, rollups.Days.Count);
        foreach (var (day, total) in expected)
            Assert.Equal(total, rollups.Days[day].Total);
    }

    [Fact]
    public void Growing_file_tail_parses_and_converges_with_full_parse()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        Append(path, Now.AddMinutes(-5), EventLine("2026-07-10T16:00:00Z", input: 200, output: 0));
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesTailParsed);
        Assert.Equal(0, r2.FilesFullParsed);
        Assert.Equal(350, RowTotal(store));                  // 150 + 200, no double count
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(2, shard.Sessions["u1"].EventCount);

        // Convergence: a from-scratch vault over the same file is identical.
        using var vault2 = new TempDir();
        var (ing2, store2) = Make(home.Path, vault2.Path);
        ing2.IngestOnce(new[] { ".claude" }, false, Now);
        store2.TryLoadSessionShard(".claude", "2026-07", out var fresh);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(fresh.Sessions["u1"]),
            System.Text.Json.JsonSerializer.Serialize(shard.Sessions["u1"]));
        AssertFoldMatchesRollups(store, ".claude", "2026-07");
    }

    [Fact]
    public void Tail_parse_preserves_project_identity_without_stored_cwd()
    {
        // store_full_paths is OFF: the stored row carries project_key but a null
        // cwd. A tail whose events carry no cwd must NOT degrade the project.
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        store.TryLoadSessionShard(".claude", "2026-07", out var before);
        var keyBefore = before.Sessions["u1"].ProjectKey;

        Append(path, Now.AddMinutes(-5), EventLine("2026-07-10T16:00:00Z", cwd: null));
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        store.TryLoadSessionShard(".claude", "2026-07", out var after);
        Assert.Equal(keyBefore, after.Sessions["u1"].ProjectKey);
        Assert.Equal("api", after.Sessions["u1"].ProjectName);
    }

    [Fact]
    public void Torn_last_line_completing_after_checkpoint_converges()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var full = EventLine("2026-07-10T16:00:00Z", input: 200, output: 0);
        var dir = Path.Combine(home.Path, ".claude", "projects", "c--Users-x-Projects-api");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "u1.jsonl");
        // Complete first line, then HALF of the second with no terminator.
        File.WriteAllText(path, EventLine("2026-07-10T15:00:00Z") + "\n" + full[..40]);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-10).UtcDateTime);

        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(150, RowTotal(store));                  // torn line not consumed

        // The writer finishes the line.
        File.AppendAllText(path, full[40..] + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-5).UtcDateTime);
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesTailParsed);                 // guard bytes precede the tear — still valid
        Assert.Equal(350, RowTotal(store));
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Equal(0, shard.Sessions["u1"].SkippedLines);  // the half-line was never "malformed"
    }

    [Fact]
    public void Guard_mismatch_forces_full_reparse()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Same uuid, longer file, DIFFERENT bytes before the old offset —
        // (a restore-from-backup shape). Grown-gate passes, guard must not.
        File.WriteAllText(path,
            EventLine("2026-07-10T14:00:00Z", input: 999, output: 0) + "\n" +
            EventLine("2026-07-10T15:30:00Z", input: 1, output: 0) + "\n" +
            EventLine("2026-07-10T16:00:00Z", input: 2, output: 0) + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-4).UtcDateTime);
        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);

        Assert.Equal(1, r2.FilesFullParsed);
        Assert.Equal(0, r2.FilesTailParsed);
        Assert.Equal(1002, RowTotal(store));                 // truth, not stale + tail
    }

    [Fact]
    public void Shrunk_file_forces_full_reparse()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"), EventLine("2026-07-10T16:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(300, RowTotal(store));

        WriteSession(home.Path, ".claude", "u1", Now.AddMinutes(-3),
            EventLine("2026-07-10T15:00:00Z", input: 10, output: 0));
        ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(10, RowTotal(store));
    }

    [Fact]
    public void Quiesced_file_verify_parses_then_seals_then_skips()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", Now.AddHours(-2),
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);

        var r1 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // backfill: full parse
        Assert.Equal(1, r1.FilesFullParsed);
        var cp1 = store.LoadCheckpoints(".claude").Entries.Values.Single();
        Assert.False(cp1.Sealed);

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // unchanged + quiesced: verify + seal
        Assert.Equal(1, r2.FilesFullParsed);
        Assert.True(store.LoadCheckpoints(".claude").Entries.Values.Single().Sealed);
        Assert.Equal(150, RowTotal(store));

        var r3 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // sealed: skipped entirely
        Assert.Equal(1, r3.FilesSkipped);
        Assert.Equal(0, r3.FilesFullParsed + r3.FilesTailParsed);

        // A sealed file that changes unseals via full reparse.
        Append(path, Now.AddHours(-1).AddMinutes(30), EventLine("2026-07-10T16:00:00Z", input: 7, output: 0));
        var r4 = ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(1, r4.FilesFullParsed);
        Assert.Equal(157, RowTotal(store));
        Assert.False(store.LoadCheckpoints(".claude").Entries.Values.Single().Sealed);
    }

    [Fact]
    public void Locked_file_advances_nothing_and_recovers_next_cycle()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        using var log = new TempDir();
        var logFile = Path.Combine(log.Path, "sanduhr.log");
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path, logFile);

        VaultIngestResult r1;
        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r1 = ing.IngestOnce(new[] { ".claude" }, false, Now);
        }
        Assert.Equal(1, r1.FilesFailed);
        Assert.Equal(ShardLoadResult.Missing, store.TryLoadSessionShard(".claude", "2026-07", out _));
        Assert.Empty(store.LoadCheckpoints(".claude").Entries);   // no entry ever existed — nothing advanced

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);
        Assert.Equal(1, r2.FilesFullParsed);
        Assert.Equal(150, RowTotal(store));
    }

    [Fact]
    public void Failed_ingest_log_contains_no_path_separators()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        using var log = new TempDir();
        var logFile = Path.Combine(log.Path, "sanduhr.log");
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, _) = Make(home.Path, vault.Path, logFile);

        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ing.IngestOnce(new[] { ".claude" }, false, Now);
        }

        // PRIVACY.md: operation + exception TYPE only — a path (or e.Message,
        // which embeds one) would carry a separator.
        var content = File.ReadAllText(logFile);
        Assert.NotEqual("", content);
        Assert.DoesNotContain("\\", content);
        Assert.DoesNotContain("/", content);
    }

    [Fact]
    public void Quarantined_shard_invalidates_checkpoints_and_full_reingest_converges()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        WriteSession(home.Path, ".claude", "u1", null, EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        // Corrupt the shard on disk behind the ingester's back.
        var shardPath = Path.Combine(vault.Path, ".claude", "sessions-2026-07.json");
        File.WriteAllText(shardPath, "{corrupt");
        // Touch the source file so the next cycle re-reads it (stat changed).
        WriteSession(home.Path, ".claude", "u1", Now.AddMinutes(-5),
            EventLine("2026-07-10T15:00:00Z"), EventLine("2026-07-10T16:00:00Z"));

        var r2 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // hits corrupt -> quarantine + abort
        Assert.Equal(1, r2.RootsAborted);
        Assert.Single(Directory.GetFiles(Path.Combine(vault.Path, ".claude"), "*.bad"));
        Assert.False(File.Exists(Path.Combine(vault.Path, ".claude", "checkpoints.json")));

        var r3 = ing.IngestOnce(new[] { ".claude" }, false, Now);   // full re-ingest rebuilds
        Assert.Equal(0, r3.RootsAborted);
        Assert.Equal(300, RowTotal(store));
        Assert.Single(Directory.GetFiles(Path.Combine(vault.Path, ".claude"), "*.bad")); // .bad untouched
        AssertFoldMatchesRollups(store, ".claude", "2026-07");
    }

    [Fact]
    public void Checkpoint_prune_then_file_resurrection_is_harmless()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-07-10T15:00:00Z"));
        var (ing, store) = Make(home.Path, vault.Path);
        ing.IngestOnce(new[] { ".claude" }, false, Now);

        File.Delete(path);
        var later = Now.AddDays(8);                          // > 7-day prune horizon
        ing.IngestOnce(new[] { ".claude" }, false, later);

        Assert.Empty(store.LoadCheckpoints(".claude").Entries);       // bookkeeping pruned
        Assert.Equal(150, RowTotal(store));                           // the RECORD outlives its source

        // Resurrection (restored from backup): re-ingest is idempotent.
        WriteSession(home.Path, ".claude", "u1", later.AddMinutes(-10),
            EventLine("2026-07-10T15:00:00Z"));
        ing.IngestOnce(new[] { ".claude" }, false, later.AddMinutes(-5));
        store.TryLoadSessionShard(".claude", "2026-07", out var shard);
        Assert.Single(shard.Sessions);
        Assert.Equal(150, shard.Sessions["u1"].Total);
    }

    [Fact]
    public void First_ts_month_move_on_reingest_leaves_one_logical_session()
    {
        using var home = new TempDir();
        using var vault = new TempDir();
        // Initially the session's first counted event is in August...
        var path = WriteSession(home.Path, ".claude", "u1", null,
            EventLine("2026-08-05T20:00:00Z", input: 200, output: 0));
        var (ing, store) = Make(home.Path, vault.Path);
        var now = DateTimeOffset.Parse("2026-08-06T00:00:00+00:00");
        ing.IngestOnce(new[] { ".claude" }, false, now);
        store.TryLoadSessionShard(".claude", "2026-08", out var aug1);
        Assert.False(aug1.Sessions["u1"].Continuation);

        // ...then a parser-fix-shaped change makes a JULY event parse too:
        // first_ts moves across the month boundary. Exactly one logical
        // session must survive, across the checkpoint's month union.
        WriteSession(home.Path, ".claude", "u1", now.AddMinutes(-5),
            EventLine("2026-07-31T20:00:00Z", input: 100, output: 0),
            EventLine("2026-08-05T20:00:00Z", input: 200, output: 0));
        ing.IngestOnce(new[] { ".claude" }, false, now);

        store.TryLoadSessionShard(".claude", "2026-07", out var jul);
        store.TryLoadSessionShard(".claude", "2026-08", out var aug2);
        Assert.False(jul.Sessions["u1"].Continuation);       // primary moved to July
        Assert.True(aug2.Sessions["u1"].Continuation);       // August is now a slice
        Assert.Equal(100, jul.Sessions["u1"].Total);
        Assert.Equal(200, aug2.Sessions["u1"].Total);

        // And the reverse: the July event stops parsing again — the July row
        // must be REMOVED, not stranded.
        WriteSession(home.Path, ".claude", "u1", now.AddMinutes(-3),
            EventLine("2026-08-05T20:00:00Z", input: 200, output: 0));
        ing.IngestOnce(new[] { ".claude" }, false, now);
        store.TryLoadSessionShard(".claude", "2026-07", out var julAfter);
        Assert.False(julAfter.Sessions.ContainsKey("u1"));
        store.TryLoadSessionShard(".claude", "2026-08", out var aug3);
        Assert.False(aug3.Sessions["u1"].Continuation);
        AssertFoldMatchesRollups(store, ".claude", "2026-07");
        AssertFoldMatchesRollups(store, ".claude", "2026-08");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultIngesterHardening"`
Expected: FAIL — tail parses report as full parses, seal never happens, quarantine mid-walk aborts differently. (Some may pass by luck of full-reparse convergence; the counters make most fail.)

- [ ] **Step 3: Implement the decision tree**

In `windows-dotnet/src/Sanduhr.Core/VaultIngester.cs`:

Add the constant next to the others:

```csharp
    internal static readonly TimeSpan QuiesceAfter = TimeSpan.FromHours(1);
```

Add two seed fields to `SessionAgg`:

```csharp
        public string? SeedProjectKey;
        public string? SeedProjectName;
```

In `BuildRows`, replace the `projectKey` / `ProjectName` derivation with the seed-aware version (a tail whose new events carry no cwd must not degrade the project when `store_full_paths` is off):

```csharp
        string projectKey, projectName;
        if (agg.Cwd is not null)
        {
            projectName = CcLogReader.ProjectDisplayName(agg.Cwd);
            projectKey = $"{projectName}~{VaultStore.PathKey(agg.Cwd)[..8]}";
        }
        else if (agg.SeedProjectKey is not null)
        {
            projectKey = agg.SeedProjectKey;
            projectName = agg.SeedProjectName ?? "(none)";
        }
        else
        {
            projectKey = "(none)";
            projectName = "(none)";
        }
```

(and use `projectKey` / `projectName` in the row initializer).

Replace the per-file section of `IngestRoot` — from `checkpoints.Entries.TryGetValue(key, out var cp);` through the `full++;` line — with:

```csharp
            checkpoints.Entries.TryGetValue(key, out var cp);
            var uuid = Path.GetFileNameWithoutExtension(fi.Name);

            bool unchanged = cp is not null && cp.MtimeTicks == statMtime && cp.Length == statLen;
            bool quiesced = nowUtc - fi.LastWriteTimeUtc >= QuiesceAfter;

            if (unchanged && (cp!.Sealed || !quiesced))
            {
                cp.LastSeenTs = nowIso;
                skipped++;
                continue;
            }

            // Live paths: (A) unchanged+quiesced+unsealed -> whole-file verify
            // parse, then seal; (B) grown + verified guard -> tail parse seeded
            // from the stored rows; (C) everything else -> full reparse.
            bool sealAfter = unchanged;
            bool wantTail = !unchanged && cp is not null && !cp.Sealed
                && statLen > cp.Length && cp.Offset > 0 && cp.TailGuard.Length > 0;

            SessionAgg? seed = null;
            if (wantTail)
            {
                bool corruptSeed = false;
                seed = SeedFromStored(rootName, uuid, cp!.Months, shardCache, nowUtc, ref corruptSeed);
                if (corruptSeed)
                    return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);
                // Crash-vs-tail guard: a crash that landed the shard but not the
                // checkpoint leaves stored rows NEWER than cp — seeding a tail
                // from them would double-count. Fingerprint mismatch -> full
                // reparse (idempotent row replace), which converges. Cache
                // counters are part of the fingerprint because a cache-only
                // tail changes no token totals. (Residual: a tail that changed
                // ONLY skipped_lines slips through and briefly double-counts
                // that counter — cosmetic, heals at the quiesce-seal verify.)
                if (seed is not null
                    && (seed.ByDay.Values.Sum(d => d.Total) != cp.RowTotal
                        || seed.EventCount != cp.RowEvents
                        || seed.CacheRead != cp.RowCacheRead
                        || seed.CacheCreation != cp.RowCacheCreation))
                {
                    seed = null;
                }
            }

            SessionAgg agg;
            long endOffset;
            string tailGuard;
            bool usedTail = false;
            try
            {
                using var fs = new FileStream(
                    fi.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (seed is not null && fs.Length >= cp!.Offset
                    && GuardAt(fs, cp.Offset) == cp.TailGuard)
                {
                    agg = seed;
                    endOffset = ParseLines(fs, cp.Offset, agg);
                    usedTail = true;
                }
                else
                {
                    agg = new SessionAgg();
                    endOffset = ParseLines(fs, 0, agg);
                }
                tailGuard = GuardAt(fs, endOffset);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (cp is not null)
                    cp.LastSeenTs = nowIso;
                Log2("file-open", e);
                failed++;
                continue;
            }

            var newRows = BuildRows(uuid, agg, storeFullPaths);
            var oldMonths = cp?.Months ?? new List<string>();
            var touched = new HashSet<string>(oldMonths, StringComparer.Ordinal);
            touched.UnionWith(newRows.Keys);

            bool corrupt = false;
            foreach (var month in touched)
            {
                var shard = GetShard(shardCache, rootName, month, nowUtc, ref corrupt);
                if (corrupt)
                    break;
                shard.Sessions.Remove(uuid);
                if (newRows.TryGetValue(month, out var row))
                    shard.Sessions[uuid] = row;
                dirtyMonths.Add(month);
            }
            if (corrupt)
                return new VaultIngestResult(true, seen, full, tailParsed, skipped, failed, 1);

            checkpoints.Entries[key] = new VaultCheckpointEntry
            {
                MtimeTicks = statMtime,
                Length = statLen,
                Offset = endOffset,
                TailGuard = tailGuard,
                RowTotal = newRows.Values.Sum(r => r.Total),
                RowEvents = newRows.Values.Sum(r => r.EventCount),
                RowCacheRead = newRows.Values.Sum(r => r.CacheTokens?.Read ?? 0),
                RowCacheCreation = newRows.Values.Sum(r => r.CacheTokens?.Creation ?? 0),
                Months = newRows.Keys.OrderBy(m => m, StringComparer.Ordinal).ToList(),
                Sealed = sealAfter,
                LastSeenTs = nowIso,
            };
            if (usedTail) tailParsed++; else full++;
```

Add the seed loader next to `GetShard`:

```csharp
    /// <summary>Reconstruct a parse aggregate from a session's stored rows
    /// (primary + slices) so a tail parse folds into a COPY of the stored row.
    /// Null when no rows exist (e.g. a previously zero-event session) — the
    /// caller falls back to a full reparse.</summary>
    private SessionAgg? SeedFromStored(
        string rootName, string uuid, List<string> months,
        Dictionary<string, VaultSessionShard> cache, DateTimeOffset nowUtc, ref bool corrupt)
    {
        var rows = new List<VaultSessionRow>();
        foreach (var month in months)
        {
            var shard = GetShard(cache, rootName, month, nowUtc, ref corrupt);
            if (corrupt)
                return null;
            if (shard.Sessions.TryGetValue(uuid, out var row))
                rows.Add(row);
        }
        if (rows.Count == 0)
            return null;

        var merged = VaultRowMath.Merge(rows);
        var agg = new SessionAgg
        {
            FirstTs = ParseIso(merged.FirstTs),
            LastTs = ParseIso(merged.LastTs),
            EventCount = merged.EventCount,
            SkippedLines = merged.SkippedLines,
            Cwd = merged.Cwd,
            CacheRead = merged.CacheTokens?.Read ?? 0,
            CacheCreation = merged.CacheTokens?.Creation ?? 0,
            SeedProjectKey = merged.ProjectKey,
            SeedProjectName = merged.ProjectName,
        };
        foreach (var (dayKey, bucket) in merged.ByDay)
        {
            var day = DateOnly.Parse(dayKey, CultureInfo.InvariantCulture);
            var d = new DayAgg { Total = bucket.Total };
            foreach (var (m, v) in bucket.ByModel)
                d.ByModel[m] = v;
            if (bucket.BySkill is not null)
                foreach (var (s, v) in bucket.BySkill)
                    d.BySkill[s] = v;
            agg.ByDay[day] = d;
        }
        return agg;
    }
```

(`SessionAgg.FirstTs`/`LastTs` are already nullable; `DayAgg.Total` is a public field so the object initializer works. Make `SessionAgg`'s fields settable via object initializer — they already are plain public fields.)

- [ ] **Step 4: Run the full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS — 390 + 11 new = 401. Task 2's tests must ALL still pass (the pinned-mtime helpers keep the seal branch out of their way).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/VaultIngester.cs windows-dotnet/tests/Sanduhr.Tests/VaultIngesterHardeningTests.cs
git commit -m "feat(vault): guarded tail parse, quiesce-seal, torn-line + quarantine + prune hardening"
```

---

### Task 4: `VaultReader` + `CcLogReader.AggregateTodayOnly` + `VaultLedgerCsv` (Core, TDD)

The whole read side. Everything the three UI sections consume comes from here, fully unit-tested, so the untestable App layer stays a thin projection.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/VaultReader.cs`
- Create: `windows-dotnet/src/Sanduhr.Core/VaultLedgerCsv.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (add `AggregateTodayOnly`)
- Modify: `windows-dotnet/src/Sanduhr.Core/CsvExport.cs` (`private static string Escape` → `internal static string Escape` — reused by `VaultLedgerCsv`; `InternalsVisibleTo` already covers App + Tests)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/VaultReaderTests.cs`, create `windows-dotnet/tests/Sanduhr.Tests/VaultLedgerCsvTests.cs`, modify `windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs`

**Interfaces:**
- Consumes (Tasks 1–3): `VaultStore`, all `Vault*` models, `VaultRowMath.Merge`.
- Produces (Tasks 6–8 rely on these exact names):
  - `sealed record VaultWindow(Dictionary<DateOnly, long> ByDay, Dictionary<string, long> ByProjectName, Dictionary<string, long> BySkill)`
  - `sealed record VaultWeek(DateOnly WeekStart, long Total, bool IsCurrent, bool HasNoRecordGap)`
  - `sealed record VaultSessionInfo(string Uuid, string Root, string ProjectKey, string ProjectName, string? Cwd, DateTimeOffset FirstTs, DateTimeOffset LastTs, long Total, Dictionary<string, long> ByModel, Dictionary<string, long>? BySkill, Dictionary<string, VaultDayBucket> ByDay, VaultCacheTokens? Cache)`
  - `sealed class VaultReader(VaultStore store)` with:
    - `VaultWindow ReadWindow(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toExclusive)` — merged rollup days across roots; `ByProjectName` merges by DISPLAY name (Overview parity with today's basename merge)
    - `IReadOnlyList<VaultWeek> ReadWeeks(IReadOnlyList<string> roots, int weeks, DateOnly today)` — Monday-start weeks, oldest first, last entry `IsCurrent`
    - `IReadOnlyList<(string Name, long Total)> TopProjects(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toExclusive, int top)`
    - `IReadOnlyList<VaultSessionInfo> ReadSessions(IReadOnlyList<string> roots)` — slices merged by (root, uuid) via `VaultRowMath.Merge`
    - `static long TokensInScope(VaultSessionInfo s, DateOnly fromInclusive, DateOnly toInclusive)`
    - `static string ProjectNameOf(string projectKey)` — strips the `~hash` suffix
    - `DateOnly? BirthDate(IReadOnlyList<string> roots)` — min of the roots' `meta.Since`, null when no meta exists
    - `DateTimeOffset? LastSuccessfulIngestUtc(IReadOnlyList<string> roots)` — MIN across roots (any stale root ⇒ degraded honesty), null when any consented root has no meta
    - `bool IsDayCovered(IReadOnlyList<string> roots, DateOnly day)` — INTERSECTION: covered only when every consented root's ranges contain the day
  - `CcLogReader.AggregateTodayOnly()` — `LocalCcAggregate` restricted to events whose LOCAL date is today (file prefilter: mtime ≥ local midnight; own 30-second cache slot, invalidated by `InvalidateCache`)
  - `static class VaultLedgerCsv` with `sealed record Row(string Uuid, string Root, string Project, string FirstTs, string LastTs, long TokensInScope, long TokensTotal, string Models)` and `CsvExport.CsvBuildResult Build(IReadOnlyList<Row> rows)` — header `session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models`, RFC-4180 quoting via `CsvExport.Escape`, CRLF, rows in the order given (the VM passes them pre-sorted)
- Corrupt/missing shards on the read path degrade to empty — readers NEVER mutate the vault (no quarantine from reads; the next ingest cycle owns that).

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/VaultReaderTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Read-side contract: window merges, weekly buckets + no-record
/// coverage, session slice merge, scope math, and the meta projections the
/// degraded-mode gate and Trends footer consume. Fixtures write shards through
/// VaultStore directly — reader tests need no ingester.</summary>
public class VaultReaderTests
{
    private static VaultSessionRow Row(
        string projectKey, string firstTs, string lastTs, bool continuation,
        params (string Day, long Total)[] days)
    {
        var row = new VaultSessionRow
        {
            ProjectKey = projectKey,
            ProjectName = VaultReader.ProjectNameOf(projectKey),
            FirstTs = firstTs,
            LastTs = lastTs,
            UtcOffsetMin = -300,
            EventCount = continuation ? 0 : days.Length,
            Continuation = continuation,
            CacheTokens = continuation ? null : new VaultCacheTokens { Read = 1, Creation = 1 },
            ByDay = days.ToDictionary(
                d => d.Day,
                d => new VaultDayBucket
                {
                    Total = d.Total,
                    ByModel = new Dictionary<string, long> { ["claude-fable-5"] = d.Total },
                }),
        };
        VaultRowMath.RecomputeRowAggregates(row);
        return row;
    }

    private static void SaveShard(VaultStore store, string root, string month,
        params (string Uuid, VaultSessionRow Row)[] rows)
    {
        var shard = new VaultSessionShard { SchemaVersion = 1, WriterVersion = "test" };
        foreach (var (uuid, row) in rows)
            shard.Sessions[uuid] = row;
        store.SaveSessionShard(root, month, shard);
    }

    private static void SaveRollup(VaultStore store, string root, string month,
        params (string Day, long Total, string ProjectKey, string Skill)[] days)
    {
        var shard = new VaultRollupShard { SchemaVersion = 1 };
        foreach (var (day, total, projectKey, skill) in days)
        {
            if (!shard.Days.TryGetValue(day, out var d))
                shard.Days[day] = d = new VaultRollupDay();
            d.Total += total;
            d.Sessions++;
            d.ByModel["claude-fable-5"] = d.ByModel.GetValueOrDefault("claude-fable-5") + total;
            d.ByProject[projectKey] = d.ByProject.GetValueOrDefault(projectKey) + total;
            if (skill.Length > 0)
                d.BySkill[skill] = d.BySkill.GetValueOrDefault(skill) + total;
        }
        store.SaveRollupShard(root, month, shard);
    }

    [Fact]
    public void ReadWindow_merges_roots_and_projects_by_display_name()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        // Same display name "api", different hashes, different roots.
        SaveRollup(store, ".claude", "2026-07",
            ("2026-07-10", 100, "api~aaaaaaaa", "code-review"),
            ("2026-07-11", 50, "api~aaaaaaaa", ""));
        SaveRollup(store, ".claude-personal", "2026-07",
            ("2026-07-10", 30, "api~bbbbbbbb", ""));

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude", ".claude-personal" },
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));

        Assert.Equal(130, w.ByDay[new DateOnly(2026, 7, 10)]);
        Assert.Equal(50, w.ByDay[new DateOnly(2026, 7, 11)]);
        Assert.Equal(180, w.ByProjectName["api"]);           // merged by display name
        Assert.Equal(100, w.BySkill["code-review"]);
    }

    [Fact]
    public void ReadWindow_excludes_toExclusive_day()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveRollup(store, ".claude", "2026-07",
            ("2026-07-11", 50, "api~aaaaaaaa", ""),
            ("2026-07-12", 999, "api~aaaaaaaa", ""));        // "today" — must not leak in

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));

        Assert.False(w.ByDay.ContainsKey(new DateOnly(2026, 7, 12)));
        Assert.Equal(50, w.ByDay.Values.Sum());
        Assert.Equal(50, w.ByProjectName["api"]);            // breakdowns respect the boundary too
    }

    [Fact]
    public void ReadWindow_spans_months()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveRollup(store, ".claude", "2026-06", ("2026-06-30", 10, "api~aaaaaaaa", ""));
        SaveRollup(store, ".claude", "2026-07", ("2026-07-01", 20, "api~aaaaaaaa", ""));

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 12));
        Assert.Equal(30, w.ByDay.Values.Sum());
    }

    [Fact]
    public void Weeks_bucket_monday_start_and_flag_current()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        // 2026-07-12 is a Sunday; its week starts Monday 2026-07-06.
        SaveRollup(store, ".claude", "2026-07",
            ("2026-07-06", 100, "api~aaaaaaaa", ""),
            ("2026-07-01", 40, "api~aaaaaaaa", ""));         // previous week (Mon 2026-06-29)
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-06-01",
            Covered = new List<VaultDateRange> { new() { From = "2026-06-01", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });

        var reader = new VaultReader(store);
        var weeks = reader.ReadWeeks(new[] { ".claude" }, 4, new DateOnly(2026, 7, 12));

        Assert.Equal(4, weeks.Count);
        Assert.Equal(new DateOnly(2026, 7, 6), weeks[^1].WeekStart);
        Assert.True(weeks[^1].IsCurrent);
        Assert.False(weeks[^2].IsCurrent);
        Assert.Equal(100, weeks[^1].Total);
        Assert.Equal(40, weeks[^2].Total);
        Assert.All(weeks, w => Assert.False(w.HasNoRecordGap));   // fully covered window
    }

    [Fact]
    public void Weeks_flag_no_record_for_uncovered_and_pre_vault_days()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            // Widget-off fortnight: coverage resumes 2026-07-08.
            Covered = new List<VaultDateRange>
            {
                new() { From = "2026-06-25", To = "2026-07-01" },
                new() { From = "2026-07-08", To = "2026-07-12" },
            },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });

        var reader = new VaultReader(store);
        var weeks = reader.ReadWeeks(new[] { ".claude" }, 4, new DateOnly(2026, 7, 12));

        // Today is Sunday 2026-07-12, so the 4 week starts are 06-15, 06-22,
        // 06-29, 07-06. Coverage: [06-25..07-01] and [07-08..07-12].
        Assert.Equal(new DateOnly(2026, 6, 15), weeks[0].WeekStart);
        Assert.True(weeks[0].HasNoRecordGap);   // 06-15..06-21 fully pre-vault
        Assert.True(weeks[1].HasNoRecordGap);   // 06-22..06-24 uncovered
        Assert.True(weeks[2].HasNoRecordGap);   // 07-02..07-05 uncovered (widget-off gap)
        Assert.True(weeks[3].IsCurrent);
        Assert.True(weeks[3].HasNoRecordGap);   // 07-06/07-07 uncovered
    }

    [Fact]
    public void Coverage_is_the_intersection_of_consented_roots()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            Covered = new List<VaultDateRange> { new() { From = "2026-07-01", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });
        store.SaveMeta(".claude-personal", new VaultRootMeta
        {
            Since = "2026-07-05",
            Covered = new List<VaultDateRange> { new() { From = "2026-07-05", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });

        var reader = new VaultReader(store);
        var roots = new[] { ".claude", ".claude-personal" };
        Assert.True(reader.IsDayCovered(roots, new DateOnly(2026, 7, 6)));
        Assert.False(reader.IsDayCovered(roots, new DateOnly(2026, 7, 3)));   // personal lacks it
        Assert.False(reader.IsDayCovered(roots, new DateOnly(2026, 6, 20)));  // nobody has it
        Assert.True(reader.IsDayCovered(new[] { ".claude" }, new DateOnly(2026, 7, 3)));
    }

    [Fact]
    public void Sessions_merge_slices_by_uuid_within_a_root()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("u1", Row("api~aaaaaaaa", "2026-07-31T20:00:00+00:00", "2026-08-01T22:00:00+00:00",
                continuation: false, ("2026-07-31", 100))));
        SaveShard(store, ".claude", "2026-08",
            ("u1", Row("api~aaaaaaaa", "2026-07-31T20:00:00+00:00", "2026-08-01T22:00:00+00:00",
                continuation: true, ("2026-08-01", 200))),
            ("u2", Row("web~cccccccc", "2026-08-02T10:00:00+00:00", "2026-08-02T11:00:00+00:00",
                continuation: false, ("2026-08-02", 50))));

        var reader = new VaultReader(store);
        var sessions = reader.ReadSessions(new[] { ".claude" });

        Assert.Equal(2, sessions.Count);
        var u1 = sessions.Single(s => s.Uuid == "u1");
        Assert.Equal(300, u1.Total);                          // slices merged
        Assert.Equal(2, u1.ByDay.Count);
        Assert.Equal(".claude", u1.Root);
        Assert.NotNull(u1.Cache);                             // primary's cache survives the merge
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 20, 0, 0, TimeSpan.Zero), u1.FirstTs);
    }

    [Fact]
    public void Same_uuid_in_two_roots_stays_two_sessions()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var row = Row("api~aaaaaaaa", "2026-07-10T15:00:00+00:00", "2026-07-10T16:00:00+00:00",
            continuation: false, ("2026-07-10", 100));
        SaveShard(store, ".claude", "2026-07", ("u1", row));
        SaveShard(store, ".claude-personal", "2026-07", ("u1", row));

        var reader = new VaultReader(store);
        var sessions = reader.ReadSessions(new[] { ".claude", ".claude-personal" });
        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, sessions.Select(s => s.Root).Distinct().Count());
    }

    [Fact]
    public void TokensInScope_sums_only_days_inside_the_scope()
    {
        var info = new VaultSessionInfo(
            "u1", ".claude", "api~aaaaaaaa", "api", null,
            DateTimeOffset.Parse("2026-07-08T10:00:00+00:00"),
            DateTimeOffset.Parse("2026-07-11T10:00:00+00:00"),
            600, new Dictionary<string, long> { ["claude-fable-5"] = 600 }, null,
            new Dictionary<string, VaultDayBucket>
            {
                ["2026-07-08"] = new() { Total = 100 },
                ["2026-07-10"] = new() { Total = 200 },
                ["2026-07-11"] = new() { Total = 300 },
            },
            null);

        Assert.Equal(500, VaultReader.TokensInScope(info, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 11)));
        Assert.Equal(300, VaultReader.TokensInScope(info, new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 11)));
        Assert.Equal(600, VaultReader.TokensInScope(info, DateOnly.MinValue, DateOnly.MaxValue));
        Assert.Equal(0, VaultReader.TokensInScope(info, new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 12)));
    }

    [Fact]
    public void Meta_projections_birth_min_ingest_min_and_null_gaps()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var reader = new VaultReader(store);
        var roots = new[] { ".claude", ".claude-personal" };

        Assert.Null(reader.BirthDate(roots));
        Assert.Null(reader.LastSuccessfulIngestUtc(roots));

        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });
        // One root still missing meta: birth reports the known min, staleness
        // stays null (an unstarted root must read as degraded, not fresh).
        Assert.Equal(new DateOnly(2026, 7, 1), reader.BirthDate(roots));
        Assert.Null(reader.LastSuccessfulIngestUtc(roots));

        store.SaveMeta(".claude-personal", new VaultRootMeta
        {
            Since = "2026-06-20",
            LastIngestTs = "2026-07-12T17:00:00.000000+00:00",
        });
        Assert.Equal(new DateOnly(2026, 6, 20), reader.BirthDate(roots));
        Assert.Equal(DateTimeOffset.Parse("2026-07-12T17:00:00+00:00"),
            reader.LastSuccessfulIngestUtc(roots));           // MIN across roots
    }

    [Fact]
    public void Corrupt_shard_on_read_degrades_to_empty_and_never_mutates()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{corrupt");
        File.WriteAllText(Path.Combine(dir, "rollups-2026-07.json"), "{corrupt");

        var reader = new VaultReader(store);
        Assert.Empty(reader.ReadSessions(new[] { ".claude" }));
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));
        Assert.Empty(w.ByDay);
        Assert.True(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));   // no quarantine from reads
        Assert.Empty(Directory.GetFiles(dir, "*.bad"));
    }

    [Fact]
    public void ProjectNameOf_strips_hash_suffix()
    {
        Assert.Equal("api", VaultReader.ProjectNameOf("api~3f2a91cc"));
        Assert.Equal("(none)", VaultReader.ProjectNameOf("(none)"));
        Assert.Equal("odd~name", VaultReader.ProjectNameOf("odd~name~12345678"));
    }
}
```

Create `windows-dotnet/tests/Sanduhr.Tests/VaultLedgerCsvTests.cs`:

```csharp
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class VaultLedgerCsvTests
{
    [Fact]
    public void Header_always_emitted_and_rows_in_given_order()
    {
        var result = VaultLedgerCsv.Build(Array.Empty<VaultLedgerCsv.Row>());
        Assert.Equal("session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models\r\n",
            result.Text);
        Assert.Equal(0, result.RowCount);

        var rows = new[]
        {
            new VaultLedgerCsv.Row("u2", ".claude", "api", "2026-07-11T00:00:00+00:00",
                "2026-07-11T01:00:00+00:00", 200, 200, "claude-fable-5:200"),
            new VaultLedgerCsv.Row("u1", ".claude", "web", "2026-07-10T00:00:00+00:00",
                "2026-07-10T01:00:00+00:00", 100, 600, "claude-fable-5:400;claude-sonnet-5:200"),
        };
        var built = VaultLedgerCsv.Build(rows);
        Assert.Equal(2, built.RowCount);
        var lines = built.Text.Split("\r\n");
        Assert.StartsWith("u2,", lines[1]);                   // caller's order preserved
        Assert.StartsWith("u1,", lines[2]);
        Assert.Contains("claude-fable-5:400;claude-sonnet-5:200", lines[2]);
    }

    [Fact]
    public void Fields_with_commas_or_quotes_are_rfc4180_quoted()
    {
        var rows = new[]
        {
            new VaultLedgerCsv.Row("u1", ".claude", "odd,\"proj\"", "t", "t", 1, 1, "m"),
        };
        var built = VaultLedgerCsv.Build(rows);
        Assert.Contains("\"odd,\"\"proj\"\"\"", built.Text);
    }
}
```

Append to `windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs` (match the file's existing fixture helpers — it already sandboxes with a temp home; reuse its established helper for writing session files, adjusting names to whatever the file uses):

```csharp
    [Fact]
    public void AggregateTodayOnly_restricts_to_local_today()
    {
        using var home = new TempDir();
        var dir = Path.Combine(home.Path, ".claude", "projects", "p1");
        Directory.CreateDirectory(dir);
        var today = DateTimeOffset.Now;
        var yesterday = today.AddDays(-1);
        string Line(DateTimeOffset ts, long tokens) =>
            "{\"type\":\"assistant\",\"timestamp\":\"" + ts.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") +
            "\",\"cwd\":\"C:\\\\p\\\\api\",\"message\":{\"model\":\"claude-fable-5\",\"usage\":{\"input_tokens\":" +
            tokens + ",\"output_tokens\":0}}}";
        File.WriteAllLines(Path.Combine(dir, "s1.jsonl"),
            new[] { Line(yesterday, 111), Line(today, 42) });

        var reader = new CcLogReader(home.Path);
        var agg = reader.AggregateTodayOnly();

        // "Today" is wall-clock-relative: if local midnight rolled over
        // between building the fixture and aggregating, the assertions are
        // undefined — bail out rather than flake (a rerun covers it).
        if (DateOnly.FromDateTime(today.LocalDateTime) != DateOnly.FromDateTime(DateTime.Now))
            return;

        var todayKey = DateOnly.FromDateTime(today.LocalDateTime);
        Assert.Equal(42, agg.ByDay.GetValueOrDefault(todayKey));
        Assert.Single(agg.ByDay);                             // yesterday excluded
        Assert.Equal(42, agg.ByProject.Values.Sum());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~VaultReader|FullyQualifiedName~VaultLedgerCsv|FullyQualifiedName~AggregateTodayOnly"`
Expected: FAIL — none of the types/methods exist.

- [ ] **Step 3: Implement `VaultReader.cs`**

```csharp
using System.Globalization;

namespace Sanduhr.Core;

/// <summary>Merged closed-day window across consented roots (Overview's
/// vault side). ByProjectName merges by display name — parity with the live
/// tab's basename merge.</summary>
public sealed record VaultWindow(
    Dictionary<DateOnly, long> ByDay,
    Dictionary<string, long> ByProjectName,
    Dictionary<string, long> BySkill);

/// <summary>One Trends bar. HasNoRecordGap = some day of the week (clamped to
/// today) is not covered by EVERY consented root — rendered as the "no record"
/// texture, never a zero-height bar (auto-start is off by default; a
/// widget-off fortnight must not read as a vacation).</summary>
public sealed record VaultWeek(DateOnly WeekStart, long Total, bool IsCurrent, bool HasNoRecordGap);

/// <summary>One logical session — primary + continuation slices merged.</summary>
public sealed record VaultSessionInfo(
    string Uuid,
    string Root,
    string ProjectKey,
    string ProjectName,
    string? Cwd,
    DateTimeOffset FirstTs,
    DateTimeOffset LastTs,
    long Total,
    Dictionary<string, long> ByModel,
    Dictionary<string, long>? BySkill,
    Dictionary<string, VaultDayBucket> ByDay,
    VaultCacheTokens? Cache);

/// <summary>
/// Read side of the vault. NEVER mutates: corrupt or missing shards degrade to
/// empty and are left for the next ingest cycle to quarantine. All aggregation
/// here is Core-tested so the App layer stays a projection.
/// </summary>
public sealed class VaultReader
{
    private readonly VaultStore _store;

    public VaultReader(VaultStore store) => _store = store;

    public static string ProjectNameOf(string projectKey)
    {
        int idx = projectKey.LastIndexOf('~');
        return idx > 0 ? projectKey[..idx] : projectKey;
    }

    public VaultWindow ReadWindow(IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toExclusive)
    {
        var byDay = new Dictionary<DateOnly, long>();
        var byProject = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();
        foreach (var root in roots)
        {
            foreach (var month in MonthsBetween(fromInclusive, toExclusive))
            {
                if (_store.TryLoadRollupShard(root, month, out var shard) != ShardLoadResult.Ok)
                    continue;
                foreach (var (dayKey, day) in shard.Days)
                {
                    if (!TryDay(dayKey, out var date) || date < fromInclusive || date >= toExclusive)
                        continue;
                    byDay[date] = byDay.GetValueOrDefault(date) + day.Total;
                    foreach (var (projectKey, v) in day.ByProject)
                    {
                        var name = ProjectNameOf(projectKey);
                        byProject[name] = byProject.GetValueOrDefault(name) + v;
                    }
                    foreach (var (skill, v) in day.BySkill)
                        bySkill[skill] = bySkill.GetValueOrDefault(skill) + v;
                }
            }
        }
        return new VaultWindow(byDay, byProject, bySkill);
    }

    public IReadOnlyList<VaultWeek> ReadWeeks(IReadOnlyList<string> roots, int weeks, DateOnly today)
    {
        var currentWeekStart = WeekStart(today);
        var firstWeekStart = currentWeekStart.AddDays(-7 * (weeks - 1));
        var window = ReadWindow(roots, firstWeekStart, today.AddDays(1));
        // One meta read per root per render — the per-day public IsDayCovered
        // would be weeks x 7 x roots file reads on a UI path.
        var metas = roots.Select(r => _store.LoadMeta(r)).ToList();

        var result = new List<VaultWeek>(weeks);
        for (int i = 0; i < weeks; i++)
        {
            var start = firstWeekStart.AddDays(7 * i);
            bool isCurrent = start == currentWeekStart;
            long total = 0;
            bool gap = false;
            for (int d = 0; d < 7; d++)
            {
                var day = start.AddDays(d);
                if (day > today)
                    break;
                total += window.ByDay.GetValueOrDefault(day);
                if (!IsDayCovered(metas, day))
                    gap = true;
            }
            result.Add(new VaultWeek(start, total, isCurrent, gap));
        }
        return result;
    }

    public IReadOnlyList<(string Name, long Total)> TopProjects(
        IReadOnlyList<string> roots, DateOnly fromInclusive, DateOnly toExclusive, int top)
        => ReadWindow(roots, fromInclusive, toExclusive).ByProjectName
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public IReadOnlyList<VaultSessionInfo> ReadSessions(IReadOnlyList<string> roots)
    {
        var result = new List<VaultSessionInfo>();
        foreach (var root in roots)
        {
            var byUuid = new Dictionary<string, List<VaultSessionRow>>();
            foreach (var month in _store.ListSessionShardMonths(root))
            {
                if (_store.TryLoadSessionShard(root, month, out var shard) != ShardLoadResult.Ok)
                    continue;   // read path never mutates — next ingest quarantines
                foreach (var (uuid, row) in shard.Sessions)
                {
                    if (!byUuid.TryGetValue(uuid, out var list))
                        byUuid[uuid] = list = new List<VaultSessionRow>();
                    list.Add(row);
                }
            }
            foreach (var (uuid, rows) in byUuid)
            {
                var merged = VaultRowMath.Merge(rows);
                if (!TryTs(merged.FirstTs, out var first) || !TryTs(merged.LastTs, out var last))
                    continue;
                result.Add(new VaultSessionInfo(
                    uuid, root, merged.ProjectKey, merged.ProjectName, merged.Cwd,
                    first, last, merged.Total, merged.ByModel, merged.BySkill,
                    merged.ByDay, merged.CacheTokens));
            }
        }
        return result;
    }

    public static long TokensInScope(VaultSessionInfo s, DateOnly fromInclusive, DateOnly toInclusive)
    {
        long total = 0;
        foreach (var (dayKey, bucket) in s.ByDay)
        {
            if (TryDay(dayKey, out var day) && day >= fromInclusive && day <= toInclusive)
                total += bucket.Total;
        }
        return total;
    }

    public DateOnly? BirthDate(IReadOnlyList<string> roots)
    {
        DateOnly? min = null;
        foreach (var root in roots)
        {
            if (_store.LoadMeta(root) is { } meta && TryDay(meta.Since, out var since))
            {
                if (min is null || since < min)
                    min = since;
            }
        }
        return min;
    }

    /// <summary>MIN of the consented roots' last successful ingest — any stale
    /// (or never-started) root makes the whole tab report degraded; frozen
    /// numbers with no signal would be the design lying.</summary>
    public DateTimeOffset? LastSuccessfulIngestUtc(IReadOnlyList<string> roots)
    {
        DateTimeOffset? min = null;
        foreach (var root in roots)
        {
            if (_store.LoadMeta(root) is not { } meta || !TryTs(meta.LastIngestTs, out var ts))
                return null;
            if (min is null || ts < min)
                min = ts;
        }
        return min;
    }

    /// <summary>Covered only when EVERY consented root's ranges contain the day.</summary>
    public bool IsDayCovered(IReadOnlyList<string> roots, DateOnly day)
        => IsDayCovered(roots.Count == 0
            ? new List<VaultRootMeta?>()
            : roots.Select(r => _store.LoadMeta(r)).ToList(), day);

    private static bool IsDayCovered(IReadOnlyList<VaultRootMeta?> metas, DateOnly day)
    {
        if (metas.Count == 0)
            return false;
        foreach (var meta in metas)
        {
            if (meta is null)
                return false;
            bool covered = false;
            foreach (var range in meta.Covered)
            {
                if (TryDay(range.From, out var from) && TryDay(range.To, out var to)
                    && day >= from && day <= to)
                {
                    covered = true;
                    break;
                }
            }
            if (!covered)
                return false;
        }
        return true;
    }

    internal static DateOnly WeekStart(DateOnly day)
    {
        int diff = ((int)day.DayOfWeek + 6) % 7;   // Monday = 0
        return day.AddDays(-diff);
    }

    private static IEnumerable<string> MonthsBetween(DateOnly fromInclusive, DateOnly toExclusive)
    {
        var cursor = new DateOnly(fromInclusive.Year, fromInclusive.Month, 1);
        var end = toExclusive.AddDays(-1);
        while (cursor <= end)
        {
            yield return cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            cursor = cursor.AddMonths(1);
        }
    }

    private static bool TryDay(string s, out DateOnly day)
        => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static bool TryTs(string s, out DateTimeOffset ts)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out ts);
}
```

- [ ] **Step 4: Implement `VaultLedgerCsv.cs`**

```csharp
using System.Globalization;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Session Ledger CSV builder — IO-free like <see cref="CsvExport"/>; the App
/// layer owns the save dialog and the write. One row per visible ledger row,
/// in the order given (the VM passes them pre-sorted by the active column).
/// </summary>
public static class VaultLedgerCsv
{
    public sealed record Row(
        string Uuid,
        string Root,
        string Project,
        string FirstTs,
        string LastTs,
        long TokensInScope,
        long TokensTotal,
        string Models);

    public static CsvExport.CsvBuildResult Build(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.Append("session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models\r\n");
        foreach (var r in rows)
        {
            Append(sb, r.Uuid); sb.Append(',');
            Append(sb, r.Root); sb.Append(',');
            Append(sb, r.Project); sb.Append(',');
            Append(sb, r.FirstTs); sb.Append(',');
            Append(sb, r.LastTs); sb.Append(',');
            sb.Append(r.TokensInScope.ToString(CultureInfo.InvariantCulture)); sb.Append(',');
            sb.Append(r.TokensTotal.ToString(CultureInfo.InvariantCulture)); sb.Append(',');
            Append(sb, r.Models);
            sb.Append("\r\n");
        }
        return new CsvExport.CsvBuildResult(sb.ToString(), rows.Count);
    }

    private static void Append(StringBuilder sb, string field) => sb.Append(CsvExport.Escape(field));
}
```

And in `CsvExport.cs`, change the escape helper's visibility (keep the XML doc):

```csharp
    internal static string Escape(string field)
```

- [ ] **Step 5: Implement `CcLogReader.AggregateTodayOnly`**

Add to `CcLogReader` (after `AggregateForLocalCcTab`), plus two cache fields next to the existing ones (`_todayCacheComputedAt`, `_todayCacheResult`) and clear them in `InvalidateCache`:

```csharp
    private DateTimeOffset _todayCacheComputedAt = DateTimeOffset.MinValue;
    private LocalCcAggregate? _todayCacheResult;

    /// <summary>Single-pass aggregate restricted to events whose LOCAL calendar
    /// date is today — the Overview's live-today source. The vault serves days
    /// strictly before today; this serves today; never both (the exclusion rule
    /// that prevents double-counting the vault's partial today row). Cached for
    /// <see cref="AggCacheTtlSec"/> seconds, independently of the 30-day cache.</summary>
    public LocalCcAggregate AggregateTodayOnly()
    {
        lock (_cacheLock)
        {
            if (_todayCacheResult is not null
                && (DateTimeOffset.UtcNow - _todayCacheComputedAt).TotalSeconds < AggCacheTtlSec)
            {
                return _todayCacheResult;
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        // Offset AT MIDNIGHT, not now — on the DST fall-back day the current
        // offset would place the cutoff an hour late and the mtime prefilter
        // could skip files last written between 00:00 and 01:00 local.
        var midnightLocal = today.ToDateTime(TimeOnly.MinValue);
        var midnightUtc = new DateTimeOffset(
            midnightLocal, TimeZoneInfo.Local.GetUtcOffset(midnightLocal)).ToUniversalTime();
        var byDay = new Dictionary<DateOnly, long>();
        var byProject = new Dictionary<string, long>();
        var bySkill = new Dictionary<string, long>();

        foreach (var path in DiscoverLogFiles())
        {
            if (!FileMtimeAfter(path, midnightUtc))
                continue;
            foreach (var ev in IterUsageEvents(path))
            {
                if (ev.Timestamp is null)
                    continue;
                var tokens = Tokens(ev);
                if (tokens <= 0)
                    continue;
                if (LocalDate(ev.Timestamp.Value) != today)
                    continue;
                byDay[today] = byDay.GetValueOrDefault(today) + tokens;
                if (!string.IsNullOrEmpty(ev.Cwd))
                    byProject[ev.Cwd] = byProject.GetValueOrDefault(ev.Cwd) + tokens;
                if (!string.IsNullOrEmpty(ev.AttributionSkill))
                    bySkill[ev.AttributionSkill] = bySkill.GetValueOrDefault(ev.AttributionSkill) + tokens;
            }
        }

        var result = new LocalCcAggregate(byDay, byProject, bySkill);
        lock (_cacheLock)
        {
            _todayCacheComputedAt = DateTimeOffset.UtcNow;
            _todayCacheResult = result;
        }
        return result;
    }
```

In `InvalidateCache`, add inside the lock:

```csharp
            _todayCacheComputedAt = DateTimeOffset.MinValue;
            _todayCacheResult = null;
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS — 401 + 15 new = 416.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/VaultReader.cs windows-dotnet/src/Sanduhr.Core/VaultLedgerCsv.cs windows-dotnet/src/Sanduhr.Core/CcLogReader.cs windows-dotnet/src/Sanduhr.Core/CsvExport.cs windows-dotnet/tests/Sanduhr.Tests/VaultReaderTests.cs windows-dotnet/tests/Sanduhr.Tests/VaultLedgerCsvTests.cs windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs
git commit -m "feat(vault): VaultReader window/weeks/sessions/coverage, today-only live aggregate, ledger CSV"
```

---

### Task 5: App plumbing — vault settings, `VaultService`, consent dialog, widget wiring, 30s-tick hygiene

The App test project cannot reference WPF code (by design) — App tasks verify by building and by the smoke steps listed at the end of each task. Follow the plan code EXACTLY; the reviewer diff is the only gate.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Services/VaultService.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml` + `.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs`

**Interfaces:**
- Consumes (Tasks 1–4): `Paths.VaultDir`, `VaultStore`, `VaultIngester.IngestOnce`, `VaultReader`, `CcLogReader.TierForModel`, `CcLogReader.SearchRoots`.
- Produces (Tasks 6–8 rely on these exact names):
  - `SettingsStore`: `bool LoadVaultPrompted()` / `SaveVaultPrompted(bool)`; `IReadOnlyDictionary<string,bool> LoadVaultRoots()` / `SaveVaultRoots(IReadOnlyDictionary<string,bool>)`; `bool LoadVaultStoreFullPaths()` (keys `vault_prompted` / `vault_roots` / `vault_store_full_paths`)
  - `VaultService` with: `VaultReader Reader`, `VaultStore Store`, `string VaultDir`, `IReadOnlyList<string> DetectedRootNames()`, `IReadOnlyList<string> ConsentedRootNames()`, `bool NeedsConsentPrompt`, `void SaveConsent(IReadOnlyDictionary<string,bool>)`, `void SetRootConsent(string root, bool on)`, `void TriggerIngest()`, `void PurgeRoot(string root)`, `void EraseArchive()`, `void OpenVaultFolder()`, `event Action? IngestCompleted` (raised on a worker thread — subscribers marshal to the dispatcher)
  - `WidgetViewModel`: `VaultService? Vault` + `AttachVaultService(VaultService)`
  - `VaultConsentDialog.ShowConsent(Window? owner, IReadOnlyList<string> rootNames)` → `IReadOnlyDictionary<string,bool>` (close/"Not now" ⇒ all false)
- 30s-tick hygiene (in scope per spec): the tick's TWO synchronous UI-thread `TokensSince` walks (`RefreshCcDelta` + `UpdateFooter`) collapse into ONE `Task.Run` walk feeding both badges and footer.

- [ ] **Step 1: SettingsStore vault keys**

Append to `windows-dotnet/src/Sanduhr.App/Services/SettingsStore.cs` (follow the file's try/catch-per-key idiom):

```csharp
    // -- WS-C usage vault (spec 2026-07-12-usage-vault-design.md) ---------------

    /// <summary>Whether the first-run per-root consent dialog has been shown.
    /// Consent is PROMPTED, never silent — a silently-archived employer root is
    /// the tenant breach the design review named.</summary>
    public bool LoadVaultPrompted()
    {
        var root = Read();
        try { return root["vault_prompted"]?.GetValue<bool>() ?? false; } catch { return false; }
    }

    public void SaveVaultPrompted(bool prompted)
    {
        var root = Read();
        root["vault_prompted"] = prompted;
        Write(root);
    }

    /// <summary>Per-root vault consent ({".claude": true, ...}). A root absent
    /// from the map is NOT consented. Consent-off is the erasure tombstone —
    /// purge flips it so the next cycle can't re-backfill.</summary>
    public IReadOnlyDictionary<string, bool> LoadVaultRoots()
    {
        var root = Read();
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (root["vault_roots"] is JsonObject map)
        {
            foreach (var (name, node) in map)
            {
                try { result[name] = node?.GetValue<bool>() ?? false; }
                catch { result[name] = false; }
            }
        }
        return result;
    }

    public void SaveVaultRoots(IReadOnlyDictionary<string, bool> roots)
    {
        var root = Read();
        var map = new JsonObject();
        foreach (var (name, on) in roots)
            map[name] = on;
        root["vault_roots"] = map;
        Write(root);
    }

    /// <summary>Hidden setting (no UI): store full cwd paths in session rows.
    /// Off by default — the vault stores basename + hash only.</summary>
    public bool LoadVaultStoreFullPaths()
    {
        var root = Read();
        try { return root["vault_store_full_paths"]?.GetValue<bool>() ?? false; } catch { return false; }
    }
```

- [ ] **Step 2: `VaultService`**

Create `windows-dotnet/src/Sanduhr.App/Services/VaultService.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// App-side owner of the usage vault: consent state (settings.json), the
/// fire-and-forget ingest trigger (Interlocked single-flight — a still-running
/// cycle means this cycle skips; cross-PROCESS exclusion is the ingester's
/// named mutex), and the stewardship verbs (purge / erase / open folder).
/// Purge flips consent OFF first — consent is the tombstone; deleting the
/// folder alone is false erasure while the app runs (it re-backfills within a
/// cycle).
/// </summary>
public sealed class VaultService
{
    private readonly Paths _paths;
    private readonly SettingsStore _settings;
    private readonly CcLogReader _reader;
    private readonly VaultStore _store;
    private readonly VaultIngester _ingester;
    private int _ingestRunning;

    /// <summary>Raised after a completed ingest cycle, ON A WORKER THREAD —
    /// UI subscribers must marshal via their Dispatcher.</summary>
    public event Action? IngestCompleted;

    public VaultService(SettingsStore settings, CcLogReader reader)
    {
        _paths = new Paths();
        _settings = settings;
        _reader = reader;
        _store = new VaultStore(_paths.VaultDir, _paths.LogFile);
        Reader = new VaultReader(_store);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var version = v is null ? "3.2.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        _ingester = new VaultIngester(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _store, version, _paths.LogFile);
    }

    public VaultReader Reader { get; }

    public VaultStore Store => _store;

    public string VaultDir => _paths.VaultDir;

    /// <summary>Basenames (".claude", ".claude-personal") of CC homes that
    /// exist on this machine right now.</summary>
    public IReadOnlyList<string> DetectedRootNames()
        => _reader.SearchRoots().Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!).ToList();

    /// <summary>Detected ∩ consented — the only roots the ingester ever touches.</summary>
    public IReadOnlyList<string> ConsentedRootNames()
    {
        var consent = _settings.LoadVaultRoots();
        return DetectedRootNames().Where(r => consent.GetValueOrDefault(r)).ToList();
    }

    public bool NeedsConsentPrompt
        => !_settings.LoadVaultPrompted() && DetectedRootNames().Count > 0;

    public void SaveConsent(IReadOnlyDictionary<string, bool> roots)
    {
        _settings.SaveVaultRoots(roots);
        _settings.SaveVaultPrompted(true);
    }

    public void SetRootConsent(string root, bool on)
    {
        var map = new Dictionary<string, bool>(_settings.LoadVaultRoots(), StringComparer.Ordinal)
        {
            [root] = on,
        };
        _settings.SaveVaultRoots(map);
    }

    /// <summary>Fire-and-forget, single-flight. Never awaited anywhere in the
    /// fetch loop (the WS-B EvaluateAlerts call is synchronous-cheap — it is
    /// explicitly NOT the template here).</summary>
    public void TriggerIngest()
    {
        if (Interlocked.CompareExchange(ref _ingestRunning, 1, 0) != 0)
            return;   // previous run still going -> skip this cycle
        var roots = ConsentedRootNames();
        bool fullPaths = _settings.LoadVaultStoreFullPaths();
        if (roots.Count == 0)
        {
            Interlocked.Exchange(ref _ingestRunning, 0);
            return;
        }
        _ = Task.Run(() =>
        {
            try
            {
                _ingester.IngestOnce(roots, fullPaths, DateTimeOffset.UtcNow);
                IngestCompleted?.Invoke();
            }
            catch (Exception e)
            {
                LogBestEffort("ingest", e);
            }
            finally
            {
                Interlocked.Exchange(ref _ingestRunning, 0);
            }
        });
    }

    /// <summary>Consent off (tombstone) THEN folder delete. Order matters: the
    /// reverse would let an in-flight cycle re-create the folder.</summary>
    public void PurgeRoot(string root)
    {
        SetRootConsent(root, false);
        _store.PurgeRoot(root);
    }

    public void EraseArchive()
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var root in DetectedRootNames())
            map[root] = false;
        _settings.SaveVaultRoots(map);
        _store.PurgeAll();
    }

    public void OpenVaultFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.VaultDir);
            Process.Start(new ProcessStartInfo("explorer.exe", _paths.VaultDir) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            LogBestEffort("open-folder", e);
        }
    }

    // PRIVACY.md contract: operation + exception type only.
    private void LogBestEffort(string operation, Exception e)
    {
        try
        {
            File.AppendAllText(_paths.LogFile,
                $"{DateTime.UtcNow:o} vault {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
        }
    }
}
```

- [ ] **Step 3: Consent dialog**

Create `windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml` (themed like `ThemedDialog` — borderless glass, `Sanduhr.Brush.*` everywhere; the checkbox style is a local copy of SettingsWindow's `ThemedCheckBox`, which is window-scoped and not reachable from here):

```xml
<Window x:Class="Sanduhr.App.Views.VaultConsentDialog"
        x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Sanduhr" Width="420" SizeToContent="Height"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False" ResizeMode="NoResize">
    <Window.Resources>
        <Style x:Key="ConsentCheckBox" TargetType="CheckBox">
            <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Text}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="Margin" Value="0,0,0,6" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="CheckBox">
                        <StackPanel Orientation="Horizontal">
                            <Border x:Name="box" Width="16" Height="16" CornerRadius="3"
                                    VerticalAlignment="Center" Margin="0,0,8,0"
                                    Background="{DynamicResource Sanduhr.Brush.Bg}"
                                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                <Path x:Name="check" Stretch="Uniform" Margin="2"
                                      Data="M0,5 L4,9 L11,0" Visibility="Collapsed"
                                      Stroke="{DynamicResource Sanduhr.Brush.Accent}" StrokeThickness="2" />
                            </Border>
                            <ContentPresenter VerticalAlignment="Center" />
                        </StackPanel>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="check" Property="Visibility" Value="Visible" />
                            </Trigger>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="box" Property="BorderBrush"
                                        Value="{DynamicResource Sanduhr.Brush.Accent}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Border CornerRadius="10" Background="{DynamicResource Sanduhr.Brush.Bg}"
            BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1" Padding="20">
        <StackPanel>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                <Border Width="10" Height="10" CornerRadius="5" VerticalAlignment="Center"
                        Margin="0,0,10,0" Background="{DynamicResource Sanduhr.Brush.Accent}" />
                <TextBlock Text="Keep a local usage history?" FontSize="14" FontWeight="SemiBold"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" />
            </StackPanel>
            <TextBlock TextWrapping="Wrap" FontSize="12" Margin="0,0,0,12"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                       Text="Claude Code deletes its session logs after about 30 days. Sanduhr can keep a local history vault of your usage totals so your trends survive — stored only on this machine, never uploaded. Choose which Claude Code homes to include; you can change this or erase everything any time in Settings &#x25B8; Claude Code." />
            <StackPanel x:Name="RootsPanel" Margin="0,0,0,14" />
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="NotNowButton" Content="Not now" MinWidth="80" Height="30"
                        Margin="0,0,8,0" Cursor="Hand"
                        Foreground="{DynamicResource Sanduhr.Brush.Text}"
                        Background="{DynamicResource Sanduhr.Brush.Glass}"
                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                        Click="OnNotNowClick" />
                <Button x:Name="KeepButton" Content="Keep history" MinWidth="100" Height="30" Cursor="Hand"
                        Foreground="{DynamicResource Sanduhr.Brush.Bg}"
                        Background="{DynamicResource Sanduhr.Brush.Accent}"
                        BorderThickness="0"
                        Click="OnKeepClick" />
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

Create `windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Services;

namespace Sanduhr.App.Views;

/// <summary>
/// First-vault-run per-root consent (spec: Ingestion — "pre-checked, but
/// PROMPTED; silent-on for an employer root is the breach WS-E's review
/// named"). Returns a consent map covering every detected root; closing or
/// "Not now" returns all-false. Either way the caller marks vault_prompted so
/// the dialog shows once — the Claude Code tab owns changes afterwards.
/// </summary>
internal partial class VaultConsentDialog : Window
{
    private readonly Dictionary<string, CheckBox> _checkboxes = new(StringComparer.Ordinal);
    private bool _keep;

    private VaultConsentDialog(IReadOnlyList<string> rootNames)
    {
        InitializeComponent();
        foreach (var root in rootNames)
        {
            var cb = new CheckBox
            {
                Content = root,
                IsChecked = true,   // pre-checked; the prompt itself is the consent gate
                Style = (Style)FindResource("ConsentCheckBox"),
            };
            _checkboxes[root] = cb;
            RootsPanel.Children.Add(cb);
        }
        Loaded += (_, _) => Sounds.PlayInfo();
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        _keep = true;
        DialogResult = true;
        Close();
    }

    private void OnNotNowClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static IReadOnlyDictionary<string, bool> ShowConsent(
        Window? owner, IReadOnlyList<string> rootNames)
    {
        var dlg = new VaultConsentDialog(rootNames);
        if (owner is not null && owner.IsLoaded)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();

        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (root, cb) in dlg._checkboxes)
            result[root] = dlg._keep && cb.IsChecked == true;
        return result;
    }
}
```

- [ ] **Step 4: `WidgetViewModel` — vault trigger + tick hygiene**

In `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`:

(a) Fields + attach, next to the AlertService pair:

```csharp
    private VaultService? _vault;
    private DateOnly _lastTickDate = DateOnly.FromDateTime(DateTime.Now);
    private long _ccDeltaTotal;
    private bool _ccScanRunning;

    /// <summary>The usage-vault service, attached by App AFTER the consent
    /// prompt resolves (never before — attach order IS the consent gate).
    /// Null in unit contexts.</summary>
    public VaultService? Vault => _vault;

    public void AttachVaultService(VaultService service) => _vault = service;
```

(b) At the very top of `RefreshAsync()` — BEFORE the `_fetcher is null` gate, so signed-out users still archive:

```csharp
        // Vault ingest rides the 5-min cycle: fire-and-forget, single-flight,
        // never awaited (spec: Ingestion). Runs even when signed out — the
        // Local CC surfaces work without an account.
        _vault?.TriggerIngest();
```

(c) In `RefreshAsync`'s success path, replace the existing two-line "Fresh fetch re-anchors…" comment AND the `RefreshCcDelta();` call beneath it (three lines total) with:

```csharp
            // Fresh fetch re-anchors the Local CC delta window: badges/footer
            // reset to ~0 now; the async scan grows them between fetches.
            _ccDeltaTotal = 0;
            foreach (var vm in Tiers)
                vm.SetLocalDelta(0);
            _ = RefreshCcDerivedAsync();
```

(d) Replace `OnTick()` and DELETE `RefreshCcDelta()` entirely:

```csharp
    private void OnTick()
    {
        // Day rollover: trigger an immediate ingest so yesterday's number
        // closes within ~30s of midnight (hot-day rule), not at the next
        // 5-minute fetch.
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _lastTickDate)
        {
            _lastTickDate = today;
            _vault?.TriggerIngest();
        }

        if (_lastData is null)
            return;
        var now = DateTimeOffset.UtcNow;
        foreach (var vm in Tiers)
            vm.Tick(now);
        _ = RefreshCcDerivedAsync();
    }

    /// <summary>ONE shared TokensSince walk feeding BOTH the per-tier badges
    /// and the footer's "CC +Nk" (the tick previously ran two synchronous
    /// walks on the UI thread). Off the UI thread via Task.Run; the await
    /// continuation returns to the dispatcher (DispatcherTimer ticks run on
    /// the UI thread), so the property/collection writes stay UI-safe.</summary>
    private async Task RefreshCcDerivedAsync()
    {
        if (_lastFetchAt is null || _ccScanRunning)
            return;
        _ccScanRunning = true;
        try
        {
            var anchor = _lastFetchAt.Value;
            Dictionary<string, long> byModel;
            try
            {
                byModel = await Task.Run(() => _ccReader.TokensSince(anchor)).ConfigureAwait(true);
            }
            catch
            {
                byModel = new Dictionary<string, long>();
            }
            long total = 0;
            var byTier = new Dictionary<string, long>();
            foreach (var (model, tokens) in byModel)
            {
                total += tokens;
                var tier = CcLogReader.TierForModel(model);
                if (tier is not null)
                    byTier[tier] = byTier.GetValueOrDefault(tier) + tokens;
            }
            foreach (var vm in Tiers)
                vm.SetLocalDelta(byTier.GetValueOrDefault(vm.TierKey));
            _ccDeltaTotal = total;
            UpdateFooter();
        }
        finally
        {
            _ccScanRunning = false;
        }
    }
```

(e) In `UpdateFooter()`, replace the whole `string cc = ""; try { ... } catch { ... }` block with:

```csharp
        // The CC delta is computed by RefreshCcDerivedAsync's single shared
        // walk — the footer never does its own file IO anymore.
        string cc = _ccDeltaTotal > 0 ? $"  ·  CC +{TokenFormat.Compact(_ccDeltaTotal)}" : "";
```

- [ ] **Step 5: App startup wiring**

In `windows-dotnet/src/Sanduhr.App/App.xaml.cs`, `OnStartup`, insert between `SetupTray();` and `_vm.Start();`:

```csharp
        // Usage vault (WS-C): consent resolves BEFORE the service attaches —
        // attach order is the consent gate; Start()'s first fetch cycle then
        // kicks the initial backfill through the attached service.
        var vaultService = new VaultService(new SettingsStore(new Sanduhr.Core.Paths()), _vm.CcReader);
        if (vaultService.NeedsConsentPrompt)
            vaultService.SaveConsent(VaultConsentDialog.ShowConsent(_window, vaultService.DetectedRootNames()));
        _vm.AttachVaultService(vaultService);
```

- [ ] **Step 6: Build + verify**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
```

Expected: build 0 errors; 416 tests still green. Manual spot-check (report result, don't block): run the Debug exe with a throwaway `%APPDATA%` untouched — the consent dialog appears once, `%LOCALAPPDATA%\Sanduhr\vault\.claude*\sessions-*.json` appear within ~1 min of Keep history, and a second launch never re-prompts.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App
git commit -m "feat(vault): consent-gated VaultService wiring, first-run dialog, single-walk 30s tick"
```

---

### Task 6: "Claude Code" tab shell + Overview rework + data stewardship

Renames the Local CC tab, adds the Overview / Trends / Sessions sub-nav (Trends and Sessions are empty placeholders until Tasks 7–8), and rebuilds Overview on the vault: closed days from rollups, today (and any hot day) from the live reader, degraded fallback, honest copy, and the stewardship strip.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs`
- Modify (rewrite): `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` + `.xaml.cs`

**Interfaces:**
- Consumes (Tasks 4–5): `VaultService` (`Reader`, `ConsentedRootNames`, `DetectedRootNames`, `SetRootConsent`, `PurgeRoot`, `EraseArchive`, `OpenVaultFolder`, `TriggerIngest`, `IngestCompleted`), `VaultReader.ReadWindow/LastSuccessfulIngestUtc`, `CcLogReader.AggregateForLocalCcTab/AggregateTodayOnly/ProjectDisplayName`, `WidgetViewModel.Vault`.
- Produces (Tasks 7–8 rely on these exact names):
  - `ClaudeCodeTabViewModel` with `LocalCcViewModel Overview`, `string Section` (`"Overview" | "Trends" | "Sessions"`, observable, default `"Overview"`), `SetOverviewCommand` / `SetTrendsCommand` / `SetSessionsCommand`, `Task RefreshActiveAsync()`, `void Attach()` / `void Detach()` (IngestCompleted subscribe/unsubscribe with dispatcher marshal), `bool IsTabActive` (plain property, set by the window)
  - `LocalCcViewModel` keeps: `Changed` event, `ByDay`, `Palette`, `TodayText`, `MonthText`, `ShowBreakdowns`, `Projects`, `Skills`, `RefreshAsync()`; gains `StatusLine`, `Roots` (ObservableCollection of `VaultRootToggleViewModel { string Name; bool IsEnabled }`), `AttachOwner(Window)`, `EraseArchiveCommand`, `OpenVaultFolderCommand`
  - `SettingsViewModel.ClaudeCode` replaces `SettingsViewModel.LocalCc` (the `LocalCc` property is REMOVED; window code-behind references update in this task)
- **Overview sourcing rule (binding):** vault serves days strictly before the hot boundary; the live reader serves hot days; NEVER both for the same day. Hot boundary = local date of the last successful ingest. Degraded (no consented roots' ingest within 15 minutes = 3 fetch cycles) ⇒ the whole 30-day window falls back to `AggregateForLocalCcTab` — today's shipping behavior — with the status line *"history vault paused — showing live logs only"*. Vault off entirely (no consented roots) ⇒ live path, empty status line (the stewardship checkboxes are the signal). Known 30-second residual: right after midnight, yesterday's BREAKDOWN contribution is briefly ABSENT (the vault window stops before the hot boundary and the today-only live slice doesn't cover yesterday) until the rollover ingest lands — day TOTALS stay exact via the live per-day merge. Accepted; matches the spec's "recovers at 00:04" bound.

- [ ] **Step 1: `ClaudeCodeTabViewModel`**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs`:

```csharp
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Parent of the Settings → Claude Code tab: owns the Overview / Trends /
/// Sessions sub-nav state and fans refreshes to the active section. Sections
/// are added as they land (Task 6: Overview; Task 7: Trends; Task 8: Sessions).
/// </summary>
public sealed partial class ClaudeCodeTabViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action _ingestHandler;

    public LocalCcViewModel Overview { get; }

    /// <summary>Set by the window on tab selection — ingest-completed refreshes
    /// only run while the user can see the tab.</summary>
    public bool IsTabActive { get; set; }

    [ObservableProperty] private string _section = "Overview";

    public ClaudeCodeTabViewModel(WidgetViewModel widget, LocalCcViewModel overview)
    {
        _widget = widget;
        Overview = overview;
        _ingestHandler = () => Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (IsTabActive)
                    await RefreshActiveAsync();
            }
            catch
            {
                // A refresh fault must never become an unhandled dispatcher
                // exception (global constraint: every UI path caught).
            }
        });
    }

    /// <summary>Subscribe to ingest completions (worker thread → dispatcher).
    /// The window calls Detach on close — the VaultService outlives every
    /// Settings window, so a missed unsubscribe is a VM leak.</summary>
    public void Attach()
    {
        if (_widget.Vault is { } vault)
            vault.IngestCompleted += _ingestHandler;
    }

    public void Detach()
    {
        if (_widget.Vault is { } vault)
            vault.IngestCompleted -= _ingestHandler;
    }

    public async Task RefreshActiveAsync()
    {
        switch (Section)
        {
            case "Overview":
                await Overview.RefreshAsync();
                break;
            // Task 7 adds: case "Trends": await Trends.RefreshAsync(); break;
            // Task 8 adds: case "Sessions": await Ledger.RefreshAsync(); break;
        }
    }

    [RelayCommand]
    private async Task SetOverview()
    {
        Section = "Overview";
        await RefreshActiveAsync();
    }

    [RelayCommand]
    private async Task SetTrends()
    {
        Section = "Trends";
        await RefreshActiveAsync();
    }

    [RelayCommand]
    private async Task SetSessions()
    {
        Section = "Sessions";
        await RefreshActiveAsync();
    }
}
```

- [ ] **Step 2: Rewrite `LocalCcViewModel`**

Replace the ENTIRE contents of `windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs` with the following (the block includes `BreakdownRow`, unchanged — nothing from the old file survives):

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row in the breakdown tables — display name + compact tokens.</summary>
public sealed record BreakdownRow(string Name, string Tokens);

/// <summary>A per-root vault consent toggle in the stewardship strip.</summary>
public sealed partial class VaultRootToggleViewModel : ObservableObject
{
    private readonly LocalCcViewModel _owner;
    private bool _loading = true;

    public string Name { get; }

    [ObservableProperty] private bool _isEnabled;

    public VaultRootToggleViewModel(LocalCcViewModel owner, string name, bool enabled)
    {
        _owner = owner;
        Name = name;
        IsEnabled = enabled;
        _loading = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading)
            _owner.OnRootToggled(Name, value);
    }
}

/// <summary>
/// Overview section of the Claude Code tab. Sourcing (spec, binding): the
/// vault's rollups serve days strictly BEFORE the hot boundary (local date of
/// the last successful ingest); the live reader serves hot days — never both
/// for one day. A stale vault (no ingest for 3 cycles) degrades the whole
/// window to the live path with a visible status line: frozen closed-day
/// numbers with no signal would be the design lying. Also owns the data-
/// stewardship strip — consent toggles, per-root purge, erase, open folder.
/// </summary>
public sealed partial class LocalCcViewModel : ObservableObject
{
    private const int LookbackDays = 30;
    private static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(15);   // 3 fetch cycles

    private readonly WidgetViewModel _widget;
    private readonly Action<bool> _persistShowBreakdowns;
    private Window? _owner;

    public event Action? Changed;

    [ObservableProperty] private string _todayText = "Loading…";
    [ObservableProperty] private string _monthText = "Loading…";
    [ObservableProperty] private bool _showBreakdowns;
    [ObservableProperty] private string _statusLine = "";

    public ObservableCollection<BreakdownRow> Projects { get; } = new();
    public ObservableCollection<BreakdownRow> Skills { get; } = new();
    public ObservableCollection<VaultRootToggleViewModel> Roots { get; } = new();

    private Dictionary<DateOnly, long> _byDay = new();
    public IReadOnlyDictionary<DateOnly, long> ByDay => _byDay;

    public ThemePalette Palette => _widget.Palette;

    public LocalCcViewModel(WidgetViewModel widget, bool showBreakdowns, Action<bool> persistShowBreakdowns)
    {
        _widget = widget;
        _showBreakdowns = showBreakdowns;
        _persistShowBreakdowns = persistShowBreakdowns;
        _widget.ThemeChanged += _ => Changed?.Invoke();
        RebuildRoots();
    }

    partial void OnShowBreakdownsChanged(bool value) => _persistShowBreakdowns(value);

    public void AttachOwner(Window owner) => _owner = owner;

    private void RebuildRoots()
    {
        Roots.Clear();
        if (_widget.Vault is not { } vault)
            return;
        var consented = new HashSet<string>(vault.ConsentedRootNames(), StringComparer.Ordinal);
        foreach (var root in vault.DetectedRootNames())
            Roots.Add(new VaultRootToggleViewModel(this, root, consented.Contains(root)));
    }

    /// <summary>Consent toggle: ON resumes archiving (backfill within one
    /// cycle); OFF stops it and OFFERS purge — consent-off is the tombstone
    /// either way, so "off" alone never silently re-backfills.</summary>
    internal void OnRootToggled(string root, bool on)
    {
        if (_widget.Vault is not { } vault)
            return;
        if (on)
        {
            vault.SetRootConsent(root, true);
            vault.TriggerIngest();
        }
        else
        {
            vault.SetRootConsent(root, false);
            var res = ThemedDialog.Show(_owner, $"Stop archiving {root}?",
                "New usage from this home will no longer be recorded. Also erase the history already stored for it?",
                MessageBoxButton.YesNo, ThemedDialogKind.Warning);
            if (res == MessageBoxResult.Yes)
                vault.PurgeRoot(root);
        }
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void OpenVaultFolder() => _widget.Vault?.OpenVaultFolder();

    [RelayCommand]
    private async Task EraseArchive()
    {
        if (_widget.Vault is not { } vault)
            return;
        var res = ThemedDialog.Show(_owner, "Erase usage history?",
            "Deletes the entire local vault and turns archiving off for every home. This cannot be undone.",
            MessageBoxButton.YesNo, ThemedDialogKind.Warning);
        if (res != MessageBoxResult.Yes)
            return;
        vault.EraseArchive();
        RebuildRoots();
        await RefreshAsync();
    }

    private sealed record OverviewData(
        Dictionary<DateOnly, long> ByDay,
        Dictionary<string, long> Projects,
        Dictionary<string, long> Skills,
        string StatusLine);

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        var reader = _widget.CcReader;
        OverviewData data;
        try
        {
            data = await Task.Run(() => Compute(vault, reader)).ConfigureAwait(true);
        }
        catch
        {
            data = new OverviewData(new(), new(), new(), "");
        }
        Apply(data);
    }

    private static OverviewData Compute(VaultService? vault, CcLogReader reader)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(LookbackDays - 1));
        var roots = vault?.ConsentedRootNames() ?? (IReadOnlyList<string>)Array.Empty<string>();
        var lastIngest = roots.Count > 0 ? vault!.Reader.LastSuccessfulIngestUtc(roots) : null;
        bool vaultOn = roots.Count > 0;
        bool degraded = vaultOn
            && (lastIngest is null || DateTimeOffset.UtcNow - lastIngest.Value > DegradedAfter);

        if (!vaultOn || degraded)
        {
            var agg = reader.AggregateForLocalCcTab(LookbackDays);
            var byName = new Dictionary<string, long>();
            foreach (var (cwd, v) in agg.ByProject)
            {
                var name = CcLogReader.ProjectDisplayName(cwd);
                byName[name] = byName.GetValueOrDefault(name) + v;
            }
            return new OverviewData(
                new Dictionary<DateOnly, long>(agg.ByDay),
                byName,
                new Dictionary<string, long>(agg.BySkill),
                degraded ? "history vault paused — showing live logs only" : "");
        }

        // Hot boundary: any day the vault hasn't confirmed since its midnight
        // serves live. With a fresh ingest that's just today; in the seconds
        // after midnight it's yesterday+today until the rollover ingest lands.
        var hotStart = DateOnly.FromDateTime(lastIngest!.Value.ToLocalTime().DateTime);
        var live = reader.AggregateForLocalCcTab(LookbackDays);
        var todayAgg = reader.AggregateTodayOnly();
        var win = vault!.Reader.ReadWindow(roots, windowStart, hotStart);

        var byDay = new Dictionary<DateOnly, long>(win.ByDay);
        for (var d = hotStart; d <= today; d = d.AddDays(1))
        {
            byDay.Remove(d);   // exclusion rule: a hot day never reads from both
            if (live.ByDay.TryGetValue(d, out var v))
                byDay[d] = v;
        }

        var projects = new Dictionary<string, long>(win.ByProjectName);
        foreach (var (cwd, v) in todayAgg.ByProject)
        {
            var name = CcLogReader.ProjectDisplayName(cwd);
            projects[name] = projects.GetValueOrDefault(name) + v;
        }
        var skills = new Dictionary<string, long>(win.BySkill);
        foreach (var (skill, v) in todayAgg.BySkill)
            skills[skill] = skills.GetValueOrDefault(skill) + v;

        return new OverviewData(byDay, projects, skills, "");
    }

    private void Apply(OverviewData data)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        long todayTotal = data.ByDay.GetValueOrDefault(today);
        TodayText = todayTotal > 0 ? $"{TokenFormat.Compact(todayTotal)} tokens" : "No activity yet";

        long monthTotal = data.ByDay.Values.Sum();
        MonthText = monthTotal > 0 ? $"{TokenFormat.Compact(monthTotal)} tokens" : "No activity";

        StatusLine = data.StatusLine;
        _byDay = data.ByDay;

        Projects.Clear();
        foreach (var (name, tokens) in data.Projects.OrderByDescending(kv => kv.Value).Take(10))
            Projects.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Skills.Clear();
        foreach (var (name, tokens) in data.Skills.OrderByDescending(kv => kv.Value).Take(10))
            Skills.Add(new BreakdownRow(name, TokenFormat.Compact(tokens)));

        Changed?.Invoke();
    }
}
```

- [ ] **Step 3: `SettingsViewModel` composition**

In `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, replace the `LocalCc` property and its construction:

```csharp
    /// <summary>Backs the Claude Code tab (WS-C): Overview / Trends / Sessions
    /// over the usage vault + live reader.</summary>
    public ClaudeCodeTabViewModel ClaudeCode { get; }
```

and in the ctor, replace the `LocalCc = new LocalCcViewModel(...)` assignment with:

```csharp
        ClaudeCode = new ClaudeCodeTabViewModel(widget, new LocalCcViewModel(
            widget, widget.LoadLocalCcShowBreakdowns(), widget.SaveLocalCcShowBreakdowns));
```

- [ ] **Step 4: XAML — sub-nav styles + tab restructure**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml`:

(a) After the `RangePillMonth` style, add the sub-nav pill + section-visibility styles:

```xml
        <!-- Claude Code sub-nav pills (Overview / Trends / Sessions) — same
             accent-fill-when-active grammar as the History RangePills. -->
        <Style x:Key="CcNavOverview" TargetType="Button" BasedOn="{StaticResource FlatButton}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding Section}" Value="Overview">
                    <Setter Property="Background" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                    <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Bg}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="CcNavTrends" TargetType="Button" BasedOn="{StaticResource FlatButton}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding Section}" Value="Trends">
                    <Setter Property="Background" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                    <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Bg}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="CcNavSessions" TargetType="Button" BasedOn="{StaticResource FlatButton}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding Section}" Value="Sessions">
                    <Setter Property="Background" Value="{DynamicResource Sanduhr.Brush.Accent}" />
                    <Setter Property="Foreground" Value="{DynamicResource Sanduhr.Brush.Bg}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <!-- CRITICAL: each section Grid overrides its own DataContext to the
             section VM (Overview/Trends/Ledger), none of which has a Section
             property — a plain {Binding Section} DataTrigger would silently
             never fire and every section would stay Collapsed. The trigger
             must reach the tab VM through the ancestor TabItem. -->
        <Style x:Key="CcSectionOverview" TargetType="Grid">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.Section, RelativeSource={RelativeSource AncestorType=TabItem}}" Value="Overview">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="CcSectionTrends" TargetType="Grid">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.Section, RelativeSource={RelativeSource AncestorType=TabItem}}" Value="Trends">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="CcSectionSessions" TargetType="Grid">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding DataContext.Section, RelativeSource={RelativeSource AncestorType=TabItem}}" Value="Sessions">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
```

(b) Replace the entire `<TabItem Header="Local CC">…</TabItem>` block with:

```xml
                <!-- Claude Code tab (WS-C): Overview / Trends / Sessions over
                     the usage vault + live session-log reader. -->
                <TabItem Header="Claude Code">
                    <Border Background="{DynamicResource Sanduhr.Brush.Glass}" CornerRadius="0,8,8,8" Padding="16"
                            DataContext="{Binding ClaudeCode}">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="*" />
                            </Grid.RowDefinitions>

                            <!-- Sub-nav -->
                            <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,10">
                                <Button Content="Overview" Style="{StaticResource CcNavOverview}"
                                        Command="{Binding SetOverviewCommand}" />
                                <Button Content="Trends" Style="{StaticResource CcNavTrends}"
                                        Command="{Binding SetTrendsCommand}" />
                                <Button Content="Sessions" Style="{StaticResource CcNavSessions}"
                                        Command="{Binding SetSessionsCommand}" />
                            </StackPanel>

                            <!-- Overview -->
                            <Grid Grid.Row="1" Style="{StaticResource CcSectionOverview}"
                                  DataContext="{Binding Overview}">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="*" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>

                                <TextBlock Grid.Row="0" TextWrapping="Wrap" FontSize="11"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,8"
                                           Text="Claude Code deletes session logs after ~30 days. Sanduhr keeps a local history vault so your trends survive — never uploaded, per-home opt-in, erase any time below." />

                                <TextBlock Grid.Row="1" FontSize="11" Margin="0,0,0,8"
                                           Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}"
                                           Text="{Binding StatusLine}"
                                           Visibility="{Binding StatusLine, Converter={StaticResource StrVis}}" />

                                <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,12">
                                    <StackPanel Margin="0,0,32,0">
                                        <TextBlock Text="Today" FontSize="9"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                        <TextBlock Text="{Binding TodayText}" FontSize="22" FontWeight="Bold"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    </StackPanel>
                                    <StackPanel>
                                        <TextBlock Text="Last 30 days" FontSize="9"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                        <TextBlock Text="{Binding MonthText}" FontSize="22" FontWeight="Bold"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    </StackPanel>
                                </StackPanel>

                                <views:LocalCcBarStrip x:Name="BarStrip" Grid.Row="3" Height="60" Margin="0,0,0,8" />

                                <CheckBox Grid.Row="4" Style="{StaticResource ThemedCheckBox}"
                                          Content="Show project &amp; skill breakdown"
                                          IsChecked="{Binding ShowBreakdowns, Mode=TwoWay}" Margin="0,0,0,8" />

                                <!-- Breakdown tables: UNCHANGED from the old Local CC tab — copy the
                                     existing two-column Projects/Skills Grid here verbatim, bound to
                                     the same Projects/Skills collections and ShowBreakdowns visibility. -->

                                <!-- Data stewardship (spec: WS-A delete-completeness precedent). -->
                                <StackPanel Grid.Row="6" Margin="0,10,0,0">
                                    <TextBlock Text="History vault" FontSize="9" Margin="0,0,0,4"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                    <ItemsControl ItemsSource="{Binding Roots}">
                                        <ItemsControl.ItemsPanel>
                                            <ItemsPanelTemplate>
                                                <WrapPanel Orientation="Horizontal" />
                                            </ItemsPanelTemplate>
                                        </ItemsControl.ItemsPanel>
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <CheckBox Style="{StaticResource ThemedCheckBox}" Margin="0,0,16,4"
                                                          Content="{Binding Name}"
                                                          IsChecked="{Binding IsEnabled, Mode=TwoWay}" />
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                                        <Button Style="{StaticResource FlatButton}" Content="Erase archive"
                                                Command="{Binding EraseArchiveCommand}"
                                                Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                                        <Button Style="{StaticResource FlatButton}" Content="Open vault folder"
                                                Command="{Binding OpenVaultFolderCommand}" />
                                    </StackPanel>
                                </StackPanel>
                            </Grid>

                            <!-- Trends (content lands in Task 7) -->
                            <Grid Grid.Row="1" Style="{StaticResource CcSectionTrends}" />

                            <!-- Sessions (content lands in Task 8) -->
                            <Grid Grid.Row="1" Style="{StaticResource CcSectionSessions}" />
                        </Grid>
                    </Border>
                </TabItem>
```

Move the OLD breakdown-tables Grid (the two side-by-side DockPanels) into row 5 of the Overview grid where the placeholder comment sits, changing only `Grid.Row="4"` → `Grid.Row="5"`. The `StrVis` converter key used above already exists in the window resources (`StringToVisibilityConverter`, non-empty → Visible, registered near the top of SettingsWindow.xaml) — Tasks 7 and 8 use the same key; register nothing new.

- [ ] **Step 5: Code-behind updates**

In `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs`:

- Ctor: `_localCcTimer.Tick` becomes:

```csharp
        _localCcTimer.Tick += async (_, _) =>
        {
            if (ViewModel.ClaudeCode.Section == "Overview")
                await ViewModel.ClaudeCode.Overview.RefreshAsync();
        };
```

- After the `ViewModel.History.AttachOwner(this);` block add:

```csharp
        ViewModel.ClaudeCode.Overview.AttachOwner(this);
        ViewModel.ClaudeCode.Attach();
```

- `ViewModel.LocalCc.Changed += RenderLocalCc;` → `ViewModel.ClaudeCode.Overview.Changed += RenderLocalCc;` (and the matching `-=` in `Closed`, plus `ViewModel.ClaudeCode.Detach();` there).
- `RenderLocalCc` body → `BarStrip.SetData(ViewModel.ClaudeCode.Overview.ByDay, ViewModel.ClaudeCode.Overview.Palette);`
- `Tabs_SelectionChanged`: replace the `"Local CC"` branch with:

```csharp
        ViewModel.ClaudeCode.IsTabActive = header == "Claude Code";
        if (header == "Claude Code")
        {
            await ViewModel.ClaudeCode.RefreshActiveAsync();
            _localCcTimer.Start();
        }
```

(keep the `else { _localCcTimer.Stop(); … }` shape).

- [ ] **Step 6: Build + verify**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
```

Expected: clean build, 416 green. Manual spot-check: tab reads "Claude Code"; pills switch sections; Overview totals match the pre-WS-C tab within one refresh; toggling a root off prompts; Erase archive empties `%LOCALAPPDATA%\Sanduhr\vault`.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App
git commit -m "feat(vault): Claude Code tab shell, vault-backed Overview, data stewardship"
```

---

### Task 7: Trends — weekly bars, no-record texture, range pills, vault-birth footer

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/CcTrendsViewModel.cs`
- Create: `windows-dotnet/src/Sanduhr.App/Views/CcTrendsControl.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs`, `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` + `.xaml.cs`

**Interfaces:**
- Consumes (Tasks 4–6): `VaultReader.ReadWeeks/TopProjects/BirthDate`, `VaultWeek`, `VaultService.ConsentedRootNames`, `BreakdownRow`, `ClaudeCodeTabViewModel` shell.
- Produces: `CcTrendsViewModel` with `event Action? Changed`, `int WeeksBack` (4/12/26, default 12), `IReadOnlyList<VaultWeek> Weeks`, `ObservableCollection<BreakdownRow> TopProjects`, `string FooterText`, `string InfoText`, `ThemePalette Palette`, `SetWeeksCommand(string)`, `Task RefreshAsync()`; `CcTrendsControl.SetData(IReadOnlyList<VaultWeek>, ThemePalette)`; `ClaudeCodeTabViewModel.Trends`.
- **Rendering rules (binding, from spec):** current week hatched ("week in progress"); a zero-total week with `HasNoRecordGap` renders the no-record TEXTURE (dotted/hatched band), NEVER a zero-height bar; a zero-total covered week renders the 1px baseline tick (true zero); a non-zero week with a gap renders its bar PLUS the textured underline strip (partial record). Footer: *"history preserved since {MMMM d, yyyy}"*. Day-1 info line: `InfoText` = "Fresh vault — the first backfill seeded about 4 weeks from your existing logs; longer trends fill in from here." shown while `today - birth <= 1 day`.

- [ ] **Step 1: `CcTrendsViewModel`**

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Trends section: weekly total bars over 4/12/26 weeks from vault rollups,
/// top projects for the window, the vault-birth footer, and the day-1 backfill
/// note. All reads off-thread; the window re-pushes into CcTrendsControl on
/// <see cref="Changed"/> (custom-render controls can't bind).
/// </summary>
public sealed partial class CcTrendsViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;

    public event Action? Changed;

    [ObservableProperty] private int _weeksBack = 12;
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string _infoText = "";

    public ObservableCollection<BreakdownRow> TopProjects { get; } = new();

    private IReadOnlyList<VaultWeek> _weeks = Array.Empty<VaultWeek>();
    public IReadOnlyList<VaultWeek> Weeks => _weeks;

    public ThemePalette Palette => _widget.Palette;

    public CcTrendsViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        _widget.ThemeChanged += _ => Changed?.Invoke();
    }

    [RelayCommand]
    private async Task SetWeeks(string weeks)
    {
        if (int.TryParse(weeks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n is 4 or 12 or 26)
        {
            WeeksBack = n;
            await RefreshAsync();
        }
    }

    private sealed record TrendsData(
        IReadOnlyList<VaultWeek> Weeks,
        IReadOnlyList<(string Name, long Total)> Top,
        DateOnly? Birth);

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        int weeksBack = WeeksBack;
        TrendsData data;
        try
        {
            data = await Task.Run(() =>
            {
                if (vault is null)
                    return new TrendsData(Array.Empty<VaultWeek>(),
                        Array.Empty<(string, long)>(), null);
                var roots = vault.ConsentedRootNames();
                var today = DateOnly.FromDateTime(DateTime.Now);
                var weeks = vault.Reader.ReadWeeks(roots, weeksBack, today);
                var from = weeks.Count > 0 ? weeks[0].WeekStart : today;
                var top = vault.Reader.TopProjects(roots, from, today.AddDays(1), 5);
                return new TrendsData(weeks, top, vault.Reader.BirthDate(roots));
            }).ConfigureAwait(true);
        }
        catch
        {
            data = new TrendsData(Array.Empty<VaultWeek>(), Array.Empty<(string, long)>(), null);
        }

        _weeks = data.Weeks;
        TopProjects.Clear();
        foreach (var (name, total) in data.Top)
            TopProjects.Add(new BreakdownRow(name, TokenFormat.Compact(total)));

        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        FooterText = data.Birth is { } birth
            ? $"history preserved since {birth.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}"
            : "";
        InfoText = data.Birth is { } b && todayLocal.DayNumber - b.DayNumber <= 1
            ? "Fresh vault — the first backfill seeded about 4 weeks from your existing logs; longer trends fill in from here."
            : "";
        Changed?.Invoke();
    }
}
```

- [ ] **Step 2: `CcTrendsControl`**

Create `windows-dotnet/src/Sanduhr.App/Views/CcTrendsControl.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// Weekly-bars strip for Trends. Grammar (spec, binding): current week hatched
/// ("week in progress"); a zero-total week WITH a coverage gap gets the
/// "no record" texture, never a zero-height bar (a widget-off fortnight must
/// not read as a vacation); a covered zero week gets the 1px baseline tick; a
/// non-zero gap week gets its bar plus a textured underline. Sanduhr.Brush.*
/// palette only, pushed via SetData.
/// </summary>
public sealed class CcTrendsControl : FrameworkElement
{
    private IReadOnlyList<VaultWeek> _weeks = Array.Empty<VaultWeek>();
    private ThemePalette _palette = ThemePalette.Obsidian;

    public CcTrendsControl()
    {
        MinHeight = 120;
    }

    public void SetData(IReadOnlyList<VaultWeek> weeks, ThemePalette palette)
    {
        _weeks = weeks ?? Array.Empty<VaultWeek>();
        _palette = palette;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (_weeks.Count == 0)
        {
            DrawCenteredText(dc, "No vault history yet.", w, h, dpi);
            return;
        }

        long maxV = _weeks.Max(x => x.Total);
        const int gap = 4;
        double labelBand = 16;
        double baselineY = h - 6 - labelBand;
        double barArea = Math.Max(1, w - 4);
        double barW = Math.Max(4, (barArea - gap * (_weeks.Count - 1)) / _weeks.Count);

        var barBrush = Frozen(_palette.Accent);
        var tickBrush = Frozen(WithAlpha(_palette.TextDim, 80));
        var gapBrush = NoRecordBrush();
        var hatchBrush = HatchBrush();

        double x = 2;
        foreach (var week in _weeks)
        {
            if (week.Total == 0 && week.HasNoRecordGap)
            {
                // "No record" texture band — visibly not a zero.
                dc.DrawRectangle(gapBrush, null, new Rect(x, baselineY - 14, barW, 14));
            }
            else if (week.Total == 0)
            {
                dc.DrawRectangle(tickBrush, null, new Rect(x, baselineY, barW, 1));
            }
            else
            {
                double barH = maxV == 0 ? 0 : Math.Max(3, week.Total / (double)maxV * (baselineY - 10));
                var rect = new Rect(x, baselineY - barH, barW, barH);
                dc.DrawRectangle(week.IsCurrent ? hatchBrush : barBrush, null, rect);
                if (week.HasNoRecordGap)
                    dc.DrawRectangle(gapBrush, null, new Rect(x, baselineY + 2, barW, 4));
            }
            x += barW + gap;
        }

        // Sparse labels: first and current week starts.
        DrawLabel(dc, _weeks[0].WeekStart, 2, baselineY + 4, dpi, FlowDirection.LeftToRight);
        var lastX = 2 + (_weeks.Count - 1) * (barW + gap);
        DrawLabel(dc, _weeks[^1].WeekStart, lastX, baselineY + 4, dpi, FlowDirection.LeftToRight);
    }

    private void DrawLabel(DrawingContext dc, DateOnly day, double x, double y, double dpi, FlowDirection dir)
    {
        var ft = new FormattedText(
            day.ToString("MMM d", CultureInfo.InvariantCulture),
            CultureInfo.CurrentCulture, dir, new Typeface("Segoe UI"), 9,
            Frozen(_palette.TextDim), dpi);
        dc.DrawText(ft, new Point(x, y));
    }

    private void DrawCenteredText(DrawingContext dc, string text, double w, double h, double dpi)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, Frozen(_palette.TextDim), dpi)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = w,
        };
        dc.DrawText(ft, new Point(0, (h - ft.Height) / 2));
    }

    /// <summary>Diagonal accent hatch — the "week in progress" fill.</summary>
    private Brush HatchBrush()
    {
        var pen = new Pen(Frozen(_palette.Accent), 2);
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            ctx.DrawRectangle(Frozen(WithAlpha(_palette.Accent, 50)), null, new Rect(0, 0, 8, 8));
            ctx.DrawLine(pen, new Point(0, 8), new Point(8, 0));
        }
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Dotted TextDim texture — "no record", visually distinct from
    /// both bars and baseline ticks.</summary>
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

- [ ] **Step 3: Wire into the shell**

- `ClaudeCodeTabViewModel`: add `public CcTrendsViewModel Trends { get; }`, extend the ctor to `(WidgetViewModel widget, LocalCcViewModel overview, CcTrendsViewModel trends)`, and add the switch case `case "Trends": await Trends.RefreshAsync(); break;` in `RefreshActiveAsync`.
- `SettingsViewModel`: pass `new CcTrendsViewModel(widget)` as the third ctor arg.
- `SettingsWindow.xaml`: add range-pill styles `TrendsPill4` / `TrendsPill12` / `TrendsPill26` next to the CcNav styles — same accent-fill grammar, `DataTrigger Binding="{Binding WeeksBack}"` values `4` / `12` / `26`. Replace the empty Trends placeholder grid with:

```xml
                            <!-- Trends -->
                            <Grid Grid.Row="1" Style="{StaticResource CcSectionTrends}"
                                  DataContext="{Binding Trends}">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="*" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>

                                <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
                                    <TextBlock Text="Window:" VerticalAlignment="Center" FontSize="12"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,8,0" />
                                    <Button Content="4 weeks" Style="{StaticResource TrendsPill4}"
                                            Command="{Binding SetWeeksCommand}" CommandParameter="4" />
                                    <Button Content="12 weeks" Style="{StaticResource TrendsPill12}"
                                            Command="{Binding SetWeeksCommand}" CommandParameter="12" />
                                    <Button Content="26 weeks" Style="{StaticResource TrendsPill26}"
                                            Command="{Binding SetWeeksCommand}" CommandParameter="26" />
                                </StackPanel>

                                <TextBlock Grid.Row="1" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,6"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                           Text="{Binding InfoText}"
                                           Visibility="{Binding InfoText, Converter={StaticResource StrVis}}" />

                                <Border Grid.Row="2" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6"
                                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1" Padding="8">
                                    <views:CcTrendsControl x:Name="TrendsChart" />
                                </Border>

                                <DockPanel Grid.Row="3" Margin="0,8,0,0" MaxHeight="120">
                                    <TextBlock DockPanel.Dock="Top" Text="Top projects (window)" FontSize="9"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,4" />
                                    <ItemsControl ItemsSource="{Binding TopProjects}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Grid Margin="0,1">
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="*" />
                                                        <ColumnDefinition Width="Auto" />
                                                    </Grid.ColumnDefinitions>
                                                    <TextBlock Grid.Column="0" Text="{Binding Name}" FontSize="11"
                                                               TextTrimming="CharacterEllipsis"
                                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                                    <TextBlock Grid.Column="1" Text="{Binding Tokens}" FontSize="11" Margin="8,0,0,0"
                                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                </Grid>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </DockPanel>

                                <TextBlock Grid.Row="4" FontSize="10" Margin="0,8,0,0"
                                           Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                                           Text="{Binding FooterText}" />
                            </Grid>
```

If `Sanduhr.Brush.TextDim` is not a registered app brush, use `Sanduhr.Brush.TextSecondary` — check `Theming/ThemePalette.cs` for the exact registered keys and use only keys that exist.

- `SettingsWindow.xaml.cs`: subscribe `ViewModel.ClaudeCode.Trends.Changed += RenderTrends;` (unsubscribe on `Closed`), with:

```csharp
    private void RenderTrends()
        => TrendsChart.SetData(ViewModel.ClaudeCode.Trends.Weeks, ViewModel.ClaudeCode.Trends.Palette);
```

- [ ] **Step 4: Build + verify**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
```

Expected: clean, 416 green. Manual: Trends shows ~4 weeks of bars on a fresh vault + no-record texture to the left; current week hatched; footer names the birth date; range pills re-render; theme flip re-tints (hatch + texture included).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App
git commit -m "feat(vault): Trends — weekly bars, no-record texture, range pills, vault-birth footer"
```

---

### Task 8: Sessions Ledger — scoped chips, virtualized list, typed sort, expansion, CSV export

The headline question is *"what ate 800k yesterday"* — the token column and its sort are ALWAYS scope-relative (from `by_day`), never lifetime, except under the All chip.

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs` (also contains `LedgerRowViewModel`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeTabViewModel.cs`, `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs`, `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` + `.xaml.cs`

**Interfaces:**
- Consumes (Tasks 4–7): `VaultReader.ReadSessions/TokensInScope`, `VaultSessionInfo`, `VaultLedgerCsv`, `CcLogReader.TierForModel`, `VaultService`, `TokenFormat.Compact`, `ThemedDialog`.
- Produces: `CcLedgerViewModel` with `string Scope` (`"Today" | "Yesterday" | "7d" | "All"`, default `"7d"`), `ListCollectionView View`, `ObservableCollection<LedgerRowViewModel> Rows`, `string SortColumn` / `bool SortDescending`, header-text props (`LastActiveHeader` / `ProjectHeader` / `TokensHeader` — include the ▲/▼ glyph, sort NEVER shown by color alone), `SetScopeCommand(string)`, `SortByCommand(string)`, `ExportCsvCommand`, `Task RefreshAsync()`, `AttachOwner(Window)`, `string EmptyText`; `ClaudeCodeTabViewModel.Ledger`.
- **Virtualization constraints (binding, spec-verbatim):** the ListBox owns its scrolling (star-sized row, NO ancestor ScrollViewer); `VirtualizingPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`; `IsExpanded` lives on the row VM; sort via `ListCollectionView.CustomSort` with a typed comparer; refresh DIFFS rows by (root, uuid) — never `Clear()`+re-add (that resets scroll every 5 minutes). `DataGrid` is forbidden.

- [ ] **Step 1: `CcLedgerViewModel` + `LedgerRowViewModel`**

Create `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs`:

```csharp
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One ledger row. IsExpanded lives HERE (recycled containers must
/// not own state); the expansion detail text builds lazily on first expand.</summary>
public sealed partial class LedgerRowViewModel : ObservableObject
{
    public string Key { get; }
    public VaultSessionInfo Info { get; private set; }
    public long ScopedTokens { get; private set; }
    public DateTimeOffset LastTs => Info.LastTs;
    public string ProjectSort => ProjectText;

    /// <summary>Full-path tooltip — non-null ONLY under store_full_paths
    /// (spec: Ledger). A null ToolTip renders nothing in WPF.</summary>
    public string? CwdTooltip => Info.Cwd;

    [ObservableProperty] private string _projectText = "";
    [ObservableProperty] private string _lastActiveText = "";
    [ObservableProperty] private string _modelBadge = "";
    [ObservableProperty] private string _scopedTokensText = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _detailText = "";

    public LedgerRowViewModel(VaultSessionInfo info, DateOnly from, DateOnly to, bool disambiguate)
    {
        Key = info.Root + "|" + info.Uuid;
        Info = info;
        Update(info, from, to, disambiguate);
    }

    public void Update(VaultSessionInfo info, DateOnly from, DateOnly to, bool disambiguate)
    {
        Info = info;
        ProjectText = disambiguate
            ? $"{info.ProjectName} ~{info.ProjectKey[^Math.Min(8, info.ProjectKey.Length)..]}"
            : info.ProjectName;
        LastActiveText = Relative(info.LastTs);
        ModelBadge = BuildBadge(info.ByModel);
        Rescope(from, to);
        if (IsExpanded)
            DetailText = BuildDetail();
    }

    public void Rescope(DateOnly from, DateOnly to)
    {
        ScopedTokens = VaultReader.TokensInScope(Info, from, to);
        ScopedTokensText = ScopedTokens > 0 ? TokenFormat.Compact(ScopedTokens) : "—";
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && DetailText.Length == 0)
            DetailText = BuildDetail();
    }

    private string BuildDetail()
    {
        var sb = new StringBuilder();
        var span = Info.LastTs - Info.FirstTs;
        sb.Append("Span (wall-clock): ").Append(span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{Math.Max(1, (int)span.TotalMinutes)}m");
        sb.Append("  ·  Lifetime: ").Append(TokenFormat.Compact(Info.Total)).Append(" tokens");
        foreach (var (day, bucket) in Info.ByDay.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append('\n').Append(day).Append(": ").Append(TokenFormat.Compact(bucket.Total));
            var models = string.Join(", ", bucket.ByModel.OrderByDescending(kv => kv.Value)
                .Select(kv => $"{ShortModel(kv.Key)} {TokenFormat.Compact(kv.Value)}"));
            if (models.Length > 0)
                sb.Append("  (").Append(models).Append(')');
        }
        if (Info.BySkill is { Count: > 0 })
        {
            sb.Append("\nSkills: ").Append(string.Join(", ",
                Info.BySkill.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {TokenFormat.Compact(kv.Value)}")));
        }
        sb.Append("\nHome: ").Append(Info.Root).Append("  ·  Session: ").Append(Info.Uuid);
        return sb.ToString();
    }

    /// <summary>Read-time tier projection with an honest raw fallback — an
    /// unmapped model (claude-fable-5 is 49% of live traffic) shows its
    /// trimmed raw name, never disappears.</summary>
    private static string BuildBadge(Dictionary<string, long> byModel)
    {
        long total = byModel.Values.Sum();
        if (total <= 0)
            return "";
        var parts = byModel.OrderByDescending(kv => kv.Value).Take(2)
            .Select(kv => $"{ShortModel(kv.Key)} {kv.Value * 100 / total}%");
        return string.Join(" · ", parts);
    }

    private static string ShortModel(string model)
    {
        var tier = CcLogReader.TierForModel(model);
        if (tier == "seven_day_opus") return "opus";
        if (tier == "seven_day_sonnet") return "sonnet";
        if (tier == "seven_day") return "haiku";
        var name = model.StartsWith("claude-", StringComparison.Ordinal) ? model[7..] : model;
        int dateIdx = name.Length - 9;   // trim trailing -yyyymmdd build stamps
        if (dateIdx > 0 && name[dateIdx] == '-' && name[(dateIdx + 1)..].All(char.IsDigit))
            name = name[..dateIdx];
        return name;
    }

    private static string Relative(DateTimeOffset ts)
    {
        var delta = DateTimeOffset.UtcNow - ts.ToUniversalTime();
        if (delta < TimeSpan.FromMinutes(2)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m ago";
        if (delta < TimeSpan.FromHours(24)) return $"{(int)delta.TotalHours}h ago";
        if (delta < TimeSpan.FromDays(14)) return $"{(int)delta.TotalDays}d ago";
        return ts.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Sessions (Ledger) section. Scope chips (Today / Yesterday / 7d / All,
/// default 7d) make the token column answer "what ate 800k yesterday" — a
/// lifetime sort ranks week-old monsters, not yesterday's culprit. Refresh
/// diffs rows by key so scroll position and expansion survive the 5-minute
/// ingest cadence.
/// </summary>
public sealed partial class CcLedgerViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private Window? _owner;

    [ObservableProperty] private string _scope = "7d";
    [ObservableProperty] private string _sortColumn = "Tokens";
    [ObservableProperty] private bool _sortDescending = true;
    [ObservableProperty] private string _emptyText = "";

    public ObservableCollection<LedgerRowViewModel> Rows { get; } = new();

    public ListCollectionView View { get; }

    public ThemePalette Palette => _widget.Palette;

    public string LastActiveHeader => Header("Last active", "LastActive");
    public string ProjectHeader => Header("Project", "Project");
    public string TokensHeader => Header(Scope == "All" ? "Tokens" : $"Tokens ({Scope})", "Tokens");

    public CcLedgerViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        View = new ListCollectionView(Rows) { CustomSort = new LedgerSort(this) };
    }

    public void AttachOwner(Window owner) => _owner = owner;

    private string Header(string label, string column)
        => SortColumn == column ? $"{label} {(SortDescending ? "▼" : "▲")}" : label;

    private void NotifyHeaders()
    {
        OnPropertyChanged(nameof(LastActiveHeader));
        OnPropertyChanged(nameof(ProjectHeader));
        OnPropertyChanged(nameof(TokensHeader));
    }

    private (DateOnly From, DateOnly To) ScopeRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return Scope switch
        {
            "Today" => (today, today),
            "Yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "7d" => (today.AddDays(-6), today),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
    }

    [RelayCommand]
    private void SetScope(string scope)
    {
        if (scope is not ("Today" or "Yesterday" or "7d" or "All"))
            return;
        Scope = scope;
        var (from, to) = ScopeRange();
        foreach (var row in Rows)
            row.Rescope(from, to);
        View.Refresh();
        NotifyHeaders();
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column != "Project";   // text asc, numbers/dates desc
        }
        View.Refresh();
        NotifyHeaders();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"sanduhr-sessions-{DateTime.Now:yyyyMMdd}.csv",
        };
        bool? ok = _owner is not null ? dialog.ShowDialog(_owner) : dialog.ShowDialog();
        if (ok != true)
            return;
        var rows = View.Cast<LedgerRowViewModel>()
            .Select(r => new VaultLedgerCsv.Row(
                r.Info.Uuid, r.Info.Root, r.ProjectText,
                r.Info.FirstTs.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                r.Info.LastTs.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                r.ScopedTokens, r.Info.Total,
                string.Join(";", r.Info.ByModel.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}:{kv.Value}"))))
            .ToList();
        var built = VaultLedgerCsv.Build(rows);
        try
        {
            File.WriteAllText(dialog.FileName, built.Text);
            ThemedDialog.Show(_owner, "Export complete", $"Wrote {built.RowCount} rows.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ThemedDialog.Show(_owner, "Export failed", "Could not write the file. Is it open elsewhere?",
                kind: ThemedDialogKind.Warning);
        }
    }

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        IReadOnlyList<VaultSessionInfo> sessions;
        try
        {
            sessions = await Task.Run(() =>
                vault is null
                    ? (IReadOnlyList<VaultSessionInfo>)Array.Empty<VaultSessionInfo>()
                    : vault.Reader.ReadSessions(vault.ConsentedRootNames())).ConfigureAwait(true);
        }
        catch
        {
            sessions = Array.Empty<VaultSessionInfo>();
        }

        var (from, to) = ScopeRange();
        // Disambiguate only when two DIFFERENT project keys share a name.
        var nameGroups = sessions.GroupBy(s => s.ProjectName, StringComparer.Ordinal)
            .Where(g => g.Select(s => s.ProjectKey).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Diff by key — NEVER Clear()+re-add (scroll + expansion survive).
        var incoming = sessions.ToDictionary(s => s.Root + "|" + s.Uuid, StringComparer.Ordinal);
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!incoming.ContainsKey(Rows[i].Key))
                Rows.RemoveAt(i);
        }
        var existing = Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        foreach (var (key, info) in incoming)
        {
            bool disambiguate = nameGroups.Contains(info.ProjectName);
            if (existing.TryGetValue(key, out var row))
                row.Update(info, from, to, disambiguate);
            else
                Rows.Add(new LedgerRowViewModel(info, from, to, disambiguate));
        }
        View.Refresh();

        EmptyText = Rows.Count > 0 ? ""
            : (vault is null || vault.ConsentedRootNames().Count == 0)
                ? "History vault is off — enable a Claude Code home in Overview."
                : "No sessions recorded yet — the first backfill lands within a minute.";
    }

    /// <summary>Typed comparer for ListCollectionView.CustomSort — the spec
    /// forbids reflection-based SortDescriptions on a recycling list.</summary>
    private sealed class LedgerSort : IComparer
    {
        private readonly CcLedgerViewModel _vm;

        public LedgerSort(CcLedgerViewModel vm) => _vm = vm;

        public int Compare(object? x, object? y)
        {
            if (x is not LedgerRowViewModel a || y is not LedgerRowViewModel b)
                return 0;
            int cmp = _vm.SortColumn switch
            {
                "LastActive" => a.LastTs.CompareTo(b.LastTs),
                "Project" => string.Compare(a.ProjectSort, b.ProjectSort, StringComparison.OrdinalIgnoreCase),
                _ => a.ScopedTokens.CompareTo(b.ScopedTokens),
            };
            if (cmp == 0)
                cmp = a.LastTs.CompareTo(b.LastTs);
            return _vm.SortDescending ? -cmp : cmp;
        }
    }
}
```

- [ ] **Step 2: Wire into the shell**

- `ClaudeCodeTabViewModel`: add `public CcLedgerViewModel Ledger { get; }`, extend the ctor to `(WidgetViewModel widget, LocalCcViewModel overview, CcTrendsViewModel trends, CcLedgerViewModel ledger)`, add `case "Sessions": await Ledger.RefreshAsync(); break;`.
- `SettingsViewModel`: pass `new CcLedgerViewModel(widget)` as the fourth arg.
- `SettingsWindow.xaml.cs`: after the other `AttachOwner` calls add `ViewModel.ClaudeCode.Ledger.AttachOwner(this);`.

- [ ] **Step 3: XAML — scope-pill styles + the Sessions section**

(a) Next to the Trends pill styles add four scope-chip styles, `LedgerChipToday` / `LedgerChipYesterday` / `LedgerChip7d` / `LedgerChipAll` — same accent-fill grammar, `DataTrigger Binding="{Binding Scope}"` values `Today` / `Yesterday` / `7d` / `All`.

(b) Replace the empty Sessions placeholder grid with:

```xml
                            <!-- Sessions (Ledger). The ListBox OWNS scrolling:
                                 star-sized row, no ancestor ScrollViewer (the
                                 spec's named in-repo trap), recycling
                                 virtualization, pixel scroll. -->
                            <Grid Grid.Row="1" Style="{StaticResource CcSectionSessions}"
                                  DataContext="{Binding Ledger}">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="*" />
                                </Grid.RowDefinitions>

                                <DockPanel Grid.Row="0" Margin="0,0,0,8">
                                    <Button DockPanel.Dock="Right" Style="{StaticResource AccentButton}"
                                            Content="Export CSV…" Command="{Binding ExportCsvCommand}" />
                                    <StackPanel Orientation="Horizontal">
                                        <Button Content="Today" Style="{StaticResource LedgerChipToday}"
                                                Command="{Binding SetScopeCommand}" CommandParameter="Today" />
                                        <Button Content="Yesterday" Style="{StaticResource LedgerChipYesterday}"
                                                Command="{Binding SetScopeCommand}" CommandParameter="Yesterday" />
                                        <Button Content="7 days" Style="{StaticResource LedgerChip7d}"
                                                Command="{Binding SetScopeCommand}" CommandParameter="7d" />
                                        <Button Content="All" Style="{StaticResource LedgerChipAll}"
                                                Command="{Binding SetScopeCommand}" CommandParameter="All" />
                                    </StackPanel>
                                </DockPanel>

                                <!-- Sort header row — glyph carries direction, never color alone. -->
                                <Grid Grid.Row="1" Margin="4,0,4,4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="90" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="150" />
                                        <ColumnDefinition Width="110" />
                                    </Grid.ColumnDefinitions>
                                    <Button Grid.Column="0" Style="{StaticResource FlatButton}" HorizontalAlignment="Left"
                                            Content="{Binding LastActiveHeader}" FontSize="10"
                                            Command="{Binding SortByCommand}" CommandParameter="LastActive" />
                                    <Button Grid.Column="1" Style="{StaticResource FlatButton}" HorizontalAlignment="Left"
                                            Content="{Binding ProjectHeader}" FontSize="10"
                                            Command="{Binding SortByCommand}" CommandParameter="Project" />
                                    <TextBlock Grid.Column="2" Text="Models" FontSize="10" VerticalAlignment="Center"
                                               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                    <Button Grid.Column="3" Style="{StaticResource FlatButton}" HorizontalAlignment="Right"
                                            Content="{Binding TokensHeader}" FontSize="10"
                                            Command="{Binding SortByCommand}" CommandParameter="Tokens" />
                                </Grid>

                                <Border Grid.Row="2" Background="{DynamicResource Sanduhr.Brush.Bg}" CornerRadius="6"
                                        BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1">
                                    <Grid>
                                        <ListBox ItemsSource="{Binding View}" Background="Transparent" BorderThickness="0"
                                                 HorizontalContentAlignment="Stretch"
                                                 ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                                                 VirtualizingPanel.IsVirtualizing="True"
                                                 VirtualizingPanel.VirtualizationMode="Recycling"
                                                 VirtualizingPanel.ScrollUnit="Pixel">
                                            <ListBox.ItemsPanel>
                                                <ItemsPanelTemplate>
                                                    <VirtualizingStackPanel />
                                                </ItemsPanelTemplate>
                                            </ListBox.ItemsPanel>
                                            <ListBox.ItemContainerStyle>
                                                <Style TargetType="ListBoxItem">
                                                    <Setter Property="Padding" Value="0" />
                                                    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                                                    <Setter Property="Focusable" Value="False" />
                                                    <Setter Property="Template">
                                                        <Setter.Value>
                                                            <ControlTemplate TargetType="ListBoxItem">
                                                                <ContentPresenter />
                                                            </ControlTemplate>
                                                        </Setter.Value>
                                                    </Setter>
                                                </Style>
                                            </ListBox.ItemContainerStyle>
                                            <ListBox.ItemTemplate>
                                                <DataTemplate>
                                                    <StackPanel>
                                                        <ToggleButton IsChecked="{Binding IsExpanded, Mode=TwoWay}" Cursor="Hand">
                                                            <ToggleButton.Template>
                                                                <ControlTemplate TargetType="ToggleButton">
                                                                    <Border x:Name="rowBg" Background="Transparent" Padding="8,5">
                                                                        <ContentPresenter />
                                                                    </Border>
                                                                    <ControlTemplate.Triggers>
                                                                        <Trigger Property="IsMouseOver" Value="True">
                                                                            <Setter TargetName="rowBg" Property="Background"
                                                                                    Value="{DynamicResource Sanduhr.Brush.Glass}" />
                                                                        </Trigger>
                                                                    </ControlTemplate.Triggers>
                                                                </ControlTemplate>
                                                            </ToggleButton.Template>
                                                            <Grid>
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="90" />
                                                                    <ColumnDefinition Width="*" />
                                                                    <ColumnDefinition Width="150" />
                                                                    <ColumnDefinition Width="110" />
                                                                </Grid.ColumnDefinitions>
                                                                <TextBlock Grid.Column="0" Text="{Binding LastActiveText}" FontSize="11"
                                                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                                <TextBlock Grid.Column="1" Text="{Binding ProjectText}" FontSize="11"
                                                                           TextTrimming="CharacterEllipsis"
                                                                           ToolTip="{Binding CwdTooltip}"
                                                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                                                <TextBlock Grid.Column="2" Text="{Binding ModelBadge}" FontSize="10"
                                                                           TextTrimming="CharacterEllipsis"
                                                                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                                <TextBlock Grid.Column="3" Text="{Binding ScopedTokensText}" FontSize="11"
                                                                           HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                                                           Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                                            </Grid>
                                                        </ToggleButton>
                                                        <Border Padding="16,2,8,8"
                                                                Visibility="{Binding IsExpanded, Converter={StaticResource BoolVis}}">
                                                            <TextBlock Text="{Binding DetailText}" FontSize="10" TextWrapping="Wrap"
                                                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
                                                        </Border>
                                                    </StackPanel>
                                                </DataTemplate>
                                            </ListBox.ItemTemplate>
                                        </ListBox>
                                        <TextBlock Text="{Binding EmptyText}" FontSize="11" TextWrapping="Wrap"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center" Margin="20"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                                                   Visibility="{Binding EmptyText, Converter={StaticResource StrVis}}" />
                                    </Grid>
                                </Border>
                            </Grid>
```

- [ ] **Step 4: Build + verify**

```bash
dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
```

Expected: clean, 416 green. Manual: with a populated vault (2,000+ sessions on this machine) the list scrolls smoothly (recycling); chips re-rank instantly ("Yesterday" surfaces yesterday's culprit, not a lifetime monster); expansion survives a background ingest refresh; sort glyphs flip; CSV opens in Excel with the visible order.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App
git commit -m "feat(vault): Sessions Ledger — scope chips, recycling virtualization, typed sort, CSV export"
```

---

### Task 9: PRIVACY.md, smoke scenarios, roadmap bank note, PR

Privacy copy ships in the SAME release as the vault (binding). No code.

**Files:**
- Modify: `docs/PRIVACY.md`
- Modify: `docs/smoke-test-plan.md`
- Modify: `docs/roadmap-2026-07-11.md`

**Interfaces:** none — documentation matching what Tasks 1–8 actually built.

- [ ] **Step 1: PRIVACY.md**

In `docs/PRIVACY.md`:

(a) Update `**Last updated:**` to the current date.

(b) In the "What data Sanduhr touches" table, add two rows after the usage-percentages row:

```markdown
| Your local Claude Code usage history (the "vault": one summary row per session — project name, day-bucketed token totals per model, skill totals; never conversation content, never prompts) | `%LOCALAPPDATA%\Sanduhr\vault\` on your machine, one folder per Claude Code home you opt in | Only your Windows user account | Kept **indefinitely — unlike Claude Code's own logs, which Claude Code deletes after ~30 days**. Per-home opt-in at first run; erase any time via Settings ▸ Claude Code (Erase archive / per-home purge) or by deleting the folder while Sanduhr is not running. Quarantined `.bad` recovery files in the same folder are part of the archive. Never transmitted anywhere. |
| Vault bookkeeping (`checkpoints.json`) | Same vault folder | Same | Hashed log-file identifiers only — no readable paths. Rebuilt automatically if deleted. |
```

(c) In the Operational-logs row, extend the never-contains sentence to:

```markdown
**Never contains your session keys, account labels, `cf_clearance` values, project paths or names, skill names, or session-log contents**
```

(d) In "How you remove your data": change the "Clear local storage" bullet to name BOTH folders (`%APPDATA%\Sanduhr\` and `%LOCALAPPDATA%\Sanduhr\`), and add:

```markdown
- **Note for Microsoft Store installs:** uninstalling from Apps & features does **not** remove `%LOCALAPPDATA%\Sanduhr` (Windows leaves per-user app data behind). If you want the usage vault gone after uninstall, delete that folder manually.
```

- [ ] **Step 2: Smoke scenarios**

Append to `docs/smoke-test-plan.md`:

```markdown
---

## WS-C — usage vault + Claude Code tab (2026-07-12)

Theming rule as above: run once in the default theme, then flip dark / light / Matrix with the surface open — zero unstyled elements (hatch + no-record textures included).

1. **First-run consent.** Fresh settings.json (`vault_prompted` absent): launch shows the themed per-home consent dialog once, pre-checked. "Keep history" → `%LOCALAPPDATA%\Sanduhr\vault\.claude*\sessions-*.json` appear within ~1 min. Relaunch: no re-prompt.
2. **Not now is honored.** Decline the dialog: no vault folder appears, ever; Overview falls back to live logs with no status line; Sessions shows the vault-off empty state.
3. **Overview parity.** With the vault fresh, Overview's Today / Last 30 days match the pre-WS-C numbers (within one 30s refresh of each other).
4. **Degraded honesty.** Stop ingestion (Task Manager: suspend the app > 15 min, or temporarily set the machine clock forward): Overview shows "history vault paused — showing live logs only" and live numbers. Resume: line clears within a cycle.
5. **Ledger answers "what ate 800k yesterday".** Sessions ▸ Yesterday chip: token column shows yesterday-only burn, top row is yesterday's heaviest session, expansion shows its per-day/model breakdown.
6. **Scroll + expansion survive refresh.** Expand a row, scroll mid-list, wait 5+ min (an ingest cycle): scroll position and the expanded row survive.
7. **Two processes, no clobber.** Run the Store build and a Velopack/debug build simultaneously for 10+ min: `sanduhr.log` shows "ingest skipped (writer mutex held)" lines from one side; no `.bad` files; session totals stay correct.
8. **Erase archive is real.** Settings ▸ Claude Code ▸ Erase archive → confirm: vault folder empties, all root checkboxes untick, and NO files reappear over the next 10 min (consent tombstone holds).
9. **Per-root purge.** Untick one home → choose erase: that folder is gone, the other home's folder untouched; re-tick: backfill restores it within a cycle.
10. **Trends honesty.** On a fresh vault, Trends shows ~4 seeded weeks; earlier weeks show the dotted no-record texture (not zero bars); current week hatched; footer names the birth date.
11. **Privacy spot-check.** Open `sanduhr.log` after a full session: no paths, no project names, no skill names, no JSONL content. Open `checkpoints.json`: hex keys only.
12. **MSIX virtualization re-check.** On the Store/MSIX build, confirm vault writes land at the REAL `%LOCALAPPDATA%\Sanduhr\vault` (spike verified virtualization off on 3.1.0 — re-verify on this package build).
```

- [ ] **Step 3: Roadmap bank note**

In `docs/roadmap-2026-07-11.md`, find the WS-C workstream section and append one line to it:

```markdown
- **Banked post-WS-C:** the transitional triple-parse of live session files (ingester + badge tick + footer) retires when the ingester's live rows become the single source for badges / footer / Overview-today — the ad-hoc `TokensSince` walkers go away. Candidate for the next Local-CC touch.
```

- [ ] **Step 4: Full suite + push + PR**

```bash
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj
git add docs/PRIVACY.md docs/smoke-test-plan.md docs/roadmap-2026-07-11.md
git commit -m "docs(vault): PRIVACY vault rows, WS-C smoke scenarios, triple-parse bank note"
git push -u origin feat/ws-c-usage-vault
gh pr create --title "feat: WS-C usage vault + Claude Code trends + Session Ledger" --body "$(cat <<'EOF'
## Summary
- Durable local usage vault under %LOCALAPPDATA%\Sanduhr\vault (per-root dirs, monthly session + rollup shards, checkpointed ingester with guarded tail-parse, Global\Sanduhr.VaultWriter mutex)
- Local CC tab -> "Claude Code" with Overview (vault + live-today merge, degraded fallback, data stewardship), Trends (weekly bars, no-record texture, vault-birth footer), Sessions Ledger (scope chips, recycling virtualization, typed sort, CSV export)
- Consent-gated per root at first run; erase / per-root purge in-app; PRIVACY.md updated in the same release
- 30s-tick hygiene: one shared TokensSince walk off the UI thread feeds badges + footer

Spec: docs/superpowers/specs/2026-07-12-usage-vault-design.md (five-lens panel remediated)

## Test plan
- [ ] Full suite green (baseline 360 -> ~416)
- [ ] Smoke: docs/smoke-test-plan.md WS-C scenarios 1-12

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR opens against main (PR-only — do NOT merge; human smoke first).

- [ ] **Step 5: Update the SDD ledger**

Append the task-completion lines to `.superpowers/sdd/progress.md` per the subagent-driven-development skill's convention (the controller does this during execution, task by task).

---

## Execution notes for the controller

- Suggested dispatch models: Tasks 2–3 most capable; Tasks 1, 4–8 standard; Task 9 cheap. Final whole-branch review: most capable.
- Task 2 and Task 3 duplicate their fixture helpers per test file so each file reads standalone — a deliberate plan choice. If a reviewer flags the duplication, adjudicate it as plan-mandated rather than dispatching a fix.
- The `%LOCALAPPDATA%` MSIX-virtualization question was empirically closed 2026-07-12 (Store build 3.1.0 writes real profile paths; spike in the WS-E remediation work) — do not re-litigate in review; re-VERIFY at release smoke (scenario 12).
- Release-time riders (NOT this branch): version bump to 3.2.0 lockstep (csproj + appxmanifest), Store listing copy, publish-size delta check, plus the riders already parked from WS-B.
