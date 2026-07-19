# Scoped-Limits Wave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the live-but-invisible Fable weekly limit (and every future model-scoped limit) as a widget tier bar, harden org selection, classify Cloudflare-challenge-on-200, and log unknown payload vocabulary.

**Architecture:** The API ships per-model weekly caps in a top-level `limits[]` array (`kind: "weekly_scoped"`, `scope.model.display_name`); the flat `seven_day_*` vocabulary is migrating out (opus/sonnet arrive null). We synthesize flat keys from `limits[]` in `UsageFetcher` (the Routines precedent), register non-Fable models dynamically in `TierModel`, and switch the five `CanonicalOrder` consumers to a new `EffectiveOrder` that folds dynamics in. Spec: `docs/superpowers/specs/2026-07-19-scoped-limits-wave-design.md`.

**Tech Stack:** .NET 10 / WPF, System.Text.Json.Nodes, xUnit.

## Global Constraints

- Branch `feat/scoped-limits-wave` off `main`. Commit by EXACT paths only — never `git add .` (untracked strays `.vibe-iterate/`, `AGENTS.md`, `CLAUDE.md` stay untracked).
- NEVER kill a running `Sanduhr.exe`; build checks go to a scratch output: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj -c Debug --nologo -v q -o "$env:TEMP\sanduhr-slw"`.
- Test command: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --nologo -v q`. Suite is 434 green before Task 1.
- App layer (Sanduhr.App) is untested by design — TDD applies to Sanduhr.Core changes only.
- Synthesized tier key format: `seven_day_{slug}`; slug = display_name lowercased, `[^a-z0-9]+` runs → `_`, trimmed of leading/trailing `_`. `"Fable"` → `seven_day_fable`, `"Haiku 5"` → `seven_day_haiku_5`.
- Never overwrite a NON-NULL upstream top-level key; a JSON-null or absent key is fair game for synthesis.
- Labels: static Fable label is exactly `"Weekly - Fable"` (ASCII hyphen, matching the existing family); dynamic labels are `"Weekly - {displayName}"`.
- fetch-debug.log additions carry key/kind NAMES only — never payload values. sanduhr.log is untouched.
- Comments state constraints and why, never narration; preserve the existing Python-parity comment style.

---

### Task 1: TierModel — SevenDayFable static tier, dynamic registry, EffectiveOrder

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/TierModel.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/TierModelTests.cs` (exists — append)

**Interfaces:**
- Produces: `TierModel.SevenDayFable` const; `TierModel.RegisterScopedTier(string tierKey, string displayName)` (idempotent, thread-safe, no-op for statically registered keys); `TierModel.EffectiveOrder` (`IReadOnlyList<string>` property — canonical order with dynamic keys inserted after `SevenDayOauthApps`, before `IguanaNecktie`, in registration order); `internal static void ResetDynamicTiersForTests()`. `IsKnown`/`Label`/`LabelWithTag` resolve dynamic keys; `ResolveOrder` completion pass iterates `EffectiveOrder`.

- [ ] **Step 1: Write the failing tests** (append to TierModelTests.cs; add `ResetDynamicTiersForTests()` in each test's opening line — the registry is process-global static state)

```csharp
[Fact]
public void SevenDayFable_IsStaticallyRegistered()
{
    TierModel.ResetDynamicTiersForTests();
    Assert.True(TierModel.IsKnown(TierModel.SevenDayFable));
    Assert.Equal("Weekly - Fable", TierModel.Label(TierModel.SevenDayFable));
    int fable = TierModel.CanonicalOrder.ToList().IndexOf(TierModel.SevenDayFable);
    int opus = TierModel.CanonicalOrder.ToList().IndexOf(TierModel.SevenDayOpus);
    Assert.Equal(opus + 1, fable);
    Assert.False(TierModel.IsSpeculative(TierModel.SevenDayFable));
}

[Fact]
public void EffectiveOrder_EqualsCanonical_WhenNoDynamics()
{
    TierModel.ResetDynamicTiersForTests();
    Assert.Equal(TierModel.CanonicalOrder, TierModel.EffectiveOrder);
}

