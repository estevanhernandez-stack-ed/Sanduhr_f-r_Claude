import Foundation
import Testing
@testable import Sanduhr

/// Settings, Message's line editor (item 69): messages.txt read into rows and written back byte
/// for byte, each control's tag, the When menu's prefixes, and the row edits. Made-up lines and
/// temp files only; never the real messages.txt.

/// A hand-written file: notes, blank lines, prefixes, every effect, unknown tags, odd spacing.
private let handWritten = """
# my lines
keep building.

  Mon:   one thing at a time.
Fri:{ink:#FF2A6D,#05d9e8}  {glow}   showtime.
10-31: {ink:#ff7518,#6b2fa0} {write} boo.
{noglow} {size:1.30} {font:Small Caps} quiet.
{shimmer} {size:0.5} small shimmer.
{sweep:20} {font:fraktur} every twenty.
{sweep} once.
{blink} unknown tag stays.
{write} {shimmer} two motions stay.
13-01: not a date.
Mon:
{glow} {write}
Note: a colon in a plain line.
	tabbed line.
   # indented note
{ink:fff} trailing spaces.
"""

@Suite("Message line model: reading and writing")
struct MessageLineModelRoundTripTests {
    @Test func defaultFileRoundTrips() {
        let text = MessageEngine.starter + "\n"
        let doc = MessageDocument(parsing: text)
        #expect(doc.text == text)
        #expect(doc.noteCount == 4)
        #expect(doc.messageRows.count == 3)
        #expect(doc.messageRows.allSatisfy { $0.line != nil })
        #expect(doc.messageRows.map { $0.line?.when } == [.everyDay, .weekday("Mon"), .weekday("Fri")])
    }

    @Test func handWrittenFileRoundTrips() {
        for text in [handWritten, handWritten + "\n", handWritten + "\n\n"] {
            #expect(MessageDocument(parsing: text).text == text)
        }
    }

    @Test func crlfEmptyAndOneLineRoundTrip() {
        for text in ["", "\n", "a", "a\r\nb\r\n", "# x\r\n\r\nMon: hi\r\n", "x\n\n\n", "\u{feff}hi\n", "ünï ✨\n"] {
            #expect(MessageDocument(parsing: text).text == text, "\(text.debugDescription)")
        }
    }

