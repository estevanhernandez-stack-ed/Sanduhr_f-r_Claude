import SwiftUI
import AppKit

/// The desktop layout engine. Eight anchors (item 59): the four corners, the top and bottom
/// centers, and the middle of each side; each widget lives at one anchor, at its own size, and
/// widgets at the same anchor stack in the order listed. The top and bottom of a side share one
/// column with a spacer between them, so a growing meeting list pushes against the message
/// instead of drawing over it, and the message shrinks to fit before anything overlaps. A
/// side's middle sits in that column between its top and bottom, pushed by them the same way;
/// the centers share a column of their own between the sides, which then draw only in the room
/// it leaves (DeskColumnsLayout), the top center below the notch or the island. A layout with
/// corners only draws exactly as before.
///
/// Settings live in the com.626labs.sanduhr.desk defaults domain:
///   defaults write com.626labs.sanduhr.desk layout "message:tl clock:bl claude:bl meetings:bl"
///       widgets: clock (time and date), claude (Sanduhr line), meters (a bar per limit),
///                nowPlaying (what plays, item 53b), watchers (item 66), meetings, message
///       anchors: tl tc tr ml mr bl bc br; leave a widget out to hide it; an unknown anchor
///                puts the widget where the standard layout has it (message tl, the rest bl)
///       size:    a third part, 0.6 to 1.6 ("clock:bl:1.2"); none is 1 (DeskArrangement)
///   defaults write com.626labs.sanduhr.desk font "EsteFont Pro"   (any installed font family; EsteFont Pro and EsteFont 26 ship in the app, Pro is the default)
///   defaults write com.626labs.sanduhr.desk timeSize -float 112    (clock size; the rest scales from it)
///   defaults write com.626labs.sanduhr.desk messageSize -float 84
///   defaults write com.626labs.sanduhr.desk messageColor 9ad7ff    (hex, or "5b8cff,a86bff" for a gradient)
///   defaults write com.626labs.sanduhr.desk messageGlow -bool false (no glow; a line's {glow} still glows)
///   defaults write com.626labs.sanduhr.desk message "text"         (pin one line; see Message.swift)
///   defaults write com.626labs.sanduhr.desk left -float 52         (edge margins in points;
///   defaults write com.626labs.sanduhr.desk right -float 52         top is measured below the menu bar)
///   defaults write com.626labs.sanduhr.desk top -float 40
///   defaults write com.626labs.sanduhr.desk bottom -float 60
///   defaults write com.626labs.sanduhr.desk menuIcon -bool true    (bring back the clock menu)
/// showMeetings (Read today's meetings) still hides the meetings. 2.10.0's showClaude is retired:
/// SettingsMigrations moved it into the layout (both Claude meters pieces Hidden).
/// Then quit and reopen Desk. Messages themselves: edit
/// ~/Library/Application Support/Desk/messages.txt (no restart needed).
struct DeskView: View {
    var model: DeskModel

    @AppStorage("layout", store: .desk) private var layout = "message:tl clock:bl claude:bl meetings:bl"
    @AppStorage("left", store: .desk) private var left = 52.0
    @AppStorage("right", store: .desk) private var right = 52.0
    @AppStorage("top", store: .desk) private var top = 40.0
    @AppStorage("bottom", store: .desk) private var bottom = 60.0
    @AppStorage("timeSize", store: .desk) private var timeSize = 112.0
    @AppStorage("showMeetings", store: .desk) private var showMeetings = true

    @AppStorage(DeskController.notchKey, store: .desk) private var island = false
    @AppStorage("notchChin", store: .desk) private var chin = 26.0

    enum Widget: String, CaseIterable { case clock, claude, meters, nowPlaying, watchers, meetings, message }

    private var inset: CGFloat { timeSize * 0.18 }

    /// Each anchor's pieces, top to bottom (DeskArrangement). Unknown widgets are skipped, so a
    /// typo hides one widget instead of blanking the desktop; an unknown anchor falls back to
    /// the widget's default.
    /// While arranging (item 60), the layout being edited; nothing is saved until Done.
    private var stacks: [DeskAnchor: [DeskPlacement]] {
        (model.arrange.working ?? DeskArrangement(layout)).stacks(showMeetings: showMeetings)
    }

