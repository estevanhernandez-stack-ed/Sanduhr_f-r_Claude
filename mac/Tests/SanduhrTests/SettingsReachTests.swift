import Testing
import Foundation
import CoreGraphics
@testable import Sanduhr

/// Settings v2, item 72, slice 3: reach. The search index, the anchors and their links, the
/// folding previews, Advanced, Add Line at the top and Text under the camera's height.
@Suite("Settings v2: search")
struct SettingsSearchTests {
    private func first(_ q: String) -> SettingsEntry? { SettingsSearch.hits(q).first?.entry }

    @Test func everyNameInTheTableIsIndexedAtItsAnchor() {
        for (name, page) in SettingsNames.table {
            let anchor = SettingsNames.anchors[name]
            #expect(anchor != nil, "\(name) has no anchor")
            if let anchor { #expect(SettingsAnchor.exists(anchor, on: page), "\(name): \(anchor) is not on \(page)") }
            let hit = SettingsSearch.hits(name).first { $0.entry.title == name }
            #expect(hit?.entry.page == page && hit?.entry.anchor == anchor && hit?.rank == 0, "\(name) is not found")
        }
    }

    @Test func everyPageAndAnchorIsIndexed() {
        for page in SettingsSection.allCases {
            #expect(SettingsSearch.index.contains { $0.page == page && $0.anchor == nil && $0.title == page.title })
        }
        for e in SettingsAnchor.all {
            #expect(SettingsSearch.index.contains(e))
            #expect(SettingsSearch.hits(e.title).contains { $0.entry == e }, "\(e.title) is not found")
        }
    }

    @Test func anchorsAreUniquePerPage() {
        for page in SettingsSection.allCases {
            let ids = SettingsAnchor.entries(on: page).compactMap(\.anchor)
            let distinctTitles = Set(SettingsAnchor.entries(on: page).map(\.title))
            #expect(distinctTitles.count == SettingsAnchor.entries(on: page).count, "\(page) repeats a title")
            // Menu bar and Menu Bar Shows share one row on General; every other id is one section.
            let shared = page == .general ? 1 : 0
            #expect(Set(ids).count == ids.count - shared, "\(page) repeats an anchor")
        }
    }

    @Test func theSpecsWordsFindTheirControl() {
        #expect(first("glow").map { [$0.page.rawValue, $0.anchor] } == ["notch", SettingsAnchor.glow])
        #expect(first("percent").map { [$0.page.rawValue, $0.anchor] } == ["general", SettingsAnchor.menuBarShows])
        #expect(first("hourglass")?.anchor == SettingsAnchor.menuBarShows)
        let margins = first("margins")
        #expect(margins?.page == .deskLayout && margins?.anchor == SettingsAnchor.margins && margins?.advanced == true)
        #expect(first("corner")?.anchor == SettingsAnchor.pieces)
        #expect(first("position")?.anchor == SettingsAnchor.pieces)
        #expect(first("statusline")?.page == .integrations)
        #expect(first("prompt")?.page == .integrations || first("prompt")?.page == .watchers)
        #expect(SettingsSearch.hits("prompt").contains { $0.entry.page == .integrations })
        #expect(first("Option+S")?.anchor == SettingsAnchor.shortcuts)
        #expect(first("hex")?.page == .deskLook && first("hex")?.advanced == true)
        #expect(first("Add Line")?.page == .message)
        #expect(first("extra height")?.anchor == SettingsAnchor.size)
    }

    @Test func titleMatchesComeBeforeSynonyms() {
        // "glow" is in Desk Look's Colors only as a synonym; the titles with it come first.
        let hits = SettingsSearch.hits("glow")
        let firstSynonym = hits.firstIndex { $0.rank == 1 } ?? hits.count
        #expect(hits[..<firstSynonym].allSatisfy { $0.entry.title.lowercased().contains("glow") })
        #expect(hits.contains { $0.entry.page == .deskLook && $0.rank == 1 })
    }

    @Test func prefixAndWordMatchOnly() {
        #expect(SettingsSearch.matches(SettingsSearch.words("men bar"), "Menu Bar Shows"))
        #expect(SettingsSearch.matches(SettingsSearch.words("BAR SHO"), "Menu Bar Shows"))
        #expect(!SettingsSearch.matches(SettingsSearch.words("enu"), "Menu Bar Shows"))
        #expect(SettingsSearch.matches(SettingsSearch.words("fur"), "Quit Sanduhr für Claude"))
        #expect(SettingsSearch.hits("").isEmpty)
        #expect(SettingsSearch.hits("   ").isEmpty)
        #expect(SettingsSearch.hits("zzzz").isEmpty)
    }

    @Test func groupedListsEachPageOnceBestFirst() {
        let groups = SettingsSearch.grouped(SettingsSearch.hits("glow"))
        let pages = groups.map(\.page)
        // Title matches first, in sidebar order; Desk Look matches only by a synonym, so it is last.
        #expect(pages == [.notch, .watchers, .integrations, .deskLook])
        #expect(Set(pages).count == pages.count)
        #expect(groups.first?.entries.first == SettingsSearch.hits("glow").first?.entry)
        #expect(groups.first { $0.page == .notch }?.entries.map(\.title) == [SettingsNames.notchGlow])
        // The page's own entry is the page row, never a line under it.
        let accounts = SettingsSearch.grouped(SettingsSearch.hits("accounts"))
        #expect(accounts.first?.page == .credentials)
        #expect(accounts.flatMap(\.entries).allSatisfy { $0.anchor != nil })
    }
}

@Suite("Settings v2: anchors and links")
@MainActor
struct SettingsAnchorLinkTests {
    @Test func settingsLinksOpenAPageAtAnAnchor() throws {
        func target(_ s: String) -> (String?, String?) {
            let t = SettingsLink.target(URL(string: s)!)
            return (t.section?.rawValue, t.anchor)
        }
        #expect(target("sanduhr://settings") == (nil, nil))
        #expect(target("sanduhr://settings/") == (nil, nil))
        #expect(target("sanduhr://settings/notch") == ("notch", nil))
        #expect(target("sanduhr://settings/notch#glow") == ("notch", "glow"))
        #expect(target("estedesk://settings/notch#Glow") == ("notch", "glow"))
        #expect(target("sanduhr://settings/desk-layout#margins") == ("deskLayout", "margins"))
        #expect(target("sanduhr://settings/deskMeters") == ("alerts", SettingsAnchor.eachLimit))
        #expect(target("sanduhr://settings/pacing") == ("widgetLook", SettingsAnchor.pacing))
        #expect(target("sanduhr://settings/credentials#data") == ("credentials", SettingsAnchor.data))
        // An anchor the page doesn't have is dropped; a page this build doesn't know opens where it was.
        #expect(target("sanduhr://settings/notch#nope") == ("notch", nil))
        #expect(target("sanduhr://settings/nope#glow") == (nil, nil))
    }

