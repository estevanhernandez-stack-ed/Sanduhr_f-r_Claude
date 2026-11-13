# Decisions waiting for the dashboard

The 626 dashboard MCP was down (502) when these were made. The next session with it connected logs each one with
`manage_decisions` (action `log`) to the project named, then deletes its entry here.

## 2026-11-12 · Sanduhr für Claude (Windows) · FX7zznEFlzArQ3CTY7RU

### A stale Codex tier draws no pace or burn; a stale Codex primary says "as of earlier" in the tray

- **Category:** bugfix
- **Decision:** A Codex tier past its freshness window keeps its countdown but drops the pace marker, pace line and burn
  projection (`TierCardViewModel.Stale`, set before `Update`). When Codex is primary and its tier for the tray's window is
  stale, the tray tooltip adds ", as of earlier", alone or beside a source further out.
- **Rationale:** The 4.8.0 hand pass showed a three-day-old 12% reading "33% under" against today's clock. The dimmed card
  alone did not stop the pace reading as current. The Desk already drew no pace for Codex rows, so it is unchanged.
- **Revisit if:** Codex gains a live feed that keeps tiers fresh, or a stale-but-recent tier (minutes old) turns out to want
  its pace back.
- **Files:** `windows-dotnet/src/Sanduhr.App/ViewModels/TierCardViewModel.cs`, `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (commit cb3e183b)

### Gotcha: radios in separate ItemsControl rows don't share WPF's arrow-key group

- **Category:** bugfix
- **Decision:** The Sources page's Primary radios step with the arrow keys through a `PreviewKeyDown` handler
  (`SourcesPage.Step`): next or previous enabled radio of the group, wrapping, and it checks the radio it lands on.
- **Rationale:** A shared `GroupName` keeps the checked state exclusive, but WPF moves between radios with the arrows only
  inside one parent. With one radio per item, Down from Claude's radio went to Codex's Track box. Any future per-row radio
  group has the same trap.
- **Files:** `windows-dotnet/src/Sanduhr.App/Views/SourcesPage.xaml(.cs)` (commit cb3e183b)

### Empty live lines collapse and announce through LiveRegion.Announces; it keeps a line's own LiveSetting

- **Category:** bugfix
- **Decision:** The Sources message line and the per-row color lines collapse while empty and carry
  `views:LiveRegion.Announces`, which raises LiveRegionChanged once layout shows them. `LiveRegion.Announces` now sets
  Polite only when the line has no LiveSetting of its own.
- **Rationale:** Narrator stopped on each blank line as "text" (Este's Narrator listen, 2026-11-12). An earlier fix
  (ab842be0) made them visible while empty, because a collapsed line with only `LiveSetting` never spoke: LiveSetting raises
  nothing. Gotcha: inside a DataTemplate, WPF applies attached properties in no set order, so Announces' local Polite
  overrode the template's Assertive.
- **Files:** `windows-dotnet/src/Sanduhr.App/Views/SourcesPage.xaml`, `windows-dotnet/src/Sanduhr.App/Views/SettingsReach.cs` (commit e4b42b23)

### 4.8.0 hand pass ran through UI Automation; the Desk-rows blind spot did not reproduce

- **Category:** deployment
- **Decision:** The hand pass ran as UI Automation plus screenshots against the dev build. Narrator's speech, the "Codex
  isn't installed" rename (Codex was running) and C2 (Desk on) were left out. In that long session the Desk piece rows after the
  first showed no automation children. 4.7.0 and a fresh 4.8 run both expose them, so it's recorded as unreproduced, with
  `DeskPieceRowsHeardTests` as the guard.
- **Revisit if:** a Narrator listen on the Desk page hears only "Sanduhr — Settings" on a piece row.
- **Next:** Narrator listen on Sources and the Desk page, then the 4.8.0 release runbook from phase 3.
