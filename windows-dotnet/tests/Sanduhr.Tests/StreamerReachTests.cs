using System.Text.RegularExpressions;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Settings v2, slice 7: every line in the app that reads an identifying name, a path-shaped value, a project or skill, a folder or text that
/// can carry one carries a "// streamer: &lt;id&gt; - &lt;why&gt;" marker saying what becomes of it, and the inventory is pinned file by file and
/// id by id, so a new reader fails CI until it is masked or marked (the reach inventory, preflight P1 and P8; slice 6's NetworkDisclosureTests
/// is the model). It can't pass vacuously: the counts, a floor and synthetic sources for every shape are pinned.
/// <para>The marker is on the line or within the three lines above (the nearest wins), and it must give a reason a reviewer can check.
/// Ids: masked (names the mask), key (a name used as a key, count, write or comparison, never shown), export (a file the person asked for),
/// demo (demo mode's samples), cant (the caption's can't-reach list), notname (not an identifying value at all).</para>
/// <para>Scope: Sanduhr.App C# only. XAML bindings read the view-model strings these lines produce; the sweep (StreamerSweepTests) walks the
/// rendered pages and their spoken names.</para>
/// </summary>
public class StreamerReachTests
{
    private static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Package.appxmanifest")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    /// <summary>The names themselves: account labels, folder paths and names, projects, skills, runners, add-ons, the PC's name, places,
    /// endpoints.</summary>
    internal const string Names =
        @"\bShownAccounts\b|\bShownActiveAccount\b|\.GetActive\(\)|\bActiveAccount\b|\.ListAccounts\(\)"
        + @"|ClaudeCodeFolders\.Display\s*\(|DetectedRootNames\s*\("
        + @"|\.ProjectName\b|\.BySkill\b|\bdata\.(?:Projects|Skills|Top)\b|\bInfo\.Root\b|\bm\.Root\b"
        + @"|\.Runners?\b|\.Marketplace\b|\bUserServers\b|\bEnabledPlugins\b|Environment\.MachineName"
        + @"|\bPlace\??\.(?:Label|Name)\b|\bEndpointUrl\b";

    /// <summary>Text that can carry a path: any use of a Message member (an exception's text names the file; preflight P1).</summary>
    internal const string Messages = @"\.Message\b";

    /// <summary>Path-shaped values: the user folders, the Claude Code profile, Sanduhr's own folders and files, a file's leaf, a dialog's file name.</summary>
    internal const string Paths =
        @"SpecialFolder\.(?:UserProfile|ApplicationData|LocalApplicationData)|\bClaudeCodeProfile\.(?:Dir|ConfigDir)\b|\b_paths\.\w+"
        + @"|Path\.GetFileName\s*\(|Path\.GetDirectoryName\s*\(|\.FileName\b";

    /// <summary>Projects and skills: a project's working folder, key or display name.</summary>
    internal const string Projects = @"\.Cwd\b|\bProjectKey\b|\bProjectDisplayName\s*\(|\.SkillName\b";

    /// <summary>Folders, in and outside Settings: a Claude Code folder's path, the folder list.</summary>
    internal const string Folders = @"\bf\.Path\b|\.Folders\s*\(\s*\)|\.FolderName\b";

    /// <summary>Spoken names and help text built from something other than a literal or a copy constant.</summary>
    internal const string Spoken =
        @"AutomationProperties\.Set(?:Name|HelpText)\s*\(\s*[\w.]+\s*,(?>\s*)(?!""[^""]*""\s*\)|(?:SettingsNames|\w+Copy|\w+Caption)\.\w+\s*\)|[\w.]+\.Text\s*\))";

    internal static readonly string[] Shapes = [Names, Messages, Paths, Projects, Folders, Spoken];

    internal static readonly Regex Producer = new(string.Join("|", Shapes.Select(s => "(?:" + s + ")")), RegexOptions.Compiled);

    private static readonly Regex Marker = new(@"//\s*streamer:\s*([a-z]+)(?:\s+-\s+(\S.*?))?\s*$", RegexOptions.Compiled);

    internal static readonly string[] Ids = ["masked", "key", "export", "demo", "cant", "notname"];

    internal sealed record Site(string File, int Line, string? Id, string? Reason, string Text);