    @Test func smokeHookTakesAnAnchor() {
        #expect(DebugLink.parse(URL(string: "sanduhr://debug/action?name=settings&arg=notch%20glow")!).command
                == .action(.settings(.notch, anchor: SettingsAnchor.glow), dir: nil))
        #expect((try? DebugLink.action("settings", arg: "notch#glow").get()) == .settings(.notch, anchor: "glow"))
        #expect((try? DebugLink.action("settings", arg: "watchers").get()) == .settings(.watchers, anchor: nil))
        #expect((try? DebugLink.action("settings", arg: "watchers above prompt").get())
                == .settings(.watchers, anchor: SettingsAnchor.abovePrompt))
        #expect((try? DebugLink.action("settings", arg: "deskMeters").get()) == .settings(.alerts, anchor: SettingsAnchor.eachLimit))
        #expect((try? DebugLink.action("settings", arg: "message new-line").get()) == .settings(.message, anchor: SettingsAnchor.newLine))
        guard case .failure(let e) = DebugLink.action("settings", arg: "notch nope") else {
            Issue.record("an unknown anchor parsed"); return
        }
        #expect(e.message.contains("notch has no anchor nope") && e.message.contains("glow"))
        guard case .failure = DebugLink.action("settings", arg: "nowhere") else {
            Issue.record("an unknown page parsed"); return
        }
        #expect((try? DebugLink.action("settings-search", arg: "glow").get()) == .settingsSearch("glow"))
        guard case .failure = DebugLink.action("settings-search", arg: "zzzz") else {
            Issue.record("a search that finds nothing parsed"); return
        }
        #expect(DebugAction.names.contains("settings-search"))
    }

    @Test func closingTheWindowFoldsPreviewsAndAdvancedAgain() {
        let nav = SettingsNavigation()
        nav.open(.deskLayout, anchor: SettingsAnchor.margins)
        nav.openPreviews.insert(.notch)
        nav.windowClosed()
        #expect(nav.openPreviews.isEmpty && nav.openAdvanced.isEmpty)
        #expect(nav.selection == .deskLayout)
    }

    @Test func openingAnAnchorScrollsLightsAndOpensAdvanced() {
        let nav = SettingsNavigation()
        nav.open(.deskLayout, anchor: SettingsAnchor.margins, highlight: true)
        #expect(nav.selection == .deskLayout && nav.anchor == SettingsAnchor.margins)
        #expect(nav.openAdvanced == [.deskLayout])
        #expect(nav.highlight == SettingsAnchor.margins)
        let request = nav.scrollRequest
        nav.open(.deskLayout, anchor: SettingsAnchor.margins)
        #expect(nav.scrollRequest == request + 1)
        #expect(nav.highlight == nil)
        nav.open(.notch, anchor: SettingsAnchor.glow)
        #expect(nav.openAdvanced == [.deskLayout])
        nav.open(.notch, anchor: SettingsAnchor.size)
        #expect(nav.openAdvanced == [.deskLayout, .notch])
        nav.open(.general, anchor: nil)
        #expect(nav.anchor == nil && nav.scrollRequest == request + 3)
        #expect(nav.openFirstHit("percent") && nav.selection == .general && nav.anchor == SettingsAnchor.menuBarShows)
        #expect(!nav.openFirstHit("zzzz"))
    }

    @Test func anchorVisibleReadsTheMarkedFrames() {
        let nav = SettingsNavigation()
        #expect(nav.anchorVisible == nil)
        nav.open(.notch, anchor: SettingsAnchor.glow)
        #expect(nav.anchorVisible == false)
        nav.viewport = CGRect(x: 190, y: 200, width: 570, height: 400)
        nav.anchorFrames[SettingsAnchor.glow] = CGRect(x: 210, y: 210, width: 500, height: 30)
        #expect(nav.anchorVisible == true)
        nav.anchorFrames[SettingsAnchor.glow] = CGRect(x: 210, y: 700, width: 500, height: 30)
        #expect(nav.anchorVisible == false)
        // A new page forgets the old page's frames.
        nav.selection = .watchers
        #expect(nav.anchorFrames.isEmpty && nav.anchorVisible == nil)
    }

    @Test func visibilityNeedsTheTopLineInsideTheScrollingArea() {
        let view = CGRect(x: 0, y: 100, width: 500, height: 400)
        #expect(SettingsAnchorVisibility.isVisible(CGRect(x: 10, y: 100, width: 300, height: 300), in: view))
        #expect(SettingsAnchorVisibility.isVisible(CGRect(x: 10, y: 470, width: 300, height: 300), in: view))
        #expect(!SettingsAnchorVisibility.isVisible(CGRect(x: 10, y: 490, width: 300, height: 300), in: view))
        #expect(!SettingsAnchorVisibility.isVisible(CGRect(x: 10, y: 60, width: 300, height: 300), in: view))
        #expect(!SettingsAnchorVisibility.isVisible(CGRect(x: 10, y: 200, width: 0, height: 0), in: view))
        #expect(!SettingsAnchorVisibility.isVisible(CGRect(x: 600, y: 200, width: 50, height: 20), in: view))
    }

    @Test func previewsFoldBelow720Points() {
        #expect(SettingsPreviewFold.folds(windowHeight: 628))
        #expect(SettingsPreviewFold.folds(windowHeight: SettingsPreviewFold.defaultWindowHeight))
        #expect(!SettingsPreviewFold.folds(windowHeight: 720))
        #expect(!SettingsPreviewFold.folds(windowHeight: 900))
        #expect(!SettingsPreviewFold.folds(windowHeight: 0))
        #expect(SettingsPreviewFold.stripHeight == 44 && SettingsPreviewFold.defaultWindowHeight == 680)
        let nav = SettingsNavigation()
        nav.windowHeight = 628
        #expect(nav.previewFolds)
        nav.windowHeight = 800
        #expect(!nav.previewFolds)
    }

    @Test func advancedHoldsTheSpecsControls() {
        let advanced = SettingsAnchor.all.filter(\.advanced).map { "\($0.page.rawValue)#\($0.anchor ?? "")" }
        #expect(advanced == ["deskLayout#margins", "deskLook#hex", "notch#size", "notch#text-color"])
        #expect(SettingsAnchor.isAdvanced(SettingsAnchor.advanced, on: .notch))
        #expect(!SettingsAnchor.isAdvanced(SettingsAnchor.glow, on: .notch))
        #expect(!SettingsAnchor.isAdvanced(nil, on: .notch))
    }
}

