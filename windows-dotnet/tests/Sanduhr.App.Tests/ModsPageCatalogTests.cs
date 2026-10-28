using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4: the Catalog section (spec, Mods &amp; Widgets, section 3; WF6, WF40). Collapsed by default, it fetches only when opened or
/// on "Check the catalog again"; a pack appears once, merged with its installed row by id. Over a fake GitHub only.
/// </summary>
public sealed class ModsPageCatalogTests : ModsPageFixture
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeGitHub _net = new();

    /// <summary>Holds every request until <see cref="Release"/>, so two callers can both be in flight when the second one asks.</summary>
    private sealed class GateHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _open.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await _open.Task;
            return await base.SendAsync(request, ct);
        }
    }

    private ModsPageViewModel Published(Version? running = null, HttpMessageHandler? handler = null)
    {
        var palette = WidgetKit.Zip(WidgetKit.Pack("good-palette"));
        var sprite = WidgetKit.Zip(WidgetKit.Pack("good-sprite"));
        WidgetKit.Publish(_net, _key, "2026-10-25T02:00:00Z",
            (WidgetKit.Entry("good-palette", "Fixture palette", "palette", "1.0.0", palette), palette),
            (WidgetKit.Entry("good-sprite", "Fixture sprite", "sprite", "1.0.0", sprite), sprite));
        return new ModsPageViewModel(() => new Sanduhr.App.Services.WidgetCatalogService(Paths, handler ?? _net, running ?? WidgetKit.V380, [WidgetKit.Spki(_key)]),
            null, Host())
        {
            Confirm = (_, _) => true,
        };
    }

    [Fact]
    public async Task Landing_on_the_page_sends_no_request()
    {
        var page = Published();
        await page.Activate();
        page.ReloadLocal();
        page.FilterText = "palette";
        Assert.Empty(_net.Seen);
        Assert.Empty(page.Catalog);
        Assert.Null(page.CatalogStatus);
    }

    [Fact]
    public async Task The_catalog_fetches_once_when_opened_and_again_only_on_check_again()
    {
        var page = Published();
        await page.Activate();

        page.CatalogOpen = true;
        await page.OpenCatalog();
        int first = _net.Seen.Count;
        Assert.True(first > 0);
        Assert.Equal(2, page.Catalog.Count);
        Assert.StartsWith("The catalog is verified: 2 widgets", page.CatalogStatus);

        page.CatalogOpen = false;
        page.CatalogOpen = true;
        await page.OpenCatalog();
        Assert.Equal(first, _net.Seen.Count);
        Assert.Equal(2, page.Catalog.Count);

        await page.CheckAgainCommand.ExecuteAsync(null);
        Assert.True(_net.Seen.Count > first);
    }

    [Fact]
    public async Task Opening_the_catalog_and_checking_again_together_fetch_once()
    {
        var single = Published();
        await single.OpenCatalog();
        int oneFetch = _net.Seen.Count;
        Assert.True(oneFetch > 0);
        _net.Seen.Clear();

        var gate = new GateHandler(_net);
        var page = Published(handler: gate);
        var open = page.OpenCatalog();
        var again = page.CheckAgainCommand.ExecuteAsync(null);   // while the first is still in flight: the guard hands back the running fetch
        gate.Release();
        await Task.WhenAll(open, again);

        Assert.Equal(oneFetch, _net.Seen.Count);
    }

    [Fact]
    public async Task An_installed_pack_appears_once_with_the_same_switch_as_its_installed_row()
    {
        InstalledPackKit.Install(Paths, "good-palette");
        var page = Published();
        page.CatalogOpen = true;
        await page.OpenCatalog();

        var row = page.Catalog.Single(r => r.Id == "good-palette");
        Assert.Equal((CatalogAction.Switch, true, false), (row.Action, row.IsSwitch, row.HasButton));
        Assert.True(row.Enabled);

        row.ToggleCommand.Execute(null);

        Assert.False(InstalledPackKit.Store(Paths).Load().Find("good-palette")!.Enabled);
        Assert.False(row.Enabled);
        Assert.False(page.Installed.Single(r => r.Id == "good-palette").Enabled);

        // And from the Installed row back: the catalog row follows.
        page.Installed.Single(r => r.Id == "good-palette").ToggleCommand.Execute(null);
        Assert.True(page.Catalog.Single(r => r.Id == "good-palette").Enabled);
    }

    [Fact]
    public async Task Install_from_the_catalog_turns_the_row_into_the_switch()
    {
        var page = Published();
        page.CatalogOpen = true;
        await page.OpenCatalog();
        var sprite = page.Catalog.Single(r => r.Id == "good-sprite");
        Assert.Equal((CatalogAction.Install, "Install Fixture sprite"), (sprite.Action, sprite.ActionName));

        await sprite.ActCommand.ExecuteAsync(null);

        Assert.Equal("Fixture sprite is installed.", page.Status);
        Assert.Equal(CatalogAction.Switch, page.Catalog.Single(r => r.Id == "good-sprite").Action);
        Assert.Contains(page.Installed, r => r.Id == "good-sprite");
    }

    [Fact]
    public async Task Offline_the_catalog_says_what_happened_and_the_installed_list_stays()
    {
        InstalledPackKit.Install(Paths, "good-palette");
        var page = Page();   // the fixture's offline network
        page.CatalogOpen = true;
        await page.OpenCatalog();
        Assert.Equal(WidgetCatalogClient.Offline, page.CatalogStatus);
        Assert.Equal("", page.Status);
        Assert.Empty(page.Catalog);
        Assert.Single(page.Installed, r => r.Id == "good-palette");
    }

    [Fact]
    public async Task The_companion_link_shows_sprites_until_every_pack_is_asked_for()
    {
        var page = Published();
        page.ShowSprites();
        await page.OpenCatalog();
        Assert.True(page.CatalogOpen);
        Assert.True(page.ShowingOneKind);
        Assert.Equal(SettingsNames.ShowingSprites, page.ShowingKindText);
        Assert.Equal(["good-sprite"], page.Catalog.Select(r => r.Id));

        page.ShowEveryPackCommand.Execute(null);
        Assert.Null(page.CatalogKind);
        Assert.False(page.ShowingOneKind);
        Assert.Equal(2, page.Catalog.Count);
    }

    [Fact]
    public void The_disclosure_sits_above_the_catalog_list_on_the_page()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanduhr.App", "Views", "ModsPage.xaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Sanduhr.App", "Views", "ModsPage.xaml"));
        int disclosure = xaml.IndexOf("SettingsNames.CatalogDisclosure", StringComparison.Ordinal);
        int list = xaml.IndexOf("ItemsSource=\"{Binding Catalog}\"", StringComparison.Ordinal);
        Assert.True(disclosure >= 0 && list >= 0);
        Assert.True(disclosure < list);
    }
}

