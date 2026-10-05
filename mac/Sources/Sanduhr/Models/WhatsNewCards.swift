import Foundation

/// The release highlights What's New shows (item 57), one small typed array per release so Swift
/// 6.0 and 6.1 type-check them quickly. A later release adds its own array and joins it to
/// `table`; nothing else changes. Write them in the app's voice: short, plain, second person,
/// sentence case, periods.
extension WhatsNew {
    static var table: [WhatsNewCard] { release260 + release250 + release240 }

    static let release260: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.6.0", id: "now-playing",
            title: "Now playing on the notch and the Desk",
            body: "The song or video playing on your Mac can show on a notch wing or in a Desk corner. Click it to play or pause.",
            art: .preview(.nowPlaying), destination: .nowPlaying),
        WhatsNewCard(
            version: "2.6.0", id: "desk-messages",
            title: "Claude writes your Desk messages",
            body: "With the Sanduhr MCP server installed, Claude Code can suggest lines for your Desk, and you approve them. Tags like {glow} and {write} add effects to a line.",
            art: .preview(.deskMessage), destination: .message),
        WhatsNewCard(
            version: "2.6.0", id: "claude-themes",
            title: "Claude designs themes",
            body: "Ask Claude Code for a widget theme and it waits in Settings, Themes with a live preview. Save it, apply it or dismiss it.",
            art: .preview(.theme), destination: .themes),
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
            title: "The Claude Usage page",
            body: "See each account's Claude Code tokens by day, week and session, with its top projects. You choose what Sanduhr keeps for each account.",
            art: .symbol("chart.bar.xaxis"), destination: .usage),
        WhatsNewCard(
            version: "2.5.0", id: "integrations",
            title: "Claude Code integrations",
            body: "Install the MCP tools, the statusline and the meters above the prompt from one page. Each one says what it adds before it installs.",
            art: .symbol("puzzlepiece.extension"), destination: .integrations),
        WhatsNewCard(
            version: "2.5.0", id: "notch-glow",
            title: "The notch glows when Claude needs you",
            body: "Claude Code can light up the notch when it waits on you or finishes a turn. Install the hooks in Integrations, then turn the glow on in Settings, Notch.",
            art: .symbol("sparkles"), destination: .notch),
    ]

    static let release240: [WhatsNewCard] = [
        WhatsNewCard(
            version: "2.4.0", id: "accounts",
            title: "More than one Claude account",
            body: "Add each account with its own key and history. Switch from the widget's name chip or the Accounts menu.",
            art: .symbol("person.2"), destination: .credentials),
        WhatsNewCard(
            version: "2.4.0", id: "follow",
            title: "Follow the account you're using",
            body: "Turn it on and Sanduhr switches to the account whose session is climbing while the other sits idle.",
            art: .symbol("arrow.triangle.swap"), destination: .credentials),
        WhatsNewCard(
            version: "2.4.0", id: "menu-bar",
            title: "Choose what the menu bar shows",
            body: "Session, weekly, whichever is higher, or both in turn. Right-click the hourglass to change it.",
            art: .preview(.menuBar), destination: .general),
        WhatsNewCard(
            version: "2.4.0", id: "hide-limits",
            title: "Hide the limits that come and go",
            body: "Switch off a promo or short-lived limit. It comes back on its own when it resets or refills.",
            art: .symbol("eye.slash"), destination: .deskMeters),
    ]
}
