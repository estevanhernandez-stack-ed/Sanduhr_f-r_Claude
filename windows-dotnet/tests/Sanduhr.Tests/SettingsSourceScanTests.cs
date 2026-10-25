using System.Net;
using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Scans the App's source for strings that still use a retired name or link to a page that does not
/// exist, and checks every search anchor is marked in SettingsWindow.xaml.
///
/// Limits of the C# literal scan: a line-by-line regex over plain "..." and $"..." literals (the
/// text between the quotes, escapes decoded with Regex.Unescape). Verbatim @"..." strings that span
/// lines, raw """ strings and block comments are not understood; a line whose trimmed start is //
/// or /// is skipped. Interpolation holes stay in the text, and a string inside a hole is read as a
/// literal of its own. XAML: the Header, Content, Text, ToolTip and AutomationProperties.Name
/// attribute values, entity-decoded, with XML comments blanked.
/// </summary>
public class SettingsSourceScanTests
{
    private static readonly Regex CsLiteral = new("(?<!\\\\)\"((?:[^\"\\\\\\r\\n]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex XamlAttr = new(
        "\\b(?:Header|Content|Text|ToolTip|AutomationProperties\\.Name)=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex XmlComment = new("<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Sanduhr.App");
    }

    private static string SrcDir() => Path.GetFullPath(Path.Combine(AppDir(), ".."));

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
    private static string Bare(string label) => label.Trim().TrimEnd('…').TrimEnd(':').Trim();

    /// <summary>The window and the pages and panels it hosts.</summary>
    private static IEnumerable<string> SettingsXamlFiles()
    {
        var views = Path.Combine(AppDir(), "Views");
        return new[] { Path.Combine(views, "SettingsWindow.xaml") }
            .Concat(Directory.EnumerateFiles(views, "*Page.xaml").Order(StringComparer.Ordinal))
            .Concat(Directory.EnumerateFiles(views, "*Panel.xaml").Order(StringComparer.Ordinal))
            .Append(Path.Combine(views, "ThemeDeskEditor.xaml"));
    }

