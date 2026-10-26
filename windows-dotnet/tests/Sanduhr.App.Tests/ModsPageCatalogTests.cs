using System.IO;
using System.Security.Cryptography;
using Sanduhr.App.ViewModels;
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

    private ModsPageViewModel Published(Version? running = null)
    {
        var palette = WidgetKit.Zip(WidgetKit.Pack("good-palette"));
        var sprite = WidgetKit.Zip(WidgetKit.Pack("good-sprite"));
        WidgetKit.Publish(_net, _key, "2026-10-25T02:00:00Z",
            (WidgetKit.Entry("good-palette", "Fixture palette", "palette", "1.0.0", palette), palette),
            (WidgetKit.Entry("good-sprite", "Fixture sprite", "sprite", "1.0.0", sprite), sprite));
        return new ModsPageViewModel(() => new Sanduhr.App.Services.WidgetCatalogService(Paths, _net, running ?? WidgetKit.V380, [WidgetKit.Spki(_key)]),
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
