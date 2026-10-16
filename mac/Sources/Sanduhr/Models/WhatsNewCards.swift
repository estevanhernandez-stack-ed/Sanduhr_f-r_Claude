import Foundation

/// The release highlights What's New shows (item 57), one small typed array per release so Swift
/// 6.0 and 6.1 type-check them quickly. A later release adds its own array and joins it to
/// `table`; nothing else changes. Write them in the app's voice: short, plain, second person,
/// sentence case, periods. Related features share one card: it sits in the array of the
/// newest release it covers and lists every release it spans (`versions:`).
extension WhatsNew {
    static var table: [WhatsNewCard] {
        release2220 + release2210 + release2200 + release2190 + release2180 + release2170 + release2160 + release2150 + release2140 + release2130 + release2120 + release2110 + release2100 + release290 + release280 + release270 + release260 + release250 + release240
    }

    static let release2220: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.22.0", id: "any-agent",
            title: "A config for any coding agent",
            body: "Export Config can add a folder for Codex, Gemini CLI, Cursor and VS Code: your skills, agents and commands as skills, an AGENTS.md, and MCP servers in each one's format.",
            art: .symbol("person.2.badge.gearshape"), destination: .mods),
    ]

    static let release2210: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.21.0", id: "share-mod",
            title: "Share a mod",
            body: "Export a kept mod to one pack file. Opened on another Mac, it waits for Check and Try before it runs.",
            art: .symbol("shippingbox"), destination: .mods),
        WhatsNewCard(
            version: "2.21.0", id: "config-pack",
            title: "Carry a Claude Code config",
            body: "Export Config on By Seat packs a folder's agents, skills, plugins, MCP servers and settings, never a secret value. Open it into another folder, with Undo Import to take it back.",
            art: .symbol("square.and.arrow.up.on.square"), destination: .mods),
        WhatsNewCard(
            version: "2.21.0", id: "try-proposal",
            title: "Try a proposed mod",
            body: "A mod Claude proposed can run in one session once Check passes, before you keep it.",
            art: .symbol("play.rectangle"), destination: .mods),
    ]

    static let release2200: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.20.0", id: "new-mod",
            title: "Build a mod with Claude",
            body: "New Mod writes the prompt for your idea. Keep a mod Claude made in a session, or review one Claude proposes, before it runs anywhere.",
            art: .symbol("wand.and.stars"), destination: .mods),
        WhatsNewCard(
            version: "2.20.0", id: "mod-studio",
            title: "Mod Studio",
            body: "Preview the band above the prompt in every state with demo numbers, then pick its meters, style, label and motion per folder.",
            art: .symbol("paintpalette"), destination: .mods),
        WhatsNewCard(
            version: "2.20.0", id: "compact-catalog",
            title: "A shorter Catalog",
            body: "One line per item with a dot per folder. Search marks what matched, groups and runs fold, and By Seat opens on your mods and plugins.",
            art: .symbol("list.bullet.rectangle"), destination: .mods),
        WhatsNewCard(
            version: "2.20.0", id: "notch-keeps-settings",
            title: "The notch keeps your place",
            body: "With Settings open, a click on the notch brings it forward on the page you were on.",
            art: .symbol("macbook"), destination: .notch),
    ]

    static let release2190: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.19.0", id: "account-folder",
            title: "Each account knows its folder",
            body: "On Accounts, link an account to its Claude Code folder and mark it Work or Personal. Mods & Config names the folder after it.",
            art: .symbol("link"), destination: .credentials),
        WhatsNewCard(
            version: "2.19.0", id: "seat-statusline",
            title: "Every folder's statusline",
            body: "By Seat shows each folder's statusline, led by its account's name, with segments to pick and style and a live preview.",
            art: .symbol("terminal"), destination: .mods),
        WhatsNewCard(
            version: "2.19.0", id: "hide-hourglass",
            title: "Hide the hourglass",
            body: "Sanduhr can leave the menu bar. Open it again to get the widget, and quit from the foot of any Settings page.",
            art: .symbol("hourglass"), destination: .general),
    ]

    static let release2180: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.18.0", id: "mods-catalog",
            title: "Everything your Claude Code loads",
            body: "Mods & Config opens on a Catalog of every plugin, skill, agent, command and MCP server, grouped, with what needs a look first.",
            art: .symbol("square.grid.2x2"), destination: .mods),
        WhatsNewCard(
            version: "2.18.0", id: "session-load",
            title: "What a session starts with",
            body: "Each folder shows about how many tokens its skill listing takes, and turns orange when Claude Code would cut descriptions.",
            art: .symbol("gauge.with.dots.needle.67percent"), destination: .mods),
        WhatsNewCard(
            version: "2.18.0", id: "compare-seats",
            title: "Compare two folders",
            body: "The Compare tab shows what only one folder has, what differs and what matches, side by side.",
            art: .symbol("rectangle.split.2x1"), destination: .mods),
    ]

    static let release2170: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.17.0", id: "reset-sanduhr",
            title: "Reset Sanduhr",
            body: "About can now remove everything Sanduhr wrote, one checkbox per part. Claude Code folders go back to how they were before Sanduhr.",
            art: .symbol("arrow.counterclockwise"), destination: .about),
        WhatsNewCard(
            version: "2.17.0", id: "signed-out-clarity",
            title: "Clear when you're signed out",
            body: "Accounts walks you through a first sign-in, Usage says when it shows saved history, and empty notch wings fold away.",
            art: .symbol("person.crop.circle.badge.exclamationmark"), destination: .credentials),
        WhatsNewCard(
            version: "2.17.0", id: "mods-folders",
            title: "Your Claude Code folders",
            body: "Add Folder… on Mods & Config keeps a folder Sanduhr didn't find. Sanduhr now reads the settings file Claude Code actually uses.",
            art: .symbol("folder.badge.plus"), destination: .mods),
    ]

    static let release2160: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.16.0", id: "per-seat-meters",
            title: "Each Claude Code folder, its own meters",
            body: "Link an account to each Claude Code folder in Accounts, and sessions there show that account's meters, whichever one Sanduhr shows.",
            art: .symbol("person.2"), destination: .credentials),
    ]

    static let release2150: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.15.0", id: "just-the-desk",
            title: "Just the Desk",
            body: "No Claude account? The welcome sheet can set Sanduhr up as the Desk on its own. Sign in any time to add your meters.",
            art: .preview(.deskMessage), destination: .deskLayout),
    ]

    static let release2140: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.14.0", id: "shortcut-app-menus",
            title: "Shortcuts that check your apps",
            body: "Turn on Also check other apps' menus and General names the app and menu item a new shortcut would take keys from.",
            art: .symbol("menubar.rectangle"), destination: .general),
        WhatsNewCard(
            version: "2.14.0", id: "sign-in-reminders",
            title: "Quiet the sign-in reminder",
            body: "Using the Desk without Claude? Alerts has a switch for the sign-in line on the Desk and notch.",
            art: .symbol("person.crop.circle.badge.questionmark"), destination: .alerts),
    ]

    static let release2130: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.13.0", id: "meter-history",
            title: "Your meters over time",
            body: "Claude Usage has a Meters tab: a week or a month of each limit, for one account or all of them, and Export CSV.",
            art: .symbol("chart.xyaxis.line"), destination: .usage),
    ]

    static let release2120: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.12.0", id: "shortcut-awareness",
            title: "Shortcuts that check with macOS",
            body: "Keys macOS already uses are named before they're saved, and Test It tells you whether your keys reach Sanduhr.",
            art: .symbol("keyboard.badge.ellipsis"), destination: .general),
    ]

    static let release2110: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.11.0", id: "settings-pages",
            title: "Settings, sorted by task",
            body: "Sixteen pages in five groups, every control in one place, and every Settings… button names the page it opens. Your choices carried over.",
            art: .symbol("sidebar.left"), destination: .general),
        WhatsNewCard(
            version: "2.11.0", id: "settings-search",
            title: "Find any setting",
            body: "Search over the sidebar, or press Command-F. Pick a match and the page opens scrolled to it, lit for a moment.",
            art: .symbol("magnifyingglass"), destination: .general),
        WhatsNewCard(
            version: "2.11.0", id: "shortcut-keys",
            title: "Pick your own shortcuts",
            body: "On General, click a shortcut's keys and press new ones. Reset puts ⌥S or ⌥J back.",
            art: .symbol("keyboard"), destination: .general),
        WhatsNewCard(
            version: "2.11.0", id: "account-status",
            title: "See that you're signed in",
            body: "Accounts shows Active and Signed in in green beside each name, and keeps sign-in tools folded while an account works.",
            art: .symbol("person.crop.circle.badge.checkmark"), destination: .credentials),
    ]

    static let release2100: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.10.0", id: "arrange-desk",
            title: "Arrange the Desk on the desktop",
            body: "Choose Arrange Desk… and drag pieces to any of eight places, reorder them and resize them right on the desktop. Return keeps it, Escape puts it back.",
            art: .symbol("hand.draw"), destination: .deskLayout),
        WhatsNewCard(
            version: "2.10.0", id: "above-the-prompt",
            title: "Meters and watchers above the prompt",
            body: "In Claude Code, Sanduhr's meters can sit above the prompt with your styles and real motion, and watchers can show there as rows.",
            art: .symbol("rectangle.topthird.inset.filled"), destination: .mods),
        WhatsNewCard(
            version: "2.10.0", id: "song-looks",
            title: "A look for every song",
            body: "Now playing can wear a gradient and letter style per song, suggested by Claude and approved by you. Themes can style the widget's title too.",
            art: .preview(.nowPlaying), destination: .nowPlaying),
        WhatsNewCard(
            version: "2.10.0", id: "desk-clicks",
            title: "The clock and message answer clicks",
            body: "Two-finger click the clock or the message for Sanduhr's menu and their settings. A switch on the Desk page lets clicks through instead.",
            art: .symbol("cursorarrow.click"), destination: .deskLayout),
        WhatsNewCard(
            version: "2.10.0", id: "mod-switches",
            title: "Switch Sanduhr's mod per folder",
            body: "Meters above the prompt, at the top of Mods & Config, turns Sanduhr's meters mod on or off for each folder, and removes it cleanly.",
            art: .symbol("cube"), destination: .mods),
    ]

    static let release290: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.9.0", id: "message-editor",
            title: "Desk messages without the tags",
            body: "Add lines by day or date and style them with buttons: colors, palettes, letter styles, write-in and sweep. Birthdays add to the day instead of replacing it.",
            art: .preview(.deskMessage), destination: .message),
        WhatsNewCard(
            version: "2.9.0", id: "desk-layout",
            title: "Arrange the Desk your way",
            body: "Eight places to put each piece, the order you want in each, and a size for every piece, with a live map of the screen.",
            art: .symbol("rectangle.3.group"), destination: .deskLayout),
        WhatsNewCard(
            version: "2.9.0", id: "settings-previews",
            title: "See it before you set it",
            body: "Every Settings page that changes something you see now shows a live preview of it, drawn the way the real thing draws.",
            art: .symbol("eye.square"), destination: .notch),
        WhatsNewCard(
            version: "2.9.0", id: "estefont-pro",
            title: "EsteFont Pro",
            body: "A cleaner cut of the Desk's handwriting, now the default for new installs. EsteFont 26 stays in the list.",
            art: .preview(.font), destination: .deskLook),
        WhatsNewCard(
            version: "2.9.0", id: "mods-page",
            title: "Your Claude Code mods, in one place",
            body: "Mods & Config lists what each Claude Code folder loads and what every mod can touch. Check runs Claude Code's own validation without running the mod.",
            art: .symbol("cube"), destination: .mods),
        WhatsNewCard(
            version: "2.9.0", id: "camera-mic",
            title: "Camera and mic on the notch",
            body: "A mic glyph while any app listens, and a red dot for cameras whose own light you can't see. Indicators only: Sanduhr never mutes or records.",
            art: .symbol("video.fill"), destination: .notch),
    ]

    static let release280: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.8.0", id: "watchers",
            title: "Watchers on the notch and the Desk",
            body: "When an agent waits on a build, a release or a long task, a live card shows its progress, glows when it needs you and fades when it passes. Turn it on in Watchers.",
            art: .symbol("eye"), destination: .watchers),
        WhatsNewCard(
            version: "2.8.0", id: "combine-statusline",
            title: "Your statusline and Sanduhr's, combined",
            body: "Keep your own statusline and add Sanduhr's meters, choosing each segment you keep and styling it with colors, gradients and letter styles.",
            art: .symbol("terminal"), destination: .integrations),
    ]

    static let release270: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.7.0", id: "sign-in",
            title: "Sign in inside Sanduhr",
            body: "Sign in to Claude in Sanduhr's own window, with no DevTools and no cookies to copy. An account made with Google gets the steps to paste its key instead.",
            art: .symbol("person.badge.key"), destination: .credentials),
        WhatsNewCard(
            version: "2.7.0", id: "tour",
            title: "A tour of Sanduhr",
            body: "A short tour of the widget, the Desk, the menu bar, accounts and Claude Code, with your own numbers. Take it any time from About or the menus.",
            art: .symbol("map"), destination: .about),
        WhatsNewCard(
            versions: ["2.6.0", "2.7.0"], id: "now-playing",
            title: "Now playing on the notch and the Desk",
            body: "The song or video playing on your Mac can show on a notch wing or in a Desk corner. Click it to play or pause; when nothing plays, the spot shows your meters or the time.",
            art: .preview(.nowPlaying), destination: .nowPlaying),
    ]

    static let release260: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.6.0", id: "claude-suggests",
            title: "Claude writes your messages and themes",
            body: "With the Sanduhr MCP server installed, Claude Code can suggest Desk messages, with effects like {glow} and {write}, and widget themes with a live preview. Nothing changes until you approve it.",
            art: .preview(.deskMessage), destination: .message),
        WhatsNewCard(
            version: "2.6.0", id: "dock-aware-desk",
            title: "The Desk moves clear of the Dock",
            body: "Whatever sits in a corner on the Dock's side stays just clear of it, and rises with a Dock that hides.",
            art: .symbol("dock.rectangle"), destination: .deskLayout),
        WhatsNewCard(
            version: "2.6.0", id: "estefont",
            title: "EsteFont 26, built in",
            body: "The Desk's handwriting now ships inside Sanduhr, so it draws the same on any Mac. You can pick it for the widget too.",
            art: .preview(.font), destination: .deskLook),
    ]

    static let release250: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.5.0", id: "usage-page",
            title: "The Usage page",
            body: "See each account's Claude Code tokens by day, week and session, with its top projects. You choose what Sanduhr keeps for each account.",
            art: .symbol("chart.bar.xaxis"), destination: .usage),
        WhatsNewCard(
            version: "2.5.0", id: "integrations",
            title: "Claude Code integrations",
            body: "Install the MCP tools, the statusline, the meters above the prompt and the Claude Code glow hook, which lights the notch when Claude needs you. Each one says what it adds before it installs.",
            art: .symbol("puzzlepiece.extension"), destination: .integrations),
    ]

    static let release240: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.4.0", id: "accounts",
            title: "More than one Claude account",
            body: "Add each account with its own key and history, switch from the name chip or the Accounts menu, or let Sanduhr follow the account you're using.",
            art: .symbol("person.2"), destination: .credentials),
        WhatsNewCard(
            version: "2.4.0", id: "menu-bar",
            title: "The menu bar and limits that come and go",
            body: "Choose what the menu bar shows: session, weekly, whichever is higher, or both in turn. Hide a promo limit; it comes back when it resets or refills.",
            art: .preview(.menuBar), destination: .general),
    ]
}
