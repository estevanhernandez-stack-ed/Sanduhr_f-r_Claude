import AppKit
import SwiftUI

/// Item 75, slice B: the Mod Studio, a sheet for working on Sanduhr's meters mod. The band drawn
/// by Sanduhr with demo numbers (BandFrame, the mod's own rules in Swift) beside the looks that
/// shape it; nothing is written until Save. Never reads a real snapshot, so it is safe to show and
/// works signed out.

/// What the preview shows (the sheet's "Show" buttons): demo numbers, and the effect that goes with them.
enum StudioMoment: String, CaseIterable, Identifiable, Sendable {
    case calm, near, crossing, beforeReset, nearlyFull, watchers

    var id: String { rawValue }

    var title: String {
        switch self {
        case .calm: "Calm"
        case .near: "Near a line"
        case .crossing: "Crossing"
        case .beforeReset: "Before reset"
        case .nearlyFull: "Nearly full"
        case .watchers: "Watchers"
        }
    }

    var help: String {
        switch self {
        case .calm: "Plenty left, nothing moving."
        case .near: "The session meter past 75%: orange."
        case .crossing: "A meter crossing a warning line sweeps its bar once. Replays every few seconds."
        case .beforeReset: "A limit less than ten minutes from its reset shimmers its reset words."
        case .nearlyFull: "A limit at 90% or more, with its reset still far off, glows with a warning mark."
        case .watchers: "Watchers above the prompt: one running, one waiting on you."
        }
    }

    private static let minute = 60_000.0, hour = 60 * minute, day = 24 * hour

    /// (session %, its reset from now, weekly %, its reset from now).
    private var numbers: (Int, Double, Int, Double) {
        switch self {
        case .calm, .watchers: (34, 3 * Self.hour + 12 * Self.minute, 21, 4 * Self.day + 2 * Self.hour)
        case .near, .crossing: (78, 2 * Self.hour + 14 * Self.minute, 52, 2 * Self.day)
        case .beforeReset: (62, 6 * Self.minute, 40, 5 * Self.day)
        case .nearlyFull: (93, 2 * Self.hour, 61, 3 * Self.day)
        }
    }

    /// The demo snapshot at `now`: the resets stay the same distance away while the moment plays.
    func snapshot(now: Double) -> BandFrame.Snapshot {
        let (s, sIn, w, wIn) = numbers
        return .reading(capturedAt: now - Self.minute, isError: false, errorKind: nil, tiers: [
            .init(key: BandFrame.session, utilization: s, resetsAt: now + sIn),
            .init(key: BandFrame.weekly, utilization: w, resetsAt: now + wIn),
        ])
    }

    /// Demo watchers, or none.
    func watchers(now: Double, startedAt: Double) -> [BandFrame.Watcher]? {
        guard self == .watchers else { return nil }
        return [
            .agent(title: "Run the test suite", short: "tests", state: .running, done: 3, total: 12,
                   startedAt: startedAt - 4 * Self.minute, endedAt: nil),
            .agent(title: "Approve the deploy", short: "deploy", state: .waiting, done: nil, total: nil,
                   startedAt: startedAt - 70 * Self.minute, endedAt: nil),
        ]
    }

    /// The crossing sweep replays every three seconds.
    func sweeps(now: Double, startedAt: Double) -> [String: Double] {
        guard self == .crossing else { return [:] }
        let cycle = 3000.0
        return [BandFrame.session: startedAt + ((now - startedAt) / cycle).rounded(.down) * cycle + 300]
    }
}

/// The Studio's state: the moment playing, the preview's terminal, and the looks being drafted.
@MainActor
@Observable
final class ModStudioModel {
    static let segments: [SanduhrSegment] = [.session, .weekly, .resets]

    var moment: StudioMoment = .calm { didSet { startedAt = Date() } }
    private(set) var startedAt = Date()
    var columns = 100
    var reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    var full = false
    var draft: [SanduhrSegment: SegmentStyle]
    private(set) var saved: [SanduhrSegment: SegmentStyle]
    @ObservationIgnored private let write: ([SanduhrSegment: SegmentStyle]) -> Void

    init(saved: [SanduhrSegment: SegmentStyle], write: @escaping ([SanduhrSegment: SegmentStyle]) -> Void) {
        let kept = saved.filter { Self.segments.contains($0.key) && !$0.value.isEmpty }
        self.saved = kept
        self.draft = kept
        self.write = write
    }