/// <summary>The catalog opened in a real Settings window over a fake GitHub: each row shows exactly the one control that acts on it.</summary>
[Collection("SettingsWindow")]
public sealed class ModsPageCatalogWalkTests : AppWindowFixture
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeGitHub _net = new();

    [Fact]
    public void An_open_catalog_shows_no_control_that_cant_act_and_each_row_its_one_action() => AppHost.Run(() =>
    {
        InstalledPackKit.Install(new Paths(), "good-palette");   // its catalog row is the switch; the sprite's is Install
        var palette = WidgetKit.Zip(WidgetKit.Pack("good-palette"));
        var sprite = WidgetKit.Zip(WidgetKit.Pack("good-sprite"));
        WidgetKit.Publish(_net, _key, "2026-10-25T02:00:00Z",
            (WidgetKit.Entry("good-palette", "Fixture palette", "palette", "1.0.0", palette), palette),
            (WidgetKit.Entry("good-sprite", "Fixture sprite", "sprite", "1.0.0", sprite), sprite));
        var (widget, _) = ScratchWidget("Personal");
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        svm.ModsPage = new ModsPageViewModel(() => new WidgetCatalogService(new Paths(), _net, WidgetKit.V380, [WidgetKit.Spki(_key)]),
            null, ClaudeCodeHost.For(widget));
        var window = new SettingsWindow(svm);
        var guard = NoActivate.Show(window);
        try
        {
            window.SelectPage(SettingsPage.Mods, SettingsIndex.Catalog);
            var until = DateTime.UtcNow.AddSeconds(10);
            while ((svm.ModsPage.Busy || svm.ModsPage.Catalog.Count == 0) && DateTime.UtcNow < until) { Pump(); Thread.Sleep(15); }
            window.UpdateLayout();
            Pump();
            guard.Check();
            Assert.True(svm.ModsPage.CatalogOpen);
            Assert.Equal(2, svm.ModsPage.Catalog.Count);

            var catalog = DoneWalk.Below<Expander>(window).Single(e => e.Name == "CatalogSection");
            Assert.True(catalog.IsExpanded);
            Assert.Empty(DoneWalk.ControlsThatCantAct(catalog));

            var rows = DoneWalk.Below<ContentPresenter>(catalog).Where(p => p.Content is CatalogRowViewModel).ToList();
            Assert.Equal(2, rows.Count);
            foreach (var presenter in rows)
            {
                var vm = (CatalogRowViewModel)presenter.Content;
                var shown = DoneWalk.ShownNames(presenter);
                // A catalog row has no Uninstall: that lives on the Installed row. Install, Update or the switch, one of them.
                Assert.Equal([vm.HasButton ? vm.ActionName : vm.SwitchName], shown);
            }
            Assert.Contains(rows, p => ((CatalogRowViewModel)p.Content).IsSwitch);
            Assert.Contains(rows, p => ((CatalogRowViewModel)p.Content).Action == CatalogAction.Install);
        }
        finally { window.Close(); }
    });
}
