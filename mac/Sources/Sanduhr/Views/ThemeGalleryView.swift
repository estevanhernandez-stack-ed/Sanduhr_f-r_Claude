import SwiftUI

/// Settings, Widget, Themes: every theme as a small preview with its name, the one in use
/// marked. A click applies it to the widget through `UsageViewModel.selectTheme(id:)`, the
/// same path as the widget's Theme menu.
struct ThemeGalleryView: View {
    @Bindable var vm: UsageViewModel

    private let columns = [GridItem(.adaptive(minimum: 116, maximum: 160), spacing: 12)]

    var body: some View {
        // Reading the tick re-lists after Save, Reload or Delete changes the registry.
        let _ = vm.userThemesTick
        let items = ThemeGallery.installed(current: vm.theme.id)
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
/// track and a pace tick, the accent strip along the top, the name underneath.
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
            p.bg
            VStack(alignment: .leading, spacing: 0) {
                p.accent.frame(height: 4)
                VStack(alignment: .leading, spacing: 6) {
                    Capsule().fill(p.text).frame(width: 34, height: 3)
                    bar(p, fill: 0.38)
                    Capsule().fill(p.textDim).frame(width: 24, height: 3)
                    bar(p, fill: 0.68)
                }
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(
                    RoundedRectangle(cornerRadius: radius).fill(p.glass))
                .overlay(
                    RoundedRectangle(cornerRadius: radius)
                        .strokeBorder((p.borderTint ?? p.border).opacity(p.borderAlpha), lineWidth: 1))
                .padding(8)
            }
        }
    }

    /// A usage bar as the widget draws it: the track, the fill in the usage color and the
    /// pace tick a little ahead of it.
    private func bar(_ p: Theme.Palette, fill: Double) -> some View {
        GeometryReader { g in
            let width = g.size.width
            let pace = min(width * CGFloat(fill + 0.12), width - 2)
            ZStack(alignment: .leading) {
                Capsule().fill(p.barBg)
                Capsule().fill(usageColor(fill * 100)).frame(width: width * CGFloat(fill))
                Rectangle().fill(p.paceMarker).frame(width: 2).offset(x: pace)
            }
        }
        .frame(height: 5)
    }
}
