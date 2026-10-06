import SwiftUI
import AppKit
import ServiceManagement

/// The Desk sections of the Settings window (SettingsWindow.swift) and its General section.
/// Every Desk control writes the same com.626labs.sanduhr.desk defaults the desktop reads, so
/// changes show on the desktop as you make them.

// MARK: - Layout

struct DeskLayoutSection: View {
    @AppStorage("layout", store: .desk) private var layout = "message:tl clock:bl claude:bl meetings:bl"
    @AppStorage("left", store: .desk) private var left = 52.0
    @AppStorage("right", store: .desk) private var right = 52.0
    @AppStorage("top", store: .desk) private var top = 40.0
    @AppStorage("bottom", store: .desk) private var bottom = 60.0

    static let slots: [(key: String, name: String)] = [
        ("tl", "Top left"), ("tr", "Top right"), ("bl", "Bottom left"), ("br", "Bottom right"), ("", "Hidden"),
    ]

    var body: some View {
        Form {
            Section("Where each piece sits") {
                ForEach(DeskLayout.widgets, id: \.key) { w in
                    Picker(w.name, selection: slotBinding(w.key)) {
                        ForEach(Self.slots, id: \.key) { Text($0.name).tag($0.key) }
                    }
                }
                Text("Pieces in the same corner stack in this order. The top and bottom of a side share a column, so they never overlap. Now playing shows only while something plays; its other settings are in Now Playing.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Margins") {
                slider("Left", $left, 0...300)
                slider("Right", $right, 0...300)
                slider("Top (below the menu bar)", $top, 0...300)
                slider("Bottom", $bottom, 0...300)
            }
        }
        .formStyle(.grouped)
    }

    private func slotBinding(_ widget: String) -> Binding<String> {
        Binding(
            get: { DeskLayout.parse(layout)[widget] ?? "" },
            set: { layout = DeskLayout.placing(widget, in: $0, layout: layout) })
    }
}

/// The layout string the Layout section edits ("message:tl clock:bl claude:bl meetings:bl"), kept
/// apart from the view so it tests without AppKit. DeskView reads the same string. Now playing
/// (item 53b) is an element like the others, off unless placed.
enum DeskLayout {
    /// The layout DeskView draws when none is saved.
    static let standard = "message:tl clock:bl claude:bl meetings:bl"

    static let widgets: [(key: String, name: String)] = [
        ("message", "Message"), ("clock", "Clock and date"), ("claude", "Claude line"),
        ("meters", "Claude meters (bars)"), ("nowPlaying", "Now playing"), ("meetings", "Meetings"),
    ]

    /// Widget to slot. Words without exactly one colon are skipped; a repeated widget keeps its last slot.
    static func parse(_ s: String) -> [String: String] {
        var out: [String: String] = [:]
        for item in s.split(separator: " ") {
            let bits = item.split(separator: ":").map(String.init)
            if bits.count == 2 { out[bits[0]] = bits[1] }
        }
        return out
    }

    /// The corners DeskView knows.
    static let slots: Set<String> = ["tl", "tr", "bl", "br"]

    /// The widgets DeskView draws for `layout`: known widgets in a known corner, less the
    /// meetings with the older showMeetings switch off and the claude line and meters with
    /// showClaude off (DeskView.placement).
    static func placed(_ layout: String, showMeetings: Bool = true, showClaude: Bool = true) -> Set<String> {
        let known = Set(widgets.map(\.key))
        var out: Set<String> = []
        for item in layout.split(separator: " ") {
            let bits = item.split(separator: ":").map(String.init)
            guard bits.count == 2, known.contains(bits[0]), slots.contains(bits[1]) else { continue }
            if bits[0] == "meetings" && !showMeetings { continue }
            if (bits[0] == "claude" || bits[0] == "meters") && !showClaude { continue }
            out.insert(bits[0])
        }
        return out
    }