    @Test func handWrittenRowsAreWhatTheDeskReads() {
        let rows = MessageDocument(parsing: handWritten).rows
        func kind(_ r: MessageRow) -> String {
            switch r.content {
            case .line: "line"
            case .raw: "raw"
            case .comment: "comment"
            case .blank: "blank"
            }
        }
        #expect(rows.map(kind) == ["comment", "line", "blank", "line", "line", "line", "line", "line", "line", "line",
                                   "raw", "raw", "raw", "raw", "raw", "line", "line", "comment", "line"])
        let lines = rows.compactMap(\.line)
        #expect(lines[1] == MessageLine(when: .weekday("Mon"), text: "one thing at a time."))
        #expect(lines[2].when == .weekday("Fri"))
        #expect(lines[2].look.ink == ["ff2a6d", "05d9e8"])
        #expect(lines[2].look.glow == true)
        #expect(lines[3].when == .date(month: 10, day: 31))
        #expect(lines[3].look.motion == .write)
        #expect(lines[4].look == MessageLook(glow: false, size: 1.3, font: .smallCaps))
        #expect(lines[5].look.motion == .shimmer)
        #expect(lines[5].look.size == 0.5)
        #expect(lines[6].look.motion == .sweep)
        #expect(lines[6].look.sweepPeriod == 20)
        #expect(lines[6].look.font == .fraktur)
        #expect(lines[7].look.sweepPeriod == nil)
        #expect(lines[8].text == "Note: a colon in a plain line.")
        #expect(lines[8].when == .everyDay)
        #expect(lines[9].text == "tabbed line.")
        #expect(lines[10].text == "trailing spaces.")
    }

    @Test func linesTheEditorCannotSetStayRaw() {
        for line in ["{blink} hi", "{write} {shimmer} two", "{sweep} {write} two", "13-01: x", "02-30: x", "Mon:   ",
                     "{glow} {write}", "{ink:#zz} x", "{size:3} big", "{glow hi", "{font:gothic} x"] {
            guard case .raw(let kept) = MessageLineModel.content(of: line) else {
                Issue.record("\(line) should stay raw"); continue
            }
            #expect(kept == line)
        }
    }

    @Test func unchangedRowsWriteTheirSourceEvenAfterAnEditIsUndone() {
        var doc = MessageDocument(parsing: handWritten)
        let id = doc.rows[4].id
        let before = doc.rows[4].line!
        doc.update(id) { $0.look.glow = nil }
        #expect(doc.text != handWritten)
        doc.update(id) { $0 = before }
        #expect(doc.text == handWritten)
    }

    @Test func aChangedRowRewritesOnlyItsLine() {
        var doc = MessageDocument(parsing: handWritten)
        let id = doc.rows[4].id
        doc.update(id) { $0.text = "curtain up." }
        let old = handWritten.components(separatedBy: "\n")
        let new = doc.text.components(separatedBy: "\n")
        #expect(old.count == new.count)
        for i in old.indices where i != 4 { #expect(old[i] == new[i]) }
        #expect(new[4] == "Fri: {ink:#ff2a6d,#05d9e8} {glow} curtain up.")
    }

    @Test func aChangedCRLFRowKeepsItsEnding() {
        var doc = MessageDocument(parsing: "# n\r\nhi\r\n")
        doc.update(doc.rows[1].id) { $0.look.glow = true }
        #expect(doc.text == "# n\r\n{glow} hi\r\n")
    }
}

@Suite("Message line model: controls")
struct MessageLineControlTests {
    private func written(_ change: (inout MessageLine) -> Void) -> String? {
        var line = MessageLine(text: "hi")
        change(&line)
        return line.written
    }

    @Test func theAcceptanceLine() {
        var line = MessageLine(when: .weekday("Fri"), text: "ship it.")
        line.look.ink = MessagePalette.colors("sunset")
        line.look.font = .script
        line.look.setMotion(.sweep)
        #expect(line.written == "Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.")
        #expect(MessageLineModel.content(of: line.written!) == .line(line))
    }