    /// The Studio on the looks Sanduhr keeps for the band.
    static func live() -> ModStudioModel {
        ModStudioModel(saved: BandFileWriter.shared.meterStyles) { BandFileWriter.shared.setMeterStyles($0) }
    }

    var isDirty: Bool { normalized(draft) != saved }

    func style(_ s: SanduhrSegment) -> SegmentStyle { draft[s] ?? SegmentStyle() }

    func setStyle(_ style: SegmentStyle, for s: SanduhrSegment) {
        draft[s] = style.isEmpty ? nil : style
    }

    func save() {
        let kept = normalized(draft)
        write(kept)
        saved = kept
        draft = kept
    }

    func revert() { draft = saved }

    func replay() { startedAt = Date() }

    private func normalized(_ d: [SanduhrSegment: SegmentStyle]) -> [SanduhrSegment: SegmentStyle] {
        d.filter { Self.segments.contains($0.key) && !$0.value.isEmpty }
    }

    /// band.json as the draft would write it, read back the way the mod reads it.
    var previewFile: BandFrame.BandFile {
        var file = BandFrame.BandFile(writtenAt: Date().timeIntervalSince1970 * 1000)
        for s in Self.segments {
            if let style = draft[s], !style.isEmpty, let look = BandFrame.parseStyle(style.json) {
                file.styles[s.rawValue] = look
            }
        }
        return file
    }

    /// The preview's lines at `date`.
    func lines(at date: Date) -> [[BandFrame.Cell]] {
        let now = date.timeIntervalSince1970 * 1000
        let start = startedAt.timeIntervalSince1970 * 1000
        var file = previewFile
        file.watchers = moment.watchers(now: now, startedAt: start)
        let input = BandFrame.Input(
            now: now, bars: .both, style: full ? .full : .compact, label: "", moving: !reduceMotion,
            width: columns, maxRows: 6, tzOffsetMinutes: -TimeZone.current.secondsFromGMT(for: date) / 60,
            seat: false, sweeps: moment.sweeps(now: now, startedAt: start))
        return BandFrame.lines(moment.snapshot(now: now), file, input)
    }
}

/// The band as a terminal draws it: monospaced cells on a dark ground, over a stand-in prompt.
struct BandPreview: View {
    var model: ModStudioModel

    static let ground = Color.hex("16161e")
    static let ink = Color.hex("d4d4d8")

    var body: some View {
        TimelineView(.animation(minimumInterval: 0.08, paused: model.reduceMotion && model.moment != .watchers)) { context in
            let lines = model.lines(at: context.date)
            VStack(alignment: .leading, spacing: 3) {
                ForEach(Array(lines.enumerated()), id: \.offset) { _, line in
                    Text(Self.attributed(line)).lineLimit(1).fixedSize()
                }
                Text(String(repeating: "─", count: model.columns))
                    .foregroundStyle(Self.ink.opacity(0.25)).lineLimit(1).fixedSize()
                Text("> ").foregroundStyle(Self.ink.opacity(0.6))
            }
            .font(.system(size: 12, design: .monospaced))
            .padding(12)
            .frame(maxWidth: .infinity, minHeight: 150, alignment: .topLeading)
        }
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Self.ground))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Preview of the meters above the prompt")
        .accessibilityValue(accessibilityText)
    }

    private var accessibilityText: String {
        model.lines(at: Date()).map { $0.map(\.text).joined() }.joined(separator: ", ")
    }

    static func attributed(_ line: [BandFrame.Cell]) -> AttributedString {
        var out = AttributedString()
        for c in line {
            var a = AttributedString(c.text)
            var color = c.color.map(Color.hex) ?? ink
            if c.dim { color = color.opacity(0.5) }
            a.foregroundColor = color
            var font = Font.system(size: 12, design: .monospaced)
            if c.bold { font = font.bold() }
            if c.italic { font = font.italic() }
            a.font = font
            if c.underline { a.underlineStyle = .single }
            out += a
        }
        return out
    }
}

