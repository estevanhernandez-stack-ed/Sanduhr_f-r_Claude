<!-- Written once at intake, before any item is planned. Keep the Ask
     verbatim. Every Inferred line is also a ledger entry in decisions.md. -->

# Run Brief: just-the-desk

## Ask
"take it", answering the offer "Next up when you want it: Just the Desk, backlog #4, as 2.29.0."

## Source
- **Kind:** a backlog item's remaining shape and open questions
- **Location:** `docs/BACKLOG.md` #4 ("Just the Desk": Sanduhr without a Claude account), the
  "Still open" line, the "Shape to spec" list and the two open questions; `mac/README.md` "Just
  the Desk" and "Welcome tour"; `docs/mac-smoke-test.md` section 45

## Intent
Everything backlog #4 asks of the Mac app is built, so the item can close on this surface: a person
who picks Just the Desk gets a tour of what they have (the open question answered yes), the
landing page and README stop saying a subscription is required, and the backlog records what moved
to the Windows agent and the hub page.

## Done when
- A tour taken with Just the Desk on shows the Desk cards only (the desktop, the menu bar, the
  notch on a Mac with one) and ends on a "Sign in any time" card that opens Accounts; it is offered
  once, a moment after Just the Desk is chosen, and records its state the way the signed-in tour does.
- A `just-the-desk on|off` debug hook, a scenario that drives the Desk-only tour, and the smoke-test
  and README lines for both.
- `docs/index.html` and `README.md` say pacing needs a Claude subscription and the Mac's Desk does
  not, with the trademark disclaimer kept.
- `docs/BACKLOG.md` #4 reads as shipped on the Mac, answers its two open questions, and names what
  stays with the Windows agent (Store copy, parity) and the hub page.
- `cd mac && ./build.sh --debug`, `./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.

## Out of scope
- The ship commit (2.29.0, the What's New card, CHANGELOG): after the review, on the owner's "ship".
- Store copy and Windows parity: the Windows agent's.
- The hub page at 626labs.dev/sanduhr: the owner's other machine.
- The Homebrew cask's `desc` line: `brew audit` rules on it and the gain is small.
- A tour variant for a signed-out Mac that did not choose Just the Desk (the existing signed-out
  first card stays).

## Inferred
Each line is something the builder did not say. Say where it came from.
- The run is unattended: every Cart list this session ran unattended on the owner's say-so, the
  owner is not watching, and an attended run would build in the working tree instead of a branch
  for the PR review the owner expects (from: the session's pattern, D1).
- The open question "does the tour get a Desk-only variant" is answered yes: a Just the Desk user
  today never sees the tour (it waits for a fetch), and Take the Tour… shows them cards about
  meters, accounts and Claude Code they do not have (from: `WelcomeTour.showsAfterFetch` and
  `signedOut`, D2).
- The open question "skip the Keychain prompt path" is answered no change: the launch's
  `KeychainStore.exists` is one lookup of the app's own item, which a signed release build answers
  without a prompt; the prompt is the dev build's partition list (from: `CLAUDE.md` Gotchas, Mac, D3).
- The landing copy that is in this repo is `docs/index.html` and `README.md`; the hub page is not
  (from: `README.md` line 37, D4).
- The menu bar already shows the icon alone with no numbers (smoke section 45 step 1), so no item.

## Mode
- **Mode:** Unattended
- **Triggered by:** Inferred (D1); the owner's "take it" to the offer, with no one watching.
