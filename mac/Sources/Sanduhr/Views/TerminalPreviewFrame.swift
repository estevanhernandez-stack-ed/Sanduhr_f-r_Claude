import SwiftUI

/// A dark terminal frame for anything Sanduhr draws inside Claude Code (item 68): the statusline
/// sample on Integrations now; the meters band, the watcher band and the Mods page's surfaces
/// when they ship. Monospaced, ANSI colors and styles drawn by ANSIText, powerline glyphs drawn
/// by PowerlineGlyph (so no Nerd Font is needed). With `clock`, the text is asked for again on
/// that period, for animated effects; Reduce Motion asks once and keeps it still.
struct TerminalPreviewFrame: View {
    /// The bar's title ("Claude Code"), nil for no bar.
    var title: String?
    /// Shown dimmed when there is no text.
    var placeholder = ""
    /// Seconds between frames for animated text; nil for still.
    var clock: TimeInterval?
    var maxLines = 8
    /// The text to draw at a moment, escapes included.
    let text: (Date) -> String?

    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Still text.
    init(_ text: String?, title: String? = nil, placeholder: String = "", maxLines: Int = 8) {
        self.title = title
        self.placeholder = placeholder
        self.maxLines = maxLines
        self.text = { _ in text }
    }

    /// Text drawn again every `clock` seconds.
    init(title: String? = nil, placeholder: String = "", clock: TimeInterval?, maxLines: Int = 8,
         text: @escaping (Date) -> String?) {
        self.title = title
        self.placeholder = placeholder
        self.clock = clock
        self.maxLines = maxLines
        self.text = text
    }

    /// The text when it is drawn: trailing newlines gone; nil for nothing to draw.
    static func shown(_ raw: String?) -> String? {
        guard let raw else { return nil }
        let trimmed = raw.trimmingCharacters(in: .newlines)
        return trimmed.trimmingCharacters(in: .whitespaces).isEmpty ? nil : trimmed
    }

    /// The period the frame redraws at: `clock`, never under Reduce Motion or below a tenth of a
    /// second.
    static func period(clock: TimeInterval?, reduceMotion: Bool) -> TimeInterval? {
        guard let clock, clock > 0, !reduceMotion else { return nil }
        return max(0.1, clock)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let title { bar(title) }
            content
                .padding(.horizontal, 10)
                .padding(.vertical, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .font(.system(size: 12, design: .monospaced))
        .foregroundStyle(Color(white: 0.9))
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color(white: 0.07)))
        .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Color.white.opacity(0.15)))
        .environment(\.colorScheme, .dark)
    }

    @ViewBuilder
    private var content: some View {
        if let period = Self.period(clock: clock, reduceMotion: reduceMotion) {
            TimelineView(.periodic(from: .now, by: period)) { context in
                lines(text(context.date))
            }
        } else {
            lines(text(Date()))
        }
    }

    @ViewBuilder
    private func lines(_ raw: String?) -> some View {
        if let shown = Self.shown(raw) {
            ANSIText.text(shown).lineLimit(maxLines)
        } else {
            Text(placeholder).foregroundStyle(.secondary)
        }
    }

    private func bar(_ title: String) -> some View {
        HStack(spacing: 6) {
            ForEach(["ff5f57", "febc2e", "28c840"], id: \.self) { hex in
                Circle().fill(Color.hex(hex)).frame(width: 8, height: 8)
            }
            Text(title)
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(.white.opacity(0.6))
                .padding(.leading, 6)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Color.white.opacity(0.06))
        .accessibilityHidden(true)
    }
}
