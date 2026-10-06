# Spike: reload detection

Question (`spec.md > Spike: reload detection`): when a file that is open and saved in VS Code is changed on disk by another process (what the Claude Code CLI does: it writes the file directly and bypasses the editor buffer), can an extension tell the resulting `workspace.onDidChangeTextDocument` apart from the user typing?

## Ruling

**Pass for disk reloads, with a two-event rule.** The single-event signal the spec expected (`isDirty === false` right after a change) is **not enough on its own**: the first edit after every save also reports `isDirty === false` inside its own change event. The dirty flag only turns true in a second event that follows about 1 ms later. A reload never produces that second event. Classifying a change by the event after it separates the two cases, with zero false positives on typing, in every case below and on both VS Code versions tested.

The Claude Code VS Code extension's diff-accept path **was not tested**: it can't be driven in an isolated test host. See the last section for why it matters.

## Recorder rule

For each `onDidChangeTextDocument` event `e` on a `file` document:

1. `e.contentChanges.length === 0` → a dirty-state flip, not an edit. Count no lines.
2. `e.reason` is `Undo` or `Redo` → undo or redo. Not a reload. (Whether it counts as your lines is the recorder's choice; it is your action either way.)
3. `e.document.isDirty === true`, read synchronously in the handler → your edit.
4. Otherwise (`isDirty === false`, content changed, no undo/redo reason) it is a **candidate**. Keep it pending with `e.document.version`:
   - If the **next** event for the same document has no content changes, `isDirty === true` and **the same `version`**, the candidate was your first edit after a clean state. Count it as yours.
   - If the next event for that document is anything else, or no event arrives within 500 ms, the candidate is a **reload**: write it with `reload: true` and `human_line_changes: 0`.

Observed margins: the dirty flip always arrived 0 to 2 ms after the candidate, and always carried the candidate's version. The nearest following event after a reload was 85 ms or more away (case h, a user typing straight after a reload). That event carried `version + 1`, so the version check separates them without depending on the timeout.

A user-run **Revert File** from a dirty state also classifies as a reload (case c4). That is correct for line counting: the content came from disk, not from typing.

## Setup

- `@vscode/test-electron` 2.5.2 `runTests`. Each run downloaded its own VS Code, with fresh `--user-data-dir` and `--extensions-dir` folders under the OS temp directory, plus `--disable-extensions`, `--disable-workspace-trust` and `--new-window`. `VSCODE_*` and `ELECTRON_RUN_AS_NODE` were removed from the runner's environment so the child could not attach to another VS Code. User settings in the throwaway profile: `files.autoSave: off`, `editor.formatOnSave: false`, `git.enabled: false`.
- Workspace: a scratch folder `<tmp>/scratch` holding one plain-text file, three lines, open in an editor.
- The probe was an extension under development with no behaviour of its own, plus an `extensionTestsPath` suite that subscribed to `onDidChangeTextDocument`, `onWillSaveTextDocument`, `onDidSaveTextDocument` and `createFileSystemWatcher(<tmp>/scratch/**/*)`. It drove every case and exited by itself; no human input.
- External writes used Node `fs.writeFileSync` from the extension host. To VS Code that is the same as another process writing the file.
- Each case started from a saved, clean document holding the base text (reset by an untracked replace and save, followed by a 1.2 s pause so watcher events could drain).
- Format-on-save used a trivial formatter registered by the probe for `plaintext` (it deletes trailing whitespace). No marketplace formatter was needed.
- Versions: **VS Code 1.140.0** (latest stable on 2026-10-06) and **1.80.0** (the extension's engine floor), on win32. Both runs exited 0 and produced the same event sequence row for row. The only differences were timings, and 1.80.0 sometimes delivering one `fs:change` per write where 1.140.0 delivered two.

## Observed events (VS Code 1.140.0)

`t` is ms since the case's action started. `changes` is `rangeLength/rangeLines -> textLength/textLines` per content change. `whole` means the single change replaced the entire previous text. `cls` is the rule above applied live in the probe. Watcher (`fs:change`) and save rows have no document fields.

| case | t | event | isDirty | reason | n | changes | whole | version | cls |
|---|---|---|---|---|---|---|---|---|---|
| a1 type `abc` | 15 | change | false | - | 1 | 0/0 -> 1/0 | no | 2 | edit (first after clean) |
| | 17 | change | true | - | 0 | | | 2 | dirty flip |
| | 19 | change | true | - | 1 | 0/0 -> 1/0 | no | 3 | edit |
| | 20 | change | true | - | 1 | 0/0 -> 1/0 | no | 4 | edit |
| a2 `TextEditor.edit` insert | 7 | change | false | - | 1 | 0/0 -> 4/1 | no | 6 | edit (first after clean) |
| | 9 | change | true | - | 0 | | | 6 | dirty flip |
| b1 external append | 96 | fs:change | | | | | | | |
| | 97 | fs:change | | | | | | | |
| | 104 | change | false | - | 1 | 0/0 -> 14/1 | no | 8 | **RELOAD** |
| b2 external full rewrite | 79 | fs:change | | | | | | | |
| | 80 | fs:change | | | | | | | |
| | 84 | change | false | - | 1 | 29/3 -> 23/4 | yes | 10 | **RELOAD** |
| c1 type while clean (makes dirty) | 4 | change | false | - | 1 | 0/0 -> 1/0 | no | 12 | edit (first after clean) |
| | 5 | change | true | - | 0 | | | 12 | dirty flip |
| | 6, 6 | change x2 | true | - | 1 | 0/0 -> 1/0 | no | 13, 14 | edit |
| c2 external write while dirty | 80 | fs:change | | | | | | | |
| | 80 | fs:change | | | | | | | |
| | (5 s) | *no change event* | | | | | | | |
| c3 save attempt while in conflict | 2 | willSave | true | save:Manual | | | | 14 | |
| | | *no didSave; `save()` returned false* | | | | | | | |
| c4 Revert File | 8 | change | false | - | 1 | 23/2 -> 41/3 | no | 15 | RELOAD |
| | 13 | change | false | - | 0 | | | 15 | dirty flip |
| d1 type `abc` | 7 | change | false | - | 1 | 0/0 -> 1/0 | no | 17 | edit (first after clean) |
| | 7 | change | true | - | 0 | | | 17 | dirty flip |
| | 8, 9 | change x2 | true | - | 1 | 0/0 -> 1/0 | no | 18, 19 | edit |
| d2 undo to saved | 5 | change | **true** | Undo | 1 | 3/0 -> 0/0 | no | 20 | undo/redo |
| | 6 | change | false | - | 0 | | | 20 | dirty flip |
| d3 redo | 4 | change | false | Redo | 1 | 0/0 -> 3/0 | no | 21 | undo/redo |
| | 5 | change | true | - | 0 | | | 21 | dirty flip |
| d4 undo again | 2 | change | true | Undo | 1 | 3/0 -> 0/0 | no | 22 | undo/redo |
| | 3 | change | false | - | 0 | | | 22 | dirty flip |
| e1 type, then save (format off) | 5 | willSave | true | save:Manual | | | | 25 | |
| | 15, 16 | change x2 | false | - | 0 | | | 25 | dirty flip |
| | 16 | didSave | false | | | | | 25 | |
| | 91, 107 | fs:change x2 | | | | | | | |
| e2 type `abc` + 3 spaces + newline, then save (format on) | 6 | willSave | true | save:Manual | | | | 33 | |
| | 17 | change | true | - | 1 | 3/0 -> 0/0 | no | 34 | edit (formatter) |
| | 38, 39 | change x2 | false | - | 0 | | | 34 | dirty flip |
| | 39 | didSave | false | | | | | 34 | |
| | 116, 128 | fs:change x2 | | | | | | | |
| f two external writes 100 ms apart | 102, 103 | fs:change x2 | | | | | | | |
| | 107 | change | false | - | 1 | 0/0 -> 6/1 | no | 36 | **RELOAD** |
| | 203 | fs:change | | | | | | | |
| | 211 | change | false | - | 1 | 0/0 -> 7/1 | no | 37 | **RELOAD** |
| | 247 | fs:change | | | | | | | |
| g type 1 char, save at once | 8 | change | false | - | 1 | 0/0 -> 1/0 | no | 39 | edit (first after clean) |
| | 8 | change | true | - | 0 | | | 39 | dirty flip |
| | 17 | willSave | true | save:Manual | | | | 39 | |
| | 36, 37 | change x2 | false | - | 0 | | | 39 | dirty flip |
| | 37 | didSave | false | | | | | 39 | |
| h external write, then type about 90 ms later | 96, 97 | fs:change x2 | | | | | | | |
| | 101 | change | false | - | 1 | 0/0 -> 10/1 | no | 41 | **RELOAD** |
| | 197 | change | false | - | 1 | 0/0 -> 1/0 | no | 42 | edit (first after clean) |
| | 198 | change | true | - | 0 | | | 42 | dirty flip |
| | 199, 200 | change x2 | true | - | 1 | 0/0 -> 1/0 | no | 43, 44 | edit |

The typing rows for e1 and e2 have the same shape as a1 and are left out. Cases g and h go beyond the brief: g covers a save that lands right behind the first keystroke, and h covers typing straight after a reload.

## Outcomes per case

- **(a) Typing and `TextEditor.edit`:** the first change after a clean state reports `isDirty: false`, and a zero-change dirty flip with the same version follows within 2 ms. Later keystrokes report `isDirty: true`. `reason` is undefined. With the two-event rule, no typing event classified as a reload.
- **(b) External write to the open, saved file:** VS Code reloaded it automatically, with no revert needed. One content change about 80 to 105 ms after the write, `isDirty: false`, no dirty flip after it, buffer equal to disk afterwards. The watcher's `fs:change` arrived 3 to 8 ms before the content change. The reload is a **minimal diff, not a whole-document replacement**: an append arrived as a pure insert (b1), and only a rewrite that shared no text came out as `whole` (b2). "Replaced the whole document" is not a usable signal.
- **(c) External write while dirty:** no change event at all. The buffer kept the typing and stayed dirty; the only trace was the watcher's `fs:change`. A save then failed with a "file modified since" conflict: `willSave` fired, `didSave` did not, `save()` returned false, and the disk kept the external content. Revert File then loaded the disk content as a reload-shaped change. VS Code never merges silently; it leaves the conflict to the user.
- **(d) Undo to the saved state:** the content change carries `reason: Undo` and `isDirty: true`, then a dirty flip to false follows. Redo from the saved state reports `isDirty: false` with `reason: Redo`. Both are excluded by `reason` before the dirty check applies.
- **(e) Save:** a save emits only zero-change events plus `willSave`/`didSave`. With format-on-save on, the formatter's edit arrives after `willSave`, with `isDirty: true`, so it can never look like a reload. It does count as an edit by the plain rule; the recorder can drop changes that land between `willSave` and `didSave` if formatter lines must not count as yours.
- **(f) Two external writes 100 ms apart:** two separate reload events about 100 ms apart, each a minimal diff and each with no dirty flip after it. Not coalesced. The buffer ended equal to the second write.

## Not tested

- **The Claude Code VS Code extension's diff view, accepted.** It can't be driven in an isolated host (the extension needs a signed-in session and a live CLI). If accepting a diff applies a workspace edit to the buffer and then saves, it would look exactly like case g (first edit after clean, dirty flip, save), and the rule above would count it as yours. If it writes the file on disk, it looks like case b and is caught. The merger's 2-second rule (a change on a file within 2 seconds of a Claude edit to the same `entity` is not yours) covers the first shape, so keeping that rule alongside the reload tag is cheap insurance. One manual accept in a scratch file settles it (the checklist's builder checkpoint for item 2).
- `files.autoSave` set to `afterDelay` or `onFocusChange`. Auto-save adds save events after edits, which the rule treats as dirty flips; not run.
- macOS and Linux. Only win32 was run. The events come from VS Code's shared text-file model, so the sequence should hold, but the watcher timing may differ.
- Files outside the workspace folder, or excluded by `files.watcherExclude`. The reload depends on VS Code noticing the disk change; these were not run.
