import SwiftUI
import AppKit

/// Settings, Message's line editor (item 69): each line a row drawn as the Desk draws it, its
/// When, Text and Look set with menus, wells and sliders; Edit as text… for the file itself. The
/// rows and the text are one document (MessageEditorModel, MessageLineModel). Each piece is its
/// own small view so Swift 6.0 and 6.1 type-check the bodies.

// MARK: - The bar: rotation, the view switch, Save

struct MessageEditorBar: View {
    @Bindable var editor: MessageEditorModel
    let saved: () -> Void
    @AppStorage("messageRotate", store: .desk) private var rotate = "daily"

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Picker("Change the line", selection: $rotate) {
                    Text("Once a day").tag("daily")
                    Text("Every hour").tag("hourly")
                }
                .pickerStyle(.segmented)
                .fixedSize()
                .help("How often the Desk picks another line from today's lines.")
                Spacer()
                Button(editor.mode == .list ? "Edit as text…" : "Edit as a list") {
                    if editor.mode == .list { editor.showText() } else { editor.showList() }
                }
                .help(editor.mode == .list
                      ? "The file itself, tags and all; your edits carry over."
                      : "Back to the rows; your edits carry over.")
            }
            HStack {
                Button("Save") { if editor.save() { saved() } }
                    .keyboardShortcut("s", modifiers: .command)
                    .disabled(!editor.unsaved)
                Button("Revert") { editor.load() }
                    .disabled(!editor.unsaved)
                    .help("Drops your unsaved edits and shows messages.txt as it is.")
                Text(editor.unsaved ? "Unsaved changes" : "Today: \(MessageEngine.current().map { MessageMarkup.parse($0).text } ?? "nothing")")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Spacer()
                if editor.fileChanged {
                    Text("The file changed").font(.caption).foregroundStyle(.secondary)
                    Button("Reload") { editor.load() }
                        .help("Shows messages.txt as it is now; your unsaved edits are dropped.")
                }
            }
        }
    }
}

// MARK: - The list

struct MessageListEditor: View {
    @Bindable var editor: MessageEditorModel
    let pinChanged: () -> Void
    @AppStorage("message", store: .desk) private var pinned = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if !pinned.isEmpty { pinnedNote }
            let rows = editor.document.messageRows
            if rows.isEmpty {
                Text("No lines yet. Add one and the Desk shows it.")
                    .foregroundStyle(.secondary)
            }
            ForEach(rows) { row in
                MessageRowView(editor: editor, row: row, pinned: $pinned, pinChanged: pinChanged)
            }
            HStack {
                Button { editor.addLine() } label: { Label("Add Line", systemImage: "plus") }
                    .help("A new line, every day, in the Desk's look.")
                Spacer()
                if editor.document.noteCount > 0 {
                    Text("Notes (#) and blank lines in the file stay where they are.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }

    private var pinnedNote: some View {
        HStack {
            Image(systemName: "pin.fill").foregroundStyle(.tint).accessibilityHidden(true)
            Text("Pinned: the Desk shows \"\(MessageMarkup.parse(pinned).text)\" every day.")
                .lineLimit(1)
            Spacer()
            Button("Unpin") { pinned = ""; pinChanged() }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 6).fill(Color.accentColor.opacity(0.08)))
        .accessibilityElement(children: .combine)
    }
}

/// One line: its day, its preview, Pin, the row menu and, opened, its controls.
private struct MessageRowView: View {
    @Bindable var editor: MessageEditorModel
    let row: MessageRow
    @Binding var pinned: String
    let pinChanged: () -> Void
    @State private var targeted = false

    private var open: Bool { editor.expanded == row.id && row.line != nil }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
                .contentShape(Rectangle())
                .draggable(row.id.uuidString) { MessageRowPreview(line: row.deskBody).frame(width: 260) }
            if let line = row.line, let problem = line.problem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(.orange)
            }
            if case .raw(let source) = row.content {
                Text(source).font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                    .lineLimit(2).textSelection(.enabled)
                Text("Kept as written: the editor can't set everything on this line. Edit as text… changes it.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if open { MessageLineControls(line: lineBinding) }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 8)
            .fill(Color.accentColor.opacity(targeted ? 0.2 : (open ? 0.06 : 0.03))))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(.quaternary))
        .dropDestination(for: String.self) { items, _ in
            guard let dragged = items.first.flatMap(UUID.init(uuidString:)) else { return false }
            editor.document.move(dragged, onto: row.id)
            return true
        } isTargeted: { targeted = $0 }
    }

