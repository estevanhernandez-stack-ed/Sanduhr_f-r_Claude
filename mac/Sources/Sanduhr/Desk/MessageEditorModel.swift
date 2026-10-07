import Foundation
import Observation

/// Settings, Message's editor state (item 69): the file as rows (the list) or as text (Edit as
/// text…), one document either way. Switching views carries every byte across: the list writes
/// its rows to the text, the text is read back into rows. Unsaved means the text the current view
/// would write differs from the file as last read or saved. Kept outside the view so the window
/// keeps it and the debug snapshot reads it (state.yaml `message_editor`).
@MainActor
@Observable
final class MessageEditorModel {
    enum Mode: String { case list, text }

    var mode: Mode = .list
    var document = MessageDocument()
    /// Edit as text…'s buffer.
    var text = ""
    /// The styled row whose controls show.
    var expanded: UUID?
    /// messages.txt changed under unsaved edits (Claude's lines were added): offer to reload.
    var fileChanged = false
    private(set) var savedText = ""
    private(set) var loaded = false
    /// The line `message-editor add` put in (debug hooks), as written; nil otherwise.
    private(set) var debugAdded: String?

    /// nil reads and writes the real messages.txt; tests pass a temp file.
    @ObservationIgnored private let url: URL?

    init(url: URL? = nil) {
        self.url = url
    }

    private var fileURL: URL { url ?? MessageEngine.fileURL }

    /// What the current view would save.
    var currentText: String { mode == .list ? document.text : text }

    var unsaved: Bool { currentText != savedText }

    /// Reads the file (creating the starter the first time), dropping unsaved edits.
    func load() {
        if url == nil { MessageEngine.ensureFile() }
        let read = (try? String(contentsOf: fileURL, encoding: .utf8)) ?? ""
        savedText = read
        text = read
        document = MessageDocument(parsing: read)
        expanded = nil
        fileChanged = false
        debugAdded = nil
        loaded = true
    }

    func loadIfNeeded() {
        if !loaded { load() }
    }

    /// Writes what the current view shows; the list's rows now read as saved.
    @discardableResult
    func save() -> Bool {
        let out = currentText
        do {
            try out.write(to: fileURL, atomically: true, encoding: .utf8)
        } catch {
            return false
        }
        savedText = out
        if mode == .list {
            document.rebase()
            text = out
        } else {
            document = MessageDocument(parsing: out)
        }
        fileChanged = false
        return true
    }

    /// Edit as text…: the list's rows as text, unsaved edits included.
    func showText() {
        guard mode == .list else { return }
        text = document.text
        mode = .text
    }

    /// Back to the list: the text read into rows, unsaved edits included.
    func showList() {
        guard mode == .text else { return }
        document = MessageDocument(parsing: text)
        expanded = nil
        mode = .list
    }

    /// Add Line: a new row, its controls open.
    func addLine(_ line: MessageLine = MessageLine()) {
        expanded = document.add(line)
    }

    /// The acceptance line, added through the debug hooks for the smoke scenario; held unsaved.
    func debugAdd() {
        loadIfNeeded()
        if mode == .text { showList() }
        var line = MessageLine(when: .weekday("Fri"), text: "ship it.")
        line.look.ink = MessagePalette.colors("sunset")
        line.look.font = .script
        line.look.setMotion(.sweep)
        addLine(line)
        debugAdded = line.written
    }

    /// state.yaml's `message_editor`: counts and flags, never a line of the user's.
    func debugState(open: Bool) -> MessageEditorDebug {
        let rows = document.messageRows
        return MessageEditorDebug(open: open, mode: mode.rawValue, rows: rows.count,
                                  styled: rows.filter { $0.line != nil }.count,
                                  raw: rows.filter { $0.line == nil }.count,
                                  notes: document.noteCount, unsaved: loaded && unsaved, added: debugAdded)
    }
}

/// state.yaml's `message_editor:` (item 69).
struct MessageEditorDebug: Equatable {
    var open = false
    var mode = "list"
    var rows = 0
    var styled = 0
    var raw = 0
    var notes = 0
    var unsaved = false
    /// How many date lines the Desk draws above the usual line today (item 69); never their text.
    var todaySpecial = 0
    /// Only the smoke's own line (`message-editor add`), never one of the user's.
    var added: String?
}
