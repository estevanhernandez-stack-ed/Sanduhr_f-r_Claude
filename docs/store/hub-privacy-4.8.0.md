# Hub privacy page, Sanduhr Windows 4.8.0: the text to add

For the hub repo (`626Labs-LLC/626Labs-LLC.github.io`), page `privacy.html`, section
`<h3 id="privacy-sanduhr">`. Paste into a hub issue or PR; the hub agent applies it. Source of truth
is `docs/PRIVACY.md` in the Sanduhr repo (the **Codex limits** paragraph and the two Codex rows in the
data table). 4.8.0 adds no network destination and no capability: it reads Codex's own logs on this PC
and keeps two local files, so the page needs one new paragraph (the page is prose, no tables, so the
files are named in it).

**Where the text lives.** The Sanduhr section is hand-written HTML in `privacy.html`
(`content/site.json` does not carry it). Phrasing follows the 3.9 CI watchers paragraph ("From 4.8.0,
…").

**Must be live before the 4.8.0.0 Store submission is committed**: the reviewer reads the hosted
page. Check after deploy:

```sh
curl -s https://626labs.dev/privacy.html | grep -c -e 'history.@codex.json' -e 'snapshot.codex.json'
```

Expect 2 or more.

---

## 1. New paragraph, right after the "CI watchers on Windows" paragraph

```html
<p>
  <strong>Codex limits on Windows (4.8.0, optional, off by default).</strong> With Codex ticked in <strong>Settings, Usage, Sources</strong> (or in Set up Sanduhr), Sanduhr shows your Codex plan's 5-hour and weekly limits beside Claude's. It adds no network request and needs no sign-in: it reads the session logs Codex already writes on this PC, under <code>%USERPROFILE%\.codex\sessions</code> and under the folder the <code>CODEX_HOME</code> setting names when it is set (plain <code>.jsonl</code> and compressed <code>.jsonl.zst</code>), for the plan percentages and reset times only. It never opens Codex's <code>auth.json</code> and never keeps a prompt or a reply. The numbers stay on this PC and are never sent anywhere. They are kept in two files, readable only by your Windows user account: <code>%APPDATA%\Sanduhr\history.@codex.json</code>, the plan percentages and reset times for up to 30 days (rolling) for the sparkline and trends, with no account name, prompt or reply, removed by Reset Sanduhr; and, only while the Claude Code statusline or the Sanduhr MCP server is installed, <code>%USERPROFILE%\.sanduhr\snapshot.codex.json</code>, the percentages, reset times, plan name and the time of Codex's last event (never a key or an account), rewritten as Codex's logs change and deleted on Remove or Reset. With the MCP server installed, its <code>get_usage</code> tool can return the Codex numbers to Claude like the Claude ones, and the Claude Code statusline can print them. "Codex" is a trademark of OpenAI, used nominatively; Sanduhr für Claude is not affiliated with, endorsed by, or associated with OpenAI.
</p>
```
