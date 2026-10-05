# mediaremote-adapter (vendored)

The source for Sanduhr's now playing (checklist item 53, findings in `docs/mac-now-playing-spike.md`).

- **Origin:** https://github.com/ungive/mediaremote-adapter, commit
  `29718252613a5b0e210bdc64de0bd944ab379706` ("Add convenience script for development").
- **License:** BSD 3-Clause, `LICENSE` here (Jonas van den Berg and contributors). The same text is in
  `mac/THIRD-PARTY-NOTICES.txt`, which ships in the app and opens from Settings, About.
- **Copied:** `LICENSE`, `CMakeLists.txt` (for reference), `bin/`, `include/`, `src/`, and upstream's
  README as `UPSTREAM-README.md`. Left out: `.git`, `.vscode`, `scripts/`, the `Makefile`, build output.
- **Local changes:** none. The files are byte for byte as upstream.

## How Sanduhr builds and uses it

`mac/scripts/build-mediaremote-adapter.sh` compiles the same sources as upstream's `CMakeLists.txt`
with plain clang (no CMake), universal (arm64 and x86_64), deployment target 14.0, warnings off for
this code. `mac/build.sh` calls it and bundles:

| File | In `Sanduhr.app/Contents/` |
|---|---|
| `MediaRemoteAdapter.framework` | `Frameworks/` |
| `MediaRemoteAdapterTestClient` | `Helpers/` |
| `bin/mediaremote-adapter.pl` | `Resources/NowPlaying/` |

Release builds sign the helper, then the framework, then the app (Developer ID, hardened runtime,
timestamp); dev builds sign ad hoc. Sanduhr runs `/usr/bin/perl mediaremote-adapter.pl <framework>
<test client> test` once, then `... stream --no-artwork --micros` as a child process
(`NowPlayingController`). Controls do not go through the adapter: Sanduhr sends them itself.

## Updating

Copy the same paths from a newer upstream commit, keep `README.md` (this file), update the commit here
and in `THIRD-PARTY-NOTICES.txt`, run `./build.sh --debug`, and check Settings, Desk, Now Playing says
"Adapter working" with something playing.
