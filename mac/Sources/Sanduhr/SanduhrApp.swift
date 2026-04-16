import AppKit

// Manual NSApplication bootstrap — we don't want a SwiftUI `App`/`WindowGroup`
// because those create a standard window we'd have to fight. AppDelegate owns
// the NSPanel setup instead.
@main
enum Sanduhr {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.run()
    }
}
