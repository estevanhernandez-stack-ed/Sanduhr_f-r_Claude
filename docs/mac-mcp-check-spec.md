# Check an installed MCP server (Mac 2.42.3)

Status: draft for the owner, 2026-11-09. Follows #144 (Mac 2.42.2, #477), which checks the server only right after an
install.

## Why the install-time check is not enough

2.42.2 starts the `sanduhr` MCP server after an install, sends `initialize` and reports what it heard. That runs once.
An installed row shows only **Remove**, so someone whose server stopped working later has no way to ask whether it
still starts, short of Remove and Install again. Claude Code never says so either: a server that can't start shows up
as no server at all.

What the page already catches, without starting anything: `IntegrationInstaller.isCurrent` reports **Outdated** when
the registered `command` is not an executable file, when `args` is not the current script path, or when the installed
scripts are not this app's. So a Python that moved or was removed, and a stale script path, already show Outdated with
an Update button.

What only starting the server can catch, while the row says **Installed**:

- A Python that runs but fails at import (a damaged install, or a Homebrew upgrade that broke its standard library),
  or one older than the server needs.
- A server that starts but doesn't answer within the timeout, or answers as something else.

So the button proves the one thing the row's status can't: that the entry Claude Code will run gets as far as the MCP
handshake.

### The stub: a hazard, not a case

`/usr/bin/python3` is Apple's stub. Without the Command Line Tools, running it opens macOS's "install the command line
developer tools" dialog. `PythonFinder` exists to never do that: it uses the stub only when the developer folder it
forwards to holds a python3, and it checks that by looking at files.

The registered entry can still name the stub after the tools are removed. Today that row reads **Installed** (the stub
is an executable file, so `isCurrent` passes it). If another python3 was found, Check would be enabled, would run the
stub, and would pop Apple's dialog. 2.42.2's install check can't hit this, because it runs the python `PythonFinder`
just picked. A check of what is already registered can hit it. So 2.42.3 makes two changes:

1. **The probe never runs an unusable stub.** In `McpLaunchProbe.check(command:args:timeout:)`, before anything
   starts: when `command` is `PythonFinder.systemStub` and `PythonFinder.candidates(...)` does not include it (the
   same file checks, nothing run), return `.failed(noPython)`. This guards the install path too, at no cost.
2. **The row tells the truth.** `isCurrent(.mcp, …)` applies the same test, so a registered stub with no tools behind
   it reads **Outdated**, and Update rewrites the entry to the python3 that was found. Check then has the Update
   wording to give, and the row is right even when nobody clicks Check.

## What changes

### The row

On a folder's **MCP server** row, a **Check** button sits before **Remove** when the status is Installed or Outdated.
Not Installed, Another entry, and unreadable JSON get no Check: there is nothing of Sanduhr's to start.

- It is disabled under the same rule as the row's other buttons (`model.canRun(.mcp)`: nothing busy, a python3 found).
- While a check runs, the folder is busy (`busy = folder`). Every button in that folder box is disabled until the
  result is in. The check takes at most 15 seconds, the probe's timeout.
- The statusline and glow hook rows get no Check. Neither is a server Claude Code waits on, and the statusline
  already has its preview and Test with live data.

### What is started: the registered entry, not a recomputed one

The install check runs `mcpLaunch(python:)`, which is exactly what it just wrote. A later check must run what the
file says now, because that is what Claude Code will run. A new installer read does this:

```swift
/// The `command` and `args` of the `sanduhr` entry in `folder`'s `.claude.json`, as Claude Code will run them.
/// Nil when there is no entry of Sanduhr's, or it has no string command.
func registeredMCPLaunch(folder: String) -> (command: String, args: [String])?
```

It reads the same file `status(.mcp, folder:)` reads, through `configFile(.mcp, folder:)` and the placement rule. It
returns nil unless `isOurMCP` holds, and drops `args` that aren't all strings (`[]`). It runs off the main thread,
like `load`.

### The model

```swift
/// Starts the folder's registered MCP entry the way Claude Code will and says what it heard (2.42.3).
func checkMCP(folder: String) async
```

1. Set `busy = folder` and `note = McpLaunchProbe.checkingInstalled(place)`.
2. Read `registeredMCPLaunch(folder:)` off the main thread. When it is nil, the entry is gone (removed by hand since
   the page loaded): reload and say `McpLaunchProbe.noEntry(place)`, as an error.
3. Run `McpLaunchProbe.check(command:args:)` off the main thread (the `override` seam applies, so tests never spawn).
4. Set the note: green `answeringInstalled(place, version:)`, or red `notAnsweringInstalled(place, reason:, outdated:)`.

