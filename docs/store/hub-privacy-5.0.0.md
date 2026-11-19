# Hub privacy page, Sanduhr Windows 5.0.0: the text to add

For the hub repo (`626Labs-LLC/626Labs-LLC.github.io`), page `privacy.html`, section
`<h3 id="privacy-sanduhr">`. Paste into a hub issue or PR; the hub agent applies it. Source of truth
is `docs/PRIVACY.md` in the Sanduhr repo (the **Agent notifications** row and the **Claude Code hook**
row in the Windows data table). 5.0.0 adds no network destination, no capability and no library: it
reads Codex's and the Antigravity CLI's own logs on this PC and the small files Claude Code's hook
writes, and keeps nothing, so the page needs one new paragraph.

5.0 has more parts (specs 5 to 7); each adds its own paragraph to this file before the release.

**Where the text lives.** The Sanduhr section is hand-written HTML in `privacy.html`
(`content/site.json` does not carry it). Place it right after the 4.9.0 "Codex and Antigravity CLI
activity" paragraph.

**Must be live before the 5.0.0.0 Store submission is committed**: the reviewer reads the hosted
page. Check after deploy:

```sh
curl -s https://626labs.dev/privacy.html | grep -o -e 'cc-signal' -e 'Agent notifications' | wc -l
```

Expect 2 or more.

---

## 1. New paragraph, right after the "Codex and Antigravity CLI activity on Windows (4.9.0, optional)" paragraph

```html
<p>
  <strong>Agent notifications on Windows (5.0.0).</strong> Sanduhr can tell you when an AI agent finishes a long turn or needs you, on a Windows notification, a card on the Desk, the glow or a taskbar flash; each has its own switch in <strong>Settings, Notifications</strong>. It adds no network request and needs no sign-in. For Codex and the Antigravity CLI, only while their source is on in Sources, it watches the same logs the activity count reads (<code>%USERPROFILE%\.codex\sessions</code> and <code>%USERPROFILE%\.gemini\antigravity-cli</code>) for when a turn starts and ends. For Claude Code, only while Sanduhr's hook is installed, the hook writes one small file per event under <code>%USERPROFILE%\.sanduhr</code> (<code>cc-signal-*.json</code>) naming the event (a prompt sent, Claude Code waiting on you, a turn finished), the session's folder, its id and the time. Claude Code hands every hook its input, which includes the prompt when a prompt is sent. The hook takes only the folder and the session id from it, and never writes or sends the rest. It stops writing once 100 files are waiting. Sanduhr reads each file and deletes it at once. One larger than 4 KB is deleted unread, and one whose time stamp is more than 10 minutes old, or in the future, is deleted without being used. A leftover temporary file is deleted once it is 10 minutes old. From all of these Sanduhr takes times, folder names and ids, never a prompt or a reply, and it keeps nothing: what a turn is doing is held in memory only. A notification shows the agent, the project's name (its codename in streamer mode) and how long the turn ran, and Windows keeps it in its notification history until you clear it or Windows drops it. Reset Sanduhr removes any signal file left behind. "Codex" is a trademark of OpenAI and "Antigravity" is a trademark of Google LLC, used nominatively; Sanduhr für Claude is not affiliated with, endorsed by, or associated with OpenAI or Google LLC.
</p>
```
