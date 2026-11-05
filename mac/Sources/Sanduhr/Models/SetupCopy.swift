import Foundation

/// Every string the setup screen shows (setup spec, sections 10 and 11). No em dash anywhere.
enum SetupCopy {
    static let title = "Set up Sanduhr"
    static let intro = "Pick what you want. You can change this any time from the menu bar or Settings, General."
    static let desktopTitle = "A nicer desktop"
    static let desktopBody = "A clock, a message and the weather on your desktop."
    static let drawersTitle = "Tidy my files"
    static let drawersBody = "Sorts screenshots, pictures, documents, installers, zip and other archive files, shortcuts and files you haven't changed in a month into drawers. Nothing moves until you say so."
    static let usageTitle = "Track my AI usage"
    static let usageBody = "See how much of your plan you've used."
    static let claudeSource = "Claude"
    static let continueTitle = "Continue"
    static let notNow = "Not now"
    static let cancel = "Cancel"
    static let done = "Done"
    static let designButton = "Design it with an AI chat"
    static let designCaption = "Copy a prompt into any AI chat, then paste its answer back here."
    static let menuItem = "Set up Sanduhr…"
    static let pickOne = "Pick at least one, or Sanduhr has nothing to show."
    static let saveFailed = "Your choices could not be saved."
    static let desktopOffLine = "The clock, message and weather hide. Your layout is kept."
    static let usageOffLine = "Your account stays saved. Remove it in Settings, Accounts."
    static let addOnsStayLine = "The Claude Code add-ons stay installed. Remove them on the Claude Code page."
    static let arrangeNeedsDesktop = "Turn on A nicer desktop in Set up Sanduhr to arrange the Desk."
    static let designDesktopOffNote = "A nicer desktop is off, so Apply saves this for when you turn it on."
    static let drawersFirstOnLine = "Drawer files live in the Drawers folder on your Desktop."
    static let nothingToSort = "Nothing on the Desktop matches a rule."
    static let whatChanges = "What changes"
    static let whatHappened = "What happened"
    /// Usage, Alerts and Claude Code pages while usage tracking is off, in place of the sign-in line.
    static let usageOffSettingsLine = "Usage tracking is off. Turn it on in Set up Sanduhr."

    static func drawersOffLine(_ files: Int) -> String {
        files == 1 ? "1 drawer file goes back to your Desktop." : "\(files) drawer files go back to your Desktop."
    }

    // Design it with an AI chat (spec section 12): the window's copy. It says "any AI chat" and names no product.
    static let chatIntro = "Copy a prompt into any AI chat. It asks what you like, then gives you a block to paste below."
    static let chatCopyPrompt = "Copy prompt"
    static let chatPasteName = "The AI chat's answer"
    static let chatPastePlaceholder = "{\"desk\": {...}, \"drawers\": {\"rules\": [...]}}"
    static let chatPreview = "Preview on my Desk"
    static let chatUseRules = "Use these drawer rules"
    static let chatUseDrawerSettings = "Use these drawer settings"
    static let chatClose = "Close"
    static let chatFindingsName = "What to fix in the paste"
    static let chatReady = "Ready."
    static let chatPromptCopied = "Prompt copied. Paste it into any AI chat, answer its questions, then paste its answer here."
    static let chatDeskShown = "Showing it on your Desk. Apply or Dismiss it there."
    static let chatRulesShown = "Showing what the rules would sort on your Desktop. Nothing moves until you Apply."
    static let chatNoDeskService = "The Desk is not running, so there is nothing to preview on."
    static let chatRulesSavedNothing = "The rules are saved, but nothing on your Desktop matches them yet."
    static let chatRulesReviewPending = "A drawers review is waiting. Apply or cancel it first, then use these rules."
    static let chatUntitledDesign = "Your AI chat's design"

    static func chatFixCount(_ n: Int) -> String { n == 1 ? "1 thing to fix before this can be used." : "\(n) things to fix before this can be used." }
    static func chatManyBlocks(_ n: Int) -> String { "The paste held \(n) blocks. The first good one is used." }
    static func chatNotShown(_ why: String) -> String { "Not shown: \(why)." }

