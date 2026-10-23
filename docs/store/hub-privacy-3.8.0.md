# Hub privacy page, Sanduhr Windows 3.8.0: the text to add

For the hub repo (`626Labs-LLC/626Labs-LLC.github.io`), page `privacy.html`, section
`<h3 id="privacy-sanduhr">`. Paste into a hub issue or PR; the hub agent applies it. Source of truth
is `docs/PRIVACY.md` in the Sanduhr repo (rows: Widget packs, The Desk companion, Widgets from Claude,
Your own art, Weather on the Desk, and the network section's GitHub and update paragraphs).

**Where the text lives.** The Sanduhr section is hand-written HTML in `privacy.html` (the 3.5.0,
3.6.0 and 3.7.0 syncs were commits to that file: 59af19d, dc2cc25, 7417e39). `content/site.json`
does not carry it. Phrasing follows the 3.7 rows ("From 3.7.0, …").

**Must be live before the 3.8.0.0 Store submission is committed**: the reviewer reads the hosted
page. Check after deploy:

```sh
curl -s https://626labs.dev/privacy.html | grep -c -e sanduhr-widgets -e widget-request.json -e companion.json
```

Expect 3 or more.

---

## 1. "Where data lives on Windows": two insertions

**a.** In the private-data list, after `the song looks you keep (<code>now-playing-looks.json</code>),` insert:

```html
from 3.8.0 the widget packs you install or make, in <code>%LOCALAPPDATA%\Sanduhr\widgets\</code> with <code>widgets.json</code> (each pack's id, version, source and whether it is on) and the companion's <code>companion.json</code> (its growth and the dates you coded),
```

**b.** After the sentence that begins `From 3.7.0, while the MCP integration is installed,` insert:

```html
From 3.8.0, with the MCP server installed, it also holds the widget handoff files, <code>widget-request.json</code> and <code>widget-result.json</code>.
```

## 2. "What touches the network": replace the update sentence, add the catalog

**a.** Before `Apart from Publish usage, if you turn it on (below), update checks are the only other traffic:` insert:

```html
From 3.8.0 on Windows, <strong>Settings, Widgets</strong> asks GitHub for the widget catalog (<code>catalog.json</code> and its signature, from the releases of <code>github.com/626Labs-LLC/sanduhr-widgets</code>) when you open that page or press Check the catalog again, and downloads a widget pack from the same releases only when you press Install or Update. GitHub may redirect those downloads to its file hosts (<code>objects.githubusercontent.com</code> or <code>release-assets.githubusercontent.com</code>); Sanduhr follows a redirect only over https and only to those hosts. Nothing polls in the background, and no account, cookie, identifier, machine name or app version goes with the request; the user agent is just "Sanduhr". Packs are data only (JSON, PNG and fonts, never code): the catalog is verified with a key built into Sanduhr before it is read, each pack's size and SHA-256 must match the catalog, and every file is checked again on your PC before the pack is installed. Nothing is uploaded.
```

**b.** Replace `Apart from Publish usage, if you turn it on (below), update checks are the only other traffic: the Windows Setup.exe build checks GitHub Releases at most once every 24 hours,` with:

```html
Apart from Publish usage, if you turn it on (below), and the widget catalog, update checks are the only other traffic: the Windows Setup.exe build checks GitHub Releases at most once every 24 hours and, from 3.8.0, downloads a newer release in the background, installing it only when you click Restart to update,
```

and after `The Microsoft Store build does not run the GitHub update check; it updates through the Store.` add:

```html
From 3.8.0 it asks the Store every 6 hours whether an update is waiting and installs it through Windows' own dialog, also only after you click Restart to update.
```

## 3. New paragraph, after "Claude designing the Desk on Windows (3.7.0, optional)"

```html
<p>
  <strong>Widgets on Windows (3.8.0, optional).</strong> Installed widget packs live in <code>%LOCALAPPDATA%\Sanduhr\widgets\</code>, one folder per pack, with <code>widgets.json</code> beside them; on the Microsoft Store build Windows keeps them in the app's package storage. The catalog fetch is described under the network section above. The <strong>Desk companion</strong> draws a sprite pack you installed and grows with the days you code, never with your usage: Sanduhr reads only the dates and total time in Sanduhr Time's day files in <code>%USERPROFILE%\.sanduhr\time\days\</code> when that folder exists (it never writes there), and otherwise the dates your local Claude Code session logs show activity, keeping no project, prompt or token count. Its growth and those dates are kept in <code>companion.json</code>; its mood comes from the usage numbers the widget already shows, and its nap from the same last-input time OLED care reads. With the MCP server installed, <code>propose_widget</code> lets Claude hand the app a widget it wrote in the conversation: a pixel-art sprite drawn as text, a palette or effect presets, never a picture, a font or code. The server names the pack <code>my-&lt;id&gt;</code> so it can never replace a catalog pack, checks it with the catalog's own rules, the trademark rule included, and writes it to <code>widget-request.json</code>; the app reads and deletes the request at once, checks it again, and answers in <code>widget-result.json</code> with a status and reasons, never the pack. <strong>Nothing is installed until you choose Apply</strong>; Dismiss removes the staged preview, and Let Claude change the Desk directly never covers widgets. <strong>Use my own art</strong> copies a PNG you pick into a sprite made here, after checking it with the pack image rules; it is never sent anywhere, and Claude never supplies an image. Nothing is uploaded from the app: sharing a pack is a pull request you make to the catalog yourself. Requests older than ten minutes are ignored, pack content is never logged, and Reset Sanduhr removes the widgets folder and the handoff files.
</p>
```

## 4. "The Desk on Windows (3.5.0, off by default)": the weather nickname

After `The weather piece is described under the network section above.` insert:

```html
From 3.8.0 it can show a name you choose in place of the city (Settings, Weather, Show it as, up to 24 characters), kept in settings and never sent.
```

## 5. "Claude Code integration on Windows": the new tool

After `described under Claude and the Desk below; the server still makes no network calls.` insert:

```html
From 3.8.0 it adds <code>propose_widget</code>, described under Widgets on Windows below; the server still makes no network calls.
```

## 6. "Third parties": GitHub's role

Replace `GitHub, which hosts both update feeds.` with:

```html
GitHub, which hosts both update feeds and, from 3.8.0, the Windows widget catalog and its packs, under <a class="inline-link" href="https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement" target="_blank" rel="noopener">GitHub's privacy statement</a>.
```

And after `and, from 3.7.0, how your Desk looks` (in the MCP sentence of the same paragraph) the text can stay: `propose_widget` only writes a request file, it exposes nothing new to MCP clients.
