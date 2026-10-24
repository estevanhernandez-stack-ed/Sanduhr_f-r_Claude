# Settings v2 for Windows, Slice 1: Names and Copy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every Settings control, menu item and piece of agent prose on Windows one name and one place-form ("Settings, <Page>, <Section>"), drawn from `SettingsNames` and pinned by tests that scan App, Core and Mcp, without moving a single page.

**Architecture:** `SettingsNames` (Core) gains the renamed and new names, the menu names, the core-word definitions, the page titles still to come, and a pure `PathProblems(text)` checker. `SettingsSourceScanTests` widens from App to App, Core and Mcp, checks every "Settings, …" path against real pages and `SettingsIndex` sections, and checks that a visible label starts its spoken name. The App then reads the names through `x:Static` or the constants; one new `TrademarkFooter` control replaces six copies of the notice. Old names go on `SettingsNames.Retired` in the same task that removes their last use, so the scan stays green after every task.

**Tech Stack:** .NET 10, WPF (XAML), xUnit, CommunityToolkit.Mvvm. Tests run from `windows-dotnet/`.

**Spec:** `docs/settings-v2-windows/spec.md`, section "Slice 1: names and copy" (the authority on scope), with the IA, the mapping table and "Outside Settings". Findings cited as WF<n> are in `docs/settings-v2-windows/findings.md`; W<n> and file line references are in `docs/settings-v2-windows/inventory.md`. Read the spec's slice 1 section and the mapping table before starting.

## Global Constraints

- **No page moves in this slice.** No TabItem is added, removed or reordered; no section changes page. New page titles go into `SettingsNames` as constants and stay unused until their slice.
- **CRLF working tree, LF index (`core.autocrlf=true`).** Change tracked files with the Edit tool only. Never `sed -i`, never a shell rewrite.
- **Keys:** no `settings.json` key is renamed, removed or changes meaning. `SettingsKeysContractTests` must pass unchanged; this slice adds no key.
- **Trust boundary:** the MCP link list in `src/Sanduhr.Mcp/Sanduhr.Mcp.csproj` is unchanged and `TrustBoundaryTests` is not edited. String edits inside `Sanduhr.Mcp/*.cs` are fine; no new `<Compile Include>` may appear. The MCP never references `SettingsNames`: its copies of a name are literals, and the Sanduhr.Tests scan holds them to the table.
- **Hands off the live app:** never start, stop or touch `Sanduhr.exe`, and never launch the app to check a change. No test reads the real AppData, LocalAppData, `%USERPROFILE%\.sanduhr` or Credential Manager. New tests are Core tests or source scans over the repo's files; the one new App test builds its `Paths` from a temp folder for all four bases.
- **Voice:** plain words, sentence-case commands ("Export meter history…"), a trailing "…" only on a command that asks before it acts, minimal em-dashes (none in new copy), periods at the end of captions. Proper names keep capitals: Claude, Claude Code, Sanduhr, What's New, Deep Work, Desk.
- **Shared files with slice 2, which runs in parallel:** both slices edit `Sanduhr.App/App.xaml.cs`, `Views/SettingsWindow.xaml`, `Sanduhr.Core/WhatsNewCards.cs`, `Sanduhr.Core/WelcomeTour.cs` and `tests/Sanduhr.Tests/SettingsIndexTests.cs`.
  - **Slice 1 owns label and automation-name strings. Slice 2 owns anchors, entry points, focus and layout.**
  - Every slice 1 edit in those files changes the value of an existing `Content`, `Text`, `Header`, `ToolTip` or `AutomationProperties.Name` attribute, or adds one attribute to an existing element, or one sibling element, on its own line. No re-indenting, no element moved, no attribute reordered. That keeps the rebase to line-local hunks.
  - **Slice 1 alone edits `SettingsNames.cs` strings. Slice 2 alone edits `SettingsIndex.cs`.** This plan does not open `SettingsIndex.cs`; the hand-offs at the end list what slice 2 changes there.
  - `WhatsNewCards.cs` is not edited by this plan (its bodies already pass every scan); slice 2 adds the anchors.
- **Slice 0 as built:** Clear history on "All accounts" clears every account and its confirmation names that. Nothing here changes Clear history.
- Every commit ends with the trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (the second `-m` in each commit step).
- Commands run from `windows-dotnet/`. `dotnet test tests/Sanduhr.Tests` builds Core and the tests only; steps that touch App code build the App through `tests/Sanduhr.App.Tests` or `dotnet build src/Sanduhr.App`.

## Review Focus

1. **Old words people still type.** Someone searches "vault", "history vault", "homes" or "csv" after the renames: each still lands on the renamed control, because `SettingsIndex` synonyms are untouched. Pinned in Task 4 (`Old_words_still_find_the_renamed_controls`).
2. **Agent remedies keep the words agents and tests key on.** The MCP remedies still say "publish token", "Endpoint URL", "Set token", "Publish usage", "vault", "re-enable", "Do not retry", and the watchers reason stays `watchers_off`. Pinned by the existing `Sanduhr.Mcp.Tests`, run in Task 2.
3. **A "Settings, " literal split across two C# string pieces** (`"... (Settings, " + "Desk); ..."` in `ToolCatalog.cs`) must not read as an unknown page, and lowercase prose after a comma ("Settings, About, or the tray menu") is not a section. Pinned in Task 1 (`PathProblems_passes_real_places`).
4. **The two-radio List | Text segment:** WPF writes `false` to the radio that loses its check. That write must not flip the mode back, and edits must carry over. Pinned in Task 8 (`Unchecking_a_mode_does_not_switch_back`).
5. **Folder paths with spaces or a long profile path in per-row names.** "Forget folder <folder>" and "Let Claude read this folder's project totals, <folder>" must keep the whole path, spaces included, after the visible words. Pinned in Task 3 (`Row_names_start_with_the_label_and_keep_the_whole_folder`).

---

## File Structure

| File | Responsibility in this slice |
|---|---|
| `src/Sanduhr.Core/SettingsNames.cs` (modify) | Every new and renamed name, menu names, core words, titles to come, group headings, `PathProblems`, `ForgetFolderFor`, `LetClaudeReadTotalsFor`, `FollowAccountCaption`, retired names. |
| `src/Sanduhr.Core/DeskHitTest.cs` (modify) | The Desk piece menu item reads "Message settings…" from the table. |
| `src/Sanduhr.Core/WelcomeTour.cs` (modify) | Tour bodies carry the core-word definitions. |
| `src/Sanduhr.Mcp/ToolLogic.cs`, `ToolLogic.Desk.cs`, `ToolLogic.Watchers.cs`, `ToolCatalog.cs` (modify) | Remedy prose in the "Settings, <Page>, <Section>" form; "Let Claude show watchers". Strings only. |
| `src/Sanduhr.App/Views/SettingsWindow.xaml` (modify) | Labels and automation names; two group headings; three footers; About path wrapping and the "Nothing archived yet." line; the Desk definition line; the Accounts hint. |
| `src/Sanduhr.App/Views/TrademarkFooter.xaml(.cs)` (create) | The one trademark notice control. |
| `src/Sanduhr.App/Views/DeskMessagePage.xaml`, `ViewModels/DeskMessagePageViewModel.cs` (modify) | List and Text segment; Message color and Message font labels; Daily and Hourly names; footer. |
| `src/Sanduhr.App/Views/WidgetsPage.xaml` (modify) | Widget-pack definition line; footer. |
| `src/Sanduhr.App/Views/ManualKeyWindow.xaml(.cs)` (modify) | Plain sessionKey steps for Edge and Chrome; cf_clearance behind "Seeing a Cloudflare error?". |
| `src/Sanduhr.App/Views/VaultConsentDialog.xaml`, `Views/ResetWindow.xaml(.cs)`, `Views/TierCard.xaml.cs` (modify) | Copy and labels. |
| `src/Sanduhr.App/App.xaml.cs`, `MainWindow.xaml(.cs)`, `Desk/DeskClicks.cs` (modify) | Tray, widget menu, icon strip and Desk menu names from the table. |
| `src/Sanduhr.App/Services/UsagePublishService.cs` (modify) | Remedy prose form. |
| `src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs`, `ModsConfigViewModel.cs`, `ThemesViewModel.cs`, `ThemeStudioViewModel.cs`, `CcLedgerViewModel.cs`, `LocalCcViewModel.cs`, `PublishViewModel.cs` (modify) | Strings only. |
| `src/Sanduhr.App/ViewModels/WidgetViewModel.cs`, `SettingsViewModel.cs`, `Views/SettingsWindow.xaml.cs` (modify) | Stale comments (WF47). |
| `tests/Sanduhr.Tests/SettingsNamesTests.cs`, `SettingsSourceScanTests.cs`, `SettingsIndexTests.cs`, `DeskHitTestTests.cs`, `WelcomeTourTests.cs` (modify) | The tests. |
| `tests/Sanduhr.App.Tests/RowControlNamesTests.cs`, `CcPieceViewModelTests.cs` (modify), `MessageModeSegmentTests.cs` (create) | App-side expectations. |

---

### Task 1: The path checker, group headings and the titles to come

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs:24-32` (Sidebar), after `:344` (new members)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml:1685`, `:2312` (two `GroupHeader` values)
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs:185`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<string> SettingsNames.PathProblems(string text)`; `SettingsNames.TitlesToCome` (`IReadOnlyList<string>`); consts `PrivacyTitle`, `DeskLookTitle`, `WatchersTitle`, `LauncherTitle`, `ModsAndWidgetsTitle`. Sidebar group titles become `null, "Usage", "Extend", "Widget", "Desktop", "Help"`.
- Problem strings, exact: `"Settings ▸": write "Settings, <Page>, <Section>"` (same for `"Settings >"` and `"Settings ›"`), `no page "<words>" after "Settings, "`, `no section "<words>" on <Page title>`.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsNamesTests` (before the final `}`):

```csharp
    [Fact]
    public void Group_titles_follow_the_v2_sidebar()
    {
        // "Extend" holds Claude Code and Mods (it will hold widget packs too); "Desktop" no longer repeats its first page.
        Assert.Equal(new string?[] { null, "Usage", "Extend", "Widget", "Desktop", "Help" },
            SettingsNames.Sidebar.Select(g => g.Title).ToArray());
    }

    [Fact]
    public void Titles_to_come_are_pinned_and_open_no_page_yet()
    {
        Assert.Equal(["Privacy", "Desk Look", "Watchers", "Launcher", "Mods & Widgets"], SettingsNames.TitlesToCome);
        foreach (var title in SettingsNames.TitlesToCome)
            Assert.Null(SettingsNames.Linked(title + " settings\u2026"));
    }

    [Theory]
    [InlineData("Turn it on in Settings, Desk, then place Watchers.")]
    [InlineData("Sign in any time from Settings, Accounts.")]
    [InlineData("(Settings, Now playing, Looks)")]
    [InlineData("in the Sanduhr widget (Settings, Publish usage, Destination).")]
    [InlineData("Settings, Claude Usage, Overview")]
    [InlineData("You can take the tour again from Settings, About, or the tray menu.")]
    [InlineData("Show in Explorer, settings, history and themes")]
    [InlineData("has not turned on 'Let Claude show watchers' in Sanduhr (Settings, ")]
    [InlineData("Settings, redesigned")]
    [InlineData("")]
    public void PathProblems_passes_real_places(string text) =>
        Assert.Empty(SettingsNames.PathProblems(text));

    [Theory]
    [InlineData("(Settings, Desk, Watchers)", "no section \"Watchers\" on Desk")]
    [InlineData("in Settings \u25B8 Claude Usage.", "\"Settings \u25B8\": write \"Settings, <Page>, <Section>\"")]
    [InlineData("(Settings > Publish usage > Endpoint URL)", "\"Settings >\": write \"Settings, <Page>, <Section>\"")]
    [InlineData("Open Settings, Privacy.", "no page \"Privacy\" after \"Settings, \"")]
    public void PathProblems_names_the_problem(string text, string problem) =>
        Assert.Contains(problem, SettingsNames.PathProblems(text));
```

Change `SettingsIndexTests.cs:185`:

```csharp
        Assert.Equal("Desk", SettingsNames.GroupTitle(SettingsPage.Widgets));
```

to:

```csharp
        Assert.Equal("Desktop", SettingsNames.GroupTitle(SettingsPage.Widgets));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests"`
Expected: build FAILS with `'SettingsNames' does not contain a definition for 'TitlesToCome'` and `'PathProblems'`.

- [ ] **Step 3: Implement in `SettingsNames.cs`**

Replace the Sidebar block (lines 22-32):

```csharp
    /// <summary>The sidebar: six groups, each page once. Widget is not a page (Show me on a widget card
    /// shows the widget itself).</summary>
    public static IReadOnlyList<SettingsGroup> Sidebar { get; } =
    [
        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts]),
        new("Usage", [SettingsPage.History, SettingsPage.ClaudeUsage, SettingsPage.Publish]),
        new("Claude Code", [SettingsPage.ClaudeCode, SettingsPage.Mods]),
        new("Widget", [SettingsPage.Themes]),
        new("Desk", [SettingsPage.Desk, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather, SettingsPage.Widgets]),
        new("Help", [SettingsPage.About]),
    ];
```

with:

```csharp
    /// <summary>The sidebar: six groups, each page once. Widget is not a page (Show me on a widget card
    /// shows the widget itself). Settings v2 names the groups: "Extend" (it will hold widget packs, which
    /// are not Claude Code's) and "Desktop" (the Mac's word, and no longer its first page's title).</summary>
    public static IReadOnlyList<SettingsGroup> Sidebar { get; } =
    [
        new(null, [SettingsPage.General, SettingsPage.Accounts, SettingsPage.Alerts]),
        new("Usage", [SettingsPage.History, SettingsPage.ClaudeUsage, SettingsPage.Publish]),
        new("Extend", [SettingsPage.ClaudeCode, SettingsPage.Mods]),
        new("Widget", [SettingsPage.Themes]),
        new("Desktop", [SettingsPage.Desk, SettingsPage.DeskMessage, SettingsPage.NowPlaying, SettingsPage.Weather, SettingsPage.Widgets]),
        new("Help", [SettingsPage.About]),
    ];

    // ---- Page titles still to come (Settings v2). Unused until their slice adds the page: Privacy
    // (slice 6), Desk Look, Watchers and Launcher (slice 3), Mods & Widgets (slice 4, the Mods page's new title).
    public const string PrivacyTitle = "Privacy";
    public const string DeskLookTitle = "Desk Look";
    public const string WatchersTitle = "Watchers";
    public const string LauncherTitle = "Launcher";
    public const string ModsAndWidgetsTitle = "Mods & Widgets";

    /// <summary>The titles above, in sidebar order of their future pages.</summary>
    public static IReadOnlyList<string> TitlesToCome { get; } =
        [PrivacyTitle, DeskLookTitle, WatchersTitle, LauncherTitle, ModsAndWidgetsTitle];
```

