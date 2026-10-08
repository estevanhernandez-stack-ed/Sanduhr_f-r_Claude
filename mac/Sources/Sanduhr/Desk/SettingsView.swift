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

    @AppStorage(DeskController.enabledKey, store: .desk) private var deskOn = false

    var body: some View {
        let arranging = DeskController.shared.model.arrange.active
        Form {
            Section("On the desktop") {
                DeskArrangeRow(deskOn: deskOn, arranging: arranging)
            }
            Section("Where each piece sits") {
                ForEach(DeskLayout.widgets, id: \.key) { w in
                    DeskPlaceRow(widget: w.key, name: w.name, layout: $layout)
                }
                .disabled(arranging)
                Text("Eight places: the four corners, the top and bottom centers, and the middle of each side. Top center sits below the notch. Size scales a piece from 60% to 160%; the message starts from its own size in Look. Now playing shows only while something plays; its other settings are in Now Playing. Watchers show only while there is one; they are switched on in Integrations.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Order") {
                DeskOrderList(layout: $layout)
                    .disabled(arranging)
                Text("Pieces in the same place stack top to bottom in this order. Drag a piece up or down to reorder it, or onto a piece in another place to move it there. The top, middle and bottom of a side share a column, and a side keeps clear of the centers, so places never overlap.")
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
}

/// Layout's Arrange Desk… button (item 60). While arranging, the places and the order here wait:
/// Done on the desktop writes the layout, so an edit here would be overwritten.
private struct DeskArrangeRow: View {
    let deskOn: Bool
    let arranging: Bool

    var body: some View {
        LabeledContent {
            Button(DeskArrangeCopy.settingsButton) { DeskController.shared.arrangeDesk() }
                .disabled(!deskOn || arranging)
        } label: {
            Text(arranging ? DeskArrangeCopy.settingsArranging
                 : deskOn ? DeskArrangeCopy.settingsNote : DeskArrangeCopy.settingsDeskOff)
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// One piece's row in Layout: where it sits (or Hidden) and its size.
private struct DeskPlaceRow: View {
    let widget: String
    let name: String
    @Binding var layout: String

    var body: some View {
        let placed = DeskArrangement(layout).placement(widget)
        LabeledContent(name) {
            HStack(spacing: 8) {
                Picker("\(name) place", selection: anchor) {
                    ForEach(DeskAnchor.allCases, id: \.self) { Text($0.name).tag($0.rawValue) }
                    Divider()
                    Text("Hidden").tag("")
                }
                .labelsHidden()
                .fixedSize()
                Picker("\(name) size", selection: scale) {
                    ForEach(DeskArrangement.scaleSteps, id: \.self) { Text(DeskPlaceRow.percent($0)).tag($0) }
                }
                .labelsHidden()
                .fixedSize()
                .disabled(placed == nil)
            }
        }
    }

    /// "120%".
    static func percent(_ scale: Double) -> String { "\(Int((scale * 100).rounded()))%" }

    private var anchor: Binding<String> {
        Binding(
            get: { DeskArrangement(layout).placement(widget)?.anchor.rawValue ?? "" },
            set: { layout = DeskLayout.placing(widget, in: $0, layout: layout) })
    }

    private var scale: Binding<Double> {
        Binding(
            get: { DeskArrangement(layout).placement(widget)?.scale ?? 1 },
            set: { value in
                var a = DeskArrangement(layout)
                a.setScale(widget, value)
                layout = a.string
            })
    }
}

/// Layout's Order list: each place that has pieces, its pieces top to bottom. A piece drags
/// onto another to take its place (DeskArrangement.move); VoiceOver gets Move Up and Move Down.
private struct DeskOrderList: View {
    @Binding var layout: String

    var body: some View {
        let a = DeskArrangement(layout)
        let anchors = DeskAnchor.allCases.filter { !a.stack($0).isEmpty }
        if anchors.isEmpty {
            Text("Nothing is placed on the Desk.").foregroundStyle(.secondary)
        }
        ForEach(anchors, id: \.self) { anchor in
            VStack(alignment: .leading, spacing: 4) {
                Text(anchor.name).font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                ForEach(a.stack(anchor), id: \.widget) { p in
                    DeskOrderRow(placement: p, layout: $layout)
                }
            }
        }
    }
}

private struct DeskOrderRow: View {
    let placement: DeskPlacement
    @Binding var layout: String
    @State private var targeted = false

    private var name: String {
        DeskLayout.widgets.first { $0.key == placement.widget }?.name ?? placement.widget
    }

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "line.3.horizontal").foregroundStyle(.secondary)
            Text(name)
            Spacer()
            if placement.scale != 1 {
                Text(DeskPlaceRow.percent(placement.scale)).foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 3)
        .padding(.horizontal, 6)
        .background(RoundedRectangle(cornerRadius: 5)
            .fill(Color.accentColor.opacity(targeted ? 0.25 : 0.06)))
        .contentShape(Rectangle())
        .draggable(placement.widget) { Text(name).padding(4) }
        .dropDestination(for: String.self) { items, _ in
            guard let dragged = items.first else { return false }
            change { $0.move(dragged, onto: placement.widget) }
            return true
        } isTargeted: { targeted = $0 }
        .accessibilityElement(children: .combine)
        .accessibilityHint("Drag to reorder")
        .accessibilityAction(named: "Move Up") { change { $0.move(placement.widget, by: -1) } }
        .accessibilityAction(named: "Move Down") { change { $0.move(placement.widget, by: 1) } }
    }

    private func change(_ edit: (inout DeskArrangement) -> Void) {
        var a = DeskArrangement(layout)
        edit(&a)
        layout = a.string
    }
}

/// The layout string the Layout section edits ("message:tl clock:bl claude:bl meetings:bl"), kept
/// apart from the view so it tests without AppKit. DeskView reads the same string. Now playing
/// (item 53b) is an element like the others, off unless placed. The string itself (anchors,
/// order, sizes) is DeskArrangement (item 59); these are the shortcuts the rest of the app uses.
enum DeskLayout {
    /// The layout DeskView draws when none is saved.
    static let standard = "message:tl clock:bl claude:bl meetings:bl"

    static let widgets: [(key: String, name: String)] = [
        ("message", "Message"), ("clock", "Clock and date"), ("claude", "Claude line"),
        ("meters", "Claude meters (bars)"), ("nowPlaying", "Now playing"), ("watchers", "Watchers"),
        ("meetings", "Meetings"),
    ]

    /// Widget to anchor word, for the widgets this build knows. A repeated widget keeps its last
    /// word; an anchor this build does not know reads as the piece's default (DeskArrangement).
    static func parse(_ s: String) -> [String: String] {
        var out: [String: String] = [:]
        for p in DeskArrangement(s).pieces { out[p.widget] = p.anchor.rawValue }
        return out
    }

    /// The widgets DeskView draws for `layout`, less the meetings with the older showMeetings
    /// switch off and the claude line and meters with showClaude off.
    static func placed(_ layout: String, showMeetings: Bool = true, showClaude: Bool = true) -> Set<String> {
        Set(DeskArrangement(layout).shown(showMeetings: showMeetings, showClaude: showClaude).map(\.widget))
    }

    /// The layout with `widget` moved to the anchor `slot` ("" hides it). The rest keep their
    /// order and sizes (DeskArrangement.place).
    static func placing(_ widget: String, in slot: String, layout: String) -> String {
        var a = DeskArrangement(layout)
        a.place(widget, at: DeskAnchor(rawValue: slot))
        return a.string
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

    /// The Desk font as drawn (EsteFont Pro until one is picked, or when the picked one is gone);
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
            // EsteFont Pro, then EsteFont 26, first: they ship inside Sanduhr.
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

/// Settings, Message (item 69): Claude's suggestion card on top, then the line editor, a row per
/// line drawn as the Desk draws it (MessageEditorView.swift), or the file as text.
struct DeskMessageSection: View {
    var model: DeskModel
    @Bindable var editor: MessageEditorModel
    var handoff = DeskMessageHandoff.shared
    @AppStorage("messageRotate", store: .desk) private var rotate = "daily"
    @State private var reviewing = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                if let proposal = handoff.pending {
                    MessageSuggestionBanner(proposal: proposal, canAdd: !editor.unsaved,
                                            review: { reviewing = true },
                                            add: { handoff.approve() },
                                            dismiss: { handoff.dismiss() })
                }
                MessageEditorBar(editor: editor, saved: refreshDesk)
                if editor.mode == .list {
                    MessageListEditor(editor: editor, pinChanged: refreshDesk)
                } else {
                    MessageTextEditorPane(editor: editor, pinChanged: refreshDesk)
                }
                Divider()
                MessageAskClaude()
            }
            .padding(20)
        }
        .onAppear { editor.loadIfNeeded() }
        .onChange(of: rotate) { _, _ in refreshDesk() }
        .onChange(of: handoff.revision) { _, _ in
            if editor.unsaved { editor.fileChanged = true } else { editor.load() }
        }
        .sheet(isPresented: $reviewing) {
            if let proposal = handoff.pending {
                MessageSuggestionReview(proposal: proposal, canAdd: !editor.unsaved,
                                        add: { reviewing = false; handoff.approve() },
                                        dismiss: { reviewing = false; handoff.dismiss() },
                                        close: { reviewing = false })
            }
        }
    }

    /// The Desk picks today's line again.
    private func refreshDesk() {
        model.refreshMessage()
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
                Text("Nothing leaves that part plain black. A wing grows to fit its text. Watchers show the most urgent watcher while there is one (switched on in Integrations), else that place's default. Camera and mic shows the indicators below while one is in use, else that place's default.")
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
            AVIndicatorSection()
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

/// Notch, Camera and mic (item 67): when the red dot shows, the mic switch, the pulse and the side.
/// Its own view so the Notch page's body stays small enough for Swift 6.0 and 6.1 to type-check.
private struct AVIndicatorSection: View {
    @AppStorage(AVCameraDotMode.key, store: .desk) private var dot = AVCameraDotMode.never
    @AppStorage(AVIndicators.micKey, store: .desk) private var mic = false
    @AppStorage(AVIndicators.pulseKey, store: .desk) private var pulse = true
    @AppStorage(AVIndicatorSide.key, store: .desk) private var side = AVIndicatorSide.right

    var body: some View {
        Section("Camera and mic") {
            Picker("Show the red dot", selection: $dot) {
                ForEach(AVCameraDotMode.allCases) { Text($0.label).tag($0) }
            }
            Text("A MacBook's camera has its own green light; the dot is for cameras whose light you can't see: an external or Continuity camera, or the built-in one with the lid closed.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Toggle("Pulse the dot gently", isOn: $pulse)
                .disabled(dot == .never)
            Toggle("Show a mic while the microphone is on", isOn: $mic)
            Picker("Beside the camera", selection: $side) {
                ForEach(AVIndicatorSide.allCases) { Text($0.label).tag($0) }
            }
            .disabled(dot == .never && !mic)
            Text("Indicators only: Sanduhr sees that a camera or the microphone is in use, never which app or anything captured, and never mutes or changes a device. No permission is asked and nothing is saved. They sit on the island beside the camera, or where you pick Camera and mic above; with the island off, or on a screen without a notch, in a small tab at the top. Click one for what is in use. Needs Desk. With Reduce Motion the dot holds still and they come and go without a fade.")
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
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
