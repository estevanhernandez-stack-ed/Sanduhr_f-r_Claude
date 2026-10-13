import AppKit

/// Quitting with a sheet up. AppKit refuses `terminate` while any window has a sheet attached: a
/// beep for Quit and ⌘Q, "user canceled" for a quit Apple Event (a script, or logging out). A
/// signed-out widget carries the Welcome sheet, so every Quit in Sanduhr goes through `now()`,
/// which ends the sheets first, and the quit Apple Event is routed here too (AppDelegate).
@MainActor
enum AppQuit {
    /// Ends every sheet attached to `windows`, nested ones included; how many it ended.
    @discardableResult
    static func endSheets(in windows: [NSWindow]) -> Int {
        var ended = 0
        for window in windows {
            // A sheet can carry its own sheet: end the innermost first. Bounded, in case one won't go.
            for _ in 0..<8 {
                guard var sheet = window.attachedSheet else { break }
                var parent = window
                while let inner = sheet.attachedSheet {
                    parent = sheet
                    sheet = inner
                }
                parent.endSheet(sheet)
                ended += 1
            }
        }
        return ended
    }

    static func now() {
        endSheets(in: NSApp.windows)
        NSApp.terminate(nil)
    }
}