    /// <summary>One TabItem of SettingsWindow.xaml, from its start tag to its end tag.</summary>
    private static string TabRange(string tag)
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        var m = Regex.Match(xaml, "<TabItem\\b[^>]*?\\bTag=\"" + tag + "\"[^>]*>");
        Assert.True(m.Success, "no TabItem with Tag=" + tag);
        return xaml[m.Index..xaml.IndexOf("</TabItem>", m.Index, StringComparison.Ordinal)];
    }

    /// <summary>Files the scan leaves out. The names table holds every retired name on purpose.</summary>
    private static readonly string[] NotScanned = ["SettingsNames.cs"];

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

    private static IEnumerable<(string Where, string Text)> XamlStrings()
    {
        foreach (var file in Files("*.xaml"))
        {
            var raw = File.ReadAllText(file);
            var src = XmlComment.Replace(raw, m => new string('\n', m.Value.Count(c => c == '\n')));
            foreach (Match m in XamlAttr.Matches(src))
            {
                int line = 1 + src.Take(m.Index).Count(c => c == '\n');
                yield return ($"{Path.GetRelativePath(AppDir(), file)}:{line}", WebUtility.HtmlDecode(m.Groups[1].Value));
            }
        }
    }

    [Fact]
    public void App_strings_use_no_retired_name_and_link_to_no_unknown_page()
    {
        var bad = new List<string>();
        foreach (var (where, text) in CsStrings().Concat(XamlStrings()))
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

    [Fact]
    public void Every_search_anchor_is_marked_in_its_pages_TabItem()
    {
        var path = Path.Combine(AppDir(), "Views", "SettingsWindow.xaml");
        Assert.True(File.Exists(path), "SettingsWindow.xaml not found at " + path);
        var xaml = File.ReadAllText(path);

        // TabItems are not nested: each range runs from its start tag to the next </TabItem>.
        var ranges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(xaml, "<TabItem\\b[^>]*?\\bTag=\"(\\w+)\"[^>]*>"))
        {
            int end = xaml.IndexOf("</TabItem>", m.Index, StringComparison.Ordinal);
            if (end < 0) end = xaml.Length;
            if (!ranges.TryGetValue(m.Groups[1].Value, out var list))
                ranges[m.Groups[1].Value] = list = [];
            var range = xaml[m.Index..end];
            list.Add(range);
            // A page drawn by its own control (S6: <views:DeskMessagePage />) carries its anchors there.
            foreach (Match c in Regex.Matches(range, "<views:(\\w+Page)\\b"))
            {
                var page = Path.Combine(AppDir(), "Views", c.Groups[1].Value + ".xaml");
                if (File.Exists(page)) list.Add(File.ReadAllText(page));
            }
        }

        var missing = new List<string>();
        foreach (var e in SettingsIndex.Anchors)
        {
            // Quit is the sidebar foot's button, not a section of General: its own test finds it.
            if (e.Anchor == SettingsIndex.Quit) continue;
            var tag = e.Page.ToString();
            var marker = $"views:SettingsAnchor.Id=\"{e.Anchor}\"";
            if (!ranges.TryGetValue(tag, out var found))
                missing.Add($"{tag}/{e.Anchor} (no TabItem with Tag=\"{tag}\")");
            else if (!found.Any(r => r.Contains(marker, StringComparison.Ordinal)))
                missing.Add($"{tag}/{e.Anchor}");
        }
        Assert.True(missing.Count == 0, "Anchors not marked: " + string.Join(", ", missing));
    }

    [Fact]
    public void Quit_sits_at_the_sidebar_foot_named_and_wired_to_the_quit_command()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        var button = Regex.Match(xaml, "<Button x:Name=\"QuitButton\"[^>]*>", RegexOptions.Singleline);
        Assert.True(button.Success, "no QuitButton in the sidebar template");
        Assert.Contains($"AutomationProperties.Name=\"{SettingsNames.QuitSanduhr}\"", button.Value);
        Assert.Contains("Command=\"{Binding QuitCommand}\"", button.Value);
        // Search finds it, and Enter on the hit puts the keyboard on the button.
        Assert.Contains(SettingsIndex.Search("quit"), h => h.Entry.Anchor == SettingsIndex.Quit);
        Assert.Contains(SettingsIndex.Search("exit"), h => h.Entry.Anchor == SettingsIndex.Quit);
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

    [Fact]
    public void The_graph_pin_and_What_s_New_controls_are_named_from_the_table()
    {
        var strip = File.ReadAllText(Path.Combine(AppDir(), "MainWindow.xaml"));
        foreach (var name in new[] { "GraphStyle", "KeepWidgetOnTop" })
            Assert.Contains($"AutomationProperties.Name=\"{{x:Static core:SettingsNames.{name}}}\"", strip);
        // The strip's tooltips lead with the same words, and the pin says what it does in both states.
        Assert.Contains("ToolTip\" Value=\"Graph style: classic line (click to cycle)\"", strip);
        Assert.Contains("ToolTip\" Value=\"Graph style: horizon bands (click to cycle)\"", strip);
        Assert.Contains("ToolTip\" Value=\"{x:Static core:SettingsNames.KeepWidgetOnTop}\"", strip);

        var whatsNew = File.ReadAllText(Path.Combine(AppDir(), "Views", "WhatsNewWindow.xaml"));
        var box = Regex.Match(whatsNew, "<CheckBox\\b[^>]*HideAfterUpdates[^>]*>", RegexOptions.Singleline);
        Assert.True(box.Success, "no check box bound to HideAfterUpdates");
        Assert.Equal(SettingsNames.HideWhatsNewAfterUpdates, Resolve(box.Value, "Content"));
        Assert.Equal(SettingsNames.HideWhatsNewAfterUpdates, Resolve(box.Value, "AutomationProperties.Name"));
    }

    [Fact]
    public void The_folder_consent_list_and_the_Themes_fields_and_lists_carry_a_spoken_name_that_starts_with_their_label()
    {
        // Publish usage: the heading is the visible label, the list says the same words.
        var publish = TabRange("Publish");
        Assert.Matches("<TextBlock Text=\"\\{x:Static core:SettingsNames\\.FoldersToShare\\}\"", publish);
        Assert.Matches("<ItemsControl x:Name=\"PublishHomesList\" ItemsSource=\"\\{Binding Roots\\}\"[^>]*AutomationProperties\\.Name=\"\\{x:Static core:SettingsNames\\.FoldersToShare\\}\"", publish);

        // Themes: every text box beside a visible label (Filename, Name, Save the theme as) and both
        // labelled lists. LabelThenField checks the first three against their labels in
        // Visible_labels_start_their_spoken_names; this pins that none is left unnamed.
        var themes = TabRange("Themes");
        foreach (Match m in LabelThenField.Matches(themes))
        {
            // A bound label (the token rows' own hex boxes) names its box through the same binding.
            if (m.Groups[2].Value != "TextBox" || ResolveValue(m.Groups[1].Value) is not { } label) continue;
            var name = Resolve(m.Groups[3].Value, "AutomationProperties.Name");
            Assert.True(name is not null, $"the text box after \"{label}\" has no spoken name");
            Assert.StartsWith(Bare(label), name);
        }
        Assert.Equal(3, LabelThenField.Matches(themes).Count(m => m.Groups[2].Value == "TextBox" && ResolveValue(m.Groups[1].Value) is not null));
        Assert.Matches("<ListBox x:Name=\"ThemeGallery\"[^>]*AutomationProperties\\.Name=\"Choose a theme\"", themes);
        Assert.Matches("<ListBox x:Name=\"ThemesList\"[^>]*AutomationProperties\\.Name=\"Installed user themes\"", themes);
    }

    [Fact]
    public void The_scan_sees_known_strings()
    {
        // A positive control: if either regex stops matching, the retired-name test would pass
        // having scanned nothing.
        var cs = CsStrings().ToList();
        var xaml = XamlStrings().ToList();
        Assert.Contains(cs, x => x.Where.StartsWith("App.xaml.cs", StringComparison.Ordinal) && x.Text == "Show");
        Assert.Contains(xaml, x => x.Text == "Sanduhr — Settings");
        Assert.True(cs.Count > 200 && xaml.Count > 100, $"scanned {cs.Count} C# and {xaml.Count} XAML strings");
    }

    [Fact]
    public void The_sidebar_in_XAML_matches_the_names_table()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "SettingsWindow.xaml"));
        var tabs = Regex.Matches(xaml, @"<TabItem\b([^>]*)>").Select(m => m.Groups[1].Value).ToList();
        string Attr(string tag, string name)
        {
            var m = Regex.Match(tag, @"(?<![\w.:])" + Regex.Escape(name) + "=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        Assert.Equal(SettingsNames.Pages.Select(p => p.ToString()), tabs.Select(t => Attr(t, "Tag")));
        Assert.Equal(SettingsNames.Pages.Select(SettingsNames.PageTitle), tabs.Select(t => WebUtility.HtmlDecode(Attr(t, "Header"))));
        Assert.Equal(SettingsNames.Pages.Select(SettingsNames.PageTitle), tabs.Select(t => WebUtility.HtmlDecode(Attr(t, "AutomationProperties.Name"))));
        Assert.DoesNotContain("SettingsSidebar.GroupHeader=\"", xaml); // one source: SettingsWindow sets it from SettingsNames.Sidebar
    }

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

    [Fact]
    public void The_dialogs_and_reset_wording_use_the_archive_and_folder_names()
    {
        // Lowercase or prose uses the retired-name scan can't see: "home" for a Claude Code folder,
        // "the usage vault" for the session archive.
        var views = Path.Combine(AppDir(), "Views");
        var reset = File.ReadAllText(Path.Combine(AppDir(), "Services", "ResetService.cs"));
        Assert.Contains("\"the session archive\"", reset);
        Assert.DoesNotContain("the usage vault", reset);

        var consentXaml = File.ReadAllText(Path.Combine(views, "McpConsentDialog.xaml"));
        var consentCs = File.ReadAllText(Path.Combine(views, "McpConsentDialog.xaml.cs"));
        Assert.Contains("Pick which Claude Code folder gets it", consentXaml);
        Assert.Contains("each Claude Code folder needs its own checkbox", consentXaml);
        Assert.Contains("only for folders checked above", consentCs);
        Assert.DoesNotContain("Claude Code home", consentXaml);
        Assert.DoesNotContain("for homes checked", consentCs);

        var settings = File.ReadAllText(Path.Combine(views, "SettingsWindow.xaml"));
        Assert.Contains("ToolTip=\"Find a setting by name: tray, archive, CSV, shortcut, theme\"", settings);
        Assert.DoesNotContain("tray, vault,", settings);

        var vault = File.ReadAllText(Path.Combine(views, "VaultConsentDialog.xaml"));
        Assert.Contains("Text=\"Keep a local session archive?\"", vault);
        Assert.Contains("Content=\"Keep archive\"", vault);
        Assert.DoesNotContain("usage history", vault);
    }

    [Fact]
    public void The_trademark_notice_has_one_source_and_both_windows_bind_to_it()
    {
        const string phrase = "trademarks of Anthropic";
        var appDir = AppDir();
        var coreFile = Path.GetFullPath(Path.Combine(appDir, "..", "Sanduhr.Core", "SettingsNames.cs"));
        Assert.Contains(phrase, File.ReadAllText(coreFile));

        var offenders = Files("*.xaml").Concat(Files("*.cs"))
            .Where(f => File.ReadAllText(f).Contains(phrase, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(appDir, f))
            .ToList();
        Assert.True(offenders.Count == 0,
            "Hardcoded trademark text (bind SettingsNames.TrademarkNotice instead): " + string.Join(", ", offenders));

        // The manifest keeps its own wording; it is the only other place the phrase may live.
        Assert.Contains(phrase, File.ReadAllText(Path.Combine(appDir, "Package.appxmanifest")));

        foreach (var window in new[] { "WhatsNewWindow.xaml", "SignInWindow.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(appDir, "Views", window));
            Assert.Contains("SettingsNames.TrademarkNotice", xaml);
        }
    }

    [Fact]
    public void The_trademark_footer_is_one_control_on_seven_pages()
    {
        var views = Path.Combine(AppDir(), "Views");
        int Count(string file) => Regex.Matches(File.ReadAllText(Path.Combine(views, file)), "<views:TrademarkFooter\\b").Count;
        Assert.Equal(4, Count("SettingsWindow.xaml"));   // Desk, Now playing, Weather, About
        Assert.Equal(1, Count("DeskMessagePage.xaml"));
        Assert.Equal(1, Count("WidgetsPage.xaml"));
        Assert.Equal(1, Count("DeskLookPage.xaml"));
        Assert.Contains("core:SettingsNames.TrademarkNotice", File.ReadAllText(Path.Combine(views, "TrademarkFooter.xaml")));
        Assert.Contains("<views:TrademarkFooter views:SettingsAnchor.Id=\"trademark\"", TabRange("About"));
        var copies = Files("*.xaml")
            .Where(f => Path.GetFileName(f) is not ("TrademarkFooter.xaml" or "WhatsNewWindow.xaml" or "SignInWindow.xaml"))
            .Where(f => File.ReadAllText(f).Contains("TrademarkNotice", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(copies.Count == 0, "Use <views:TrademarkFooter /> in: " + string.Join(", ", copies));
    }

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

    [Fact]
    public void Each_Message_radio_group_sits_in_a_named_container()
    {
        var page = File.ReadAllText(Path.Combine(AppDir(), "Views", "DeskMessagePage.xaml"));
        var groups = RadioGroups(page);
        foreach (var (group, nameStart) in new[] { ("MessageMode", "Message view"), ("MessageRotate", "Rotation"), ("MessageSpecial", "On special days") })
        {
            var holders = groups.Where(g => g.Body.Contains($"GroupName=\"{group}\"")).ToList();
            var holder = Assert.Single(holders);
            var name = Regex.Match(holder.Open, "AutomationProperties\\.Name=\"([^\"]*)\"");
            Assert.True(name.Success, $"{group}: the container has no AutomationProperties.Name");
            Assert.StartsWith(nameStart, name.Groups[1].Value);
        }
        // No radio sits outside a named container.
        var radios = Regex.Matches(page, "<RadioButton\\b").Count;
        var inside = groups.Sum(g => Regex.Matches(g.Body, "<RadioButton\\b").Count);
        Assert.Equal(radios, inside);
    }

    /// <summary>
    /// The RadioGroupPanel elements of a XAML file, each with its opening-tag attributes and its own body:
    /// the text between its tags without the bodies of groups nested inside it, so a radio belongs to the
    /// innermost group around it. A self-closing panel has an empty body.
    /// </summary>
    private static List<(string Open, string Body)> RadioGroups(string xaml)
    {
        var tokens = Regex.Matches(xaml, "<controls:RadioGroupPanel\\b([^>]*?)(/?)>|</controls:RadioGroupPanel>");
        var found = new List<(string Open, string Body)>();
        var stack = new Stack<OpenGroup>();
        foreach (Match t in tokens)
        {
            // Whatever lies between the previous tag and this one is the innermost open group's own text.
            if (stack.Count > 0)
                stack.Peek().TakeUpTo(xaml, t.Index);
            if (t.Value.StartsWith("</", StringComparison.Ordinal))
            {
                Assert.True(stack.Count > 0, "a </controls:RadioGroupPanel> with no opening tag");
                var done = stack.Pop();
                found.Add((done.Open, done.Own.ToString()));
                if (stack.Count > 0)
                    stack.Peek().Resume = t.Index + t.Length;
            }
            else if (t.Groups[2].Value == "/")
            {
                found.Add((t.Groups[1].Value, ""));
                if (stack.Count > 0)
                    stack.Peek().Resume = t.Index + t.Length;
            }
            else
            {
                stack.Push(new OpenGroup { Open = t.Groups[1].Value, Resume = t.Index + t.Length });
            }
        }
        Assert.Empty(stack);
        return found;
    }

    /// <summary>A RadioGroupPanel being read: its attributes, where its own text resumes, and that text so far.</summary>
    private sealed class OpenGroup
    {
        public string Open { get; init; } = "";
        public int Resume { get; set; }
        public System.Text.StringBuilder Own { get; } = new();

        public void TakeUpTo(string xaml, int end)
        {
            if (end > Resume)
                Own.Append(xaml, Resume, end - Resume);
            Resume = end;
        }
    }

    [Fact]
    public void The_radio_group_scan_reads_nested_and_self_closing_panels()
    {
        const string xaml =
            "<controls:RadioGroupPanel AutomationProperties.Name=\"Outer\">" +
            "<RadioButton GroupName=\"A\" />" +
            "<controls:RadioGroupPanel AutomationProperties.Name=\"Inner\"><RadioButton GroupName=\"B\" /></controls:RadioGroupPanel>" +
            "<controls:RadioGroupPanel AutomationProperties.Name=\"Empty\" />" +
            "<RadioButton GroupName=\"C\" />" +
            "</controls:RadioGroupPanel>";
        var groups = RadioGroups(xaml);
        Assert.Equal(3, groups.Count);
        var outer = Assert.Single(groups, g => g.Open.Contains("Outer"));
        Assert.Contains("GroupName=\"A\"", outer.Body);
        Assert.Contains("GroupName=\"C\"", outer.Body);
        Assert.DoesNotContain("GroupName=\"B\"", outer.Body);
        var inner = Assert.Single(groups, g => g.Open.Contains("Inner"));
        Assert.Contains("GroupName=\"B\"", inner.Body);
        Assert.Empty(Assert.Single(groups, g => g.Open.Contains("Empty")).Body);
    }

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

    [Fact]
    public void The_Cloudflare_help_opens_from_a_keyboard_reachable_link()
    {
        // A TextBlock that opens on a mouse click leaves keyboard and screen-reader users without cf_clearance.
        var key = File.ReadAllText(Path.Combine(AppDir(), "Views", "ManualKeyWindow.xaml"));
        Assert.DoesNotContain("MouseLeftButtonUp=\"OnCloudflareToggleClick\"", key);
        var link = Regex.Match(key, "<Hyperlink\\b[^>]*x:Name=\"CloudflareLink\"[^>]*>");
        Assert.True(link.Success, "the Cloudflare toggle is not a Hyperlink named CloudflareLink");
        Assert.Contains("Click=\"OnCloudflareToggleClick\"", link.Value);
        var name = Resolve(link.Value, "AutomationProperties.Name");
        Assert.NotNull(name);
        Assert.StartsWith(SettingsNames.ManualKeyCloudflareToggle, name);
    }

    [Fact]
    public void The_sessionKey_help_opens_from_a_keyboard_reachable_link()
    {
        var key = File.ReadAllText(Path.Combine(AppDir(), "Views", "ManualKeyWindow.xaml"));
        Assert.DoesNotContain("MouseLeftButtonUp=\"OnHelpToggleClick\"", key);
        var link = Regex.Match(key, "<Hyperlink\\b[^>]*x:Name=\"HelpLink\"[^>]*>(.*?)</Hyperlink>", RegexOptions.Singleline);
        Assert.True(link.Success, "the sessionKey help toggle is not a Hyperlink named HelpLink");
        Assert.Contains("Click=\"OnHelpToggleClick\"", link.Value);
        var name = Resolve(link.Value, "AutomationProperties.Name");
        Assert.NotNull(name);
        var visible = Regex.Match(link.Groups[1].Value, "Text=\"([^\"]*)\"").Groups[1].Value;
        Assert.Equal("Where do I find the sessionKey?", visible);
        Assert.StartsWith(visible, name);
    }

    [Fact]
    public void No_toggle_in_the_key_window_opens_on_a_mouse_click_alone()
    {
        // Nothing in the window may act on a mouse click alone: every link is a Hyperlink, which Tab reaches.
        var key = File.ReadAllText(Path.Combine(AppDir(), "Views", "ManualKeyWindow.xaml"));
        Assert.DoesNotContain("MouseLeftButtonUp", key);
        Assert.DoesNotContain("MouseLeftButtonDown", key);
    }

    [Fact]
    public void The_secure_sign_in_link_is_a_keyboard_reachable_link()
    {
        var key = File.ReadAllText(Path.Combine(AppDir(), "Views", "ManualKeyWindow.xaml"));
        var link = Regex.Match(key, "<Hyperlink\\b[^>]*x:Name=\"UseEmbeddedHyperlink\"[^>]*>(.*?)</Hyperlink>", RegexOptions.Singleline);
        Assert.True(link.Success, "the secure sign-in link is not a Hyperlink named UseEmbeddedHyperlink");
        Assert.Contains("Click=\"OnUseEmbeddedClick\"", link.Value);
        var name = Resolve(link.Value, "AutomationProperties.Name");
        Assert.NotNull(name);
        var visible = Regex.Match(link.Groups[1].Value, "Text=\"([^\"]*)\"").Groups[1].Value;
        Assert.Equal("Use the secure sign-in window instead", visible);
        Assert.StartsWith(visible, name);
        // The wrapping TextBlock keeps the name the code-behind shows and hides.
        Assert.Contains("x:Name=\"UseEmbeddedLink\"", key);
    }

    [Fact]
    public void The_shared_dialog_paints_no_button_with_a_hard_coded_color()
    {
        // A literal hex Background pairs a themed Foreground with a fixed fill: dark text on near-black in a light theme (Store 10.1.4.4).
        var xaml = XmlComment.Replace(File.ReadAllText(Path.Combine(AppDir(), "Views", "ThemedDialog.xaml")), "");
        var buttons = Regex.Matches(xaml, "<Button\\b[^>]*>", RegexOptions.Singleline);
        Assert.Equal(3, buttons.Count);   // Primary, Secondary and the Cancel of ShowChoice
        foreach (Match button in buttons)
            Assert.DoesNotMatch("\\bBackground=\"#", button.Value);
    }

    [Fact]
    public void The_Desk_and_Widgets_pages_open_with_their_definitions()
    {
        Assert.Contains("core:SettingsNames.DeskLine", TabRange("Desk"));
        Assert.Contains("core:SettingsNames.WidgetPackLine",
            File.ReadAllText(Path.Combine(AppDir(), "Views", "WidgetsPage.xaml")));
    }

    [Fact]
    public void No_literal_ends_inside_a_Settings_path()
    {
        // "(Settings, " + "Desk);" escapes PathProblems: the page is in the next literal (slice 1 review, split literals). Keep a
        // place in one literal; wrap the prose around it instead.
        var split = CsStrings().Concat(CoreAndMcpStrings())
            .Where(x => x.Text.TrimEnd().EndsWith("Settings,", StringComparison.Ordinal))
            .Select(x => $"{x.Where}: \"{x.Text}\"")
            .ToList();
        Assert.True(split.Count == 0, "A Settings path split across literals: " + string.Join(", ", split));
    }

    [Fact]
    public void Every_radio_in_Settings_sits_in_a_named_group()
    {
        // Slice 1's scan read DeskMessagePage only; the Desk split adds pages. A radio outside a named RadioGroupPanel is read
        // as a lone choice by Narrator.
        var bad = new List<string>();
        foreach (var file in SettingsXamlFiles())
        {
            var src = XmlComment.Replace(File.ReadAllText(file), "");
            int radios = Regex.Matches(src, "<RadioButton\\b").Count;
            if (radios == 0) continue;
            var groups = RadioGroups(src);
            foreach (var g in groups.Where(g => !Regex.IsMatch(g.Open, "AutomationProperties\\.Name=\"[^\"]+\"")))
                bad.Add($"{Path.GetFileName(file)}: a RadioGroupPanel with no AutomationProperties.Name");
            int inside = groups.Sum(g => Regex.Matches(g.Body, "<RadioButton\\b").Count);
            if (inside != radios)
                bad.Add($"{Path.GetFileName(file)}: {radios - inside} radio(s) outside a RadioGroupPanel");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Own_file_pages_draw_a_focus_ring_and_add_no_scroll_stop()
    {
        // P25: the window's styles ring keyboard focus, but a page in its own file can hide the focus rectangle in a local style
        // and draw nothing instead. P5: a page's ScrollViewer must not be an extra, invisible Tab stop.
        var bad = new List<string>();
        foreach (var file in SettingsXamlFiles().Skip(1))   // SettingsWindow.xaml's own styles are pinned by slice 2's tests
        {
            var src = XmlComment.Replace(File.ReadAllText(file), "");
            foreach (Match style in Regex.Matches(src, "<Style\\b.*?</Style>", RegexOptions.Singleline))
                if (Regex.IsMatch(style.Value, "Property=\"FocusVisualStyle\"\\s+Value=\"\\{x:Null\\}\"")
                    && !style.Value.Contains("IsKeyboardFocused", StringComparison.Ordinal))
                    bad.Add($"{Path.GetFileName(file)}: {Regex.Match(style.Value, "<Style\\b[^>]*>").Value} hides the focus rectangle and draws no ring");
            // the element, not <ScrollViewer.Resources>
            foreach (Match sv in Regex.Matches(src, "<ScrollViewer(?=[\\s/>])[^>]*>"))
                if (!sv.Value.Contains("Focusable=\"False\"", StringComparison.Ordinal))
                    bad.Add($"{Path.GetFileName(file)}: {sv.Value} is a Tab stop");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }
}
