# Sanduhr for Mac: setup guide

You just installed Sanduhr für Claude. Start with task 1, then pick the tasks you want.

Sanduhr is an independent tool. It is not made by or affiliated with Anthropic.

## Where things are

| Thing | Where | What it does |
| --- | --- | --- |
| Menu bar icon | The hourglass in the menu bar, with a percent beside it. | Click it to show or hide the widget. Right-click (or Control-click) it for the Sanduhr menu. |
| Widget | A small floating window. | Your limits as bars, with a row of buttons at the bottom. Two-finger click it for the Sanduhr menu. |
| Desk | Your desktop, under every window. Off until you turn it on. | A clock, your meters, today's meetings and a message, drawn on the wallpaper. |
| Notch | Around the camera at the top of a MacBook screen. Needs the Desk. | Black "wings" beside the camera that show the time, your meters, a meeting, a song and more. |
| Settings | Settings… in any Sanduhr menu, the gear on the widget, or its shortcut (⌥S unless you changed it on General). | Every setting, in one window. The sidebar lists the pages in five groups: the first four pages, then Desktop, Claude Code, Widget and Help. Type in **Search**, above the sidebar, to find any setting by name: Return opens the best match, scrolled into view. |
| The Sanduhr menu | The same menu everywhere: menu bar icon, widget, the Desk's pieces. | Show or hide the widget, Menu Bar Shows, Tools, Refresh, Settings…, Arrange Desk…, Check for Updates…, What's New…, Take the Tour…, Quit. |

In this guide, "Settings, Accounts" means: open Settings, then click **Accounts** in the sidebar. A setting the guide names is also one search away: type **glow**, **percent** or **margins** and press Return. On a short window (under 600 pt), each page's preview folds to a strip at the top; click it to see the preview. A few expert settings sit under **Advanced**, folded, at the bottom of their page.

## 1. Install and sign in

You get your own claude.ai usage on your Mac, refreshed every 5 minutes.

1. Install Sanduhr from the DMG (drag it to Applications) or with Homebrew, then open it.
2. The widget opens with a sheet called **Welcome to Sanduhr**.
3. Click **Sign In to Claude…**. A window opens with claude.ai's own sign-in page.
4. Sign in the way you normally do. When you're in, the window closes by itself and your numbers appear.

**Signed up with Google?** Google doesn't allow sign-in inside apps, so the window can't finish it. When it reaches Google's page, it shows the steps instead:

1. Click **Open claude.ai in Browser** and sign in there with Google.
2. Open the browser's developer tools. In Chrome press Option+Command+I, then click **Application**. In Safari choose Develop, Show Web Inspector, then **Storage**.
3. Under Cookies, then claude.ai, find **sessionKey** and copy its value.
4. Click **Paste a Key Instead**. Settings opens at Accounts.
5. Paste the value into the **sessionKey** field and click **Save**.

You can also click **Paste a Key Instead** right on the welcome sheet and do the same steps.

**Where the key is kept.** Sanduhr keeps only the session key, in your macOS Keychain. It sends it only to claude.ai, to read your usage.

If it doesn't work: if the widget says "Cloudflare", copy the **cf_clearance** cookie the same way and paste it into the **cf_clearance (optional)** field in Settings, Accounts. Most people never need this.

## 2. Read your numbers

You get every limit as a bar, with a mark that says whether you're ahead of pace.

**On the widget**, each card is one limit: Session, Weekly and any model limits your plan has.

- The percent on the right is how much of that limit you've used.
- The thin tick on the bar is the **pace marker**. It shows where an even pace would put you right now. A bar past the tick means you're using it faster than it refills.
- Under the bar, **Resets in** counts down to the reset. Beside it, the pace reads **On pace**, a percent **ahead** (orange) or a percent **under** (blue).
- Hover a card to see **Cool down** (how long to wait to get back on pace) or **Surplus** (how much room you have).
- When you're on track to run out early, a red line says **At current pace, expires in** and a time.
- A bar that turns red with a glow is nearly full with the reset still far off. Settings, **Alerts**, under **Each limit**, sets that warning per limit (**Warn when nearly full**, **At**, **Only while the reset is more than**) and, for a limit that looks temporary, **Show this limit**. The same page holds the notifications.

