# Sanduhr for Windows: Mac parity — Technical Spec

## Stack
The existing app, unchanged: .NET 10, WPF (`Sanduhr.App`, `net10.0-windows10.0.19041.0`), pure logic in `Sanduhr.Core` (`net10.0`), xUnit in `tests/Sanduhr.Tests`, the network-free `Sanduhr.Mcp` with its own tests. CommunityToolkit.Mvvm for view models, Hardcodet `TaskbarIcon` for the tray, Win32 through P/Invoke (`Interop/`). No new packages in S1.

## Runtime & Deployment
Store MSIX and Velopack, both cut by Este (`docs/release-runbook.md`). Nothing in this cycle bumps a version, edits `Package.appxmanifest` or touches Partner Center. The app version at runtime is `Assembly.GetEntryAssembly().GetName().Version` formatted Major.Minor.Build (as `SettingsViewModel` and `WidgetViewModel` already do); Core's own assembly version is 1.0.0 and is never used for this.

## Deployment — Identity & Signing
Unchanged and out of scope: Store identity and the frozen Velopack packId `626Labs.Sanduhr`. Every stage's PR states "no manifest, packId, vault-location or Credential Manager change" or names the escalation it waits on.

## Architecture Overview
Each Mac feature splits the same way the Windows app already does: the rules (which cards, which homes, is this limit temporary, what does the tray read, is this series steady, is this combo valid) go into a Core class with tests ported from the Mac's tests; the WPF shell binds to it. Settings persist as keys in `%APPDATA%\Sanduhr\settings.json` through `SettingsStore` (a loose `JsonObject`, missing key = default, unknown keys survive), so an existing install reads unchanged.

Two seams are shared across stages and built in S1:
- **The card table and Show me router** (`WhatsNew`, `WhatsNewCard`, `SettingsPage`): S2's tour reuses the card type, the window chrome and the router; S3's names table checks card text.
- **Claude Code home discovery** (`ClaudeCodeHomes`): S4's folder list, the meters mod, the glow hooks and per-seat meters all start from it.

The MCP trust boundary is held: `Sanduhr.Mcp` source-links ten Core files, pinned by `TrustBoundaryTests`. New Core files are never referenced from a linked file. `CcLogReader.cs` is linked, so S1 changes it only by adding a constructor parameter of a BCL type (`Func<IEnumerable<string>>`), with the MCP's own code path unchanged.

---

## Stage 1 (full)

### 1.1 What's New (W1)

**Core: `Sanduhr.Core/WhatsNew.cs`** (port of `mac/Sources/Sanduhr/Models/WhatsNew.swift`).

```csharp
public enum SettingsPage { Accounts, Themes, Alerts, History, ClaudeUsage, Publish, General, Widget }
public enum WhatsNewPreview { None, Tray, Sparkline, Shortcut }
public sealed record WhatsNewCard(IReadOnlyList<string> Versions, string Id, string Title, string Body,
    string Glyph, WhatsNewPreview Preview, SettingsPage Destination)
{   // Versions sorted newest first by the constructor helper; Version = Versions[0]
}
public static class WhatsNew
{
    public const int Cap = 8;
    public static int Compare(string a, string b);                 // trim, split '.', leading digits, missing = 0
    public static IReadOnlyList<string> Shown(WhatsNewCard c, string? lastSeen, string current);
    public static IReadOnlyList<WhatsNewCard> Cards(string? lastSeen, string current, int? limit = Cap, IReadOnlyList<WhatsNewCard>? table = null);
    public static string? RangeLabel(IReadOnlyList<WhatsNewCard> cards, string? lastSeen, string current);
    public static IReadOnlyList<WhatsNewCard> All(string current, IReadOnlyList<WhatsNewCard>? table = null);
    public static (IReadOnlyList<WhatsNewCard> Cards, string Header) ForMenu(string current, string? installed, IReadOnlyList<WhatsNewCard>? table = null);
    public sealed record LaunchDecision(IReadOnlyList<WhatsNewCard> Show, bool Record);
    public static LaunchDecision AtLaunch(string? lastSeen, string current, bool fresh, bool onboarding, bool hidden, bool tour = false, IReadOnlyList<WhatsNewCard>? table = null);
}
```

Rules, exactly as the Mac:
- `Compare`: trim whitespace, split on `.`, each part's leading digits (none = 0), missing parts 0. "3.10.0" > "3.9.1"; "3.5" == "3.5.0"; "3.5.0-beta" == "3.5.0".
- `Shown`: the card's versions `<= current` and (`lastSeen` null or `> lastSeen`), newest first.
- `Cards`: cards with a non-empty `Shown`, sorted by their newest shown version descending, ties in table order (stable sort), first `limit` (null = all). Downgrade or same version shows nothing.
- `RangeLabel`: oldest version shown across the cards; equal to current gives "New in {current}", else "New in {oldest} – {current}" (U+2013 with spaces); null with no cards. The window falls back to "Version {current}".
- `ForMenu`: when `installed == current` and some card covers `current`, those cards in table order with "Highlights in {current}"; otherwise `All` with its range label, falling back to "Version {current}".
- `AtLaunch`, in order: `fresh` gives (no cards, record); `onboarding || tour` gives (no cards, no record); otherwise show `hidden ? none : Cards(...)`, record when `lastSeen != current`.

**Core: `Sanduhr.Core/WhatsNewCards.cs`**: the Windows table, newest first. S1's cards, all version "3.5.0" (Open Question 4):

| Id | Title | Glyph / preview | Destination |
|---|---|---|---|
| `shortcut-keys` | Open Settings from any app | preview Shortcut | General |
| `tray-shows` | Pick what the tray shows | preview Tray | General |
| `limits-come-and-go` | Limits that come and go | glyph | Widget |
| `every-folder` | Every Claude Code folder counts | glyph | ClaudeUsage |
| `steady-sparklines` | Steady meters read steady | preview Sparkline | Widget |
| `whats-new` | What's new, after every update | glyph | General |

Bodies: second person, plain, at most two sentences and 200 characters, no "Claude" without need (a card naming Claude Code is fine; the window carries the disclaimer). Tests pin unique ids, the length and sentence limits, and that every destination is a real page.

**Settings keys** (`SettingsStore`): `whats_new_last_seen` (string), `whats_new_installed_version` (string), `whats_new_hide_after_updates` (bool, default false). Load/Save pairs in the existing style; empty string reads as null.

**Launch wiring** (`App.xaml.cs`, after `_vm.Start()`):
- `fresh` = no `whats_new_last_seen`, no `whats_new_installed_version`, and no saved account (`AccountStore` empty after the legacy-credential migration `Start()` runs). A fresh launch also records `whats_new_installed_version`.
- `onboarding` = no active account.
- Record `current` immediately when the decision says so (before showing), then show the window two seconds later on the dispatcher when there are cards.

