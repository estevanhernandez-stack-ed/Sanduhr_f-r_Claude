import SwiftUI
import AppKit

/// The desktop layout engine. Four slots, one per corner; each widget lives in one slot and
/// widgets in the same slot stack in the order listed. The two slots on a side share one
/// column with a spacer between them, so a growing meeting list pushes against the message
/// instead of drawing over it, and the message shrinks to fit before anything overlaps.
///
/// Settings live in the com.626labs.sanduhr.desk defaults domain:
///   defaults write com.626labs.sanduhr.desk layout "message:tl clock:bl claude:bl meetings:bl"
///       widgets: clock (time and date), claude (Sanduhr line), meters (a bar per limit),
///                meetings, message
///       slots:   tl tr bl br; leave a widget out to hide it
///   defaults write com.626labs.sanduhr.desk font "EsteFont"       (any installed font family)
///   defaults write com.626labs.sanduhr.desk timeSize -float 112    (clock size; the rest scales from it)
///   defaults write com.626labs.sanduhr.desk messageSize -float 84
///   defaults write com.626labs.sanduhr.desk messageColor 9ad7ff    (hex, or "5b8cff,a86bff" for a gradient)
///   defaults write com.626labs.sanduhr.desk message "text"         (pin one line; see Message.swift)
///   defaults write com.626labs.sanduhr.desk left -float 52         (edge margins in points;
///   defaults write com.626labs.sanduhr.desk right -float 52         top is measured below the menu bar)
///   defaults write com.626labs.sanduhr.desk top -float 40
///   defaults write com.626labs.sanduhr.desk bottom -float 60
///   defaults write com.626labs.sanduhr.desk menuIcon -bool true    (bring back the clock menu)
/// The older showMeetings / showClaude switches still hide those widgets (showClaude hides
/// both the line and the meters).
/// Then quit and reopen Desk. Messages themselves: edit
/// ~/Library/Application Support/Desk/messages.txt (no restart needed).
struct DeskView: View {
    var model: DeskModel

    @AppStorage("layout", store: .desk) private var layout = "message:tl clock:bl claude:bl meetings:bl"
    @AppStorage("font", store: .desk) private var font = ""
    @AppStorage("messageFont", store: .desk) private var messageFont = ""
    @AppStorage("left", store: .desk) private var left = 52.0
    @AppStorage("right", store: .desk) private var right = 52.0
    @AppStorage("top", store: .desk) private var top = 40.0
    @AppStorage("bottom", store: .desk) private var bottom = 60.0
    @AppStorage("timeSize", store: .desk) private var timeSize = 112.0
    @AppStorage("messageSize", store: .desk) private var messageSize = 84.0
    @AppStorage("messageColor", store: .desk) private var messageColor = "9ad7ff"
    @AppStorage("showMeetings", store: .desk) private var showMeetings = true
    @AppStorage("showClaude", store: .desk) private var showClaude = true
    @AppStorage("inkColor", store: .desk) private var ink = "ffffff"

    enum Widget: String { case clock, claude, meters, meetings, message }
    enum Slot: String { case tl, tr, bl, br }

    private var inset: CGFloat { timeSize * 0.18 }

    /// Parses the layout string. Unknown words are skipped, so a typo hides one widget
    /// instead of blanking the desktop.
    private var placement: [Slot: [Widget]] {
        var out: [Slot: [Widget]] = [:]
        for item in layout.split(separator: " ") {
            let bits = item.split(separator: ":").map(String.init)
            guard bits.count == 2, let w = Widget(rawValue: bits[0]), let slot = Slot(rawValue: bits[1]) else { continue }
            if w == .meetings && !showMeetings { continue }
            if (w == .claude || w == .meters) && !showClaude { continue }
            out[slot, default: []].append(w)
        }
        return out
    }