    /// The layout with `widget` moved to `slot` ("" hides it), rewritten in the canonical widget
    /// order, which is also the stacking order within a corner.
    static func placing(_ widget: String, in slot: String, layout: String) -> String {
        var map = parse(layout)
        map[widget] = slot.isEmpty ? nil : slot
        return widgets.compactMap { w in map[w.key].map { "\(w.key):\($0)" } }.joined(separator: " ")
    }
}

// MARK: - Look

struct DeskLookSection: View {
    @AppStorage("font", store: .desk) private var savedFont: String?
    @AppStorage("messageFont", store: .desk) private var messageFont = ""
    @AppStorage("timeSize", store: .desk) private var timeSize = 112.0
    @AppStorage("messageSize", store: .desk) private var messageSize = 84.0
    @AppStorage("messageColor", store: .desk) private var messageColor = "9ad7ff"
    @AppStorage(DeskMessageLook.glowKey, store: .desk) private var messageGlow = true
    @AppStorage("inkColor", store: .desk) private var inkColor = "ffffff"
    @AppStorage("inkShadow", store: .desk) private var inkShadow = true
    @AppStorage("notchTextColor", store: .desk) private var notchTextColor = "ffffff"
    @State private var families: [String] = []

    /// The Desk font as drawn (EsteFont 26 until one is picked, or when the picked one is gone);
    /// picking writes it.
    private var font: Binding<String> {
        Binding(get: { DeskFont.resolve(saved: savedFont) }, set: { savedFont = $0 })
    }

    static let presets: [(name: String, value: String)] = [
        ("Ice gradient", "8f5bd6,3a63e0,33fdff"),
        ("Ice gradient, exact", "531b93,012089,00fdff"),
        ("Sky", "9ad7ff"),
        ("Marquee", "ffd08a"),
        ("White", "ffffff"),
    ]

    var body: some View {
        Form {
            Section("Fonts") {
                Picker("Desk font", selection: font) {
                    Text("System").tag("")
                    Divider()
                    ForEach(families, id: \.self) { Text($0).tag($0) }
                }
                Picker("Message font", selection: $messageFont) {
                    Text("Same as Desk").tag("")
                    Divider()
                    ForEach(families, id: \.self) { Text($0).tag($0) }
                }
            }
            Section("Sizes") {
                slider("Clock", $timeSize, 48...220)
                slider("Message", $messageSize, 24...180)
            }
            Section("Colors (one hex, or several with commas for a gradient)") {
                ColorRow(title: "Message", value: $messageColor)
                Toggle("Glow around the message ({glow} and {noglow} change one line)", isOn: $messageGlow)
                ColorRow(title: "Clock, date, meetings, Claude", value: $inkColor)
                Toggle("Drop shadow under the clock text", isOn: $inkShadow)
                ColorRow(title: "Notch text", value: $notchTextColor)
            }
        }
        .formStyle(.grouped)
        .onAppear {
            // EsteFont 26 first: it ships inside Sanduhr (item 58).
            families = DeskFont.pickerFamilies(installed: NSFontManager.shared.availableFontFamilies)
        }
    }
}

private struct ColorRow: View {
    let title: String
    @Binding var value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                Spacer()
                Picker("", selection: $value) {
                    ForEach(DeskLookSection.presets, id: \.value) { Text($0.name).tag($0.value) }
                    if !DeskLookSection.presets.contains(where: { $0.value == value }) {
                        Text("Custom").tag(value)
                    }
                }
                .labelsHidden()
                .frame(width: 170)
            }
            HStack {
                TextField("hex", text: $value)
                    .font(.system(.body, design: .monospaced))
                Swatch(value: value).frame(width: 90, height: 18)
            }
        }
    }
}

private struct Swatch: View {
    let value: String
    var body: some View {
        let colors = value.split(separator: ",").map { Color.hex($0.trimmingCharacters(in: .whitespaces)) }
        RoundedRectangle(cornerRadius: 5)
            .fill(LinearGradient(colors: colors.count > 1 ? colors : [colors.first ?? .white, colors.first ?? .white],
                                 startPoint: .leading, endPoint: .trailing))
    }
}

// MARK: - Meters

/// Warnings on the meters, on Desk and the widget alike, set per meter: a group for the session, the all-models weekly
/// limit, and every other weekly limit the server reports, each of those with a Show switch (MeterVisibility).
struct DeskMetersSection: View {
    var model: DeskModel

    /// Session and weekly always; the other weekly limits once the server has reported them.
    static func tiers(present: [Tier]) -> [Tier] {
        Tier.allCases.filter { $0 == .fiveHour || $0 == .sevenDay || present.contains($0) }
    }

