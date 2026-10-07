import SwiftUI

/// The handwritten line with its effects (item 54), on the Desk and in Settings, Message's review
/// of a suggestion. Drawn the way handwritten.py baked it: colored ink, a soft glow in the same
/// color, two lines at most, scaling down rather than colliding.
///
/// Cost: a line without `{write}` or `{shimmer}` is plain text, no mask, no task that wakes.
/// `{write}` is one 1.5 s animation when the line first shows (a mask whose width animates).
/// `{shimmer}` is a task that sleeps 6.4 s, sweeps for 1.6 s and sleeps again; it does not run
/// with Reduce Motion or while `paused` (the Desk is covered, the screens sleep, the session is
/// switched away), so nothing wakes while nobody can see it. `{sweep}` draws frames only while
/// its light moves (1.1 s), once when the line appears or every `{sweep:<seconds>}`, and rests
/// the same way. `{font:…}` (item 65) is drawn by MessageTypography: no cost beyond the text.
struct DeskMessageLine: View {
    /// The line's text as picked (prefix gone, tags still on).
    let raw: String
    let font: String
    let baseSize: CGFloat
    /// Settings, Desk, Look's message color: one hex or a comma list.
    let inkSpec: String
    /// Settings, Desk, Look's glow switch.
    let globalGlow: Bool
    let alignment: HorizontalAlignment
    let paused: Bool
    var lineLimit = 2
    /// Settings, Message's preview Replay (item 68): the first `{shimmer}` and `{sweep}` come at once.
    var sweepFirst = false

    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var reveal: CGFloat = 0
    @State private var sweep: CGFloat = 0
    /// While a `{sweep}` light runs: when it started. nil between sweeps (no frames).
    @State private var lightStart: Date?
    /// The line whose once-only `{sweep}` has run, so coming back into sight doesn't repeat it.
    @State private var sweptRaw: String?