    var body: some View {
        let place = placement
        ZStack(alignment: .topLeading) {
        GeometryReader { geo in
            let colWidth = max(200, geo.size.width * 0.46)
            HStack(alignment: .top, spacing: 0) {
                column(top: place[.tl] ?? [], bottom: place[.bl] ?? [], alignment: .leading)
                    .frame(maxWidth: colWidth, alignment: .leading)
                Spacer(minLength: 24)
                column(top: place[.tr] ?? [], bottom: place[.br] ?? [], alignment: .trailing)
                    .frame(maxWidth: colWidth, alignment: .trailing)
            }
            // Handwritten glyphs reach past their own boxes (the left curve of an 8, say), and
            // shadows draw inside those boxes, clipping them. Inner breathing room fixes that;
            // the outer padding gives it back so the margins still mean the visible edge.
            .padding(inset)
            .padding(.leading, max(0, left - inset))
            .padding(.trailing, max(0, right - inset))
            .padding(.top, max(0, model.topInset + top - inset))
            .padding(.bottom, max(0, bottom - inset))
        }
        NotchView(model: model)
        }
    }

    private func column(top: [Widget], bottom: [Widget], alignment: HorizontalAlignment) -> some View {
        VStack(alignment: alignment, spacing: 10) {
            ForEach(top, id: \.self) { view(for: $0, alignment: alignment) }
            Spacer(minLength: 32)
            ForEach(bottom, id: \.self) { view(for: $0, alignment: alignment) }
        }
        .frame(maxHeight: .infinity)
    }

    @ViewBuilder
    private func view(for widget: Widget, alignment: HorizontalAlignment) -> some View {
        switch widget {
        case .clock: clock(alignment: alignment).deskInk()
        case .claude: claude.deskInk()
        case .meters: meters(alignment: alignment).deskInk()
        case .meetings: meetings(alignment: alignment).deskInk()
        case .message: message(alignment: alignment)
        }
    }

