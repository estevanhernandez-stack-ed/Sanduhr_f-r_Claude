# Widget pack fixtures (copied from sanduhr-widgets)

Source: `626Labs-LLC/sanduhr-widgets` at commit **473597d** (main, "feat/p256-and-pixel-sheets" merged:
P-256 signing and sprite.json v2 pixel sheets).

| Folder here | Source path | What it is |
|---|---|---|
| `good/` | `tools/fixtures/good/` | Packs that pass `validate.py --strict` with no findings. |
| `bad/` | `tools/fixtures/bad/` | Packs one deliberate mistake away from a good one, named for the mistake. |
| `packs/` | `packs/` | The four first-party packs (hourglass, oled-neon, oled-neon-effects, oled-neon-palettes). |
| `python-expected.tsv` | generated | `validate.validate_pack` findings for every folder above, one per line: `<group>/<pack>`, `ERROR` or `WARN`, the message. |

`PackValidatorFixtureTests` asserts the C# `PackValidator` gives the same findings: every good pack and real pack is
strict-clean, every bad pack fails with exactly the Python's errors (byte-equal messages, except theme schema
messages, which come from Core's `ThemeLint` and are matched by substring), and `bad-theme-light-bg` warns, failing
only under strict.

The files are byte-exact copies (`.gitattributes` here turns off line-ending conversion, since this repo checks out
with `core.autocrlf=true`).

## Drift

These are a snapshot, not a submodule. When FORMAT.md or `tools/make_fixtures.py` changes in `sanduhr-widgets`:

1. Regenerate the fixtures there (`python tools/make_fixtures.py`) and land the change.
2. Copy `tools/fixtures/good`, `tools/fixtures/bad` and `packs/` over these folders.
3. Regenerate `python-expected.tsv` from that checkout: for each pack, `validate.validate_pack(folder)`, writing
   `<group>/<name>\tERROR\t<message>` per error and `\tWARN\t` per warning, LF line endings.
4. Update the commit above, port the rule change to `PackValidator.cs`, and run the tests.

A new bad fixture with no line in `python-expected.tsv` fails the tests, so a copy without step 3 cannot pass.