    var body: some View {
        Form {
            Section {
                Text("A meter that is nearly full while its reset is still far off draws its bar in red with a soft glow around it, on the Desk (in the Desk ink) and on the widget (in the theme's color). Each meter has its own setting; changes show at once on both.")
                    .font(.caption).foregroundStyle(.secondary)
                Text("A limit that looks temporary (a promotion, or a new limit whose reset is far off) can be hidden: it leaves the widget, the Desk meters and the alerts, and comes back on its own when it resets or refills. Session, Weekly and the model limits always show.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            ForEach(Self.tiers(present: model.reportedTiers), id: \.self) { tier in
                MeterWarningGroup(tier: tier, model: model)
            }
        }
        .formStyle(.grouped)
    }
}

private struct MeterWarningGroup: View {
    let tier: Tier
    var model: DeskModel
    @AppStorage private var enabled: Bool
    @AppStorage private var threshold: Double
    @AppStorage private var minReset: Double
    @AppStorage private var shown: Bool

    init(tier: Tier, model: DeskModel) {
        self.tier = tier
        self.model = model
        _shown = AppStorage(wrappedValue: true, MeterVisibility.showKey(tier), store: .desk)
        let standard = MeterWarningSettings.standard(for: tier)
        _enabled = AppStorage(wrappedValue: standard.enabled, MeterWarningSettings.onKey(tier), store: .desk)
        _threshold = AppStorage(wrappedValue: standard.threshold, MeterWarningSettings.thresholdKey(tier), store: .desk)
        _minReset = AppStorage(wrappedValue: standard.minReset, MeterWarningSettings.minResetKey(tier), store: .desk)
    }

    var body: some View {
        Section(tier.label) {
            // Only a temporary limit can be hidden (item 42); a permanent one keeps just its
            // warning settings.
            if temporary {
                Toggle("Show this limit", isOn: showBinding)
            }
            warningControls
                .disabled(!shown && temporary)
        }
    }

    private var temporary: Bool { model.temporaryTiers.contains(tier) }

    /// Reads the saved switch; writes through MeterVisibility, so a hide records the limit's numbers.
    private var showBinding: Binding<Bool> {
        Binding(get: { shown }, set: { model.setShown(tier, $0) })
    }

    @ViewBuilder private var warningControls: some View {
        Toggle("Warn when nearly full", isOn: $enabled)
        HStack {
            Text("At")
            Slider(value: $threshold, in: 50...100, step: 5)
            Text("\(Int(threshold))%")
                .font(.system(.body, design: .monospaced))
                .frame(width: 48, alignment: .trailing)
        }
        .disabled(!enabled)
        Picker("Only while the reset is more than", selection: $minReset) {
            ForEach(MeterWarning.minResetChoices, id: \.seconds) { Text($0.name).tag($0.seconds) }
            if !MeterWarning.minResetChoices.contains(where: { $0.seconds == minReset }) {
                Text("Custom").tag(minReset)
            }
        }
        .disabled(!enabled)
    }
}

// MARK: - Message

struct DeskMessageSection: View {
    var model: DeskModel
    var handoff = DeskMessageHandoff.shared
    @AppStorage("message", store: .desk) private var pinned = ""
    @AppStorage("messageRotate", store: .desk) private var rotate = "daily"
    @AppStorage(DeskMessageHandoff.directKey, store: .desk) private var claudeDirect = false
    @State private var text = ""
    @State private var saved = true
    /// messages.txt changed under unsaved edits (Claude's lines were added): offer to reload.
    @State private var fileChanged = false
    @State private var reviewing = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let proposal = handoff.pending {
                MessageSuggestionBanner(proposal: proposal, canAdd: saved,
                                        review: { reviewing = true },
                                        add: { handoff.approve() },
                                        dismiss: { handoff.dismiss() })
            }
            Text("One line per message. \"Mon: text\" only on Mondays, \"10-31: text\" only on that date, # for notes. Effects go first: {ink:#ff2a6d,#05d9e8} {glow} {noglow} {size:1.2} {write} {shimmer}.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            editor
            Picker("Change", selection: $rotate) {
                Text("Once a day").tag("daily")
                Text("Every hour").tag("hourly")
            }
            .pickerStyle(.segmented)
            TextField("Pin one line instead (leave empty to use the list)", text: $pinned)
                .onChange(of: pinned) { _, _ in model.message = MessageEngine.current() }
            claudeSwitch
        }
        .padding(.top, 8)
        .onAppear(perform: load)
        .onChange(of: rotate) { _, _ in model.message = MessageEngine.current() }
        .onChange(of: handoff.revision) { _, _ in
            if saved { load() } else { fileChanged = true }
        }
        .sheet(isPresented: $reviewing) {
            if let proposal = handoff.pending {
                MessageSuggestionReview(proposal: proposal, canAdd: saved,
                                        add: { reviewing = false; handoff.approve() },
                                        dismiss: { reviewing = false; handoff.dismiss() },
                                        close: { reviewing = false })
            }
        }
    }

