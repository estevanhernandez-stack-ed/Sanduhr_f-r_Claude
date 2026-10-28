import SwiftUI

/// "Claude suggests a widget" at the top of Settings, Widgets while a proposal waits (spec 3.8 section 6), modelled on
/// `ThemeSuggestionBanner`: the heading, the name, Claude's note, "What this widget is", the files and their license,
/// and "made here, on this Mac only"; then Preview on the Desk (End preview while it shows), Make it the companion (a
/// sprite), Apply and Dismiss. Nothing is installed or drawn until the user presses a button. The words are
/// `WidgetReviewCopy`'s.
struct WidgetSuggestionBanner: View {
    let model: WidgetReviewModel
    /// One sentence from a Preview that could not show (the Desk is off, a sprite that cannot be drawn), else nil.
    let previewNote: String?
    let preview: () -> Void
    let endPreview: () -> Void
    let makeCompanion: () -> Void
    let apply: () -> Void
    let dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            WidgetSuggestionDetails(model: model)
            hint
            WidgetSuggestionButtons(model: model, preview: preview, endPreview: endPreview,
                                    makeCompanion: makeCompanion, apply: apply, dismiss: dismiss)
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.accentColor.opacity(0.25)))
        .accessibilityElement(children: .contain)
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Image(systemName: "sparkles").foregroundStyle(.tint)
            Text(model.heading).font(.headline)
        }
    }

    @ViewBuilder
    private var hint: some View {
        if let text = model.previewHint ?? previewNote {
            Text(text).font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The note, "What this widget is" and its lines (the last two are the files with their license, and the made-here line).
private struct WidgetSuggestionDetails: View {
    let model: WidgetReviewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(model.name).font(.callout.weight(.semibold))
            if let note = model.note {
                Text(note).font(.callout).foregroundStyle(.secondary).lineLimit(4)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(model.changesTitle).font(.caption.weight(.semibold)).padding(.top, 2)
            ForEach(Array(model.changes.enumerated()), id: \.offset) { _, line in
                Text(line).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

private struct WidgetSuggestionButtons: View {
    let model: WidgetReviewModel
    let preview: () -> Void
    let endPreview: () -> Void
    let makeCompanion: () -> Void
    let apply: () -> Void
    let dismiss: () -> Void

    var body: some View {
        HStack {
            Button(WidgetReviewCopy.dismiss, action: dismiss)
            Spacer()
            if model.previewing {
                Button(WidgetReviewCopy.endPreview, action: endPreview)
            } else {
                Button(WidgetReviewCopy.previewOnDesk, action: preview)
                    .disabled(!model.canPreview || model.deskOff)
            }
            if let label = model.option {
                Button(label, action: makeCompanion)
            }
            Button(WidgetReviewCopy.apply, action: apply)
                .buttonStyle(.borderedProminent)
        }
    }
}
