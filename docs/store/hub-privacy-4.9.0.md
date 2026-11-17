# Hub privacy page, Sanduhr Windows 4.9.0: the text to add

For the hub repo (`626Labs-LLC/626Labs-LLC.github.io`), page `privacy.html`, section
`<h3 id="privacy-sanduhr">`. Paste into a hub issue or PR; the hub agent applies it. Source of truth
is `docs/PRIVACY.md` in the Sanduhr repo (the **Codex and Antigravity CLI activity** row in the data
table, and the agents clause in "Through Claude Code, not Sanduhr"). 4.9.0 adds no network destination,
no capability and no library: it reads Codex's and the Antigravity CLI's own logs on this PC and keeps
daily counts in the existing usage vault, so the page needs one new paragraph.

**Where the text lives.** The Sanduhr section is hand-written HTML in `privacy.html`
(`content/site.json` does not carry it). Place it right after the 4.8.0 "Codex limits on Windows"
paragraph.

**Must be live before the 4.9.0.0 Store submission is committed**: the reviewer reads the hosted
page. Check after deploy:

```sh
curl -s https://626labs.dev/privacy.html | grep -c -e 'antigravity-cli' -e 'Codex and Antigravity'
```

Expect 2 or more.

---

## 1. New paragraph, right after the "Codex limits on Windows (4.8.0)" paragraph

```html
<p>
  <strong>Codex and Antigravity CLI activity on Windows (4.9.0, optional).</strong> Sanduhr can count the tokens Codex and the Antigravity CLI use, beside Claude Code's. Codex follows its switch in <strong>Settings, Usage, Sources</strong>; the Antigravity CLI has its own "Activity only" switch there, off by default. It adds no network request and needs no sign-in: it reads the logs those tools already write on this PC, Codex's session logs under <code>%USERPROFILE%\.codex\sessions</code> and the Antigravity CLI's conversation logs and conversation list under <code>%USERPROFILE%\.gemini\antigravity-cli</code>. From them it takes timestamps, project folder names, model names and token counts, plus the conversation ids and workspace folders the Antigravity list holds; never a prompt, a reply, or a conversation's title or preview. The Antigravity IDE's own files are not read. Daily token counts per project are kept in Sanduhr's usage vault (<code>%LOCALAPPDATA%\Sanduhr\vault\</code>, in the package's storage on the Store build), in a folder for each agent, readable only by your Windows user account and never sent anywhere. Full folder paths are kept only if a hidden setting, off by default, is on. Purging an agent's folder on the Local burn page, or erasing the archive, removes its counts and stops reading it until you switch its source on again. With the Sanduhr MCP server installed, Claude can read an agent's project totals only after you turn on that agent's switch under <strong>Settings, Claude Code, Folders</strong> (each off by default); the answer then becomes part of that Claude conversation. "Codex" is a trademark of OpenAI and "Antigravity" is a trademark of Google LLC, used nominatively; Sanduhr für Claude is not affiliated with, endorsed by, or associated with OpenAI or Google LLC.
</p>
```
