import Foundation

extension Notification.Name {
    /// Posted when the stored setup changes (the setup service, item 4): the Desk reloads its pieces.
    static let sanduhrSetupDidChange = Notification.Name("sanduhrSetupDidChange")
}

/// "A nicer desktop" off (setup spec, section 10): the Desk draws none of its pieces. Drawer panels
/// are their own windows and the notch is not a layout piece, so neither is touched. The saved
/// layout string is never rewritten, so ticking desktop again puts every piece back where it was.
enum SetupDesk {
    /// Whether the Desk draws its pieces: the stored setup's desktop flag, absent setup reading as on.
    static func desktopOn(from store: DefaultsStore = UserDefaults.standard) -> Bool {
        SetupStore.load(from: store)?.desktop ?? true
    }

    /// Whether one layout piece draws. Every Desk piece is a layout piece on the Mac.
    static func shows(_ widget: String, desktop: Bool) -> Bool { desktop }

    /// The pieces the Desk draws for a layout: all of them with desktop on, none with it off.
    static func visible(_ arrangement: DeskArrangement, desktop: Bool) -> DeskArrangement {
        guard !desktop else { return arrangement }
        var shown = arrangement
        shown.pieces = arrangement.pieces.filter { shows($0.widget, desktop: desktop) }
        return shown
    }

    /// The layout words the Desk draws: the saved words as they are with desktop on, no words with it off.
    static func visibleWords(_ words: String, desktop: Bool) -> String {
        desktop ? words : visible(DeskArrangement(words), desktop: false).string
    }

    /// Desk Layout's Arrange line while desktop is off, else nil.
    static func arrangeNote(desktop: Bool) -> String? { desktop ? nil : SetupCopy.arrangeNeedsDesktop }

    /// A Desk design review's line while desktop is off, else nil. Apply still saves the design.
    static func designNote(desktop: Bool) -> String? { desktop ? nil : SetupCopy.designDesktopOffNote }
}