**App: `Views/WhatsNewWindow.xaml(.cs)`**: borderless themed window like `ThemedDialog` (DynamicResource brushes `Sanduhr.Brush.*`, rounded glass border, draggable by its background), 640×620, min 520×420, resizable, centered, `ShowInTaskbar` true, single instance (reopening replaces the content and activates).
- Header: the hourglass glyph, "What's New in Sanduhr" (title), the range label below in secondary text.
- Body: `ScrollViewer` over an `ItemsControl` with a two-column `UniformGrid`, 14 px gaps. Empty: "Nothing new to show for this version."
- Card: art box 96 px high (a Segoe Fluent Icons glyph at 40 px in the accent colour, or a preview: Tray = the rendered tray icon beside "Session 12%"; Sparkline = a `SparklineControl` drawing a level line at 96; Shortcut = "Ctrl+Alt+S" keycaps), title (SemiBold 14), body (12, secondary, wrapping), **Show me** button with tooltip "Opens Settings, {page}" (or "Shows the widget").
- Footer: the trademark line "“Claude” and “claude.ai” are trademarks of Anthropic PBC, used nominatively; Sanduhr für Claude is an independent third-party tool." (the manifest's wording), then a row: checkbox **Don't show after updates** (tooltip "What's New stays in the tray menu and Settings either way."), spacer, **Done** (`IsDefault`, `IsCancel` so Escape closes too).
- Accessibility: `AutomationProperties.Name` on each card container ("{title}. {body}"), each Show me ("Show me: {title}"), the checkbox and Done; art is `AutomationProperties.IsOffscreen`/not a control. Tab order: cards' Show me in reading order, checkbox, Done.
- Show me: close, then `App.ShowSettingsAsync(page)` or, for `Widget`, `ShowWindow()`.

**Router**: `ShowSettingsAsync(SettingsPage? page = null)`; `SettingsWindow.SelectPage(SettingsPage)` selects the `TabItem` whose header matches (`Accounts`, `Themes`, `Alerts`, `History`, `Claude Usage`, `Publish usage`, `General`). One mapping table in `SettingsWindow` with a unit-free assertion at startup in DEBUG.

**Entry points**: tray menu "What's New…" (after Settings…); Settings, General: a **What's New…** button beside the version line. Both use `ForMenu`.

### 1.2 Global shortcut with a recorder

**Core: `Sanduhr.Core/HotKeyCombo.cs`** (semantics of `mac/.../Models/HotKeyCombo.swift`, keys in Win32 terms).

```csharp
[Flags] public enum HotKeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }  // = MOD_* values
public readonly record struct HotKeyCombo(int VirtualKey, HotKeyModifiers Modifiers)
{
    public bool HasRequiredModifier => (Modifiers & (Alt | Control | Win)) != 0;
    public string Display { get; }          // "Ctrl+Alt+S": order Ctrl, Alt, Shift, Win, then key name
    public static HotKeyCombo? Parse(string text);   // case-insensitive "ctrl+alt+s", "Win+Shift+F5"
}
public enum HotKeyAction { Settings }       // Join arrives with meetings (S6)
public static class HotKeys
{
    public static HotKeyCombo Default(HotKeyAction a);           // Settings: Ctrl+Alt+S (VK 0x53, Control|Alt)
    public static string Label(HotKeyAction a, HotKeyCombo c);   // "{c.Display} opens Settings"
    public static HotKeyRefusal? Check(HotKeyCombo c, HotKeyAction a, IReadOnlyDictionary<HotKeyAction, HotKeyCombo> current);
    public static string Caption(IReadOnlyDictionary<HotKeyAction, HotKeyCombo> current);
}
public sealed record HotKeyRefusal(string Kind, string Note);   // Kind: "modifier" | "taken" | "os"
```

- Key names: A to Z, 0 to 9, F1 to F24, Space, Enter, Tab, Backspace, Delete, Insert, Home, End, PageUp, PageDown, arrows ("Left", "Right", "Up", "Down"), OEM keys by their US glyph (`- = [ ] ; ' , . / \ \``), numpad as "Num 0" to "Num 9"; anything else "Key 0x{vk:X2}".
- `Check` order: no Ctrl/Alt/Win gives `modifier`, "Add Ctrl, Alt or Win: Shift alone isn't enough."; another action holding the same combo gives `taken`, "{combo} already {other label tail}." (no other action exists in S1, but the rule is in place for Join). Pure modifier virtual keys (Shift, Ctrl, Alt, Win, both sides) are never a combo.
- Registration failure is not a Core concern: the App turns `ERROR_HOTKEY_ALREADY_REGISTERED` (1409) into `HotKeyRefusal("os", "Another app already uses {combo}. Pick another.")`.
- Caption: "Works in every app whenever Sanduhr runs. While it's on, {combo} goes to Sanduhr instead of the app in front."

**Settings key**: `hotkey_settings` = `{"on": bool, "vk": int, "mods": int}`. Missing, a half-written entry, a `vk` outside 1..0xFE or `mods` without a required modifier reads as the default combo; `on` missing reads as false. Storing the default combo omits `vk` and `mods`.

**App: `Services/GlobalHotKeyService.cs`**: owns a message-only `HwndSource` (parent `HWND_MESSAGE`), `RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk)` per enabled action, `WM_HOTKEY` (0x0312) dispatch to the action (`Settings` → `ShowSettingsAsync()`), `Suspend()`/`Resume()` for the recorder, `Apply(settings)` re-registers only on change, unregisters on exit. P/Invoke in `Interop/HotKeyNative.cs`. Reports the last registration failure per action so the row can show it.

**App: Settings, General, "Shortcuts" section** (new `ShortcutsViewModel` on `SettingsViewModel`): one row, "{combo} opens Settings": a keys button (min width 96) and a switch (`ThemedCheckBox`), a **Reset to Ctrl+Alt+S** link shown only when the combo differs from the default, and a note line (refusal or registration failure, orange; otherwise empty). Recording: click the keys button; it reads "Type keys…", tooltip "Press the new keys, or Escape to cancel."; the service is suspended; `PreviewKeyDown` (using `e.SystemKey` when `e.Key == Key.System`) ignores pure modifier keys, Escape cancels, anything else is checked and saved (a refusal keeps recording and shows its note). Accessible names: "Change keys, Shortcut to open Settings" / "Recording keys, Shortcut to open Settings"; the switch "Shortcut to open Settings".

### 1.3 Claude Code home discovery (W7)

**Core: `Sanduhr.Core/ClaudeCodeHomes.cs`** (port of the discovery half of `mac/.../Services/ClaudeCodeFolders.swift`).

```csharp
public static class ClaudeCodeHomes
{
    public const string DefaultName = ".claude";
    public static bool IsCandidateName(string name);   // ".claude" or ".claude-" + at least one char
    public static bool IsBackup(string name);          // contains ".config-backup-" or starts ".claude-backup-"
    public static bool LooksLikeHome(string dir, string profileDir);  // dir exists; dir/projects is a directory, or ConfigFile exists
    public static string ConfigFile(string dir, string profileDir);   // placement rule below
    public static IReadOnlyList<string> Discover(string profileDir, string? claudeConfigDir);  // basenames
}
```

- `Discover`: names directly in the profile that pass `IsCandidateName`, drop `IsBackup`, `.claude` first then the rest in natural order (digit runs compared as numbers, case-insensitive), keep `LooksLikeHome`. Then `CLAUDE_CONFIG_DIR` when non-empty, expanded (`~`, environment variables), full-path-normalized, its parent equal to the profile directory, not already listed, looking like a home: appended last by its basename. A folder outside the profile is skipped (roots are keyed by basename today; PRD "with more time").
- `ConfigFile`: for any name but `.claude`, `dir/.claude.json`. For `.claude`: outer `profile/.claude.json`, inner `profile/.claude/.claude.json`; only inner exists gives inner; both exist: the signed-in one wins when exactly one parses as JSON with an object `oauthAccount`, else the strictly newer write time wins for inner, ties and unknowns go to outer; otherwise outer. Nothing inside `oauthAccount` is read.
- Every IO failure reads as "not a home"; `Discover` never throws.

**CcLogReader** (linked into the MCP): add `public CcLogReader(string? homeDir, Func<IEnumerable<string>>? rootNames)`; `SearchRoots()` uses `rootNames?.Invoke() ?? LogRootNames`. The existing one-argument constructor is unchanged, and the MCP never passes a provider.

**Callers moved to discovery**: the App constructs one reader (`WidgetViewModel.cs:475`, `_ccReader = new CcLogReader()`), shared as `WidgetViewModel.CcReader` with `VaultService` and `UsagePublishService`; it becomes `new CcLogReader(null,` `() => ClaudeCodeHomes.Discover(profile, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"))`. Vault and publish consent are unchanged: a root absent from `vault_roots` or the publish map is off, and the existing per-root toggles list it.

**Unchanged on purpose (escalated)**: `Sanduhr.Mcp/McpConfig.cs` `KnownRootNames`, `StatuslineInstaller.KnownHomeNames`, `McpIntegrationInstaller.KnownHomeNames`.

### 1.4 Limits that come and go (W2)

**Core: `Sanduhr.Core/LimitLifetime.cs`** (port of `LimitLifetime.swift` and the rules of `MeterVisibility.swift`).

```csharp
public sealed record HiddenLimitRecord(string? ResetsAt, double? Utilization);
public static class LimitLifetime
{
    public static readonly TimeSpan LongReset = TimeSpan.FromDays(8), NewWindow = TimeSpan.FromDays(14), NewResetSlack = TimeSpan.FromHours(1);
    public const double RefillPoints = 10;
    public static bool IsPermanent(string tier);      // five_hour, seven_day
    public static bool IsKnown(string tier);          // permanent + sonnet, opus, fable, cowork, omelette, oauth_apps, extra_usage, routines
    public static bool IsTemporary(string tier, string? resetsAt, DateTimeOffset now, DateTimeOffset? firstSeen);
    public static string? ShouldReturn(HiddenLimitRecord rec, string? resetsAt, double? util);  // "new_window" | "refill" | null
    public static ReconcileResult Reconcile(LimitState state, IReadOnlyDictionary<string, (string? ResetsAt, double? Util)> present, DateTimeOffset now);
}
public sealed record LimitState(IReadOnlyDictionary<string, DateTimeOffset> FirstSeen,
    IReadOnlyDictionary<string, HiddenLimitRecord> HiddenUntilReset);
public sealed record ReconcileResult(LimitState State, IReadOnlyDictionary<string, string> Returned);
```

- `IsTemporary` in order: permanent gives false; `iguana_necktie` gives true; `resetsAt` parses and is more than 8 days after now gives true (exactly 8 days false); `firstSeen` set, `now - firstSeen < 14 days` and not known gives true; else false.
- `ShouldReturn`: `new_window` when both resets parse and the new one is more than 1 hour after the recorded one (strictly); `refill` when both utilizations exist and `recorded - now >= 10`; else null.
- `Reconcile`: record first sightings (never overwrite); for each hidden-until-reset tier present in this usage: not temporary any more gives `not_temporary`, else `ShouldReturn`; returned tiers leave the map; absent tiers stay hidden.
- Hide: allowed only when `IsTemporary`; stores the current reset and utilization.

**Settings keys**: `limit_first_seen` `{tier: ISO-8601}`, `limits_hidden_until_reset` `{tier: {"resets_at": string?, "utilization": number?}}`, `alert_muted_tiers` `[tier]`. Existing `hidden_tiers` keeps its meaning (manual hide).

**WidgetViewModel**: on each successful fetch, `Reconcile` before `RenderCards` and persist; `RenderCards` passes `manualHidden ∪ hiddenUntilReset` to `TierModel.ActiveTiers`; `BuildAlertSnapshots` drops `hiddenUntilReset` and `alert_muted_tiers` tiers (manual hidden still alert, as today). New commands: `HideUntilReset(tier)`, `ShowTier(tier)` (removes from both hidden sets), `SetWarnings(tier, on)`, and `HiddenTiers` for the menu.

**TierCard context menu** (built on open from the card's view model): "Hide until it resets" (only when temporary and not hidden), "Hide this tier" (as today), "Stop warnings for this limit" / "Warn again for this limit", a separator, a "Hidden limits" submenu listing every hidden tier by label (click shows it again; absent when none), "Alerts Settings…". `TierCardViewModel` carries `IsTemporary` and `WarningsOn`.

### 1.5 What the tray shows (W3)

**Core: `Sanduhr.Core/TrayReadout.cs`** (port of `MenuBarMode.swift`'s reading rules plus Windows' existing mode).

```csharp
public enum TrayMode { Highest, Session, Weekly, Higher, Rotate }   // settings value: "highest", "session", "weekly", "higher", "rotate"
public sealed record TrayReading(int Percent, string Label);         // Label: "Session", "Weekly", or the tier label for Highest
public static class TrayReadout
{
    public static readonly TimeSpan RotateInterval = TimeSpan.FromSeconds(8);
    public static string Title(TrayMode m);   // "Highest of any limit", "Session", "Weekly", "Whichever is higher", "Rotate (session and weekly)"
    public static TrayMode Parse(string? raw); // unknown or missing: Highest
    public static TrayReading? Reading(TrayMode mode, double? session, double? weekly, (string Label, double Util)? highestVisible, int step);
}
```

- Session: session, else weekly, else null. Weekly: weekly, else session. Higher: weekly only when strictly greater; one present uses it. Rotate: even step prefers session, odd weekly, falling back to the other. Highest: the highest visible tier (today's behavior). Percent is truncated (`(int)util`). Null means "--".
- Session and weekly are read from the fetch data whether or not their cards are hidden.

**Wiring**: `WidgetViewModel` raises `TrayReadingChanged(TrayReading?)` in place of `TrayPercentChanged(int)` (all call sites, including the -1 resets, become null). `App` keeps the last inputs, a `DispatcherTimer` ticking every 8 s only while Rotate is chosen, renders `TrayIconRenderer.Render(percent)` and the tooltip "Sanduhr für Claude — {Label} {n}%" (or "— no data yet"). Settings key `tray_mode`. Tray menu: a "Tray Shows" submenu with the five titles as checkable items; Settings, General: a "Tray shows" combo with the same titles and the caption "The number on the tray icon. Rotate switches between session and weekly every 8 seconds."

### 1.6 Steady sparklines (W5)

**Core: `Sanduhr.Core/SparklineSteady.cs`**.

```csharp
public enum SparklineDraw { Classic, Horizon, Level }
public static class SparklineSteady
{
    public const double SteadySpread = 2;
    public static SparklineDraw Drawn(SparklineStyle style, IReadOnlyList<double> values);  // Horizon and non-empty and max-min < 2 gives Level
    public static double LevelY(IReadOnlyList<double> values, double height);              // level = clamp(mean, 0, 100); y = h - level/100*(h-2) - 1
}
```

**SparklineControl**: `OnRender` asks `Drawn`; Level paints a wash rectangle from `y` to the bottom at 12% of the line colour and a 1.5 px round-capped line across the width at 85%. Classic and Horizon are unchanged.

### 1.7 Tests for S1
Ported from `mac/Tests/SanduhrTests/{WhatsNewTests, HotKeyComboTests, ClaudeCodeFoldersTests, LimitLifetimeTests, MeterVisibilityTests, MenuBarTextTests, SparklineModeTests}.swift`, retargeted to Windows values. New files under `tests/Sanduhr.Tests/`: `WhatsNewTests.cs`, `WhatsNewCardsTests.cs`, `HotKeyComboTests.cs`, `ClaudeCodeHomesTests.cs` (temp-directory fixtures), `LimitLifetimeTests.cs`, `TrayReadoutTests.cs`, `SparklineSteadyTests.cs`, plus `SettingsStore` round-trips where the store is testable, and a `CcLogReader` test that the provider overload is honored and the one-argument constructor is unchanged. `TrustBoundaryTests` must pass untouched.

---

## Stage 2: welcome tour and a clearer first run
- **Core `WelcomeTour.cs`**: steps as data (`Step`, `Requires`, `Choice`) reusing `WhatsNewCard`'s fields; `Visible(steps, features)`; `AtLaunch(fresh, finished, firstFetchOk)`; key `tour_state` ("pending" | "finished" | "skipped" | "not_offered", decided once at launch as the Mac does). Finishing or skipping records `whats_new_last_seen`, and `WhatsNew.AtLaunch(tour: pending)` yields to it. Mac reference: `WelcomeTour.swift`, `WelcomeTourSteps.swift`, `WelcomeTourWindow.swift`.
- **Window**: the What's New window in step mode ("Welcome to Sanduhr · 1 of N", Back, Next, Skip the tour); keyboard Enter = Next, Escape = Skip.
- **Signed-out widget**: "Not signed in" with Sign In, model controls hidden (`SignedOut.swift`).
- **Demo mode** (`DemoMode.swift`): a session-only switch that pauses fetches and feeds sample data to the cards, tray, tour and Settings previews; never writes history, snapshot or vault.

## Stage 3: Settings v2
- Sidebar navigation replaces the tab strip; pages per `docs/settings-v2-spec.md`, minus the Mac-only pages (Notch); the Desk pages appear with S5. As built: the `TabControl` re-templated as a grouped sidebar (each `TabItem` tagged with its `SettingsPage`), so every page, the router and the Claude Usage refresh stay as they were. The statusline and MCP installers moved to the Claude Code page in S4 (PRIVACY.md names it).
- `SettingsNames.cs` (the names table) and `SettingsIndex.cs` (search entries and synonyms) in Core; a test checks the tray menu, card and tour strings against the names table.
- `SettingsMigrations.cs`: one-time, idempotent moves of any key whose meaning changes.
- About: version, What's New…, Take the Tour…, the trademark disclaimer, Third-Party Notices, where settings live (Show in Explorer).
- Reset Sanduhr… (escalated): ordered steps, Claude Code receipts first, a dry-run report testable without deleting.

## Stage 4: the Claude Code side

Este's rulings of 2026-10-11 (decision 6h5YrnXr0YVjPqlqqi8i) are built here: the MCP and installer folder set widens to discovered folders behind consent, the MCP publish race is fixed, and the sign-in window carries the trademark notice from one source constant.

### 4.0 Rulings
- **MCP folder set.** `Sanduhr.Mcp` source-links `ClaudeCodeHomes.cs` (BCL only: `System.IO`, `System.Text.Json`) and resolves roots as `ClaudeCodeHomes.Discover(profile, CLAUDE_CONFIG_DIR)` filtered by `mcp_roots[name] == true`. A consent key is only ever looked up for a discovered name; no path is ever built from a consent key or from `cc_folders_added`, so a folder added by hand never reaches the MCP. `RootsFound` reports discovered names. `TrustBoundaryTests` gains exactly `ClaudeCodeHomes.cs`. The statusline and MCP installers list `Discover`'s folders; `McpIntegrationInstaller.ConfigPathFor` uses `ClaudeCodeHomes.ConfigFile` (one placement rule).
- **Publish race.** `BuildPublish` writes `publish-request.json` through a temp sibling and an atomic replace, as `propose_theme` does. `PublishHandoff.TryReadRequest` leaves an unparseable file younger than 10 s for the next look (a writer may be mid-write) and deletes an older one. Tests: the request is replaced, not rewritten in place (a handle opened on the old file keeps reading the old bytes); a fresh partial file survives one read and a stale one is deleted.
- **Trademark notice.** `SettingsNames.TrademarkNotice` is the one in-app source: About, the What's New footer and a new footer on the sign-in window bind to it. The manifest's wording is untouched.

### 4.1 Edits with exact undo (Core)
- `JsonEdit.cs`: port of `JSONEdit.swift` over UTF-8 bytes (top object, members, nested object and array spans, `Set`, `Remove`, `Restore`, empty-inner handling, insert indentation copied from the neighbour). Validates with `JsonDocument` before scanning.
- `ConfigCommit.cs`: compare-and-swap commit (re-read, abort when the bytes changed, up to three tries), backup of the first change to `%APPDATA%\Sanduhr\backups\<file-hash>-<stamp>.json` (never beside the file), write to a hidden temp sibling and replace. A file that isn't a JSON object is never written.
- `ReceiptLog.cs` and `IntegrationReceipt.cs`: receipts in `%APPDATA%\Sanduhr\integrations\installs.json`, read record by record; an unreadable record is kept byte for byte and reported; a file that isn't an array is moved aside to `installs.json.unreadable`, never overwritten.
- `ConfigMask.cs`: the one mask (env values; key paths naming token, key, secret, password, credential; values inside commands, args and URLs).

### 4.2 The Claude Code page and Mods & Config (App)
- New pages `SettingsPage.ClaudeCode` ("Claude Code") and `SettingsPage.Mods` ("Mods & Config"), names and search entries in `SettingsNames`/`SettingsIndex`.
- **Claude Code**: the folder list (discovered, then folders added by hand in `cc_folders_added`, with Add folder… and Forget folder), and per Sanduhr piece (statusline, MCP server, meters mod, glow hooks) an Install…/Remove pair for a chosen folder behind the consent dialog. The statusline and MCP installers and the MCP folder checkboxes move here from Claude Usage; PRIVACY.md's page paths follow. `statusline_enabled` keeps its key and now means "a Claude Code piece that reads `snapshot.json` is installed": true when the statusline or the meters mod is installed in any known folder, false when none remains; it is recomputed after every install or remove and when the page loads. `ResetGuard.Done` is set when Reset starts its steps, after which nothing writes `settings.json`, `snapshot.json`, history or the vault.
- **Mods & Config** (slice 1, read-only): `ModInventory.Read(folder)` reports, for one folder, `settings.json` readable or unreadable with the parse position, the statusline (Sanduhr's, combined, or someone else's, command masked), hooks per event with Sanduhr's marked, `enabledPlugins`, the mods in `env.CLAUDE_CODE_PLUGIN_DIRS`, MCP server names (user scope, names only), `CLAUDE.md` presence and size, and retention (`cleanupPeriodDays`, warning when unset). Reads files only, never runs anything. The meters mod switch (On, Off, Update, Remove) sits on this page.
- Not in this stage (spec slices 2 to 5): the Catalog, the precedence walker, drift, the seat panel, `configEdit` edits and CLI-routed installs.

### 4.3 The meters mod
The mod is shared with the Mac: the build copies `mac/integrations/mods/sanduhr-meters` (manifest, hooks sources and types, no tests) into the app's `mods\sanduhr-meters`. Install copies it to `%APPDATA%\Sanduhr\mods\<stamp>\sanduhr-meters` and adds that path to `env.CLAUDE_CODE_PLUGIN_DIRS` in the chosen folder's `settings.json` (`;`-separated on Windows, per the Claude Code env-vars docs), keeping any folders listed. Off writes `enabledPlugins["sanduhr-meters@inline"]: false`, On undoes it, Update moves a pinned entry to the current stamp, Remove takes out the switch then the entry. All through receipts; Remove returns the file byte for byte.

### 4.4 Statusline combine
The installer moves to the splice engine with a receipt. When the folder already has someone else's `statusLine`, the consent dialog offers Replace or Combine. Combine keeps their command (base64 in Sanduhr's command line, as the Mac's `--chain-b64`): the script runs it first with the same stdin (Git Bash when present, else PowerShell, as Claude Code would, 2 s timeout) and prints their output, then Sanduhr's segments on a line of their own or appended to their last line. A segment picker chooses Sanduhr's segments (session, weekly, weekly reset). Remove restores their `statusLine` byte for byte. The Mac's looks and per-segment picks are not ported in this stage.

### 4.5 Glow when Claude needs you
- Hooks: one Sanduhr group in each of `hooks.Notification` (matcher `permission_prompt|idle_prompt|elicitation_dialog|agent_needs_input`) and `hooks.Stop`, `"shell": "powershell"`, `"async": true`, `"timeout": 5`, running one line that touches `%APPDATA%\Sanduhr\glow-waiting.signal` (or `glow-done.signal`) when that folder exists. Nothing from Claude Code's hook input is read or passed on. Installed per folder through receipts.
- The widget watches the two signal files (`FileSystemWatcher`, last write time), applies `ClaudeCodeGlowLimiter` (20 s per kind, no done within 5 s of waiting) and `ClaudeCodeGlowRules` (skipped while a known terminal or editor is the foreground process), and shows the glow plus `FlashWindowEx` on the widget. Settings key `claude_code_glow` `{waiting, done, skip_terminal}`, both kinds off by default.
- Where it glows (Este's ruling, 2026-10-11): at the top of the window the user is working in. The target is the foreground window's frame (`DWMWA_EXTENDED_FRAME_BOUNDS`, per-monitor DPI). The glow is an edge light attached to that top edge: a bright line across the middle 50% of the width, fading about 24 px down into the window, so it reads as part of the window rather than stranded. Waiting uses the live theme's accent, done its success color, light themes included. With no usable foreground window (the desktop or shell, a minimized window, full-screen exclusive, or Sanduhr's own windows) it falls back to the top edge of the widget's monitor in the same style. Click-through, never activated, about 1.5 s. Core `GlowPlacement` computes the rect from the target and work-area rects, so placement is tested without a screen.

### 4.6 Account follow and per-seat meters
- `AccountFollow.cs`: the Mac's decision rules with their tests. Wired per Este's ruling of 2026-10-11 (AwvJCwPCmizEVZKcFQ2l): each inactive account gets its own WebView2 transport profile at `%APPDATA%\Sanduhr\webview2-<account_ref>` (inside the un-virtualized folder), built on demand and disposed after its check; Sign Out, Remove Account and Reset delete it, and About's "Where settings live" names it. `AccountFollower` (App) runs the check every 15 minutes while "Follow the account I'm using" is on (`follow_account`, off by default), records readings, asks `AccountFollow.Decide`, and switches. `SeatMeters` (App) fetches an inactive linked account after each active fetch when following saw it in use or its folder's newest session log is under 10 minutes old (file times only), at most once per refresh interval, staggered 5 s.
- Per-seat meters: an account can link a Claude Code folder (`account_folders` `{label: path}`). With any link, each good fetch of the active account also writes `snapshot.<account_ref>.json` (the same schema 1) and `seats.json` (`{"schema_version": 1, "seats": [{"folder", "account_ref"}]}`), both beside `snapshot.json`; a signed-out account's file says signed out; unlinking or removing deletes its file and row; no link means neither file exists. The statusline script looks its `CLAUDE_CONFIG_DIR` up in `seats.json` as the Mac's does. Inactive linked accounts are read by `SeatMeters` above. `snapshot.json` and the MCP are unchanged; the MCP reading seat files is a trust-boundary change and is not built. The mod's own seat lookup is macOS-only and stays so until its test runner can run on Windows.

### 4.7 Reset Sanduhr… (Este's option C, 2026-10-11)
Settings, About, **Reset Sanduhr…** opens a themed dialog with five checkboxes, all checked: "Sanduhr's entries in Claude Code (statusline, MCP server)", "Accounts and sign-in keys", "Usage history and the vault", "Your themes", "Settings". Under the first: "An entry you leave unchecked can't be removed from Sanduhr later." Reset runs the checked steps in that order, each reporting its failures without stopping the rest, shows the results, and quits.
- Claude Code entries: every receipt in `installs.json` undone through the receipt engine (meters mod, glow hooks, statusline), and the MCP entry through `McpIntegrationInstaller.Deregister`; an entry is removed only while it still points at Sanduhr's files. Nothing promises a byte-for-byte return; backups stay in `backups\`.
- Accounts: `AccountStore.RemoveAccount` for each account (Credential Manager), the publish token, the per-account WebView2 profiles, `webview2-fetch` and the sign-in profile.
- History: `history.*.json`, `snapshot*.json`, `seats.json`, the vault under `%LOCALAPPDATA%\Sanduhr\vault`, handoff and signal files.
- Themes: `themes\`. Settings: `settings.json` last.
- Core `ResetPlan` builds the ordered steps from the choices and runs them against injected actions, so the order and the keep-going rule are tested without deleting anything.

## Stage 5: Desk foundation (deepened 2026-10-11)

Este's bar (2026-10-11): "Desk on Windows will probably replace Rainmeter for me if we do it right." Rainmeter's "On desktop" skins are the benchmark, so the Desk window has eight acceptance checks, (a) to (h) below, and the spike answers them before any Desk code exists.

### 5.0 The WorkerW spike (run 2026-10-11 on Dunder, Windows 11 25H2, build 26220)

A throwaway WPF program (never committed) built borderless per-pixel-transparent `HwndSource` windows three ways and queried them. Dunder had **one monitor, 3840x2160 at 96 dpi (100%)**, and its desktop was **completely covered** by a maximized File Explorer window and others (0 uncovered points in a 24 px scan of the work area). Rainmeter was not running. Explorer was never restarted; wallpaper, icons, display settings and the registry were untouched; spike windows lived seconds and were closed; no image was saved.

**Desktop structure on 26220.** The 24H2 layout, already in place before the spike sent `0x052C`: `Progman` (`WS_EX_NOREDIRECTIONBITMAP`) holds two full-screen children, top to bottom `SHELLDLL_DefView` (layered, `LWA_ALPHA` 255, with `SysListView32` inside) and `WorkerW` (`WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, the wallpaper host). The fifteen top-level `WorkerW` windows are hidden 136x39 stubs. So on current builds "the `WorkerW` behind `SHELLDLL_DefView`" is the `WorkerW` child of `Progman`, below the icons.

The variants: **V1** (the brief's route) a child of that `WorkerW`; **V2** a child of `Progman` placed above `SHELLDLL_DefView`; **V3** a top-level tool window owned by `Progman`, sent to `HWND_BOTTOM` (Rainmeter's "On desktop" shape).

| Check | Verdict | Evidence |
|---|---|---|
| (a) above the wallpaper, below normal windows, stays | **Not proven** (V1, V2, V3) | All three parented as intended and kept their parent, rect and visibility after 1.9 s and after settings messages (V1 chain `spike -> WorkerW -> Progman`, the only child of `WorkerW`). "Below normal windows" holds by structure for V1 and V2 (children of the bottom top-level window) and screen pixels over the piece read the covering Explorer window, not the piece. "Above the wallpaper" could not be seen: no desktop pixel was uncovered, `PrintWindow` of `Progman` returns black (no redirection bitmap), and a DWM thumbnail of `Progman` and a test window's pixels read inconsistently, so screen sampling was unreliable in this session. |
| (b) click-through outside pieces, clicks on pieces | **FAIL for V1** | At the piece's centre and at its transparent band, the topmost child of `Progman` is `SHELLDLL_DefView` (`ChildWindowFromPointEx`, skipping invisible and transparent windows), and both `SHELLDLL_DefView` and `SysListView32` answer `WM_NCHITTEST` with `HTCLIENT`. Empty desktop must take clicks for the desktop menu and rubber-band selection, so on 24H2 and later the icons view sits over every point of a `WorkerW` child: **a Desk in the `WorkerW` never receives a click**, whatever its `WM_NCHITTEST` says. For V2 the topmost child at the same points is the spike window. Real mouse routing (layered alpha, cross-thread `HTTRANSPARENT`) could not be exercised on a covered desktop. |
| (c) per-monitor placement and DPI | **PASS, single monitor only** | Wanted and got rects matched to the pixel for all three variants; `GetDpiForWindow` 96, WPF `TransformToDevice` 1.00, monitor 96. **Mixed DPI was not tested** (one monitor). |
| (d) recovery on `TaskbarCreated` | **PASS (simulated)** | A hidden top-level (not message-only: message-only windows never get broadcasts) `HwndSource` received the registered `TaskbarCreated` message (posted to that window only, so no other app re-added its tray icon), destroyed the Desk window as Explorer's exit would, re-found the `WorkerW` and rebuilt it: same rect, chain `spike -> WorkerW -> Progman`. **A real Explorer restart was not tested.** In a real restart the child is destroyed with its parent, so recovery means rebuild, never re-parent. |
| (e) Show desktop leaves the Desk visible | **Not tested** | Win+D and `ToggleDesktop` minimize Este's windows and restoring is not guaranteed, so neither was sent. By structure V1 and V2 are part of the desktop window Show desktop reveals; V3 depends on staying owned by `Progman`. |
| (f) coexistence with Rainmeter | **Not tested** | Rainmeter was not running on Dunder. |
| (g) idle cost | **Not measured** | A spike window is not the Desk; measured on the Desk window before the PR. |
| (h) stays put on wallpaper, theme, display change | **Partial PASS** | `WM_SETTINGCHANGE` (`SPI_SETDESKWALLPAPER`), `WM_THEMECHANGED`, `WM_DWMCOLORIZATIONCHANGED` and `WM_DISPLAYCHANGE`, sent to the spike window only: rect and parent unchanged in all three variants. A real wallpaper change (which may rebuild Explorer's `WorkerW`), a theme change, monitor sleep and wake, and a DPI change were not tested. |

**Outcome: the route as specified fails (b) and is unproven on (a).** Per the stage's rule the Desk is not built on it. Escalated to Este with two alternatives:
1. **V2, a child of `Progman` above `SHELLDLL_DefView`** (on pre-24H2 builds, a child of whichever `WorkerW` hosts the icons view). Still the desktop layer: below every normal window, never minimized by Show desktop, rebuilt on `TaskbarCreated`, and the topmost child where it draws, so pieces can take clicks while fully transparent pixels pass to the icons. Draws over desktop icons (the Mac's Desk does too). Unproven: its pixels on screen and real click routing.
2. **V3, a bottom-most top-level tool window owned by `Progman`** (Rainmeter's "On desktop"): `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, layered, kept above `Progman` by ownership and re-sent to the bottom on `WM_WINDOWPOSCHANGING`. Clickable like any window; the known risk is Show desktop, which Rainmeter answers with extra handling.

Either needs one run on a desktop with a visible area (Este's call: a quiet minute with windows minimized, or a second session) to close (a), (b) and (e).

**Ruling (Este, 2026-10-11): V3, Rainmeter's way.** The live checks (a), (b) and (e) wait for Este to clear his desktop; the runner is a scratchpad tool, not shipped, and Stage 5 does not merge until they pass.

### 5.1 The Desk on V3 (deepened against the Mac and Rainmeter)

What ships in S5, with the Mac's names: the layout string and its rules (`DeskArrangement.swift`, `DeskAnchorGeometry`), the clock and date, the Claude line and the meters (`DeskMeters.swift`), EsteFont, taskbar clearance (the Windows answer to `DockClearance.swift`), the layout map in Settings (`DeskLayoutMap.swift`). Not in S5: the message, meetings, now playing, watchers (S6), arrange by dragging, piece clicks, sign-in reminders as a switch (S7), and the notch (cut). The Mac draws its Desk on the main screen only; Windows draws it on every monitor, each laid out against its own work area.

#### The window: `IDeskHost` and `DesktopLayerHost`

Everything except the host talks to `IDeskHost` (App, `Desk/IDeskHost.cs`): the host owns the HWNDs, their placement and their z-order; the controller hands it one WPF element per screen and hears `ScreensChanged`. `Screens` reports each monitor as a Core `DeskScreen` (device name, bounds and work area in physical pixels, DPI scale, taskbar insets).

`DesktopLayerHost` is the V3 route. Rainmeter's "On desktop" mechanism (read from its source, `Library/System.cpp` and `Library/Skin.cpp`, not copied):
- **Rest position.** Top-level windows, one per island (a stack of pieces at one place) on each monitor: `WS_POPUP`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, per-pixel transparent (`HwndSource.UsesPerPixelTransparency`), owned by the shell window (`GetShellWindow()`, `Progman`) as Este ruled, and sent to `HWND_BOTTOM`, which leaves a window just above the desktop and below every normal window. `WM_WINDOWPOSCHANGING` adds `SWP_NOZORDER` unless the host itself is moving it, so a click, Alt+Tab or another app's reordering never raises it; `WM_MOUSEACTIVATE` answers `MA_NOACTIVATE`. DPI: the process is system aware (as shipped; changing that would touch every window), so the host creates the Desk windows under a per-monitor-v2 thread context (`SetThreadDpiAwarenessContext`), reads monitors in physical pixels the same way, and scales each window's content by the monitor's scale over WPF's, so every monitor draws crisply at its own DPI.
- **Show desktop.** Win+D and the taskbar corner raise the desktop icons host (`Progman` on 24H2 and later; before it, the `WorkerW` holding `SHELLDLL_DefView`) above the normal windows. **Ownership does the work:** Este's live run (2026-10-11) showed Win+D with the watcher off left the Desk visible and above the host, carried up by its owner; the corner did the same. The first watcher, modeled on Rainmeter, lifted the Desk below the backmost topmost window and made it vanish (Windows hides topmost windows in Show desktop) and pulled it out of its owner's band, so check e1 failed. Now Core `ShowDesktopRule` acts only on the measured failure, a Desk window below the icons host while the desktop is the foreground and no app is in front, and then re-seats it just above the host in the normal band, never topmost; it drops back to the bottom as soon as any visible non-tool window other than the Desk is in front (Sanduhr's Settings included). A Desk above the host, at rest or carried up, is never touched. A hidden sentinel kept just above the icons host at rest (`HWND_BOTTOM` would put it below `Progman`) tells the `WM_WINDOWPOSCHANGING` guard when the host is raised, so the owner may carry its windows up; otherwise the guard refuses restacks the host did not ask for, except moves down toward the desktop. Checked on `EVENT_SYSTEM_FOREGROUND` and on a timer (250 ms while re-seated or just after the desktop took the foreground, 2 s otherwise). After an Explorer restart (a Desk window destroyed unasked, or `TaskbarCreated`) the next show builds new windows owned by the new shell window instead of reusing the dead ones.
- **Recovery.** A hidden top-level window (never message-only, which misses broadcasts) hears `TaskbarCreated` (Explorer restarted: the owner died and took the windows with it), `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE` with `SPI_SETWORKAREA` (taskbar moved, auto-hide toggled) and `WM_DPICHANGED`; each raises `ScreensChanged` once (coalesced over 500 ms) and the controller rebuilds.
- **Clicks.** Fully transparent pixels pass clicks through at the OS level; drawn pieces take them. S5 has no click actions (S7), so every drawn piece is also click-through: the content root sets `IsHitTestVisible = false` and the window adds `WS_EX_TRANSPARENT`. The runner still proves a piece can take a click with `WS_EX_TRANSPARENT` off, which S7 needs.
- Desk windows are never activated, never in Alt+Tab or the taskbar, and never take focus.

#### Core (pure, tests ported from the Mac)

- **`DeskArrangement.cs`**: `DeskAnchor` (`TL TC TR ML MR BL BC BR`; layout words `tl` … `br`), `DeskAnchors` (`Word`, `Parse`, `Column`, `Row`, `Name` "Top left" …, `At(column, row)`), `DeskPlacement(Widget, Anchor, Scale)`, `DeskArrangement`: parse and write the layout string (`widget:anchor[:scale]`; unknown widget dropped; unknown anchor to `DefaultAnchor` (message top left, the rest bottom left); scale snapped to 0.1 within 0.6..1.6, unreadable is 1, 1 not written, invariant culture; a widget named twice keeps its last word), `Place`, `SetScale`, `Move(widget, onto)`, `Move(widget, step)`, `Rank`, `Stack(anchor)`, `Stacks(drawn)`. `Known` is the Mac's full list in its order (message, clock, claude, meters, nowPlaying, watchers, meetings) so later stages' words round-trip; `DrawnInS5` is clock, claude, meters. `Standard` = `"clock:bl claude:bl"`.
- **`DeskGeometry.cs`**: `DeskInsets(Left, Top, Right, Bottom)`, `DeskRect`, `Margins(left, right, top, bottom, taskbar, inset)` (each side `max(0, saved + taskbar side - inset)`), `Content(width, height, margins, inset)`, `ColumnWidths(total, colWidth, center)` (24 DIP gap, the three never overlap), `Point(anchor, content)`. DIPs throughout, no notch.
- **`TaskbarClearance.cs`**: `TaskbarEdge` (Left, Top, Right, Bottom); `Reserved(monitor, work)` (what the work area leaves out on each side); `ForBar(bar, monitor)` (a taskbar rectangle, shown or slid away by auto-hide, that touches the monitor within 4 px gives its edge and its thickness, the side perpendicular to the edge); `Insets(monitor, work, bars, scale)` (per side, the larger of the reservation and any auto-hide bar on that edge, converted to DIPs); `AnchorsFor(edge)`. An auto-hide taskbar's thickness is kept clear at all times; the Mac's slide that follows the Dock is S7 polish.
- **`DeskMeters.cs`**: `DeskTier(Key, Utilization, ResetsAt)`, `DeskUsage(Tiers, FetchedAt, SignInNeeded, FirstSignIn, Account)` with `IsStale(now)` (15 minutes, or sign-in needed), `DeskMeterRow(Key, Label, Percent, Fill, Pace, Reset, Warning)` and `DeskMeters.Rows(usage, now)` (the widget's visible tiers in its order, label from `TierModel.Label`, percent truncated, fill clamped to 0..1, pace from `Pacing.PaceFrac`, reset text as the widget's card prints it, warning at 90% and up, the widget's red), `DeskClaudeText.Parts/Line` ("claude   7% session, resets 3:00   63% week", the account label in place of "claude" with two or more accounts, `h:mm` invariant as the Mac, nil with no numbers) and `SignInLine(first)` ("sign in to Sanduhr", "sign in again in Sanduhr").
- **`DeskFont.cs`**: `BundledFamily`, `BundledFonts` (EsteFont Pro default, EsteFont 26 heritage, `Face(family, bold)`, `FileNames`), `DeskFont.Resolve(saved, available)` and `PickerFamilies(installed)`. No upgrade migration (no Windows Desk existed before).
- **`ThirdPartyNotices.cs`**: a `Fonts` list beside `Packages`, each with its files and the copyright and license records quoted verbatim; a test reads the TTFs' name tables (IDs 0 and 13) from `mac/Resources/Fonts` and checks them against the list.

#### App

- **Fonts**: the four TTFs linked from `mac/Resources/Fonts` as `Resource` items under `Fonts/` (no second copy in the repo), drawn through a `pack://application:,,,/Sanduhr;component/Fonts/#EsteFont Pro` `FontFamily` (the component form, because `pack://application:,,,/Fonts/` did not resolve in the render harness). Nothing is installed system-wide. About lists them under Third-party notices.
- **`DeskController`**: on when `desk_enabled`; builds a `DeskView` per screen from the layout, the font, the sizes and the latest `DeskUsage`; rebuilds on `ScreensChanged` and on a setting change; stops the host when switched off, at Reset and at quit. `WidgetViewModel` raises `DeskUsageChanged` after each render and on sign-in state changes (demo mode feeds the samples, as the cards do).
- **`DeskView`**: the Mac's column layout (sides with top, middle and bottom stacks; a center column when tc or bc has pieces), the inner inset `timeSize * 0.18`, ink white at 0.92 with a soft dark shadow. Clock "h:mm" in the bold face at `timeSize`, date "dddd, MMMM d" at 0.3; Claude line at 0.19, opacity 0.75 (0.45 stale); meters at 0.17 with a bar `timeSize * 3.2` wide, a pace tick, "resets …" under it.
- **Settings, Desk page** ("Desk", a sidebar group "Desk"): Show the Desk (off by default), Where each piece sits (per drawn widget: place from the eight or Hidden, size 60% to 160%), Margins, Font (EsteFont Pro, EsteFont 26, then installed families), Clock size, and the layout map of the primary monitor with the taskbar band; `SettingsNames`, `SettingsIndex` anchors and synonyms; the page carries `SettingsNames.TrademarkNotice` (it names Claude).
- **Keys** (`settings.json`, absent = default): `desk_enabled` (false), `desk_layout` ("clock:bl claude:bl"), `desk_margins` (`{left: 52, right: 52, top: 40, bottom: 60}`), `desk_font` (absent = EsteFont Pro; "" = Segoe UI), `desk_time_size` (112).
- **Islands (cost).** A per-pixel-transparent window repaints its whole surface on every redraw: one full-screen window at 3840x2160 measured 100 to 800 ms of CPU a redraw and +90 MB. So `DeskView` lays the whole screen out once (the Mac's columns decide where each stack hangs) and `TakeIslands()` hands each stack out with its anchor point and alignment; the host gives each a window just its size plus 16 DIPs for the shadow, and reuses the windows across the minute's redraw.
- **Cost (g)**: the clock redraws once a minute on the minute; the Claude line and meters on each fetch and on the minute (the stale dim); no render loop or animation; the Show-desktop watch is a z-order walk every 2 s (250 ms only while Show desktop is on or just after the desktop took the foreground). Measured on Dunder (4K, 1 monitor, standard layout plus meters, two island windows) with the real host and view in a test process: **0.34% of one core (0.02% of the machine) over 60 s including one minute redraw; 106 MB working set against 74 MB for the same process with no Desk (+32 MB)**. Was 0.5 to 1.0% and +90 MB with one full-screen window.
- **Render harness**: the `desk` scenario renders the Desk content on a dark and a light wallpaper stand-in, at 100% and 150%: standard, standard with meters (a tier at 91% in the warning red), two layouts that fill the centers and middles at 60% and 160%, and signed out; the `settings` scenario renders the Desk page. White ink is faint on a light wallpaper, as on the Mac (which has an ink color setting S5 does not port).

#### The V3 live checks (Este runs them; the PR does not merge before they pass)

`DeskRunner` (scratchpad, not shipped) compiles the App's real `DesktopLayerHost`, `DeskNative`, `DeskView` and `DeskFonts` and, on a clear desktop, checks (a) drawn and visible, above the icons host, below a normal window; (c) one window per monitor at its DPI; (b) a click lands on a piece, a click on empty space reaches the desktop, nothing activates; (e) Win+D with the watcher off (does ownership alone keep it?) and on, and the taskbar's Show-desktop corner, each toggled back and checked to drop back to the bottom; (d) `TaskbarCreated` asks for a rebuild. It refuses to start while an app window covers the middle of the primary screen.

| Check | Status |
|---|---|
| (a), (b), (e) | Waiting for Este's clear desktop |
| (c) | Single 96-dpi monitor on Dunder; mixed DPI not available to test |
| (d) | Simulated (posted `TaskbarCreated`); a real Explorer restart not run |
| (f) | Rainmeter not installed or not running on Dunder; not tested |
| (g) | Measured, above |
| (h) | The rebuild triggers (`WM_DISPLAYCHANGE`, `SPI_SETWORKAREA`, `WM_DPICHANGED`, `TaskbarCreated`) are coalesced over 500 ms; settings messages sent to the windows left them in place (5.0). A real wallpaper change, theme change, sleep and wake and DPI change not run |

## Stage 6: Desk content (deepened 2026-10-11)

Este's rulings for S6 (2026-10-11, logged on the dashboard) are built here. Held for Este and not built: the MCP tools `propose_desk_messages`, `get_desk_messages` and `watch_*` (a trust-boundary change: `Sanduhr.Mcp` is not touched), and meetings (`Windows.ApplicationModel.Appointments` needs a manifest capability). The Desk side of each held tool is built so the tool is a thin later addition (6.9).

### 6.0 Rulings and what they mean here
1. `hotkey_settings` becomes a per-action map before any new shortcut (6.1). No shortcut is added in S6: Join waits for meetings.
2. From the Mac: messages with effects, the editor, special days (6.2); now playing in the widget first, then a Desk piece (6.3).
3. New Desk pieces after Knighthawk (Este's Rainmeter suite, read for layout only; none of its images or fonts enter the repo): telemetry (6.4), weather (6.5), an audio visualizer beside now playing (6.6).
4. Glow targets: `claude_code_glow.target` (6.8).
5. Desk ink: the Mac's ink setting plus Automatic, the new default, sampled from the wallpaper image, never a screen capture (6.7).
6. The word "claude" on the desktop stays; the Desk pages carry `SettingsNames.TrademarkNotice`.

### 6.1 Shortcuts as a per-action map (first)
- Shape: `hotkey_settings` = `{"settings": {"on", "vk", "mods"}}`, one member per `HotKeyAction` word (`HotKeys.Word`: "settings"; "join" later). The key name is unchanged, so the frozen-keys contract holds.
- Core `HotKeys.DecodeMap(JsonNode?)` reads both shapes: an object with `on`, `vk` or `mods` at its top is the flat S1 shape and belongs to Settings; anything else is the map. `HotKeys.EncodeMap` writes the map; a member for an action this build does not know is kept as is.
- Core `SettingsMigrations.HotKeyMap(JsonObject root)`: flat to map, once, idempotent, the values carried exactly (an odd value is carried, not repaired: `Decode` already reads it as the default). Runs at launch before anything reads the key; a second run changes nothing. Tests: flat on/custom combo, flat off/default, odd values, already a map, absent key, unknown members survive, idempotent.

### 6.2 Messages (W11), the editor and special days
**Core** (ported with the Mac's tests: `DeskMessageTests`, `MessageEffectsTests`, `MessageStyleTests`, `MessageEditorTests`):
- `DeskMessages.cs` (`Message.swift`): `messages.txt` grammar (`Mon:`, `10-31:`, `#` comments), pools, `Today(special, usual)`, rotation (plain lines by day, a weekday's lines by week, hourly within the day when `rotate` is hourly), mix, pin, `Special` (3 at a time, the window moving hourly), `MessageSpecialMode` (stack, turns, scroll; 5/10/30/60/300 s; `Index(at)`, `NextChange`). The calendar is injected; tests run on a fixed one.
- `MessageEffects.cs` (`MessageEffects.swift`): `{ink:}`, `{glow}`/`{noglow}`, `{size:}`, `{write}`, `{shimmer}`, `{sweep}`/`{sweep:n}`, `{font:}`; the lenient `Parse` and the strict `ParseStrict` with the Mac's reason wording; `MessageMotion` (timings and the sweep math).
- `LetterMap.cs`: `LetterStyle` and the Unicode tables (`LetterMap.swift`).
- `MessageDocument.cs` (`MessageLineModel.swift`): rows, raw rows kept verbatim, comments in place, an unchanged file saves back byte for byte, a changed row rewrites only its line; `MessageWhen`, `MessageLook`, the palette.
- `MessageProposal.cs` (`MessageProposal.swift`): request decode and checks, Add and Replace, the result and state JSON, file names and limits as the Mac's (`desk-messages-request.json`, `-result.json`, `-state.json`).

**Files.** `%APPDATA%\Sanduhr\messages.txt` (the existing un-virtualized folder; no new location), created with the Mac's starter text the first time the message piece or the editor needs it. Applying a proposal keeps `messages.txt.previous`. Reset's Settings step removes both.

**Keys** (absent = default): `desk_message` `{pin: "", rotate: "daily", mix: false, special: "stack", seconds: 10, size: 84, color: "", glow: true, font: ""}` (`color` "" follows the ink: `9ad7ff` on light ink, `1f4f86` on dark; a hex spec is the user's; `font` "" is the Desk font). `desk_message_claude_direct` is not added until the MCP tool exists.

**Drawing** (`DeskView`, the `message` widget word, default place top left as the Mac): the message size is its own (`size`, times the piece's scale, times `{size:}`); ink is the line's `{ink:}` gradient, else the message color; `{glow}` is a soft glow in the ink (blur 18, opacity 0.55); `{font:}` bold, italic and bold-italic in the line's own font, the rest through `LetterMap`. Motion runs only while it can be seen and never with Windows' animations off (`SystemParameters.ClientAreaAnimation` false): `{write}` reveals left to right over 1.5 s once; `{sweep}` is a three-character light over 1.1 s, after 0.6 s, then every n s for `{sweep:n}`; `{shimmer}` a band of 30% of the width every 8 s taking 1.6 s. Animations run at 30 frames a second on the message island only and stop between runs. Special days: Stack draws every line; Take turns and Scroll show one line at a time by the clock (`Index(at)`), a crossfade of 0.4 s or a glide of 0.8 s, redrawn by a timer at `NextChange`, never a loop.

**Settings, Message page** (Desk group): the editor as rows (when, text, color preset or Custom, glow, size, letter style, motion; Add line at the top of its day's group; raw rows shown verbatim and editable as text), Edit as text, Save and Revert (unsaved is the text the view would write against the file as read), a reload offer when the file changes under unsaved edits; Rotate (daily, hourly), "Mix every-day lines in on days with their own line", Pin a line, On special days (Stack, Take turns, Scroll and the time each line shows); Look: size, color, glow, font. The Claude suggestion card (6.9) sits at the top of this page and shows only while a suggestion waits.

### 6.3 Now playing (W8): the widget first, then the Desk
**Core** `NowPlaying.cs` (`NowPlaying.swift`, the pure half; tests from `NowPlayingTests`): `NowPlayingInfo` (title, artist, album, duration, position at a time, playing, app id; `State`, `Position(now)`, `Progress(now)`), `NowPlayingTracker` (a track shows at once; nothing counts only after 1.5 s without a track, so a skip never blinks), `NowPlayingPrefs` (hide while paused, excluded apps), `NowPlayingApps` (seen this session, in memory), `NowPlayingText` ("▶ Title · Artist", "⏸ …"). No adapter, supervisor or fallback: Windows has a public API.

**App** `NowPlayingService`: `GlobalSystemMediaTransportControlsSessionManager` (WinRT, no capability for a full-trust app), event driven (`CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`); nothing polls. Started only while the widget line is on or the Desk shows the piece; stopped otherwise. Play/pause and Next through `TryTogglePlayPauseAsync` and `TrySkipNextAsync`. **Titles, artists and app ids live in memory only: never written to a file, a log or settings.** The excluded-apps list stores app ids (`SourceAppUserModelId`), which the user picks from apps seen this session.

**Widget**: a line under the cards, "▶ Title · Artist" trimmed to width, a click plays or pauses, a Next button while paused; hidden with nothing playing. **Desk** (`nowPlaying` widget word): the same line over a thin position bar in the ink; the bar moves once a second while playing (in place, no relayout), drawn once while paused; nothing ticks with nothing playing. Desk clicks wait for S7.

**Keys**: `now_playing` `{widget: false, hide_paused: false, excluded: [], visualizer: true}`. Settings, **Now playing** page (Desk group): Show in the widget, Hide while paused, the apps seen this session with a switch each, Bars beside it on the Desk (6.6), and a status line ("Playing in Spotify", "Nothing playing"; never the title).

### 6.4 Telemetry (Desk piece)
Knighthawk's System skin, cut to what Este asked for: **CPU, RAM and disk**, each a label, a percent and a bar in the ink, the meters' layout. Core `Telemetry.cs`: CPU from two `GetSystemTimes` readings (busy = (kernel + user − idle) deltas over total), RAM from `GlobalMemoryStatusEx.dwMemoryLoad`, disk as used share of the system drive (`GetDiskFreeSpaceEx`), clamped and rounded; formatting tested. App `TelemetrySource`: one P/Invoke sample every 5 s (at most once a second, per the ruling; 5 s after the cost measurement, 6.11), only while the Desk shows the piece; the piece updates its numbers and bar widths in place (fixed widths, no relayout). No performance-counter package, no WMI. `telemetry` widget word, default place top right.

### 6.5 Weather (Desk piece, off by default)
Knighthawk's Weather skin: the city as a heading, "Partly cloudy · 72°", "High 80° · Low 61°", "Tomorrow: Showers 75° / 60°", and under it in small type "Weather data by Open-Meteo.com".
- **Where the data comes from.** Open-Meteo, no key and no account. Settings, **Weather** page: type a city, Look up sends the city name to `https://geocoding-api.open-meteo.com/v1/search?name=<city>&count=5&language=en&format=json`; the user picks one of the places found; its name, admin area, country and coordinates are saved. The forecast asks `https://api.open-meteo.com/v1/forecast?latitude=<lat>&longitude=<lon>&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&temperature_unit=<fahrenheit|celsius>&timezone=auto&forecast_days=2`. Coordinates are rounded to two decimals (about 1 km) before they are sent or saved. **Only the city name and those coordinates are sent**: no account, id, machine name or version (the user agent is "Sanduhr"; no cookies). No Windows location capability.
- **When.** Only while the Desk is on and the weather piece is placed: once at start, then at most every 30 minutes; a failure keeps the last reading (shown dim after 3 hours) and tries again 30 minutes later. The reading is kept in memory; nothing about the weather is written.
- **Core** `Weather.cs`: the two URLs, the JSON readers (geocoding results; current and daily), the WMO code words (Knighthawk's table, sentence case), `WeatherSchedule` (due, backoff, stale), units from the region (`RegionInfo.IsMetric`) unless chosen. Tests on recorded JSON fixtures.
- **Keys**: `desk_weather` `{name, admin, country, lat, lon, unit}` (absent = no place; the piece shows "Set a city in Settings, Weather").
- **Attribution.** Open-Meteo's data is CC BY 4.0 and its licence asks for a link next to wherever its data shows: the Desk piece carries the credit line (a link once Desk clicks arrive in S7), the Weather page carries "Weather data by Open-Meteo.com" as a link with the licence link, and About's third-party notices list Open-Meteo (CC BY 4.0) and GeoNames (place search, CC BY 4.0). **Open-Meteo's free API is for non-commercial use only**: Sanduhr is free with no ads or subscriptions, which reads as non-commercial; Este confirms before release.

### 6.6 The audio visualizer (beside now playing)
Command Center-style bars beside the now-playing line, driven by the system **peak meter** only: `IAudioMeterInformation.GetPeakValue` on the default render endpoint (`IMMDeviceEnumerator.GetDefaultAudioEndpoint(eRender, eConsole)`, `IMMDevice.Activate`). **No audio capture, no loopback, no recording, no microphone.** Core `AudioBars.cs`: 16 bars as a short history of peak levels (newest at the right), attack and decay smoothing, a floor, idle after 5 s under 0.004 (Knighthawk's threshold). App `AudioPeakMeter` (COM interop, re-acquires the endpoint when it fails or the default device changes) and the bars update in place at 20 frames a second **only while now playing says Playing and the piece is on screen**; paused, stopped or idle, the meter is released and nothing ticks.

### 6.7 The Desk ink
- **Modes** (`desk_ink` `{mode, color, shadow}`; absent = `{mode: "auto", color: "ffffff", shadow: true}`): **Automatic** (default), **Light** (white at 0.92 with a dark shadow, S5's ink), **Dark** (`1b1f27` with a soft light shadow), **Theme** (the widget theme's text color, shadow by its brightness), **Custom** (the Mac's hex spec: one to four colors, a gradient, and its presets Ice gradient, Sky, Marquee, White). Shadow: "Drop shadow under the text", as the Mac.
- **Automatic**: for each island, the average brightness of the wallpaper behind it picks Light or Dark (relative luminance above 0.55 is Dark ink). The brightness comes from the wallpaper itself, never a screen capture: `IDesktopWallpaper` gives each monitor's wallpaper path, its position (fill, fit, stretch, tile, center, span) and the background color; the image is decoded once at 256 px wide and cached by path and write time; Core `WallpaperSampler` maps an island's screen rect through the position onto the image and averages the pixels. A solid-color desktop uses its color. Recomputed on a wallpaper change (`WM_SETTINGCHANGE` with `SPI_SETDESKWALLPAPER`) and a monitor change; a slideshow updates on its next change. Unknown (no path, an unreadable file, a failure) is Light ink.
- The meters' warning red, the message's own `{ink:}` and the message color's explicit hex stay as set in every mode.

### 6.8 Where the Claude Code glow lands
`claude_code_glow.target`: **"window"** (default, S4's behavior: the top of the window in use), **"edge"** (a chosen edge, `edge_side` top, bottom, left or right, of a chosen monitor, `edge_monitor` by device name, absent = primary; the same edge light along the middle half of that edge, fading 24 px inward), **"widget"** (`FlashWindowEx` on the widget only, no light). Core `GlowTargetKind { Window, Edge, Widget, Desk }`: `Desk` is S7's placeholder, never offered, and reads as Window. Unknown or absent reads as Window, so an existing install is unchanged. `GlowPlacement.Edge` computes the rect; tests. The Claude Code page gains "Where the glow shows". The frozen-keys contract gains `claude_code_glow` and its members.

### 6.9 Held tools: the Desk side and its seams
- **Messages from Claude.** `DeskMessageHandoff` (App, over Core `MessageProposal`) reads `desk-messages-request.json` from `%APPDATA%\Sanduhr` (the theme handoff's watch pattern), checks it again, holds it as a suggestion and answers in `desk-messages-result.json` (`pending_approval`, then `applied` or `rejected`); Add applies it with `messages.txt.previous` kept; Dismiss rejects. `desk-messages-state.json` (pin, rotation, mix, special mode) is written only once the tool exists. Settings, Message shows the suggestion card (Add, Review, Dismiss) only while one waits. **Not started at launch in S6**: nothing writes the request until the MCP tool ships, so the watch is wired but off (`DeskMessageHandoff.Enabled = false`); the tool turns it on.
- **Watchers.** Core `Watchers.cs` and `WatcherRequest.cs` (`Watchers.swift`, `WatcherRequest.swift`, agent requests only; tests from `WatcherTests`): the board, states, limits, ordering, fade and lost-touch rules, the Desk's lines. App `WatcherStore` reads `watch-request-*.json` from `%APPDATA%\Sanduhr` and deletes each as it reads, keeps the board in memory, and is **off** until the tools ship (no switch in Settings yet, no `watchers.json`). The Desk's `watchers` piece draws the board's cards (title, progress, state or note, elapsed) and is not offered in "Where each piece sits" until then; the render harness draws it from samples. Claude Code's background work (the Stop hook's `background_tasks`) is not built: it would change what the S4 hooks read, a privacy claim.
- **Meetings.** Not built: no `meetings` piece, no Join shortcut.

### 6.10 Settings, names and the layout
- Desk group pages: **Desk** (switch, where each piece sits, look with the ink, margins, font, clock size, map), **Message**, **Now playing**, **Weather**. Each new page is in `SettingsNames`, `SettingsIndex` (anchors and synonyms) and carries `SettingsNames.TrademarkNotice`.
- `DeskArrangement.Known` gains `telemetry` and `weather` (the bars belong to the now-playing piece, not a word of their own); `Drawn` becomes the S6 list: message, clock, claude, meters, nowPlaying, telemetry, weather (watchers drawn only from the harness). Default places: message top left, telemetry top right, weather middle right, the rest bottom left. `Standard` is unchanged, so an existing layout reads the same.
- **Live pieces.** A piece whose numbers change between redraws (the position bar, telemetry, the bars) updates its own elements in place with fixed widths, so its island repaints alone and no screen is laid out again. The controller lays the screens out only on the minute, a new reading, a track change, a weather reading or a setting change, and disposes the old live pieces each time.

### 6.11 Cost (Rainmeter's bar: near-zero idle)
Measured on Dunder (4K, one monitor, 100%) with a scratch runner that drives the real `DeskController`, `DesktopLayerHost` and `DeskView` in a test process, three runs each, 120 s windows after warm-up; baseline is the same process with no Desk (0.08 to 0.23% of one core, 46 to 53 MB).
- **Idle, S6 layout** (message, clock, Claude line, meters, now playing with nothing playing, system readings, weather without a place), Automatic ink: **0.85 to 1.00% of one core (0.05 to 0.06% of the machine), 134 to 136 MB** (about +83 MB).
- **Idle without the system readings**: 0.27 to 0.29% of one core, 125 MB. The S5 layout alone: 0.16% over 180 s.
- **Playing**, the bars at 20 fps (synthetic levels) with the readings and the position bar: **about 2% of one core (0.13% of the machine)**, 139 MB, only while a track plays.
- What changed to get there: the minute no longer lays every screen out (the clock updates in place and the host re-fits its island; a full layout every 5 minutes and on any reading, track, weather, setting or screen change), the readings every 5 s and only on a change, no blur under the bars. Was about 0.9% of one core for the S5 layout alone, and 3.4% playing.
- **Open**: one repaint of the readings' small island costs about 50 ms of CPU (the sampling itself is free; ink and shadow make no difference), so the readings are most of the idle cost. Profiling the per-pixel-transparent repaint path is S7's.

### 6.12 Render harness
`desk` gains: the message (effects as still frames: glow, gradient ink, letter styles; a special day stacked), now playing with bars, telemetry, weather, watchers from samples; each in Light, Dark and Automatic ink over a dark and a light wallpaper stand-in (Automatic's sampler fed the stand-in image), at 100% and 150%. `settings` renders the Message, Now playing and Weather pages and the Desk page's ink, in a dark and a light theme.

### 6.13 PRIVACY.md (Este reads before release)
New rows, Windows table: **Desk messages** (`messages.txt`, `.previous`; never sent; Claude's suggestions only once the MCP tool exists); **Now playing** (read from Windows' media sessions, in memory only, never written or logged; excluded apps stored as app ids); **Audio level for the bars** (the system output peak level only, never audio, in memory); **Telemetry** (CPU, RAM, disk percentages read locally, in memory); **Weather** (off until a city is set; the city name you type and the place's coordinates, rounded to about 1 km, go to Open-Meteo; the place is saved in `settings.json`; the reading stays in memory); **Desk ink, Automatic** (reads the wallpaper image file and its placement, never the screen; in memory). Network section: Open-Meteo as the third destination, only with the weather piece set up.

## Stage 7: Desk polish (deepened 2026-10-12)

Este's order for S7 (from the S6 handback): (1) pause what moves while a Desk island is covered, (2) the idle cost at its root, (3) arrange by dragging with the glow's Desk spot, (4) Desk clicks, (5) sign-in reminders and Just the Desk, (6) a release-note draft for the S6 downgrade issue. **Held, not built:** the MCP tools (`propose_desk_messages`, `get_desk_messages`, `watch_*`), meetings (the Appointments capability), watchers fed by Claude Code background tasks. **Cut from the cycle:** the camera and mic indicators (no spike).

### 7.0 What the Mac does and where Windows differs
- The Mac's Desk is one window per screen; Windows draws one window per island (S5's cost answer). So "covered" is per island, clicks are per island, and arrange mode needs its own surface (7.3).
- The Mac has a notch and a menu bar; Windows has neither, so the sign-in reminder switch names only the Desk, and Just the Desk hides the widget instead of following a "When the widget shows" choice (Windows has none: the widget shows at launch and the tray toggles it).

### 7.1 Covered islands pause (S6 open issue)
**Core `DeskCoverage.cs`** (pure, tested):
- `FullyCovered(PxRect target, IEnumerable<PxRect> above)`: rectangle subtraction; true only when nothing of `target` is left. An empty target is never covered.
- `Occluder(WindowFacts f)`: a window above the island counts only when it is visible, not minimized, not cloaked (another virtual desktop, a suspended app), not `WS_EX_TRANSPARENT`, not `WS_EX_LAYERED` (it may be see-through: counting it could pause something on screen, the wrong way to fail), not one of Sanduhr's own Desk windows, and has a non-empty frame. The frame is the DWM extended frame (no invisible resize borders).
- `DeskPauseRule.Paused(bool covered, bool arranging)`: arranging never pauses.

**App.** `DesktopLayerHost` gains `Coverage` (one bool per island, in `Show` order) and `CoverageChanged`. A `DeskCoverageWatch` owns one `SetWinEventHook` set, out of context (Sanduhr's own windows included, since Settings or the widget can cover an island): `EVENT_SYSTEM_FOREGROUND`, `EVENT_SYSTEM_MOVESIZEEND`, `EVENT_SYSTEM_MINIMIZESTART`..`MINIMIZEEND`, `EVENT_OBJECT_SHOW`/`HIDE`, `EVENT_OBJECT_CLOAKED`/`UNCLOAKED` (window objects only; everything else returns at once). Events are throttled to one recompute per 250 ms (a throttle, so a steady stream of tooltips still gets one). `EVENT_OBJECT_LOCATIONCHANGE` is not hooked: it fires on every cursor move. While any island reads covered, a 5 s recheck catches a covering window restored by keyboard, which raises none of the hooked events; with nothing covered nothing ticks. A recompute walks the z-order upward from each island window (`GetWindow(GW_HWNDPREV)`), reads each window's styles, cloak and frame (never its title), and asks `DeskCoverage`.

**What pauses.** Each island carries a `DeskPause` gate (`Paused`, `Changed`) made by `DeskView` per stack, plus the widget words it holds. On a covered island: the message's `{shimmer}` and periodic `{sweep}` timers stop and its special-day turns stop (on uncover a sweep runs after the replay delay and turns jump to the line the clock says, without motion); the now-playing position bar stops ticking (on uncover it reads the current position at once); the controller stops `AudioLevelSource` when every island holding now playing is paused and `TelemetrySource` when every island holding the system readings is paused (on uncover each restarts and the piece keeps its last reading until the next sample). The clock is not paused (once a minute). Nothing re-lays the screen out on a coverage change, so a change repaints nothing.

**The glow's Desk spot** (7.3) uses the same answer: a glow aimed at a covered Desk spot shows at the window in use instead.

### 7.2 Idle cost: what the 50 ms was, and the fix
Profiled on Dunder (4K, one monitor, 100%) with S6's runner (the real `DeskController`, `DesktopLayerHost` and `DeskView` in a test process; 120 s windows after an 8 s warm-up), plus a probe that repaints the readings island alone by variant:
- **The repaint itself is cheap once warm**: the readings island (301 x 180 px) costs 2 to 8 ms of CPU per change in hardware rendering, 5 to 16 ms in software; with its drop shadows stripped, about the same. The first window a process repaints costs about 85 ms a change for its first minute, whatever the variant.
- **The "50 ms" was the JIT warming the repaint path.** The readings alone cost 1.06% of one core over 120 s with the defaults, 0.03% with `TieredCompilation` off, 0.09% with the tier-up threshold raised to 1000 calls (no recompiling inside the window) and 0.42 to 0.43% with QuickJit or dynamic PGO off. A repaint every 5 s reaches tier 1's 30 calls only after minutes, so every 120 s window right after start pays the recompiles (dynamic PGO adds an instrumented tier, so twice). After an 8-minute warm-up the **full S6 layout idles at 0.03% of one core** with the defaults.
- **Decision: keep the runtime defaults; measure steady state.** A Desk runs for hours, so the metric is steady state, and dynamic PGO's gains are kept. Turning PGO off measured 0.33% of one core over the first 120 s against 1.15% with the defaults, but only by trading away the warm code; `TieredCompilation` off measured 0.13% the same way and pays full JIT at start. The cost method from S7 on: three runs, each an 8 to 10 minute warm-up (its CPU reported as the warm-up cost) and then a 120 s steady-state window; the 0.5% target applies to steady state. ReadyToRun or a startup profile for the Desk path are the levers to measure only if the warm-up averages over about 2% of one core across the first 5 minutes. Playing (the bars at 20 fps) is about 2.1 to 2.5% of one core, only while a track plays.
- Not the cause: `UpdateLayeredWindow` of a large surface (each island is its own size since S5), WPF re-laying out the island (the readings update in place), the sampling (0.10% alone), ink or shadow.

### 7.3 Arrange by dragging (item 60), with the glow's Desk spot
**Core `DeskArrange.cs`** (ported from `DeskArrange.swift` with `DeskArrangeTests`): `Nearest(point, content)` (the nearest of the eight anchor points as shares of the content's width and height; ties to the first in `DeskAnchors.All`), `StackReach` 26, `StackBounds(arrangement, frames, reach)`, `Target(point, content, stacks, own)` (own stack first, then any stack under the point, then nearest), `PieceAfter(y, stack)`, `Handle(anchor)` and `Scale(start, size, drag, anchor)` (ported for the resize handle, which Windows does not draw yet), `EndKey(Key)` (Return and keypad Enter are Done, Escape is Cancel), `DeskArrangeEnd`, `DeskArrangeCopy` (the Mac's words, "Return or Done" kept), `DeskArrangeSession(saved)` (`Working`, `Changed`, `ToWrite`: nothing written when unchanged, so an older string stays as it was), and `DeskArrangement.Put(widget, anchor, before)`.

**The glow's Desk spot** (Este's 4-choice glow ruling): `glow` joins `DeskArrangement.Known` and the drawn list, default place top center. The piece is a fixed-size slot (so a glow never moves its stack-mates) that is fully transparent at rest, outlined and named only while arranging, and lit by the glow: a soft pill of light in the glow's color (the theme accent while waiting, the success color when done), fading in over 0.2 s and out by 1.5 s, at 30 frames a second on its island only. `GlowTargetSettings.Offered` gains Desk ("A spot on the Desk"); `Effective(deskShowsGlow)` reads Desk as Window when the Desk is off, the spot is not placed, or its island is covered. Choosing Desk places the spot when it is not placed. The Claude Code page's "Where the glow shows" offers the four, with the caption "On the Desk while it is on and in sight; otherwise at the top of the window you're working in."

**App.** Arrange mode is its own surface: on every monitor a full-screen window above every other window (`Topmost`, activatable while arranging so it takes the keys) that dims the screen and draws the Desk laid out from the working arrangement with the same `DeskView` (cost does not matter for a mode), each placed piece outlined with its name, the glow spot included. The island windows hide while it is up; this is the only time anything of the Desk takes the mouse over its whole frame. Drag a piece: a ghost follows the pointer, the target anchor lights (`Target`), the drop is `Put` before `PieceAfter`, and the Desk lays out again at once. A bar in the middle of the primary screen carries `DeskArrangeCopy`'s title, hint and keys line with **Done** (default, Return) and **Cancel** (Escape). Always endable: Done, Cancel, Return, Escape on any monitor's window, the tray's "Finish arranging", turning the Desk off, a screens change (cancels), Reset and quit (cancel). Done writes `desk_layout` once when changed and the Desk page and its map follow; while arranging the Settings map draws the working layout live and the page says `DeskArrangeCopy.SettingsArranging`. Entry points: "Arrange Desk…" on the Desk page (disabled with `SettingsDeskOff`'s line while the Desk is off) and in the tray menu while the Desk is on. Accessible names on the bar, its buttons and the pieces' outlines. The resize handle is not drawn (sizes stay in Settings): a follow-up.

### 7.4 Desk clicks
**Core `DeskHitTest.cs`** (the Mac's, cut to what Windows draws; ported tests): kinds `nowPlaying`, `weatherCredit` (new: Open-Meteo's licence asks for a link beside its data, spec 6.5), `clock`, `message`, `claudeLine`; `Priority`, `Slack`, `Area`, `Element(point, elements)`, `DeskElements.Build(input)` (the clock, message and Claude line only while `PiecesTakeClicks`), and `DeskPieceClicks` (`desk_pieces_take_clicks`, default on as the Mac; title "Clock and message take clicks"; caption "On: right-click them for Sanduhr's menu. Desktop icons right beneath them can't be clicked there while it's on.").

**App.** An island holding a click element drops `WS_EX_TRANSPARENT`; every other island keeps it, so it is click-through everywhere as before. Inside a clicking island the element gets a faint hit plate (alpha 1/255, the Mac's faint plate) over its frame; fully transparent pixels still pass clicks to the desktop at the OS level, so **empty space always passes clicks through**. The island's root hit-tests with `DeskHitTest.Element`: now playing, a click plays or pauses; the weather credit, a click opens open-meteo.com; the clock, message and Claude line, a plain click does nothing and a right-click opens the Desk menu (Edit messages… on the message, Desk settings…, Arrange Desk…, Turn off the Desk). Nothing activates the island. Settings, Desk: the switch with its caption.

### 7.5 Sign-in reminders and Just the Desk (Mac 2.14, BACKLOG #4)
- `desk_sign_in_reminder` (default on): Settings, Alerts, "Sign-in reminders on the Desk", caption "Shows "sign in to Sanduhr" where your Claude meters would be while you're signed out or your session has expired. The widget always says when you're not signed in." `DeskUsage` gains `SignInReminder`; with it off a needed sign-in draws nothing on the Claude line or the meters.
- **Core `JustTheDesk.cs`** (`JustTheDeskTests`): `just_the_desk` and `just_the_desk_silenced_reminder`; `Choose(state, reminderOn)` (on, the reminder off and remembered as silenced only when this turned it off, the Desk on), `EndBySignIn(state)` (off, and the reminder back on only when Just the Desk had turned it off), `Onboarding(hasAccount, on)`, `WidgetStartsHidden(on, hasAccount)`. Copy: button "Just the Desk", caption "No Claude account? Use the Desk on its own: a clock, a message and more on the desktop. Sign in any time from Settings, Accounts."
- **App.** The first-run card gains **Just the Desk** beside Sign in and Not now. Choosing it saves the keys, turns the Desk on, hides the widget (the tray icon stays the way back) and refreshes the Desk: the Claude pieces stay placed and draw nothing. Later launches with it on and no account start with the widget hidden and count as not onboarding, so What's New still shows after updates. Adding an account (sign-in, paste a key) ends it at once, without a relaunch: the reminder comes back if it was silenced, the widget shows for the new account, and the Desk fills in on the first fetch.
- Store copy is not built here: the report drafts the listing line.

### 7.6 The S6 downgrade note
Recorded in the report as a release-note line; `docs/store` is not edited.

### 7.7 Keys, names, harness, live check
- **Keys** (absent = default): `desk_pieces_take_clicks` (true), `desk_sign_in_reminder` (true), `just_the_desk` (false), `just_the_desk_silenced_reminder` (absent). `desk_layout` may now hold `glow`. `SettingsKeysContractTests` gains them.
- **Names**: Arrange Desk…, Clock and message take clicks, Sign-in reminders on the Desk, Just the Desk, A spot on the Desk, in `SettingsNames` and `SettingsIndex` (synonyms: drag, move, rearrange; click-through; signed out, without Claude).
- **Render harness**, dark and light: the arrange overlay (a piece mid-drag, an anchor lit, the bar), the Desk page with Arrange Desk… and the clicks switch, the Alerts switch, the first-run card with Just the Desk, the Claude Code page's four glow targets, the glow spot lit.
- **Live check (Este runs it; S7 does not merge before it passes).** `s7-runner` extends S5's `DeskRunner`: (b) still passes with clicks on (empty space between pieces reaches the desktop, a left click on the clock lands on the island and activates nothing), an arrange drag (begin, drag the clock to the top right, Return, the layout string changed, then restored), a covered-island pause (a normal window over the readings island stops telemetry and the message motion; removing it resumes), and e1/e2 still pass. Its `--cost` mode needs no input.

## Data Model
All new state is keys in `settings.json`, defaults on absence:

| Key | Type | Stage | Default |
|---|---|---|---|
| `whats_new_last_seen` | string | S1 | none |
| `whats_new_installed_version` | string | S1 | none |
| `whats_new_hide_after_updates` | bool | S1 | false |
| `hotkey_settings` | `{on, vk, mods}` | S1 | off, Ctrl+Alt+S |
| `limit_first_seen` | `{tier: ISO}` | S1 | {} |
| `limits_hidden_until_reset` | `{tier: {resets_at, utilization}}` | S1 | {} |
| `alert_muted_tiers` | `[tier]` | S1 | [] |
| `tray_mode` | string | S1 | "highest" |
| `tour_state` | string | S2 | fresh: "pending"; otherwise "not_offered" |
| `hotkey_settings` | `{settings: {on, vk, mods}}` (was flat; migrated once) | S6 | as before |
| `desk_ink` | `{mode, color, shadow}` | S6 | auto, "ffffff", true |
| `desk_message` | `{pin, rotate, mix, special, seconds, size, color, glow, font}` | S6 | "", daily, false, stack, 10, 84, "" (follows the ink), true, "" |
| `desk_weather` | `{name, admin, country, lat, lon, unit}` | S6 | no place; unit from the region |
| `now_playing` | `{widget, hide_paused, excluded, visualizer}` | S6 | false, false, [], true |
| `claude_code_glow.target` (+ `edge_side`, `edge_monitor`) | string | S6 | "window" |

No new files outside `settings.json` in S1. Nothing new in Credential Manager, the vault or `snapshot.json`.

## File Structure (S1)
```
windows-dotnet/
  src/Sanduhr.Core/
    WhatsNew.cs, WhatsNewCards.cs, HotKeyCombo.cs, ClaudeCodeHomes.cs,
    LimitLifetime.cs, TrayReadout.cs, SparklineSteady.cs        (new)
    CcLogReader.cs                                               (provider overload)
  src/Sanduhr.App/
    Views/WhatsNewWindow.xaml(.cs)                               (new)
    Services/GlobalHotKeyService.cs, Interop/HotKeyNative.cs     (new)
    ViewModels/ShortcutsViewModel.cs                             (new)
    App.xaml.cs, ViewModels/WidgetViewModel.cs, ViewModels/SettingsViewModel.cs,
    ViewModels/TierCardViewModel.cs, Views/TierCard.xaml(.cs), Views/SettingsWindow.xaml(.cs),
    Services/SettingsStore.cs, Services/VaultService.cs, Services/UsagePublishService.cs,
    Controls/SparklineControl.cs                                 (edited)
  tests/Sanduhr.Tests/  one test file per new Core file
```

## Key Technical Decisions
1. **Windows behavior kept where 1:1 would regress it** (tray "Highest of any limit" default, manual hide beside hide-until-reset, manually hidden tiers keep alerting). Logged.
2. **Discovery outside the MCP**: new Core file, not linked; `CcLogReader` gains a BCL-typed provider. The allowlist and the MCP's root set are untouched. Logged, escalated.
3. **Shortcut opt-in on Ctrl+Alt+S**, only a registration-failure conflict check, no Test It. Logged.
4. **What's New on the Mac's timing** (two seconds after launch with an account), versions tagged 3.5.0.
5. **No new packages, no manifest change, no new storage location** in S1.

## Dependencies & External Services
None new. `RegisterHotKey`/`UnregisterHotKey` (user32) by P/Invoke.

## Open Issues
- From the Stage 1 review (PR #204): `limits_hidden_until_reset`, `limit_first_seen` and `alert_muted_tiers` are global, not per account, as on the Mac; switching accounts can return a hidden promo limit as a new window. A per-account shape is a later item for both apps. Discovery runs on every `SearchRoots()` call; cache it if a slow profile folder shows up.
- From the Stage 2 review (PR #205), recorded rather than changed:
  - **The tray's Accounts submenu and Settings show real account names in demo mode.** Only the widget, the tray tooltip and the tour use the samples. The Mac fills Settings too; that is a later item.
  - **A tour opened from the tray while still pending records on Finish or Skip.** The Mac does the same (`WelcomeTour.end` records whenever the state is pending): the person took the tour.
  - **Mixed casing ("Take the Tour…", "End Demo Mode", "Sign In" beside "Show me") follows the Mac's labels.** S3's names table settles casing for both apps.
  - **No unit tests for `WidgetViewModel`'s demo guards or `WelcomeTourViewModel`.** These are WPF view models with no test seam. The answer-drop rule is in Core (`DemoMode.AnswerApplies`) and tested; the rest was checked with the off-screen render harness.
- From the Stage 6 review (PR #213), fixed in the PR: `{write}` and a once-only `{sweep}` replayed on every full redraw (now once per line appearing, kept by the controller); the Desk position bar ignored a seek until the next full redraw (it reads the current track now); an older media-session read could overwrite a newer one (a refresh sequence); the glow-target page dropped a saved Desk placeholder or an unplugged monitor when another control changed (each control replaces only its member); the peak meter's retry could throw from a timer tick; a nested redraw when the now-playing listener stopped; hourly rotation waited for the five-minute layout (a full layout on the hour now); the shortcut map's nested keys are in the frozen-keys contract. Recorded rather than changed:
  - **Message motion runs while the Desk is covered.** The Mac pauses `{shimmer}` and periodic `{sweep}` while its window is covered, the screens sleep or the session is away; Windows has no covered signal for the island windows yet, so motion runs while the Desk shows. It is 1.1 to 1.6 s at 30 fps every 8 s or more, on the message island only. S7 takes the pause with Desk clicks and occlusion.
  - **`SaveClaudeCodeGlow` keeping the target members is tested only through `GlowTargetSettings.WriteInto`**, not the App's write of the switches. Moving the switches' write into Core is a later tidy.
  - **A downgrade to a pre-S6 build reads the shortcut map as off**; if that build saves, it writes the flat shape, which the next upgrade migrates again, so no value is lost. Worth a line in the release notes.
1. The MCP root set and installers on discovered homes (Este).
2. The in-app trademark disclaimer on existing surfaces (Este; S3's About is the natural home).
3. Card version numbers at release (Este).
4. Self-review findings, answered in-line for the autonomous run:
   - *Does the tray's Rotate timer belong in Core?* No: the timer is UI; Core takes `step`. Tested by step parity.
   - *Is `ClaudeCodeHomes.Discover` cheap enough to run on every `SearchRoots()` call?* One directory listing of the profile plus a few `Directory.Exists` per candidate; `CcLogReader` already walks every project file each scan, so it is noise. No cache.
   - *Does `snapshot.json` keep hidden tiers?* It is written from the fetch payload, not the visible cards (`WriteSnapshotOk`); the build re-verifies.
