using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Widgets made with Claude, the app side (spec 3.8 sections 5 and 6): a request is checked again by the widget, previewed (a sprite
/// staged and drawn by the companion's own loader) and answered <c>previewing</c>; Apply installs it as <c>claude</c> and can make it
/// the companion; Dismiss writes nothing; the direct switch never skips Apply; and "Use my own art…" turns a Claude-made sprite into
/// the user's own. Scratch folders and a fake host only; never the real Sanduhr folders.
/// </summary>
[Collection("SettingsWindow")]
public sealed class WidgetReviewServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wreview-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public WidgetReviewServiceTests()
    {
        _paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"), Path.Combine(_root, "home", ".sanduhr"));
        _store = new SettingsStore(_paths);
        _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "clock:bl claude:bl" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class FakeHost : IWidgetReviewHost
    {
        public List<string> Calls { get; } = [];
        public WidgetPreview? Preview { get; private set; }
        public DeskReviewModel? Model { get; private set; }
        public Action<bool>? Apply { get; private set; }
        public Action? Dismiss { get; private set; }
        public bool DeskBusy { get; set; }

        public void ShowPreview(WidgetPreview preview, DeskReviewModel model, Action<bool> apply, Action dismiss)
        {
            Calls.Add("show");
            Preview = preview;
            Model = model;
            Apply = apply;
            Dismiss = dismiss;
        }

        public void EndPreview() => Calls.Add("end");
        public void Installed(string packId) => Calls.Add("installed " + packId);
    }

    private static string Fixture(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs", "good", "good-sprite-pixel", file));

    private static WidgetProposalPack Sprite(string id = "blob", string name = "Blob") => WidgetProposal.Build(new JsonObject
    {
        ["kind"] = "sprite",
        ["pack"] = new JsonObject { ["id"] = id, ["name"] = name, ["description"] = "A small blob." },
        ["sprite"] = JsonNode.Parse(Fixture("sprite.json")),
        ["sheets"] = new JsonObject { ["sheet.json"] = JsonNode.Parse(Fixture("sheet.json")) },
    }, Now).Pack!;

    private static WidgetProposalPack Palette() => WidgetProposal.Build(new JsonObject
    {
        ["kind"] = "palette",
        ["pack"] = new JsonObject { ["id"] = "dusk", ["name"] = "Dusk", ["description"] = "An evening gradient." },
        ["palettes"] = JsonNode.Parse("""[{"name":"dusk","colors":["#ff2a6d","#05d9e8"]}]"""),
    }, Now).Pack!;

    private static byte[] Request(string id, WidgetProposalPack pack) =>
        Encoding.UTF8.GetBytes(WidgetProposal.RequestJson(id, pack, "made for you", Now).ToJsonString());

    private WidgetReviewService Service(FakeHost? host) => new(_paths, _store, clock: () => Now) { Host = host };

    private JsonObject Result() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.ExternalDir, WidgetProposal.ResultFile)))!;

    private WidgetStore Installed => new(_paths.WidgetsDir, WidgetCatalogService.EncodePng);

    [Fact]
    public void ASpriteIsStagedPreviewedAndAnsweredPreviewingWithNothingInstalled()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            service.Handoff.Receive(Request("w1", Sprite()));

            Assert.Equal("previewing", (string?)Result()["result"]!["status"]);
            Assert.Equal("my-blob", (string?)Result()["result"]!["pack_id"]);
            Assert.Equal(["show"], host.Calls);
            Assert.True(service.Reviewing);
            Assert.Equal("Claude suggests a widget · Blob", host.Model!.Heading);
            Assert.Equal("Make it the companion", host.Model.Option);
            Assert.Equal("made for you", host.Model.Note);
            // Staged where the companion's loader reads it, rasterized; the real widgets folder has no row and no pack.
            var staged = host.Preview!.Staged!;
            Assert.True(File.Exists(staged.RasterPath("my-blob", "sheet.json")));
            Assert.StartsWith(Path.Combine(_paths.WidgetsDir, WidgetReviewService.PreviewFolder), staged.Root);
            Assert.Empty(Installed.Load().Packs);
            Assert.False(Directory.Exists(Installed.PackDir("my-blob")));
        });
    }

    [Fact]
    public void TheCompanionsOwnLoaderDrawsTheStagedPackUntilThePreviewEnds()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            service.Handoff.Receive(Request("w2", Sprite()));
            using var companion = new CompanionService(_paths, _store, claudeCodeDays: () => [], lastInput: () => Now, clock: () => Now, reduceMotion: () => true);
            companion.PreviewPack(host.Preview!.Staged!, host.Preview.Pack.Id);
            companion.Reload();
            Assert.True(companion.PreviewingPack);
            Assert.Equal("my-blob", companion.PackId);
            Assert.True(companion.Ready);
            Assert.Equal(16, companion.CellSize.Width);   // 8 px cells at scale 2

            companion.EndPreviewPack();
            companion.Reload();
            Assert.Null(companion.PackId);   // nothing is installed, so nothing is drawn
        });
    }

    [Fact]
    public void ApplyInstallsAsClaudeMadeAndMakesItTheCompanion()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            int installed = 0;
            service.Installed += () => installed++;
            service.Handoff.Receive(Request("w3", Sprite()));
            host.Apply!(true);

            var result = Result()["result"]!;
            Assert.Equal("applied", (string?)result["status"]);
            Assert.True((bool)result["companion"]!);
            var row = Installed.Load().Find("my-blob")!;
            Assert.Equal(WidgetSource.Claude, row.Source);
            Assert.True(row.MadeHere);
            Assert.True(File.Exists(Installed.RasterPath("my-blob", "sheet.json")));
            Assert.Equal("my-blob", _store.LoadCompanion().Pack);
            Assert.NotNull(new DeskArrangement(_store.LoadDesk().Layout).Placement(CompanionSettings.Widget));
            Assert.Equal(["show", "end", "installed my-blob"], host.Calls);
            Assert.Equal(1, installed);
            Assert.False(service.Reviewing);
            Assert.False(Directory.Exists(service.PreviewRoot));
        });
    }

    [Fact]
    public void ApplyWithoutTheCompanionChoiceLeavesTheCompanionAlone()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            service.Handoff.Receive(Request("w4", Sprite()));
            host.Apply!(false);
            Assert.False((bool)Result()["result"]!["companion"]!);
            Assert.Null(_store.LoadCompanion().Pack);
            Assert.Null(new DeskArrangement(_store.LoadDesk().Layout).Placement(CompanionSettings.Widget));
            Assert.NotNull(Installed.Load().Find("my-blob"));
        });
    }

    [Fact]
    public void DismissInstallsNothingAndRemovesTheStagedCopy()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            string settingsBefore = File.ReadAllText(_paths.SettingsFile);
            service.Handoff.Receive(Request("w5", Sprite()));
            host.Dismiss!();

            var result = Result()["result"]!;
            Assert.Equal("rejected", (string?)result["status"]);
            Assert.Equal("dismissed", (string?)result["reason"]);
            Assert.False(File.Exists(Installed.RegistryFile));
            Assert.False(Directory.Exists(Installed.PackDir("my-blob")));
            Assert.False(Directory.Exists(service.PreviewRoot));
            Assert.Equal(settingsBefore, File.ReadAllText(_paths.SettingsFile));
            Assert.Equal(["show", "end"], host.Calls);
        });
    }

    [Fact]
    public void LettingClaudeChangeTheDeskDirectlyNeverSkipsApplyForAWidget()
    {
        Sta.Run(() =>
        {
            _store.SaveDeskDirect(true);
            var host = new FakeHost();
            using var service = Service(host);
            service.Handoff.Receive(Request("w6", Palette()));
            Assert.Equal("previewing", (string?)Result()["result"]!["status"]);
            Assert.Empty(Installed.Load().Packs);
            Assert.Equal("ff2a6d,05d9e8", host.Preview!.Ink);
            Assert.Null(host.Preview.Staged);   // only a sprite is staged
        });
    }

    [Fact]
    public void WithNoHostTheWidgetAnswersNotReadyAndWithADeskDesignUnderReviewBusy()
    {
        Sta.Run(() =>
        {
            using (var service = Service(null))
            {
                service.Handoff.Receive(Request("w7", Palette()));
                Assert.Equal("not_ready", (string?)Result()["result"]!["reason"]);
            }
            var host = new FakeHost { DeskBusy = true };
            using var busy = Service(host);
            busy.Handoff.Receive(Request("w8", Palette()));
            Assert.Equal("busy", (string?)Result()["result"]!["reason"]);
            Assert.Empty(host.Calls);
        });
    }

    [Fact]
    public void ANewerProposalReplacesTheOneUnderReview()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            service.Handoff.Receive(Request("w9", Sprite()));
            var older = host.Dismiss;
            service.Handoff.Receive(Request("w10", Palette()));
            Assert.Equal("my-dusk", service.ReviewingId);
            Assert.Equal(["show", "end", "show"], host.Calls);
            older!();   // the old callback finds the new review; Dismiss acts on what is on the desktop now
            Assert.False(service.Reviewing);
        });
    }

    [Fact]
    public void TheWidgetRefusesATamperedRequestWithoutShowingIt()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using var service = Service(host);
            var request = WidgetProposal.RequestJson("w11", Sprite(), null, Now);
            request["files"]!["sprite.json"] = ((string)request["files"]!["sprite.json"]!).Replace("\"walk\"", "\"anthropic_walk\"");
            service.Handoff.Receive(Encoding.UTF8.GetBytes(request.ToJsonString()));
            Assert.Equal("rejected", (string?)Result()["result"]!["status"]);
            Assert.Empty(host.Calls);
            Assert.False(Directory.Exists(service.PreviewRoot));
        });
    }

    [Fact]
    public void TheBarWearsTheWidgetsWordsAndItsOneChoice()
    {
        Sta.Run(() =>
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var bar = new DeskReviewBar();
            bar.SetModel(WidgetReview.Model(Sprite(), null, deskOn: true));
            Assert.Equal("Claude suggests a widget · Blob", bar.Heading.Text);
            Assert.Equal("What this widget is", bar.ChangesHeader.Text);
            Assert.Equal(System.Windows.Visibility.Visible, bar.Option.Visibility);
            Assert.Equal("Make it the companion", System.Windows.Automation.AutomationProperties.GetName(bar.Option));
            Assert.Equal("Review Claude's widget", System.Windows.Automation.AutomationProperties.GetName(bar));
            Assert.True(bar.OptionChecked);   // on by default
            bar.Option.IsChecked = false;
            bar.SetModel(WidgetReview.Model(Sprite(), "again", deskOn: true));
            Assert.False(bar.OptionChecked);   // the same bar updating keeps the user's choice

            bar.SetModel(WidgetReview.Model(Palette(), null, deskOn: true));
            Assert.Equal(System.Windows.Visibility.Collapsed, bar.Option.Visibility);
            Assert.False(bar.OptionChecked);

            bar.SetModel(new DeskReviewModel("Dusk", null, ["Plate behind the text: on"], [], false, []));
            Assert.Equal("Claude suggests a Desk design", bar.Heading.Text);
            Assert.Equal("What changed", bar.ChangesHeader.Text);
        });
    }

    // ---- Use my own art… ----

    private static byte[] Png(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 0xee; pixels[i + 1] = 0xd3; pixels[i + 2] = 0x22; pixels[i + 3] = 255; }
        return WidgetCatalogService.EncodePng(new PixelSheetImage(w, h, 1, 1, 1, pixels));
    }

    [Fact]
    public void UseMyOwnArtCopiesACheckedPngIntoAClaudeMadeSpriteAndMakesItLocal()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using (var service = Service(host))
            {
                service.Handoff.Receive(Request("w12", Sprite()));
                host.Apply!(true);
            }
            string art = Path.Combine(_root, "my-art.png");
            File.WriteAllBytes(art, Png(32, 8));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            page.ReloadLocal();
            var row = Assert.Single(page.Installed);
            Assert.True(row.CanUseOwnArt);
            var titles = new List<string>();
            page.PickImage = t => { titles.Add(t); return art; };
            row.UseOwnArtCommand.Execute(null);

            Assert.Single(titles);
            Assert.Contains("now uses your own art", page.Status);
            var store = Installed;
            var reg = store.Load().Find("my-blob")!;
            Assert.Equal(WidgetSource.Local, reg.Source);
            Assert.Equal("1.0.1", reg.Version);
            Assert.True(File.Exists(Path.Combine(store.PackDir("my-blob"), "art", "sprite.png")));
            Assert.False(File.Exists(Path.Combine(store.PackDir("my-blob"), "sheet.json")));
        });
    }

    [Fact]
    public void UseMyOwnArtRefusesAnImageThatBreaksThePackRulesAndChangesNothing()
    {
        Sta.Run(() =>
        {
            var host = new FakeHost();
            using (var service = Service(host))
            {
                service.Handoff.Receive(Request("w13", Sprite()));
                host.Apply!(false);
            }
            string art = Path.Combine(_root, "odd.png");
            File.WriteAllBytes(art, Png(30, 8));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            page.ReloadLocal();
            page.PickImage = _ => art;
            page.Installed[0].UseOwnArtCommand.Execute(null);
            Assert.Contains("whole number of 8x8 cells", page.Status);
            Assert.Equal(WidgetSource.Claude, Installed.Load().Find("my-blob")!.Source);

            File.WriteAllText(art, "not a png");
            page.Installed[0].UseOwnArtCommand.Execute(null);
            Assert.Contains("isn't a PNG", page.Status);

            page.PickImage = _ => null;
            page.Installed[0].UseOwnArtCommand.Execute(null);
            Assert.Contains("No image was chosen", page.Status);
        });
    }

    [Fact]
    public void ACatalogSpriteNeverOffersUseMyOwnArtAndAPaletteMadeHereDoesNotEither()
    {
        Sta.Run(() =>
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs", "good", "good-sprite-pixel");
            var files = Directory.GetFiles(dir).ToDictionary(f => Path.GetFileName(f), f => File.ReadAllBytes(f), StringComparer.Ordinal);
            var verdict = PackValidator.Validate(files, "good-sprite-pixel");
            Assert.True(Installed.Install(files, verdict.Manifest!, WidgetSource.Catalog).Ok);
            var palette = Palette();
            Assert.True(Installed.Install(palette.Files, palette.Manifest, WidgetSource.Claude).Ok);

            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            page.ReloadLocal();
            Assert.Equal(2, page.Installed.Count);
            Assert.All(page.Installed, r => Assert.False(r.CanUseOwnArt));
        });
    }
}
