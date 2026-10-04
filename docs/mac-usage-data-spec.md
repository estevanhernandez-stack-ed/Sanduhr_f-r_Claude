# Mac usage data, per account

Sanduhr on the Mac shows meters for one or more Claude accounts (2.4.0). This spec adds what
Windows calls the usage vault, the Claude Code views and the remaining MCP tools, organized
around the accounts people already have: **each account decides what Sanduhr keeps for it and
what Claude can see of it.** The meters themselves never change with these choices.

The storage formats stay the Windows ones (`docs/superpowers/specs/2026-07-12-usage-vault-design.md`
for the vault), so `sanduhr-mcp` and any other reader work the same on both platforms.

## Why per account

People run Sanduhr with several accounts for many reasons: a subscription of their own and one
through a team, an account per client, a shared account they only watch. They want the meters
for all of them, and they often want each one's data handled differently: one kept in full, one
watched live but never recorded, one never shown to Claude. A single global switch can't say
that, and a per-folder consent (Windows today) asks the question in terms of folders rather
than the accounts people think in.

## What an account can have

Each account in Settings, Accounts gets a **Data** section:

| Choice | Options | Default |
|---|---|---|
| Meter history | Off · 30 days | 30 days |
| Claude Code folder | None · one of the folders found (`~/.claude`, `~/.claude-*`, or `CLAUDE_CONFIG_DIR` homes) · Choose… | None |
| Claude Code activity | Not tracked · Live only · Keep a record | Not tracked |
| Project names in the record | Names · Hidden · Full paths | Names |
| Share with Claude (MCP) | Off · Meters · Meters and activity | Off |

- **Meter history** is the utilization series behind the sparklines and trends (`history.{label}.json`),
  today capped at 2 hours on the Mac; 30 days matches Windows.
- **Claude Code folder** links the account to the Claude Code home whose logs belong to it. A folder
  links to at most one account. Sanduhr suggests the folders it finds and never links one on its own.
- **Claude Code activity**: *Live only* reads the folder's logs for the cards' local burn and the
  Claude Usage page and stores nothing. *Keep a record* also writes the vault for that folder
  (monthly session shards, rollups, checkpoints), kept until erased.
- **Project names**: *Hidden* stores a stable short hash per project instead of its name, so the
  record still groups by project without naming it. *Full paths* is Windows'
  `vault_store_full_paths`, off unless chosen.
- **Share with Claude** decides what `sanduhr-mcp` may return for this account. *Off* means its tools
  answer as if the account did not exist. *Meters* gives `get_usage` and the meter history.
  *Meters and activity* adds the burn, model and vault tools for its folder.
- **Erase this account's data** (meter history, vault, checkpoints) is one button, with a confirmation
  naming what goes. Turning *Keep a record* off asks whether to erase what was kept.

Every choice is local. Nothing in this spec sends anything off the Mac; Windows' publish feature
stays out (merge plan: drop).

## How the MCP server learns the choices

The app writes `~/Library/Application Support/Sanduhr/mcp-access.json`: for each account its
`account_ref` (the hashed label, never the name), its sharing level, and the vault folder id it may
read. `sanduhr-mcp` reads only what that file allows and never reads `settings`, the Keychain or
the account names. No file, or an unreadable one, means nothing is shared.

## Mapping to the Windows model

Windows keeps per-folder consents (`vault_roots`, `mcp_roots`). On the Mac those are derived from
the accounts: a folder is in `vault_roots` when its account keeps a record, in `mcp_roots` when its
account shares activity. Bringing the per-account model to Windows is a separate decision.

## Defaults and the upgrade

Upgrading changes nothing anyone can see except that meter history now keeps 30 days. No folder is
linked, nothing is recorded and nothing is shared until chosen. The first time Settings, Accounts
opens after the upgrade, a short note says the Data section exists.

## Build order

1. **30 days of meter history** (item 43): `HistoryStore` keeps 30 days per account, with the
   Windows point cap; Off per account.
2. **Data section and folder linking** (item 44): the choices above, stored per account, with the
   folder discovery and the one-folder-one-account rule.
3. **Live Claude Code activity** (item 45): the log reader and the burn on the cards for linked folders.
4. **The vault** (item 46): a port of `VaultIngester`, `VaultStore`, `VaultReader` with the Windows
   tests' fixtures, Hidden names, erase.
5. **MCP tools and access file** (item 47): `get_local_burn_by_project`, `get_model_usage`,
   `get_usage_history` in `mac/integrations/sanduhr_mcp.py`, filtered by `mcp-access.json`.
6. **Claude Usage page** (item 48): overview, trends, sessions ledger, CSV export, per account.
7. **One-click install** (item 49) of the MCP server and statusline from Settings.

## Testing

Pure Swift tests for every choice's effect (what is read, written, shared), the access file, the
Hidden-name hashing and erase; the vault ports the Windows fixtures (torn files, month boundaries,
worktree folding). Development and screenshots use a test Claude Code folder or a folder whose
project names are fine to show; release material never shows real project names.

## Open questions

1. Meter history: 30 days on by default, or off until chosen?
2. One Claude Code folder per account, or allow several (for someone with two folders on one login)?
3. Is *Hidden* project names worth having in the first vault release, or later?
