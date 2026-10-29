using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Publish usage, Weather, Watchers and About (spec, Reach inside Settings; CPs 11, 18 to 23), and the
/// theme error lines (preflight P1). Masks are display only: the model keeps real values and the record that would be sent is unchanged.</summary>
[Collection("SettingsWindow")]
public sealed class StreamerPagesTests : AppWindowFixture
{
    private const string Endpoint = "https://qxhost.example/ingest";
    private const string Raw = @"Access to the path 'C:\Users\qxuser\AppData\Roaming\Sanduhr\themes\x.json' is denied.";

    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    private (SettingsViewModel Svm, WidgetViewModel Widget, UsagePublishService Service, SettingsStore Store) Make()
    {
        Home(".claude-qxwork");
        var (widget, _) = ScratchWidget("Qxalpha");
        var paths = ScratchPaths();
        var store = new SettingsStore(paths);
        store.SavePublishEndpointUrl(Endpoint);
        store.SavePublishAuthScheme(PublishAuthScheme.None);
        store.SavePublishRoots(new Dictionary<string, bool> { [".claude-qxwork"] = true });
        var service = new UsagePublishService(store, new CcLogReader(Profile, () => [".claude-qxwork"]), paths,
            new PublishTokenStore(new MemoryCredentials()), () => Profile);
        widget.AttachPublishService(service);
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        return (svm, widget, service, store);
    }