[Fact]
public void RegisterScopedTier_AddsKnownLabeledKey_InFamilyPosition()
{
    TierModel.ResetDynamicTiersForTests();
    TierModel.RegisterScopedTier("seven_day_haiku_5", "Haiku 5");
    Assert.True(TierModel.IsKnown("seven_day_haiku_5"));
    Assert.Equal("Weekly - Haiku 5", TierModel.Label("seven_day_haiku_5"));
    var order = TierModel.EffectiveOrder.ToList();
    Assert.Equal(order.IndexOf(TierModel.SevenDayOauthApps) + 1, order.IndexOf("seven_day_haiku_5"));
    Assert.True(order.IndexOf("seven_day_haiku_5") < order.IndexOf(TierModel.IguanaNecktie));
}

[Fact]
public void RegisterScopedTier_IsIdempotent_AndStaticKeysNoOp()
{
    TierModel.ResetDynamicTiersForTests();
    TierModel.RegisterScopedTier("seven_day_zephyr", "Zephyr");
    TierModel.RegisterScopedTier("seven_day_zephyr", "Zephyr Again");
    Assert.Equal("Weekly - Zephyr", TierModel.Label("seven_day_zephyr"));
    Assert.Equal(1, TierModel.EffectiveOrder.Count(k => k == "seven_day_zephyr"));
    TierModel.RegisterScopedTier(TierModel.SevenDayFable, "Renamed");
    Assert.Equal("Weekly - Fable", TierModel.Label(TierModel.SevenDayFable));
}

[Fact]
public void ResolveOrder_SurfacesDynamics_AndSavedDynamicKeysResolve()
{
    TierModel.ResetDynamicTiersForTests();
    TierModel.RegisterScopedTier("seven_day_zephyr", "Zephyr");
    var resolved = TierModel.ResolveOrder(null);
    Assert.Contains("seven_day_zephyr", resolved);
    var savedFirst = TierModel.ResolveOrder(new[] { "seven_day_zephyr", "bogus_key" });
    Assert.Equal("seven_day_zephyr", savedFirst[0]);
    Assert.DoesNotContain("bogus_key", savedFirst);
}