    @Test func whenPrefixes() {
        #expect(MessageWhen.everyDay.prefix == "")
        #expect(MessageWhen.weekdays.map { MessageWhen.weekday($0).prefix }
                == ["Mon: ", "Tue: ", "Wed: ", "Thu: ", "Fri: ", "Sat: ", "Sun: "])
        #expect(MessageWhen.date(month: 10, day: 31).prefix == "10-31: ")
        #expect(MessageWhen.date(month: 2, day: 5).prefix == "02-05: ")
        #expect(MessageWhen.date(month: 2, day: 29).prefix == "02-29: ")
        // Every weekday the menu writes is one the Desk reads.
        #expect(Set(MessageWhen.weekdays) == Set(MessageEngine.weekdays))
        for when in [MessageWhen.everyDay, .weekday("Sun"), .date(month: 12, day: 25), .date(month: 1, day: 1)] {
            let line = MessageLine(when: when, text: "x")
            #expect(MessageLineModel.content(of: line.written!) == .line(line))
        }
        #expect(MessageWhen.weekday("Fri").label == "Fridays")
        #expect(MessageWhen.date(month: 10, day: 31).label == "October 31")
        #expect(MessageWhen.days(inMonth: 2) == 29)
        #expect(MessageWhen.days(inMonth: 4) == 30)
    }

    @Test func color() {
        #expect(written { _ in } == "hi")
        #expect(written { $0.look.setInkMode(.solid) } == "{ink:#ff2a6d} hi")
        #expect(written { $0.look.setInkMode(.gradient) } == "{ink:#ff2a6d,#05d9e8} hi")
        #expect(written { $0.look.setInkMode(.gradient); $0.look.addStop() } == "{ink:#ff2a6d,#05d9e8,#05d9e8} hi")
        #expect(written {
            $0.look.ink = ["111111", "222222", "333333", "444444"]
            $0.look.addStop()
        } == "{ink:#111111,#222222,#333333,#444444} hi")
        #expect(written {
            $0.look.ink = ["111111", "222222", "333333"]
            $0.look.removeStop(at: 1)
            $0.look.removeStop(at: 0)
        } == "{ink:#111111,#333333} hi")
        #expect(written { $0.look.setInkMode(.solid); $0.look.setStop(0, hex: "#ABCDEF") } == "{ink:#abcdef} hi")
        #expect(written { $0.look.setInkMode(.solid); $0.look.setStop(0, hex: "nope") } == "{ink:#ff2a6d} hi")
        #expect(written { $0.look.setInkMode(.gradient); $0.look.setInkMode(.desk) } == "hi")
    }

    @Test func palettes() {
        #expect(MessagePalette.all.map(\.name) == ["synthwave", "sunset", "ocean", "aurora", "ember", "bubblegum", "toxic", "gold"])
        #expect(written { $0.look.ink = MessagePalette.colors("synthwave") } == "{ink:#ff2a6d,#d16ba5,#05d9e8} hi")
        #expect(written { $0.look.ink = MessagePalette.colors("gold") } == "{ink:#f7971e,#ffd200,#fff6b7} hi")
        #expect(MessagePalette.name(of: ["00F5A0", "00d9f5", "a06bff"]) == "aurora")
        #expect(MessagePalette.name(of: ["000000"]) == nil)
        for p in MessagePalette.all {
            #expect(p.colors.count == 3 && p.colors.allSatisfy(MessageMarkup.isHex))
        }
    }

    @Test func glowSizeLettersMotion() {
        #expect(written { $0.look.glow = true } == "{glow} hi")
        #expect(written { $0.look.glow = false } == "{noglow} hi")
        #expect(written { $0.look.size = 1.25 } == "{size:1.25} hi")
        #expect(written { $0.look.size = 0.5 } == "{size:0.5} hi")
        #expect(written { $0.look.size = 2 } == "{size:2} hi")
        #expect(written { $0.look.size = 1 } == "hi")
        for style in LetterStyle.allCases {
            #expect(written { $0.look.font = style } == "{font:\(style.rawValue)} hi")
        }
        #expect(Set(MessageLetters.order) == Set(LetterStyle.allCases))
        #expect(written { $0.look.setMotion(.write) } == "{write} hi")
        #expect(written { $0.look.setMotion(.shimmer) } == "{shimmer} hi")
        #expect(written { $0.look.setMotion(.sweep) } == "{sweep} hi")
        #expect(MessageMotionKind.allCases.map(\.title) == ["None", "Write in", "Shimmer", "Sweep"])
        // A {sweep:20} keeps its period while Motion stays Sweep, and drops it on a change.
        var line = MessageLine(text: "hi")
        line.look.motion = .sweep
        line.look.sweepPeriod = 20
        #expect(line.written == "{sweep:20} hi")
        line.look.setMotion(.sweep)
        #expect(line.written == "{sweep:20} hi")
        line.look.setMotion(.none)
        line.look.setMotion(.sweep)
        #expect(line.written == "{sweep} hi")
    }

    @Test func everyLookReadsBack() {
        var line = MessageLine(when: .date(month: 3, day: 14), text: "pi day.")
        line.look = MessageLook(ink: ["abc", "123456"], glow: false, size: 1.75, font: .doubleStruck, motion: .write)
        #expect(line.written == "03-14: {ink:#abc,#123456} {noglow} {size:1.75} {font:double-struck} {write} pi day.")
        #expect(MessageLineModel.content(of: line.written!) == .line(line))
        // The Desk reads the tags as the editor meant them.
        let parsed = MessageMarkup.parse(line.body)
        #expect(parsed.text == "pi day.")
        #expect(parsed.effects.size == 1.75)
        #expect(parsed.effects.font == .doubleStruck)
        #expect(parsed.effects.write)
    }

    @Test func textStaysOnOneLine() {
        #expect(MessageLine(text: "two\nlines").written == "two lines")
        #expect(MessageLine(text: "  padded  ").written == "padded")
        #expect(MessageLine(text: "   ").written == nil)
    }

    @Test func problemsAreNamed() {
        #expect(MessageLine(text: "fine.").problem == nil)
        #expect(MessageLine(text: "#1 dad").problem?.contains("note") == true)
        var tagged = MessageLine(text: "#1 dad")
        tagged.look.glow = true
        #expect(tagged.problem == nil)
        #expect(MessageLine(text: "{glow} hi").problem?.contains("{") == true)
        #expect(MessageLine(text: "Mon: hi").problem?.contains("day") == true)
        #expect(MessageLine(when: .weekday("Tue"), text: "Mon: hi").problem == nil)
        #expect(MessageLine(text: "").problem == nil)
    }
}