    var body: some View {
        let parsed = MessageMarkup.parse(raw)
        let effects = parsed.effects
        let size = MessageMotion.size(effects, base: baseSize)
        let colors = Color.inkStops(MessageMotion.inkSpec(effects, global: inkSpec), fallback: "9ad7ff")
        let glow = colors[colors.count / 2]
        let glows = MessageMotion.glows(effects, global: globalGlow)
        let writes = MessageMotion.animatesWrite(effects, reduceMotion: reduceMotion)
        let shimmers = MessageMotion.shimmers(effects, reduceMotion: reduceMotion, paused: paused)
        let plan = MessageTypography.plan(parsed.text, style: effects.font, family: font)
        let sweepPlan = MessageMotion.sweepPlan(effects, reduceMotion: reduceMotion, paused: paused,
                                                sweptOnce: sweptRaw == raw, replay: sweepFirst)
        styled(plan, size: size)
            .foregroundStyle(LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
            .overlay { if shimmers { shimmerBand.mask(styled(plan, size: size)) } }
            .overlay { if let start = lightStart { light(plan, size: size, start: start) } }
            .modifier(Slant(degrees: plan.slant))
            .accessibilityLabel(parsed.text)
            .modifier(RevealMask(progress: writes ? reveal : 1, overhang: size * 0.4, active: writes))
            .opacity(0.94)
            .shadow(color: glow.opacity(glows ? 0.55 : 0), radius: glows ? size * 0.18 : 0)
            .shadow(color: glow.opacity(glows ? 0.35 : 0), radius: glows ? size * 0.18 : 0)
            .task(id: raw) { await write(writes) }
            .task(id: shimmers) { await shimmer(shimmers) }
            .task(id: SweepKey(raw: raw, runs: sweepPlan != nil)) { await sweepLight(sweepPlan) }
    }

    private func styled(_ plan: MessageTypography.Plan, size: CGFloat, color: ((Int) -> Color?)? = nil) -> some View {
        MessageTypography.text(plan, size: size, color: color)
            .font(MessageTypography.font(plan, size: size))
            .multilineTextAlignment(alignment.deskText)
            .lineLimit(lineLimit)
            .minimumScaleFactor(0.4)
    }

    /// A soft band of light, off to the left at rest, across the line while `sweep` runs to 1.
    private var shimmerBand: some View {
        GeometryReader { geo in
            let band = max(1, geo.size.width * MessageMotion.shimmerBand)
            LinearGradient(colors: [.white.opacity(0), .white.opacity(0.7), .white.opacity(0)],
                           startPoint: .leading, endPoint: .trailing)
                .frame(width: band, height: geo.size.height)
                .offset(x: -band + (geo.size.width + band) * sweep)
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    /// `{sweep}`'s light: the same text over the line, each character white by how near the light
    /// is, the rest clear; frames only while it crosses.
    private func light(_ plan: MessageTypography.Plan, size: CGFloat, start: Date) -> some View {
        let length = plan.text.count
        return TimelineView(.animation(minimumInterval: MessageMotion.sweepFrame)) { context in
            let fraction = context.date.timeIntervalSince(start) / MessageMotion.sweepRun
            let at = MessageMotion.sweepAt(fraction: fraction, length: length)
            styled(plan, size: size) { i in
                let k = MessageMotion.sweepLight(index: i, at: at)
                return k > 0 ? Color.white.opacity(k) : nil
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    /// `{sweep}`: wait, cross, and for a period wait again, until cancelled (the line changed, the
    /// Desk was covered, Reduce Motion came on). A once-only sweep remembers its line.
    private func sweepLight(_ plan: (first: TimeInterval, every: TimeInterval?)?) async {
        lightStart = nil
        guard let plan else { return }
        var wait = plan.first
        while !Task.isCancelled {
            do { try await Task.sleep(for: .seconds(wait)) } catch { return }
            lightStart = Date()
            do { try await Task.sleep(for: .seconds(MessageMotion.sweepRun)) } catch { lightStart = nil; return }
            lightStart = nil
            guard let every = plan.every else { sweptRaw = raw; return }
            wait = every - MessageMotion.sweepRun
        }
    }

    /// `{write}`: from nothing to the whole line, once per line that appears.
    private func write(_ animates: Bool) async {
        guard animates else { return }
        var snap = Transaction()
        snap.disablesAnimations = true
        withTransaction(snap) { reveal = 0 }
        do { try await Task.sleep(for: .milliseconds(60)) } catch { return }
        withAnimation(.easeInOut(duration: MessageMotion.writeDuration)) { reveal = 1 }
    }

    /// `{shimmer}`: wait, sweep, back to rest unseen, until cancelled (the line changed, the Desk
    /// was covered, Reduce Motion came on).
    private func shimmer(_ runs: Bool) async {
        var snap = Transaction()
        snap.disablesAnimations = true
        withTransaction(snap) { sweep = 0 }
        guard runs else { return }
        let rest = MessageMotion.shimmerPeriod - MessageMotion.shimmerSweep
        var first = true
        while !Task.isCancelled {
            // Settings' Replay sweeps at once; the Desk waits its rest first.
            let wait = first && sweepFirst ? 0.3 : rest
            first = false
            do { try await Task.sleep(for: .seconds(wait)) } catch { return }
            withAnimation(.easeInOut(duration: MessageMotion.shimmerSweep)) { sweep = 1 }
            do { try await Task.sleep(for: .seconds(MessageMotion.shimmerSweep)) } catch { return }
            withTransaction(snap) { sweep = 0 }
        }
    }
}

/// A date's own lines stacked above the day's usual line (item 69).
enum DeskMessageStack {
    /// The special lines' size against the message size while the usual line shows under them,
    /// so the day's line stays the anchor; alone they draw at the full size.
    static let specialScale: Double = 0.8
    /// The gap between lines, as a share of the message size.
    static let spacing: Double = 0.12
}

/// What restarts `{sweep}`: a new line, or the light being allowed to run or not.
private struct SweepKey: Equatable {
    let raw: String
    let runs: Bool
}

/// `{write}`'s mask: a rectangle growing from the left edge, reaching past the text's box on
/// every side by `overhang`, so handwritten strokes and the glow are never cut once it is whole.
private struct RevealMask: ViewModifier {
    let progress: CGFloat
    let overhang: CGFloat
    let active: Bool

    func body(content: Content) -> some View {
        if active {
            content.mask(RevealShape(progress: progress, overhang: overhang))
        } else {
            content
        }
    }
}

private struct RevealShape: Shape {
    var progress: CGFloat
    let overhang: CGFloat

    var animatableData: CGFloat {
        get { progress }
        set { progress = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let full = rect.width + overhang * 2
        return Path(CGRect(x: rect.minX - overhang, y: rect.minY - overhang,
                           width: full * max(0, min(1, progress)), height: rect.height + overhang * 2))
    }
}