    private var header: some View {
        HStack(spacing: 8) {
            Image(systemName: "line.3.horizontal").foregroundStyle(.secondary)
                .help("Drag to reorder")
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                Text(row.line?.when.label ?? "As written").font(.caption).foregroundStyle(.secondary)
                MessageRowPreview(line: row.deskBody)
            }
            pinButton
            rowMenu
            if row.line != nil {
                Button(open ? "Done" : "Edit") { editor.expanded = open ? nil : row.id }
                    .help(open ? "Hides this line's controls." : "Shows this line's When, Text and Look.")
                    .accessibilityLabel(open ? "Done editing this line" : "Edit this line")
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityAction(named: "Move Up") { editor.document.move(row.id, by: -1) }
        .accessibilityAction(named: "Move Down") { editor.document.move(row.id, by: 1) }
    }

    private var isPinned: Bool { row.deskBody.map { !pinned.isEmpty && $0 == pinned } ?? false }

    private var pinButton: some View {
        Button {
            pinned = isPinned ? "" : (row.deskBody ?? "")
            pinChanged()
        } label: {
            Image(systemName: isPinned ? "pin.fill" : "pin")
        }
        .buttonStyle(.borderless)
        .disabled(row.deskBody == nil)
        .help(isPinned ? "Unpin: the Desk picks from the list again." : "Pin: the Desk shows this line every day, whatever the list says.")
        .accessibilityLabel(isPinned ? "Unpin this line" : "Pin this line")
    }

    private var rowMenu: some View {
        Menu {
            Button("Duplicate") { editor.expanded = editor.document.duplicate(row.id) ?? editor.expanded }
            Button("Move Up") { editor.document.move(row.id, by: -1) }
            Button("Move Down") { editor.document.move(row.id, by: 1) }
            Divider()
            Button("Delete", role: .destructive) { editor.document.delete(row.id) }
        } label: {
            Image(systemName: "ellipsis.circle")
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .help("Duplicate, move or delete this line.")
        .accessibilityLabel("Line actions")
    }

    private var lineBinding: Binding<MessageLine> {
        let id = row.id
        return Binding(
            get: { editor.document.rows.first { $0.id == id }?.line ?? MessageLine() },
            set: { new in editor.document.update(id) { $0 = new } })
    }
}

/// A row's line drawn as the Desk draws it (DeskMessageLine, as item 68's Message preview), small,
/// on the preview wallpaper. Reduce Motion stills it, as on the Desk.
struct MessageRowPreview: View {
    let line: String?

    @AppStorage("font", store: .desk) private var savedFont: String?
    @AppStorage("messageFont", store: .desk) private var messageFont = ""
    @AppStorage("messageColor", store: .desk) private var messageColor = "9ad7ff"
    @AppStorage(DeskMessageLook.glowKey, store: .desk) private var messageGlow = true

    static let baseSize: CGFloat = 22

    var body: some View {
        ZStack(alignment: .leading) {
            PreviewWallpaper()
            if let line {
                DeskMessageLine(raw: line, font: messageFont.isEmpty ? DeskFont.resolve(saved: savedFont) : messageFont,
                                baseSize: Self.baseSize, inkSpec: messageColor, globalGlow: messageGlow,
                                alignment: .leading, paused: false, lineLimit: 1)
                    .padding(.horizontal, 12)
            } else {
                Text("Type the line's text").font(.callout).foregroundStyle(.white.opacity(0.5))
                    .padding(.horizontal, 12)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 50, maxHeight: 50)
        .clipShape(RoundedRectangle(cornerRadius: 7))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(line.map { "Preview: \(MessageMarkup.parse($0).text)" } ?? "Preview: no text yet")
    }
}

// MARK: - A line's controls

struct MessageLineControls: View {
    @Binding var line: MessageLine

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            MessageWhenControl(when: $line.when)
            LabeledContent("Text") {
                TextField("Text", text: $line.text, prompt: Text("What the Desk says"))
                    .labelsHidden()
                    .help("What the Desk says. Plain text: the look below adds the effects.")
            }
            MessageInkControl(look: $line.look)
            MessageLookControls(look: $line.look)
        }
        .padding(.leading, 22)
    }
}

/// When: Every day, a weekday, or a date (month and day).
private struct MessageWhenControl: View {
    @Binding var when: MessageWhen

    var body: some View {
        LabeledContent("When") {
            HStack {
                Picker("When", selection: choice) {
                    Text("Every day").tag("every")
                    Divider()
                    ForEach(MessageWhen.weekdays, id: \.self) { d in
                        Text(MessageWhen.weekdayNames[d] ?? d).tag(d)
                    }
                    Divider()
                    Text("A date").tag("date")
                }
                .labelsHidden()
                .fixedSize()
                .help("When the line shows: every day, on one weekday, or on one date each year.")
                if case .date(let month, let day) = when { datePickers(month, day) }
                Spacer()
            }
        }
    }

