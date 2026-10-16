# Agnostify: a pack any coding agent can use (item 76, slice H)

The owner, 2026-10-15: "we are also going to make an agnostify tool so any agent can use as much of the config as possible (second folder in the zip)". A config pack carries a Claude Code folder for Claude Code. The second folder, `any-agent/`, carries the same rows in the formats other agents read, so a pack is useful to someone on Codex, Gemini CLI, Cursor or Copilot, on any OS, with no Sanduhr.

## What other agents read (checked 2026-10-15)

| Piece | Standard | Who reads it |
|---|---|---|
| Instructions | `AGENTS.md`, under the Agentic AI Foundation [web https://codex.danielvaughan.com/2026/04/07/agents-md-open-standard-cross-tool-portability] | Codex, Copilot, Cursor, Windsurf, Amp, Devin, Aider, Zed and more natively; Gemini CLI with `"context": {"fileName": "AGENTS.md"}` [web https://www.geminicli.com/docs/cli/gemini-md] |
| Skills | Agent Skills, `SKILL.md` with `name` and `description` [docs https://agentskills.io/specification] | Codex, Gemini CLI, Cursor, Copilot; `~/.agents/skills/` is the shared user folder Codex and Gemini CLI read [web https://geminicli.com/docs/cli/skills/] [unverified for Cursor and Copilot] |
| MCP servers | MCP, but a config file per agent | Codex `~/.codex/config.toml` `[mcp_servers.<name>]` [docs https://learn.chatgpt.com/docs/extend/mcp?surface=cli]; Cursor `~/.cursor/mcp.json` `mcpServers`, `${env:NAME}` [docs https://cursor.com/docs/context/mcp]; VS Code `mcp.json` `servers`, and `.mcp.json` `mcpServers` for sharing [docs https://code.visualstudio.com/docs/copilot/customization/mcp-servers]; Gemini CLI `~/.gemini/settings.json` `mcpServers`, `$VAR`, `httpUrl` for HTTP [docs https://geminicli.com/docs/tools/mcp-server/] |

Skills are the one piece that is already the same file everywhere: a Claude Code skill folder is an Agent Skills folder. Instructions need a rename. MCP needs one file per agent, because each spells servers, transports and secrets its own way.

## The folder

```
any-agent/
  README.md                  where each file goes, per agent, as copyable commands
  AGENTS.md                  CLAUDE.md when checked, then an index of the skills and env names
  skills/<name>/SKILL.md     every skill, then every agent and command as a skill
  mcp/mcp.json               mcpServers, ${NAME} (Claude Code, .mcp.json, Copilot CLI)
  mcp/cursor.json            mcpServers, ${env:NAME}
  mcp/vscode.json            servers, ${env:NAME}
  mcp/gemini.json            mcpServers, $NAME, httpUrl
  mcp/codex.toml             [mcp_servers.<name>], env_vars, bearer_token_env_var, env_http_headers
  LEFT-OUT.md                each checked row that has no equivalent, and why
```

- **Built from the same checked rows.** Nothing reaches `any-agent/` that isn't in `files/` or the manifest already, so the secret scan, path rewriting and the work stamp cover it unchanged.
- **Listed in the manifest** under `files` with sizes and hashes, so 2.21.0 verifies it and its import (which reads only `files/`) ignores it. The schema stays 1.
- **Skills** are copied as they are. A name that breaks the standard (capitals, `--`, over 64 characters) is rewritten to fit and the folder renamed with it; the README lists each rename.
- **Agents become skills.** A subagent's prompt becomes a skill body, its description the skill's, with `compatibility: Written as a Claude Code subagent; runs inline here.` Tools and model lines are dropped and listed in `LEFT-OUT.md`.
- **Commands become skills.** The description says to use it when the user asks for `/<name>`; `$ARGUMENTS` stays, with a line saying what it stands for.
- **MCP servers** keep the pack's `${NAME}` references; each file spells them as its agent expands them: `${env:NAME}` for Cursor and VS Code [unverified for VS Code], `${NAME}` for Gemini CLI and `.mcp.json`, and `${HOME}` as `${userHome}` where that is the spelling. A pack already writes every header as a whole `${NAME}`, so Codex reads each through `env_http_headers` and each env value through `env_vars`; Codex has no SSE transport, so an SSE server is left out of `codex.toml` and named.
- **AGENTS.md** is CLAUDE.md when its row is checked, followed by an index: each skill with its description, and the environment variables the setup expects. Without CLAUDE.md it is the index alone (the owner, 2026-10-15).
- **Left out, and said:** plugins (a Claude Code marketplace; each one's skills could come later), hooks, permissions, output styles, the statusline, settings keys, and env names (listed in the README as variables to set).

## Export and import

- The export sheet gains one switch, **Also for other agents (any-agent/)**, on by default. It adds no rows; the count line says how many files it adds.
- The 256-entry and 20 MB caps count both folders. When `any-agent/` would pass them, the sheet says so and the switch turns off for that export.
- Opening a pack ignores `any-agent/` (it has its own README). A future slice could offer to put it into another agent's folder; not this one.

## Decided (the owner, 2026-10-15)

1. MCP files for Codex, Gemini CLI, Cursor and VS Code, plus `.mcp.json`. Windsurf, Zed and goose when someone asks.
2. Agents and commands travel as skills.
3. `AGENTS.md` is always written; without CLAUDE.md it is the index alone.

## Built (2026-10-15)

`AnyAgent` builds the folder from the chosen rows; `ConfigPack.write(anyAgent:)` adds it and lists it in the manifest; `ConfigPack.anyAgentFiles` returns nil past the caps; the export sheet's switch says how many files it adds. `AnyAgentTests` covers the Verify list but the hand check.

## Verify

swift-testing: each MCP file for a stdio server with env references and an HTTP server with a bearer header and another header, compared against fixtures; the TOML parsed back by a minimal reader in the test; skill name repair; an agent and a command converted; `any-agent/` listed in the manifest and read by `ModPackReader`; the caps with the switch on. By hand: a pack's `any-agent/skills` copied to a scratch `~/.agents/skills` under a temporary `HOME`, and `codex` and `gemini` listing the skills if installed [unverified: neither is installed on this Mac].