@Suite("Message line model: row edits")
struct MessageRowEditTests {
    /// The written lines other than `skip`, in order.
    private func others(_ text: String, skipping skip: Set<String>) -> [String] {
        text.components(separatedBy: "\n").filter { !skip.contains($0) }
    }

    @Test func addAppendsAndAnEmptyLineIsLeftOut() {
        var doc = MessageDocument(parsing: handWritten)
        doc.add()
        #expect(doc.text == handWritten)
        var empty = MessageDocument(parsing: "")
        empty.add(MessageLine(text: "first."))
        #expect(empty.text == "first.\n")
        var noNewline = MessageDocument(parsing: "a")
        noNewline.add(MessageLine(text: "b"))
        #expect(noNewline.text == "a\nb")
    }

    @Test func duplicateCopiesTheLineAsWritten() {
        var doc = MessageDocument(parsing: handWritten)
        let source = doc.rows[4]
        let copy = doc.duplicate(source.id)
        #expect(copy != nil && copy != source.id)
        let lines = doc.text.components(separatedBy: "\n")
        let old = handWritten.components(separatedBy: "\n")
        #expect(lines.count == old.count + 1)
        #expect(lines[4] == old[4] && lines[5] == old[4])
        #expect(Array(lines[6...]) == Array(old[5...]))
        #expect(Array(lines[..<4]) == Array(old[..<4]))
    }

    @Test func deleteRemovesOnlyThatLine() {
        var doc = MessageDocument(parsing: handWritten)
        let target = doc.rows[10]
        doc.delete(target.id)
        var old = handWritten.components(separatedBy: "\n")
        old.remove(at: 10)
        #expect(doc.text == old.joined(separator: "\n"))
    }

    @Test func dragMovesOneLineAndKeepsTheRest() {
        var doc = MessageDocument(parsing: handWritten)
        let old = handWritten.components(separatedBy: "\n")
        // Down: after the target.
        doc.move(doc.rows[1].id, onto: doc.rows[5].id)
        var want = old
        let moved = want.remove(at: 1)
        want.insert(moved, at: 5)
        #expect(doc.text == want.joined(separator: "\n"))
        // Up: before the target.
        var up = MessageDocument(parsing: handWritten)
        up.move(up.rows[9].id, onto: up.rows[3].id)
        var wantUp = old
        let m = wantUp.remove(at: 9)
        wantUp.insert(m, at: 3)
        #expect(up.text == wantUp.joined(separator: "\n"))
        // Onto itself: nothing.
        var same = MessageDocument(parsing: handWritten)
        same.move(same.rows[4].id, onto: same.rows[4].id)
        #expect(same.text == handWritten)
    }

