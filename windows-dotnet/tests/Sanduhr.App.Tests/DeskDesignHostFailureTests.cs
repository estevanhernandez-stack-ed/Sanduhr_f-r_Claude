using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Desk and the widget follow a design the service has already written. When they fail (a redraw throws), the widget must
/// not terminate: the failure is logged by operation and exception type, the settings stay as written, and the caller is answered.
/// </summary>
public sealed class DeskDesignHostFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-hostfail-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskDesignHostFailureTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
        _store.SaveDeskDirect(true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class ThrowingHost : IDeskDesignHost
    {
        public List<string> Calls { get; } = [];
        public Action? Apply { get; private set; }

        public void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss)
        {
            Calls.Add("show");
            Apply = apply;
            throw new ArgumentException("Width and Height must be non-negative.");
        }

        public void EndPreview()
        {
            Calls.Add("end");
            throw new InvalidOperationException("redraw failed");
        }

        public void Applied(string? themeKey)
        {
            Calls.Add("applied");
            throw new ArgumentException("Width and Height must be non-negative.");
        }
    }

    private void Run(Action<DeskDesignService, ThrowingHost> body) => Sta.Run(() =>
    {
        var host = new ThrowingHost();
        using var s = new DeskDesignService(_paths, _store) { Host = host, InstalledFonts = () => ["Segoe UI"] };
        body(s, host);
    });

    private void Send(DeskDesignService s, string id, string json)
    {
        Directory.CreateDirectory(_paths.ExternalDir);
        File.WriteAllBytes(s.Handoff.Paths.Request, Encoding.UTF8.GetBytes(
            "{\"schema_version\":1,\"id\":\"" + id + "\",\"requested_at\":\"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "\",\"design\":" + json + "}"));
        s.Handoff.Check();
    }

    private string ResultStatus() =>
        (string)((JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, DeskDesign.ResultFile)))!)["result"]!["status"]!;

    private string Log() => File.Exists(_paths.LogFile) ? File.ReadAllText(_paths.LogFile) : "";

    [Fact]
    public void ADirectApplyWhoseRedrawThrowsStillAppliesAndIsLoggedNotFatal() =>
        Run((s, host) =>
        {
            Send(s, "a", """{"name":"Dusk","plate":true}""");
            Assert.Equal("applied", ResultStatus());
            Assert.True(_store.LoadDeskPlate());          // written before the host was asked to follow
            Assert.Contains("applied", host.Calls);
            Assert.Contains("desk design refresh failed (ArgumentException)", Log());
            Assert.DoesNotContain("Dusk", Log());          // the log names the operation and the exception type, never the design
        });

    [Fact]
    public void PutMyDeskBackWhoseRedrawThrowsStillSwitchesAndReportsSuccess() =>
        Run((s, _) =>
        {
            Send(s, "a", """{"name":"Dusk","plate":true}""");
            var result = s.PutBack();
            Assert.True(result.Ok, result.Message);
            Assert.False(_store.LoadDeskPlate());
            Assert.Contains("refresh failed", Log());
        });

    [Fact]
    public void APreviewThatThrowsWhileDrawingStillAnswersAndNeverTerminates()
    {
        _store.SaveDeskDirect(false);
        Run((s, host) =>
        {
            Send(s, "p", """{"name":"Dusk","plate":true}""");
            Assert.Equal("previewing", ResultStatus());
            Assert.Contains("preview failed", Log());
            host.Apply!();                                 // Apply from the bar still works, and the end-preview failure is logged too
            Assert.Equal("applied", ResultStatus());
            Assert.True(_store.LoadDeskPlate());
            Assert.Contains("end preview failed (InvalidOperationException)", Log());
            s.DismissReview();                             // nothing is being reviewed any more
        });
    }

    [Fact]
    public void DismissWhoseRestoreThrowsStillAnswersRejected()
    {
        _store.SaveDeskDirect(false);
        Run((s, _) =>
        {
            Send(s, "d", """{"name":"Dusk","plate":true}""");
            s.DismissReview();
            Assert.Equal("rejected", ResultStatus());
            Assert.False(_store.LoadDeskPlate());
        });
    }
}
