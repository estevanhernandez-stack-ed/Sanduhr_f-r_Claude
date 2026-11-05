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

    /// For the copy tests.
    static var all: [String] {
        [title, intro, desktopTitle, desktopBody, drawersTitle, drawersBody, usageTitle, usageBody,
         claudeSource, continueTitle, notNow, cancel, menuItem, pickOne, saveFailed, desktopOffLine,
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