    @Test func moveUpAndDownStepPastMessageRowsOnly() {
        var doc = MessageDocument(parsing: "# top\nA\n\nB\n# mid\nC\n")
        let b = doc.rows[3].id
        doc.move(b, by: -1)
        #expect(doc.text == "# top\nB\nA\n\n# mid\nC\n")
        doc.move(b, by: -1)
        #expect(doc.text == "# top\nB\nA\n\n# mid\nC\n")
        let a = doc.rows[2].id
        doc.move(a, by: 1)
        #expect(doc.text == "# top\nB\n\n# mid\nC\nA\n")
        doc.move(a, by: 1)
        #expect(doc.text == "# top\nB\n\n# mid\nC\nA\n")
    }

    @Test func rebaseMakesWrittenLinesTheSource() {
        var doc = MessageDocument(parsing: "a\n")
        doc.update(doc.rows[0].id) { $0.look.glow = true }
        doc.add()
        doc.rebase()
        #expect(doc.rows.count == 1)
        #expect(doc.rows[0].source == "{glow} a")
        #expect(doc.text == "{glow} a\n")
    }
}

@Suite("Message editor: one document, two views")
@MainActor
struct MessageEditorModelTests {
    private func tempFile(_ text: String) throws -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("msg-editor-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent("messages.txt")
        try text.write(to: url, atomically: true, encoding: .utf8)
        return url
    }

    @Test func unchangedFileSavesBackByteForByte() throws {
        let url = try tempFile(handWritten)
        let editor = MessageEditorModel(url: url)
        editor.load()
        #expect(!editor.unsaved)
        editor.showText()
        editor.showList()
        #expect(!editor.unsaved)
        #expect(editor.save())
        #expect(try String(contentsOf: url, encoding: .utf8) == handWritten)
    }

    @Test func switchingViewsKeepsUnsavedEdits() throws {
        let url = try tempFile(handWritten)
        let editor = MessageEditorModel(url: url)
        editor.load()
        editor.debugAdd()
        #expect(editor.unsaved)
        #expect(editor.debugAdded == "Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.")
        editor.showText()
        #expect(editor.text == handWritten + "\n" + editor.debugAdded!)
        editor.text += "\n{blink} typed by hand"
        editor.showList()
        #expect(editor.document.messageRows.last?.content == .raw("{blink} typed by hand"))
        #expect(editor.save())
        let saved = try String(contentsOf: url, encoding: .utf8)
        #expect(saved == handWritten + "\nFri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.\n{blink} typed by hand")
        #expect(!editor.unsaved)
    }

    @Test func revertDropsEditsAndSavedRowsReadAsSaved() throws {
        let url = try tempFile("a\n")
        let editor = MessageEditorModel(url: url)
        editor.load()
        editor.addLine(MessageLine(text: "b"))
        #expect(editor.unsaved)
        editor.load()
        #expect(!editor.unsaved)
        #expect(editor.document.messageRows.count == 1)
        editor.addLine(MessageLine(text: "b"))
        #expect(editor.save())
        #expect(!editor.unsaved)
        #expect(try String(contentsOf: url, encoding: .utf8) == "a\nb\n")
        let id = editor.document.messageRows[1].id
        editor.document.update(id) { $0.look.glow = false }
        #expect(editor.unsaved)
        #expect(editor.save())
        #expect(try String(contentsOf: url, encoding: .utf8) == "a\n{noglow} b\n")
    }

