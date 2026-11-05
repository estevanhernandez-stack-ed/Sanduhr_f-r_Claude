import AppKit
import SwiftUI

/// The Design it with an AI chat window (setup spec, section 12): a small window opened from Set up Sanduhr and from
/// Settings, Drawers. Each opening starts empty; a paste is never kept.
@MainActor
final class ChatDesignWindowController: NSObject, NSWindowDelegate {
    static let shared = ChatDesignWindowController()
    private(set) var window: NSWindow?

    /// Opens the window, or brings an open one forward.
    func show() {
        if let w = window, w.isVisible {
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return
        }
        let model = Self.makeModel()
        model.onClose = { [weak self] in self?.window?.close() }
        model.onReviewShown = { SettingsWindowController.shared.show(.deskLayout) }
        model.onShowRules = { SettingsWindowController.shared.show(.drawers) }
        let w = window ?? NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 560),
                                   styleMask: [.titled, .closable, .resizable],
                                   backing: .buffered, defer: false)
        w.title = SetupCopy.designButton
        w.isReleasedWhenClosed = false
        w.delegate = self
        w.contentView = NSHostingView(rootView: ChatDesignView(model: model))
        w.center()
        window = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    /// The app's model: the real pasteboard, the Desk's own validator and review, and the one drawer service.
    static func makeModel() -> ChatDesignViewModel {
        let handoff = DeskDesignProposalHandoff.shared
        return ChatDesignViewModel(
            copy: { text in
                let pb = NSPasteboard.general
                pb.clearContents()
                return pb.setString(text, forType: .string)
            },
            validate: { handoff.validatePasted($0) },
            previewDesk: { handoff.proposePasted($0) },
            drawers: ChatDrawerApplier(service: .shared))
    }

    func windowWillClose(_ notification: Notification) {
        // The window's content (and the paste in it) goes with the window.
        window?.contentView = nil
    }
}

struct ChatDesignView: View {
    @Bindable var model: ChatDesignViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(SetupCopy.designButton).font(.title2.bold()).accessibilityAddTraits(.isHeader)
            Text(SetupCopy.chatIntro)
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Button(SetupCopy.chatCopyPrompt) { model.copyPrompt() }
            paste
            ChatFindings(model: model)
            ChatActions(model: model)
            if !model.status.isEmpty { Text(model.status).font(.callout).fixedSize(horizontal: false, vertical: true) }
            Spacer(minLength: 0)
            HStack {
                Spacer()
                Button(SetupCopy.chatClose) { model.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(20)
        .frame(minWidth: 480, minHeight: 480)
    }

    private var paste: some View {
        TextEditor(text: $model.pasteText)
            .font(.system(.callout, design: .monospaced))
            .frame(minHeight: 110)
            .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.primary.opacity(0.2)))
            .overlay(alignment: .topLeading) {
                if model.pasteText.isEmpty {
                    Text(SetupCopy.chatPastePlaceholder)
                        .font(.system(.callout, design: .monospaced)).foregroundStyle(.tertiary)
                        .padding(6).allowsHitTesting(false).accessibilityHidden(true)
                }
            }
            .accessibilityLabel(SetupCopy.chatPasteName)
    }
}

private struct ChatFindings: View {
    let model: ChatDesignViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if !model.summary.isEmpty { Text(model.summary).font(.callout.weight(.semibold)) }
            ForEach(Array(model.findings.enumerated()), id: \.offset) { _, f in
                Text("\(f.field): \(f.text)")
                    .font(.callout)
                    .foregroundStyle(f.isError ? Color.red : Color.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(SetupCopy.chatFindingsName)
    }
}

private struct ChatActions: View {
    let model: ChatDesignViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button(SetupCopy.chatPreview) { model.preview() }.disabled(!model.canPreview)
            Button(SetupCopy.chatUseRules) { model.useRules() }.disabled(!model.canUseRules)
            if !model.rulesResult.isEmpty {
                Text(model.rulesResult).font(.callout).fixedSize(horizontal: false, vertical: true)
                if model.rulesSaved { Button(SetupCopy.chatSeeRules) { model.showRules() }.buttonStyle(.link) }
            } else if !model.rulesLine.isEmpty {
                caption(model.rulesLine)
            }
            HStack {
                Button(SetupCopy.chatUseDrawerSettings) { model.useDrawerSettings() }.disabled(!model.canUseDrawerSettings)
                if model.canUndo { Button(ChatDesignViewModel.undoTitle) { model.undoDrawerSettings() } }
            }
            if !model.drawerSettingsLine.isEmpty { caption(model.drawerSettingsLine) }
        }
    }

    private func caption(_ text: String) -> some View {
        Text(text).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }
}
