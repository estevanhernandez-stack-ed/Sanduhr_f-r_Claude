import AppKit
import SwiftUI

/// The Set up Sanduhr window (setup spec, section 10): a small window, one choice per row. It opens
/// on a new install, from the menu bar item's Set up Sanduhr…, from Settings, General and from the
/// What's New card. Closing it any way but Continue saves nothing.
@MainActor
final class SetupWindowController: NSObject, NSWindowDelegate {
    static let shared = SetupWindowController()
    private(set) var window: NSWindow?
    private(set) var model: SetupViewModel?

    var isOpen: Bool { window?.isVisible ?? false }

    /// Opens the window, or brings an open one forward. Each opening reads the setup afresh.
    func show() {
        if let w = window, w.isVisible {
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return
        }
        guard let app = NSApp.delegate as? AppDelegate else { return }
        let host = AppSetupHost(usage: app.viewModel, confirm: Self.confirmPutBack)
        let service = SetupService(storage: .defaults(setup: UserDefaults.standard, desk: UserDefaults.desk), host: host)
        let model = SetupViewModel(service: service, firstRun: SetupStore.load() == nil,
                                   hasAccount: { !app.viewModel.accountLabels.isEmpty })
        model.onApplied = { [weak app] result in app?.afterSetup(result) }
        model.onClose = { [weak self] in self?.close() }
        model.onDesignWithChat = { ChatDesignWindowController.shared.show() }
        self.model = model

        let w = window ?? NSWindow(contentRect: NSRect(x: 0, y: 0, width: 480, height: 520),
                                   styleMask: [.titled, .closable, .fullSizeContentView],
                                   backing: .buffered, defer: false)
        w.title = SetupCopy.title
        w.titleVisibility = .hidden
        w.titlebarAppearsTransparent = true
        w.isMovableByWindowBackground = true
        w.isReleasedWhenClosed = false
        w.delegate = self
        w.contentView = NSHostingView(rootView: SetupView(model: model))
        w.center()
        window = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    func close() { window?.close() }

    /// The Drawers page's "Put N files back on the Desktop?".
    private static func confirmPutBack(_ count: Int) -> Bool {
        let alert = NSAlert()
        alert.messageText = DrawerReturn.prompt(count)
        alert.addButton(withTitle: "Put Them Back")
        alert.addButton(withTitle: SetupCopy.cancel)
        return alert.runModal() == .alertFirstButtonReturn
    }
}

struct SetupView: View {
    @Bindable var model: SetupViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(SetupCopy.title)
                .font(.title2.bold())
                .accessibilityAddTraits(.isHeader)
            Text(SetupCopy.intro)
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if model.said {
                said
            } else {
                choices
                if let blocked = model.blocked { note(blocked) }
                changes
                if !model.resultLines.isEmpty { lines(SetupCopy.whatHappened, model.resultLines) }
                if model.showTrademark { trademark }
                design
            }
            Spacer(minLength: 0)
            buttons
        }
        .padding(24)
        .frame(width: 480)
        .frame(minHeight: 360)
    }

    private var choices: some View {
        VStack(alignment: .leading, spacing: 12) {
            choice(SetupCopy.desktopTitle, SetupCopy.desktopBody, $model.desktop)
            choice(SetupCopy.drawersTitle, SetupCopy.drawersBody, $model.drawers)
            choice(SetupCopy.usageTitle, SetupCopy.usageBody, $model.claudeUsage, source: SetupCopy.claudeSource)
        }
    }

    /// `source` names what a usage choice reads (Claude), under its body.
    private func choice(_ title: String, _ body: String, _ isOn: Binding<Bool>, source: String? = nil) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Toggle(title, isOn: isOn)
                .font(.headline)
                .accessibilityLabel(title)
                .accessibilityHint(body)
            Text(body)
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.leading, 20)
                .accessibilityHidden(true)
            if let source {
                Text(source)
                    .font(.caption.bold())
                    .foregroundStyle(.secondary)
                    .padding(.leading, 20)
                    .accessibilityHidden(true)
            }
        }
    }

    @ViewBuilder
    private var changes: some View {
        if !model.changeLines.isEmpty { lines(SetupCopy.whatChanges, model.changeLines) }
    }

    private func lines(_ title: String, _ text: [String]) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.subheadline.bold())
            ForEach(text, id: \.self) { line in
                Text(line)
                    .font(.callout)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
        .accessibilityElement(children: .combine)
    }

    private var said: some View {
        lines(SetupCopy.whatHappened, model.resultLines)
    }

    private func note(_ text: String) -> some View {
        Text(text)
            .font(.callout)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var trademark: some View {
        Text(AppInfo.independence)
            .font(.system(size: 11))
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// Design it with an AI chat (item 7 sets the closure; the button stays hidden until then).
    @ViewBuilder
    private var design: some View {
        if let open = model.onDesignWithChat {
            VStack(alignment: .leading, spacing: 4) {
                Button(SetupCopy.designButton, action: open)
                    .accessibilityHint(SetupCopy.designCaption)
                Text(SetupCopy.designCaption)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var buttons: some View {
        HStack {
            if model.said {
                Spacer()
                Button(SetupCopy.done) { model.dismiss() }
                    .keyboardShortcut(.defaultAction)
            } else {
                Button(model.dismissLabel) { model.dismiss() }
                    .keyboardShortcut(.cancelAction)
                    .help("Close this and save nothing")
                Spacer()
                Button(SetupCopy.continueTitle) { model.continueTapped() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(!model.canContinue)
                    .help(model.blocked ?? "Apply these choices")
            }
        }
    }
}
