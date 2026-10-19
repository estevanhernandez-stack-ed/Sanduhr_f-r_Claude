<!-- Written once at intake, before any item is planned. Keep the Ask
     verbatim. Every Inferred line is also a ledger entry in decisions.md. -->

# Run Brief: polish-leftovers

## Ask
"close it and start 3 unattended", where 3 is the third item of the morning list: "Run 2's six
leftovers, as a small unattended Cart list on this Mac: the Desk page's Order caption wall, the
warning card's At column width, the doubled 'Needs the Desk', debug hooks for Hide and Show, the
ModsSettings.swift split, three-digit hex. About an hour, no release needed until something bigger
joins it."

## Source
- **Kind:** the "Found along the way, not fixed" list of a previous run's report
- **Location:** `docs/cart-runs/2026-10-17-polish-ux/report.md`, section "Found along the way, not
  fixed (for your call)", items 1, 3, 4, 5 and 6 (item 2, the spec's old shadow label, was left as a
  dated record and stays left)

## Intent
The six small things the ux-polish run noticed but did not touch are closed, each on its own
branch with the full Mac checks, so the next polish scan starts from zero known leftovers.

## Done when
- The Desk page's Order caption is two sentences, with its moved sentences in `docs/setup-guide.md`
  behind a Learn more link, the way run 2 item 14 did the other three captions.
- The meter-warning card's At column uses the shared value width.
- With the Desk off, the Notch page says "Needs the Desk." once.
- Three-digit hex shorthand ("fff") is accepted on commit, expanded to six digits, and pinned.
- Arrange mode's Hide and Show have debug hook steps and show in the snapshot, with the fixture,
  selftest and README updated, so `desk-arrange.yaml` can drive them.
- `ModsSettings.swift` is split by section into files under 500 lines each, with no behavior change.
- `cd mac && ./build.sh --debug`, `./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.

## Out of scope
- Any new feature or copy beyond the six; the Order caption's meaning stays.
- A release: these ride with the next minor.
- Windows parity.

## Inferred
Each line is something the builder did not say. Say where it came from.
- Item 2 of that list (the spec's old shadow label, "left as the dated record") is not one of the
  six: the owner said six, and the report counted that one as deliberately left (from: the report).
- "Three-digit hex" means accepting the shorthand on commit by expanding it, not refusing it with a
  better hint: the report's D8 says an existing saved "fff" still renders, so accepting is the
  consistent reading (from: run 2 ledger D8).
- The split of `ModsSettings.swift` runs last, so no other item touches the file after it and
  the merge stays clean (from: the branch layout, one worktree per item cut from the tip).
- Each item is checked by the full Mac build, test suite and selftest, as the 2026-10-17 runs were.

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so ("start 3 unattended").