    private var editor: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextEditor(text: $text)
                .font(.system(size: 12, design: .monospaced))
                .frame(minHeight: 170)
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(.quaternary))
                .onChange(of: text) { _, _ in saved = false }
            HStack {
                Button("Save") { save() }
                    .keyboardShortcut("s", modifiers: .command)
                    .disabled(saved)
                Text(saved ? "Today: \(MessageEngine.current() ?? "nothing")" : "Unsaved")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Spacer()
                if fileChanged {
                    Text("The file changed").font(.caption).foregroundStyle(.secondary)
                    Button("Reload") { load() }
                        .help("Shows messages.txt as it is now; your unsaved edits are dropped.")
                }
            }
        }
    }

    private var claudeSwitch: some View {
        VStack(alignment: .leading, spacing: 4) {
            Toggle("Let Claude change the messages directly", isOn: $claudeDirect)
            Text(claudeDirect
                 ? "Lines Claude proposes with the Sanduhr MCP server go into the list at once. The list before each change is kept as messages.txt.previous."
                 : "Lines Claude proposes with the Sanduhr MCP server wait here for you to add, review or dismiss.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.top, 6)
    }

    private func load() {
        MessageEngine.ensureFile()
        text = (try? String(contentsOf: MessageEngine.fileURL, encoding: .utf8)) ?? ""
        saved = true
        fileChanged = false
        // Assigning the text can mark it unsaved a moment later (onChange); it is not.
        DispatchQueue.main.async {
            saved = true
            fileChanged = false
        }
    }

    private func save() {
        try? text.write(to: MessageEngine.fileURL, atomically: true, encoding: .utf8)
        model.message = MessageEngine.current()
        saved = true
        fileChanged = false
    }
}

/// "Claude suggested N lines" over Settings, Message: the note, Add, Review and Dismiss.
private struct MessageSuggestionBanner: View {
    let proposal: MessageProposal
    /// False while the editor has unsaved edits: Add would write under them.
    let canAdd: Bool
    let review: () -> Void
    let add: () -> Void
    let dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Image(systemName: "sparkles").foregroundStyle(.tint)
                Text(MessageSuggestionReview.headline(proposal)).font(.headline)
                Spacer()
                Button("Dismiss", action: dismiss)
                Button("Review…", action: review)
                Button(proposal.mode == .replace ? "Replace" : "Add", action: add)
                    .buttonStyle(.borderedProminent)
                    .disabled(!canAdd)
                    .help(canAdd ? "" : "Save or reload your edits first.")
            }
            if let note = proposal.note {
                Text(note).font(.callout).foregroundStyle(.secondary).lineLimit(3)
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.accentColor.opacity(0.25)))
        .accessibilityElement(children: .contain)
    }
}

/// Review: every suggested line drawn as the Desk would draw it, effects included (a {write}
/// line writes itself in, a {shimmer} line shimmers), on a dark card.
struct MessageSuggestionReview: View {
    let proposal: MessageProposal
    let canAdd: Bool
    let add: () -> Void
    let dismiss: () -> Void
    let close: () -> Void

    @AppStorage("font", store: .desk) private var savedFont: String?
    private var font: String { DeskFont.resolve(saved: savedFont) }
    @AppStorage("messageFont", store: .desk) private var messageFont = ""
    @AppStorage("messageColor", store: .desk) private var messageColor = "9ad7ff"
    @AppStorage(DeskMessageLook.glowKey, store: .desk) private var messageGlow = true