Insert before the final `}` of the class (after `IsWindowsOwn`):

```csharp

    /// <summary>
    /// What is wrong with the places <paramref name="text"/> names, empty when nothing is. Prose names a
    /// place one way only: "Settings, &lt;Page&gt;" or "Settings, &lt;Page&gt;, &lt;Section&gt;", where the
    /// page is a sidebar title and the section is the title of one of that page's anchors in
    /// <see cref="SettingsIndex"/>. "Settings ▸ X" and "Settings &gt; X" are the old forms. Only a
    /// capitalised word after a comma is read as a page or section, so "Settings, About, or the tray
    /// menu" and a literal that ends in "Settings, " pass.
    /// </summary>
    public static IReadOnlyList<string> PathProblems(string text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text))
            return found;
        foreach (var old in new[] { "Settings \u25B8", "Settings >", "Settings \u203A" })
        {
            if (text.Contains(old, StringComparison.Ordinal))
                found.Add($"\"{old}\": write \"Settings, <Page>, <Section>\"");
        }
        const string lead = "Settings, ";
        int at = 0;
        while ((at = text.IndexOf(lead, at, StringComparison.Ordinal)) >= 0)
        {
            at += lead.Length;
            var rest = text[at..];
            if (rest.Length == 0 || !char.IsUpper(rest[0]))
                continue;
            SettingsPage? page = null;
            foreach (var p in Pages)
            {
                if (StartsWithWord(rest, PageTitle(p)) && (page is null || PageTitle(p).Length > PageTitle(page.Value).Length))
                    page = p;
            }
            if (page is null)
            {
                found.Add($"no page \"{FirstWords(rest)}\" after \"Settings, \"");
                continue;
            }
            var after = rest[PageTitle(page.Value).Length..];
            if (after.Length > 2 && after.StartsWith(", ", StringComparison.Ordinal) && char.IsUpper(after[2]))
            {
                var section = after[2..];
                if (!SettingsIndex.EntriesOn(page.Value).Any(e => StartsWithWord(section, e.Title)))
                    found.Add($"no section \"{FirstWords(section)}\" on {PageTitle(page.Value)}");
            }
        }
        return found;
    }

    private static bool StartsWithWord(string text, string words) =>
        text.StartsWith(words, StringComparison.Ordinal)
        && (text.Length == words.Length || !char.IsLetterOrDigit(text[words.Length]));

    private static string FirstWords(string text)
    {
        var end = text.IndexOfAny([',', '.', ')', ';', ':']);
        return end < 0 ? text : text[..end];
    }
```

In `Views/SettingsWindow.xaml`, change line 1685's attribute value:

```xml
                         views:SettingsSidebar.GroupHeader="Claude Code">
```

to:

```xml
                         views:SettingsSidebar.GroupHeader="Extend">
```

and on line 2312 replace `views:SettingsSidebar.GroupHeader="Desk"` with `views:SettingsSidebar.GroupHeader="Desktop"` (same line, nothing else on it changes).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: PASS (`The_sidebar_in_XAML_matches_the_names_table` passes with the two new headings).

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs
git commit -m "feat(settings-v2): path checker, Extend and Desktop groups, titles to come" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: One form for every Settings path, in App, Core and Mcp (WF18, WF10)

**Files:**
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs:26-64` (helpers), new facts
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs:147-148` (`WatchersSwitch`), `Retired`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/UsagePublishService.cs:164,169,174`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml:53`
- Modify: `windows-dotnet/src/Sanduhr.Mcp/ToolLogic.cs:133,361,619,771,774,777,814`, `ToolLogic.Desk.cs:41-42`, `ToolLogic.Watchers.cs:114`, `ToolCatalog.cs:686`

**Interfaces:**
- Consumes: `SettingsNames.PathProblems` (Task 1).
- Produces: `SettingsNames.WatchersSwitch == "Let Claude show watchers"`. Scan helpers `FilesIn(string root, string pattern)`, `CsStringsIn(string root, string prefix)`, `CoreAndMcpStrings()` and the `NotScanned` list (`SettingsNames.cs`, `SettingsIndex.cs`), used by Tasks 3 to 10.

- [ ] **Step 1: Write the failing tests**

In `SettingsSourceScanTests.cs`, replace the `Files` and `CsStrings` helpers (lines 35-64):

```csharp
    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(AppDir(), pattern, SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(AppDir(), f).Replace('\\', '/');
                return !rel.StartsWith("bin/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.Contains("/obj/");
            })
            .OrderBy(f => f, StringComparer.Ordinal);

    private static IEnumerable<(string Where, string Text)> CsStrings()
    {
        foreach (var file in Files("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//"))
                    continue;
                foreach (Match m in CsLiteral.Matches(lines[i]))
                {
                    string text;
                    try { text = Regex.Unescape(m.Groups[1].Value); }
                    catch (ArgumentException) { text = m.Groups[1].Value; }
                    yield return ($"{Path.GetRelativePath(AppDir(), file)}:{i + 1}", text);
                }
            }
        }
    }
```

with:

```csharp
    private static string SrcDir() => Path.GetFullPath(Path.Combine(AppDir(), ".."));

    /// <summary>Files the scan leaves out. The names table holds every retired name on purpose. The search
    /// index is slice 2's file: its titles keep old words until slice 2 points them at the table, and it
    /// leaves this list then.</summary>
    private static readonly string[] NotScanned = ["SettingsNames.cs", "SettingsIndex.cs"];

    private static IEnumerable<string> FilesIn(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                return !rel.StartsWith("bin/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.Contains("/obj/")
                    && !NotScanned.Contains(Path.GetFileName(f));
            })
            .OrderBy(f => f, StringComparer.Ordinal);

    private static IEnumerable<string> Files(string pattern) => FilesIn(AppDir(), pattern);

    private static IEnumerable<(string Where, string Text)> CsStringsIn(string root, string prefix)
    {
        foreach (var file in FilesIn(root, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//"))
                    continue;
                foreach (Match m in CsLiteral.Matches(lines[i]))
                {
                    string text;
                    try { text = Regex.Unescape(m.Groups[1].Value); }
                    catch (ArgumentException) { text = m.Groups[1].Value; }
                    yield return ($"{prefix}{Path.GetRelativePath(root, file)}:{i + 1}", text);
                }
            }
        }
    }

    private static IEnumerable<(string Where, string Text)> CsStrings() => CsStringsIn(AppDir(), "");

    /// <summary>Core's and the MCP server's literals, prefixed "Core/" and "Mcp/". The MCP cannot read
    /// SettingsNames (its link list is fixed), so this is what holds its copies of a name to the table.</summary>
    private static IEnumerable<(string Where, string Text)> CoreAndMcpStrings() =>
        CsStringsIn(Path.Combine(SrcDir(), "Sanduhr.Core"), "Core/")
            .Concat(CsStringsIn(Path.Combine(SrcDir(), "Sanduhr.Mcp"), "Mcp/"));
```

Add these facts after `App_strings_use_no_retired_name_and_link_to_no_unknown_page`:

```csharp
    [Fact]
    public void Core_and_Mcp_strings_use_no_retired_name_and_link_to_no_unknown_page()
    {
        var bad = new List<string>();
        foreach (var (where, text) in CoreAndMcpStrings())
        {
            var retired = SettingsNames.RetiredNames(text);
            var links = SettingsNames.UnknownLinks(text);
            if (retired.Count > 0)
                bad.Add($"{where}: retired [{string.Join(", ", retired)}] in \"{text}\"");
            if (links.Count > 0)
                bad.Add($"{where}: unknown link [{string.Join(", ", links)}] in \"{text}\"");
        }
        Assert.True(bad.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Every_Settings_path_names_a_real_page_and_section()
    {
        var bad = CsStrings().Concat(XamlStrings()).Concat(CoreAndMcpStrings())
            .SelectMany(x => SettingsNames.PathProblems(x.Text).Select(p => $"{x.Where}: {p} in \"{x.Text}\""))
            .ToList();
        Assert.True(bad.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void The_Core_and_Mcp_scan_sees_known_strings()
    {
        var strings = CoreAndMcpStrings().ToList();
        Assert.Contains(strings, x => x.Where.StartsWith("Core/Weather.cs", StringComparison.Ordinal)
            && x.Text == "Set a city in Settings, Weather");
        Assert.Contains(strings, x => x.Where.StartsWith("Mcp/ToolLogic.Watchers.cs", StringComparison.Ordinal)
            && x.Text.Contains(SettingsNames.WatchersSwitch, StringComparison.Ordinal));
        Assert.DoesNotContain(strings, x => x.Where.Contains("SettingsNames.cs", StringComparison.Ordinal));
    }
```

In `SettingsNamesTests.cs`, append:

```csharp
    [Fact]
    public void The_agent_switches_say_Claude()
    {
        Assert.Equal("Let Claude show watchers", SettingsNames.WatchersSwitch);
        Assert.Equal(["Let agents show watchers"], SettingsNames.RetiredNames("turned on Let agents show watchers"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests"`
Expected: FAIL. `Every_Settings_path_names_a_real_page_and_section` lists the `Settings >` and `Settings ▸` lines (UsagePublishService.cs 164/169/174, VaultConsentDialog.xaml:53, Mcp/ToolLogic.cs 133/361/619/771/774/777/814, Mcp/ToolLogic.Desk.cs:42) and `no section "Watchers" on Desk` at Mcp/ToolLogic.Watchers.cs:114; `The_agent_switches_say_Claude` fails on the switch's value; `The_Core_and_Mcp_scan_sees_known_strings` fails on the watchers literal.

- [ ] **Step 3: Rename the switch and retire the old name in `SettingsNames.cs`**

Replace:

```csharp
    /// <summary>Desk: lets agents put watchers up through the MCP server (settings.json "watchers_agents", off by default).</summary>
    public const string WatchersSwitch = "Let agents show watchers";
```

with:

```csharp
    /// <summary>Desk: lets Claude put watchers up through the MCP server (settings.json "watchers_agents", off by default).
    /// "Claude" means any agent using Sanduhr's MCP server, the one word every agent consent uses (WF10).</summary>
    public const string WatchersSwitch = "Let Claude show watchers";
```

In `Retired`, after `("Save & Apply", SaveAndApply, false),` add:

```csharp
        ("Let agents show watchers", WatchersSwitch, false),
```

- [ ] **Step 4: Rewrite the App prose**

`Services/UsagePublishService.cs`, replace the three literals:

```csharp
                "Publishing is off. Turn on 'Publish daily' in the Sanduhr widget (Settings > Publish usage).");
```
```csharp
                "Publishing is off. Turn on 'Publish daily at' in the Sanduhr widget (Settings, Publish usage, Publish daily).");
```

```csharp
                "No publish endpoint is configured. Set one in the Sanduhr widget (Settings > Publish usage > Endpoint URL, or pick a preset).");
```
```csharp
                "No publish endpoint is configured. Set the Endpoint URL, or pick a preset, in the Sanduhr widget (Settings, Publish usage, Destination).");
```

```csharp
                "No publish token is stored for the publish endpoint. Add one in the Sanduhr widget (Settings > Publish usage > Set token).");
```
```csharp
                "No publish token is stored for the publish endpoint. Add one with Set token… in the Sanduhr widget (Settings, Publish usage, Token).");
```

`Views/VaultConsentDialog.xaml:53`, replace the `Text` value:

```xml
                       Text="Claude Code deletes its session logs after about 30 days. Sanduhr can keep a local history vault of your usage totals so your trends survive — stored only on this machine, never uploaded. Choose which Claude Code homes to include; you can change this or erase everything any time in Settings &#x25B8; Claude Usage." />
```
```xml
                       Text="Claude Code deletes its session logs after about 30 days. Sanduhr can keep a session archive of your usage totals on this PC so your trends survive. It is never uploaded. Choose which Claude Code folders to include; you can change this or erase the archive any time in Settings, Claude Usage, Overview." />
```

- [ ] **Step 5: Rewrite the MCP prose (strings only; no link list change)**

`Sanduhr.Mcp/ToolLogic.cs`:

```csharp
                "No usage snapshot. Start the Sanduhr widget and enable its Claude Code integration (Settings > Claude Usage).");
```
```csharp
                "No usage snapshot. Start the Sanduhr widget and turn on its Claude Code integration (Settings, Claude Usage).");
```

```csharp
                  + "is already 3.4.0 or later, re-enable its Claude Code integration (Settings > Claude Usage)."
```
```csharp
                  + "is already 3.4.0 or later, re-enable its Claude Code integration (Settings, Claude Usage)."
```

```csharp
                "No vault history for the consented homes. Enable the history vault in the Sanduhr widget (Settings > Claude Usage) and let it ingest.");
```
```csharp
                "No archived history for the consented folders. Turn on the session archive (the vault) in the Sanduhr widget (Settings, Claude Usage, Overview) and let it ingest.");
```

```csharp
                "Publishing is off. Turn on 'Publish daily' in the Sanduhr widget (Settings > Publish usage) and tick the homes to share.");
```
```csharp
                "Publishing is off. Turn on 'Publish daily at' in the Sanduhr widget (Settings, Publish usage, Publish daily) and tick the folders to share.");
```

```csharp
                "No publish endpoint is configured. Set one in the Sanduhr widget (Settings > Publish usage > Endpoint URL, or pick a preset).");
```
```csharp
                "No publish endpoint is configured. Set the Endpoint URL, or pick a preset, in the Sanduhr widget (Settings, Publish usage, Destination).");
```

```csharp
                "No publish token is stored for the publish endpoint. Add one in the Sanduhr widget (Settings > Publish usage > Set token), or set Auth to None if the endpoint needs no credential.");
```
```csharp
                "No publish token is stored for the publish endpoint. Add one with Set token… in the Sanduhr widget (Settings, Publish usage, Token), or set Auth to None if the endpoint needs no credential.");
```

```csharp
                         + "The request stays queued and runs on the widget's next tick; check Settings > Publish usage for the result.",
```
```csharp
                         + "The request stays queued and runs on the widget's next tick; check Settings, Publish usage, Publish daily for the result.",
```

`Sanduhr.Mcp/ToolLogic.Desk.cs:42`:

```csharp
                    + "(Settings > Claude Code); the state appears within a few seconds. Then call get_desk again.");
```
```csharp
                    + "(Settings, Claude Code); the state appears within a few seconds. Then call get_desk again.");
```

`Sanduhr.Mcp/ToolLogic.Watchers.cs:114` (slice 3 changes it to "Settings, Watchers" when that page exists):

