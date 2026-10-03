import SwiftUI

/// Settings, Widget, Themes: every theme as a small preview with its name, the one in use
/// marked. A click applies it to the widget through `UsageViewModel.selectTheme(id:)`, the
/// same path as the widget's Theme menu.
struct ThemeGalleryView: View {
    @Bindable var vm: UsageViewModel
    // Match Desk's card follows Desk's ink as it changes.
    @AppStorage(DeskLook.inkKey, store: .desk) private var deskInk = "ffffff"
    @AppStorage(DeskLook.shadowKey, store: .desk) private var deskShadow = true

    private let columns = [GridItem(.adaptive(minimum: 116, maximum: 160), spacing: 12)]

    var body: some View {
        // Reading the tick re-lists after Save, Reload or Delete changes the registry.
        let _ = vm.userThemesTick
        let items = ThemeGallery.withLiveDesk(
            ThemeGallery.installed(current: vm.theme.id), ink: deskInk, shadow: deskShadow)
        LazyVGrid(columns: columns, alignment: .leading, spacing: 14) {
            ForEach(items) { item in
                Button { vm.selectTheme(id: item.id) } label: {
                    ThemeCard(item: item)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(item.name)
                .accessibilityAddTraits(item.isCurrent ? .isSelected : [])
                .help(item.isUser ? "\(item.name) (your theme)" : item.name)
            }
        }
    }
}

/// A preview drawn from the palette: the background, a card with two usage bars on their
/// track and a pace tick, the accent strip along the top, the name underneath. Match Desk has
/// no background, card or strip of its own, so its preview draws the ink over a stand-in
/// desktop.
private struct ThemeCard: View {
    let item: ThemeGalleryItem

    var body: some View {
        let p = item.theme.palette
        VStack(alignment: .leading, spacing: 6) {
            preview(p)
                .frame(height: 72)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .strokeBorder(item.isCurrent ? Color.accentColor : Color.secondary.opacity(0.3),
                                      lineWidth: item.isCurrent ? 2.5 : 1))
                .overlay(alignment: .topTrailing) {
                    if item.isCurrent {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 14))
                            .foregroundStyle(Color.white, Color.accentColor)
                            .padding(5)
                    }
                }
            HStack(spacing: 4) {
                Text(item.name)
                    .font(.callout.weight(item.isCurrent ? .semibold : .regular))
                    .lineLimit(1)
                    .truncationMode(.tail)
                if item.isUser {
                    Image(systemName: "person.crop.circle")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .contentShape(Rectangle())
    }

    private func preview(_ p: Theme.Palette) -> some View {
        let radius = min(p.cardCornerRadius, 6)
        return ZStack(alignment: .top) {
            if p.ink != nil {
                LinearGradient(colors: [Color.hex("3a4a5c"), Color.hex("1e2a36")],
                               startPoint: .topLeading, endPoint: .bottomTrailing)
            } else {
                p.bg
            }
            VStack(alignment: .leading, spacing: 0) {
                p.accent.frame(height: 4).opacity(p.ink == nil ? 1 : 0)
                VStack(alignment: .leading, spacing: 6) {
                    Capsule().fill(p.text).frame(width: 34, height: 3)
                    bar(p, fill: 0.38)
                    Capsule().fill(p.textDim).frame(width: 24, height: 3)
                    bar(p, fill: 0.68)
                }
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .shadow(color: .black.opacity(p.ink?.shadow == true ? 0.45 : 0), radius: 2, x: 0, y: 1)
                .background(
                    RoundedRectangle(cornerRadius: radius).fill(p.glass))
                .overlay(
                    RoundedRectangle(cornerRadius: radius)
                        .strokeBorder((p.borderTint ?? p.border).opacity(p.borderAlpha), lineWidth: 1))
                .padding(8)
            }
        }
    }

    /// A usage bar as the widget draws it: the track, the fill in the usage color (or the
    /// Desk ink) and the pace tick a little ahead of it.
    private func bar(_ p: Theme.Palette, fill: Double) -> some View {
        GeometryReader { g in
            let width = g.size.width
            let pace = min(width * CGFloat(fill + 0.12), width - 2)
            ZStack(alignment: .leading) {
                if let ink = p.ink {
                    let run = LinearGradient(colors: ink.stops, startPoint: .leading, endPoint: .trailing)
                    Capsule().fill(run).opacity(0.22)
                    Capsule().fill(run).frame(width: width * CGFloat(fill))
                } else {
                    Capsule().fill(p.barBg)
                    Capsule().fill(usageColor(fill * 100)).frame(width: width * CGFloat(fill))
                }
                Rectangle().fill(p.paceMarker)
                    .frame(width: 2, height: p.ink == nil ? nil : 8)
                    .offset(x: pace)
            }
        }
        .frame(height: 5)
    }
}