    var body: some View {
        let place = stacks
        let arranging = model.arrange.active
        let margins = DeskAnchorGeometry.margins(left: left, right: right, top: top, bottom: bottom,
                                                 topInset: model.topInset, dock: model.dockInsets, inset: inset)
        ZStack(alignment: .topLeading) {
        // Arrange mode: the whole screen takes clicks (DeskArrangePlate); outside it, only what is drawn.
        if arranging { DeskArrangePlate() }
        GeometryReader { geo in
            let colWidth = max(200, geo.size.width * 0.46)
            let lineInLayout = place.values.contains { $0.contains { $0.widget == Widget.claude.rawValue } }
            let anchors = arrangeGeometry(geo.size, margins: margins)
            columns(place, colWidth: colWidth, lineInLayout: lineInLayout, contentTop: margins.top + inset)
            // Handwritten glyphs reach past their own boxes (the left curve of an 8, say), and
            // shadows draw inside those boxes, clipping them. Inner breathing room fixes that;
            // the outer padding gives it back so the margins still mean the visible edge.
            .padding(inset)
            // The Dock's side moves in by its reach (item 56), so nothing sits under it: every
            // anchor, centers and middles too, is inside these margins.
            .padding(.leading, margins.leading)
            .padding(.trailing, margins.trailing)
            .padding(.top, margins.top)
            .padding(.bottom, margins.bottom)
            .environment(\.deskArrangeGeometry, arranging ? anchors : nil)
            if arranging { DeskArrangeOverlay(mode: model.arrange, geometry: anchors) }
        }
        NotchView(model: model)
        }
        .coordinateSpace(.named(DeskArrange.space))
    }

    /// The anchors' rectangle and the top center's drop, as the columns below lay them out.
    private func arrangeGeometry(_ size: CGSize, margins: DeskAnchorGeometry.Margins) -> DeskArrangeGeometry {
        let notch = DeskAnchorGeometry.notchBottom(notch: model.notchRect, island: island, chin: chin)
        return DeskArrangeGeometry(
            content: DeskAnchorGeometry.content(window: size, margins: margins, inset: inset),
            centerDrop: DeskAnchorGeometry.centerDrop(contentTop: margins.top + inset, notchBottom: notch))
    }

    /// The columns. Corners and middles only: the two side columns as before item 59, the
    /// spacer between them. With a piece at a center, the center column sits between the sides
    /// (DeskColumnsLayout) and the sides draw only in the room it leaves, so nothing at a side
    /// reaches a center piece: a long message wraps and shrinks instead.
    @ViewBuilder
    private func columns(_ place: [DeskAnchor: [DeskPlacement]], colWidth: CGFloat,
                         lineInLayout: Bool, contentTop: CGFloat) -> some View {
        let left = column(.left, place, alignment: .leading, lineInLayout: lineInLayout)
        let right = column(.right, place, alignment: .trailing, lineInLayout: lineInLayout)
        if (place[.tc] ?? []).isEmpty && (place[.bc] ?? []).isEmpty {
            HStack(alignment: .top, spacing: 0) {
                left.frame(maxWidth: colWidth, alignment: .leading)
                Spacer(minLength: DeskAnchorGeometry.columnGap)
                right.frame(maxWidth: colWidth, alignment: .trailing)
            }
        } else {
            DeskColumnsLayout(colWidth: colWidth) {
                left.frame(maxWidth: .infinity, alignment: .leading)
                centerColumn(place, lineInLayout: lineInLayout, contentTop: contentTop)
                right.frame(maxWidth: .infinity, alignment: .trailing)
            }
        }
    }

