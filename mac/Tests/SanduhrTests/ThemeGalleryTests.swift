import Testing
@testable import Sanduhr

/// The theme list Settings, Widget, Themes shows: the same themes, in the same order, as the
/// widget's Theme menu.

@Suite("Theme gallery")
struct ThemeGalleryTests {
    /// A theme copied from a built-in under a new id and name, as a user file would load.
    func user(_ id: String, _ name: String, like base: Theme = ThemeRegistry.builtIn[0]) -> Theme {
        Theme(id: id, displayName: name, palette: base.palette)
    }

    @Test func builtInsInOrder() {
        let items = ThemeGallery.items(builtIns: ThemeRegistry.builtIn, user: [], current: "aurora")
        #expect(items.map(\.id) == ["obsidian", "aurora", "ember", "mint", "626-labs", "matrix", "blueprint",
                                  "match-desk"])
        #expect(items.map(\.name).contains("Obsidian"))
        #expect(items.allSatisfy { !$0.isUser })
    }

    @Test func everyBuiltInHasATooltipLine() {
        let items = ThemeGallery.items(builtIns: ThemeRegistry.builtIn, user: [], current: "obsidian")
        for item in items {
            #expect(item.summary?.isEmpty == false, "no summary for \(item.id)")
            #expect(item.tooltip == "\(item.name)\n\(item.summary ?? "")")
        }
        #expect(Set(ThemeGalleryItem.builtInSummaries.keys) == Set(items.map(\.id)))
    }

    @Test func userThemeTooltipUsesItsOwnDescription() {
        var described = user("sunset", "Sunset")
        described.summary = ThemeGalleryItem.cleaned("  Warm dusk.  ")
        var takeover = user("obsidian", "My Obsidian")
        takeover.summary = ThemeGalleryItem.cleaned("   ")
        let items = ThemeGallery.items(builtIns: ThemeRegistry.builtIn,
                                       user: [described, takeover], current: "obsidian")
        #expect(items.first { $0.id == "sunset" }?.tooltip == "Sunset (your theme)\nWarm dusk.")
        // A user file over a built-in id has no line of its own and doesn't inherit the built-in's.
        #expect(items.first { $0.id == "obsidian" }?.tooltip == "My Obsidian (your theme)")
    }

    @Test func userThemesAppended() {
        let items = ThemeGallery.items(
            builtIns: ThemeRegistry.builtIn,
            user: [user("sunset", "Sunset"), user("neon", "Neon")], current: "obsidian")
        #expect(items.map(\.id).suffix(2) == ["sunset", "neon"])
        #expect(items.count == ThemeRegistry.builtIn.count + 2)
        #expect(items.filter(\.isUser).map(\.name) == ["Sunset", "Neon"])
    }

    @Test func currentFlagMarksOnlyTheOneInUse() {
        let items = ThemeGallery.items(
            builtIns: ThemeRegistry.builtIn, user: [user("sunset", "Sunset")], current: "sunset")
        #expect(items.filter(\.isCurrent).map(\.id) == ["sunset"])
        let none = ThemeGallery.items(builtIns: ThemeRegistry.builtIn, user: [], current: "gone")
        #expect(none.filter(\.isCurrent).isEmpty)
    }

    @Test func duplicateUserThemesListedOnce() {
        let items = ThemeGallery.items(
            builtIns: ThemeRegistry.builtIn,
            user: [user("sunset", "Sunset"), user("sunset", "Sunset Again")], current: "")
        #expect(items.filter { $0.id == "sunset" }.map(\.name) == ["Sunset"])
    }

    @Test func userThemeOverridesBuiltInInPlace() {
        // The registry's rule: a user file with a built-in's id replaces it where it stands.
        let items = ThemeGallery.items(
            builtIns: ThemeRegistry.builtIn, user: [user("ember", "My Ember")], current: "ember")
        #expect(items.count == ThemeRegistry.builtIn.count)
        #expect(items[2].id == "ember")
        #expect(items[2].name == "My Ember")
        #expect(items[2].isUser && items[2].isCurrent)
    }

    @Test func blankThemesSkipped() {
        let items = ThemeGallery.items(
            builtIns: ThemeRegistry.builtIn,
            user: [user("", "No Id"), user("blank", "  "), user("ok", "Fine")], current: "")
        #expect(items.filter(\.isUser).map(\.id) == ["ok"])
    }

    @Test func installedMatchesTheRegistry() {
        // The menu lists `ThemeRegistry.themes`; the gallery shows the same ids in the same order.
        let items = ThemeGallery.installed(current: ThemeRegistry.default.id)
        #expect(items.map(\.id) == ThemeRegistry.themes.map(\.id))
        #expect(items.filter(\.isCurrent).map(\.id) == [ThemeRegistry.default.id])
    }
}