    private func datePickers(_ month: Int, _ day: Int) -> some View {
        HStack {
            Picker("Month", selection: Binding(
                get: { month },
                set: { m in when = .date(month: m, day: min(day, MessageWhen.days(inMonth: m))) })) {
                ForEach(1...12, id: \.self) { Text(MessageWhen.monthNames[$0 - 1]).tag($0) }
            }
            .labelsHidden().fixedSize()
            .accessibilityLabel("Month")
            Picker("Day", selection: Binding(get: { day }, set: { when = .date(month: month, day: $0) })) {
                ForEach(1...MessageWhen.days(inMonth: month), id: \.self) { Text("\($0)").tag($0) }
            }
            .labelsHidden().fixedSize()
            .accessibilityLabel("Day")
        }
        .help("The date the line shows, every year.")
    }

    private var choice: Binding<String> {
        Binding(
            get: {
                switch when {
                case .everyDay: "every"
                case .weekday(let d): d
                case .date: "date"
                }
            },
            set: { new in
                switch new {
                case "every": when = .everyDay
                case "date":
                    if case .date = when { return }
                    let c = Calendar.current.dateComponents([.month, .day], from: Date())
                    when = .date(month: c.month ?? 1, day: c.day ?? 1)
                default: when = .weekday(new)
                }
            })
    }
}

/// Color: as the Desk, one color, or a gradient of 2 to 4 wells; the named palettes.
private struct MessageInkControl: View {
    @Binding var look: MessageLook

    var body: some View {
        LabeledContent("Color") {
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Picker("Color", selection: Binding(get: { look.inkMode }, set: { look.setInkMode($0) })) {
                        ForEach(MessageLook.InkMode.allCases, id: \.self) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .fixedSize()
                    .help("The line's ink: the Desk's message color, one color, or a gradient of 2 to 4 colors.")
                    palettes
                }
                if let ink = look.ink { wells(ink) }
            }
        }
    }

    private var palettes: some View {
        Menu(MessagePalette.name(of: look.ink).map { "Palette: \($0)" } ?? "Palette") {
            ForEach(MessagePalette.all, id: \.name) { p in
                Button(p.name) { look.ink = p.colors }
            }
        }
        .fixedSize()
        .help("A named gradient: synthwave, sunset, ocean, aurora, ember, bubblegum, toxic or gold.")
    }

    private func wells(_ ink: [String]) -> some View {
        HStack(spacing: 6) {
            ForEach(Array(ink.indices), id: \.self) { i in
                ColorPicker("Color \(i + 1)", selection: color(i), supportsOpacity: false)
                    .labelsHidden()
                    .accessibilityLabel(ink.count == 1 ? "Color" : "Color \(i + 1) of \(ink.count)")
                if ink.count > 2 {
                    Button { look.removeStop(at: i) } label: { Image(systemName: "minus.circle") }
                        .buttonStyle(.borderless)
                        .help("Removes this color from the gradient.")
                        .accessibilityLabel("Remove color \(i + 1)")
                }
            }
            if ink.count >= 2 && ink.count < MessageLook.maxInk {
                Button("Add Color") { look.addStop() }
                    .help("Adds a color to the gradient, up to 4.")
            }
        }
    }

    private func color(_ i: Int) -> Binding<Color> {
        Binding(
            get: {
                let hex = look.ink.flatMap { $0.indices.contains(i) ? $0[i] : nil } ?? "ffffff"
                let c = SegmentStyle.components(hex)
                return Color(red: c?.0 ?? 1, green: c?.1 ?? 1, blue: c?.2 ?? 1)
            },
            set: { new in
                guard let ns = NSColor(new).usingColorSpace(.sRGB) else { return }
                look.setStop(i, hex: SegmentStyle.hex(red: ns.redComponent, green: ns.greenComponent, blue: ns.blueComponent))
            })
    }
}

/// Glow, Size, Letters and Motion.
private struct MessageLookControls: View {
    @Binding var look: MessageLook

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            LabeledContent("Glow") {
                Picker("Glow", selection: $look.glow) {
                    Text("On").tag(Bool?.some(true))
                    Text("Off").tag(Bool?.some(false))
                    Text("As the Desk").tag(Bool?.none)
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
                .help("The soft glow around the line: on, off, or as the Desk's Look page sets it.")
            }
            LabeledContent("Size") { size }
            LabeledContent("Letters") {
                Picker("Letters", selection: $look.font) {
                    Text("As written").tag(LetterStyle?.none)
                    ForEach(MessageLetters.order, id: \.self) { Text($0.title).tag(LetterStyle?.some($0)) }
                }
                .labelsHidden().fixedSize()
                .help("A letter style: bold, italic and small caps in the Desk's font, the others as Unicode letters.")
            }
            LabeledContent("Motion") {
                Picker("Motion", selection: Binding(get: { look.motion }, set: { look.setMotion($0) })) {
                    ForEach(MessageMotionKind.allCases, id: \.self) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
                .help("Write in draws the line once as it appears; Shimmer sweeps a soft light every few seconds; Sweep runs a bright light across it. Reduce Motion keeps it still.")
            }
        }
    }

