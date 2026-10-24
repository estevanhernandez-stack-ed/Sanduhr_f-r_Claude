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
        // A group's heading sits on its first page and nowhere else.
        var expected = SettingsNames.Pages.Select(p =>
            SettingsNames.Sidebar.FirstOrDefault(g => g.Pages[0] == p)?.Title ?? "");
        Assert.Equal(expected, tabs.Select(t => Attr(t, "views:SettingsSidebar.GroupHeader")));
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
}