@Suite("Settings v2: Add Line and Text under the camera")
@MainActor
struct SettingsReachEditTests {
    @Test func addLineGoesToTheTopOfItsDay() {
        let file = "# notes\nfirst.\nMon: monday.\nsecond.\n"
        var doc = MessageDocument(parsing: file)
        doc.insertAtTopOfGroup(MessageLine(text: "new."))
        #expect(doc.text == "# notes\nnew.\nfirst.\nMon: monday.\nsecond.\n")
        doc = MessageDocument(parsing: file)
        doc.insertAtTopOfGroup(MessageLine(when: .weekday("Mon"), text: "also monday."))
        #expect(doc.text == "# notes\nfirst.\nMon: also monday.\nMon: monday.\nsecond.\n")
        // No line for Sundays yet: the top of the list, under the notes.
        doc = MessageDocument(parsing: file)
        doc.insertAtTopOfGroup(MessageLine(when: .weekday("Sun"), text: "sunday."))
        #expect(doc.text == "# notes\nSun: sunday.\nfirst.\nMon: monday.\nsecond.\n")
        var notes = MessageDocument(parsing: "# only notes\n")
        notes.insertAtTopOfGroup(MessageLine(text: "a."))
        #expect(notes.text == "# only notes\na.\n")
        var empty = MessageDocument(parsing: "")
        empty.insertAtTopOfGroup(MessageLine(text: "a."))
        #expect(empty.text == "a.\n")
        // A new line with no text is left out of the file, as with add().
        var blank = MessageDocument(parsing: file)
        blank.insertAtTopOfGroup()
        #expect(blank.text == file)
    }