**In the menu bar**, the hourglass shows one percent. It turns orange at 75% and red at 90%.

To choose what it shows:

1. Right-click the hourglass (or two-finger click the widget, or any piece of the Desk).
2. Choose **Menu Bar Shows**.
3. Pick **Session**, **Weekly**, **Whichever is higher** or **Rotate (session and weekly)**. Rotate switches every 8 seconds and marks them S and W.

The same choice, under the same name, is in Settings, General, under Menu bar: **Menu Bar Shows**.

## 3. Put things on your desktop

You get a clock, your meters, your meetings and a message on the wallpaper, under every window.

**Turn it on**

1. Open Settings, **Desk**.
2. At the top, turn on **Desk: clock, meters, meetings and the message on the desktop**.

Settings, **General** shows each surface's state under Surfaces, with a button to its page (**Desk Settings…**, **Notch Settings…**, **Widget Settings…**).

The pieces are: **Message** (a line of your own, see task 5), **Clock and date**, **Claude meters (line)** (your numbers in one line), **Claude meters (bars)**, **Now playing**, **Watchers** and **Meetings** (today's calendar; macOS asks for Calendar access the first time, and **Read today's meetings** under the Meetings row turns it off).

**Move pieces right on the desktop**

1. In any Sanduhr menu, choose **Arrange Desk…**. (Or open Settings, **Desk** and click **Arrange Desk…**.)
2. Every piece gets an outline and a round handle. A bar with **Cancel** and **Done** floats in the middle of the screen.
3. Drag a piece toward any of the eight places: the four corners, the top and bottom centers, and the middle of each side. It snaps to the nearest.
4. Drag a piece up or down its stack to reorder it. Drag its round handle to resize it.
5. Press Return (or click **Done**) to keep it. Press Escape (or click **Cancel**) to put everything back.

**Move pieces from Settings.** Settings, **Desk** has the same choices as menus. **Where each piece sits** picks a place (or **Hidden**) and a size for each piece; to keep your meters off the desktop, set both Claude meters pieces to **Hidden**. **Order** lists the stacks; drag to reorder. **Margins**, under **Advanced** at the bottom, keeps pieces away from the screen edges.

**Change the look.** Settings, **Desk Look** sets the **Desk font**, the **Message font**, the sizes of the clock and message, and the colors: pick a preset under **Colors**, or type your own hex colors under **Advanced** (one, or two to four with commas for a gradient). EsteFont Pro, a handwriting font, is the Desk font on new installs.

**Clicks on the clock and message.** A plain click does nothing. Two-finger click either one for the Sanduhr menu, plus **Desk Look Settings…** or **Edit Messages…**.

If it doesn't work: desktop icons right under the clock or message can't be clicked while those pieces take clicks. Turn off **Clock and message take clicks** in Settings, Desk, under Clicks.

## 4. Use the notch

You get a black island around the camera, with information on either side.

1. Turn on the Desk first (task 3).
2. Open Settings, **Notch**.
3. Turn on **Notch: the island around the camera**, at the top. While the Desk is off, the page says **Needs the Desk.** with a **Desk Settings…** button.

The island has three places:

- **Left wing** and **Right wing**, beside the camera. They need **Text beside the camera** on.
- **Under the camera**, a strip below it. Turn on **Text under the camera too (desktop only)**. With no height below the notch yet, this sets **Extra height below** to 18 pt, so the strip shows at once.

Each place shows one thing: **Next meeting, or the time**, **Next meeting, or the Claude meters**, **Time**, **Claude meters**, **Message**, **Now playing**, **Watchers** or **Nothing**. With nothing to show, Watchers and Now playing give way to that place's usual content.

Under **Advanced**, at the bottom of the page, **Size** sets **Extra width each side** and **Extra height below (0 = none)**, and **Notch text color** sets the color of the island's text.

**Notch glow.** The notch's edge can glow softly for a few seconds. Under **Notch glow**, pick **For Sanduhr alerts**, **A minute before a meeting** or **When the camera fill light comes on**. Click **Test Glow** to see it. The two Claude Code rows in the same section are in task 7.

**Camera and mic.** Under **Camera and mic**:

