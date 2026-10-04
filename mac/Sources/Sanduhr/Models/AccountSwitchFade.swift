import AppKit
import SwiftUI

/// An account switch as a crossfade (items 40 and 41): the old account's meters fade out on the
/// widget and the Desk, gently, stay in the layout unseen so nothing jumps, and the new account's
/// fade in when they arrive, never sooner than the fade out ends. The account name on the widget
/// chip and the Desk line stays the old one until the fade out has finished (NameHold), so old
/// numbers never sit under the new name. A fetch that outlasts the fade shows a faint
/// "Switching account…". With Reduce Motion it is a plain swap.
enum AccountSwitchFade {
    /// Seconds the old account's meters take to fade out (ease in and out).
    static let fadeOut: TimeInterval = 0.6
    /// Seconds the new account's meters take to fade in (ease out).
    static let fadeIn: TimeInterval = 0.45
    /// Seconds the account name takes to cross over to the new one, once the fade out is done.
    static let nameFade: TimeInterval = 0.25
    /// The switching note comes up once the fade out is over and the new numbers have not.
    static let noteDelay: TimeInterval = fadeOut
    /// The note is faint: this much of the text's own opacity.
    static let noteOpacity = 0.6
    /// The widget's note.
    static let note = "Switching account…"
    /// The Desk's note, in the Desk's lower case.
    static let deskNote = "switching account…"

    /// Settings, Accessibility, Display, Reduce motion.
    @MainActor static var reduceMotion: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }

    /// The fade out, or none with Reduce Motion.
    static func outAnimation(reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : .easeInOut(duration: fadeOut)
    }

    /// The fade in, or none with Reduce Motion.
    static func inAnimation(reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : .easeOut(duration: fadeIn)
    }

    /// The name's crossfade, or none with Reduce Motion.
    static func nameAnimation(reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : .easeInOut(duration: nameFade)
    }

    /// How long the new account's numbers wait before they show, so they never land while the
    /// old ones are still fading: the rest of the fade out since `start`. Nothing with Reduce
    /// Motion or outside a switch.
    static func holdBack(since start: Date?, now: Date, reduceMotion: Bool) -> TimeInterval {
        guard let start, !reduceMotion else { return 0 }
        return max(0, fadeOut - now.timeIntervalSince(start))
    }

    /// The account name the widget chip and the Desk line show through a switch: the departing
    /// account's while its numbers fade out, then the active one's.
    struct NameHold: Equatable {
        /// The departing account's name as the chip showed it (nil with fewer than two accounts).
        private(set) var departing: String?
        private(set) var holding = false

        /// The name to show, given the active account's.
        func shown(active: String?) -> String? { holding ? departing : active }

        /// A switch starts: keep `leaving` up. Nothing with Reduce Motion (a plain swap), and a
        /// second switch during the hold keeps the first one's name.
        mutating func begin(leaving: String?, reduceMotion: Bool) {
            guard !reduceMotion, !holding else { return }
            departing = leaving
            holding = true
        }

        /// The fade out has finished (or the switch ended): the active name shows.
        mutating func release() {
            departing = nil
            holding = false
        }
    }
}