    [Fact]
    public void Publish_shows_the_endpoint_host_by_codename_and_hides_the_box() => AppHost.Run(() =>
    {
        var (svm, widget, _, _) = Make();
        try
        {
            Assert.Contains(svm.Publish.Roots, r => r.Name == ".claude-qxwork");   // the rows exist, so the check below is not vacuous
            Assert.Equal(Endpoint, svm.Publish.EndpointShown);
            widget.Streamer.SetOn(true);
            svm.Publish.RefreshNames();
            Assert.True(svm.Publish.StreamerOn);
            NoSentinel("endpoint", svm.Publish.EndpointShown);
            Assert.Equal(Endpoint, svm.Publish.EndpointUrl);   // the value itself never changes
            Assert.All(svm.Publish.Roots, r => NoSentinel("share row", r.Shown + r.ToString()));
            Assert.Contains(svm.Publish.Roots, r => r.Name == ".claude-qxwork");   // the key stays real
            NoSentinel("status", svm.Publish.StatusText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_publish_sample_names_no_folder_project_host_or_pc_and_the_record_sent_is_unchanged() => AppHost.Run(() =>
    {
        var (svm, widget, service, _) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            string? header = null, json = null;
            svm.Publish.SampleHook = (h, _, j) => { header = h; json = j; };
            var running = svm.Publish.ShowSampleCommand.ExecuteAsync(null);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!running.IsCompleted && DateTime.UtcNow < deadline)   // the sample builds off the UI thread and resumes on it: pump, never block
            {
                Pump();
                Thread.Sleep(10);
            }
            Assert.True(running.IsCompleted);
            Assert.NotNull(json);
            NoSentinel("sample header", header);
            NoSentinel("sample json", json);
            Assert.DoesNotContain(Environment.MachineName, json!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(SettingsNames.StreamerThisPc, (string?)JsonNode.Parse(json!)!["machine"]);
            // Display only: the record a publish would send keeps the real PC name and folder.
            var sent = service.BuildRecord(UsagePublisher.DefaultDate(DateTimeOffset.Now));
            Assert.Equal(Environment.MachineName, (string?)sent.Payload["machine"]);
            Assert.Contains(".claude-qxwork", sent.Homes);
            Assert.Contains(".claude-qxwork", sent.Payload.ToJsonString());
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_publish_result_masks_every_host_including_a_redirect_target_and_cuts_a_rejection_body() => AppHost.Run(() =>
    {
        var (svm, widget, _, store) = Make();
        try
        {
            store.SavePublishAttempt(DateTimeOffset.Now, SettingsNames.PublishRedirected(302, "qxother.example"), null);
            svm.Publish.RefreshNames();
            Assert.Contains("qxother.example", svm.Publish.StatusText);   // off: as before
            widget.Streamer.SetOn(true);
            svm.Publish.RefreshNames();
            Assert.Contains("redirected (302)", svm.Publish.StatusText);
            NoSentinel("redirect", svm.Publish.StatusText);

            store.SavePublishAttempt(DateTimeOffset.Now, @"Rejected (400): qxsecret.example says no to C:\Users\qxuser\x", null);
            svm.Publish.RefreshNames();
            NoSentinel("rejected", svm.Publish.StatusText);
            Assert.Contains("Rejected (400).", svm.Publish.StatusText);

            store.SavePublishAttempt(DateTimeOffset.Now, "Could not reach qxhost.example (10.1.2.3): timed out", null);
            svm.Publish.RefreshNames();
            NoSentinel("unreachable", svm.Publish.StatusText);
            Assert.DoesNotContain("10.1.2.3", svm.Publish.StatusText);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Weather_shows_the_nickname_else_the_citys_codename_and_masks_places_found() => AppHost.Run(() =>
    {
        var (svm, widget, _, _) = Make();
        try
        {
            var weather = svm.WeatherPage;
            weather.Picked = WeatherPlace.Create("Qxcity", "Texas", "United States", 32.75, -97.33);
            Assert.Contains("Qxcity", weather.PlaceText);
            widget.Streamer.SetOn(true);
            weather.RefreshNames();
            NoSentinel("place", weather.PlaceText);
            weather.Nickname = "Home";
            weather.RefreshNames();
            Assert.Equal("Home", weather.PlaceText);
            var found = WeatherPlace.Create("Qxtown", null, "Germany", 50.1, 8.7);
            weather.ShowResultsForTests([found]);
            Assert.NotEmpty(weather.Results);
            Assert.All(weather.Results, r => NoSentinel("place found", r.Shown + r.ToString()));
            Assert.Equal(found, weather.Results[0].Place);   // the choice still carries the real place
            Assert.True(weather.PlacesHidden);
            widget.Streamer.SetOn(false);
            weather.RefreshNames();
            Assert.Contains("Qxtown", weather.Results[0].Shown);
            Assert.False(weather.PlacesHidden);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_runner_line_names_runners_by_codename()
    {
        StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), Profile));
        var text = App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.Watching, ["qxrunner-1", "qxrunner-2"]));
        NoSentinel("runners", text);
        Assert.StartsWith("Watching 2 runners: ", text);
        Assert.Equal("Docker isn't running.", App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.DockerDown, [])));
        Assert.Equal("", App.RunnerStatusText(null));
        StreamerMode.Reset();
        Assert.Contains("qxrunner-1", App.RunnerStatusText(new RunnerStatus(RunnerStatusKind.Watching, ["qxrunner-1"])));
    }

    [Fact]
    public void About_shows_the_user_folder_as_USERPROFILE_and_opens_the_real_one() => AppHost.Run(() =>
    {
        var (svm, widget, _, _) = Make();
        try
        {
            Assert.Contains(Environment.UserName, svm.SettingsFolderShown, StringComparison.OrdinalIgnoreCase);   // off: the real path
            widget.Streamer.SetOn(true);
            svm.ReloadNames();
            foreach (var shown in new[] { svm.SettingsFolderShown, svm.LocalFolderShown, svm.ClaudeCodeFilesFolderShown })
            {
                Assert.DoesNotContain(Environment.UserName, shown, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith("%USERPROFILE%", shown);
            }
            Assert.NotEqual(svm.SettingsFolderShown, svm.SettingsFolder);   // the Show in Explorer command keeps the real path
            Assert.Contains(Environment.UserName, svm.SettingsFolder, StringComparison.OrdinalIgnoreCase);
        }
        finally { widget.Dispose(); }
    });

    // ---- Preflight P1: theme error lines ----

    [Fact]
    public void A_failed_theme_save_on_the_Themes_page_hides_the_user_folder() => AppHost.Run(() =>
    {
        var (svm, widget, _, _) = Make();
        try
        {
            var themes = svm.Themes;
            var shown = new List<string>();
            themes.ErrorHook = shown.Add;
            themes.WriteText = (_, _) => throw new IOException(Raw);
            themes.PasteJson = """{"name":"Qx theme"}""";
            themes.FileName = "x";
            widget.Streamer.SetOn(true);
            themes.SaveAndApplyCommand.Execute(null);
            var line = Assert.Single(shown);
            Assert.StartsWith("Could not write theme: ", line);
            Assert.DoesNotContain("qxuser", line);
            Assert.Contains("%USERPROFILE%", line);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_failed_theme_delete_hides_the_user_folder_and_shows_it_while_streamer_mode_is_off() => AppHost.Run(() =>
    {
        var (svm, widget, _, _) = Make();
        try
        {
            var themes = svm.Themes;
            var shown = new List<string>();
            themes.ErrorHook = shown.Add;
            themes.DeleteFile = _ => throw new IOException(Raw);
            Assert.False(themes.TryDelete("x.json"));
            Assert.Contains("qxuser", Assert.Single(shown));   // off: unchanged
            shown.Clear();
            widget.Streamer.SetOn(true);
            Assert.False(themes.TryDelete("x.json"));
            var line = Assert.Single(shown);
            Assert.StartsWith("Could not delete: ", line);
            Assert.DoesNotContain("qxuser", line);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_failed_Theme_Studio_save_hides_the_user_folder() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Qxalpha");
        try
        {
            var errors = new List<string>();
            var studio = new ThemeStudioViewModel(widget, errors.Add, _ => { }, () => { }, ["Consolas", "Segoe UI"]);
            studio.WriteText = (_, _) => throw new IOException(Raw);
            studio.Activate();
            studio.FileName = "studio-qx";
            widget.Streamer.SetOn(true);
            studio.SaveCommand.Execute(null);
            var line = Assert.Single(errors);
            Assert.StartsWith("Could not write the theme: ", line);
            Assert.DoesNotContain("qxuser", line);
            Assert.Contains("%USERPROFILE%", line);
        }
        finally { widget.Dispose(); }
    });
}