1. **Show the red dot**: choose **For cameras without a visible light** for an external camera or a closed lid, or **Always**.
2. Turn on **Show a mic while the microphone is on**.
3. **Where they show** picks one place: **Beside the camera, left**, **Beside the camera, right**, **In the left wing**, **In the right wing** or **Under the camera**. In a wing or under the camera they show while one is in use, and that place shows its own text the rest of the time.

These are indicators only. Sanduhr never mutes, records or changes a device. Click one to see what's in use, with **Notch Settings…** at the bottom.

Click the island itself to open Settings, Notch.

If it doesn't work: a screen without a notch is left alone. The camera and mic indicators show in a small tab at the top center instead.

## 5. Write your Desk messages

You get a line of your own on the desktop, with its own colors and motion, and different lines on different days.

1. Open Settings, **Message** (or two-finger click the message on the desktop and pick **Edit Messages…**).
2. Click **Add Line**, at the top of the page. A new row appears at the top of the list, drawn the way the Desk draws it, with its settings open.
3. **When**: **Every day**, a weekday, or **A date** (pick the month and day; it repeats every year).
4. **Text**: what the Desk says.
5. **Color**: **As the Desk**, **One color** or **Gradient**, or pick a **Palette**.
6. **Glow**, **Size** and **Letters** set the rest of the line's look.
7. **Motion**: **None**, **Write in** (draws itself once), **Shimmer** or **Sweep**.
8. Click **Done**, then **Save**.

**Add Line**, **List | Text**, **Save** and **Revert** stay at the top while the list scrolls. Under them, **Rotation** (folded to a one-line summary until you change it) holds:

- **Change the line**: **Once a day** or **Every hour**.
- **Mix every-day lines in on days with their own line**: off, a Friday line replaces the everyday lines on Fridays. On, they take turns.
- **On special days** decides how a date's lines share the Desk with the day's line: **Stack** (all at once), **Take turns** or **Scroll**. With the last two, **Each line shows for** sets the timing.

The pin button on a row shows that line every day. Special days still show above it.

**List | Text** at the top switches views. **Text** shows the whole file, with a list of tags beside it, if you'd rather type; **List** goes back. Your edits carry over either way. In the file, a line that starts with `Mon:` is a line for Mondays: it replaces the every-day lines, or with **Mix** on takes turns with them.

If it doesn't work: if the Desk shows an old line, click **Save**. Unsaved changes never reach the desktop.

## 6. Add a second account

You get a second Claude account, with its own key and history, and a quick way to switch.

1. Open Settings, **Accounts**.
2. Click **Add Account…**.
3. Type a **Label**, such as Work.
4. Click **Sign In to Claude…**, or paste its sessionKey.
5. Leave **Make it the active account** on if you want to see it now.
6. Click **Add Account**.

With two or more accounts:

- The widget's title shows the active account's name as a chip. Click the chip for the next account.
- Every Sanduhr menu gains an **Accounts** submenu with each account and **Manage Accounts…**.
- **Follow the account I'm using** appears at the bottom of Settings, Accounts. On, Sanduhr checks your other accounts every 15 minutes and switches to the one you're using. Switching by hand pauses it for up to 3 hours.

To change an account later, select it in the list. You'll find **Rename**, **Sign Out**, **Remove Account…** and **Make Active**.

## 7. Connect Claude Code

You get your meters inside Claude Code, a notch that glows when Claude needs you, and Claude can read your usage.

**Install, per Claude Code folder**

1. Open Settings, **Claude Code**.
2. Under **Folders**, each Claude Code folder has its own box (most people have one, `~/.claude`). Use **Add Folder…** beside the Folders heading if yours isn't listed.
3. The box's first row, **Account**, says which account the folder belongs to, or **Follows the active account**. **Change in Accounts…** opens the account's Data, where the link is set.
4. Click **Install…** beside what you want:
   - **MCP server**: lets Claude Code ask Sanduhr about your usage, and suggest Desk messages, themes and song looks.
   - **Statusline**: your meters on a line under Claude Code's prompt.
   - **Claude Code glow hook**: tells Sanduhr when Claude Code waits on you or finishes, so the notch can glow.