    /// A side's column: its top and bottom stacks as before, and its middle (item 59) between
    /// them, with a flexible spacer on each side, so it sits halfway between the two stacks and a
    /// tall corner pushes against it instead of drawing over it. Without a middle it is exactly
    /// the column from before item 59.
    private func column(_ side: DeskAnchor.Column, _ place: [DeskAnchor: [DeskPlacement]],
                        alignment: HorizontalAlignment, lineInLayout: Bool) -> some View {
        let top = DeskAnchor.at(side, .top).flatMap { place[$0] } ?? []
        let middle = DeskAnchor.at(side, .middle).flatMap { place[$0] } ?? []
        let bottom = DeskAnchor.at(side, .bottom).flatMap { place[$0] } ?? []
        return VStack(alignment: alignment, spacing: DeskNowPlaying.columnSpacing) {
            pieces(top, alignment: alignment, lineInLayout: lineInLayout)
            Spacer(minLength: 32)
            if !middle.isEmpty {
                pieces(middle, alignment: alignment, lineInLayout: lineInLayout)
                Spacer(minLength: 32)
            }
            pieces(bottom, alignment: alignment, lineInLayout: lineInLayout)
        }
        .frame(maxHeight: .infinity)
    }

    /// The centers (item 59): top center below the notch or the island, bottom center on the
    /// bottom margin. Draws nothing when neither has a piece.
    @ViewBuilder
    private func centerColumn(_ place: [DeskAnchor: [DeskPlacement]], lineInLayout: Bool, contentTop: CGFloat) -> some View {
        let top = place[.tc] ?? []
        let bottom = place[.bc] ?? []
        if !top.isEmpty || !bottom.isEmpty {
            let notch = DeskAnchorGeometry.notchBottom(notch: model.notchRect, island: island, chin: chin)
            VStack(alignment: .center, spacing: DeskNowPlaying.columnSpacing) {
                pieces(top, alignment: .center, lineInLayout: lineInLayout)
                Spacer(minLength: 32)
                pieces(bottom, alignment: .center, lineInLayout: lineInLayout)
            }
            .padding(.top, DeskAnchorGeometry.centerDrop(contentTop: contentTop, notchBottom: notch))
            .frame(maxHeight: .infinity)
        }
    }

    private func pieces(_ stack: [DeskPlacement], alignment: HorizontalAlignment, lineInLayout: Bool) -> some View {
        ForEach(stack, id: \.widget) { p in
            if let widget = Widget(rawValue: p.widget) {
                DeskPiece(widget: widget, model: model, alignment: alignment, lineInLayout: lineInLayout, scale: p.scale)
                    .deskArrangeable(p, mode: model.arrange)
            }
        }
    }
}

/// Left, center and right columns side by side with no horizontal overlap (item 59): the
/// center takes its own width, the sides what is left (DeskAnchorGeometry.columnWidths). The
/// center stays on the screen's middle; each side hugs its edge. Expects three subviews.
struct DeskColumnsLayout: Layout {
    let colWidth: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        proposal.replacingUnspecifiedDimensions()
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        guard subviews.count == 3 else { return }
        let height = bounds.height
        let ideal = subviews[1].sizeThatFits(ProposedViewSize(width: colWidth, height: height)).width
        let widths = DeskAnchorGeometry.columnWidths(total: bounds.width, colWidth: colWidth, center: ideal)
        subviews[0].place(at: CGPoint(x: bounds.minX, y: bounds.minY), anchor: .topLeading,
                          proposal: ProposedViewSize(width: widths.side, height: height))
        subviews[1].place(at: CGPoint(x: bounds.midX, y: bounds.minY), anchor: .top,
                          proposal: ProposedViewSize(width: widths.center, height: height))
        subviews[2].place(at: CGPoint(x: bounds.maxX, y: bounds.minY), anchor: .topTrailing,
                          proposal: ProposedViewSize(width: widths.side, height: height))
    }
}

/// One Desk piece as the desktop draws it, in the Desk's font, sizes and ink: the clock, the
/// claude line, the meters, now playing, the watchers, the meetings or the message. DeskView
/// places these in its corners; Settings' previews (item 68) draw the same pieces under
/// `isSurfacePreview`, where they report no frames and take no clicks.
struct DeskPiece: View {
    let widget: DeskView.Widget
    var model: DeskModel
    let alignment: HorizontalAlignment
    /// The claude line is placed somewhere: a switch's note shows there, else on the meters.
    var lineInLayout = true
    /// The Message preview's Replay: a `{shimmer}` line sweeps at once instead of after its rest.
    var sweepFirst = false
    /// The piece's size in the layout (item 59), 0.6 to 1.6: its sizes are the saved ones times this.
    var scale: Double = 1

