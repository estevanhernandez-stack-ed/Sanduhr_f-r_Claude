using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5: Publish usage, What gets sent (WF28). The sample is built by the function that builds the real record, and
/// building it sends nothing, reads no token and writes nothing. Scratch folders, an in-memory token store, and a recording handler
/// in place of the network.
/// </summary>
[Collection("SettingsWindow")]
public sealed class PublishSampleTests : AppWindowFixture
{
    private const string FakeToken = "tok-SECRET-0123456789-do-not-show";

    private sealed class Recorder : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        /// <summary>When set, any call is a test failure (thrown, and counted).</summary>
        public bool Forbid { get; set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Forbid)
                throw new InvalidOperationException("The sample made an HTTP request.");
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>Counts every credential read, so a test can prove the sample never touches the publish token slot.</summary>
    private sealed class CountingCredentials : ICredentialManager
    {
        private readonly Dictionary<string, string> _store = new();
        public int Reads { get; set; }
        public string Service => AccountStore.Service;

        public string? GetPassword(string slot)
        {
            Reads++;
            return _store.GetValueOrDefault(slot);
        }

        public void SetPassword(string slot, string value) => _store[slot] = value;
        public bool DeletePassword(string slot) => _store.Remove(slot);
    }

    private (UsagePublishService Service, Recorder Net, CountingCredentials Creds, Paths Paths) Service(bool tickFolder, bool tokenScheme = false)
    {
        var paths = ScratchPaths();
        var home = Path.Combine(Root, "home");
        Directory.CreateDirectory(Path.Combine(home, ".claude", "projects"));
        var settings = new SettingsStore(paths);
        settings.SavePublishEndpointUrl("https://example.test/ingest");
        settings.SavePublishAuthScheme(tokenScheme ? PublishAuthScheme.Bearer : PublishAuthScheme.None);
        if (tickFolder)
            settings.SavePublishRoots(new Dictionary<string, bool> { [".claude"] = true });
        var creds = new CountingCredentials();
        var service = new UsagePublishService(settings, new CcLogReader(home, () => [".claude"]), paths,
            new PublishTokenStore(creds), () => home);
        var net = new Recorder();
        service.Handler = net;
        return (service, net, creds, paths);
    }