    @Test func addLineFromTextViewOpensTheNewRowInTheList() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("reach-\(UUID().uuidString).txt")
        try "a.\nb.\n".write(to: url, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: url) }
        let editor = MessageEditorModel(url: url)
        editor.load()
        editor.showText()
        editor.addLineAtTop(MessageLine(text: "new."))
        #expect(editor.mode == .list)
        #expect(editor.justAdded != nil && editor.expanded == editor.justAdded)
        #expect(editor.document.messageRows.first?.id == editor.justAdded)
        #expect(editor.currentText == "new.\na.\nb.\n")
        editor.load()
        #expect(editor.justAdded == nil)
    }

    @Test func rotationFoldsWhileAsShipped() {
        #expect(MessageRotationSummary.isStandard(rotate: "daily", mix: false, special: .stack))
        #expect(!MessageRotationSummary.isStandard(rotate: "hourly", mix: false, special: .stack))
        #expect(!MessageRotationSummary.isStandard(rotate: "daily", mix: true, special: .stack))
        #expect(MessageRotationSummary.text(rotate: "daily", mix: false, special: .stack)
                == "Once a day, Mix off, special days: \(MessageSpecialMode.stack.title.lowercased())")
    }

    @Test func textUnderTheCameraSetsAHeight() {
        #expect(NotchChin.height(turningTextOn: true, chin: 0) == NotchChin.textHeight)
        #expect(NotchChin.textHeight == 18)
        #expect(NotchChin.height(turningTextOn: true, chin: 26) == 26)
        #expect(NotchChin.height(turningTextOn: false, chin: 0) == 0)
        #expect(NotchChin.height(turningTextOn: false, chin: 30) == 30)
        // Again: nothing more changes.
        #expect(NotchChin.height(turningTextOn: true, chin: NotchChin.height(turningTextOn: true, chin: 0)) == 18)
        #expect(NotchChin.hint(chin: 18).contains("Advanced"))
        #expect(NotchChin.hint(chin: 0).hasPrefix("Extra height below is 0"))
    }
}