    /// What Use these drawer rules will do, said before it does it.
    static func chatRulesLine(rules: Int, drawerNames: [String]) -> String {
        let head = rules == 1 ? "1 drawer rule" : "\(rules) drawer rules"
        let into = drawerNames.isEmpty ? "" : " into " + drawerNames.joined(separator: ", ")
        let them = rules == 1 ? "it" : "them"
        return "\(head)\(into). Using \(them) replaces your drawer rules, turns drawers on if they are off, then shows what \(them) would sort. Nothing moves until you Apply."
    }

    /// One line saying what a pasted drawers look would do. `corner` and `flow` are spoken words, nil where the paste left the part to the user's own Tidy.
    static func chatDrawerSettingsLine(look: String?, opacity: Int?, corner: String?, flow: String?) -> String {
        var parts: [String] = []
        if let look { parts.append(opacity.map { "Look: \(look), \($0) percent." } ?? "Look: \(look).") }
        else if let opacity { parts.append("Opacity: \(opacity) percent.") }
        if corner != nil || flow != nil {
            let from = corner.map { "the \($0)" } ?? "your usual corner"
            let how = flow.map { ", in a \($0)" } ?? ""
            parts.append("Lines drawers up from \(from)\(how).")
        }
        parts.append("Using it changes how your drawers look and sit right away.")
        return parts.joined(separator: " ")
    }

    /// The chat window's strings, for the copy tests.
    static var chatAll: [String] {
        [chatIntro, chatCopyPrompt, chatPasteName, chatPastePlaceholder, chatPreview, chatUseRules, chatUseDrawerSettings, chatClose,
         chatFindingsName, chatReady, chatPromptCopied, chatDeskShown, chatRulesShown, chatNoDeskService, chatRulesSavedNothing,
         chatRulesReviewPending, chatUntitledDesign, chatFixCount(1), chatFixCount(2), chatManyBlocks(2), chatNotShown("the Desk is busy"),
         chatRulesLine(rules: 1, drawerNames: ["Photos"]), chatRulesLine(rules: 2, drawerNames: ["Photos", "Receipts"]),
         chatDrawerSettingsLine(look: "glass", opacity: 70, corner: "bottom left", flow: "row"),
         chatDrawerSettingsLine(look: nil, opacity: 40, corner: nil, flow: "column")]
    }

    /// For the copy tests.
    static var all: [String] {
        [title, intro, desktopTitle, desktopBody, drawersTitle, drawersBody, usageTitle, usageBody,
         claudeSource, continueTitle, notNow, cancel, done, designButton, designCaption, menuItem, pickOne, saveFailed, desktopOffLine,
         usageOffLine, addOnsStayLine, whatChanges, whatHappened, usageOffSettingsLine,
         arrangeNeedsDesktop, designDesktopOffNote, drawersFirstOnLine, nothingToSort, drawersOffLine(1), drawersOffLine(2)]
    }
}

/// What a change says before Continue applies it: one line per tick taken away.
enum SetupChange {
    /// `hasAccount`: "Your account stays saved" is said only when there is one to stay.
    static func lines(before: SetupChoices, after: SetupChoices, drawerFiles: Int,
                      addonsInstalled: Bool, hasAccount: Bool) -> [String] {
        var lines: [String] = []
        if before.desktop && !after.desktop { lines.append(SetupCopy.desktopOffLine) }
        if before.drawers && !after.drawers && drawerFiles > 0 { lines.append(SetupCopy.drawersOffLine(drawerFiles)) }
        if before.claudeUsage && !after.claudeUsage {
            if hasAccount { lines.append(SetupCopy.usageOffLine) }
            if addonsInstalled { lines.append(SetupCopy.addOnsStayLine) }
        }
        return lines
    }

    /// Why Continue is disabled, or nil when it is not.
    static func blocked(_ after: SetupChoices) -> String? {
        after.anyTicked ? nil : SetupCopy.pickOne
    }
}
