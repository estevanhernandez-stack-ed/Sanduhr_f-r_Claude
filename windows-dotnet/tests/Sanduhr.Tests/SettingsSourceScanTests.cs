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