    static func headline(_ p: MessageProposal) -> String {
        let n = p.messageLines.count
        let lines = "\(n) line\(n == 1 ? "" : "s")"
        return p.mode == .replace ? "Claude suggested \(lines) to replace your list" : "Claude suggested \(lines)"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(Self.headline(proposal)).font(.title3.weight(.semibold))
            if let note = proposal.note { Text(note).foregroundStyle(.secondary) }
            if proposal.mode == .replace {
                Text("Replace keeps the notes at the top of messages.txt and swaps every other line for these. Your list now is kept as messages.txt.previous.")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    ForEach(Array(proposal.lines.enumerated()), id: \.offset) { _, line in
                        row(line)
                    }
                }
                .padding(16)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .background(RoundedRectangle(cornerRadius: 10).fill(Color.black.opacity(0.85)))
            .frame(minHeight: 220)
            buttons
        }
        .padding(20)
        .frame(width: 560, height: 480)
    }

    @ViewBuilder
    private func row(_ line: String) -> some View {
        if MessageProposal.isMessageLine(line) {
            let split = MessageEngine.splitPrefix(line)
            VStack(alignment: .leading, spacing: 2) {
                if split.kind != .plain {
                    Text(split.kind == .date ? "only on \(split.tag)" : "only on \(split.tag)days")
                        .font(.caption2).foregroundStyle(.white.opacity(0.55))
                }
                DeskMessageLine(raw: split.body, font: messageFont.isEmpty ? font : messageFont, baseSize: 30,
                                inkSpec: messageColor, globalGlow: messageGlow, alignment: .leading, paused: false)
            }
        } else if !line.trimmingCharacters(in: .whitespaces).isEmpty {
            Text(line).font(.system(size: 11, design: .monospaced)).foregroundStyle(.white.opacity(0.5))
        }
    }

    private var buttons: some View {
        HStack {
            Button("Dismiss", action: dismiss)
            Spacer()
            Button("Close", action: close).keyboardShortcut(.cancelAction)
            Button(proposal.mode == .replace ? "Replace" : "Add", action: add)
                .keyboardShortcut(.defaultAction)
                .disabled(!canAdd)
        }
    }
}

// MARK: - Notch

struct DeskNotchSection: View {
    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchChin", store: .desk) private var chin = 26.0
    @AppStorage("notchText", store: .desk) private var wingText = true
    @AppStorage("notchChinText", store: .desk) private var chinText = false
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var left = NotchContent.Place.left.fallback
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var right = NotchContent.Place.right.fallback
    @AppStorage(NotchContent.Place.strip.key, store: .desk) private var strip = NotchContent.Place.strip.fallback
    @AppStorage(CameraLightController.enabledKey, store: .desk) private var cameraLight = false
    @AppStorage(CameraLightController.brightnessKey, store: .desk) private var lightBrightness = CameraLightController.defaultBrightness
    @AppStorage(CameraLightController.sizeKey, store: .desk) private var lightSize = CameraLightController.defaultSize