    private static string[] Snapshot(string dir) =>
        Directory.Exists(dir)
            ? [.. Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => f + "|" + File.GetLastWriteTimeUtc(f).Ticks + "|" + new FileInfo(f).Length).Order()]
            : [];

    [Fact]
    public void The_sample_is_the_record_publish_now_sends()
    {
        var (service, net, _, _) = Service(tickFolder: true);
        var now = DateTimeOffset.Now;

        var sample = service.SampleAsync(now).GetAwaiter().GetResult();
        var outcome = service.PublishAsync(UsagePublisher.DefaultDate(now)).GetAwaiter().GetResult();

        Assert.NotNull(outcome);
        Assert.True(outcome.Result.Ok, outcome.Result.Message);
        var sent = JsonNode.Parse(Assert.Single(net.Bodies));
        Assert.True(JsonNode.DeepEquals(sample.Payload, sent), $"sample {sample.Payload.ToJsonString()}{Environment.NewLine}sent {sent?.ToJsonString()}");
        Assert.Equal([".claude"], sample.Homes);
    }

    [Fact]
    public void The_sample_is_byte_for_byte_the_record_BuildRecord_gives_publish()
    {
        var (service, _, _, _) = Service(tickFolder: true);
        var now = DateTimeOffset.Now;
        var date = UsagePublisher.DefaultDate(now);

        var sample = service.SampleAsync(now).GetAwaiter().GetResult();
        var built = service.BuildRecord(date);

        Assert.Equal(built.Payload.ToJsonString(), sample.Payload.ToJsonString());
        Assert.Equal(built.Homes, sample.Homes);
        Assert.Equal(built.TotalTokens, sample.TotalTokens);
        Assert.Equal(built.QuotaStatus, sample.QuotaStatus);
    }

    [Fact]
    public void Building_the_sample_sends_nothing()
    {
        var (service, net, _, _) = Service(tickFolder: true);
        net.Forbid = true;

        service.SampleAsync(DateTimeOffset.Now).GetAwaiter().GetResult();

        Assert.Equal(0, net.Calls);
        Assert.Empty(net.Bodies);
    }

    [Fact]
    public void Show_a_sample_makes_no_request_writes_no_file_and_no_history()
    {
        var (service, net, _, paths) = Service(tickFolder: true);
        net.Forbid = true;
        var vm = new PublishViewModel(service) { SampleHook = (_, _, _) => { } };
        var external = Snapshot(paths.ExternalDir);
        var appData = Snapshot(paths.AppDataDir);

        vm.ShowSampleCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal(0, net.Calls);
        Assert.Equal(external, Snapshot(paths.ExternalDir));
        Assert.Equal(appData, Snapshot(paths.AppDataDir));
        Assert.DoesNotContain(Directory.Exists(paths.ExternalDir) ? Directory.EnumerateFiles(paths.ExternalDir) : [], f => Path.GetFileName(f).StartsWith("publish-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Directory.Exists(paths.AppDataDir) ? Directory.EnumerateFiles(paths.AppDataDir) : [], f => Path.GetFileName(f).StartsWith("history", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Never published.", vm.StatusText);   // no attempt recorded
    }

    [Fact]
    public void The_sample_never_reads_or_shows_the_publish_token()
    {
        var (service, net, creds, _) = Service(tickFolder: true, tokenScheme: true);
        service.SaveToken(FakeToken);
        net.Forbid = true;
        string? header = null, note = null, json = null;
        var vm = new PublishViewModel(service) { SampleHook = (h, n, j) => (header, note, json) = (h, n, j) };
        creds.Reads = 0;

        vm.ShowSampleCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        var record = service.SampleAsync(DateTimeOffset.Now).GetAwaiter().GetResult();

        Assert.Equal(0, creds.Reads);
        Assert.NotNull(json);
        Assert.DoesNotContain(FakeToken, json);
        Assert.DoesNotContain(FakeToken, header);
        Assert.DoesNotContain(FakeToken, note);
        Assert.DoesNotContain(FakeToken, record.Payload.ToJsonString());
        Assert.DoesNotContain("SECRET", json);
    }

    [Fact]
    public void With_no_folder_ticked_the_sample_says_so_and_names_the_destination()
    {
        var (service, net, _, _) = Service(tickFolder: false);
        net.Forbid = true;
        string? header = null, note = null, json = null;
        var vm = new PublishViewModel(service) { SampleHook = (h, n, j) => (header, note, json) = (h, n, j) };

        vm.ShowSampleCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal(SettingsNames.SampleNoFolders, note);
        var date = UsagePublisher.DefaultDate(DateTimeOffset.Now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(SettingsNames.SampleHeader(date, "example.test"), header);
        Assert.Empty(((JsonArray)JsonNode.Parse(json!)!["homes"]!));
        Assert.Equal(0, net.Calls);
        Assert.False(vm.IsSampling);
    }

    [Fact]
    public void With_no_endpoint_the_header_has_no_host()
    {
        var (service, _, _, _) = Service(tickFolder: true);
        service.SetEndpointUrl("");
        string? header = null;
        var vm = new PublishViewModel(service) { SampleHook = (h, _, _) => header = h };

        vm.ShowSampleCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        var date = UsagePublisher.DefaultDate(DateTimeOffset.Now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(SettingsNames.SampleHeader(date, null), header);
    }

    [Fact]
    public void A_sample_that_cant_be_built_says_so()
    {
        var paths = ScratchPaths();
        var settings = new SettingsStore(paths);
        settings.SavePublishRoots(new Dictionary<string, bool> { [".claude"] = true });
        var broken = new UsagePublishService(settings, new CcLogReader(Path.Combine(Root, "home"), () => [".claude"]), paths,
            new PublishTokenStore(new CountingCredentials()), () => throw new IOException("the folder went away"));
        var shown = false;
        var vm = new PublishViewModel(broken) { SampleHook = (_, _, _) => shown = true };

        vm.ShowSampleCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.False(shown);
        Assert.Equal(SettingsNames.SampleFailed, vm.StatusText);
        Assert.False(vm.IsSampling);
        Assert.False(new PublishViewModel(null).ShowSampleCommand.CanExecute(null));   // no service: inert
    }

    [Fact]
    public void The_sample_window_is_read_only_selectable_named_and_closes_on_Escape()
    {
        RunOnSta(() =>
        {
            var window = new PublishSampleWindow("header text", "note text", "{ \"a\": 1 }");
            var box = (TextBox)window.FindName("RecordBox");
            Assert.True(box.IsReadOnly);
            Assert.True(box.IsReadOnlyCaretVisible);
            Assert.Equal("{ \"a\": 1 }", box.Text);
            Assert.Equal(SettingsNames.SampleTitle, AutomationProperties.GetName(box));
            Assert.Equal(SettingsNames.SampleTitle, window.Title);
            var close = (Button)window.FindName("CloseButton");
            Assert.Equal(SettingsNames.SampleClose, AutomationProperties.GetName(close));
            Assert.True(close.IsCancel);   // Escape closes it
            Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("NoteText")).Visibility);
        });
    }

    [Fact]
    public void An_empty_note_collapses()
    {
        RunOnSta(() =>
        {
            var window = new PublishSampleWindow("h", "", "{}");
            Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("NoteText")).Visibility);
        });
    }

    private static void RunOnSta(Action body) => Sta.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        body();
    });
}
