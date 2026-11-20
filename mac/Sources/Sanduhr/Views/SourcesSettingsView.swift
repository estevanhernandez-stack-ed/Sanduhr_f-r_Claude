import AppKit
import SwiftUI

/// Settings, Usage, Sources (agent sources, item 7): one row per source with its switch, a color
/// well with Reset, a Primary radio among the sources that have limits and are on, and a caption.
/// The group sits at the top of the Usage page and stays while usage is off (a Mac with only Codex
/// on has no account, and still needs the switches).
struct SourcesSettingsView: View {
    let t: Theme.Palette
    @State private var model: SourcesSettingsModel

    init(vm: UsageViewModel, t: Theme.Palette) {
        self.t = t
        // Applies a change exactly as Set up Sanduhr's Continue does (the same service and host).
        let host = AppSetupHost(usage: vm, confirm: { _ in false })
        let service = SetupService(storage: .defaults(setup: UserDefaults.standard, desk: UserDefaults.desk), host: host)
        let model = SourcesSettingsModel(
            service: service,
            hasVaultRoot: { VaultService.standard.reader.agentRoots().contains($0) })
        model.onApplied = { (NSApp.delegate as? AppDelegate)?.afterSetup($0) }
        model.onChanged = { NotificationCenter.default.post(name: .sanduhrSourcesDidChange, object: nil) }
        // Claude's consent to an agent's totals lives in mcp-access.json: rewrite it now.
        model.onConsentChanged = { [weak vm] in vm?.reloadAccounts() }
        _model = State(initialValue: model)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(SourcesCopy.title)
                .font(.headline)
                .accessibilityAddTraits(.isHeader)
            Text(SourcesCopy.intro)
                .font(.caption)
                .foregroundStyle(t.textDim)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(model.rows) { row in
                SourceRowView(row: row, model: model, t: t)
            }
            Text(SourcesCopy.primaryCaption)
                .font(.caption)
                .foregroundStyle(t.textDim)
                .fixedSize(horizontal: false, vertical: true)
            if let message = model.message {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(t.paceMarker)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.updatesFrequently)
            }
            Divider()
        }
        .onReceive(NotificationCenter.default.publisher(for: .sanduhrSetupDidChange)) { _ in model.reload() }
    }
}

/// One source: the switch, the lines under it, then Primary and the color.
private struct SourceRowView: View {
    let row: SourceRow
    let model: SourcesSettingsModel
    let t: Theme.Palette

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Toggle(row.name, isOn: Binding(get: { row.isOn }, set: { model.setOn(row.source, $0) }))
                .font(.system(size: 13, weight: .semibold))
                .disabled(!row.canToggle)
                .accessibilityLabel(SourcesCopy.trackName(row.source))
            Lines(row: row, t: t)
            if let mcp = row.mcpAgent { ConsentSwitch(mcp: mcp, row: row, model: model) }
            if row.source.hasLimits { Controls(row: row, model: model, t: t) }
            else { ColorControls(row: row, model: model, t: t).padding(.leading, 20) }
            if let refused = model.colorMessage[row.source] {
                Text(refused).font(.caption).foregroundStyle(t.paceMarker).padding(.leading, 20)
            }
        }
    }

    /// The captions: what it reads, nothing found, and the trademark line.
    private struct Lines: View {
        let row: SourceRow
        let t: Theme.Palette

        var body: some View {
            VStack(alignment: .leading, spacing: 2) {
                if let caption = row.caption { line(caption) }
                if let none = row.nothingFound { line(none) }
                if let mark = row.trademark { line(mark, size: 11) }
            }
            .padding(.leading, 20)
        }

        private func line(_ text: String, size: CGFloat = 12) -> some View {
            Text(text)
                .font(.system(size: size))
                .foregroundStyle(t.textDim)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// "Let Claude read Codex's project totals", off until switched on.
    private struct ConsentSwitch: View {
        let mcp: MCPAgentSwitch
        let row: SourceRow
        let model: SourcesSettingsModel

        var body: some View {
            Toggle(mcp.label, isOn: Binding(get: { mcp.isOn }, set: { model.setMCPAgent(row.source, $0) }))
                .toggleStyle(.checkbox)
                .font(.system(size: 12))
                .padding(.leading, 20)
        }
    }

    private struct Controls: View {
        let row: SourceRow
        let model: SourcesSettingsModel
        let t: Theme.Palette

        var body: some View {
            HStack(spacing: 16) {
                Toggle(SourcesCopy.primary, isOn: Binding(
                    get: { row.isPrimary }, set: { if $0 { model.setPrimary(row.source) } }))
                    .toggleStyle(.checkbox)
                    .disabled(!row.canBePrimary || row.isPrimary)
                    .accessibilityLabel(SourcesCopy.primaryName(row.source))
                ColorControls(row: row, model: model, t: t)
            }
            .padding(.leading, 20)
        }
    }
}

/// The color well and its Reset.
private struct ColorControls: View {
    let row: SourceRow
    let model: SourcesSettingsModel
    let t: Theme.Palette

    var body: some View {
        HStack(spacing: 8) {
            Text(SourcesCopy.color).font(.system(size: 12)).foregroundStyle(t.textDim)
            ColorPicker(SourcesCopy.colorName(row.source), selection: color, supportsOpacity: false)
                .labelsHidden()
                .accessibilityLabel(SourcesCopy.colorName(row.source))
            Button(SourcesCopy.reset) { model.resetColor(row.source) }
                .controlSize(.small)
                .disabled(row.isDefaultColor)
                .accessibilityLabel(SourcesCopy.resetName(row.source))
        }
    }

    private var color: Binding<Color> {
        Binding(
            get: {
                let rgb = ThemeLint.parseHex(row.color)
                return Color(red: rgb?.r ?? 1, green: rgb?.g ?? 1, blue: rgb?.b ?? 1)
            },
            set: { new in
                guard let ns = NSColor(new).usingColorSpace(.sRGB) else { return }
                model.setColor(row.source, SourcesColor.hex(red: ns.redComponent, green: ns.greenComponent, blue: ns.blueComponent))
            })
    }
}