    var body: some View {
        Form {
            Section {
                Toggle("Extend the camera notch", isOn: $enabled)
                Text("Widens the notch into one black island while Desk is on. Click it to open these settings. Screens without a notch are left alone.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Text") {
                Toggle("Text beside the camera", isOn: $wingText)
                contentPicker("Left wing", $left).disabled(!wingText)
                if left == .nowPlaying { NowPlayingIdleCaption(place: .left).disabled(!wingText) }
                contentPicker("Right wing", $right).disabled(!wingText)
                if right == .nowPlaying { NowPlayingIdleCaption(place: .right).disabled(!wingText) }
                Toggle("Text under the camera too (desktop only)", isOn: $chinText)
                    .disabled(chin == 0)
                contentPicker("Under the camera", $strip).disabled(!chinText || chin == 0)
                if strip == .nowPlaying { NowPlayingIdleCaption(place: .strip).disabled(!chinText || chin == 0) }
                Text("Nothing leaves that part plain black. A wing grows to fit its text.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            .disabled(!enabled)
            Section("Size") {
                slider("Extra width each side", $wings, 0...180)
                slider("Extra height below (0 = none)", $chin, 0...56)
            }
            .disabled(!enabled)
            Section("Camera light") {
                Toggle("Light up for the camera", isOn: $cameraLight)
                    .onChange(of: cameraLight) { _, _ in CameraLightController.shared.apply() }
                HStack {
                    Text("Brightness")
                    Slider(value: $lightBrightness, in: CameraLightLayout.brightnessRange)
                    Text("\(Int((lightBrightness * 100).rounded()))%")
                        .font(.system(.body, design: .monospaced))
                        .frame(width: 48, alignment: .trailing)
                }
                slider("Reach below the menu bar", $lightSize, CameraLightLayout.sizeRange)
                Text("While any app uses a camera, a soft white light around the notch lights your face, above every app. It ends when the camera stops, with or without Desk or the island. Tools, Camera Light shows it by hand. Screens without a notch get it at the top center.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            NotchGlowSection()
        }
        .formStyle(.grouped)
    }

    private func contentPicker(_ title: String, _ selection: Binding<NotchContent>) -> some View {
        Picker(title, selection: selection) {
            ForEach(NotchContent.allCases) { Text($0.label).tag($0) }
        }
    }
}

/// Under a notch place set to Now playing: what it shows when nothing plays, and a link to the
/// choice on the Now Playing page. Its own view so the Notch page's body stays small.
private struct NowPlayingIdleCaption: View {
    let place: NotchContent.Place
    @AppStorage(NowPlayingIdle.key, store: .desk) private var idle = NowPlayingIdle.automatic

    var body: some View {
        HStack(spacing: 4) {
            Text(idle.caption(at: place))
            Button("Change…") { SettingsWindowController.shared.show(.nowPlaying) }
                .buttonStyle(.link)
        }
        .font(.caption)
        .foregroundStyle(.secondary)
    }
}

/// Notch, Glow: which events glow the notch. Its own view so the Notch page's body stays small
/// enough for Swift 6.0 and 6.1 to type-check.
private struct NotchGlowSection: View {
    @AppStorage(NotchGlowSwitches.alertsKey, store: .desk) private var glowAlerts = false
    @AppStorage(NotchGlowSwitches.meetingsKey, store: .desk) private var glowMeetings = false
    @AppStorage(NotchGlowSwitches.cameraKey, store: .desk) private var glowCamera = false
    @AppStorage(NotchGlowSwitches.claudeWaitingKey, store: .desk) private var claudeWaiting = false
    @AppStorage(NotchGlowSwitches.claudeDoneKey, store: .desk) private var claudeDone = false
    @AppStorage(NotchGlowSwitches.claudeSkipTerminalKey, store: .desk) private var skipTerminal = true

    var body: some View {
        Section("Glow") {
            Toggle("For Sanduhr alerts", isOn: $glowAlerts)
            Toggle("A minute before a meeting", isOn: $glowMeetings)
                .onChange(of: glowMeetings) { _, _ in NotchGlowController.shared.apply() }
            Toggle("When the camera light comes on", isOn: $glowCamera)
            HStack {
                Text("The notch's edge glows softly in the notch text color for a few seconds, once per event: around the island when it is on, around the notch itself when it is off. A Desk pulse always glows it. It never takes a click.")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                // Glows once whatever the switches say, so the look can be checked before
                // turning any of them on.
                Button("Test Glow") { NotchGlowController.shared.fire() }
            }
        }
        claudeSection
    }

    /// Claude Code's events (item 51).
    private var claudeSection: some View {
        Section("Glow for Claude Code") {
            Toggle("When Claude Code is waiting on you", isOn: $claudeWaiting)
            Toggle("When Claude Code finishes", isOn: $claudeDone)
            Toggle("Not while a terminal is in front", isOn: $skipTerminal)
                .disabled(!claudeWaiting && !claudeDone)
            Text("Needs the notch glow hooks in the Claude Code folder (Settings, Integrations). Claude Code tells Sanduhr only that it waits or finished, nothing about the conversation. At most one glow of each kind every 20 seconds. With a terminal or an editor that runs Claude Code in front (Terminal, iTerm2, Ghostty, Warp, WezTerm, Alacritty, kitty, VS Code, Cursor, Zed), you are already looking, so it doesn't glow. Screens without a notch glow at the top center.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

// MARK: - General

/// Which surfaces show, open at login, the shortcuts. The Widget picker is WidgetVisibility;
/// the switch under it mirrors panelHidden, which AppDelegate writes whenever the widget shows
/// or hides.
struct GeneralSection: View {
    @AppStorage(WidgetVisibility.key) private var widgetVisibility = WidgetVisibility.always
    @AppStorage(DeskController.enabledKey, store: .desk) private var deskEnabled = false
    @AppStorage(DeskController.notchKey, store: .desk) private var notch = false
    @AppStorage(AppDelegate.panelHiddenKey) private var panelHidden = false
    @AppStorage("menuIcon", store: .desk) private var menuIcon = false
    @AppStorage(MenuBarMode.key) private var menuBarMode = MenuBarMode.higher
    @AppStorage("showMeetings", store: .desk) private var showMeetings = true
    @AppStorage("showClaude", store: .desk) private var showClaude = true
    @AppStorage(DeskController.hotKeysKey, store: .desk) private var hotKeys = true
    @State private var atLogin = SMAppService.mainApp.status == .enabled

    var body: some View {
        Form {
            Section("Surfaces") {
                Toggle("Desk: clock, meters, meetings and the message on the desktop", isOn: $deskEnabled)
                    .onChange(of: deskEnabled) { _, _ in DeskController.shared.apply() }
                Toggle("Notch: the island around the camera (needs Desk)", isOn: $notch)
                // A choice made here, not any change to the key: onChange also fired for writes
                // from outside (a smoke run's defaults step), even with Settings closed, and
                // treated them as a pick (issue #105's "Always shown shows the widget").
                Picker("Widget: the floating window with the tools", selection: Binding(
                    get: { widgetVisibility },
                    set: { choice in
                        guard choice != widgetVisibility else { return }
                        widgetVisibility = choice
                        (NSApp.delegate as? AppDelegate)?.widgetVisibilityDidChange()
                    })) {
                    ForEach(WidgetVisibility.allCases) { Text($0.label).tag($0) }
                }
                Toggle("Show the widget now", isOn: Binding(
                    get: { !panelHidden },
                    set: { show in
                        let app = NSApp.delegate as? AppDelegate
                        if show { app?.showPanel() } else { app?.hidePanel() }
                    }))
                Text("Showing or hiding the widget by hand lasts until Desk turns on or off or Sanduhr starts again; then the choice above takes over. Sanduhr keeps fetching and alerting with every surface off. Every setting stays here, and Option+S opens this window while Desk is on.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Toggle("Open Sanduhr at login", isOn: $atLogin)
                    .onChange(of: atLogin) { _, on in
                        do { if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() } }
                        catch { atLogin = SMAppService.mainApp.status == .enabled }
                    }
            }
            menuBarSection
            Section("Calendar and Claude") {
                Toggle("Read today's meetings", isOn: $showMeetings)
                    .onChange(of: showMeetings) { _, on in
                        let desk = DeskController.shared
                        if on, desk.running { desk.model.requestCalendar() }
                        if !on { desk.model.meetings = [] }
                    }
                Toggle("Show the Claude meters on the desktop", isOn: $showClaude)
            }
            Section("Shortcuts") {
                Toggle("Option+J joins the next meeting, Option+S opens these settings", isOn: $hotKeys)
                    .onChange(of: hotKeys) { _, _ in DeskController.shared.applyHotKeys() }
                Text("Work in every app while Desk is on. While they are on, Option+J and Option+S no longer type ∆ and ß.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                HStack {
                    Button("Quit Sanduhr für Claude") { NSApp.terminate(nil) }
                    Spacer()
                    Text("Sanduhr \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Text("Quitting closes everything: the widget, Desk and the notch. To put away only the widget, use Hide Widget in any Sanduhr menu or the widget's close button.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    /// The percent beside the hourglass and the Desk menu's icon.
    private var menuBarSection: some View {
        Section("Menu bar") {
            Picker("Percent beside the hourglass", selection: $menuBarMode) {
                ForEach(MenuBarMode.allCases) { Text($0.label).tag($0) }
            }
            .onChange(of: menuBarMode) { _, _ in
                (NSApp.delegate as? AppDelegate)?.menuBarModeDidChange()
            }
            Text("Only the session and the weekly all-models limit show here; Rotate switches between them every 8 seconds (S for session, W for weekly).")
                .font(.caption).foregroundStyle(.secondary)
            Toggle("Desk menu in the menu bar (meetings, join, settings)", isOn: $menuIcon)
                .onChange(of: menuIcon) { _, on in DeskController.shared.setMenuIcon(on) }
        }
    }
}

// MARK: - Shared

private func slider(_ title: String, _ value: Binding<Double>, _ range: ClosedRange<Double>) -> some View {
    HStack {
        Text(title)
        Slider(value: value, in: range, step: 1)
        Text("\(Int(value.wrappedValue))")
            .font(.system(.body, design: .monospaced))
            .frame(width: 40, alignment: .trailing)
    }
}
