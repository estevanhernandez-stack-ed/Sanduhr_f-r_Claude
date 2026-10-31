using System.Text.RegularExpressions;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 6 (spec, Privacy, "What leaves this PC"; WF6): every outbound call in src is disclosed. The scan finds every call shape
/// in Sanduhr.App, Sanduhr.Core and Sanduhr.Mcp (decision 1, amended by preflight P1 and P3). Each line must carry "// network: id", on the
/// line or within the three above, naming a row of NetworkDisclosure.All, "your-browser" or "local". The inventory is pinned file by file and
/// in total, so:
/// <list type="bullet">
/// <item><description>a new call fails here until it is marked and counted;</description></item>
/// <item><description>a call that stops matching (a rename, a refactor) fails too;</description></item>
/// <item><description>a scan that finds nothing can't pass, and synthetic sources prove each shape and the marker rule catch what they should.</description></item>
/// </list>
/// The MCP server may have none at all.
/// </summary>
public class NetworkDisclosureTests
{
    private static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    /// <summary>The call shapes (decision 1; P1 adds the popup hand-off, P3 the forms a future call would take).</summary>
    internal static readonly Regex Call = new(
        @"new\s+HttpClient\s*\("
        + @"|\.(?:GetAsync|GetStringAsync|GetByteArrayAsync|GetStreamAsync|PostAsync|PutAsync|PatchAsync|DeleteAsync|SendAsync)\s*\("
        + @"|\.Navigate\s*\(|\.NavigateToString\s*\(|ExecuteScriptAsync\s*\("
        + @"|new\s+GithubSource\s*\(|new\s+UpdateManager\s*\(|\.CheckForUpdatesAsync\s*\(|\.DownloadUpdatesAsync\s*\("
        + @"|GetAppAndOptionalStorePackageUpdatesAsync\s*\(|RequestDownloadAndInstallStorePackageUpdatesAsync\s*\("
        + @"|\bDns\.|new\s+(?:TcpClient|UdpClient|Socket|ClientWebSocket|WebClient|Ping)\s*\(|WebRequest\.Create|\bHttpWebRequest\b"
        + @"|Process\.Start\s*\(|LaunchUriAsync\s*\("
        + @"|\.NewWindow\s*=(?!=)"
        + @"|\.Send\s*\(|\b\w*(?:From|As)JsonAsync\s*(?:<[^>(]*>)?\s*\(|NavigateWithWebResourceRequest\s*\(|\.Source\s*=\s*new\s+Uri\s*\(|HttpClient\s+\w+\s*=\s*new\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex Marker = new(@"//\s*network:\s*([a-z0-9-]+(?:\s*,\s*[a-z0-9-]+)*)", RegexOptions.Compiled);

    internal sealed record Site(string File, int Line, IReadOnlyList<string> Ids, string Text);

    /// <summary>Every line in src (bin and obj aside) that matches a call shape, outside comments, with its marker's ids.</summary>
    internal static List<Site> Scan()
    {
        var src = Src();
        var sites = new List<Site>();
        foreach (var project in new[] { "Sanduhr.App", "Sanduhr.Core", "Sanduhr.Mcp" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(src, project), "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, path).Replace('\\', '/');
                if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                    continue;
                sites.AddRange(ScanLines(rel, File.ReadAllLines(path)));
            }
        }
        return sites;
    }

    /// <summary>The scan of one file's lines. Split out so a synthetic file can be fed through the same code.</summary>
    internal static List<Site> ScanLines(string rel, string[] lines)
    {
        var sites = new List<Site>();
        for (int i = 0; i < lines.Length; i++)
        {
            var code = lines[i].TrimStart();
            if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
                continue;
            if (Call.IsMatch(lines[i]))
                sites.Add(new Site(rel, i + 1, MarkerFor(lines, i), lines[i].Trim()));
        }
        return sites;
    }

    /// <summary>The marker on the line, else the nearest within the three lines above.</summary>
    private static IReadOnlyList<string> MarkerFor(string[] lines, int i)
    {
        for (int j = i; j >= Math.Max(0, i - 3); j--)
        {
            if (Marker.Match(lines[j]) is { Success: true } m)
                return [.. m.Groups[1].Value.Split(',').Select(s => s.Trim())];
        }
        return [];
    }

    /// <summary>The sites with no marker, as the failure lines.</summary>
    private static List<string> Undisclosed(IEnumerable<Site> sites) =>
        sites.Where(s => s.Ids.Count == 0).Select(s => $"{s.File}:{s.Line}  {s.Text}").ToList();

    /// <summary>The inventory at the base commit, file by file (this plan's "The outbound-call inventory", with preflight P1: Google's popup
    /// hand-off is a sixth sign-in site). A site with two ids counts once under each.</summary>
    private static readonly (string File, string Id, int Count)[] Known =
    [
        ("Sanduhr.App/Services/WebView2ApiClient.cs", "claude-usage", 2),
        ("Sanduhr.Core/ClaudeApiClient.cs", "claude-usage", 3),
        ("Sanduhr.App/Views/SignInWindow.xaml.cs", "claude-sign-in", 6),
        ("Sanduhr.App/Desk/WeatherService.cs", "open-meteo", 3),
        ("Sanduhr.App/Services/WidgetCatalogService.cs", "widget-catalog", 2),
        ("Sanduhr.App/Updates/UpdateChecker.cs", "github-update", 4),
        ("Sanduhr.App/Services/StoreUpdateService.cs", "store-update", 3),
        ("Sanduhr.Core/UsagePublisher.cs", "publish-usage", 2),
        ("Sanduhr.Core/UsagePublisher.cs", "publish-agent", 2),
        ("Sanduhr.App/Desk/DockerPipe.cs", "local", 3),
        ("Sanduhr.App/App.xaml.cs", "local", 1),
        ("Sanduhr.App/Interop/LauncherApps.cs", "local", 1),
        ("Sanduhr.App/Interop/ShellFiles.cs", "local", 1),
        ("Sanduhr.App/Services/VaultService.cs", "local", 1),
        ("Sanduhr.App/ViewModels/SettingsViewModel.cs", "local", 1),
        ("Sanduhr.App/ViewModels/ThemesViewModel.cs", "local", 1),
        ("Sanduhr.Core/Startup.cs", "local", 1),
        ("Sanduhr.App/Desk/DeskController.cs", "your-browser", 1),
        ("Sanduhr.App/Desk/DeskReviewBar.xaml.cs", "your-browser", 1),
        ("Sanduhr.App/Interop/LauncherApps.cs", "your-browser", 1),
        ("Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml.cs", "your-browser", 1),
        ("Sanduhr.App/ViewModels/SettingsViewModel.cs", "your-browser", 1),
        ("Sanduhr.App/Views/SettingsWindow.xaml.cs", "your-browser", 1),
    ];

    /// <summary>Lines the scan finds per file: the same inventory counted by line, so an unmarked line can't hide by having no id.</summary>
    private static readonly (string File, int Lines)[] KnownLines =
    [
        ("Sanduhr.App/App.xaml.cs", 1),
        ("Sanduhr.App/Desk/DeskController.cs", 1),
        ("Sanduhr.App/Desk/DeskReviewBar.xaml.cs", 1),
        ("Sanduhr.App/Desk/DockerPipe.cs", 3),
        ("Sanduhr.App/Desk/WeatherService.cs", 3),
        ("Sanduhr.App/Interop/LauncherApps.cs", 2),
        ("Sanduhr.App/Interop/ShellFiles.cs", 1),
        ("Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml.cs", 1),
        ("Sanduhr.App/Services/StoreUpdateService.cs", 3),
        ("Sanduhr.App/Services/VaultService.cs", 1),
        ("Sanduhr.App/Services/WebView2ApiClient.cs", 2),
        ("Sanduhr.App/Services/WidgetCatalogService.cs", 2),
        ("Sanduhr.App/Updates/UpdateChecker.cs", 4),
        ("Sanduhr.App/ViewModels/SettingsViewModel.cs", 2),
        ("Sanduhr.App/ViewModels/ThemesViewModel.cs", 1),
        ("Sanduhr.App/Views/SettingsWindow.xaml.cs", 1),
        ("Sanduhr.App/Views/SignInWindow.xaml.cs", 6),
        ("Sanduhr.Core/ClaudeApiClient.cs", 3),
        ("Sanduhr.Core/Startup.cs", 1),
        ("Sanduhr.Core/UsagePublisher.cs", 2),
    ];

    /// <summary>Lines the scan must find: 25 calls Sanduhr makes, 6 browser hand-offs, 10 local.</summary>
    private const int KnownSites = 41;
    private const int KnownOutboundSites = 25;

    private static HashSet<string> Allowed() =>
        [.. NetworkDisclosure.All.Where(r => r.Caller == NetworkCaller.Sanduhr).Select(r => r.Id), NetworkDisclosure.YourBrowser, NetworkDisclosure.LocalOnly];

    [Fact]
    public void Every_call_site_carries_a_marker()
    {
        var bare = Undisclosed(Scan());
        Assert.True(bare.Count == 0, "Undisclosed outbound calls (add a row to NetworkDisclosure.All and \"// network: <id>\"):\n" + string.Join("\n", bare));
    }

    [Fact]
    public void Every_marker_names_a_row_or_says_your_browser_or_local()
    {
        var allowed = Allowed();
        var bad = Scan().SelectMany(s => s.Ids.Where(id => !allowed.Contains(id)).Select(id => $"{s.File}:{s.Line} names \"{id}\"")).ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void The_inventory_is_what_this_slice_found()
    {
        var sites = Scan();
        Assert.Equal(KnownSites, sites.Count);
        Assert.Equal(KnownOutboundSites,
            sites.Count(s => s.Ids.Any(id => id != NetworkDisclosure.YourBrowser && id != NetworkDisclosure.LocalOnly)));
        var found = sites.SelectMany(s => s.Ids.Select(id => (s.File, Id: id)))
            .GroupBy(x => x).Select(g => (g.Key.File, g.Key.Id, Count: g.Count()))
            .OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var expected = Known.OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, found);
    }

    [Fact]
    public void The_scan_finds_each_file_s_lines()
    {
        var foundLines = Scan().GroupBy(s => s.File).Select(g => (File: g.Key, Lines: g.Count()))
            .OrderBy(x => x.File, StringComparer.Ordinal).ToList();
        var expected = KnownLines.OrderBy(x => x.File, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, foundLines);
        Assert.Equal(KnownSites, KnownLines.Sum(k => k.Lines));
    }

    [Fact]
    public void The_MCP_server_makes_no_call()
    {
        // Network-free by contract (repo CLAUDE.md, Windows gotchas). TrustBoundaryTests scans its linked Core files; this scans its own.
        Assert.DoesNotContain(Scan(), s => s.File.StartsWith("Sanduhr.Mcp/", StringComparison.Ordinal));
        // And the disclosure list is App-side Core: it is not on the MCP server's link list.
        var csproj = File.ReadAllText(Path.Combine(Src(), "Sanduhr.Mcp", "Sanduhr.Mcp.csproj"));
        Assert.DoesNotContain("NetworkDisclosure", csproj);
    }

    [Fact]
    public void Every_row_Sanduhr_makes_has_a_site_and_the_Claude_Code_row_has_none()
    {
        var named = Scan().SelectMany(s => s.Ids).ToHashSet();
        foreach (var row in NetworkDisclosure.All)
        {
            if (row.Caller == NetworkCaller.Sanduhr)
                Assert.True(named.Contains(row.Id), $"{row.Id} has no call site");
            else
                Assert.DoesNotContain(row.Id, named);
        }
        Assert.Contains(NetworkDisclosure.All, r => r.Caller == NetworkCaller.ClaudeCode);
    }

    [Theory]
    [InlineData("var c = new HttpClient(handler) { Timeout = x };", true)]
    [InlineData("resp = await http.SendAsync(req, ct).ConfigureAwait(false);", true)]
    [InlineData("var json = await Http.Value.GetStringAsync(url);", true)]
    [InlineData("WebView.CoreWebView2?.Navigate(ClaudeSignIn.LoginUrl);", true)]
    [InlineData("await core.ExecuteScriptAsync(BuildFetchScript(id, url, headers));", true)]
    [InlineData("var manager = new UpdateManager(source);", true)]
    [InlineData("var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();", true)]
    [InlineData("using var q = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });", true)]
    [InlineData("var ip = Dns.GetHostAddresses(host);", true)]
    [InlineData("using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);", true)]
    // P1: handing a popup to a WebView2 window of our own is an outbound site.
    [InlineData("e.NewWindow = core;", true)]
    [InlineData("if (e.NewWindow == null) return;", false)]
    // P3: the forms a future call would take.
    [InlineData("var r = http.Send(req);", true)]
    [InlineData("var x = await http.GetFromJsonAsync<Foo>(url);", true)]
    [InlineData("await http.PostAsJsonAsync(url, body);", true)]
    [InlineData("var x = await resp.Content.ReadFromJsonAsync<Foo>();", true)]
    [InlineData("core.NavigateWithWebResourceRequest(request);", true)]
    [InlineData("view.Source = new Uri(\"https://claude.ai/\");", true)]
    [InlineData("HttpClient c = new(handler);", true)]
    [InlineData("protected override Task<HttpResponseMessage> SendAsync(", false)]
    [InlineData("private readonly HttpClient _http;", false)]
    [InlineData("_ = _updateChecker?.RunIfDueAsync();", false)]
    [InlineData("var handler = new HttpClientHandler { UseCookies = false };", false)]
    [InlineData("view.Source = source;", false)]
    [InlineData("var s = sender.Sender(x);", false)]
    public void The_call_shapes_match_calls_and_only_calls(string line, bool isCall) =>
        Assert.Equal(isCall, Call.IsMatch(line));

    // The scan can't pass vacuously: a synthetic file with one unmarked call, in each shape, is found and fails the disclosure check.
    [Theory]
    [InlineData("var c = new HttpClient(handler);")]
    [InlineData("var s = await c.GetStringAsync(url);")]
    [InlineData("var r = await c.SendAsync(req);")]
    [InlineData("var r = c.Send(req);")]
    [InlineData("var x = await c.GetFromJsonAsync<Foo>(url);")]
    [InlineData("await c.PostAsJsonAsync(url, body);")]
    [InlineData("core.Navigate(url);")]
    [InlineData("core.NavigateWithWebResourceRequest(request);")]
    [InlineData("view.Source = new Uri(url);")]
    [InlineData("await core.ExecuteScriptAsync(script);")]
    [InlineData("e.NewWindow = core;")]
    [InlineData("HttpClient c = new(handler);")]
    [InlineData("Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });")]
    public void A_new_unmarked_call_in_any_shape_fails_the_disclosure_check(string call)
    {
        var sites = ScanLines("Sanduhr.App/Fake.cs", ["class Fake", "{", "    void M()", "    {", "        " + call, "    }", "}"]);
        var site = Assert.Single(sites);
        Assert.Equal(5, site.Line);
        Assert.Empty(site.Ids);
        Assert.Single(Undisclosed(sites));
    }

    [Fact]
    public void A_marker_covers_its_line_and_the_three_below_it_and_no_further()
    {
        var covered = ScanLines("Sanduhr.App/Fake.cs", ["// network: local", "", "", "core.Navigate(url);"]);
        Assert.Equal(new[] { "local" }, Assert.Single(covered).Ids.ToArray());
        var uncovered = ScanLines("Sanduhr.App/Fake.cs", ["// network: local", "", "", "", "core.Navigate(url);"]);
        Assert.Empty(Assert.Single(uncovered).Ids);
        // A marker on the call's own line counts, and a comment line holding a call shape is not a call.
        var inline = ScanLines("Sanduhr.App/Fake.cs", ["core.Navigate(url); // network: your-browser", "// core.Navigate(url);", "* core.Navigate(url);"]);
        Assert.Equal(new[] { "your-browser" }, Assert.Single(inline).Ids.ToArray());
        // Two ids on one marker are both read.
        var two = ScanLines("Sanduhr.App/Fake.cs", ["// network: publish-usage, publish-agent", "http.SendAsync(req);"]);
        Assert.Equal(new[] { "publish-usage", "publish-agent" }, Assert.Single(two).Ids.ToArray());
    }

    [Fact]
    public void Every_row_says_where_when_what_and_how_to_stop_it()
    {
        Assert.Equal(9, NetworkDisclosure.All.Count);
        Assert.Equal(NetworkDisclosure.All.Count, NetworkDisclosure.All.Select(r => r.Id).Distinct().Count());
        Assert.Equal(NetworkDisclosure.All.Count, NetworkDisclosure.All.Select(r => r.Destination).Distinct().Count());
        foreach (var row in NetworkDisclosure.All)
        {
            foreach (var text in new[] { row.Destination, row.Hosts, row.When, row.Carries, row.Off })
            {
                Assert.False(string.IsNullOrWhiteSpace(text), row.Id);
                Assert.Empty(SettingsNames.RetiredNames(text));
                Assert.Empty(SettingsNames.PathProblems(text));
            }
            // The link lands on a live section, never on one that fetches when opened (decision 17).
            Assert.Equal(new SettingsLink(row.OffPage, row.OffAnchor), SettingsIndex.Check(row.OffPage, row.OffAnchor));
            Assert.False(SettingsIndex.FetchingSections.ContainsKey((row.OffPage, row.OffAnchor)), row.Id);
            Assert.Equal(SettingsNames.LinkTitle(row.OffPage), row.OffLinkTitle);
            Assert.StartsWith(row.OffLinkTitle + ", ", row.OffLinkName);
            Assert.Equal(row.Destination, row.ToString());
        }
    }

    [Fact]
    public void The_rows_name_the_hosts_the_code_asks()
    {
        Assert.Contains("claude.ai", NetworkDisclosure.Find("claude-usage")!.Hosts);
        foreach (var host in WidgetCatalog.AllowedHosts)
            Assert.Contains(host, NetworkDisclosure.Find("widget-catalog")!.Hosts);
        Assert.Contains("geocoding-api.open-meteo.com", NetworkDisclosure.Find("open-meteo")!.Hosts);
        Assert.Contains("api.open-meteo.com", NetworkDisclosure.Find("open-meteo")!.Hosts);
        Assert.Contains("api.github.com", NetworkDisclosure.Find("github-update")!.Hosts);
        Assert.StartsWith("https://", NetworkDisclosure.PrivacyPolicyUrl);
        Assert.EndsWith("/docs/PRIVACY.md", NetworkDisclosure.PrivacyPolicyUrl);
    }

    [Fact]
    public void Hosts_are_set_in_code_type_only_when_they_are_host_names()
    {
        // P10: a row whose Hosts is prose reads in the body font, from SettingsNames. A row of real host names is monospace.
        var host = new Regex(@"^[a-z0-9-]+(\.[a-z0-9-]+)+(, [a-z0-9-]+(\.[a-z0-9-]+)+)*$");
        foreach (var row in NetworkDisclosure.All)
            Assert.Equal(row.HostsAreNames, host.IsMatch(row.Hosts));
        Assert.Equal(new[] { "open-meteo", "widget-catalog" }, NetworkDisclosure.All.Where(r => r.HostsAreNames).Select(r => r.Id).ToArray());
        // P4 and P13: the words, from SettingsNames.
        Assert.Equal(SettingsNames.NetClaudeUsageHosts, NetworkDisclosure.Find("claude-usage")!.Hosts);
        Assert.Equal(SettingsNames.NetSignInHosts, NetworkDisclosure.Find("claude-sign-in")!.Hosts);
        Assert.Equal(SettingsNames.NetUpdatesHosts, NetworkDisclosure.Find("github-update")!.Hosts);
        Assert.Equal(SettingsNames.NetStoreHosts, NetworkDisclosure.Find("store-update")!.Hosts);
        Assert.Equal(SettingsNames.NetPublishHosts, NetworkDisclosure.Find("publish-usage")!.Hosts);
        Assert.Equal(SettingsNames.NetPublishHosts, NetworkDisclosure.Find("publish-agent")!.Hosts);
        Assert.Equal(SettingsNames.NetThroughClaudeHosts, NetworkDisclosure.Find("claude-code")!.Hosts);
    }

    [Fact]
    public void Xaml_opens_no_page_of_its_own()
    {
        var app = Path.Combine(Src(), "Sanduhr.App");
        foreach (var path in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(path);
            Assert.DoesNotMatch(@"Source=""https?://", text);
            // A Hyperlink opens only through the window's https-only handler (the marked SettingsWindow.xaml.cs site).
            foreach (Match m in Regex.Matches(text, @"<Hyperlink\b[^>]*NavigateUri=[^>]*>", RegexOptions.Singleline))
                Assert.Contains("RequestNavigate=\"Hyperlink_RequestNavigate\"", m.Value);
        }
    }
}
