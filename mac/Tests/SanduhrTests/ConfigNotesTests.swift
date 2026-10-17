import Foundation
import Testing
@testable import Sanduhr

/// Notes that clear (polish 2026-10-17, #7, #8, #9): a folder's note shows under that folder
/// only, the general slot holds a note with no folder, both go when the picker moves or the
/// window closes, and From Claude's note says whether it failed. Nothing here reads the real
/// ~/.claude* or UserDefaults.
@Suite("Mods & Config notes")
@MainActor
struct ConfigNotesTests {
    @Test func aNoteSitsUnderItsFolderOnly() {
        var notes = FolderNotes()
        notes.setNote("Exported 3 items.", for: "/tmp/seats/a/")
        // Keyed by the normalized path: the trailing slash doesn't make a second folder.
        #expect(notes.note(for: "/tmp/seats/a") == "Exported 3 items.")
        #expect(notes.note(for: "/tmp/seats/b") == nil)
        #expect(notes.note(for: nil) == nil)
        notes.setNote("Sanduhr can't open x.", for: nil)
        #expect(notes.note(for: nil) == "Sanduhr can't open x.")
        #expect(notes.note(for: "/tmp/seats/a") == "Exported 3 items.")
        notes.setNote(nil, for: "/tmp/seats/a")
        #expect(notes.note(for: "/tmp/seats/a") == nil && notes.note(for: nil) != nil)
        notes.clearNotes()
        #expect(notes.note(for: nil) == nil)
    }

    @Test func thePickerAndTheWindowClearThem() {
        let model = ModsPageModel(moreStore: MemoryDefaults())
        model.selectedSeat = "/tmp/seats/a"
        model.exportNotes.setNote("Exported.", for: "/tmp/seats/a")
        model.importNotes.setNote("Undone.", for: nil)
        // The same seat again keeps them.
        model.selectedSeat = "/tmp/seats/a"
        #expect(model.exportNotes.note(for: "/tmp/seats/a") == "Exported." && model.importNotes.note(for: nil) == "Undone.")
        model.selectedSeat = "/tmp/seats/b"
        #expect(model.exportNotes.note(for: "/tmp/seats/a") == nil && model.importNotes.note(for: nil) == nil)
        model.importNotes.setNote("Undone.", for: "/tmp/seats/b")
        model.windowClosed()
        #expect(model.importNotes.note(for: "/tmp/seats/b") == nil)
        // No seat is listed before the folders are read: an import's note goes to the general slot.
        #expect(model.importNoteFolder("/tmp/seats/b") == nil)
    }

    @Test func keptNoteSaysWhetherItFailed() async {
        #expect(!KeptNote.done("Exported watch to watch.sanduhrpack.").isFailure)
        #expect(KeptNote.failed("Sanduhr can't open x.").isFailure)
        // Forget refuses a mod that is on somewhere: a failure, said before any store is touched.
        let model = ModsPageModel(moreStore: MemoryDefaults())
        let mod = KeptMod(name: "watch", stamp: "abcdef123456", keptAt: Date(), session: "s", files: 1, bytes: 1)
        model.keptStates[mod.id] = ["/tmp/seats/a": .on]
        await model.forget(mod)
        #expect(model.keptNote?.isFailure == true)
        #expect(model.keptNote?.text.contains("Turn it off there first") == true)
    }
}