    @Test func debugStateIsCountsOnly() throws {
        let url = try tempFile(handWritten)
        let editor = MessageEditorModel(url: url)
        editor.load()
        var s = editor.debugState(open: true)
        #expect(s == MessageEditorDebug(open: true, mode: "list", rows: 16, styled: 11, raw: 5, notes: 3, unsaved: false, added: nil))
        editor.debugAdd()
        s = editor.debugState(open: true)
        #expect(s.rows == 17 && s.styled == 12 && s.unsaved)
        var input = DebugStateInput()
        input.messageEditor = s
        let yaml = YAMLEmitter.emit(DebugState.yaml(input))
        #expect(yaml.contains("message_editor:\n  open: true\n  mode: list\n  rows: 17\n  styled: 12\n  raw: 5\n  notes: 3\n  unsaved: true\n  today_special: 0\n  added: \"Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.\""))
        input.messageEditor = MessageEditorDebug(todaySpecial: 2)
        #expect(YAMLEmitter.emit(DebugState.yaml(input)).contains("  today_special: 2\n"))
        input.messageEditor = MessageEditorDebug()
        #expect(YAMLEmitter.emit(DebugState.yaml(input)).contains("message_editor:\n  open: false\n  mode: list\n  rows: 0\n  styled: 0\n  raw: 0\n  notes: 0\n  unsaved: false\n  today_special: 0\n  added: null"))
    }

    @Test func debugActionParses() {
        #expect((try? DebugLink.action("message-editor", arg: "add").get()) == .messageEditor(.add))
        #expect((try? DebugLink.action("message-editor", arg: "REVERT").get()) == .messageEditor(.revert))
        guard case .failure = DebugLink.action("message-editor", arg: "save") else {
            Issue.record("message-editor has no save"); return
        }
        #expect(DebugAction.names.contains("message-editor"))
    }
}

@Suite("Message editor: what each row does")
struct MessageRowNoteTests {
    private func notes(_ text: String, mix: Bool = false) -> [String] {
        let doc = MessageDocument(parsing: text)
        return doc.messageRows.map { doc.note(for: $0, mix: mix) }
    }

    @Test func dateRows() {
        #expect(notes("10-31: boo.") == ["October 31 · shows above the day's message"])
        #expect(notes("03-14: Sam.\n03-14: Alex.\n04-01: joke.") == [
            "March 14 · shows above the day's message · with 1 other that day",
            "March 14 · shows above the day's message · with 1 other that day",
            "April 1 · shows above the day's message",
        ])
        let four = (1...4).map { "03-14: \($0)" }.joined(separator: "\n")
        #expect(notes(four).first == "March 14 · shows above the day's message · with 3 others that day · 3 at a time, taking turns hourly")
    }

    @Test func weekdayRows() {
        #expect(notes("Fri: a.\nFri: b.\nFri: c.\nkeep.").first == "Fridays · takes turns with 2 others")
        #expect(notes("Fri: a.\nkeep.").first == "Fridays · shows instead of the every-day lines")
        #expect(notes("Fri: a.\nkeep.", mix: true).first == "Fridays · mixes with every-day lines")
        #expect(notes("Fri: a.\nFri: b.", mix: true).first == "Fridays · takes turns with 1 other · mixes with every-day lines")
    }

    @Test func everyDayRows() {
        #expect(notes("keep.") == ["Every day"])
        #expect(notes("keep.\ngo.").first == "Every day · takes turns with 1 other")
        #expect(notes("keep.\nFri: a.").first == "Every day · steps aside on days with their own line")
        #expect(notes("keep.\ngo.\nship.\nMon: a.").first == "Every day · takes turns with 2 others · steps aside on days with their own line")
        #expect(notes("keep.\nFri: a.", mix: true).first == "Every day · mixes in on days with their own line")
        // A date line is not a day with its own line: it adds, it doesn't replace.
        #expect(notes("keep.\n10-31: boo.").first == "Every day")
    }

    @Test func rawAndEmptyRows() {
        #expect(notes("{blink} hi\n{blink} ho").first == "Every day · takes turns with 1 other")
        #expect(notes("13-01: no.") == ["As written · never shows"])
        var doc = MessageDocument(parsing: "keep.")
        doc.add()
        #expect(doc.note(for: doc.rows[1], mix: false) == "Shows once it has text")
        #expect(doc.note(for: doc.rows[0], mix: false) == "Every day")
    }

    @Test func stateFileCarriesTheMixSwitch() throws {
        let on = try #require(JSONSerialization.jsonObject(
            with: MessageProposal.stateJSON(pinned: nil, rotate: "daily", mixDaily: true)) as? [String: Any])
        #expect(on["mix_daily"] as? Bool == true)
        let off = try #require(JSONSerialization.jsonObject(
            with: MessageProposal.stateJSON(pinned: nil, rotate: "daily")) as? [String: Any])
        #expect(off["mix_daily"] as? Bool == false)
        #expect(MessageEngine.mixKey == "messageMixDaily")
    }

    @Test func todayLine() {
        #expect(MessageEditorBar.todayText(MessageEngine.Today()) == "Today: nothing")
        #expect(MessageEditorBar.todayText(MessageEngine.Today(special: ["{glow} happy birthday, Sam."], usual: "{write} keep."))
                == "Today: happy birthday, Sam. / keep.")
    }
}