    @AppStorage("font", store: .desk) private var savedFont: String?
    /// The Desk font as drawn: EsteFont Pro unless a font was picked (DeskFont, item 58).
    private var font: String { DeskFont.resolve(saved: savedFont) }
    @AppStorage("messageFont", store: .desk) private var messageFont = ""
    @AppStorage("timeSize", store: .desk) private var savedTimeSize = 112.0
    @AppStorage("messageSize", store: .desk) private var savedMessageSize = 84.0
    /// The clock size this piece draws from (every piece but the message scales from it), and
    /// the message's own size as its base, each times the piece's scale.
    private var timeSize: Double { savedTimeSize * scale }
    private var messageSize: Double { savedMessageSize * scale }
    @AppStorage("messageColor", store: .desk) private var messageColor = "9ad7ff"
    @AppStorage(DeskMessageLook.glowKey, store: .desk) private var messageGlow = true
    @AppStorage("inkColor", store: .desk) private var ink = "ffffff"
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        switch widget {
        case .clock: clock.deskPieceClickArea(.clock) { model.clockFrame = $0 }.deskInk()
        case .claude: claude.deskPieceClickArea(.claudeLine) { model.claudeLineFrame = $0 }.deskInk()
        case .meters: meters.deskInk()
        case .nowPlaying: nowPlaying.deskInk()
        case .watchers: DeskWatchers(model: model, font: font, size: timeSize * 0.17, alignment: alignment).deskInk()
        case .meetings: meetings.deskInk()
        case .message: message.deskPieceClickArea(.message) { model.messageFrame = $0 }
        }
    }

    /// The now playing element (item 53b): "▶ Title · Artist" over its position bar while a track
    /// shows, nothing otherwise. Padded so its click area stays clear of its neighbours'.
    @ViewBuilder
    private var nowPlaying: some View {
        if let info = model.nowPlaying {
            DeskNowPlayingLine(info: info, model: model, ink: ink, font: font, size: timeSize * 0.17,
                               width: timeSize * 3.2, alignment: alignment)
                .padding(.vertical, DeskNowPlaying.padding)
        }
    }

    private var clock: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            VStack(alignment: alignment, spacing: 2) {
                // The time is the Desk's heading: a bundled family (EsteFont Pro, EsteFont 26)
                // draws it in its Bold face.
                Text(Self.format(context.date, "h:mm"))
                    .font(.custom(BundledFonts.face(font, bold: true), size: timeSize))
                Text(Self.format(context.date, "EEEE, MMMM d"))
                    .font(.custom(font, size: timeSize * 0.3))
                    .opacity(0.85)
            }
        }
    }

    /// The claude line. During an account switch the old account's line keeps its place unseen
    /// (AccountSwitchFade) and the faint note shows over it once the fetch outlasts the fade.
    private var claude: some View {
        claudeText
            .opacity(model.veiled ? 0 : 1)
            .accessibilityHidden(model.veiled)
            .overlay(alignment: .leading) {
                if model.switchNote && model.claudeLine != nil {
                    switchingNote(size: timeSize * 0.19).opacity(0.75)
                }
            }
    }

    /// The Desk's faint "switching account…".
    private func switchingNote(size: CGFloat) -> some View {
        Text(AccountSwitchFade.deskNote)
            .font(.custom(font, size: size))
            .fixedSize()
            .opacity(AccountSwitchFade.noteOpacity)
            .transition(.opacity)
    }

    @ViewBuilder
    private var claudeText: some View {
        if let parts = model.claudeParts, let account = parts.account {
            // Two or more accounts: the label is its own element, clickable like a meeting row
            // (DeskController cycles to the next account); the rest of the line lets clicks through.
            HStack(alignment: .firstTextBaseline, spacing: 0) {
                AccountLead(label: account, model: model)
                Text("   \(parts.rest)")
            }
            .font(.custom(font, size: timeSize * 0.19))
            .opacity(model.claudeLineIsStale ? 0.45 : 0.75)
        } else if let line = model.claudeLine {
            Text(line)
                .font(.custom(font, size: timeSize * 0.19))
                .opacity(model.claudeLineIsStale ? 0.45 : 0.75)
        } else if model.switchNote {
            // Nothing to fade out (the old account showed no line): the note on its own.
            switchingNote(size: timeSize * 0.19).opacity(0.75)
        }
    }

    /// A bar per Claude limit, in the widget's order: label and percent over a bar in the ink,
    /// a pace tick where the widget puts its own, and the reset time underneath.
    @ViewBuilder
    private var meters: some View {
        let noteHere = model.switchNote && !lineInLayout
        if !model.meters.isEmpty || model.signInNeeded || noteHere {
            let size = timeSize * 0.17
            VStack(alignment: alignment, spacing: size * 0.6) {
                ForEach(model.meters) { row in
                    MeterRow(row: row, ink: ink, font: font, size: size, width: timeSize * 3.2, alignment: alignment)
                        .onGlobalFrame { model.meterRowFrames[row.tier] = $0 }
                        .deskPulse(model.pulses[row.tier] ?? 0, ink: ink, size: size)
                }
                if model.signInNeeded {
                    Text(DeskClaudeText.signInLine(first: model.firstSignIn)).opacity(0.75)
                }
            }
            .font(.custom(font, size: size))
            .opacity(model.veiled ? 0 : (model.claudeLineIsStale ? 0.5 : 1))
            .accessibilityHidden(model.veiled)
            // A switch's note, when the claude line is not on the desktop to carry it.
            .overlay(alignment: alignment == .trailing ? .topTrailing : .topLeading) {
                if noteHere { switchingNote(size: size) }
            }
            // Passive to a plain click (item 41): no hand, nothing happens. A two-finger click
            // opens the row's limit menu (DeskController). The faint plate, as wide as the click
            // slack, is what lets that click reach this transparent window at all (DeskPointerMenu).
            .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity)
                .padding(EdgeInsets(top: -6, leading: -8, bottom: -6, trailing: -8)))
            .contentShape(Rectangle())
            .onGlobalFrame { model.metersFrame = $0 }
        }
    }

    private var meetings: some View {
        VStack(alignment: alignment, spacing: 2) {
            if let note = model.calendarNote {
                // Clickable like a meeting row: DeskController opens System Settings at
                // Privacy & Security, Calendars.
                Text(note).opacity(0.6)
                    .contentShape(Rectangle())
                    .onHover { inside in
                        if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
                    }
                    .onGlobalFrame { model.noteFrame = $0 }
            } else if model.meetings.isEmpty {
                Text(model.noMeetingsLine).opacity(0.6)
            } else {
                ForEach(model.meetings) { meeting in
                    MeetingRow(meeting: meeting)
                        .onGlobalFrame { model.rowFrames[meeting.id] = $0 }
                }
            }
        }
        .font(.custom(font, size: timeSize * 0.21))
        .onGlobalFrame { model.meetingsFrame = $0 }
    }

    /// The handwritten line with its per-line effects (DeskMessageLine, item 54). messageColor
    /// takes one hex, or two or more separated by commas for a left-to-right gradient (to match
    /// Ice's menu bar tint); a line's {ink:…} replaces it for that line.
    /// On a date with its own lines (item 69) those stack above the usual line, each with its own
    /// effects (DeskMessageStack), or take turns with it, one at a time (Take turns, Scroll).
    @ViewBuilder
    private var message: some View {
        if model.cycling {
            turns
        } else if !model.specialMessages.isEmpty || model.message != nil {
            VStack(alignment: alignment, spacing: messageSize * DeskMessageStack.spacing) {
                let hasUsual = model.message != nil
                ForEach(Array(model.specialMessages.enumerated()), id: \.offset) { _, text in
                    line(text, size: messageSize * (hasUsual ? DeskMessageStack.specialScale : 1))
                }
                if let text = model.message { line(text, size: messageSize) }
            }
        }
    }

    /// Take turns and Scroll: one line at a time, each at the full message size (its own {size:}
    /// still applies). Every line is laid out unseen underneath, so the piece keeps the height and
    /// width of the biggest and nothing around it moves; the line showing comes in by a crossfade,
    /// glides up (Scroll), or simply swaps with Reduce Motion. A new line is a new view, so its
    /// {write} plays each time it comes in.
    private var turns: some View {
        let lines = model.messageLines
        let change = MessageSpecialMode.change(model.specialMode, reduceMotion: reduceMotion)
        return ZStack(alignment: Alignment(horizontal: alignment, vertical: .center)) {
            ForEach(Array(lines.enumerated()), id: \.offset) { _, text in
                line(text, size: messageSize, paused: true).hidden()
            }
            if let current = model.cycleLine {
                line(current, size: messageSize)
                    .id(model.cycleIndex)
                    .transition(DeskMessageStack.transition(change))
            }
        }
        .animation(DeskMessageStack.animation(change), value: model.cycleIndex)
    }

    private func line(_ text: String, size: Double, paused: Bool? = nil) -> some View {
        DeskMessageLine(raw: text, font: messageFont.isEmpty ? font : messageFont, baseSize: size,
                        inkSpec: messageColor, globalGlow: messageGlow, alignment: alignment,
                        paused: paused ?? model.motionPaused, sweepFirst: sweepFirst)
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

    /// The clock, the message or the claude line as a click area (DeskPieceClicks): its frame goes
    /// to the model, and while the switch is on the faint plate behind it, as wide as the kind's
    /// click slack, gives every point there a drawn pixel, so the window server hands a two-finger
    /// click to this transparent window instead of the Finder (DeskPointerMenu). Off, nothing is
    /// drawn behind it and the click goes through, as before.
    func deskPieceClickArea(_ kind: DeskElement.Kind, report: @escaping (CGRect) -> Void) -> some View {
        modifier(DeskPieceClickArea(kind: kind, report: report))
    }
}