    private func clock(alignment: HorizontalAlignment) -> some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            VStack(alignment: alignment, spacing: 2) {
                Text(Self.format(context.date, "h:mm"))
                    .font(.custom(font, size: timeSize))
                Text(Self.format(context.date, "EEEE, MMMM d"))
                    .font(.custom(font, size: timeSize * 0.3))
                    .opacity(0.85)
            }
        }
    }

    @ViewBuilder
    private var claude: some View {
        if let line = model.claudeLine {
            Text(line)
                .font(.custom(font, size: timeSize * 0.19))
                .opacity(model.claudeLineIsStale ? 0.45 : 0.75)
        }
    }

    /// A bar per Claude limit, in the widget's order: label and percent over a bar in the ink,
    /// a pace tick where the widget puts its own, and the reset time underneath.
    @ViewBuilder
    private func meters(alignment: HorizontalAlignment) -> some View {
        if !model.meters.isEmpty || model.signInNeeded {
            let size = timeSize * 0.17
            VStack(alignment: alignment, spacing: size * 0.6) {
                ForEach(model.meters) { row in
                    MeterRow(row: row, ink: ink, font: font, size: size, width: timeSize * 3.2, alignment: alignment)
                        .deskPulse(model.pulses[row.tier] ?? 0, ink: ink, size: size)
                }
                if model.signInNeeded {
                    Text("sign in again in Sanduhr").opacity(0.75)
                }
                if model.meterHintVisible && !model.meters.isEmpty {
                    Text(DeskMeterHint.text)
                        .font(.custom(font, size: size * 0.75))
                        .multilineTextAlignment(alignment == .trailing ? .trailing : .leading)
                        .frame(maxWidth: timeSize * 3.2, alignment: alignment == .trailing ? .trailing : .leading)
                        .opacity(0.7)
                        .onAppear { model.meterHintShown() }
                }
            }
            .font(.custom(font, size: size))
            .opacity(model.claudeLineIsStale ? 0.5 : 1)
            // Clickable like a meeting row: the click itself is handled in DeskController, which
            // shows the widget beside the meters.
            .contentShape(Rectangle())
            .onHover { inside in
                if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
            }
            .background(GeometryReader { geo in
                Color.clear.preference(key: MetersFrameKey.self, value: geo.frame(in: .global))
            })
            .onPreferenceChange(MetersFrameKey.self) { frame in
                model.metersFrame = frame
            }
            .onDisappear { model.metersFrame = .zero }
        }
    }

    private func meetings(alignment: HorizontalAlignment) -> some View {
        VStack(alignment: alignment, spacing: 2) {
            if let note = model.calendarNote {
                Text(note).opacity(0.6)
            } else if model.meetings.isEmpty {
                Text("Nothing else on the calendar today").opacity(0.6)
            } else {
                ForEach(model.meetings) { meeting in
                    MeetingRow(meeting: meeting)
                }
            }
        }
        .font(.custom(font, size: timeSize * 0.21))
        .background(GeometryReader { geo in
            Color.clear.preference(key: MeetingsFrameKey.self, value: geo.frame(in: .global))
        })
        .onPreferenceChange(MeetingsFrameKey.self) { frame in
            model.meetingsFrame = frame
        }
        .onPreferenceChange(RowFramesKey.self) { frames in
            model.rowFrames = frames
        }
    }

    /// The handwritten line, drawn the way handwritten.py baked it: colored ink with a soft
    /// glow in the same color. messageColor takes one hex, or two or more separated by commas
    /// for a left-to-right gradient (to match Ice's menu bar tint). Two lines at most, then it
    /// scales down rather than collide.
    @ViewBuilder
    private func message(alignment: HorizontalAlignment) -> some View {
        if let text = model.message {
            let colors = Color.inkStops(messageColor, fallback: "9ad7ff")
            let glow = colors[colors.count / 2]
            Text(text)
                .font(.custom(messageFont.isEmpty ? font : messageFont, size: messageSize))
                .multilineTextAlignment(alignment == .trailing ? .trailing : .leading)
                .lineLimit(2)
                .minimumScaleFactor(0.4)
                .foregroundStyle(LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
                .opacity(0.94)
                .shadow(color: glow.opacity(0.55), radius: messageSize * 0.18)
                .shadow(color: glow.opacity(0.35), radius: messageSize * 0.18)
        }
    }

    private static func format(_ date: Date, _ pattern: String) -> String {
        let f = DateFormatter()
        f.dateFormat = pattern
        return f.string(from: date)
    }
}

private extension View {
    /// The Desk ink: inkColor (one hex, or several for a gradient) with a dark drop shadow,
    /// readable on any wallpaper.
    func deskInk() -> some View { modifier(DeskInk()) }
}

extension View {
    /// A soft glow in the ink behind the view, three times over about three seconds, each time
    /// `trigger` goes up: an alert delivered to the Desk.
    func deskPulse(_ trigger: Int, ink: String, size: CGFloat) -> some View {
        phaseAnimator(DeskPulse.phases, trigger: trigger) { view, phase in
            view.background(
                RoundedRectangle(cornerRadius: size * 0.6)
                    .fill(LinearGradient.ink(ink))
                    .padding(-size * 0.5)
                    .blur(radius: size * 0.6)
                    .opacity(0.35 * DeskPulse.intensity(phase)))
        } animation: { _ in DeskPulse.animation }
    }
}

/// The pulse's timing, shared by the meters and the notch island: seven steps, lit on the odd
/// ones, so three glows and back to rest.
enum DeskPulse {
    static let phases = Array(0..<7)
    static func intensity(_ phase: Int) -> Double { phase % 2 == 1 ? 1 : 0 }
    static let animation = Animation.easeInOut(duration: 0.42)
}

private struct DeskInk: ViewModifier {
    @AppStorage("inkColor", store: .desk) private var ink = "ffffff"
    @AppStorage("inkShadow", store: .desk) private var shadow = true
    func body(content: Content) -> some View {
        content
            .foregroundStyle(LinearGradient.ink(ink))
            .opacity(0.92)
            .shadow(color: .black.opacity(shadow ? 0.45 : 0), radius: 10, x: 0, y: 2)
    }
}