    internal static List<Site> Scan()
    {
        var src = Src();
        var sites = new List<Site>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(src, "Sanduhr.App"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, path).Replace('\\', '/');
            if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                continue;
            sites.AddRange(ScanLines(rel, File.ReadAllLines(path)));
        }
        return sites;
    }

    /// <summary>The scan of one file's lines. Split out so a synthetic file runs through the same code. A line's own marker is stripped
    /// before the shapes look at it, so a reason that quotes a shape can't create a site.</summary>
    internal static List<Site> ScanLines(string rel, string[] lines)
    {
        var sites = new List<Site>();
        for (int i = 0; i < lines.Length; i++)
        {
            var code = lines[i].TrimStart();
            if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
                continue;
            int at = lines[i].IndexOf("// streamer:", StringComparison.Ordinal);
            var body = at >= 0 ? lines[i][..at] : lines[i];
            if (Producer.IsMatch(body))
            {
                var (id, reason) = MarkerFor(lines, i);
                sites.Add(new Site(rel, i + 1, id, reason, lines[i].Trim()));
            }
        }
        return sites;
    }

    private static (string? Id, string? Reason) MarkerFor(string[] lines, int i)
    {
        for (int j = i; j >= Math.Max(0, i - 3); j--)
        {
            int at = lines[j].IndexOf("// streamer:", StringComparison.Ordinal);
            if (at >= 0 && Marker.Match(lines[j][at..]) is { Success: true } m)
                return (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null);
        }
        return (null, null);
    }

    /// <summary>The unmarked sites, as failure lines. The real scan and the synthetic one both go through this.</summary>
    internal static List<string> Unmarked(IEnumerable<Site> sites) =>
        sites.Where(s => s.Id is null).Select(s => $"{s.File}:{s.Line}  {s.Text}").ToList();

    /// <summary>The inventory this slice pinned, file by file and id by id, from the scan's own output (Task 13). A site whose marker is
    /// shared by neighbouring lines counts once per line.</summary>
    private static readonly (string File, string Id, int Count)[] Known =
    [
        ("Sanduhr.App/App.xaml.cs", "key", 7),
        ("Sanduhr.App/App.xaml.cs", "masked", 2),
        ("Sanduhr.App/App.xaml.cs", "notname", 4),
        ("Sanduhr.App/Desk/DeskArrangeMode.cs", "notname", 3),
        ("Sanduhr.App/Desk/DeskClicks.cs", "notname", 4),
        ("Sanduhr.App/Desk/DeskCompanion.cs", "key", 1),
        ("Sanduhr.App/Desk/DeskCompanion.cs", "notname", 2),
        ("Sanduhr.App/Desk/DeskController.cs", "notname", 1),
        ("Sanduhr.App/Desk/DeskDesignService.cs", "notname", 8),
        ("Sanduhr.App/Desk/DeskHandoffSeams.cs", "notname", 4),
        ("Sanduhr.App/Desk/DeskLauncherPiece.cs", "notname", 2),   // controller, after Task 13: the Desk names an unlabelled link by codename
        ("Sanduhr.App/Desk/DeskLookResolver.cs", "notname", 3),
        ("Sanduhr.App/Desk/DeskMessagePiece.cs", "notname", 3),
        ("Sanduhr.App/Desk/DeskReviewBar.xaml.cs", "notname", 12),
        ("Sanduhr.App/Desk/DeskReviewWindow.xaml.cs", "notname", 1),
        ("Sanduhr.App/Desk/DeskView.Styles.cs", "notname", 1),
        ("Sanduhr.App/Desk/DeskView.cs", "cant", 1),
        ("Sanduhr.App/Desk/DeskView.cs", "notname", 1),
        ("Sanduhr.App/Desk/Drawers/DrawerJournalStore.cs", "notname", 1),
        ("Sanduhr.App/Desk/Drawers/DrawerPlanEditWindow.xaml.cs", "masked", 2),
        ("Sanduhr.App/Desk/Drawers/DrawerMover.cs", "key", 12),
        ("Sanduhr.App/Desk/Drawers/DrawerPiece.xaml.cs", "notname", 1),
        ("Sanduhr.App/Desk/Drawers/DrawerService.cs", "key", 4),
        ("Sanduhr.App/Desk/Drawers/DrawerService.cs", "notname", 1),
        ("Sanduhr.App/Desk/Drawers/DrawerState.cs", "key", 1),
        ("Sanduhr.App/Desk/NowPlayingLookService.cs", "notname", 1),
        ("Sanduhr.App/Desk/RunnerWatchService.cs", "key", 1),
        ("Sanduhr.App/Desk/WallpaperPreviewFile.cs", "notname", 1),
        ("Sanduhr.App/Desk/WidgetReviewService.cs", "notname", 4),
        ("Sanduhr.App/Interop/DeskSensorsNative.cs", "notname", 1),
        ("Sanduhr.App/Services/AccountMenuBuilder.cs", "masked", 2),
        ("Sanduhr.App/Services/AlertService.cs", "notname", 1),
        ("Sanduhr.App/Services/ClaudeCodeGlowService.cs", "notname", 2),
        ("Sanduhr.App/Services/ClaudeCodeProfile.cs", "notname", 1),
        ("Sanduhr.App/Services/DataFolders.cs", "notname", 1),
        ("Sanduhr.App/Services/DesignStore.cs", "notname", 2),
        ("Sanduhr.App/Services/ExternalDirMigrationRunner.cs", "key", 3),
        ("Sanduhr.App/Services/InactiveUsageReader.cs", "notname", 2),
        ("Sanduhr.App/Services/LauncherImages.cs", "notname", 1),
        ("Sanduhr.App/Services/ResetService.cs", "key", 1),
        ("Sanduhr.App/Services/ResetService.cs", "notname", 5),   // slice 8: the per-kind keep reads a leaf
        ("Sanduhr.App/Services/SignInCoordinator.cs", "key", 7),
        ("Sanduhr.App/Services/SignInCoordinator.cs", "masked", 2),
        ("Sanduhr.App/Services/SingleInstance.cs", "notname", 2),
        ("Sanduhr.App/Services/StoreUpdateService.cs", "notname", 1),
        ("Sanduhr.App/Services/StreamerMode.cs", "masked", 1),
        ("Sanduhr.App/Services/StreamerService.cs", "key", 4),
        ("Sanduhr.App/Services/StreamerService.cs", "notname", 6),
        ("Sanduhr.App/Services/ThemeHandoffService.cs", "notname", 7),
        ("Sanduhr.App/Services/ThemeMatchService.cs", "notname", 2),
        ("Sanduhr.App/Services/UsagePublishService.cs", "cant", 2),
        ("Sanduhr.App/Services/UsagePublishService.cs", "export", 1),
        ("Sanduhr.App/Services/UsagePublishService.cs", "key", 3),
        ("Sanduhr.App/Services/UsagePublishService.cs", "notname", 6),
        ("Sanduhr.App/Services/VaultService.cs", "cant", 1),
        ("Sanduhr.App/Services/VaultService.cs", "key", 4),
        ("Sanduhr.App/Services/VaultService.cs", "notname", 6),
        ("Sanduhr.App/Services/WebView2ApiClient.cs", "notname", 1),
        ("Sanduhr.App/Updates/UpdateChecker.cs", "notname", 3),
        ("Sanduhr.App/ViewModels/AccountsViewModel.cs", "masked", 4),
        ("Sanduhr.App/ViewModels/CcLedgerViewModel.cs", "export", 5),
        ("Sanduhr.App/ViewModels/CcLedgerViewModel.cs", "key", 16),
        ("Sanduhr.App/ViewModels/CcLedgerViewModel.cs", "masked", 6),
        ("Sanduhr.App/ViewModels/CcTrendsViewModel.cs", "masked", 1),
        ("Sanduhr.App/ViewModels/ClaudeCodeHost.cs", "key", 2),
        ("Sanduhr.App/ViewModels/ClaudeCodeHost.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs", "key", 16),   // slice 8: the undo moved to Core (ClaudeCodeReset)
        ("Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs", "masked", 5),
        ("Sanduhr.App/ViewModels/DesignsSettingsViewModel.cs", "cant", 1),
        ("Sanduhr.App/ViewModels/DeskCompanionViewModel.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/DeskSettingsViewModel.cs", "notname", 2),
        ("Sanduhr.App/ViewModels/FolderHoldsViewModel.cs", "key", 3),
        ("Sanduhr.App/ViewModels/FolderHoldsViewModel.cs", "masked", 3),
        ("Sanduhr.App/ViewModels/HistoryTabViewModel.cs", "export", 1),
        ("Sanduhr.App/ViewModels/HistoryTabViewModel.cs", "key", 2),
        ("Sanduhr.App/ViewModels/HistoryTabViewModel.cs", "masked", 4),
        ("Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs", "key", 1),
        ("Sanduhr.App/ViewModels/LauncherSettingsViewModel.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/LocalCcViewModel.cs", "key", 6),
        ("Sanduhr.App/ViewModels/LocalCcViewModel.cs", "masked", 6),   // streamer smoke: archive folder names redraw on the switch
        ("Sanduhr.App/ViewModels/ModsPageViewModel.cs", "key", 1),
        ("Sanduhr.App/ViewModels/ModsPageViewModel.cs", "masked", 5),
        ("Sanduhr.App/ViewModels/ModsPageViewModel.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/PrivacyViewModel.cs", "key", 1),
        ("Sanduhr.App/ViewModels/PrivacyViewModel.cs", "masked", 1),
        ("Sanduhr.App/ViewModels/PublishViewModel.cs", "key", 5),
        ("Sanduhr.App/ViewModels/PublishViewModel.cs", "masked", 4),
        ("Sanduhr.App/ViewModels/ResetFolderListViewModel.cs", "key", 4),      // slice 8: Reset's rows key on real folders
        ("Sanduhr.App/ViewModels/ResetFolderListViewModel.cs", "masked", 1),   // slice 8: and draw them through ShowFolder (carry)
        ("Sanduhr.App/ViewModels/StreamerSettingsViewModel.cs", "key", 1),
        ("Sanduhr.App/ViewModels/ThemeDeskEditorViewModel.cs", "notname", 3),
        ("Sanduhr.App/ViewModels/ThemeMatchViewModel.cs", "cant", 4),
        ("Sanduhr.App/ViewModels/ThemeStudioViewModel.cs", "masked", 1),
        ("Sanduhr.App/ViewModels/ThemeStudioViewModel.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/ThemesViewModel.cs", "cant", 1),
        ("Sanduhr.App/ViewModels/ThemesViewModel.cs", "masked", 3),
        ("Sanduhr.App/ViewModels/ThemesViewModel.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/TierCardViewModel.cs", "notname", 1),
        ("Sanduhr.App/ViewModels/WelcomeTourViewModel.cs", "masked", 1),
        ("Sanduhr.App/ViewModels/WidgetViewModel.cs", "cant", 4),
        ("Sanduhr.App/ViewModels/WidgetViewModel.cs", "key", 18),
        ("Sanduhr.App/ViewModels/WidgetViewModel.cs", "masked", 3),
        ("Sanduhr.App/ViewModels/WidgetViewModel.cs", "notname", 18),
        ("Sanduhr.App/Views/DeskLayoutMap.cs", "notname", 1),
        ("Sanduhr.App/Views/IntegrationConsentDialog.xaml.cs", "notname", 1),
        ("Sanduhr.App/Views/ManualKeyWindow.xaml.cs", "masked", 1),
        ("Sanduhr.App/Views/McpConsentDialog.xaml.cs", "masked", 3),
        ("Sanduhr.App/Views/ResetWindow.xaml.cs", "key", 1),      // slice 8: the real paths of the unticked rows
        ("Sanduhr.App/Views/SectionJumpLinks.cs", "notname", 1),
        ("Sanduhr.App/Views/SignInWindow.xaml.cs", "masked", 3),
        ("Sanduhr.App/Views/SignInWindow.xaml.cs", "notname", 1),
        ("Sanduhr.App/Views/VaultConsentDialog.xaml.cs", "masked", 1),
    ];

    /// <summary>The floor: fewer lines than this means the shapes stopped seeing the code (the count this inventory pinned, re-derived by an independent pass: 328 lines in 72 files, the pinned total).</summary>
    private const int Floor = 328;

    [Fact]
    public void Every_reader_of_a_name_carries_a_marker()
    {
        var bare = Unmarked(Scan());
        Assert.True(bare.Count == 0,
            "Unmarked readers of a name, path, project, folder or error text (mask it, or mark it \"// streamer: key|export|demo|cant|notname - why\"):\n"
            + string.Join("\n", bare));
    }

    [Fact]
    public void Every_marker_is_one_of_the_six_and_says_why()
    {
        var bad = new List<string>();
        foreach (var s in Scan().Where(s => s.Id is not null))
        {
            if (!Ids.Contains(s.Id!)) bad.Add($"{s.File}:{s.Line} says \"{s.Id}\"");
            else if (s.Reason is null || s.Reason.Length < 8) bad.Add($"{s.File}:{s.Line} marked {s.Id} with no reason (\"// streamer: {s.Id} - why\")");
            else if (s.Id == "masked" && !Regex.IsMatch(s.Reason, @"CP\s?\d+|[Mm]ask|StreamerMode"))
                bad.Add($"{s.File}:{s.Line} marked masked but the reason doesn't name the choke point or mask: {s.Reason}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>The mask calls that count: a call of one of the mask's own methods or a folder helper. A mention of StreamerMode (On, Mask.Hides)
    /// masks nothing, so it doesn't count (final review M4).</summary>
    private static readonly Regex MaskCall = new(
        @"Mask\.(?:Name|Folder|FolderName|AddOn|Paths|Endpoint|HostsIn|Watchers|WeatherName|PublishSample|Omit)\(|StreamerMode\.FoldersIn\(|ShowFolder\(|LauncherName\(",
        RegexOptions.Compiled);

    /// <summary>How far a mask call may stand from a marked line: the line and the twelve below it (a local read is masked before it is drawn).</summary>
    private const int MaskWindow = 13;

    /// <summary>A "via X" hand-off may call X a loop away (a menu's rows are built after its labels are read): twenty lines.</summary>
    private const int ViaWindow = 20;

    /// <summary>Choke points that mask inside, for a site that hands its value on ("via Symbol" in the marker): the file that defines each
    /// and the mask call it must make. A site can name only a symbol listed here, and the listing is checked against the source.</summary>
    private static readonly (string Symbol, string File, string Mask)[] Callees =
    [
        ("Header", "Sanduhr.App/Services/AccountMenuBuilder.cs", @"Mask\.Name\("),
        ("AccountItemViewModel", "Sanduhr.App/ViewModels/AccountsViewModel.cs", @"Mask\.Name\("),
        ("ProjectShown", "Sanduhr.App/ViewModels/CcLedgerViewModel.cs", @"Mask\.Name\(CodenameCategory\.Project"),
        ("FillTop", "Sanduhr.App/ViewModels/CcTrendsViewModel.cs", @"Mask\.Name\(|Rows\("),
        ("Shorten", "Sanduhr.App/ViewModels/ClaudeCodeViewModel.cs", @"StreamerMode\.FoldersIn\("),
        ("Short", "Sanduhr.App/ViewModels/ModsPageViewModel.cs", @"StreamerMode\.FoldersIn\("),
        ("Display", "Sanduhr.App/ViewModels/ClaudeCodeHost.cs", @"StreamerMode\.ShowFolder\("),
        ("FillBreakdowns", "Sanduhr.App/ViewModels/LocalCcViewModel.cs", @"Mask\.Name\(category"),
        ("VaultRootToggleViewModel", "Sanduhr.App/ViewModels/LocalCcViewModel.cs", @"Mask\.FolderName\("),
        ("PublishRootToggleViewModel", "Sanduhr.App/ViewModels/PublishViewModel.cs", @"Mask\.FolderName\("),
        ("ShowConsent", "Sanduhr.App/Views/VaultConsentDialog.xaml.cs", @"Mask\.FolderName\("),
        ("DetailsFor", "Sanduhr.App/Views/McpConsentDialog.xaml.cs", @"StreamerMode\.FoldersIn\("),
        ("FailedText", "Sanduhr.App/Services/SignInCoordinator.cs", @"Mask\.Paths\("),
        ("LoadErrorText", "Sanduhr.App/Views/SignInWindow.xaml.cs", @"Mask\.Paths\("),
        ("ShowError", "Sanduhr.App/Views/ManualKeyWindow.xaml.cs", @"Mask\.Paths\("),
        ("ExportedMessage", "Sanduhr.App/ViewModels/HistoryTabViewModel.cs", @"Mask\.Paths\("),
        ("ExportFailedMessage", "Sanduhr.App/ViewModels/HistoryTabViewModel.cs", @"Mask\.Paths\("),
        ("Error", "Sanduhr.App/ViewModels/ThemesViewModel.cs", @"void Error\(string message\)[\s\S]{0,400}Mask\.Paths\("),
        ("LauncherName", "Sanduhr.App/Services/StreamerMode.cs", @"Mask\.Name\(CodenameCategory\.Host"),
    ];

    /// <summary>Whether a line marked masked really masks: a mask call on the line or within the twelve below it, or, for "via Symbol", a listed
    /// choke point that the line or the nineteen below call.</summary>
    private static bool MaskedReallyMasks(IReadOnlyList<string> lines, int lineNumber, string? reason, out string why)
    {
        why = "";
        var via = Regex.Match(reason ?? "", @"via (\w+)");
        var window = string.Join("\n", lines.Skip(lineNumber - 1).Take(via.Success ? ViaWindow : MaskWindow));
        if (via.Success)
        {
            // A site that hands its value to a choke point must call it in its window, and the choke point must be a listed one.
            var symbol = via.Groups[1].Value;
            if (Callees.Any(c => c.Symbol == symbol) && Regex.IsMatch(window, @"\b" + Regex.Escape(symbol) + @"\s*\(")) return true;
            why = $"via {symbol}, but the line and the {ViaWindow - 1} below don't call it (or it isn't listed)";
            return false;
        }
        if (MaskCall.IsMatch(window)) return true;
        why = "no mask call on the line or the twelve below";
        return false;
    }

    [Fact]
    public void A_masked_line_really_calls_the_mask()
    {
        var src = Src();
        var bad = new List<string>();
        foreach (var site in Scan().Where(s => s.Id == "masked"))
        {
            var lines = File.ReadAllLines(Path.Combine(src, site.File));
            if (!MaskedReallyMasks(lines, site.Line, site.Reason, out var why))
                bad.Add($"{site.File}:{site.Line}  {why}: {site.Text}");
        }
        Assert.True(bad.Count == 0, "Marked masked, but it doesn't call the mask (Mask.<Method>(, StreamerMode.FoldersIn(, ShowFolder(, LauncherName():\n" + string.Join("\n", bad));
    }

    /// <summary>The check can't pass on a mention (final review M4): StreamerMode.On and Mask.Hides mask nothing, and a via site must call its symbol.</summary>
    [Theory]
    [InlineData("CP 1: StreamerMode.On is checked", "if (StreamerMode.On) return;", false)]
    [InlineData("CP 1: Mask.Hides is asked", "var hidden = StreamerMode.Mask.Hides(CodenameCategory.Project, x);", false)]
    [InlineData("CP 1: the name passes Mask.Name", "Text = StreamerMode.Mask.Name(CodenameCategory.Project, x);", true)]
    [InlineData("CP 1: the folder passes ShowFolder", "Text = StreamerMode.ShowFolder(path, profile);", true)]
    [InlineData("via Shorten: the text", "var y = 1;", false)]
    [InlineData("via Shorten: the text", "Status = Shorten(x);", true)]
    [InlineData("via NotListed: the text", "Status = NotListed(x);", false)]
    public void The_masked_check_needs_a_real_mask_call(string reason, string next, bool expected) =>
        Assert.Equal(expected, MaskedReallyMasks(["var x = info.ProjectName;", next], 1, reason, out _));

    [Fact]
    public void Every_listed_choke_point_exists_and_masks()
    {
        var src = Src();
        var bad = new List<string>();
        foreach (var (symbol, file, mask) in Callees)
        {
            var path = Path.Combine(src, file);
            if (!File.Exists(path)) { bad.Add($"{symbol}: {file} is gone"); continue; }
            var text = File.ReadAllText(path);
            if (!Regex.IsMatch(text, @"\b" + Regex.Escape(symbol) + @"\b")) bad.Add($"{symbol}: not found in {file}");
            else if (!Regex.IsMatch(text, mask)) bad.Add($"{symbol}: {file} no longer makes the mask call {mask}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void The_inventory_is_what_this_slice_found()
    {
        var sites = Scan();
        Assert.True(sites.Count >= Floor, $"the scan found {sites.Count} lines, under the floor of {Floor}: the shapes stopped matching");
        var found = sites.Where(s => s.Id is not null).GroupBy(s => (s.File, s.Id!)).Select(g => (g.Key.File, g.Key.Item2, Count: g.Count()))
            .OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Item2, StringComparer.Ordinal).ToList();
        var expected = Known.OrderBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        Assert.True(expected.SequenceEqual(found),
            $"Pin this inventory ({sites.Count} lines; or explain the change):\n" + string.Join("\n", found.Select(f => $"        (\"{f.File}\", \"{f.Item2}\", {f.Count}),")));
    }

    [Fact]
    public void The_pinned_inventory_adds_up_to_the_total_and_no_id_is_empty()
    {
        int total = Known.Sum(k => k.Count);
        Assert.True(total >= Floor, $"the pinned counts add to {total}, under the floor of {Floor}");
        // demo is the one id with no scanned site: demo mode's sample labels are read through DemoMode, whose lines match no shape.
        foreach (var id in Ids.Where(i => i != "demo"))
            Assert.True(Known.Any(k => k.Id == id), $"no site is pinned as \"{id}\": the marker would never be exercised");
    }

    // ---- the shapes, one synthetic source each (preflight P1 and P8) ----

    [Theory]
    // names
    [InlineData("var a = _widget.ShownAccounts;", true)]
    [InlineData("foreach (var root in vault.DetectedRootNames())", true)]
    [InlineData("Text = info.ProjectName;", true)]
    [InlineData("var who = Environment.MachineName;", true)]
    [InlineData("var s = Place?.Label;", true)]
    [InlineData("var n = row.Marketplace;", true)]
    [InlineData("var u = settings.EndpointUrl;", true)]
    // error text that reaches the screen
    [InlineData("Error($\"Could not write theme: {e.Message}\");", true)]
    [InlineData("Status = \"Failed: \" + ex.Message;", true)]
    [InlineData("ShowError(ex.Message);", true)]
    [InlineData("Status = result.Message;", true)]
    // path-shaped values
    [InlineData("var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);", true)]
    [InlineData("var dir = ClaudeCodeProfile.Dir;", true)]
    [InlineData("var f = Path.Combine(_paths.ThemesDir, \"x.json\");", true)]
    [InlineData("var leaf = Path.GetFileName(path);", true)]
    [InlineData("Info(dlg.FileName);", true)]
    // projects and skills
    [InlineData("var p = session.Cwd;", true)]
    [InlineData("var k = info.ProjectKey;", true)]
    [InlineData("var d = CcLogReader.ProjectDisplayName(cwd);", true)]
    [InlineData("foreach (var (skill, v) in window.BySkill)", true)]
    [InlineData("var s = use.SkillName;", true)]
    // folders outside Settings
    [InlineData("var leaf = Path.GetFileName(f.Path);", true)]
    [InlineData("foreach (var f in _host.Folders())", true)]
    // spoken names and help text with a name in them
    [InlineData("AutomationProperties.SetName(this, $\"{label}, folder\");", true)]
    [InlineData("AutomationProperties.SetHelpText(row, folder.Display);", true)]
    [InlineData("AutomationProperties.SetName(button, accessibleName);", true)]
    // not matched
    [InlineData("AutomationProperties.SetName(this, \"Close\");", false)]
    [InlineData("AutomationProperties.SetName(this, SettingsNames.DeskCompanion);", false)]
    [InlineData("AutomationProperties.SetName(Heading, Heading.Text);", false)]
    [InlineData("var x = 1 + 2;", false)]
    [InlineData("// _widget.ShownAccounts in a comment", false)]
    [InlineData("/// the error text e.Message on a doc line", false)]
    public void The_shapes_see_what_they_should(string line, bool expected) =>
        Assert.Equal(expected, ScanLines("x.cs", [line]).Count == 1);

    [Fact]
    public void Every_shape_matches_something_in_the_real_source()
    {
        var src = Src();
        var lines = Directory.EnumerateFiles(Path.Combine(src, "Sanduhr.App"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(File.ReadAllLines).ToList();
        foreach (var shape in Shapes)
        {
            var re = new Regex(shape);
            Assert.True(lines.Any(re.IsMatch), "a shape matches nothing in src, so it can no longer see the code: " + shape);
        }
    }

    // ---- the marker rule ----

    [Fact]
    public void An_unmarked_fake_site_fails_the_scan()
    {
        string[] file =
        [
            "void Draw()",
            "{",
            "    Title.Text = _widget.ShownActiveAccount;",
            "    Error($\"Could not save: {ex.Message}\");",
            "    var leaf = Path.GetFileName(f.Path);",
            "}",
        ];
        var bare = Unmarked(ScanLines("Fake.cs", file));
        Assert.Equal(3, bare.Count);
        Assert.Contains(bare, b => b.StartsWith("Fake.cs:3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_marked_fake_site_passes_and_the_nearest_marker_wins()
    {
        string[] file =
        [
            "// streamer: key - a label compared, never shown",
            "var same = _accounts.GetActive() == label;",
            "var shown = _widget.ShownActiveAccount;   // streamer: masked - Mask.Name below",
        ];
        var sites = ScanLines("Fake.cs", file);
        Assert.Empty(Unmarked(sites));
        Assert.Equal("key", sites[0].Id);
        Assert.Equal("masked", sites[1].Id);
        Assert.Equal("a label compared, never shown", sites[0].Reason);
    }

    [Fact]
    public void A_marker_above_counts_and_one_too_far_does_not()
    {
        string[] near = ["// streamer: key - near enough", "", "", "var a = _widget.ShownAccounts;"];
        string[] far = ["// streamer: key - too far", "", "", "", "var a = _widget.ShownAccounts;"];
        Assert.Equal("key", ScanLines("x.cs", near).Single().Id);
        Assert.Null(ScanLines("x.cs", far).Single().Id);
    }

    [Fact]
    public void A_reason_that_quotes_a_shape_makes_no_site_of_its_own()
    {
        string[] file = ["var n = 1;   // streamer: notname - not e.Message, not ShownAccounts"];
        Assert.Empty(ScanLines("x.cs", file));
    }

    [Fact]
    public void A_marker_without_a_reason_is_seen_as_one_and_has_none()
    {
        var site = ScanLines("x.cs", ["var a = _widget.ShownAccounts;   // streamer: key"]).Single();
        Assert.Equal("key", site.Id);
        Assert.Null(site.Reason);
    }

    [Fact]
    public void The_MCP_server_is_not_scanned_and_the_mask_is_not_linked()
    {
        Assert.DoesNotContain(Scan(), s => s.File.StartsWith("Sanduhr.Mcp/", StringComparison.Ordinal));
        var csproj = File.ReadAllText(Path.Combine(Src(), "Sanduhr.Mcp", "Sanduhr.Mcp.csproj"));
        Assert.DoesNotContain("StreamerMask", csproj);
        Assert.DoesNotContain("Codenames", csproj);
        Assert.DoesNotContain("StreamerFile", csproj);
    }

    /// <summary>Decision 2: the alert toasts carry a tier label and the update toast a version, so neither reads a name. The scan finds one
    /// site in the alert service, a log-file write marked "notname", and none in the restart-to-update service; a name reaching either toast
    /// would be a new site here, unmasked or marked otherwise.</summary>
    [Fact]
    public void Toasts_read_no_name()
    {
        var sites = Scan().Where(s => s.File.EndsWith("AlertService.cs", StringComparison.Ordinal)
                                      || s.File.EndsWith("RestartToUpdateService.cs", StringComparison.Ordinal)).ToList();
        Assert.All(sites, s =>
        {
            Assert.Equal("notname", s.Id);
            Assert.Contains("never drawn", s.Reason ?? "");
        });
        Assert.DoesNotContain(sites, s => s.File.EndsWith("RestartToUpdateService.cs", StringComparison.Ordinal));
    }
}