private struct DeskPieceClickArea: ViewModifier {
    let kind: DeskElement.Kind
    let report: (CGRect) -> Void
    @AppStorage(DeskPieceClicks.key, store: .desk) private var on = DeskPieceClicks.defaultOn

    func body(content: Content) -> some View {
        let s = DeskHitTest.slack(kind)
        content
            .background(Color.black.opacity(on ? DeskPointerMenu.hitPlateOpacity : 0)
                .padding(EdgeInsets(top: -s.height, leading: -s.width, bottom: -s.height, trailing: -s.width)))
            .onGlobalFrame(report)
    }
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

extension HorizontalAlignment {
    /// A piece's frame alignment: its column's edge, or the middle at a center anchor (item 59).
    var deskEdge: Alignment {
        if self == .trailing { return .trailing }
        return self == .center ? .center : .leading
    }

    /// A piece's text alignment, the same way.
    var deskText: TextAlignment {
        if self == .trailing { return .trailing }
        return self == .center ? .center : .leading
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

    /// The widget's over-limit red (`usageColor` at 90% and up).
    static let warningRed = meterWarningRed

    /// "92%", or "92%, nearly full" on a warning row, for VoiceOver.
    private var spokenValue: String { MeterWarning.spokenValue(percent: row.percent, warning: row.warning) }

    var body: some View {
        let barHeight = max(4, size * 0.38)
        VStack(alignment: alignment, spacing: size * 0.22) {
            HStack(alignment: .firstTextBaseline) {
                Text(row.label).lineLimit(1).opacity(0.85)
                Spacer(minLength: size)
                HStack(alignment: .firstTextBaseline, spacing: size * 0.25) {
                    if row.warning {
                        // Says so without the red: the glyph in the ink, sized to the row.
                        Image(systemName: MeterWarning.glyph)
                            .font(.system(size: size * 0.75, weight: .semibold))
                            .foregroundStyle(LinearGradient.ink(ink))
                        Text("\(row.percent)%").foregroundStyle(Self.warningRed)
                    } else {
                        Text("\(row.percent)%")
                    }
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(spokenValue)
            }
            ZStack(alignment: .leading) {
                Capsule().fill(LinearGradient.ink(ink)).opacity(0.22)
                Capsule().fill(row.warning ? AnyShapeStyle(Self.warningRed) : AnyShapeStyle(LinearGradient.ink(ink)))
                    .frame(width: width * row.fill)
            }
            .frame(width: width, height: barHeight)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(row.label)
            .accessibilityValue(spokenValue)
            .background {
                // A warning row: a steady glow in the ink wrapping the bar, as the notch glows.
                if row.warning {
                    Capsule()
                        .fill(LinearGradient.ink(ink))
                        .padding(-barHeight * 0.7)
                        .blur(radius: barHeight * 0.9)
                        .opacity(0.6)
                }
            }
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

/// The account label at the start of the claude line, with two or more accounts. The pointer
/// turns into a hand and the label underlines while over it; the click itself is handled in
/// DeskController, which switches to the next account (the widget chip's cycle).
private struct AccountLead: View {
    let label: String
    var model: DeskModel
    @State private var hovering = false

    var body: some View {
        Text(label)
            .underline(hovering)
            .contentTransition(.opacity)
            .contentShape(Rectangle())
            .onGlobalFrame { model.accountFrame = $0 }
            .onHover { inside in
                hovering = inside
                if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
            }
            .accessibilityAddTraits(.isButton)
            .accessibilityHint("Switches to the next account")
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
        // The click itself is handled in AppDelegate, which sees it even when macOS hands
        // the first click to the desktop instead of this window.
        .onHover { inside in
            guard meeting.link != nil else { return }
            hovering = inside
            if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
        }
    }
}

extension View {
    /// Reports this view's frame in global (window) coordinates when it appears and whenever it
    /// moves or resizes, for the clicks DeskController routes by position. Reported straight to
    /// the model, not through a PreferenceKey: on the Desk window the meters' preference reached
    /// onPreferenceChange once, as .zero, and never again, so the meters took no clicks (found
    /// 2026-10-04 with a click probe). The other click areas used the same pattern.
    /// Nothing clears a frame on disappear: when Desk comes back (off and on, a layout change) the
    /// old views' onDisappear ran after the new views' onAppear and wiped the fresh frames. A
    /// stale frame is harmless, because DeskElements lists only what is drawn now.
    /// Under `isSurfacePreview` (Settings' previews, item 68) nothing is reported: a preview's
    /// pieces are never click areas.
    func onGlobalFrame(_ report: @escaping (CGRect) -> Void) -> some View {
        modifier(GlobalFrameReport(report: report))
    }
}

private struct GlobalFrameReport: ViewModifier {
    let report: (CGRect) -> Void
    @Environment(\.isSurfacePreview) private var preview

    func body(content: Content) -> some View {
        if SurfacePreview.reportsFrames(preview: preview) {
            content.background(GeometryReader { geo in
                Color.clear
                    .onAppear { report(geo.frame(in: .global)) }
                    .onChange(of: geo.frame(in: .global)) { _, frame in report(frame) }
            })
        } else {
            content
        }
    }
}

/// A surface drawn as a preview in Settings (item 68): the real view, fed a preview model,
/// that reports no frames, takes no clicks and registers no click areas.
enum SurfacePreview {
    /// Frames go to the model only off a preview.
    static func reportsFrames(preview: Bool) -> Bool { !preview }
}

private struct SurfacePreviewKey: EnvironmentKey {
    static let defaultValue = false
}

extension EnvironmentValues {
    /// True inside a Settings preview card (SettingsPreviewCard): the surfaces' views draw as
    /// usual but report no frames for click routing.
    var isSurfacePreview: Bool {
        get { self[SurfacePreviewKey.self] }
        set { self[SurfacePreviewKey.self] = newValue }
    }
}