extension LinearGradient {
    /// "ffffff" is one color; "531b93,012089,00fdff" runs left to right.
    static func ink(_ spec: String, fallback: String = "ffffff") -> LinearGradient {
        LinearGradient(colors: Color.inkStops(spec, fallback: fallback), startPoint: .leading, endPoint: .trailing)
    }
}

extension Color {
    /// The gradient stops for an ink spec, always two or more: one color is doubled, an empty
    /// spec falls back. Spaces around the commas are fine ("531b93, 012089").
    static func inkStops(_ spec: String, fallback: String) -> [Color] {
        var colors = spec.split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
            .map { Color.hex($0) }
        if colors.isEmpty { colors = [Color.hex(fallback)] }
        if colors.count == 1 { colors.append(colors[0]) }
        return colors
    }
}


/// One Desk meter: "Session (5hr)   42%" over the bar, the reset time under it. The fill is
/// clamped to the bar; the percent says how far past 100 a limit went.
private struct MeterRow: View {
    let row: DeskMeterRow
    let ink: String
    let font: String
    let size: CGFloat
    let width: CGFloat
    let alignment: HorizontalAlignment

    var body: some View {
        let barHeight = max(4, size * 0.38)
        VStack(alignment: alignment, spacing: size * 0.22) {
            HStack(alignment: .firstTextBaseline) {
                Text(row.label).lineLimit(1).opacity(0.85)
                Spacer(minLength: size)
                Text("\(row.percent)%")
            }
            ZStack(alignment: .leading) {
                Capsule().fill(LinearGradient.ink(ink)).opacity(0.22)
                Capsule().fill(LinearGradient.ink(ink))
                    .frame(width: width * row.fill)
            }
            .frame(width: width, height: barHeight)
            .overlay(alignment: .leading) {
                // Taller than the bar, so it still reads where it crosses the fill.
                if let pace = row.pace {
                    Rectangle()
                        .fill(LinearGradient.ink(ink))
                        .frame(width: 2, height: barHeight * 2)
                        .offset(x: max(0, min(width - 2, pace * width)))
                }
            }
            if !row.reset.isEmpty {
                Text("resets \(row.reset)")
                    .font(.custom(font, size: size * 0.8))
                    .opacity(0.7)
            }
        }
        .frame(width: width)
    }
}

/// One meeting line. With a join link it is clickable: the pointer turns into a hand and a
/// click opens the meeting in Teams, Zoom or the browser.
private struct MeetingRow: View {
    let meeting: Meeting
    @State private var hovering = false

    var body: some View {
        HStack(spacing: 10) {
            Text("\(meeting.time)   \(meeting.title)")
            if let service = meeting.service {
                Text(hovering ? "join \(service)" : service)
                    .underline(hovering)
                    .opacity(hovering ? 0.95 : 0.55)
            }
        }
        .contentShape(Rectangle())
        .background(GeometryReader { geo in
            Color.clear.preference(key: RowFramesKey.self, value: [meeting.id: geo.frame(in: .global)])
        })
        // The click itself is handled in AppDelegate, which sees it even when macOS hands
        // the first click to the desktop instead of this window.
        .onHover { inside in
            guard meeting.link != nil else { return }
            hovering = inside
            if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
        }
    }
}

private struct RowFramesKey: PreferenceKey {
    static let defaultValue: [String: CGRect] = [:]
    static func reduce(value: inout [String: CGRect], nextValue: () -> [String: CGRect]) {
        value.merge(nextValue()) { _, new in new }
    }
}

private struct MetersFrameKey: PreferenceKey {
    static let defaultValue: CGRect = .zero
    static func reduce(value: inout CGRect, nextValue: () -> CGRect) { value = nextValue() }
}

private struct MeetingsFrameKey: PreferenceKey {
    static let defaultValue: CGRect = .zero
    static func reduce(value: inout CGRect, nextValue: () -> CGRect) { value = nextValue() }
}
