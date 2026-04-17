import AppKit

// Manual NSApplication bootstrap — we don't want a SwiftUI `App`/`WindowGroup`
// because those create a standard window we'd have to fight. AppDelegate owns
// the NSPanel setup instead.
@main
enum Sanduhr {
    static func main() {
        let app = NSApplication.shared
        // Populate ThemeRegistry with user-dropped JSON themes before
        // anything reads it (UsageViewModel.init resolves the saved theme
        // id on construction).
        UserThemes.load()
        let delegate = AppDelegate()
        app.delegate = delegate
        app.run()
    }
}
