import AppKit
import SwiftUI

/// An account switch as a crossfade (item 40): the old account's meters fade out on the widget
/// and the Desk, stay in the layout unseen so nothing jumps, and the new account's fade in when
/// they arrive, never sooner than the fade out ends. A fetch that outlasts the fade shows a faint
/// "Switching account…". With Reduce Motion it is a plain swap.
enum AccountSwitchFade {
    /// Seconds the old account's meters take to fade out (ease out).
    static let fadeOut: TimeInterval = 0.25
    /// Seconds the new account's meters take to fade in (ease in).
    static let fadeIn: TimeInterval = 0.35
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
        reduceMotion ? nil : .easeOut(duration: fadeOut)
    }

    /// The fade in, or none with Reduce Motion.
    static func inAnimation(reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : .easeIn(duration: fadeIn)
    }

    /// How long the new account's numbers wait before they show, so they never land while the
    /// old ones are still fading: the rest of the fade out since `start`. Nothing with Reduce
    /// Motion or outside a switch.
    static func holdBack(since start: Date?, now: Date, reduceMotion: Bool) -> TimeInterval {
        guard let start, !reduceMotion else { return 0 }
        return max(0, fadeOut - now.timeIntervalSince(start))
    }
}
