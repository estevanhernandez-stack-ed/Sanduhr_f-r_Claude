import SwiftUI

/// "Claude suggests a Desk design" at the top of Settings, Desk while a design waits (spec 8), modelled on
/// `WidgetSuggestionBanner`: the heading, the name, Claude's note, "What changed" with a line per change, any font to
/// install; then Dismiss, Preview on the Desk (End preview while it shows) and Apply. Nothing is written or drawn until
/// the user presses a button. The words are `DeskDesignReviewCopy`'s.
struct DeskDesignBanner: View {
    let model: DeskDesignReviewModel
    /// One sentence from a Preview that could not show (the Desk is off), else nil.
    let previewNote: String?
    let preview: () -> Void
    let endPreview: () -> Void
    let apply: () -> Void
    let dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            DeskDesignDetails(model: model)
            hint
            DeskDesignButtons(model: model, preview: preview, endPreview: endPreview, apply: apply, dismiss: dismiss)
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

/// The name, Claude's note, "What changed" and its lines, and the fonts still to install.
private struct DeskDesignDetails: View {
    let model: DeskDesignReviewModel

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
            if !model.missingFonts.isEmpty {
                Text(DeskDesignReviewCopy.fontsTitle).font(.caption.weight(.semibold)).padding(.top, 2)
                ForEach(model.missingFonts, id: \.family) { font in
                    Text(font.line).font(.caption).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Text(DeskDesignReviewCopy.applyWaiting).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

private struct DeskDesignButtons: View {
    let model: DeskDesignReviewModel
    let preview: () -> Void
    let endPreview: () -> Void
    let apply: () -> Void
    let dismiss: () -> Void

    var body: some View {
        HStack {
            Button(DeskDesignReviewCopy.dismiss, action: dismiss)
            Spacer()
            if model.previewing {
                Button(DeskDesignReviewCopy.endPreview, action: endPreview)
            } else {
                Button(DeskDesignReviewCopy.previewOnDesk, action: preview)
                    .disabled(model.deskOff)
            }
            Button(DeskDesignReviewCopy.apply, action: apply)
                .buttonStyle(.borderedProminent)
                .disabled(!model.canApply)
        }
    }
}