[Fact]
public void ActiveTiers_RendersDynamicWithData()
{
    TierModel.ResetDynamicTiersForTests();
    TierModel.RegisterScopedTier("seven_day_zephyr", "Zephyr");
    var util = new Dictionary<string, double?> { ["seven_day_zephyr"] = 12.0 };
    Assert.Contains("seven_day_zephyr", TierModel.ActiveTiers(util));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TierModelTests"`
Expected: FAIL — `SevenDayFable`, `RegisterScopedTier`, `EffectiveOrder`, `ResetDynamicTiersForTests` do not exist.

- [ ] **Step 3: Implement in TierModel.cs**

Const block (after `SevenDaySonnet`/`SevenDayOpus`, keeping declaration order tidy):

```csharp
    public const string SevenDayFable = "seven_day_fable";
```

`CanonicalOrder`: insert `SevenDayFable` directly after `SevenDayOpus`. `LabelsByKey`: add `[SevenDayFable] = "Weekly - Fable",` after the opus entry. Do NOT add it to `SpeculativeTiers`.

Dynamic registry (new region after the registry accessors):

```csharp
    // -- dynamic scoped tiers (synthesized from the API's limits[] array) -----

    // Append-only for the process lifetime: a tier seen once stays orderable
    // even when a later fetch omits it (its utilization goes null, the card
    // drops, the order slot remains). Fetch runs on a background thread while
    // render reads on the UI thread — hence the gate.
    private static readonly Dictionary<string, string> DynamicLabels = new(StringComparer.Ordinal);
    private static readonly List<string> DynamicOrder = new();
    private static readonly object DynamicGate = new();

    /// <summary>Register a scoped tier synthesized from a limits[] entry.
    /// Idempotent; statically registered keys are a no-op (the static label wins).</summary>
    public static void RegisterScopedTier(string tierKey, string displayName)
    {
        lock (DynamicGate)
        {
            if (LabelsByKey.ContainsKey(tierKey) || DynamicLabels.ContainsKey(tierKey))
                return;
            DynamicLabels[tierKey] = $"Weekly - {displayName}";
            DynamicOrder.Add(tierKey);
        }
    }

    /// <summary>Canonical order with dynamic scoped tiers folded in after the
    /// seven_day family (post-SevenDayOauthApps, pre-IguanaNecktie), in
    /// registration order. Consumers that used to iterate CanonicalOrder
    /// iterate this so dynamic tiers render/persist/chart without edits.</summary>
    public static IReadOnlyList<string> EffectiveOrder
    {
        get
        {
            lock (DynamicGate)
            {
                if (DynamicOrder.Count == 0)
                    return CanonicalOrder;
                var order = new List<string>(CanonicalOrder.Count + DynamicOrder.Count);
                foreach (var key in CanonicalOrder)
                {
                    order.Add(key);
                    if (key == SevenDayOauthApps)
                        order.AddRange(DynamicOrder);
                }
                return order;
            }
        }
    }

    internal static void ResetDynamicTiersForTests()
    {
        lock (DynamicGate)
        {
            DynamicLabels.Clear();
            DynamicOrder.Clear();
        }
    }
```

`IsKnown` / `Label` / `LabelWithTag` consult the dynamic map under the gate:

```csharp
    public static bool IsKnown(string tierKey)
    {
        if (LabelsByKey.ContainsKey(tierKey)) return true;
        lock (DynamicGate) return DynamicLabels.ContainsKey(tierKey);
    }

    public static string Label(string tierKey)
    {
        if (LabelsByKey.TryGetValue(tierKey, out var label)) return label;
        lock (DynamicGate)
        {
            if (DynamicLabels.TryGetValue(tierKey, out var dyn)) return dyn;
        }
        throw new KeyNotFoundException($"Unknown tier key: {tierKey}");
    }
```

`ResolveOrder`: change the completion loop `foreach (var key in CanonicalOrder)` → `foreach (var key in EffectiveOrder)` (saved-order pass is unchanged — `IsKnown` now resolves dynamics, so saved dynamic keys survive).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --nologo -v q`
Expected: all green — report the actual total. If any existing TierModel test asserts the exact CanonicalOrder contents or count, update it to include `seven_day_fable` — that is the ONLY sanctioned existing-test edit in this task; report it.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/TierModel.cs windows-dotnet/tests/Sanduhr.Tests/TierModelTests.cs
git commit -m "feat(tiers): seven_day_fable static tier + dynamic scoped-tier registry + EffectiveOrder"
```

---

### Task 2: Switch the five CanonicalOrder consumers to EffectiveOrder

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (~lines 881, 944)
- Modify: `windows-dotnet/src/Sanduhr.Core/UsageFetcher.cs` (~line 46 + the loop at ~92)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs` (~line 121)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/HistoryChart.cs` (~line 69)

**Interfaces:**
- Consumes: `TierModel.EffectiveOrder` (Task 1).
- Produces: no signature changes anywhere.

- [ ] **Step 1: Make the four edits**

1. `WidgetViewModel.RenderCards` (~881): `foreach (var key in TierModel.CanonicalOrder)` → `foreach (var key in TierModel.EffectiveOrder)`.
2. `WidgetViewModel.BuildAlertSnapshots` (~944): same substitution.
3. `UsageFetcher`: DELETE the `HistoryTiers` static field (~lines 43-46 including its comment) and change the history loop header (~92) to `foreach (var tierKey in TierModel.EffectiveOrder)`. The field captured `CanonicalOrder` at class-init time; dynamics register at runtime, so the order MUST be read per call. Replace the field's comment with:

```csharp
        // History tiers = the effective order (canonical + dynamic scoped tiers),
        // read PER CALL — dynamics register during fetch, so a static capture
        // would persist a stale list. fetcher._HISTORY_TIERS parity now spans
        // both static and synthesized keys.
```

4. `HistoryTabViewModel.Refresh` (~121): `foreach (var key in TierModel.CanonicalOrder)` → `TierModel.EffectiveOrder`.
5. `HistoryChart` label-gutter loop (~69): `foreach (var key in TierModel.CanonicalOrder)` → `TierModel.EffectiveOrder` (the "FULL canonical label set" comment above it becomes "FULL effective label set" — dynamic labels resolve via Task 1's Label fallback, so no KeyNotFound risk).

- [ ] **Step 2: Verify no other CanonicalOrder consumers exist**

Run: `grep -rn "CanonicalOrder" windows-dotnet/src --include="*.cs"`
Expected: hits ONLY inside TierModel.cs (definition, EffectiveOrder, ResolveOrder) — plus nothing else. Any additional consumer found: switch it the same way and report it.

- [ ] **Step 3: Build + full suite**

Run: scratch build (Global Constraints) then `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --nologo -v q`
Expected: 0 errors; all tests pass (Task 1's `EffectiveOrder_EqualsCanonical_WhenNoDynamics` is the no-dynamics regression lock for every one of these sites).

- [ ] **Step 4: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.Core/UsageFetcher.cs windows-dotnet/src/Sanduhr.App/ViewModels/HistoryTabViewModel.cs windows-dotnet/src/Sanduhr.App/Views/HistoryChart.cs
git commit -m "refactor(tiers): CanonicalOrder consumers read EffectiveOrder so dynamic scoped tiers cascade"
```

---

### Task 3: UsageFetcher — synthesize scoped tiers from limits[]

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/UsageFetcher.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/UsageFetcherTests.cs` (exists — append; it already has a canned `IClaudeApiClient` stub pattern)

**Interfaces:**
- Consumes: `TierModel.RegisterScopedTier` (Task 1).
- Produces: `internal static string ScopedSlug(string displayName)` on `UsageFetcher`; synthesized `data["seven_day_{slug}"] = { "utilization": <percent>, "resets_at": <resets_at> }` entries in the returned payload.

- [ ] **Step 1: Write the failing tests** (each test calls `TierModel.ResetDynamicTiersForTests()` first; build the usage payload the stub returns from a raw JSON string). `FetchWith(body)` / `FetchWith(body, history)` and `RecordingHistory` below are HELPERS you write against the file's existing doubles — read UsageFetcherTests.cs first and reuse its canned `IClaudeApiClient` + history-recording pattern under whatever names it already has (create thin ones only if absent: a stub client whose `GetUsageAsync` parses the given body and whose `GetRoutineBudgetAsync` returns null, plus an `IUsageHistory` that records `Append` calls).

The live-capture fixture (verbatim from the 2026-07-19 probe of the owner's Max account — this exact shape is the contract):

```csharp
    private const string LiveLimitsFixture = """
    {
      "five_hour": { "utilization": 12, "resets_at": "2026-07-20T03:00:00Z" },
      "seven_day": { "utilization": 34, "resets_at": "2026-07-26T05:59:59Z" },
      "seven_day_opus": null,
      "seven_day_sonnet": null,
      "extra_usage": { "utilization": 19, "resets_at": null },
      "limits": [
        { "kind": "session", "group": "session", "percent": 12, "severity": "normal",
          "resets_at": "2026-07-20T03:00:00Z", "scope": null, "is_active": true },
        { "kind": "weekly", "group": "weekly", "percent": 34, "severity": "normal",
          "resets_at": "2026-07-26T05:59:59Z", "scope": null, "is_active": true },
        { "kind": "weekly_scoped", "group": "weekly", "percent": 7, "severity": "normal",
          "resets_at": "2026-07-26T05:59:59Z", "is_active": false,
          "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null } }
      ]
    }
    """;
```

```csharp
[Fact]
public async Task Synthesizes_SevenDayFable_FromWeeklyScopedLimit()
{
    TierModel.ResetDynamicTiersForTests();
    var data = await FetchWith(LiveLimitsFixture);   // helper: stub client returning this body, null routines
    var fable = Assert.IsType<JsonObject>(data["seven_day_fable"]);
    Assert.Equal(7, (int)fable["utilization"]!.GetValue<double>());
    Assert.Equal("2026-07-26T05:59:59Z", (string?)fable["resets_at"]);
}

[Fact]
public async Task NonScoped_And_Scopeless_Limits_AreIgnored()
{
    TierModel.ResetDynamicTiersForTests();
    var data = await FetchWith(LiveLimitsFixture);
    // session/weekly group entries must NOT synthesize keys ("seven_day_" + nothing)
    Assert.All(((JsonObject)data!).Select(kv => kv.Key), k => Assert.False(k == "seven_day_"));
    Assert.Null(data["session"]);
}

[Fact]
public async Task DoesNotOverwrite_NonNull_UpstreamKey()
{
    TierModel.ResetDynamicTiersForTests();
    var body = LiveLimitsFixture.Replace("\"seven_day_opus\": null",
        "\"seven_day_opus\": null, \"seven_day_fable\": { \"utilization\": 99, \"resets_at\": null }");
    var data = await FetchWith(body);
    Assert.Equal(99, (int)data["seven_day_fable"]!["utilization"]!.GetValue<double>());
}

[Fact]
public async Task Fills_JsonNull_UpstreamKey()
{
    TierModel.ResetDynamicTiersForTests();
    var body = LiveLimitsFixture.Replace("\"seven_day_opus\": null",
        "\"seven_day_opus\": null, \"seven_day_fable\": null");
    var data = await FetchWith(body);
    Assert.Equal(7, (int)data["seven_day_fable"]!["utilization"]!.GetValue<double>());
}

[Fact]
public async Task NullPercent_SynthesizesNullUtilization()
{
    TierModel.ResetDynamicTiersForTests();
    var body = LiveLimitsFixture.Replace("\"percent\": 7", "\"percent\": null");
    var data = await FetchWith(body);
    var fable = Assert.IsType<JsonObject>(data["seven_day_fable"]);
    Assert.Null(fable["utilization"]);
}

[Fact]
public async Task MissingOrMalformed_Limits_NoOp()
{
    TierModel.ResetDynamicTiersForTests();
    var noLimits = await FetchWith("""{ "five_hour": { "utilization": 1, "resets_at": null } }""");
    Assert.Null(noLimits["seven_day_fable"]);
    var badLimits = await FetchWith("""{ "limits": { "not": "an array" } }""");
    Assert.Null(badLimits["seven_day_fable"]);
    var junkEntries = await FetchWith("""{ "limits": [ 42, null, { "kind": "weekly_scoped" } ] }""");
    Assert.Null(junkEntries["seven_day_fable"]);
}

[Theory]
[InlineData("Fable", "fable")]
[InlineData("Haiku 5", "haiku_5")]
[InlineData("  Weird--Name!! ", "weird_name")]
public void ScopedSlug_NormalizesDisplayNames(string display, string slug)
    => Assert.Equal(slug, UsageFetcher.ScopedSlug(display));

[Fact]
public async Task SynthesizedTier_RegistersAndPersistsToHistory()
{
    TierModel.ResetDynamicTiersForTests();
    var history = new RecordingHistory();          // existing test double pattern
    var data = await FetchWith(LiveLimitsFixture, history);
    Assert.True(TierModel.IsKnown("seven_day_fable"));
    Assert.Contains(history.Appends, a => a.TierKey == "seven_day_fable" && a.Utilization == 7);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ... --filter "FullyQualifiedName~UsageFetcherTests"`
Expected: FAIL — no synthesis, no `ScopedSlug`.

- [ ] **Step 3: Implement in UsageFetcher.FetchAsync** (insert AFTER the routines block ends at ~line 90, BEFORE the history loop — history must see the synthesized keys)

```csharp
        // Per-model weekly caps ship in the top-level limits[] array
        // (kind=weekly_scoped, keyed by scope.model.display_name — model.id is
        // null in live payloads, the display name is the only stable handle).
        // The flat seven_day_* vocabulary is migrating out upstream
        // (opus/sonnet now arrive null), so synthesize a flat key per scoped
        // entry and let the whole tier pipeline pick it up. A NON-null
        // upstream key of the same name always wins; JSON-null or absent is
        // synthesized over.
        if (data["limits"] is JsonArray limits)
        {
            foreach (var node in limits)
            {
                if (node is not JsonObject entry) continue;
                if ((string?)entry["kind"] != "weekly_scoped") continue;
                string? displayName = (string?)entry["scope"]?["model"]?["display_name"];
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                string key = "seven_day_" + ScopedSlug(displayName);
                TierModel.RegisterScopedTier(key, displayName.Trim());
                if (data.ContainsKey(key) && data[key] is not null)
                    continue;
                data[key] = new JsonObject
                {
                    ["utilization"] = entry["percent"]?.DeepClone(),
                    ["resets_at"] = entry["resets_at"]?.DeepClone(),
                };
            }
        }
```

And the slug helper (below `FetchAsync`):

```csharp
    /// <summary>Community slug convention for scoped-limit keys:
    /// "Fable" → fable, "Haiku 5" → haiku_5. Lowercase, non-alphanumeric runs
    /// collapse to '_', trimmed — so a display-name tweak upstream maps to a
    /// stable key wherever possible.</summary>
    internal static string ScopedSlug(string displayName)
    {
        var sb = new System.Text.StringBuilder(displayName.Length);
        bool lastUnderscore = false;
        foreach (char c in displayName.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
                lastUnderscore = false;
            }
            else if (!lastUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }
        return sb.ToString().TrimEnd('_');
    }
```

- [ ] **Step 4: Full suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --nologo -v q`
Expected: PASS (Task 1+3 additions on top of 434).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/UsageFetcher.cs windows-dotnet/tests/Sanduhr.Tests/UsageFetcherTests.cs
git commit -m "feat(tiers): synthesize scoped weekly limits from limits[] — the Fable bar"
```

---

### Task 4: Org selection by capabilities

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/ClaudeApiParsing.cs` (`ParseOrganizations`, ~lines 35-68)
- Test: `windows-dotnet/tests/Sanduhr.Tests/ClaudeApiParsingTests.cs` (exists — append)

**Interfaces:**
- Consumes/Produces: `ParseOrganizations(string body)` signature unchanged; selection rule changes from `orgs[0]` to capabilities-preferred.

- [ ] **Step 1: Write the failing tests** (fixture = the trimmed live two-org shape)

```csharp
    private const string TwoOrgFixture = """
    [
      { "uuid": "aaaa-1111", "name": "someone's Organization",
        "rate_limit_tier": "default_claude_max_20x", "billing_type": "stripe_subscription",
        "capabilities": ["claude_max", "chat"] },
      { "uuid": "bbbb-2222", "name": "Someone's Individual Org",
        "rate_limit_tier": "auto_trust_tier_c", "billing_type": "prepaid",
        "capabilities": ["api", "api_individual"] }
    ]
    """;

[Fact]
public void PicksClaudeMaxOrg_RegardlessOfOrdering()
{
    Assert.Equal("aaaa-1111", ClaudeApiParsing.ParseOrganizations(TwoOrgFixture).OrgId);
    // reversed ordering must select the SAME org
    var reversed = """
    [
      { "uuid": "bbbb-2222", "capabilities": ["api", "api_individual"] },
      { "uuid": "aaaa-1111", "rate_limit_tier": "default_claude_max_20x",
        "billing_type": "stripe_subscription", "capabilities": ["claude_max", "chat"] }
    ]
    """;
    var d = ClaudeApiParsing.ParseOrganizations(reversed);
    Assert.Equal("aaaa-1111", d.OrgId);
    Assert.Equal("default_claude_max_20x", d.Account.RateLimitTier);   // plan fields from the SELECTED org
}

[Fact]
public void FallsBackToChat_ThenFirst()
{
    var chatOnly = """[ { "uuid": "x", "capabilities": ["api"] }, { "uuid": "y", "capabilities": ["chat"] } ]""";
    Assert.Equal("y", ClaudeApiParsing.ParseOrganizations(chatOnly).OrgId);
    var noCaps = """[ { "uuid": "x" }, { "uuid": "y" } ]""";
    Assert.Equal("x", ClaudeApiParsing.ParseOrganizations(noCaps).OrgId);
}
```

- [ ] **Step 2: Run to verify** — the reversed-ordering assertion FAILS on current code (picks bbbb-2222).

- [ ] **Step 3: Implement** — in `ParseOrganizations`, replace the `orgs[0]` selection with:

```csharp
        // Accounts can carry multiple orgs (a claude_max subscription org AND
        // an API individual org, observed live 2026-07-19). orgs[0] is
        // ordering-luck; prefer the org we actually track usage for.
        JsonObject? selected = null, byChat = null, first = null;
        foreach (var node in orgs)
        {
            if (node is not JsonObject candidate) continue;
            first ??= candidate;
            if (candidate["capabilities"] is not JsonArray caps) continue;
            bool hasMax = caps.Any(c => (string?)c == "claude_max");
            bool hasChat = caps.Any(c => (string?)c == "chat");
            if (hasMax) { selected = candidate; break; }
            if (hasChat) byChat ??= candidate;
        }
        var org = selected ?? byChat ?? first;
        if (org is null)
            throw new NetworkException("Malformed organization entry");
```

(the existing `uuid` extraction, `_account` capture, and exception messages continue unchanged against `org`).

- [ ] **Step 4: Full suite** — expected PASS; if an existing parsing test asserts first-org selection with a multi-org fixture, update it to the new rule and report it.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ClaudeApiParsing.cs windows-dotnet/tests/Sanduhr.Tests/ClaudeApiParsingTests.cs
git commit -m "fix(api): select the claude_max/chat org instead of orgs[0] — two-org accounts rode ordering luck"
```

---

### Task 5: CF-challenge-on-200 classification + WebView2 re-navigation

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/ClaudeApiParsing.cs` (`ParseOrganizations` ~line 38, `ParseUsage` ~line 80)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/WebView2ApiClient.cs` (usage + org core methods)
- Test: `windows-dotnet/tests/Sanduhr.Tests/ClaudeApiParsingTests.cs`

**Interfaces:**
- Produces: both parse entry points throw `CloudflareBlockedException` (not `NetworkException`) when the body fails JSON parse AND `ClaudeApiClient.LooksLikeCloudflare(body)`; valid-JSON bodies never reach the CF check.

- [ ] **Step 1: Failing tests**

```csharp
private const string ChallengeHtml = "<html><head><title>Just a moment...</title></head><body>cf-challenge</body></html>";

[Fact]
public void ParseUsage_ChallengeBodyOn200_ThrowsCloudflareBlocked()
    => Assert.Throws<CloudflareBlockedException>(() => ClaudeApiParsing.ParseUsage(ChallengeHtml, null));

[Fact]
public void ParseOrganizations_ChallengeBody_ThrowsCloudflareBlocked()
    => Assert.Throws<CloudflareBlockedException>(() => ClaudeApiParsing.ParseOrganizations(ChallengeHtml));

[Fact]
public void ValidJson_ContainingCloudflareText_ParsesNormally()
{
    var data = ClaudeApiParsing.ParseUsage("""{ "note": "we love cloudflare", "five_hour": null }""", null);
    Assert.Equal("we love cloudflare", (string?)data["note"]);
}

[Fact]
public void NonCfGarbage_StillThrowsNetworkException()
    => Assert.Throws<NetworkException>(() => ClaudeApiParsing.ParseUsage("<html>plain error page</html>", null));
```

- [ ] **Step 2: Run to verify** — first two FAIL (currently NetworkException).

- [ ] **Step 3: Implement** — in BOTH `catch (JsonException e)` branches:

```csharp
        catch (JsonException e)
        {
            // Cloudflare can serve a challenge page WITH HTTP 200 — a
            // status-only check (ThrowForStatus/CheckAsync handle 403) never
            // sees it, and the generic non-JSON error left the UI stuck on
            // "No connection — retrying…". Only a parse FAILURE consults the
            // CF sniffer, so JSON payloads containing the word "cloudflare"
            // can never false-positive.
            if (ClaudeApiClient.LooksLikeCloudflare(body))
                throw new CloudflareBlockedException("Cloudflare challenge in 2xx body -- cf_clearance needed");
            throw new NetworkException("<endpoint> returned non-JSON", e);
        }
```

(keep each method's existing message string — "Org discovery returned non-JSON" / "Usage endpoint returned non-JSON"; note `<html>plain error page</html>` contains no CF markers so it stays NetworkException).

- [ ] **Step 4: WebView2 re-navigation (App layer, no tests)** — in `WebView2ApiClient`, wrap the `ClaudeApiParsing.ParseUsage(...)` call in `GetUsageCoreAsync` and the `ClaudeApiParsing.ParseOrganizations(...)` call in `GetOrgIdCoreAsync`:

```csharp
        try { data = ClaudeApiParsing.ParseUsage(body, _accountNode); }
        catch (CloudflareBlockedException)
        {
            // The page cleared init but is now wedged behind a challenge —
            // EnsureReadyOrResetAsync only re-navigates when _ready is false,
            // so drop readiness and let the next cycle rebuild the page.
            _ready = false;
            throw;
        }
```

Read the class first: reuse the exact `_ready` field name and match surrounding style; if readiness is tracked differently (e.g. a method), invalidate through that mechanism instead and report the deviation.

- [ ] **Step 5: Full suite + scratch build; commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ClaudeApiParsing.cs windows-dotnet/tests/Sanduhr.Tests/ClaudeApiParsingTests.cs windows-dotnet/src/Sanduhr.App/Services/WebView2ApiClient.cs
git commit -m "fix(api): classify Cloudflare challenge on 2xx as Blocked + re-navigate the wedged WebView2 page"
```

---

### Task 6: Unknown-key logging (once per process)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/WebView2ApiClient.cs`

**Interfaces:**
- Consumes: `TierModel.IsKnown` (covers canonical + dynamic after Task 1).
- Produces: fetch-debug.log lines `usage: unregistered keys: a, b` and `usage: unhandled limit kinds: x` — names only, each name logged at most once per process lifetime.

- [ ] **Step 1: Implement** — in `WebView2ApiClient`, add:

```csharp
    // Names already reported to fetch-debug.log — once per process, so a new
    // upstream vocabulary word logs exactly once instead of every 5-minute
    // cycle. Names only, never payload values (the fetch-debug contract).
    private static readonly HashSet<string> ReportedUsageNames = new(StringComparer.Ordinal);
    private static readonly string[] StructuralUsageKeys = { "_account", "limits", "spend", "member_dashboard_available", "routines" };

    private void LogUnknownUsageMembers(JsonObject data)
    {
        List<string>? keys = null;
        foreach (var kv in data)
        {
            if (StructuralUsageKeys.Contains(kv.Key) || TierModel.IsKnown(kv.Key))
                continue;
            if (ReportedUsageNames.Add("key:" + kv.Key))
                (keys ??= new()).Add(kv.Key);
        }
        if (keys is not null)
            Log($"usage: unregistered keys: {string.Join(", ", keys)}");

        List<string>? kinds = null;
        if (data["limits"] is JsonArray limits)
        {
            foreach (var node in limits)
            {
                if (node is not JsonObject entry) continue;
                var kind = (string?)entry["kind"];
                if (kind is null or "weekly_scoped" or "session" or "weekly") continue;
                if (ReportedUsageNames.Add("kind:" + kind))
                    (kinds ??= new()).Add(kind);
            }
        }
        if (kinds is not null)
            Log($"usage: unhandled limit kinds: {string.Join(", ", kinds)}");
    }
```

Call it in `GetUsageCoreAsync` right after the existing `Log($"usage: status=...")` line: `LogUnknownUsageMembers(data);`

Note: synthesis happens in `UsageFetcher` AFTER this client returns, so scoped keys are not yet injected here — but their FLAT keys aren't in the raw payload anyway (that's the whole problem), and `limits`/`spend` are structural. The five null codenames (tangelo, omelette_promotional, nimbus_quill, cinder_cove, amber_ladder) will each log exactly once on first fetch — that's the desired behavior, not noise.

- [ ] **Step 2: Scratch build + full suite (regression only — App layer untested by design); commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/WebView2ApiClient.cs
git commit -m "feat(api): log unregistered usage keys and unhandled limit kinds once per process"
```

---

### Task 7: CcLogReader fable prefix + smoke additions

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/CcLogReader.cs` (~lines 50-55)
- Test: `windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs` (exists — append)
- Modify: `docs/smoke-test-plan.md` (append a section)

**Interfaces:**
- Consumes: `TierModel.SevenDayFable` (Task 1).

- [ ] **Step 1: Failing test** — follow the file's existing model-attribution test pattern (find one asserting `claude-opus-*` → `seven_day_opus` and mirror it):

```csharp
[Fact]
public void FableModels_AttributeToSevenDayFable()
    => Assert.Equal(TierModel.SevenDayFable, CcLogReader.TierForModel("claude-fable-5"));
```

(If the attribution helper has a different name/shape, adapt the assertion to the file's existing pattern — the contract is: a model id starting `claude-fable` maps to `seven_day_fable`.)

- [ ] **Step 2: Implement** — in `ModelTierPrefixes`, after the opus/sonnet entries, before haiku:

```csharp
        ("claude-fable", TierModel.SevenDayFable),
```

- [ ] **Step 3: Smoke additions** — append to `docs/smoke-test-plan.md`:

```markdown
## Scoped-limits wave (2026-07-19)

1. **The Fable bar.** With live data on a Max account: a "Weekly - Fable" card renders
   between Weekly - Opus and the rest, percentage matching claude.ai; it appears in the
   Settings hide/reorder list, the History chart tier rows, and CSV export headers.
2. **July 20 flip.** After the entitlement change lands upstream, the bar tracks the new
   50% standard allocation with no app update.
3. **Unknown-key log.** fetch-debug.log contains exactly one `usage: unregistered keys:`
   line naming the null codename buckets (tangelo, …) per app session — not one per cycle.
4. **Org stability.** Accounts with a claude_max org + an API org track the Max org's
   usage regardless of API-side org ordering (numbers match claude.ai's settings page).
5. **CC attribution.** Local Claude Code burn on a fable model shows as the Fable card's
   `+Nk` badge, not only in the footer total.
```

- [ ] **Step 4: Full suite; commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/CcLogReader.cs windows-dotnet/tests/Sanduhr.Tests/CcLogReaderTests.cs docs/smoke-test-plan.md
git commit -m "feat(cc): attribute claude-fable burn to the Fable tier + scoped-limits smoke scenarios"
```