5. A sheet says what it adds. Click **Install**.
6. The box's last line, **Meters above the prompt: on** (or off, or not installed), is Sanduhr's mod that draws your meters as animated bars above the prompt. It is a mod, so it switches on Settings, **Mods & Config**: click **Mods & Config Settings…** right there.

**Meters above the prompt, per Claude Code folder**

1. Open Settings, **Mods & Config** (or search **meters above**). Sanduhr's own mod, sanduhr-meters, is at the top, one row per Claude Code folder.
2. Turn its switch on. The first time in a folder, a sheet says what it adds; click **Install**.
3. **Off** switches the mod off for that folder (its `enabledPlugins` entry turns false) and keeps the entry and the mod's files. **Remove** takes the entry out, deletes Sanduhr's record and puts the folder's settings back exactly as before. **Update** shows when this Sanduhr carries a newer version; if the new one can do more, it asks first.
4. If a project's own settings keep the mod on or off, the row says so.

Remove takes out exactly what Sanduhr added.

**Already have a statusline?** The sheet offers **Combine** (keeps yours and adds Sanduhr's), **Replace** or **Cancel**. Combine lets you pick which pieces of each line stay and how they join. Check **Show Sanduhr's meters above the prompt instead (animated)** to keep your line as it is and put Sanduhr's meters above the prompt (turn on Meters above the prompt for the same folder, on Mods & Config).

**Turn on the glow.** The Claude Code glow hook only tells Sanduhr. To see it, open Settings, **Notch** (or click **Notch Settings…** right there in Claude Code), and under **Notch glow** turn on **When Claude Code is waiting on you** or **When Claude Code finishes**. **Not while a terminal is in front** skips it when you're already looking.

**Share with your agents.** The MCP server shares nothing until you say so, per account:

1. Open Settings, **Accounts** and select the account.
2. Scroll to **Data**.
3. Set **Share with your agents** to **Meters** or **Meters and activity**. **Off** is the default.

If it doesn't work: the integrations need Python 3.9 or later. When Claude Code says none was found, click **Install Command Line Tools…**, then **Check Again**. Changes reach new Claude Code sessions.

## 8. Watch a long run

You get a live card for a build, a release or a long task, which glows when it needs you.

1. Open Settings, **Watchers**.
2. Turn on **Let agents show watchers**. Claude Code (with the MCP server installed) can then start a card, update its progress and end it.
3. Optional: turn on **Show Claude Code's background work** to see Claude Code's background tasks as cards too. This needs the Claude Code glow hook from task 7; until a folder has it, the page says so with a **Claude Code Settings…** button.
4. Place them, under **Where they show**: **On the notch** picks **Left wing**, **Right wing** or **Under the camera**, and **On the Desk** picks one of the eight places. They are the same choices as in Settings, Notch and Settings, Desk.
5. Optional: turn on **Show watchers above the prompt** to see them as rows above Claude Code's prompt (needs Meters above the prompt; the page lists which folders have it, and **Mods & Config Settings…** opens the mod's switches).

A card shows a title, a state dot (running, waiting on you, passed, failed), the time so far and the progress. Waiting on you pulses, and glows the notch once if watchers show somewhere: **Glow when a watcher waits on you** says whether they do now, and why not. Click a card to open its link. Cards live in memory only, so quitting Sanduhr clears them.

## 9. Now playing

You get the song or video playing on your Mac on the notch or the desktop. Click it to play or pause.

1. Place it. In Settings, **Notch**, set a wing or **Under the camera** to **Now playing**. Or in Settings, **Desk**, give the **Now playing** piece a place.
2. Open Settings, **Now Playing** for the rest:
   - **Hide while paused** under Show.
   - **When nothing is playing** picks what that spot shows instead.
   - Under Apps, switch off any app you never want shown.
3. For a look per song, turn on **Style what's playing** under Looks. Each song gets its own gradient and letter style. With the MCP server, Claude Code can suggest looks. They wait on this page for **Save** or **Dismiss**, unless you turn on **Let Claude style songs directly**. **Clear Looks…** forgets them all.

Two-finger click it for Previous and Next. It runs only while it's placed somewhere and the Desk is on.

## 10. Make it yours

You get the widget in your colors and font.

**Themes**

