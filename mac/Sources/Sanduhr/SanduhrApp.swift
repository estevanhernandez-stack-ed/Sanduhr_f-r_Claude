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
        app.mainMenu = editMenu()
        app.run()
    }

    /// Never shown (accessory app), but its Edit items are what make copy, paste, undo and
    /// select all work in text fields, and Cmd+W close a settings window.
    private static func editMenu() -> NSMenu {
        let main = NSMenu()
        let appItem = NSMenuItem()
        appItem.submenu = NSMenu()
        appItem.submenu?.addItem(withTitle: "Close Window", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        appItem.submenu?.addItem(withTitle: "Quit Sanduhr für Claude", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        main.addItem(appItem)
        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)
        return main
    }
}