@Suite("On special days: Stack, Take turns, Scroll")
@MainActor
struct MessageSpecialModeTests {
    private let t0 = Date(timeIntervalSince1970: 1_800_000_000)   // a multiple of 10, 30, 60 and 300

    private func model(_ mode: MessageSpecialMode, seconds: Double = 10, special: [String] = ["sam.", "alex."],
                       usual: String? = "keep.", at now: Date) -> DeskModel {
        let m = DeskModel()
        m.specialMessages = special
        m.message = usual
        m.applySpecialSettings(mode: mode, seconds: seconds, now: now)
        return m
    }

    @Test func cycleOrderAndTiming() {
        for (offset, want) in [(0.0, 0), (9.9, 0), (10, 1), (19, 1), (20, 2), (29.9, 2), (30, 0), (45, 1)] {
            #expect(MessageSpecialMode.index(at: t0.addingTimeInterval(offset), count: 3, seconds: 10) == want, "\(offset)")
        }
        #expect(MessageSpecialMode.index(at: t0.addingTimeInterval(61), count: 2, seconds: 60) == 1)
        #expect(MessageSpecialMode.index(at: t0, count: 1, seconds: 10) == 0)
        #expect(MessageSpecialMode.nextChange(after: t0.addingTimeInterval(3), seconds: 10) == t0.addingTimeInterval(10))
        #expect(MessageSpecialMode.nextChange(after: t0, seconds: 300) == t0.addingTimeInterval(300))
        // Cycle order: the date's lines, then the usual line, round again.
        let m = model(.turns, at: t0)
        #expect(m.messageLines == ["sam.", "alex.", "keep."])
        var shown: [String?] = []
        for k in 0..<4 {
            m.updateCycle(now: t0.addingTimeInterval(Double(k) * 10))
            shown.append(m.cycleLine)
        }
        #expect(shown == ["sam.", "alex.", "keep.", "sam."])
    }

    @Test func theNotchFollowsTheSameLine() {
        for mode in [MessageSpecialMode.turns, .scroll] {
            let m = model(mode, at: t0.addingTimeInterval(10))
            #expect(m.oneLineMessage == "alex.")
            m.updateCycle(now: t0.addingTimeInterval(20))
            #expect(m.oneLineMessage == "keep.")
        }
        // Stack: the first date line, as before.
        #expect(model(.stack, at: t0.addingTimeInterval(10)).oneLineMessage == "sam.")
        #expect(model(.stack, at: t0).cycleLine == nil)
    }

    @Test func nothingCyclesWithoutASpecialDay() {
        let m = model(.turns, special: [], at: t0.addingTimeInterval(10))
        #expect(!m.cycling)
        #expect(m.oneLineMessage == "keep.")
        // A date line alone: one line, nothing to take turns with.
        let alone = model(.scroll, special: ["sam."], usual: nil, at: t0.addingTimeInterval(10))
        #expect(!alone.cycling)
        #expect(alone.oneLineMessage == "sam.")
    }