1. Open Settings, **Themes**.
2. Click a theme to use it: Obsidian, Aurora, Ember, Mint, 626 Labs, Matrix, Blueprint, or Match Desk (the widget in the Desk's ink, without the glass).
3. To add your own, paste a theme's JSON under **Your own themes**, type a **Filename** and click **Save & Apply**. **Copy Agent Prompt** copies a prompt you can give any AI chat, with a picture, to make one.

**The widget**

1. Open Settings, **Widget**.
2. **Show the widget** picks when it shows on its own: **Always shown**, **Hidden while Desk is on** or **Only when I open it**. **Show the widget now** shows or hides it until the Desk turns on or off.
3. Pick a **Font**. EsteFont Pro and EsteFont 26 come with Sanduhr and sit at the top of the list. **Use System Font** goes back.
4. Turn on **Subtle mode** for just the numbers, with no background.
5. Under **Pacing calculators**, **Pin the pacing calculators on every card** keeps them on every card until Sanduhr quits.

The Desk has its own fonts in Desk Look (task 3).

## 11. Mods

You get one page that lists what each Claude Code folder loads.

1. Open Settings, **Mods & Config**.
2. Under the heading **Mods and plugins**, the list shows every mod and plugin each folder loads, and what it can touch. The card at the top counts them: mods, plugins, enabled plugins and folders.
3. Sanduhr's own mod, sanduhr-meters, sits at the top as **Meters above the prompt**, with a switch, **Update** and **Remove** per folder (task 7). Every other mod is read-only here.
4. Click **Check** on a mod to run Claude Code's own validation. It reads the files and never runs the mod.

If it doesn't work: Check needs Claude Code's command line (`claude`) installed. Changes reach new Claude Code sessions, or run /reload-plugins.

## 12. Get help

- **What's New…** (any Sanduhr menu, or Settings, **About**) shows what each version added. **Show me** on a card opens its Settings page.
- **Take the Tour…** (same places) replays the welcome tour with your own numbers.
- **Check for Updates…** (any Sanduhr menu, or the same button in Settings, **Updates**) checks now. Updates also has the automatic update switches.
- **Shortcuts.** Settings, **General**, under Shortcuts: **⌥S opens Settings** and **⌥J joins the next meeting**, one switch each. They work in every app whenever Sanduhr runs, with or without the Desk. While one is on, ⌥S no longer types ß (or ⌥J ∆). To change one, click its keys beside the switch and press the new combination; Escape cancels. It needs ⌘, ⌃ or ⌥ (Shift alone isn't enough) and can't be the other shortcut's keys; the label follows (**⌃⌥S opens Settings**). **Reset to ⌥S** (or ⌥J) appears once changed. If macOS refuses the keys, a note under the switch says another app uses them. macOS doesn't flag every clash (system shortcuts such as ⌘Space aren't), so pick keys you know are free. After updating from 2.10 they start on only if the Desk was on, since that is the only time they worked before.
- **Where settings live.** Your choices are in two preferences files, `com.626labs.sanduhr` and `com.626labs.sanduhr.desk`. History, themes and integration files are in `~/Library/Application Support/Sanduhr/`. Desk messages are in `~/Library/Application Support/Desk/messages.txt`. Your key is in the Keychain.
- **How to reset.** In Settings, **Accounts**, select each account and click **Remove Account…**. Quit Sanduhr. Delete the folder `~/Library/Application Support/Sanduhr/`. Then, in Terminal, type:

```
defaults delete com.626labs.sanduhr
defaults delete com.626labs.sanduhr.desk
```

Open Sanduhr again and it starts fresh. Dragging Sanduhr to the Trash doesn't remove your key, so remove your accounts (or sign out) first.

## Privacy in one paragraph

Sanduhr runs only on your Mac. It has no server, collects no analytics and never calls home. The one place it connects to is claude.ai, with the key you gave it, to read your own usage. Your key stays in the Keychain. Your history stays in Sanduhr's folder and is never sent anywhere. Claude Code activity is read only when you turn it on for an account, and then only token counts, never what was written. The Claude Code integrations make no network requests, and the MCP server tells your agents only what you chose under Share with your agents. What's playing, camera and mic use, and watchers stay in memory. The full policy is in [PRIVACY.md](PRIVACY.md).
