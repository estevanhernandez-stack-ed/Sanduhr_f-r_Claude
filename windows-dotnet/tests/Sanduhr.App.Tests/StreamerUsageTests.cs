using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Claude Usage (spec, Reach inside Settings, Claude Usage; CPs 3, 4, 11, 15, 16, 17). Exports keep real
/// names; what shows on screen does not. The Task 15 sweep cannot fill the session archive, so these builder-level tests are the guard for
/// projects, skills and folders on Overview, Trends and Sessions: every row, spoken name, tooltip and detail carries a "qx" sentinel.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerUsageTests : AppWindowFixture
{
    private static void On(string profile) => StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), profile));

    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    private static VaultSessionInfo Session(string project = "qxproject", string projectKey = "C--Users-qxuser-qxproject", string root = ".claude-qxwork",
        string? cwd = null, string uuid = "u1", string[]? skills = null) =>
        new(uuid, root, projectKey, project, cwd,
            DateTimeOffset.Parse("2026-10-20T10:00:00+00:00"), DateTimeOffset.Parse("2026-10-20T11:00:00+00:00"),
            600, new Dictionary<string, long> { ["claude-fable-5"] = 600 },
            skills?.ToDictionary(s => s, _ => 50L),
            new Dictionary<string, VaultDayBucket> { ["2026-10-20"] = new() { Total = 600 } },
            null, 0, 0);

    [Fact]
    public void The_meters_picker_and_legend_name_accounts_by_codename_and_keep_the_real_label_as_the_value() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            widget.Streamer.SetOn(true);
            var history = new HistoryTabViewModel(widget);
            history.RefreshNames();
            foreach (var o in history.AccountOptions)
            {
                NoSentinel("option", o.Display);
                NoSentinel("option spoken", o.ToString());
            }
            Assert.Contains(history.AccountOptions, o => o.Account == "Qxalpha");
            history.SelectedOption = history.AccountOptions[0];   // All accounts: the legend lists every account
            Assert.NotEmpty(history.Legend);
            Assert.All(history.Legend, l => NoSentinel("legend", l.Label + l.ToString()));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_selected_account_survives_the_switch_and_its_real_label_stays_the_value() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        try
        {
            var history = new HistoryTabViewModel(widget);
            history.SelectedOption = history.AccountOptions.First(o => o.Account == "Qxbeta");
            Assert.Equal("Qxbeta", history.SelectedOption.Display);
            widget.Streamer.SetOn(true);
            history.RefreshNames();
            Assert.Equal("Qxbeta", history.SelectedOption!.Account);
            NoSentinel("selected", history.SelectedOption.Display);
            widget.Streamer.SetOn(false);
            history.RefreshNames();
            Assert.Equal("Qxbeta", history.SelectedOption!.Display);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void An_exports_file_name_and_its_result_show_no_real_name() => AppHost.Run(() =>
    {
        On(Profile);
        NoSentinel("file name", HistoryTabViewModel.ExportFileName("Qxalpha", new DateTime(2026, 10, 26)));
        Assert.StartsWith("Sanduhr-usage-", HistoryTabViewModel.ExportFileName("Qxalpha", new DateTime(2026, 10, 26)));
        Assert.Equal("Sanduhr-usage-all-accounts-2026-10-26.csv", HistoryTabViewModel.ExportFileName(null, new DateTime(2026, 10, 26)));
        var message = HistoryTabViewModel.ExportedMessage(12, $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\Documents\x.csv");
        Assert.DoesNotContain(Environment.UserName, message, StringComparison.OrdinalIgnoreCase);
        var failed = HistoryTabViewModel.ExportFailedMessage($@"{Profile}\Documents\Qxalpha.csv",
            $@"Access to the path '{Profile}\Documents\Qxalpha.csv' is denied.");
        Assert.DoesNotContain(Profile, failed, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void An_exports_file_name_is_the_old_one_with_streamer_mode_off() => AppHost.Run(() =>
    {
        Assert.Equal("Sanduhr-usage-qx-alpha-2026-10-26.csv", HistoryTabViewModel.ExportFileName("Qx Alpha", new DateTime(2026, 10, 26)));
        Assert.Equal($"Wrote 3 rows to:\n{Profile}\\a.csv", HistoryTabViewModel.ExportedMessage(3, $@"{Profile}\a.csv"));
    });

    [Fact]
    public void Exports_say_they_keep_real_names_only_while_streamer_mode_is_on() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            var history = new HistoryTabViewModel(widget);
            Assert.False(history.ExportsKeepRealNamesShown);
            widget.Streamer.SetOn(true);
            history.RefreshNames();
            Assert.True(history.ExportsKeepRealNamesShown);
            var ledger = new CcLedgerViewModel(widget, false, _ => { });
            Assert.True(ledger.ExportsKeepRealNamesShown);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Overview_and_trends_rows_are_codenames_and_remember_their_real_names() => AppHost.Run(() =>
    {
        On(Profile);
        var rows = LocalCcViewModel.Rows([("qxproject", 1200L), ("qxother", 900L)], CodenameCategory.Project);
        Assert.All(rows, r => NoSentinel("row", r.Name + r.ToString()));
        Assert.Equal(["qxproject", "qxother"], rows.Select(r => r.Real));
        Assert.All(rows, r => Assert.True(r.CanShowRealName));
        var skills = LocalCcViewModel.Rows([("qxskill", 10L)], CodenameCategory.Skill);
        Assert.Equal(StreamerMode.Mask.Name(CodenameCategory.Skill, "qxskill"), skills[0].Name);
        // Overview's and Trends' top projects are one name in two places: one codename.
        Assert.Equal(rows[0].Name, CcTrendsViewModel.Rows([("qxproject", 5L)])[0].Name);
    });

    [Fact]
    public void A_row_off_streamer_mode_reads_as_before_and_offers_no_quick_route() => AppHost.Run(() =>
    {
        var row = LocalCcViewModel.Rows([("qxproject", 1200L)], CodenameCategory.Project)[0];
        Assert.Equal("qxproject", row.Name);
        Assert.Equal("qxproject, 1.2k", row.ToString());
        Assert.False(row.CanShowRealName);
    });

    [Fact]
    public void Overview_tables_mask_every_row_and_follow_the_switch_without_a_new_read() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var overview = new LocalCcViewModel(widget, true, _ => { });
            overview.ShowTopForTests([("qxproject", 5000L), ("qxsecond", 400L)], [("qxskill", 900L), ("qxother-skill", 30L)]);
            Assert.Equal("qxproject", overview.Projects[0].Name);
            widget.Streamer.SetOn(true);
            overview.RefreshNames();
            Assert.Equal(2, overview.Projects.Count);
            Assert.Equal(2, overview.Skills.Count);
            foreach (var r in overview.Projects.Concat(overview.Skills))
            {
                NoSentinel("name", r.Name);
                NoSentinel("spoken", r.ToString());
                Assert.True(r.CanShowRealName);
            }
            Assert.Equal(CodenameCategory.Project, overview.Projects[0].Category);
            Assert.Equal(CodenameCategory.Skill, overview.Skills[0].Category);
            // A project and a skill that share a name are two things: two codenames.
            overview.ShowTopForTests([("qxsame", 1L)], [("qxsame", 1L)]);
            overview.RefreshNames();
            Assert.NotEqual(overview.Projects[0].Name, overview.Skills[0].Name);
            widget.Streamer.SetOn(false);
            overview.RefreshNames();
            Assert.Equal("qxsame", overview.Projects[0].Name);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Trends_top_projects_mask_every_row_and_share_overviews_codenames() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.Streamer.SetOn(true);
            var overview = new LocalCcViewModel(widget, true, _ => { });
            var trends = new CcTrendsViewModel(widget);
            overview.ShowTopForTests([("qxproject", 5000L)], []);
            trends.ShowTopForTests([("qxproject", 7000L), ("qxsecond", 100L)]);
            Assert.Equal(2, trends.TopProjects.Count);
            foreach (var r in trends.TopProjects)
            {
                NoSentinel("name", r.Name);
                NoSentinel("spoken", r.ToString());
                Assert.True(r.CanShowRealName);
            }
            Assert.Equal(overview.Projects[0].Name, trends.TopProjects[0].Name);
            widget.Streamer.SetOn(false);
            trends.RefreshNames();
            Assert.Equal("qxproject", trends.TopProjects[0].Name);
            trends.Detach();
            overview.Detach();
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_large_archive_assigns_codenames_only_for_the_rows_shown() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.Streamer.SetOn(true);
            var overview = new LocalCcViewModel(widget, true, _ => { });
            // Hundreds of projects in the archive; the table keeps ten, and only ten names are ever looked up.
            var assigned = new List<string>();
            StreamerMode.Mask.Assigned += c => assigned.Add(c.ToString());
            var top = Enumerable.Range(0, 10).Select(i => ($"qxproject{i}", 1000L - i)).ToList();
            overview.ShowTopForTests(top, []);
            Assert.Equal(10, assigned.Count);
            assigned.Clear();
            overview.RefreshNames();   // a second pass is dictionary hits, nothing new assigned
            Assert.Empty(assigned);
            overview.Detach();
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_session_row_names_its_project_skills_and_folder_by_codename_and_drops_the_full_path() => AppHost.Run(() =>
    {
        On(Profile);
        var info = Session(cwd: $@"{Profile}\qxproject", skills: ["qxskill"]);
        var row = new LedgerRowViewModel("k", info, DateOnly.MinValue, DateOnly.MaxValue, disambiguate: false);
        row.IsExpanded = true;
        NoSentinel("project", row.ProjectText);
        NoSentinel("spoken", row.ToString());
        NoSentinel("detail", row.DetailText);
        Assert.Contains("Skills:", row.DetailText);
        Assert.Contains("Folder:", row.DetailText);
        Assert.Null(row.CwdTooltip);
        var twin = new LedgerRowViewModel("k2", info, DateOnly.MinValue, DateOnly.MaxValue, disambiguate: true);
        NoSentinel("disambiguated", twin.ProjectText);
        Assert.NotEqual(row.ProjectText, twin.ProjectText);   // two projects that share a name keep two codenames
    });

    [Fact]
    public void A_session_row_off_streamer_mode_reads_as_before() => AppHost.Run(() =>
    {
        var info = Session(cwd: $@"{Profile}\qxproject", skills: ["qxskill"]);
        var row = new LedgerRowViewModel("k", info, DateOnly.MinValue, DateOnly.MaxValue, disambiguate: false);
        row.IsExpanded = true;
        Assert.Equal("qxproject", row.ProjectText);
        Assert.Equal($@"{Profile}\qxproject", row.CwdTooltip);
        Assert.Contains("qxskill", row.DetailText);
        Assert.Contains("Folder: .claude-qxwork", row.DetailText);
    });

    [Fact]
    public void Sessions_rows_keep_names_off_screen_in_flat_and_stacked_modes_and_follow_the_switch() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var sessions = new[]
            {
                Session(uuid: "u1", cwd: $@"{Profile}\qxproject", skills: ["qxskill"]),
                Session(uuid: "u2", root: ".claude-qxother", cwd: $@"{Profile}\qxproject", skills: ["qxskill2"]),
                Session(project: "qxtwin", projectKey: "C--a-qxtwin~1111aaaa", uuid: "u3"),
                Session(project: "qxtwin", projectKey: "C--b-qxtwin~2222bbbb", uuid: "u4"),
            };
            foreach (bool stacked in new[] { false, true })
            {
                var ledger = new CcLedgerViewModel(widget, stacked, _ => { });
                ledger.PresentForTests(sessions);
                foreach (var r in ledger.Rows) r.IsExpanded = true;
                Assert.Contains(ledger.Rows, r => r.ProjectText == "qxproject");
                widget.Streamer.SetOn(true);
                ledger.RefreshNames();
                Assert.NotEmpty(ledger.Rows);
                foreach (var r in ledger.Rows)
                {
                    NoSentinel($"project (stacked {stacked})", r.ProjectText);
                    NoSentinel("sort key", r.ProjectSort);
                    NoSentinel("spoken", r.ToString());
                    NoSentinel("detail", r.DetailText);
                    Assert.Null(r.CwdTooltip);
                }
                widget.Streamer.SetOn(false);
                ledger.RefreshNames();
                Assert.Contains(ledger.Rows, r => r.ProjectText == "qxproject");
                if (!stacked)
                    Assert.Contains("qxskill", ledger.Rows.First(r => r.ProjectText == "qxproject").DetailText, StringComparison.Ordinal);
            }
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_collapsed_rows_built_detail_is_dropped_when_names_change() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            var ledger = new CcLedgerViewModel(widget, false, _ => { });
            ledger.PresentForTests([Session(skills: ["qxskill"])]);
            var row = ledger.Rows[0];
            row.IsExpanded = true;
            Assert.Contains("qxskill", row.DetailText);
            row.IsExpanded = false;   // collapsed, but the text is still built
            widget.Streamer.SetOn(true);
            ledger.RefreshNames();
            row.IsExpanded = true;
            NoSentinel("detail after the switch", row.DetailText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_sessions_export_keeps_real_project_names_and_folders() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        try
        {
            widget.Streamer.SetOn(true);
            var ledger = new CcLedgerViewModel(widget, false, _ => { });
            ledger.PresentForTests([Session()]);
            var rows = (System.Collections.IEnumerable)typeof(CcLedgerViewModel)
                .GetMethod("BuildCsvRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(ledger, null)!;
            var first = rows.Cast<VaultLedgerCsv.Row>().Single();
            Assert.Contains("qxproject", first.ToString());
            Assert.Contains(".claude-qxwork", first.ToString());
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_session_archives_folders_are_codenames_and_keep_their_keys() => AppHost.Run(() =>
    {
        On(Profile);
        var toggle = new VaultRootToggleViewModel(null!, ".claude-qxwork", true);
        NoSentinel("archive", toggle.Shown + toggle.ToString());
        Assert.Equal(".claude-qxwork", toggle.Name);
        NoSentinel("stop question", LocalCcViewModel.StopArchivingQuestion(".claude-qxwork"));
        StreamerMode.Reset();
        Assert.Equal(".claude-qxwork", new VaultRootToggleViewModel(null!, ".claude-qxwork", true).Shown);
        Assert.Equal("Stop archiving .claude-qxwork?", LocalCcViewModel.StopArchivingQuestion(".claude-qxwork"));
    });

    [Fact]
    public void The_quick_route_shows_a_rows_real_name_and_settings_reloads_every_view() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            widget.Streamer.SetOn(true);
            var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
            svm.ClaudeCode.Overview.ShowTopForTests([("qxproject", 100L)], []);
            svm.ClaudeCode.Overview.RefreshNames();
            var row = svm.ClaudeCode.Overview.Projects[0];
            NoSentinel("before", row.Name);
            svm.ClaudeCode.ShowRealNameOfCommand.Execute(row);   // SetShown raises the reload through the service
            Assert.False(StreamerMode.Mask.Hides(CodenameCategory.Project, "qxproject"));
            svm.ReloadNames();
            Assert.Equal("qxproject", svm.ClaudeCode.Overview.Projects[0].Name);
            Assert.False(svm.ClaudeCode.Overview.Projects[0].CanShowRealName);
        }
        finally { widget.Dispose(); }
    });
}
