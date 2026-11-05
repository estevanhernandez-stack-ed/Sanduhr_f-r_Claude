import Foundation

/// The welcome tour's cards (item 61), in small typed arrays so Swift 6.0 and 6.1 type-check
/// them quickly. Written as What's New's cards are: short, plain, second person, sentence case,
/// periods; a body is two sentences at most and 200 characters. A later feature adds its card
/// here the way it adds a What's New card.
extension WelcomeTour {
    static var table: [TourCard] { yourNumbers + aroundTheMac + beyondTheWidget }

    /// The tour for a Mac with Track my AI usage unticked in Set up Sanduhr (what Just the Desk was):
    /// three steps with no meters, accounts or Claude Code in them. The desk, menu-bar and notch cards are the table's, moved up a step
    /// (the menu-bar body is rewritten for a Mac with no numbers); a last card says the Desk
    /// stands alone and where sign-in lives.
    static var deskOnly: [TourCard] {
        let byID = Dictionary(uniqueKeysWithValues: table.map { ($0.id, $0) })
        return [
            moved(byID["desk"]!, to: 1),
            moved(byID["menu-bar"]!, to: 2,
                  body: "The hourglass in the menu bar opens Settings and the Desk's pages. Once you turn on usage it shows a percent of your limits."),
            moved(byID["notch"]!, to: 2),
            TourCard(
                id: "usage", step: 3,
                title: "Usage when you want it",
                body: "The Desk works without tracking usage. Turn on Track my AI usage in Set up Sanduhr whenever you like, and the meters fill in on the desktop and the notch.",
                art: .preview(.meters), showMe: .setup,
                showMeTitle: SetupCopy.title),
        ]
    }

    /// `card` at `step`, with `body` when one is given. Everything else is the table's.
    private static func moved(_ card: TourCard, to step: Int, body: String? = nil) -> TourCard {
        TourCard(id: card.id, step: step, title: card.title, body: body ?? card.body, art: card.art,
                 showMe: card.showMe, choices: card.choices, requires: card.requires,
                 showMeTitle: card.showMeTitle)
    }

    static let yourNumbers: [TourCard] = [
        TourCard(
            id: "paced", step: 1,
            title: "Your limits, paced",
            body: "Each bar is one of your limits, filling as you use it. The tick marks where an even pace would put you right now.",
            art: .preview(.meters), showMe: .widget),
        TourCard(
            id: "desk", step: 2,
            title: "On your desktop",
            body: "The Desk puts a clock, your meters, today's meetings and a message on the desktop, under every window. Match Desk draws the widget in the same ink.",
            art: .preview(.deskCorner), showMe: .settings(.deskLayout),
            choices: [.desk, .matchDesk]),
    ]

    static let aroundTheMac: [TourCard] = [
        TourCard(
            id: "menu-bar", step: 3,
            title: "At a glance",
            body: "The hourglass in the menu bar shows a percent: your session, your weekly limit, whichever is higher, or both in turn.",
            art: .preview(.menuBar), showMe: .settings(.general),
            choices: [.menuBar]),
        TourCard(
            id: "notch", step: 3,
            title: "Around the notch",
            body: "With Desk on, the notch can grow wings that show your meters, the next meeting or what's playing. Pick what each wing shows.",
            art: .preview(.notch), showMe: .settings(.notch),
            requires: .notch),
    ]

    static let beyondTheWidget: [TourCard] = [
        TourCard(
            id: "accounts", step: 4,
            title: "More than one account",
            body: "Add each Claude account with its own key and history, then switch from the name chip or the Accounts menu. Sanduhr can also follow the account you're using.",
            art: .preview(.accountChip), showMe: .settings(.credentials)),
        TourCard(
            id: "claude-code", step: 5,
            title: "Claude Code, connected",
            body: "The Usage page shows your Claude Code tokens by day and project. The Claude Code page adds the MCP tools, the statusline and the Claude Code glow hook, each after you say yes.",
            art: .symbol("puzzlepiece.extension"), showMe: .settings(.integrations)),
    ]
}