```csharp
                "The user has not turned on Let agents show watchers in Sanduhr (Settings, Desk, Watchers). "
```
```csharp
                "The user has not turned on 'Let Claude show watchers' in Sanduhr (Settings, Desk). "
```

`Sanduhr.Mcp/ToolCatalog.cs:686` (the next line, `"Desk); then do not retry. ..."`, stays):

```csharp
        "watchers_off when the user has not turned on Let agents show watchers in Sanduhr (Settings, " +
```
```csharp
        "watchers_off when the user has not turned on 'Let Claude show watchers' in Sanduhr (Settings, " +
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests"`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.Mcp.Tests`
Expected: PASS, including `TrustBoundaryTests`, `ToolLogicPublishTests` ("Publish usage", "publish endpoint", "Endpoint URL", "publish token", "Set token"), `ToolLogicWatchTests` ("Do not retry"), `ToolLogicHistoryFallbackTests` ("re-enable"), `ToolLogicAbilitiesTests` ("vault"), `McpServerTests` ("watchers_off").

Run: `git diff -- windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj`
Expected: no output.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Services/UsagePublishService.cs windows-dotnet/src/Sanduhr.App/Views/VaultConsentDialog.xaml windows-dotnet/src/Sanduhr.Mcp/ToolLogic.cs windows-dotnet/src/Sanduhr.Mcp/ToolLogic.Desk.cs windows-dotnet/src/Sanduhr.Mcp/ToolLogic.Watchers.cs windows-dotnet/src/Sanduhr.Mcp/ToolCatalog.cs
git commit -m "feat(settings-v2): one form for Settings paths across App, Core and Mcp; Let Claude show watchers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Spoken names start with the visible label (WF34, WF38 names, WF12 font names, WF10)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (new names and two formatters, one retired name)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (lines 670-672, 862-864, 930, 1115-1119, 1258, 1289, 1316, 1376, 1459, 1557-1610, 1746, 1759-1761, 1797-1801, 1864-1866, 2428-2433, 2591-2597, 2639-2654, 2946-2970, 2993-2994)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml:317-320,402,426`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs:66-67`
- Modify: `windows-dotnet/tests/Sanduhr.App.Tests/RowControlNamesTests.cs:60`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs`, `SettingsNamesTests.cs`

**Interfaces:**
- Consumes: scan helpers from Task 2.
- Produces: consts `DeskFont = "Desk font"`, `MessageFont = "Message font"`, `MessageColor = "Message color"`, `DeskInkPicker = "Clock, date, Claude"`, `CloseSettings = "Close Settings"`, `LetClaudeReadTotals = "Let Claude read this folder's project totals"`; `static string ForgetFolderFor(string folder)` returns `"Forget folder <folder>"`; `static string LetClaudeReadTotalsFor(string folder)` returns `"Let Claude read this folder's project totals, <folder>"`. Scan helpers `Resolve(string tag, string attr)`, `ResolveValue(string raw)` and `TabRange(string tag)` (used by Tasks 4, 7 and 10).

- [ ] **Step 1: Write the failing tests**

Append to `SettingsSourceScanTests`:

```csharp
    private static readonly Regex ControlTag = new("<(Button|CheckBox|RadioButton|ToggleButton)\\b[^>]*>", RegexOptions.Compiled);
    private static readonly Regex LabelThenField = new(
        "<TextBlock\\b[^>]*?\\bText=\"([^\"]+)\"[^>]*/>\\s*<(TextBox|ComboBox|Slider)\\b([^>]*)>", RegexOptions.Compiled);

    /// <summary>An attribute's value as shown: a literal (entities decoded), a SettingsNames member through
    /// x:Static, or null for a binding or anything else the scan can't read.</summary>
    private static string? ResolveValue(string raw)
    {
        var s = Regex.Match(raw, @"^\{x:Static core:SettingsNames\.(\w+)\}$");
        if (s.Success)
        {
            var t = typeof(SettingsNames);
            var member = s.Groups[1].Value;
            return (t.GetField(member)?.GetValue(null) ?? t.GetProperty(member)?.GetValue(null)) as string;
        }
        return raw.StartsWith('{') ? null : WebUtility.HtmlDecode(raw);
    }

    private static string? Resolve(string tag, string attr)
    {
        var m = Regex.Match(tag, @"(?<![\w.:])" + Regex.Escape(attr) + "=\"([^\"]*)\"");
        return m.Success ? ResolveValue(m.Groups[1].Value) : null;
    }

    /// <summary>A label as voice control hears it: no trailing ellipsis or colon.</summary>
    private static string Bare(string label) => label.Trim().TrimEnd('\u2026').TrimEnd(':').Trim();

    /// <summary>The window and the pages and panels it hosts.</summary>
    private static IEnumerable<string> SettingsXamlFiles()
    {
        var views = Path.Combine(AppDir(), "Views");
        return new[] { Path.Combine(views, "SettingsWindow.xaml") }
            .Concat(Directory.EnumerateFiles(views, "*Page.xaml").Order(StringComparer.Ordinal))
            .Concat(Directory.EnumerateFiles(views, "*Panel.xaml").Order(StringComparer.Ordinal));
    }

    /// <summary>One TabItem of SettingsWindow.xaml, from its start tag to its end tag.</summary>
    private static string TabRange(string tag)
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        var m = Regex.Match(xaml, "<TabItem\\b[^>]*?\\bTag=\"" + tag + "\"[^>]*>");
        Assert.True(m.Success, "no TabItem with Tag=" + tag);
        return xaml[m.Index..xaml.IndexOf("</TabItem>", m.Index, StringComparison.Ordinal)];
    }

    [Fact]
    public void Visible_labels_start_their_spoken_names()
    {
        // Voice control matches the words on screen (WF34): a control's spoken name starts with its visible
        // text; per-row context may follow ("Show in Explorer, session archive").
        var bad = new List<string>();
        int pairs = 0;
        foreach (var file in SettingsXamlFiles())
        {
            var src = XmlComment.Replace(File.ReadAllText(file), m => new string('\n', m.Value.Count(c => c == '\n')));
            string At(int index) => $"{Path.GetFileName(file)}:{1 + src.Take(index).Count(c => c == '\n')}";
            foreach (Match m in ControlTag.Matches(src))
            {
                var label = Resolve(m.Value, "Content");
                var name = Resolve(m.Value, "AutomationProperties.Name");
                if (label is null || name is null || !label.Any(char.IsLetter)) continue;
                pairs++;
                if (!name.StartsWith(Bare(label), StringComparison.Ordinal))
                    bad.Add($"{At(m.Index)}: shows \"{label}\", says \"{name}\"");
            }
            foreach (Match m in LabelThenField.Matches(src))
            {
                var label = ResolveValue(m.Groups[1].Value);
                var name = Resolve(m.Groups[3].Value, "AutomationProperties.Name");
                if (label is null || name is null) continue;
                pairs++;
                if (!name.StartsWith(Bare(label), StringComparison.Ordinal))
                    bad.Add($"{At(m.Index)}: label \"{label}\", says \"{name}\"");
            }
        }
        Assert.True(pairs >= 60, $"checked only {pairs} label and name pairs");
        Assert.True(bad.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Lists_the_close_button_and_the_shortcut_switch_are_named()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        Assert.Contains("AutomationProperties.Name=\"{x:Static core:SettingsNames.CloseSettings}\"", xaml);
        Assert.Matches("<ListBox x:Name=\"AccountsList\"[^>]*AutomationProperties.Name=\"Accounts\"", xaml);
        foreach (var list in new[] { "Top projects (last 30 days)", "Top skills (last 30 days)", "Top projects (window)", "Session archive folders", "Sessions" })
            Assert.Contains($"AutomationProperties.Name=\"{list}\"", TabRange("ClaudeUsage"));
        Assert.Contains("Content=\"{x:Static core:SettingsNames.ShortcutToOpenSettings}\"", TabRange("General"));
    }
```

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void Names_that_match_their_labels_are_pinned()
    {
        Assert.Equal("Desk font", SettingsNames.DeskFont);
        Assert.Equal("Message font", SettingsNames.MessageFont);
        Assert.Equal("Message color", SettingsNames.MessageColor);
        Assert.Equal("Clock, date, Claude", SettingsNames.DeskInkPicker);
        Assert.Equal("Close Settings", SettingsNames.CloseSettings);
        Assert.Equal("Let Claude read this folder's project totals", SettingsNames.LetClaudeReadTotals);
        Assert.Equal(["Claude can read this folder's project totals"],
            SettingsNames.RetiredNames("Claude can read this folder's project totals (MCP)"));
    }

    [Theory]
    [InlineData(@"~\.claude")]
    [InlineData(@"D:\Work Stuff\my claude seat")]
    [InlineData(@"C:\Users\someone-with-a-very-long-profile-name\AppData\Roaming\claude-code\.claude-work")]
    public void Row_names_start_with_the_label_and_keep_the_whole_folder(string folder)
    {
        Assert.Equal("Forget folder " + folder, SettingsNames.ForgetFolderFor(folder));
        Assert.Equal("Let Claude read this folder's project totals, " + folder, SettingsNames.LetClaudeReadTotalsFor(folder));
        Assert.StartsWith(SettingsNames.ForgetFolder, SettingsNames.ForgetFolderFor(folder));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests"`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'DeskFont'`). After Step 3 alone, `Visible_labels_start_their_spoken_names` fails listing these pairs (and only these): DeskMessagePage.xaml Daily, Hourly, Color, Font; SettingsWindow.xaml Account:, Preset, Endpoint URL, Auth, Header name, Set token…, Clear token, Add folder…, Try it: waiting, Try it: done, Refresh, Buttons in a, Clock, date, Claude, Colors, Show in Explorer ×3.

- [ ] **Step 3: Add the names to `SettingsNames.cs`**

After `public const string ForgetFolder = "Forget folder";` add:

```csharp
    /// <summary>Claude Code, a folder card's Forget button, spoken with its folder (WF34).</summary>
    public static string ForgetFolderFor(string folder) => $"{ForgetFolder} {folder}";
    /// <summary>Claude Code, a folder card: the MCP server may read this folder's project totals (settings.json "mcp_roots").</summary>
    public const string LetClaudeReadTotals = "Let Claude read this folder's project totals";
    /// <summary>The same switch spoken with its folder, so each card's reads apart.</summary>
    public static string LetClaudeReadTotalsFor(string folder) => $"{LetClaudeReadTotals}, {folder}";
    /// <summary>Settings' title-bar close button, spoken (its face is a glyph).</summary>
    public const string CloseSettings = "Close Settings";
```

Replace:

```csharp
    /// <summary>Desk, Look: the ink the Desk draws in (spec 6.7).</summary>
    public const string DeskInk = "Ink";
```

with:

```csharp
    /// <summary>Desk, Look: the ink the Desk draws in (spec 6.7). The section's name.</summary>
    public const string DeskInk = "Ink";
    /// <summary>Desk, Ink: the picker's label and spoken name (it was spoken "Ink", WF34).</summary>
    public const string DeskInkPicker = "Clock, date, Claude";
    /// <summary>Desk, Look: the Desk's font, one of the two "Font"s (WF12).</summary>
    public const string DeskFont = "Desk font";
    /// <summary>Message, Look: the message's font and color.</summary>
    public const string MessageFont = "Message font";
    public const string MessageColor = "Message color";
```

In `Retired`, add:

```csharp
        ("Claude can read this folder's project totals", LetClaudeReadTotals, false),
```

- [ ] **Step 4: Name the folder rows from the table**

`ViewModels/ClaudeCodeViewModel.cs`, replace:

```csharp
    public string ForgetName => $"Forget {Display}";
    public string McpReadableName => $"Claude can read the project totals of {Display} through the MCP server";
```

with:

```csharp
    public string ForgetName => SettingsNames.ForgetFolderFor(Display);
    public string McpReadableName => SettingsNames.LetClaudeReadTotalsFor(Display);
```

`tests/Sanduhr.App.Tests/RowControlNamesTests.cs:60`: the generic names it guards against ended in "this folder"; the switch's own words now contain it before the folder. Replace:

```csharp
            Assert.DoesNotContain(names, n => n.Name.Contains("this folder"));
```

with:

```csharp
            Assert.DoesNotContain(names, n => n.Name.EndsWith("this folder"));
```

- [ ] **Step 5: Fix the names in `Views/DeskMessagePage.xaml`**

```xml
                                     IsChecked="{Binding IsDaily, Mode=TwoWay}" AutomationProperties.Name="Change the line once a day" />
```
```xml
                                     IsChecked="{Binding IsDaily, Mode=TwoWay}" AutomationProperties.Name="Daily, the line changes once a day" />
```

```xml
                                     IsChecked="{Binding IsHourly, Mode=TwoWay}" AutomationProperties.Name="Change the line every hour" />
```
```xml
                                     IsChecked="{Binding IsHourly, Mode=TwoWay}" AutomationProperties.Name="Hourly, the line changes every hour" />
```

Line 402, `<TextBlock Text="Color" FontSize="12" VerticalAlignment="Center"` becomes `<TextBlock Text="{x:Static core:SettingsNames.MessageColor}" FontSize="12" VerticalAlignment="Center"`, and the ComboBox's `AutomationProperties.Name="Message color" />` becomes `AutomationProperties.Name="{x:Static core:SettingsNames.MessageColor}" />`.

Line 426, `<TextBlock Text="Font" FontSize="12" VerticalAlignment="Center"` becomes `<TextBlock Text="{x:Static core:SettingsNames.MessageFont}" FontSize="12" VerticalAlignment="Center"`, and `AutomationProperties.Name="Message font" />` becomes `AutomationProperties.Name="{x:Static core:SettingsNames.MessageFont}" />`.

- [ ] **Step 6: Fix the names in `Views/SettingsWindow.xaml`** (attribute values only; each old string is unique in the file)

| Old | New |
|---|---|
| `Click="CloseButton_Click" ToolTip="Close settings" />` | `Click="CloseButton_Click" ToolTip="Close Settings" AutomationProperties.Name="{x:Static core:SettingsNames.CloseSettings}" />` |
| `<ListBox x:Name="AccountsList"` | `<ListBox x:Name="AccountsList" AutomationProperties.Name="Accounts"` |
| `AutomationProperties.Name="History account"` | `AutomationProperties.Name="Account, meter history"` |
| `<ItemsControl ItemsSource="{Binding Projects}" Margin="4">` | `<ItemsControl ItemsSource="{Binding Projects}" Margin="4" AutomationProperties.Name="Top projects (last 30 days)">` |
| `<ItemsControl ItemsSource="{Binding Skills}" Margin="4">` | `<ItemsControl ItemsSource="{Binding Skills}" Margin="4" AutomationProperties.Name="Top skills (last 30 days)">` |
| `<ItemsControl ItemsSource="{Binding Roots}">` | `<ItemsControl ItemsSource="{Binding Roots}" AutomationProperties.Name="Session archive folders">` |
| `<ItemsControl ItemsSource="{Binding TopProjects}">` | `<ItemsControl ItemsSource="{Binding TopProjects}" AutomationProperties.Name="Top projects (window)">` |
| `<ListBox ItemsSource="{Binding View}" Background="Transparent" BorderThickness="0"` | `<ListBox ItemsSource="{Binding View}" Background="Transparent" BorderThickness="0" AutomationProperties.Name="Sessions"` |
| `AutomationProperties.Name="Publish preset"` | `AutomationProperties.Name="Preset"` |
| `AutomationProperties.Name="Publish endpoint URL"` | `AutomationProperties.Name="Endpoint URL"` |
| `AutomationProperties.Name="Publish auth scheme"` | `AutomationProperties.Name="Auth"` |
| `AutomationProperties.Name="Publish auth header name"` | `AutomationProperties.Name="Header name"` |
| `AutomationProperties.Name="Set publish token"` | `AutomationProperties.Name="Set token…"` |
| `AutomationProperties.Name="Clear publish token"` | `AutomationProperties.Name="Clear token"` |
| `Content="Claude can read this folder's project totals (MCP)"` | `Content="{x:Static core:SettingsNames.LetClaudeReadTotals}"` |
| `<Button Style="{StaticResource FlatButton}" Content="Add folder…" HorizontalAlignment="Left"` | `<Button Style="{StaticResource FlatButton}" Content="{x:Static core:SettingsNames.AddFolder}" HorizontalAlignment="Left"` |
| `AutomationProperties.Name="Add a Claude Code folder" />` | `AutomationProperties.Name="{x:Static core:SettingsNames.AddFolder}" />` |
| `AutomationProperties.Name="Try the waiting glow" />` | `AutomationProperties.Name="Try it: waiting" />` |
| `AutomationProperties.Name="Try the done glow" />` | `AutomationProperties.Name="Try it: done" />` |
| `AutomationProperties.Name="Read this folder again" />` | `AutomationProperties.Name="Refresh, read this folder again" />` |
| `AutomationProperties.Name="Launcher buttons in a row or a column" />` | `AutomationProperties.Name="Buttons in a row or a column" />` |
| `<TextBlock Text="Font" FontSize="12" VerticalAlignment="Center"` | `<TextBlock Text="{x:Static core:SettingsNames.DeskFont}" FontSize="12" VerticalAlignment="Center"` |
| `AutomationProperties.Name="Font" />` | `AutomationProperties.Name="{x:Static core:SettingsNames.DeskFont}" />` |
| `<TextBlock Text="Clock, date, Claude" FontSize="12" VerticalAlignment="Center" TextWrapping="Wrap"` | `<TextBlock Text="{x:Static core:SettingsNames.DeskInkPicker}" FontSize="12" VerticalAlignment="Center" TextWrapping="Wrap"` |
| `AutomationProperties.Name="{x:Static core:SettingsNames.DeskInk}" />` | `AutomationProperties.Name="{x:Static core:SettingsNames.DeskInkPicker}" />` |
| `AutomationProperties.Name="Ink preset" />` | `AutomationProperties.Name="Colors, ink preset" />` |
| `AutomationProperties.Name="Show the settings folder in File Explorer"` | `AutomationProperties.Name="Show in Explorer, settings, history and themes"` |
| `AutomationProperties.Name="Show the usage vault folder in File Explorer"` | `AutomationProperties.Name="Show in Explorer, session archive"` |
| `AutomationProperties.Name="Show the Claude Code files folder in File Explorer"` | `AutomationProperties.Name="Show in Explorer, Claude Code files"` |
| `AutomationProperties.Name="Reset Sanduhr, remove what Sanduhr keeps on this PC"` | `AutomationProperties.Name="{x:Static core:SettingsNames.ResetSanduhr}" AutomationProperties.HelpText="Removes what Sanduhr keeps on this PC. You choose which parts."` |

The shortcut switch gets visible text that matches its spoken name (WF35). Replace (the old string spans two lines, which makes it unique):

```xml
                                    <CheckBox Style="{StaticResource ThemedCheckBox}"
                                              IsChecked="{Binding Shortcuts.IsOn, Mode=TwoWay}"
```
```xml
                                    <CheckBox Style="{StaticResource ThemedCheckBox}" Content="{x:Static core:SettingsNames.ShortcutToOpenSettings}"
                                              IsChecked="{Binding Shortcuts.IsOn, Mode=TwoWay}"
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsIndexTests"`
Expected: PASS.

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~RowControlNamesTests|FullyQualifiedName~SettingsWindowNamesTests"`
Expected: PASS (these run off-screen windows over a temp profile through `SANDUHR_DEV_PROFILE`; they never start `Sanduhr.exe`).

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/RowControlNamesTests.cs
git commit -m "feat(settings-v2): spoken names start with the visible label; lists and close named" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Meter history, session archive and folders (WF14, WF21, WF20 label, WF43 About)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs:103-104` (export), `Table`, `Retired`, new names
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml:1154,1314,1331,1334,1414,1627,2942-2960`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml:161-162,173-174`, `Views/ResetWindow.xaml.cs:57,59`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs:133,169,590`, `LocalCcViewModel.cs:143,188-189`, `PublishViewModel.cs:354`
- Modify: `windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs:96-108`
- Test: `SettingsNamesTests.cs`, `SettingsSourceScanTests.cs`

**Interfaces:**
- Consumes: `TabRange` (Task 3).
- Produces: consts `ExportMeterHistory = "Export meter history…"`, `ExportSessions = "Export sessions…"`, `SessionArchive = "Session archive"`, `EraseSessionArchive = "Erase session archive…"`, `OpenArchiveFolder = "Open archive folder"`, `FoldersToShare = "Folders to share (off until you tick them)"`, `NothingArchivedYet = "Nothing archived yet."`, `ResetClaudeCodeEntries`, `ResetMeterHistory`. `ExportCsv` stays as an alias of `ExportMeterHistory` because `SettingsIndex.cs:145,271` (slice 2's file) refers to it.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void History_and_archive_names_are_pinned()
    {
        Assert.Equal("Export meter history\u2026", SettingsNames.ExportMeterHistory);
        Assert.Equal(SettingsNames.ExportMeterHistory, SettingsNames.ExportCsv);
        Assert.Equal("Export sessions\u2026", SettingsNames.ExportSessions);
        Assert.Equal("Session archive", SettingsNames.SessionArchive);
        Assert.Equal("Erase session archive\u2026", SettingsNames.EraseSessionArchive);
        Assert.Equal("Open archive folder", SettingsNames.OpenArchiveFolder);
        Assert.Equal("Folders to share (off until you tick them)", SettingsNames.FoldersToShare);
        Assert.Equal("Nothing archived yet.", SettingsNames.NothingArchivedYet);
        Assert.Equal("Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)", SettingsNames.ResetClaudeCodeEntries);
        Assert.Equal("Meter history and the session archive", SettingsNames.ResetMeterHistory);
        foreach (var old in new[] { "Export CSV\u2026", "History vault", "Usage vault", "Erase archive", "Open vault folder", "Homes to share", "(statusline, MCP server)", "Usage history and the vault" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
    }
```

Append to `SettingsSourceScanTests`:

```csharp
    [Fact]
    public void The_reset_window_and_About_use_the_new_names()
    {
        var reset = File.ReadAllText(Path.Combine(AppDir(), "Views", "ResetWindow.xaml"));
        Assert.Contains($"Content=\"{SettingsNames.ResetClaudeCodeEntries}\"", reset);
        Assert.Contains($"Content=\"{SettingsNames.ResetMeterHistory}\"", reset);
        var about = TabRange("About");
        Assert.Contains("SettingsNames.NothingArchivedYet", about);
        Assert.DoesNotContain("CharacterEllipsis", about); // paths wrap (WF43)
    }
```

In `SettingsIndexTests.cs`, replace the whole `Csv_first_hit_is_History_export_because_its_title_matches` fact (lines 96-108) with:

```csharp
    [Fact]
    public void Csv_first_hit_is_the_meter_history_export()
    {
        // "Export meter history…" has no "csv" in its title any more; both exports match "csv" through a
        // synonym (rank 1), and the tie goes to sidebar order: History before Claude Usage.
        var top = Top("csv");
        Assert.Equal(1, top.Rank);
        Assert.Equal(SettingsPage.History, top.Entry.Page);
        Assert.Equal(SettingsIndex.HistoryExport, top.Entry.Anchor);
        Assert.Equal(SettingsNames.ExportMeterHistory, top.Entry.Title);
        Assert.Contains(SettingsIndex.Search("csv"),
            h => h.Entry.Page == SettingsPage.ClaudeUsage && h.Entry.Anchor == SettingsIndex.Sessions && h.Rank == 1);
    }

    [Theory]
    [InlineData("vault", SettingsPage.ClaudeUsage, SettingsIndex.Vault)]
    [InlineData("history vault", SettingsPage.ClaudeUsage, SettingsIndex.Vault)]
    [InlineData("homes", SettingsPage.Publish, SettingsIndex.Homes)]
    [InlineData("export", SettingsPage.History, SettingsIndex.HistoryExport)]
    [InlineData("meter history", SettingsPage.History, SettingsIndex.HistoryExport)]
    public void Old_words_still_find_the_renamed_controls(string query, SettingsPage page, string anchor) =>
        Assert.Contains(SettingsIndex.Search(query), h => h.Entry.Page == page && h.Entry.Anchor == anchor);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests"`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'ExportMeterHistory'`).

- [ ] **Step 3: Names in `SettingsNames.cs`**

Replace:

```csharp
    /// <summary>History and Claude Usage.</summary>
    public const string ExportCsv = "Export CSV…";
```

with:

```csharp
    /// <summary>History: the 30-day meter chart as a CSV file ("meter history", WF14).</summary>
    public const string ExportMeterHistory = "Export meter history…";
    /// <summary>The old identifier, kept because SettingsIndex.cs (slice 2's file) refers to it. Same text.</summary>
    public const string ExportCsv = ExportMeterHistory;
    /// <summary>Claude Usage, Sessions: the session table as a CSV file.</summary>
    public const string ExportSessions = "Export sessions…";
    /// <summary>Claude Usage, Overview, and About: the vault of Claude Code session totals ("session archive", WF14).</summary>
    public const string SessionArchive = "Session archive";
    public const string EraseSessionArchive = "Erase session archive…";
    public const string OpenArchiveFolder = "Open archive folder";
    /// <summary>About, Where settings live: the archive's Show in Explorer is dim until something is archived.</summary>
    public const string NothingArchivedYet = "Nothing archived yet.";
    /// <summary>Publish usage: the folders whose totals may leave ("Folder" is the one word, WF21).</summary>
    public const string FoldersToShare = "Folders to share (off until you tick them)";
    /// <summary>Reset Sanduhr…: two of its five parts, named for everything they remove (WF20).</summary>
    public const string ResetClaudeCodeEntries = "Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)";
    public const string ResetMeterHistory = "Meter history and the session archive";
```

In `Table`, replace `(ExportCsv, SettingsPage.History),` with `(ExportMeterHistory, SettingsPage.History),`.

In `Retired`, add:

```csharp
        ("Export CSV…", ExportMeterHistory, false),
        ("History vault", SessionArchive, false),
        ("Usage vault", SessionArchive, false),
        ("Erase archive", EraseSessionArchive, false),
        ("Open vault folder", OpenArchiveFolder, false),
        ("Homes to share", FoldersToShare, false),
        ("(statusline, MCP server)", ResetClaudeCodeEntries, false),
        ("Usage history and the vault", ResetMeterHistory, false),
```

- [ ] **Step 4: Labels in `Views/SettingsWindow.xaml`**

| Old (unique) | New |
|---|---|
| `Content="{x:Static core:SettingsNames.ExportCsv}"` | `Content="{x:Static core:SettingsNames.ExportMeterHistory}"` |
| `<TextBlock Text="History vault" FontSize="9" Margin="0,0,0,4" views:SettingsAnchor.Id="vault"` | `<TextBlock Text="{x:Static core:SettingsNames.SessionArchive}" FontSize="9" Margin="0,0,0,4" views:SettingsAnchor.Id="vault"` |
| `<Button Style="{StaticResource FlatButton}" Content="Erase archive"` | `<Button Style="{StaticResource FlatButton}" Content="{x:Static core:SettingsNames.EraseSessionArchive}"` |
| `<Button Style="{StaticResource FlatButton}" Content="Open vault folder"` | `<Button Style="{StaticResource FlatButton}" Content="{x:Static core:SettingsNames.OpenArchiveFolder}"` |
| `Content="Export CSV…" Command="{Binding ExportCsvCommand}" />` | `Content="{x:Static core:SettingsNames.ExportSessions}" Command="{Binding ExportCsvCommand}" />` |
| `<TextBlock Text="Homes to share (off until you tick them)" FontSize="9" Margin="0,0,0,4" views:SettingsAnchor.Id="homes"` | `<TextBlock Text="{x:Static core:SettingsNames.FoldersToShare}" FontSize="9" Margin="0,0,0,4" views:SettingsAnchor.Id="homes"` |
| `<TextBlock Grid.Row="1" Grid.Column="0" Text="Usage vault" FontSize="12"` | `<TextBlock Grid.Row="1" Grid.Column="0" Text="{x:Static core:SettingsNames.SessionArchive}" FontSize="12"` |

About's three path TextBlocks wrap instead of cutting with "…". For each of `{Binding SettingsFolder}` and `{Binding ClaudeCodeFilesFolder}`, change the second line of its TextBlock, for example:

```xml
                                    <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding SettingsFolder}" FontSize="11"
                                               FontFamily="Cascadia Code, Consolas" TextTrimming="CharacterEllipsis"
```
```xml
                                    <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding SettingsFolder}" FontSize="11"
                                               FontFamily="Cascadia Code, Consolas" TextWrapping="Wrap"
```

(and the same two-line edit with `Grid.Row="2" ... {Binding ClaudeCodeFilesFolder}`). The archive row also says why its button is dim. Replace:

```xml
                                    <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding LocalFolder}" FontSize="11"
                                               FontFamily="Cascadia Code, Consolas" TextTrimming="CharacterEllipsis"
                                               ToolTip="{Binding LocalFolder}" VerticalAlignment="Center" Margin="0,0,8,6"
                                               Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                    <Button Grid.Row="1" Grid.Column="2" Style="{StaticResource FlatButton}" Padding="10,4"
```

with:

```xml
                                    <StackPanel Grid.Row="1" Grid.Column="1" VerticalAlignment="Center" Margin="0,0,8,6">
                                        <TextBlock Text="{Binding LocalFolder}" FontSize="11"
                                                   FontFamily="Cascadia Code, Consolas" TextWrapping="Wrap"
                                                   ToolTip="{Binding LocalFolder}"
                                                   Foreground="{DynamicResource Sanduhr.Brush.Text}" />
                                        <TextBlock Text="{x:Static core:SettingsNames.NothingArchivedYet}" FontSize="10.5" Margin="0,2,0,0"
                                                   Foreground="{DynamicResource Sanduhr.Brush.TextDim}">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding IsEnabled, ElementName=VaultExplorerButton}" Value="False">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                    </StackPanel>
                                    <Button x:Name="VaultExplorerButton" Grid.Row="1" Grid.Column="2" Style="{StaticResource FlatButton}" Padding="10,4"
```

- [ ] **Step 5: The reset window**

`Views/ResetWindow.xaml` (no `core` namespace there; the scan test holds the literals to the table):

```xml
                          Content="Sanduhr's entries in Claude Code (statusline, MCP server)"
                          AutomationProperties.Name="Sanduhr's entries in Claude Code (statusline, MCP server)"
```
```xml
                          Content="Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)"
                          AutomationProperties.Name="Sanduhr's entries in Claude Code (statusline, MCP server, meters mod, glow hook)"
```

```xml
                          Content="Usage history and the vault"
                          AutomationProperties.Name="Usage history and the vault"
```
```xml
                          Content="Meter history and the session archive"
                          AutomationProperties.Name="Meter history and the session archive"
```

`Views/ResetWindow.xaml.cs`:

```csharp
        ResetPart.ClaudeCodeEntries => "Sanduhr's entries in Claude Code (statusline, MCP server)",
```
```csharp
        ResetPart.ClaudeCodeEntries => SettingsNames.ResetClaudeCodeEntries,
```

```csharp
        ResetPart.History => "Usage history and the vault",
```
```csharp
        ResetPart.History => SettingsNames.ResetMeterHistory,
```

- [ ] **Step 6: "Folder", not "home", and the archive's own words**

`ViewModels/CcLedgerViewModel.cs`:

```csharp
        sb.Append("\nHome: ").Append(Info.Root).Append("  ·  Session: ").Append(Info.Uuid);
```
```csharp
        sb.Append("\nFolder: ").Append(Info.Root).Append("  ·  Session: ").Append(Info.Uuid);
```

```csharp
        sb.Append("  ·  Homes: ").Append(string.Join(", ", roots));
```
```csharp
        sb.Append("  ·  Folders: ").Append(string.Join(", ", roots));
```

```csharp
                ? "History vault is off — enable a Claude Code home in Overview."
```
```csharp
                ? "The session archive is off. Turn it on for a Claude Code folder under Overview, Session archive."
```

`ViewModels/LocalCcViewModel.cs`:

```csharp
                    "Archiving is off for this home either way. Erase the history already stored for it?",
```
```csharp
                    "Archiving is off for this folder either way. Erase the history already stored for it?",
```

```csharp
            var res = ThemedDialog.Show(_owner, "Erase usage history?",
                "Deletes the entire local vault and turns archiving off for every home. This cannot be undone.",
```
```csharp
            var res = ThemedDialog.Show(_owner, "Erase the session archive?",
                "Deletes the whole session archive and turns archiving off for every folder. This cannot be undone.",
```

`ViewModels/PublishViewModel.cs`:

```csharp
                StatusText = "Tick at least one home to share first.";
```
```csharp
                StatusText = "Tick at least one folder to share first.";
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests"`
Expected: PASS (`Every_table_name_is_findable_at_its_anchor` still finds "Export meter history…" at `export`).

Run: `dotnet build src/Sanduhr.App`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ResetWindow.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/CcLedgerViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/LocalCcViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/PublishViewModel.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsIndexTests.cs
git commit -m "feat(settings-v2): meter history, session archive and folders, one name each" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Claude Code, Mods, Themes, Alerts and Desk copy (WF11, WF16, WF27 caption, WF42, WF43)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs:124-125` (DeskSwitch), new names, `Retired`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs:204,482`, `ModsConfigViewModel.cs:287`, `ThemesViewModel.cs:133`, `ThemeStudioViewModel.cs:217`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml:1067,1742,2022-2024,2254`
- Modify: `windows-dotnet/tests/Sanduhr.App.Tests/CcPieceViewModelTests.cs:13`
- Test: `SettingsNamesTests.cs`

**Interfaces:**
- Produces: `DeskSwitch == "Desk: a clock, your meters, a message and more on the desktop"`; consts `ClaudeCodeGlowHook`, `PieceOutdated`, `LinkedAccountCaption`, `TranscriptsKept`, `ThemeJsonValid`, `SaveThemeAs`, `SnakeSoundAtFull`.

- [ ] **Step 1: Write the failing test**

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void Page_copy_names_are_pinned_and_the_old_ones_retired()
    {
        Assert.Equal("Desk: a clock, your meters, a message and more on the desktop", SettingsNames.DeskSwitch);
        Assert.Equal("Claude Code glow hook", SettingsNames.ClaudeCodeGlowHook);
        Assert.Equal("Installed, but not the current version.", SettingsNames.PieceOutdated);
        Assert.Equal("Once the statusline or meters are installed here, they show this account's meters.", SettingsNames.LinkedAccountCaption);
        Assert.Equal("Claude Code keeps transcripts for", SettingsNames.TranscriptsKept);
        Assert.Equal("This theme's JSON is valid.", SettingsNames.ThemeJsonValid);
        Assert.Equal("Save the theme as", SettingsNames.SaveThemeAs);
        Assert.Equal("Play the cooldown snake's sound at 100%", SettingsNames.SnakeSoundAtFull);
        foreach (var old in new[] { "Desk: the clock and your Claude meters on the desktop", "Glow when Claude needs you",
                     "Install again to update it.", "History kept", "Looks good", "Save as:", "100% plays the ! (you know the one)" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
        Assert.Empty(SettingsNames.RetiredNames("Looks good to me")); // "Looks good" is whole-string only
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsNamesTests`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'ClaudeCodeGlowHook'`).

- [ ] **Step 3: Names in `SettingsNames.cs`**

Replace:

```csharp
    /// <summary>Desk: the switch at the top of the page (settings.json "desk_enabled", off by default).</summary>
    public const string DeskSwitch = "Desk: the clock and your Claude meters on the desktop";
```

with:

```csharp
    /// <summary>Desk: the switch at the top of the page (settings.json "desk_enabled", off by default). It lists
    /// what the Desk draws, as the tour does (WF16).</summary>
    public const string DeskSwitch = "Desk: a clock, your meters, a message and more on the desktop";
    /// <summary>Claude Code: the install that lights the glow, the Mac's name (WF11). "Claude Code glow" is the
    /// section and the Desk piece.</summary>
    public const string ClaudeCodeGlowHook = "Claude Code glow hook";
    /// <summary>Claude Code: a piece installed in an older version. "Update…" beside it is the verb (WF42).</summary>
    public const string PieceOutdated = "Installed, but not the current version.";
    /// <summary>Claude Code, a folder card's Linked account caption, true before and after an install (WF27).</summary>
    public const string LinkedAccountCaption = "Once the statusline or meters are installed here, they show this account's meters.";
    /// <summary>Mods &amp; Config: Claude Code's own retention (cleanupPeriodDays), the fourth "history" (WF14).</summary>
    public const string TranscriptsKept = "Claude Code keeps transcripts for";
    /// <summary>Themes: a pasted or edited theme with no errors or notes (WF43).</summary>
    public const string ThemeJsonValid = "This theme's JSON is valid.";
    /// <summary>Themes, Studio: the file name the theme is saved under (WF43).</summary>
    public const string SaveThemeAs = "Save the theme as";
    /// <summary>Alerts, Sound (settings.json "alert_snake_full"), said plainly (WF43, WF38).</summary>
    public const string SnakeSoundAtFull = "Play the cooldown snake's sound at 100%";
```

In `Retired`, add:

```csharp
        ("Desk: the clock and your Claude meters on the desktop", DeskSwitch, false),
        ("Glow when Claude needs you", ClaudeCodeGlowHook, false),
        ("Install again to update it", PieceOutdated, false),
        ("History kept", TranscriptsKept, false),
        ("Looks good", ThemeJsonValid, true),
        ("Save as:", SaveThemeAs, true),
        ("100% plays the !", SnakeSoundAtFull, false),
```

- [ ] **Step 4: Run the test to verify it passes, and see the scan fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: `SettingsNamesTests` PASS; `App_strings_use_no_retired_name_and_link_to_no_unknown_page` FAILS listing ClaudeCodeViewModel.cs:204 and :482, ThemesViewModel.cs:133, ThemeStudioViewModel.cs:217, SettingsWindow.xaml:1067, :2024 and :2254.

- [ ] **Step 5: Use the names in the App**

`ViewModels/ClaudeCodeViewModel.cs`:

```csharp
        Hooks = new CcPieceViewModel("Glow when Claude needs you",
```
```csharp
        Hooks = new CcPieceViewModel(SettingsNames.ClaudeCodeGlowHook,
```

```csharp
        IntegrationStatus.Outdated => "Installed, but not the current version. Install again to update it.",
```
```csharp
        IntegrationStatus.Outdated => SettingsNames.PieceOutdated,
```

`ViewModels/ModsConfigViewModel.cs:287` (the heading now carries "Claude Code keeps transcripts for"):

```csharp
            : report.Retention.CleanupPeriodDays is { } days ? $"Kept for {days} days." : "Unknown while settings.json is unreadable.";
```
```csharp
            : report.Retention.CleanupPeriodDays is { } days ? $"{days} days." : "Unknown while settings.json is unreadable.";
```

`ViewModels/ThemesViewModel.cs:133` and `ViewModels/ThemeStudioViewModel.cs:217`, each:

```csharp
            : "Looks good";
```
```csharp
            : SettingsNames.ThemeJsonValid;
```

`Views/SettingsWindow.xaml`:

| Old (unique) | New |
|---|---|
| `<CheckBox Content="100% plays the ! (you know the one)"` | `<CheckBox Content="{x:Static core:SettingsNames.SnakeSoundAtFull}"` |
| `<TextBlock Text="The statusline in this folder shows that account's meters." FontSize="10.5"` | `<TextBlock Text="{x:Static core:SettingsNames.LinkedAccountCaption}" FontSize="10.5"` |
| `<!-- History kept -->` | `<!-- Claude Code keeps transcripts for -->` |
| `<TextBlock Text="History kept" FontSize="12" FontWeight="SemiBold" Margin="0,0,0,4"` | `<TextBlock Text="{x:Static core:SettingsNames.TranscriptsKept}" FontSize="12" FontWeight="SemiBold" Margin="0,0,0,4"` |
| `<TextBlock Text="Save as:" VerticalAlignment="Center" FontSize="12"` | `<TextBlock Text="{x:Static core:SettingsNames.SaveThemeAs}" VerticalAlignment="Center" FontSize="12"` |

The Alerts edit changes the `Content` value on the CheckBox's first line only; slice 2 adds `IsEnabled` to that element on a line of its own.

`tests/Sanduhr.App.Tests/CcPieceViewModelTests.cs:13`:

```csharp
    [InlineData("Glow when Claude needs you")]
```
```csharp
    [InlineData("Claude Code glow hook")]
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~SettingsIndexTests"`
Expected: PASS (the index title for the Desk switch follows the constant).

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~CcPieceViewModelTests|FullyQualifiedName~ModsConfig"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ModsConfigViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemesViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/ThemeStudioViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.App.Tests/CcPieceViewModelTests.cs
git commit -m "feat(settings-v2): Desk switch, glow hook, transcripts, theme and sound copy" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Menus with one name each (WF19), the tier card and the Desk menu

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (menu names, `Retired`)
- Modify: `windows-dotnet/src/Sanduhr.Core/DeskHitTest.cs:145-146`
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs:419,437,440,446`
- Modify: `windows-dotnet/src/Sanduhr.App/MainWindow.xaml.cs:107,110-111,116,119`, `MainWindow.xaml:270-272,290-303`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/TierCard.xaml.cs:72`
- Modify: `windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs:50,282`
- Modify: `windows-dotnet/tests/Sanduhr.Tests/DeskHitTestTests.cs:188-190`
- Test: `SettingsNamesTests.cs`, `SettingsSourceScanTests.cs`

**Interfaces:**
- Produces: consts `CompactMode = "Compact mode"`, `DeepWork = "Deep Work"`, `CooldownSnake = "Cooldown snake"`, `RefreshUsageNow = "Refresh usage now"`, `HideThisLimit = "Hide this limit"`; `DeskClicks.MessageSettings` (static readonly, `"Message settings…"`), replacing the const `EditMessages`.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void Menu_names_are_pinned_and_their_old_forms_retired()
    {
        Assert.Equal("Compact mode", SettingsNames.CompactMode);
        Assert.Equal("Deep Work", SettingsNames.DeepWork);
        Assert.Equal("Cooldown snake", SettingsNames.CooldownSnake);
        Assert.Equal("Refresh usage now", SettingsNames.RefreshUsageNow);
        Assert.Equal("Settings\u2026", SettingsNames.OpenSettings);
        Assert.Equal("Quit Sanduhr", SettingsNames.QuitSanduhr);
        Assert.Equal("Hide this limit", SettingsNames.HideThisLimit);
        Assert.Equal("Message settings\u2026", SettingsNames.LinkTitle(SettingsPage.DeskMessage));
        foreach (var old in new[] { "Compact Mode", "Expand", "Deep Work (Focus)", "Deep Work focus timer (Ctrl+P)",
                     "Cooldown Snake", "Cooldown snake game", "Hide this tier", "Edit messages\u2026" })
            Assert.NotEmpty(SettingsNames.RetiredNames(old));
        Assert.Empty(SettingsNames.RetiredNames("Expand the list")); // "Expand" is whole-string only
    }
```

Append to `SettingsSourceScanTests`:

```csharp
    [Fact]
    public void Tray_widget_menu_and_icon_strip_read_their_names_from_the_table()
    {
        var app = File.ReadAllText(Path.Combine(AppDir(), "App.xaml.cs"));
        var menu = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml.cs"));
        var strip = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        foreach (var name in new[] { "OpenSettings", "DeepWork", "CooldownSnake", "RefreshUsageNow", "QuitSanduhr" })
            Assert.Contains($"SettingsNames.{name}", app);
        foreach (var name in new[] { "OpenSettings", "CompactMode", "DeepWork", "CooldownSnake", "RefreshUsageNow" })
        {
            Assert.Contains($"SettingsNames.{name}", menu);
            Assert.Contains($"AutomationProperties.Name=\"{{x:Static core:SettingsNames.{name}}}\"", strip);
        }
        Assert.DoesNotContain("Header = \"Refresh\"", app + menu);
        Assert.DoesNotContain("Header = \"Quit Sanduhr\"", app);
    }
```

In `DeskHitTestTests.cs`, replace:

```csharp
        Assert.Equal("Edit messages…", DeskPieceClicks.MenuItem(DeskElementKind.Message)?.Title);
```

with:

```csharp
        Assert.Equal("Message settings…", DeskPieceClicks.MenuItem(DeskElementKind.Message)?.Title);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~DeskHitTestTests"`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'CompactMode'`).

- [ ] **Step 3: Names in `SettingsNames.cs`**

After `public const string QuitSanduhr = "Quit Sanduhr";` add:

```csharp
    /// <summary>The widget's menu, icon strip and the tray (WF19): one name each. The strip's tooltips add
    /// the shortcut ("Compact mode (Ctrl+D)").</summary>
    public const string CompactMode = "Compact mode";
    public const string DeepWork = "Deep Work";
    public const string CooldownSnake = "Cooldown snake";
    public const string RefreshUsageNow = "Refresh usage now";
    /// <summary>A limit card's menu: hides the limit until shown again.</summary>
    public const string HideThisLimit = "Hide this limit";
```

Change the casing line of the class summary (line 14):

```csharp
/// capitals: Claude, Claude Code, Sanduhr, What's New (the window's name), Deep Work, Cooldown Snake.
```
```csharp
/// capitals: Claude, Claude Code, Sanduhr, What's New (the window's name), Deep Work. The cooldown snake is a game, not a name.
```

In `Retired`, add:

```csharp
        ("Compact Mode", CompactMode, false),
        ("Expand", CompactMode, true),
        ("Deep Work (Focus)", DeepWork, false),
        ("Deep Work focus timer", DeepWork, false),
        ("Cooldown Snake", CooldownSnake, false),
        ("Cooldown snake game", CooldownSnake, false),
        ("Hide this tier", HideThisLimit, false),
        ("Edit messages…", LinkTitle(SettingsPage.DeskMessage), false),
```

- [ ] **Step 4: The Desk piece menu in Core and App**

`Sanduhr.Core/DeskHitTest.cs`:

```csharp
        DeskElementKind.Message => ("Edit messages…", DeskElementKind.Message),
        DeskElementKind.Clock => ("Desk settings…", DeskElementKind.Clock),
```
```csharp
        DeskElementKind.Message => (SettingsNames.LinkTitle(SettingsPage.DeskMessage), DeskElementKind.Message),
        DeskElementKind.Clock => (SettingsNames.LinkTitle(SettingsPage.Desk), DeskElementKind.Clock),
```

`Sanduhr.App/Desk/DeskClicks.cs`:

```csharp
    public const string EditMessages = "Edit messages…";
```
```csharp
    /// <summary>The message's menu item: "Message settings…", a page name like every Settings entry point.</summary>
    public static readonly string MessageSettings = SettingsNames.LinkTitle(SettingsPage.DeskMessage);
```

```csharp
        if (kind == DeskElementKind.Message) Add(EditMessages, DeskMenuAction.EditMessages);
```
```csharp
        if (kind == DeskElementKind.Message) Add(MessageSettings, DeskMenuAction.EditMessages);
```

(`LauncherSettings = "Edit buttons…"` stays until slice 3 adds the Launcher page; "Launcher settings…" would name a page that does not exist yet.)

- [ ] **Step 5: Tray, widget menu, icon strip and tier card**

`App.xaml.cs`:

```csharp
        var refresh = new MenuItem { Header = "Refresh" };
```
```csharp
        var refresh = new MenuItem { Header = SettingsNames.RefreshUsageNow };
```

```csharp
        var focus = new MenuItem { Header = "Deep Work (Focus)" };
```
```csharp
        var focus = new MenuItem { Header = SettingsNames.DeepWork };
```

```csharp
        var game = new MenuItem { Header = "Cooldown Snake" };
```
```csharp
        var game = new MenuItem { Header = SettingsNames.CooldownSnake };
```

```csharp
        var quit = new MenuItem { Header = "Quit Sanduhr" };
```
```csharp
        var quit = new MenuItem { Header = SettingsNames.QuitSanduhr };
```

`MainWindow.xaml.cs`:

```csharp
        var refresh = new MenuItem { Header = "Refresh", Command = Vm.RefreshCommand };
```
```csharp
        var refresh = new MenuItem { Header = SettingsNames.RefreshUsageNow, Command = Vm.RefreshCommand };
```

```csharp
        var compact = new MenuItem { Command = Vm.ToggleCompactCommand };
        menu.Opened += (_, _) => compact.Header = (Vm?.IsCompact ?? false) ? "Expand" : "Compact Mode";
```
```csharp
        // One name in both states; the check mark says which (WF19).
        var compact = new MenuItem { Header = SettingsNames.CompactMode, Command = Vm.ToggleCompactCommand };
        menu.Opened += (_, _) => compact.IsChecked = Vm?.IsCompact ?? false;
```

```csharp
        var focus = new MenuItem { Header = "Deep Work (Focus)", Command = Vm.ToggleFocusCommand };
```
```csharp
        var focus = new MenuItem { Header = SettingsNames.DeepWork, Command = Vm.ToggleFocusCommand };
```

```csharp
        var game = new MenuItem { Header = "Cooldown Snake", Command = Vm.StartGameCommand };
```
```csharp
        var game = new MenuItem { Header = SettingsNames.CooldownSnake, Command = Vm.StartGameCommand };
```

`MainWindow.xaml` (it already declares `xmlns:core`):

| Old | New |
|---|---|
| `ToolTip="Settings" AutomationProperties.Name="Settings" />` | `ToolTip="{x:Static core:SettingsNames.OpenSettings}" AutomationProperties.Name="{x:Static core:SettingsNames.OpenSettings}" />` |
| `ToolTip="Compact mode (Ctrl+D)" AutomationProperties.Name="Compact mode" />` | `ToolTip="Compact mode (Ctrl+D)" AutomationProperties.Name="{x:Static core:SettingsNames.CompactMode}" />` |
| `ToolTip="Deep Work focus timer (Ctrl+P)"` | `ToolTip="Deep Work (Ctrl+P)"` |
| `AutomationProperties.Name="Deep Work focus timer" />` | `AutomationProperties.Name="{x:Static core:SettingsNames.DeepWork}" />` |
| `ToolTip="Cooldown snake game" AutomationProperties.Name="Cooldown snake game" />` | `ToolTip="{x:Static core:SettingsNames.CooldownSnake}" AutomationProperties.Name="{x:Static core:SettingsNames.CooldownSnake}" />` |
| `ToolTip="Refresh usage now" AutomationProperties.Name="Refresh usage" />` | `ToolTip="{x:Static core:SettingsNames.RefreshUsageNow}" AutomationProperties.Name="{x:Static core:SettingsNames.RefreshUsageNow}" />` |

`Views/TierCard.xaml.cs`:

```csharp
        menu.Items.Add(Item("Hide this tier", () => host.HideTier(key)));
```
```csharp
        menu.Items.Add(Item(SettingsNames.HideThisLimit, () => host.HideTier(key)));
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~DeskHitTestTests"`
Expected: PASS.

Run: `dotnet build src/Sanduhr.App`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/DeskHitTest.cs windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.App/MainWindow.xaml windows-dotnet/src/Sanduhr.App/MainWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Views/TierCard.xaml.cs windows-dotnet/src/Sanduhr.App/Desk/DeskClicks.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/tests/Sanduhr.Tests/DeskHitTestTests.cs
git commit -m "feat(settings-v2): tray, widget menu, strip and Desk menu read one name each" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The shared trademark footer (WF44)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.App/Views/TrademarkFooter.xaml`, `TrademarkFooter.xaml.cs`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml:2728-2730,2799-2801,2874-2876,2916-2918`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml:436-438`, `Views/WidgetsPage.xaml:185-187`
- Test: `SettingsSourceScanTests.cs`

**Interfaces:**
- Produces: `Sanduhr.App.Views.TrademarkFooter` (UserControl, no properties). The separator `Border` above each notice stays at its site; the footer is the notice only.

- [ ] **Step 1: Write the failing test**

Append to `SettingsSourceScanTests`:

```csharp
    [Fact]
    public void The_trademark_footer_is_one_control_on_six_pages()
    {
        var views = Path.Combine(AppDir(), "Views");
        int Count(string file) => Regex.Matches(File.ReadAllText(Path.Combine(views, file)), "<views:TrademarkFooter\\b").Count;
        Assert.Equal(4, Count("SettingsWindow.xaml"));   // Desk, Now playing, Weather, About
        Assert.Equal(1, Count("DeskMessagePage.xaml"));
        Assert.Equal(1, Count("WidgetsPage.xaml"));
        Assert.Contains("core:SettingsNames.TrademarkNotice", File.ReadAllText(Path.Combine(views, "TrademarkFooter.xaml")));
        Assert.Contains("<views:TrademarkFooter views:SettingsAnchor.Id=\"trademark\"", TabRange("About"));
        var copies = Files("*.xaml")
            .Where(f => Path.GetFileName(f) is not ("TrademarkFooter.xaml" or "WhatsNewWindow.xaml" or "SignInWindow.xaml"))
            .Where(f => File.ReadAllText(f).Contains("TrademarkNotice", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(copies.Count == 0, "Use <views:TrademarkFooter /> in: " + string.Join(", ", copies));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: FAIL in `The_trademark_footer_is_one_control_on_six_pages` (`Assert.Equal() Failure: Expected: 4, Actual: 0`).

- [ ] **Step 3: Create the control**

`Views/TrademarkFooter.xaml`:

```xml
<UserControl x:Class="Sanduhr.App.Views.TrademarkFooter"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
             Focusable="False">
    <!-- The trademark disclaimer (Store test 10.1.4.4) at the foot of every Settings page that names Claude.
         One control, so the six pages can't drift. The separator above it stays with each page's layout. -->
    <TextBlock Text="{x:Static core:SettingsNames.TrademarkNotice}"
               TextWrapping="Wrap" FontSize="11"
               Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
</UserControl>
```

`Views/TrademarkFooter.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>The trademark notice at the foot of a Settings page (Settings v2, WF44).</summary>
public partial class TrademarkFooter : UserControl
{
    public TrademarkFooter() => InitializeComponent();
}
```

- [ ] **Step 4: Use it on the six pages**

`Views/SettingsWindow.xaml`: the Desk, Now playing and Weather notices are the same three lines at the same indent. Edit with `replace_all: true`:

```xml
                            <TextBlock Text="{x:Static core:SettingsNames.TrademarkNotice}"
                                       TextWrapping="Wrap" FontSize="11"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```
```xml
                            <views:TrademarkFooter />
```

About (anchor kept on the control):

```xml
                            <TextBlock views:SettingsAnchor.Id="trademark" Text="{Binding TrademarkNotice}"
                                       TextWrapping="Wrap" FontSize="11"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```
```xml
                            <views:TrademarkFooter views:SettingsAnchor.Id="trademark" />
```

`Views/DeskMessagePage.xaml` and `Views/WidgetsPage.xaml`, each:

```xml
                <TextBlock Text="{x:Static core:SettingsNames.TrademarkNotice}"
                           TextWrapping="Wrap" FontSize="11"
                           Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
```
```xml
                <views:TrademarkFooter />
```

(`SettingsViewModel.TrademarkNotice` is now unused. It stays: `SettingsViewModel.cs` is slice 2's file this round; see the hand-offs.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter FullyQualifiedName~SettingsSourceScanTests`
Expected: PASS, including `Every_search_anchor_is_marked_in_its_pages_TabItem` (the `trademark` marker is on the footer) and `The_trademark_notice_has_one_source_and_both_windows_bind_to_it`.

Run: `dotnet build src/Sanduhr.App`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/TrademarkFooter.xaml windows-dotnet/src/Sanduhr.App/Views/TrademarkFooter.xaml.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs
git commit -m "feat(settings-v2): one trademark footer control on six pages" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Message's List | Text segment (WF48)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (two names, `Retired`)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs:588-593,625`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml:201,268-271`
- Create: `windows-dotnet/tests/Sanduhr.App.Tests/MessageModeSegmentTests.cs`
- Test: `SettingsSourceScanTests.cs`, `SettingsNamesTests.cs`

**Interfaces:**
- Produces: consts `MessageModeList = "List"`, `MessageModeText = "Text"`; `DeskMessagePageViewModel.IsListMode` and `IsTextMode` become settable (`true` switches, `false` does nothing); `ModeButtonText` is removed.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void Message_modes_are_List_and_Text()
    {
        Assert.Equal("List", SettingsNames.MessageModeList);
        Assert.Equal("Text", SettingsNames.MessageModeText);
        Assert.NotEmpty(SettingsNames.RetiredNames("Edit as text\u2026"));
        Assert.NotEmpty(SettingsNames.RetiredNames("Back to the list"));
    }
```

Append to `SettingsSourceScanTests`:

```csharp
    [Fact]
    public void Message_mode_is_a_two_radio_segment()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "DeskMessagePage.xaml"));
        var radios = Regex.Matches(page, "<RadioButton\\b[^>]*GroupName=\"MessageMode\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(2, radios.Count);
        Assert.Contains("Content=\"{x:Static core:SettingsNames.MessageModeList}\"", radios[0]);
        Assert.Contains("IsChecked=\"{Binding IsListMode, Mode=TwoWay}\"", radios[0]);
        Assert.Contains("Content=\"{x:Static core:SettingsNames.MessageModeText}\"", radios[1]);
        Assert.Contains("IsChecked=\"{Binding IsTextMode, Mode=TwoWay}\"", radios[1]);
        Assert.DoesNotContain("ModeButtonText", page);
    }
```

Create `tests/Sanduhr.App.Tests/MessageModeSegmentTests.cs`:

```csharp
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings, Message: List | Text is a two-radio segment (WF48). Checking a radio switches the view;
/// the false WPF writes to the radio that loses its check never switches back. Everything lives in a temp
/// folder: settings, the external folder and messages.txt.</summary>
public sealed class MessageModeSegmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-msgmode-" + Guid.NewGuid().ToString("N")[..8]);

    public MessageModeSegmentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskMessagePageViewModel Page()
    {
        var paths = new Paths(appDataBase: Path.Combine(_root, "appdata"), homeDir: Path.Combine(_root, "home"),
            localAppDataBase: Path.Combine(_root, "local"), externalDir: Path.Combine(_root, "external"));
        var page = new DeskMessagePageViewModel(new SettingsStore(paths), Path.Combine(_root, "messages.txt"),
            () => true, () => { }, ["Segoe UI"]);
        page.Activate();
        return page;
    }

    [Fact]
    public void Checking_Text_then_List_switches_and_says_so() => AppHost.Run(() =>
    {
        var page = Page();
        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.True(page.IsListMode);

        page.IsTextMode = true;
        Assert.True(page.IsTextMode);
        Assert.False(page.IsListMode);
        Assert.Contains(nameof(DeskMessagePageViewModel.IsListMode), raised);
        Assert.Contains(nameof(DeskMessagePageViewModel.IsTextMode), raised);

        page.IsListMode = true;
        Assert.True(page.IsListMode);
    });

    [Fact]
    public void Unchecking_a_mode_does_not_switch_back() => AppHost.Run(() =>
    {
        var page = Page();
        page.IsTextMode = true;
        page.IsListMode = false;   // what WPF writes to the List radio as Text takes the check
        Assert.True(page.IsTextMode);
        page.IsTextMode = false;
        Assert.True(page.IsTextMode);
    });

    [Fact]
    public void Text_edits_carry_over_to_the_list() => AppHost.Run(() =>
    {
        var page = Page();
        page.IsTextMode = true;
        page.EditorText = "A line typed in the Text view";
        page.IsListMode = true;
        page.IsTextMode = true;
        Assert.Contains("A line typed in the Text view", page.EditorText);
    });
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'MessageModeList'`).

Run: `dotnet test tests/Sanduhr.App.Tests --filter FullyQualifiedName~MessageModeSegmentTests`
Expected: build FAILS (`Property or indexer 'DeskMessagePageViewModel.IsTextMode' cannot be assigned to -- it is read only`).

- [ ] **Step 3: Names in `SettingsNames.cs`**

After the `ClearLooks` constant add:

```csharp
    /// <summary>Message, Your lines: the two views of messages.txt, as a segment (WF48).</summary>
    public const string MessageModeList = "List";
    public const string MessageModeText = "Text";
```

In `Retired`, add:

```csharp
        ("Edit as text…", MessageModeText, false),
        ("Back to the list", MessageModeList, false),
```

- [ ] **Step 4: The view model**

`ViewModels/DeskMessagePageViewModel.cs`, replace:

```csharp
    public bool IsListMode => _editor.Mode == MessageEditorMode.List;
    public bool IsTextMode => !IsListMode;
```

with:

```csharp
    /// <summary>The List radio. Checking it shows the list; the false WPF writes when Text takes the check does nothing.</summary>
    public bool IsListMode
    {
        get => _editor.Mode == MessageEditorMode.List;
        set { if (value && !IsListMode) ToggleMode(); }
    }

    /// <summary>The Text radio, the other half of the segment.</summary>
    public bool IsTextMode
    {
        get => !IsListMode;
        set { if (value && IsListMode) ToggleMode(); }
    }
```

Delete:

```csharp
    /// <summary>"Edit as text…" in the list, "Back to the list" in the text.</summary>
    public string ModeButtonText => IsListMode ? "Edit as text…" : "Back to the list";

```

and in `RaiseState` delete the line `        OnPropertyChanged(nameof(ModeButtonText));`.

- [ ] **Step 5: The page**

`Views/DeskMessagePage.xaml`, replace:

```xml
                        <Button Style="{DynamicResource FlatButton}" Content="{Binding ModeButtonText}"
                                Command="{Binding ToggleModeCommand}"
                                AutomationProperties.Name="Switch between the list and the text of messages.txt"
                                ToolTip="List: each line as controls. Text: the file itself, tags and all. Your edits carry over either way." />
```

with:

```xml
                        <RadioButton Style="{StaticResource MessageRadio}" GroupName="MessageMode"
                                     Content="{x:Static core:SettingsNames.MessageModeList}"
                                     IsChecked="{Binding IsListMode, Mode=TwoWay}"
                                     AutomationProperties.Name="List, each line as its own row"
                                     ToolTip="Each line as controls. Your edits carry over to Text." />
                        <RadioButton Style="{StaticResource MessageRadio}" GroupName="MessageMode"
                                     Content="{x:Static core:SettingsNames.MessageModeText}"
                                     IsChecked="{Binding IsTextMode, Mode=TwoWay}"
                                     AutomationProperties.Name="Text, messages.txt as written, tags and all"
                                     ToolTip="The file itself, tags and all. Your edits carry over to List." />
```

and line 201:

```xml
                                   Text="Kept as written: the editor can't set everything on this line. Edit as text changes it." />
```
```xml
                                   Text="Kept as written: the editor can't set everything on this line. Text changes it." />
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: PASS (`Visible_labels_start_their_spoken_names` checks the two new radios too).

Run: `dotnet test tests/Sanduhr.App.Tests --filter "FullyQualifiedName~MessageModeSegmentTests|FullyQualifiedName~RowControlNamesTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/DeskMessagePageViewModel.cs windows-dotnet/src/Sanduhr.App/Views/DeskMessagePage.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs windows-dotnet/tests/Sanduhr.App.Tests/MessageModeSegmentTests.cs
git commit -m "feat(settings-v2): Message's List and Text segment, no ellipsis" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Accounts copy and plain key help (WF29 copy, WF31)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (new names)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml:918,991-1000` (Accounts)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml:1-6,44-47,66-74`, `Views/ManualKeyWindow.xaml.cs:102-105`
- Test: `SettingsNamesTests.cs`, `SettingsSourceScanTests.cs`

**Interfaces:**
- Consumes: `AccountFollow.CheckInterval`, `PauseLength`, `PauseIdle` (Core, existing).
- Produces: `SettingsNames.FollowAccountCaption` (static property), consts `PickAnAccount`, `ManualKeySteps`, `ManualKeyCloudflareToggle`, `ManualKeyCloudflareHelp`, `ManualKeyCfLabel`.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void Follow_caption_says_how_long_a_manual_switch_pauses_it()
    {
        Assert.Equal(
            "Every 15 minutes Sanduhr checks your other signed-in accounts and switches to the one whose session is climbing. "
            + "A manual switch pauses it for up to 3 hours, or until that account sits idle for 30 minutes.",
            SettingsNames.FollowAccountCaption);
        Assert.Equal("Pick an account to switch, rename, update or remove it.", SettingsNames.PickAnAccount);
    }

    [Fact]
    public void Key_help_is_plain_steps_for_Edge_and_Chrome()
    {
        Assert.Contains("Edge", SettingsNames.ManualKeySteps);
        Assert.Contains("Chrome", SettingsNames.ManualKeySteps);
        Assert.Contains("sessionKey", SettingsNames.ManualKeySteps);
        Assert.DoesNotContain("\u2192", SettingsNames.ManualKeySteps);
        Assert.DoesNotContain("DevTools", SettingsNames.ManualKeySteps);
        Assert.Equal("Seeing a Cloudflare error?", SettingsNames.ManualKeyCloudflareToggle);
        Assert.Contains("cf_clearance", SettingsNames.ManualKeyCloudflareHelp);
        Assert.Equal("cf_clearance", SettingsNames.ManualKeyCfLabel);
    }
```

Append to `SettingsSourceScanTests`:

```csharp
    [Fact]
    public void Accounts_and_the_key_window_read_their_copy_from_the_table()
    {
        var accounts = TabRange("Accounts");
        Assert.Contains("SettingsNames.FollowAccountCaption", accounts);
        Assert.Contains("SettingsNames.PickAnAccount", accounts);
        var key = File.ReadAllText(Path.Combine(AppDir(), "Views", "ManualKeyWindow.xaml"));
        foreach (var name in new[] { "ManualKeySteps", "ManualKeyCloudflareToggle", "ManualKeyCloudflareHelp", "ManualKeyCfLabel" })
            Assert.Contains($"core:SettingsNames.{name}", key);
        Assert.DoesNotContain("DevTools", key);
        Assert.Contains("x:Name=\"CfClearanceBox\"", key);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'FollowAccountCaption'`).

- [ ] **Step 3: Names in `SettingsNames.cs`**

After `public const string FollowAccount = "Follow the account I'm using";` add:

```csharp
    /// <summary>Accounts: the follow switch's caption, its numbers read from AccountFollow so they can't drift (WF29).</summary>
    public static string FollowAccountCaption { get; } =
        $"Every {(int)AccountFollow.CheckInterval.TotalMinutes} minutes Sanduhr checks your other signed-in accounts and switches to the one whose session is climbing. "
        + $"A manual switch pauses it for up to {(int)AccountFollow.PauseLength.TotalHours} hours, or until that account sits idle for {(int)AccountFollow.PauseIdle.TotalMinutes} minutes.";
    /// <summary>Accounts: why the account buttons are dim (WF29).</summary>
    public const string PickAnAccount = "Pick an account to switch, rename, update or remove it.";
    /// <summary>The manual key window: where the sessionKey is, in plain steps (WF31).</summary>
    public const string ManualKeySteps =
        "In Edge or Chrome, open claude.ai and sign in. Press F12, open the Application tab, then Cookies, then https://claude.ai. "
        + "Copy the Value next to sessionKey and paste it below.";
    /// <summary>The manual key window: cf_clearance waits behind this line.</summary>
    public const string ManualKeyCloudflareToggle = "Seeing a Cloudflare error?";
    public const string ManualKeyCloudflareHelp =
        "Only if Sanduhr says Cloudflare blocked it: copy the Value next to cf_clearance from the same Cookies list and paste it here.";
    public const string ManualKeyCfLabel = "cf_clearance";
```

- [ ] **Step 4: Accounts in `Views/SettingsWindow.xaml`**

```xml
                                               Text="Every 15 minutes Sanduhr checks your other signed-in accounts and switches to the one whose session is climbing. A manual switch pauses it." />
```
```xml
                                               Text="{x:Static core:SettingsNames.FollowAccountCaption}" />
```

Replace the per-selection row:

```xml
                            <!-- Per-selection actions -->
                            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,12,0,0">
```

with:

```xml
                            <!-- Per-selection actions, and why they're dim until a row is picked (WF29). -->
                            <StackPanel Grid.Row="2" Margin="0,12,0,0">
                            <StackPanel Orientation="Horizontal">
```

and after that row's `Remove account…` button and its closing `</StackPanel>` (the one directly before `<!-- Add -->`), replace:

```xml
                                        Command="{Binding SignOutCommand}" Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                            </StackPanel>
```

with:

```xml
                                        Command="{Binding SignOutCommand}" Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
                            </StackPanel>
                            <TextBlock Text="{x:Static core:SettingsNames.PickAnAccount}" FontSize="10.5" Margin="0,4,0,0"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextDim}">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedAccount}" Value="{x:Null}">
                                                <Setter Property="Visibility" Value="Visible" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                            </StackPanel>
```

(The inner panel keeps its indent so the button lines are unchanged in the diff.)

- [ ] **Step 5: The manual key window**

`Views/ManualKeyWindow.xaml`, header:

```xml
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Add account by sessionKey"
        Height="470" Width="520"
```
```xml
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:core="clr-namespace:Sanduhr.Core;assembly=Sanduhr.Core"
        Title="Add account by sessionKey"
        SizeToContent="Height" Width="520"
```

```xml
                       Text="On claude.ai: open DevTools (F12) → Application → Cookies → claude.ai, then copy the value of the sessionKey cookie." />
```
```xml
                       Text="{x:Static core:SettingsNames.ManualKeySteps}" />
```

Replace the cf_clearance rows:

```xml
        <TextBlock Grid.Row="6" Text="cf_clearance (optional — only if Cloudflare blocks)" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,14,0,4" />
        <TextBox Grid.Row="7" x:Name="CfClearanceBox" FontSize="13" Padding="8,6"
```

with:

```xml
        <TextBlock Grid.Row="6" x:Name="CloudflareToggle" Margin="0,14,0,4" Cursor="Hand"
                   MouseLeftButtonUp="OnCloudflareToggleClick">
            <Run Text="{x:Static core:SettingsNames.ManualKeyCloudflareToggle}"
                 Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                 TextDecorations="Underline" FontSize="11" />
        </TextBlock>
        <StackPanel Grid.Row="7" x:Name="CloudflarePanel" Visibility="Collapsed">
        <TextBlock Text="{x:Static core:SettingsNames.ManualKeyCloudflareHelp}" FontSize="11" TextWrapping="Wrap"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,6" />
        <TextBlock Text="{x:Static core:SettingsNames.ManualKeyCfLabel}" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,4" />
        <TextBox x:Name="CfClearanceBox" FontSize="13" Padding="8,6"
```

and close the panel after the TextBox:

```xml
                 FontFamily="Consolas, monospace" />

        <TextBlock Grid.Row="8" x:Name="ErrorText"
```
```xml
                 FontFamily="Consolas, monospace" />
        </StackPanel>

        <TextBlock Grid.Row="8" x:Name="ErrorText"
```

(Only the cf_clearance TextBox ends that way directly before `ErrorText`; the sessionKey box is followed by the old row 6.)

`Views/ManualKeyWindow.xaml.cs`, after `OnHelpToggleClick` add:

```csharp

    /// <summary>Shows or hides the cf_clearance field behind "Seeing a Cloudflare error?" (WF31).</summary>
    private void OnCloudflareToggleClick(object sender, MouseButtonEventArgs e)
        => CloudflarePanel.Visibility = CloudflarePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~AccountFollowTests"`
Expected: PASS.

Run: `dotnet build src/Sanduhr.App`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml.cs windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs
git commit -m "feat(settings-v2): follow pause length, account hint, plain sessionKey steps" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Core words, defined once (WF21)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs` (definitions)
- Modify: `windows-dotnet/src/Sanduhr.Core/WelcomeTour.cs:41-55` (three bodies)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (Desk title area), `Views/WidgetsPage.xaml:110-111`
- Test: `WelcomeTourTests.cs`, `SettingsNamesTests.cs`, `SettingsSourceScanTests.cs`

**Interfaces:**
- Produces: consts `DefineDesk`, `DefineWidget`, `DefinePiece`, `DefineWatchers`, `DefineCompanion`, `DefineMod`, `DefineWidgetPack`, `DeskLine`, `WidgetPackLine`; `CoreWords` (`IReadOnlyList<(string Word, string Definition)>`). Watchers' and Companion's lines join their home pages in slice 3; Mod's joins the Mods & Widgets intro in slice 4.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsNamesTests`:

```csharp
    [Fact]
    public void Core_words_are_defined_once()
    {
        Assert.Equal(
            new (string, string)[]
            {
                ("Desk", "the clock, meters and pieces Sanduhr draws on your desktop"),
                ("Widget", "the small floating window with your meters"),
                ("Piece", "one thing on the Desk"),
                ("Watchers", "cards that show work in progress, from Claude or your CI"),
                ("Companion", "a small animated sprite on the Desk"),
                ("Mod", "an add-on that Claude Code loads"),
                ("Widget pack", "an add-on for Sanduhr: a theme, colors, a font or a companion"),
            },
            SettingsNames.CoreWords);
        Assert.Equal("The Desk is the clock, meters and pieces Sanduhr draws on your desktop.", SettingsNames.DeskLine);
        Assert.Equal("A widget pack is an add-on for Sanduhr: a theme, colors, a font or a companion.", SettingsNames.WidgetPackLine);
    }
```

Append to `WelcomeTourTests` (inside the class; it has a `ById` helper):

```csharp
    [Fact]
    public void The_tour_defines_the_Desk_a_piece_the_widget_and_a_mod()
    {
        Assert.Contains(SettingsNames.DefineDesk, ById("desk").Card.Body);
        Assert.Contains(SettingsNames.DefinePiece, ById("desk").Card.Body);
        Assert.Contains(SettingsNames.DefineWidget, ById("paced").Card.Body);
        Assert.Contains(SettingsNames.DefineMod, ById("claude-code").Card.Body);
    }
```

Append to `SettingsSourceScanTests`:

```csharp
    [Fact]
    public void The_Desk_and_Widgets_pages_open_with_their_definitions()
    {
        Assert.Contains("core:SettingsNames.DeskLine", TabRange("Desk"));
        Assert.Contains("core:SettingsNames.WidgetPackLine",
            File.ReadAllText(Path.Combine(AppDir(), "Views", "WidgetsPage.xaml")));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~WelcomeTourTests|FullyQualifiedName~SettingsSourceScanTests"`
Expected: build FAILS (`'SettingsNames' does not contain a definition for 'CoreWords'`).

- [ ] **Step 3: Definitions in `SettingsNames.cs`**

Before the `TrademarkNotice` constant add:

```csharp
    // ---- Core words (WF21): each is defined once, used as its home page's first line and in the tour.
    public const string DefineDesk = "the clock, meters and pieces Sanduhr draws on your desktop";
    public const string DefineWidget = "the small floating window with your meters";
    public const string DefinePiece = "one thing on the Desk";
    public const string DefineWatchers = "cards that show work in progress, from Claude or your CI";
    public const string DefineCompanion = "a small animated sprite on the Desk";
    public const string DefineMod = "an add-on that Claude Code loads";
    public const string DefineWidgetPack = "an add-on for Sanduhr: a theme, colors, a font or a companion";

    /// <summary>The Desk page's first line.</summary>
    public const string DeskLine = "The Desk is " + DefineDesk + ".";
    /// <summary>The Widgets page's first line (Mods &amp; Widgets' in slice 4).</summary>
    public const string WidgetPackLine = "A widget pack is " + DefineWidgetPack + ".";

    /// <summary>Every core word and its definition.</summary>
    public static IReadOnlyList<(string Word, string Definition)> CoreWords { get; } =
    [
        ("Desk", DefineDesk),
        ("Widget", DefineWidget),
        ("Piece", DefinePiece),
        ("Watchers", DefineWatchers),
        ("Companion", DefineCompanion),
        ("Mod", DefineMod),
        ("Widget pack", DefineWidgetPack),
    ];
```

- [ ] **Step 4: The tour (at most two sentences and 200 characters each, as `CopyIsShortAndPlain` pins)**

`WelcomeTour.cs`, step 1:

```csharp
            "Each bar is one of your limits, filling as you use it. The tick marks where an even pace would put you right now.",
```
```csharp
            "The widget is the small floating window with your meters, one bar per limit. The tick on each bar marks where an even pace would put you right now.",
```

step 2:

```csharp
            "The Desk puts a clock, your meters, a message and more on the desktop, under every window. Arrange each piece from Settings, Desk.",
```
```csharp
            "The Desk is the clock, meters and pieces Sanduhr draws on your desktop, under every window. A piece is one thing on the Desk, and you arrange each from Settings, Desk.",
```

step 5:

```csharp
            "The Claude Usage page shows your Claude Code tokens by day and project. The Claude Code page adds the MCP tools and the statusline, each after you say yes.",
```
```csharp
            "The Claude Usage page shows your Claude Code tokens by day and project. The Claude Code page adds the MCP tools, the statusline and a mod, an add-on that Claude Code loads, each after you say yes.",
```

(Lengths: 147, 167 and 196 characters. `WelcomeTour.SignedOut` rewrites step 1's body by itself, so `SignedOut_ChangesOnlyStepOne` is untouched.)

- [ ] **Step 5: The pages' first lines**

`Views/SettingsWindow.xaml`, Desk tab:

```xml
                            <TextBlock Text="Desk" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```
```xml
                            <TextBlock Text="Desk" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                            <TextBlock Text="{x:Static core:SettingsNames.DeskLine}" FontSize="11" TextWrapping="Wrap"
                                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,0,0,12" />
```

`Views/WidgetsPage.xaml`:

```xml
                <TextBlock Text="Widgets" FontSize="14" FontWeight="SemiBold"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,12" />
```
```xml
                <TextBlock Text="Widgets" FontSize="14" FontWeight="SemiBold"
                           Foreground="{DynamicResource Sanduhr.Brush.Text}" Margin="0,0,0,4" />
                <TextBlock Text="{x:Static core:SettingsNames.WidgetPackLine}" Style="{StaticResource Dim}" Margin="0,0,0,12" />
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Sanduhr.Tests --filter "FullyQualifiedName~SettingsNamesTests|FullyQualifiedName~WelcomeTourTests|FullyQualifiedName~SettingsSourceScanTests|FullyQualifiedName~WhatsNew"`
Expected: PASS (`CopyIsShortAndPlain` holds the new bodies to 200 characters and two sentences).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.Core/WelcomeTour.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/Views/WidgetsPage.xaml windows-dotnet/tests/Sanduhr.Tests/SettingsNamesTests.cs windows-dotnet/tests/Sanduhr.Tests/WelcomeTourTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsSourceScanTests.cs
git commit -m "feat(settings-v2): core words defined once, in the tour and on their pages" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Stale comments (WF47)

Comments only, so no test cycle; the build is the check.

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/SettingsNames.cs:80`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs:309-310`
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs:13-14`
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs:9-14`

**Interfaces:** none.

- [ ] **Step 1: Rewrite the four comments**

`SettingsNames.cs`:

```csharp
    /// <summary>Tray and widget menus: opens Settings where it was left.</summary>
```
```csharp
    /// <summary>Tray and widget menus: opens Settings. Where it lands is App's rule (Settings v2, "Deep links"):
    /// the page last open this session, else General.</summary>
```

`WidgetViewModel.cs`:

```csharp
    /// <summary>Raised by the "Settings…" command (title-bar chip, widget right-click,
    /// tray) — App opens the <c>SettingsWindow</c> on the Accounts tab.</summary>
```
```csharp
    /// <summary>Raised by the "Settings…" command (title-bar chip, widget right-click,
    /// tray). App opens the <c>SettingsWindow</c>; which page it lands on is App's rule.</summary>
```

`SettingsWindow.xaml.cs`:

```csharp
/// The Settings window (Settings v2): a grouped sidebar with search over General,
/// Accounts, Alerts, History, Claude Usage, Publish usage, Claude Code, Mods &amp; Config, Themes and About. The
```
```csharp
/// The Settings window (Settings v2): a grouped sidebar with search over the pages
/// <see cref="SettingsNames.Sidebar"/> lists, in its order. The
```

`SettingsViewModel.cs`:

```csharp
/// <summary>
/// Backs the tabbed <c>SettingsWindow</c> (port of <c>settings_dialog.py</c>'s
/// shell): the <b>Accounts</b> tab (multi-account), <b>Themes</b> (item 8),
/// <b>History</b> (item 9) + <b>Claude Usage</b> (WS-C), and the <b>General</b>
/// tab — which surfaces version + the active account and now hosts the
/// item-10 auto-start control via <see cref="GeneralViewModel"/>.
/// </summary>
```
```csharp
/// <summary>
/// Backs the <c>SettingsWindow</c>: one view model per page in <see cref="SettingsNames.Sidebar"/>,
/// plus the search and the About page's own commands.
/// </summary>
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Sanduhr.App`
Expected: Build succeeded, 0 errors, no new warnings (the `<see cref>`s resolve).

- [ ] **Step 3: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/SettingsNames.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs
git commit -m "docs(settings-v2): comments that described pages and tabs no longer there" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Whole-suite check and the slice's done line

**Files:** none changed unless a check fails.

- [ ] **Step 1: Run every test project**

Run: `dotnet test`
Expected: PASS for `Sanduhr.Tests`, `Sanduhr.Mcp.Tests` and `Sanduhr.App.Tests` (App tests use temp profiles; none starts `Sanduhr.exe`). `SettingsKeysContractTests` and `TrustBoundaryTests` pass unedited.

- [ ] **Step 2: Confirm nothing outside the slice moved**

Run: `git diff release/4.0 --stat -- windows-dotnet/src/Sanduhr.Core/SettingsIndex.cs windows-dotnet/src/Sanduhr.Mcp/Sanduhr.Mcp.csproj windows-dotnet/tests/Sanduhr.Mcp.Tests/TrustBoundaryTests.cs windows-dotnet/tests/Sanduhr.Tests/SettingsKeysContractTests.cs windows-dotnet/src/Sanduhr.Core/WhatsNewCards.cs`
Expected: no output.

Run: `git diff release/4.0 -- windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml | grep -c "^[-+].*<TabItem"`
Expected: `0` (no page added, removed or moved; the two group headings sit on attribute-only lines).

- [ ] **Step 3: Confirm line endings survived**

Run: `git ls-files --eol windows-dotnet/src windows-dotnet/tests | grep -v "i/lf" | grep -v "i/-text"`
Expected: no output (the index stays LF everywhere; new files went in through the Write tool and git normalizes them).

- [ ] **Step 4: Record the hand-offs**

Paste the "Hand-offs to other slices" section below into the slice 1 PR description, so slices 2 to 5 pick the items up.

---

## Hand-offs to other slices

**Slice 2 (it alone edits `SettingsIndex.cs`):**
- Point four search titles at the table: `SettingsIndex.cs:149` "History vault" → `SettingsNames.SessionArchive`; `:163` "Glow when Claude needs you" → `SettingsNames.ClaudeCodeGlowHook`; `:174` "History kept" → `SettingsNames.TranscriptsKept`; `:180` "Homes to share" → "Folders to share" (the section title; the control's full label is `SettingsNames.FoldersToShare`). Keep the old words as synonyms ("history vault", "usage vault", "homes", "history kept") so `Old_words_still_find_the_renamed_controls` stays green.
- Replace `SettingsNames.ExportCsv` at `SettingsIndex.cs:145` and `:271` with `ExportMeterHistory`, then delete the alias from `SettingsNames.cs` in a one-line follow-up that slice 1's owner reviews. **Delete `tests/Sanduhr.Tests/SettingsNamesTests.cs:258` in the same commit**: `Assert.Equal(SettingsNames.ExportMeterHistory, SettingsNames.ExportCsv)` is the one remaining use, so the "nothing uses it" grep prints that line and removing the alias alone breaks the build. Slice 2's Task 19 Step 7 carries this.
- Remove `"SettingsIndex.cs"` from `NotScanned` in `SettingsSourceScanTests.cs` once the titles above are changed.
- Alerts layout items that are WF43 but touch the block slice 2 restructures: the page title ("Alerts", as other pages have) and the "%" suffix after Warn at and Urgent at.
- `SettingsViewModel.TrademarkNotice` is unused after Task 7; delete it with slice 2's `SettingsViewModel.cs` edits.
- **Sidebar group headers (P13).** Five `views:SettingsSidebar.GroupHeader` values in `SettingsWindow.xaml` are still literals ("Usage", "Extend", "Widget", "Desktop", "Help" at roughly `:1108, 1702, 2067, 2332, 2905`). Once the sidebar template reads `SettingsNames.Sidebar`, remove them so the group titles have one source. Slice 2's Task 19 (Step 8) owns it; it is not in slice 1 because the sidebar is slice 2's.
- **Stale class summaries (T11 / P9).** `SettingsWindow.xaml.cs:12-14` still lists "Mods & Config" and the old page set, and `SettingsViewModel.cs:9-14` still says "tabbed" and "item 8/9/10". Rewrite both to the sidebar and the page set as slice 2 leaves them, in the same commits that touch those files (slice 2 already adds a paragraph to the window summary). Slice 2's Task 19 (Step 8) carries the rewrite.
- **Names table and anchors.** `SettingsNames.Table` and `SettingsIndex.NameAnchors` were not extended with slice 1's new control names: `ExportSessions`, `SessionArchive`, `FoldersToShare`, `ClaudeCodeGlowHook`, `TranscriptsKept`, `LetClaudeReadTotals`, `SnakeSoundAtFull`, `DeskFont`, `MessageFont`, `MessageColor`. Slice 2's Task 19 (Step 9) adds a `Table` row (name, page) and a `NameAnchors` row (name, section anchor) for each, on the page each sits on today, so a Settings link by control name resolves. The `Table` rows are the one `SettingsNames.cs` edit slice 2 makes; the `Define*` parts and the "titles to come" stay out of `Table` on purpose.

**Slice 3:** "Edit buttons…" becomes "Launcher settings…" when the Launcher page exists (`DeskClicks.LauncherSettings`). The watchers MCP text and `ToolCatalog.cs:686` become "Settings, Watchers". The duplicate spoken "Sign-in reminders" heading goes when the switch moves to Desk, Behavior. `DefineWatchers` and `DefineCompanion` become the first lines of Watchers and the Companion row. Desk card bodies name the exact section ("Settings, Desk Look, Ink and color").

**Slice 4:** "Uninstall…" with an ellipsis needs a confirmation first (today it acts at once, so the label stays "Uninstall"). `DefineMod` and `WidgetPackLine` join the Mods & Widgets intro; the Mods page title becomes `ModsAndWidgetsTitle`.

**Slice 5:** the Remove account… caption under the button, with the Data section. `DefineWidget` becomes the first line of the Widget page. The owner's sign-off is still open on the tour carrying 4 of the 7 core words (P14); record it when the Widget page lands.

**The 4.0 docs pass:** `docs/PRIVACY.md` still quotes controls slice 1 renamed: "Glow when Claude needs you", "Erase archive", "Mods & Config" and the old "Settings ▸ …" path form. Update those rows to the new names and the "Settings, <Page>, <Section>" form. The page titles that shipped ("Claude Usage", "Publish usage") stay as they are.