    private var size: some View {
        HStack {
            Slider(value: Binding(
                get: { look.size ?? 1 },
                set: { v in
                    let r = (v * 20).rounded() / 20
                    look.size = abs(r - 1) < 0.001 ? nil : r
                }), in: MessageMarkup.sizeRange, step: 0.05)
                .frame(maxWidth: 220)
                .accessibilityLabel("Size")
                .accessibilityValue("\(MessageLook.number(look.size ?? 1)) times")
            Text("\(MessageLook.number(look.size ?? 1))×")
                .font(.system(.body, design: .monospaced))
                .frame(width: 48, alignment: .leading)
                .accessibilityHidden(true)
        }
        .help("The line's size against the Desk's message size, from 0.5 to 2 times.")
    }
}

// MARK: - Edit as text…

struct MessageTextEditorPane: View {
    @Bindable var editor: MessageEditorModel
    let pinChanged: () -> Void
    @AppStorage("message", store: .desk) private var pinned = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .top, spacing: 12) {
                TextEditor(text: $editor.text)
                    .font(.system(size: 12, design: .monospaced))
                    .frame(height: 320)
                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(.quaternary))
                    .accessibilityLabel("messages.txt")
                MessageTagReference().frame(width: 230)
            }
            TextField("Pin one line instead (leave empty to use the list)", text: $pinned)
                .onChange(of: pinned) { _, _ in pinChanged() }
        }
    }
}

/// The grammar, beside the text: prefixes, notes and every tag.
struct MessageTagReference: View {
    static let entries: [(tag: String, meaning: String)] = [
        ("Mon: text", "only on Mondays (Mon to Sun)"),
        ("10-31: text", "only on that date"),
        ("# note", "a note; the Desk skips it"),
        ("{ink:#ff2a6d,#05d9e8}", "1 to 4 colors; two or more make a gradient"),
        ("{glow} {noglow}", "the glow on or off"),
        ("{size:1.2}", "0.5 to 2 times the size"),
        ("{font:script}", "letters: \(LetterStyle.tagNames)"),
        ("{write}", "draws itself in once"),
        ("{shimmer}", "a soft light every few seconds"),
        ("{sweep} {sweep:20}", "a bright light across, once or every N seconds"),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Tags go first, after any day.").font(.caption.weight(.semibold))
            ForEach(Self.entries, id: \.tag) { e in
                VStack(alignment: .leading, spacing: 1) {
                    Text(e.tag).font(.system(size: 11, design: .monospaced))
                    Text(e.meaning).font(.caption).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .accessibilityElement(children: .combine)
            }
            Text("A tag the Desk doesn't know draws as text, from there on.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .textSelection(.enabled)
    }
}

// MARK: - Ask Claude

struct MessageAskClaude: View {
    @AppStorage(DeskMessageHandoff.directKey, store: .desk) private var claudeDirect = false
    @State private var copied = false

    static let examplePrompt = "Suggest three Desk messages for next week, one for Fridays in a sunset gradient with a sweep."

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Ask Claude").font(.headline)
            Text("Claude Code can suggest lines through Sanduhr's MCP server (Settings, Integrations). Its suggestions wait at the top of this page for you to add.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(alignment: .firstTextBaseline) {
                Text(Self.examplePrompt)
                    .font(.system(size: 12, design: .monospaced))
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer()
                Button(copied ? "Copied" : "Copy") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(Self.examplePrompt, forType: .string)
                    copied = true
                }
                .help("Copies the example prompt, to paste into Claude Code.")
                .accessibilityLabel("Copy the example prompt")
            }
            .padding(8)
            .background(RoundedRectangle(cornerRadius: 6).fill(.quaternary.opacity(0.5)))
            Toggle("Let Claude change the messages directly", isOn: $claudeDirect)
            Text(claudeDirect
                 ? "Lines Claude proposes with the Sanduhr MCP server go into the list at once. The list before each change is kept as messages.txt.previous."
                 : "Lines Claude proposes with the Sanduhr MCP server wait here for you to add, review or dismiss.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
