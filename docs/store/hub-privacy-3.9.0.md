# Hub privacy page, Sanduhr Windows 3.9.0: the text to add

For the hub repo (`626Labs-LLC/626Labs-LLC.github.io`), page `privacy.html`, section
`<h3 id="privacy-sanduhr">`. Paste into a hub issue or PR; the hub agent applies it. Source of truth
is `docs/PRIVACY.md` in the Sanduhr repo (the row **CI watchers**). 3.9.0 adds no network destination
and no new file, so the page needs one new paragraph and one sentence in the network section.

**Where the text lives.** The Sanduhr section is hand-written HTML in `privacy.html`
(`content/site.json` does not carry it). Phrasing follows the 3.6 watchers paragraph and the 3.7 rows
("From 3.9.0, …").

**Must be live before the 3.9.0.0 Store submission is committed**: the reviewer reads the hosted
page. Check after deploy:

```sh
curl -s https://626labs.dev/privacy.html | grep -c -e watchers_ci_runners -e docker_engine
```

Expect 2 or more.

---

## 1. New paragraph, right after the "Watchers from Claude" paragraph

```html
<p>
  <strong>CI watchers on Windows (3.9.0, optional, off by default).</strong> With <strong>Settings, Desk, Show my CI runners' jobs</strong> on, Sanduhr shows a card on the Desk for each job a GitHub Actions runner on your own PC runs. Through Docker's local pipe (<code>\\.\pipe\docker_engine</code>), it lists your containers (each one's id, image name, names, state and Compose service label) every 30 seconds (every 60 seconds while Docker isn't answering or refuses) and follows the log output of those whose image is a GitHub Actions runner (<code>actions-runner</code>, <code>github-runner</code> or <code>gh-runner</code> in the image's name). From each log it keeps only a job's name, its runner's name, when it started and ended, and its result, in memory until its card fades, you dismiss it (a failed job's card stays until you do), you turn the switch off or you quit. The names of the runners it is watching are also held in memory, for the line under the switch. It never reads any other container, any container's environment or settings (where a runner keeps its registration token), or files inside a container, and it never starts, stops or changes anything in Docker. The only thing written to disk is the switch itself (<code>watchers_ci_runners</code> in <code>settings.json</code>). There is no GitHub account, no token and no network: the pipe is local to your PC. Cards hide in demo mode, and nothing about jobs or runners is logged.
</p>
```

## 2. "What touches the network": one sentence

After the sentence that describes the Desk's watchers (or, if the page has none, after the last sentence of the paragraph that begins `Apart from Publish usage`), add:

```html
CI watchers (3.9.0) use no network: they read Docker's local pipe on your PC and send nothing anywhere.
```

## 3. No other changes

No new third party, no new host and no new file: the "Third parties" section, the data-location list
and the MCP paragraph stay as they are. `watch_start`, `watch_update` and `watch_end` are untouched.