/// The sheet: the preview and its moments on top, the looks below, Save and Revert at the foot.
struct ModStudioSheet: View {
    @State var model: ModStudioModel
    /// "On in 2 of 3 folders", or nil before the folders are read.
    let onLine: String?
    let close: () -> Void
    @State private var askingToClose = false
    @State private var styling: SanduhrSegment?

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            BandPreview(model: model)
            moments
            terminal
            Divider()
            look
            Spacer(minLength: 0)
            footer
        }
        .padding(20)
        .frame(minWidth: 760, idealWidth: 860, minHeight: 560, idealHeight: 620)
        .confirmationDialog("Save the band's new look?", isPresented: $askingToClose) {
            Button("Save") { model.save(); close() }
            Button("Discard Changes", role: .destructive) { close() }
            Button("Keep Editing", role: .cancel) {}
        } message: {
            Text("Saved looks show in every Claude Code session's band right away.")
        }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Mod Studio: \(IntegrationScripts.modName)").font(.title3.weight(.semibold))
                Text("Drawn by Sanduhr with demo numbers, by the same rules a session draws the band.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if let onLine { Text(onLine).font(.caption).foregroundStyle(.secondary) }
        }
    }

    private var moments: some View {
        HStack(spacing: 6) {
            Text("Show").font(.caption).foregroundStyle(.secondary)
            ForEach(StudioMoment.allCases) { m in
                Button(m.title) { if model.moment == m { model.replay() } else { model.moment = m } }
                    .buttonStyle(.bordered)
                    .tint(model.moment == m ? .accentColor : nil)
                    .controlSize(.small)
                    .help(m.help)
                    .accessibilityAddTraits(model.moment == m ? .isSelected : [])
            }
        }
    }

    private var terminal: some View {
        HStack(spacing: 16) {
            HStack(spacing: 6) {
                Text("Width").font(.caption).foregroundStyle(.secondary)
                Slider(value: Binding(get: { Double(model.columns) }, set: { model.columns = Int($0) }), in: 50...140, step: 1)
                    .frame(width: 160)
                    .accessibilityLabel("Terminal width")
                    .accessibilityValue("\(model.columns) columns")
                Text("\(model.columns) columns").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }
            Toggle("Full style", isOn: $model.full)
                .help("A row per meter with pace words, as the mod's Full style draws it. It folds to one line in a terminal narrower than 64 columns.")
            Toggle("Reduce Motion", isOn: $model.reduceMotion)
                .help("The band honors macOS's Reduce Motion. Flip it here to see the band still; this changes the preview only.")
        }
        .toggleStyle(.checkbox)
        .controlSize(.small)
    }

    private var look: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Look").font(.headline)
            Text("The band's looks are one setting for every folder, the same ones the statusline's Combine sheet sets. Click the brush to style a segment.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ForEach(ModStudioModel.segments, id: \.self) { s in
                HStack(spacing: 10) {
                    Text(s == .resets ? "Resets" : s.title).frame(width: 70, alignment: .leading)
                    Button { styling = s } label: { Image(systemName: model.style(s).isEmpty ? "paintbrush" : "paintbrush.fill") }
                        .buttonStyle(.borderless)
                        .help("Style \(s == .resets ? "the reset words" : s.title)")
                        .accessibilityLabel("Style \(s == .resets ? "Resets" : s.title)")
                        .popover(isPresented: Binding(get: { styling == s }, set: { if !$0 { styling = nil } })) {
                            StylePopover(name: s == .resets ? "Resets" : s.title,
                                         style: Binding(get: { model.style(s) }, set: { model.setStyle($0, for: s) }))
                        }
                    Text(summary(model.style(s))).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }

    private func summary(_ style: SegmentStyle) -> String {
        guard !style.isEmpty else { return "Its own look" }
        var parts: [String] = []
        if style.ink.count == 1 { parts.append("one color") } else if style.ink.count > 1 { parts.append("a gradient of \(style.ink.count)") }
        if let f = style.font { parts.append(f.title.lowercased()) }
        if style.bold { parts.append("bold") }
        if style.italic { parts.append("italic") }
        if style.dim { parts.append("dim") }
        if style.underline { parts.append("underlined") }
        return parts.joined(separator: ", ").prefix(1).uppercased() + parts.joined(separator: ", ").dropFirst()
    }

    private var footer: some View {
        HStack {
            Text(model.isDirty ? "Not saved. Save shows the new look in every session's band." : "Saved looks show in every session's band.")
                .font(.caption).foregroundStyle(.secondary)
            Spacer()
            Button("Revert") { model.revert() }.disabled(!model.isDirty)
            Button("Save") { model.save() }.disabled(!model.isDirty).keyboardShortcut("s")
            Button("Done") { if model.isDirty { askingToClose = true } else { close() } }
                .keyboardShortcut(.defaultAction)
        }
    }
}