`checkMCPStarts` (after an install) stays as it is. Both share the probe and the reasons, and differ only in wording.

### The words

New sentences on `McpLaunchProbe`, beside the install ones, with the same rules: no paths beyond the folder as Settings
shows it, no em dashes, and a period at the end.

| Case | Sentence |
|---|---|
| Checking | `Checking that the MCP server for ~/.claude starts…` |
| Answered | `The MCP server for ~/.claude starts and answers (version 0.5.0-mac). Claude Code sessions started from now on use it.` |
| Answered, no version | the same, without the parenthesis |
| Not answering, row Installed | `The MCP server for ~/.claude doesn't start: <reason>. Claude Code won't see it until this is fixed. Remove it and install again, or check that Python 3 runs.` |
| Not answering, row Outdated | `The MCP server for ~/.claude doesn't start: <reason>. Click Update to point it at this version's server.` |
| Entry gone | `The MCP server isn't registered for ~/.claude any more, so there was nothing to check.` |

The reasons are 2.42.2's (`noPython`, `noServerFile`, `didNotStart`, `exited`, `noAnswer`, `notMCP`, `wrongServer`).
The stub guard reuses `noPython` ("the Python 3 it runs is missing"). That is true from the user's side, since the
stub has nothing to forward to, and the row's Update fixes it.

## Out of scope

- **Checking automatically when the page opens.** It would start a Python process on every visit, and the folder
  count is unbounded. Ruled out on 2026-11-09 in favour of the button.
- Checking from the menu bar, or on a schedule.
- Showing the server's stderr. It can name a path, and streamer mode hides paths. 2.42.2's rule stands.
- Windows. Same gap (`ClaudeCodeViewModel.cs:783` checks only after Install). It gets a parity issue with this spec
  linked. The Windows agent owns the change.

## Release shape

A patch: 2.42.3, build 60. No What's New card (`WhatsNewTests` needs none for a patch). CHANGELOG under
`## Unreleased (mac)`: "An installed MCP server has a Check button that starts it and says whether it answers." and
"An MCP entry that names Apple's python3 stub with no developer tools behind it reads Outdated, so Update fixes it."

## Tests

In `McpLaunchProbeTests` (or a new `McpCheckTests` suite, serialized like it):

1. `registeredMCPLaunch` returns the file's command and args for Sanduhr's entry, including a command that differs
   from the current python (a moved Python must be checked as registered, not as recomputed).
2. It returns nil for no file, no `mcpServers`, someone else's `sanduhr` entry, and a non-string command.
3. `checkMCP` passes the registered command and args to the probe (override asserts on them), and sets the green
   sentence on `.answered`.
4. On `.failed`, the sentence is red and carries the reason. An Outdated row gets the Update wording, and an Installed
   row the Remove-and-install wording.
5. An entry removed between load and Check gives the "isn't registered" sentence and reloads.
6. `busy` is the folder during the check and nil after it, so the box's buttons come back.
7. The no-path, no-em-dash test covers the new sentences.
8. The stub guard: with `candidates` excluding the stub, `check(command: "/usr/bin/python3", …)` returns `noPython`
   and starts nothing. The guard takes the same injectable `fileExists` / `developerDir` as `PythonFinder.candidates`,
   so the test never touches the real stub. With the stub usable, the guard steps aside.
9. `isCurrent(.mcp)` reads Outdated for a stub entry with no developer tools behind it, and Installed with them
    (`IntegrationInstallerTests`, same injection).
10. The real server, through the registered entry written by a real install into a temp folder, answers (the
   `theRealServerAnswers` shape, through `registeredMCPLaunch`).

## Docs

- `mac/README.md`, where 2.42.2's check is described: add the Check button, and that it runs the registered entry.
- `docs/mac-smoke-test.md`: a new section 76, with three steps. Check on an installed row turns green. Check after
  pointing `command` at a script that exits 3 says "stopped at once (exit code 3)". Restore the file from its
  `.sanduhr-backup`, or Remove and install again.
- `docs/PRIVACY.md`: no change. 2.42.2 already covers starting the local server, and nothing new leaves the Mac.

## The walk

On the work Mac, against the release-built dev app, signed out, with the usual backups:

1. On `~/.claude` (Installed), Check turns green with version 0.5.0-mac.
2. Point a scratch folder's entry at a script that exits 3: the check turns red with the exit code.
3. Point a scratch folder's entry at a python3 path that doesn't exist: the row already says Outdated, and Check
   gives the Update wording.
4. The stub case is not walked: it would mean removing the Command Line Tools from a Mac. The injected tests (8, 9)
   cover it, and the walk notes the gap.