    @Test func theTurnsRestWhileTheDeskIsCovered() {
        let m = model(.turns, at: t0)
        #expect(m.cycleIndex == 0)
        m.motionPaused = true
        m.updateCycle(now: t0.addingTimeInterval(10))
        #expect(m.cycleIndex == 0)
        m.updateCycle(now: t0.addingTimeInterval(20))
        #expect(m.cycleLine == "sam.")
        m.motionPaused = false
        m.updateCycle(now: t0.addingTimeInterval(20))
        #expect(m.cycleLine == "keep.")
    }

    @Test func reduceMotionFallsBackToASwap() {
        #expect(MessageSpecialMode.change(.turns, reduceMotion: false) == .crossfade)
        #expect(MessageSpecialMode.change(.scroll, reduceMotion: false) == .scroll)
        #expect(MessageSpecialMode.change(.turns, reduceMotion: true) == .none)
        #expect(MessageSpecialMode.change(.scroll, reduceMotion: true) == .none)
        #expect(MessageSpecialMode.change(.stack, reduceMotion: false) == .none)
        #expect(MessageSpecialMode.fade == 0.4)
    }

    @Test func settingsRoundTrip() throws {
        let d = try #require(UserDefaults(suiteName: "special-\(UUID().uuidString)"))
        #expect(MessageSpecialMode.saved(in: d) == .stack)
        #expect(MessageSpecialMode.savedSeconds(in: d) == 10)
        d.set("scroll", forKey: MessageSpecialMode.key)
        d.set(60.0, forKey: MessageSpecialMode.secondsKey)
        #expect(MessageSpecialMode.saved(in: d) == .scroll)
        #expect(MessageSpecialMode.savedSeconds(in: d) == 60)
        d.set("ticker", forKey: MessageSpecialMode.key)
        d.set(7.0, forKey: MessageSpecialMode.secondsKey)
        #expect(MessageSpecialMode.saved(in: d) == .stack)
        #expect(MessageSpecialMode.savedSeconds(in: d) == 10)
        #expect(MessageSpecialMode.key == "messageSpecialMode")
        #expect(MessageSpecialMode.secondsKey == "messageSpecialSeconds")
        #expect(MessageSpecialMode.allCases.map(\.title) == ["Stack", "Take turns", "Scroll"])
        #expect(MessageSpecialMode.presets.map(MessageSpecialMode.label) == ["5 s", "10 s", "30 s", "1 min", "5 min"])
    }

    @Test func stateFileCarriesTheSetting() throws {
        let doc = try #require(JSONSerialization.jsonObject(
            with: MessageProposal.stateJSON(pinned: nil, rotate: "daily", specialMode: .turns, specialSeconds: 30)) as? [String: Any])
        #expect(doc["special_mode"] as? String == "turns")
        #expect(doc["special_seconds"] as? Int == 30)
        let plain = try #require(JSONSerialization.jsonObject(
            with: MessageProposal.stateJSON(pinned: nil, rotate: "daily")) as? [String: Any])
        #expect(plain["special_mode"] as? String == "stack")
        #expect(plain["special_seconds"] as? Int == 10)
    }

    @Test func theMessageCardShowsASampleSpecialDay() throws {
        let d = try #require(UserDefaults(suiteName: "special-card-\(UUID().uuidString)"))
        d.set("turns", forKey: MessageSpecialMode.key)
        let live = DeskModel()
        live.message = "keep."
        let preview = DeskModel()
        let samples = SurfacePreviewData.fill(preview, from: live, islandUp: false, sampleSpecialDay: true, desk: d, now: t0)
        #expect(samples.contains(.specialDay))
        #expect(preview.specialMessages == [SurfacePreviewData.sampleSpecialLine])
        #expect(preview.specialMode == .turns)
        #expect(preview.cycling)
        // Other cards copy the live lines only.
        let other = DeskModel()
        #expect(!SurfacePreviewData.fill(other, from: live, islandUp: false, desk: d, now: t0).contains(.specialDay))
        #expect(other.specialMessages.isEmpty)
    }
}
