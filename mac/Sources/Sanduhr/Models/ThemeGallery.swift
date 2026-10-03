import Foundation

/// One card in Settings, Widget, Themes: a theme, whether it is the one in use and whether it
/// came from the user's themes folder.
struct ThemeGalleryItem: Identifiable, Equatable {
    let theme: Theme
    let isCurrent: Bool
    let isUser: Bool

    var id: String { theme.id }
    var name: String { theme.displayName }
}

/// The theme list the Settings gallery shows, built the way `ThemeRegistry` merges themes so
/// the gallery and the widget's Theme menu list the same themes in the same order.
enum ThemeGallery {
    /// Built-ins first, in their order, then user themes in theirs. A user theme that shares a
    /// built-in's id takes that built-in's place (the registry's rule: user files override).
    /// A theme with a blank id or name is skipped, as is a second theme with an id already
    /// listed from the same source. `current` marks the theme in use; an id that matches
    /// nothing marks none.
    static func items(builtIns: [Theme], user: [Theme], current: String) -> [ThemeGalleryItem] {
        var themes: [(theme: Theme, isUser: Bool)] = []
        var position: [String: Int] = [:]
        for theme in builtIns where isValid(theme) && position[theme.id] == nil {
            position[theme.id] = themes.count
            themes.append((theme, false))
        }
        var userSeen: Set<String> = []
        for theme in user where isValid(theme) && !userSeen.contains(theme.id) {
            userSeen.insert(theme.id)
            if let i = position[theme.id] {
                themes[i] = (theme, true)
            } else {
                position[theme.id] = themes.count
                themes.append((theme, true))
            }
        }
        return themes.map {
            ThemeGalleryItem(theme: $0.theme, isCurrent: $0.theme.id == current, isUser: $0.isUser)
        }
    }

    /// The gallery for this session: the compiled-in themes plus every user theme that loaded.
    /// A user file that failed to parse never reached the registry, so it is not shown.
    static func installed(current: String) -> [ThemeGalleryItem] {
        let builtIns = ThemeRegistry.builtIn
        let user = ThemeRegistry.themes.filter { !builtIns.contains($0) }
        return items(builtIns: builtIns, user: user, current: current)
    }

    private static func isValid(_ theme: Theme) -> Bool {
        !theme.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !theme.displayName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}
