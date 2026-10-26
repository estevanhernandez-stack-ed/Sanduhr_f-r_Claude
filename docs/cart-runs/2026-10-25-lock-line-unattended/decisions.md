<!-- The ledger. Append only. -->

# Decisions: lock-line-unattended

## D1
- **Decision:** The run covers spec items 1 and 2 plus the daemon-free part of item 4 (state.yaml and a scenario); the daemon, signing, install and the owner's-message restore wait for the attended session.
- **Why:** The owner merged the spec and asked for "the unattended half"; the spec marks the root helper attended.
- **Alternative not taken:** Building the daemon target without running it.
- **Item:** run
- **How to reverse:** Not needed.

## D2
- **Decision:** In the app, the usage and reset slots read `UsageViewModel` rather than parsing `snapshot.json`; the composer takes plain values.
- **Why:** The snapshot is written from those same numbers; reading the file back inside the writer adds a lag and a parse for nothing, and plain inputs keep the composer pure and testable.
- **Alternative not taken:** Reading `snapshot.json` as the spec's table names it.
- **Item:** 1, 2
- **How to reverse:** Feed the composer from the file instead.

## D3
- **Decision:** The sanitizer's secret test calls `ConfigPack.secretLine(in:)` and `ConfigPack.looksRandom`.
- **Why:** The spec says shared, not copied; one definition of a secret shape across export and the lock screen.
- **Alternative not taken:** A second regex set.
- **Item:** 1
- **How to reverse:** Not needed.

## D4
- **Decision:** Until the daemon lands, the switch row says the writer comes in a later build, and no release ships with this run.
- **Why:** With the switch on, Sanduhr writes `lock-line.txt` and nothing reads it yet; shipping that would be a switch that does nothing.
- **Alternative not taken:** Hiding the page until the daemon exists.
- **Item:** 2, run
- **How to reverse:** Remove the line when the daemon merges.

## D5
- **Decision:** Cart's session, friction and wins logs are not written; commits carry the session attribution lines.
- **Why:** The plugin's data path resolves to the work seat on this Mac.
- **Alternative not taken:** Writing to the work seat.
- **Item:** run
- **How to reverse:** Point the plugin's data path at the personal seat.

## D6
- **Decision:** `LockLine` turns U+00A0 and U+202F into plain spaces, checks rejections in the order path, address, secret, long word through one private function both `sanitize` and `reason` use, and shows the reset slot only with a usage percent.
- **Why:** Newer macOS formats "6:10 PM" with a narrow no-break space; one rejection function keeps the line and the Settings reason in agreement; the spec's table empties reset with usage.
- **Alternative not taken:** Separate checks for the reason line.
- **Item:** 1
- **How to reverse:** Not needed.

## D7
- **Decision:** The writer picks the highest-utilization tier across every tier (ties to the session), treats idle and refreshing as fresh, runs its own `LockLineController` started at launch with its own session observers, and keeps its 5-minute timer only while the switch is on and the session active. Reset needs no new entry: it clears the whole support folder.
- **Why:** `MenuBarMode` sees only session and weekly; a fetch in flight should not flash "unavailable"; `DeskController` drops its observers when the Desk is off, and the lock line must not depend on the Desk.
- **Alternative not taken:** Hooking `DeskController`'s observers, or recomposing on every usage update.
- **Item:** 2
- **How to reverse:** Move the hooks into `DeskController`.
