using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings ▸ Mods &amp; Widgets ▸ Install widget from file… (#408): the picked zip installs, refuses or is cancelled, and says so.</summary>
[Collection("SettingsWindow")]
public sealed class WidgetFromFilePageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-wfpage-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public WidgetFromFilePageTests()
    {
        _paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"), Path.Combine(_root, "home", ".sanduhr"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private WidgetStore Installed => new(_paths.WidgetsDir, WidgetCatalogService.EncodePng);

    private string ZipFile(string name, Dictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, WidgetKit.Zip(files));
        return path;
    }

    [Fact]
    public void A_picked_pack_installs_lists_as_made_here_and_reloads_the_Desk()
    {
        Sta.Run(() =>
        {
            string zip = ZipFile("palette.zip", WidgetKit.Pack("good-palette"));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            int changed = 0;
            page.Changed = () => changed++;
            page.PickZip = () => zip;

            page.InstallFromFileCommand.Execute(null);

            Assert.Equal("Fixture palette is installed from your file. It stays on this PC.", page.Status);
            Assert.Equal(1, changed);
            Assert.Equal(WidgetSource.Local, Installed.Load().Find("good-palette")!.Source);
            Assert.Contains(page.Installed, r => r.Id == "good-palette");
        });
    }

    [Fact]
    public void Streamer_mode_shows_the_rows_codename_in_the_status_line_not_the_packs_name()
    {
        Sta.Run(() =>
        {
            try
            {
                StreamerMode.Use(new StreamerMask(true, CodenameBook.New(), _root));
                string zip = ZipFile("palette.zip", WidgetKit.Pack("good-palette"));
                var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
                page.PickZip = () => zip;

                page.InstallFromFileCommand.Execute(null);

                string codename = StreamerMode.Mask.Name(CodenameCategory.AddOn, "Fixture palette");
                Assert.NotEqual("Fixture palette", codename);
                Assert.DoesNotContain("Fixture palette", page.Status);
                Assert.Equal($"{codename} is installed from your file. It stays on this PC.", page.Status);
            }
            finally { StreamerMode.Reset(); }
        });
    }

    [Theory]
    [InlineData(true, "companion:bl", "Fixture sprite is on your desk now. It came from your file and stays on this PC.", "")]
    [InlineData(false, "companion:bl", "Fixture sprite is your companion now. It lives on the Desk, the layer Sanduhr draws on your desktop, and the Desk is off.", "Turn on the Desk")]
    [InlineData(true, "clock:bl", "Fixture sprite is your companion now, but the Companion piece is hidden on the Desk.", "Show it on the Desk")]
    public void A_sprite_from_a_file_becomes_the_companion_and_the_line_says_where_it_went(bool deskOn, string layout, string expected, string fix)
    {
        Sta.Run(() =>
        {
            _store.SaveDesk(DeskSettings.Default with { Enabled = deskOn, Layout = layout });
            _store.SaveCompanion(new CompanionSettings("an-older-pick", Roam: false));
            string zip = ZipFile("sprite.zip", WidgetKit.Pack("good-sprite"));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            page.PickZip = () => zip;

            page.InstallFromFileCommand.Execute(null);

            Assert.Equal(expected, page.Status);
            Assert.Equal(fix, page.CompanionFix);
            Assert.Equal(new CompanionSettings("good-sprite", Roam: false), _store.LoadCompanion());
            Assert.Equal(layout, _store.LoadDesk().Layout);   // the choice is written, the layout is left to the Desk tab
        });
    }

    [Fact]
    public void One_click_turns_the_Desk_on_and_shows_the_piece_then_the_button_goes()
    {
        Sta.Run(() =>
        {
            _store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
            string zip = ZipFile("sprite.zip", WidgetKit.Pack("good-sprite"));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            page.PickZip = () => zip;
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal("Turn on the Desk and show it", page.CompanionFix);

            page.FixCompanionCommand.Execute(null);
            Assert.True(_store.LoadDesk().Enabled);
            Assert.NotNull(new DeskArrangement(_store.LoadDesk().Layout).Placement(CompanionSettings.Widget));
            Assert.Equal("Fixture sprite is on your desk now. It came from your file and stays on this PC.", page.Status);
            Assert.Equal("", page.CompanionFix);

            // Any other line takes the button away.
            _store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal("Turn on the Desk and show it", page.CompanionFix);
            page.PickZip = () => null;
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal("", page.CompanionFix);
        });
    }

    [Fact]
    public void Settings_routes_the_button_through_its_Desk_tab()
    {
        Sta.Run(() =>
        {
            _store.SaveDesk(DeskSettings.Default with { Enabled = false, Layout = "clock:bl" });
            string zip = ZipFile("sprite.zip", WidgetKit.Pack("good-sprite"));
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            int on = 0;
            page.TurnOnDesk = () => { on++; _store.SaveDesk(_store.LoadDesk() with { Enabled = true }); };
            page.PickZip = () => zip;
            page.InstallFromFileCommand.Execute(null);

            int shown = 0;
            page.ShowCompanionPiece = () =>
            {
                shown++;
                var desk = _store.LoadDesk();
                var layout = new DeskArrangement(desk.Layout);
                layout.PlaceAtDefault(CompanionSettings.Widget);
                _store.SaveDesk(desk with { Layout = layout.Layout });
            };
            page.FixCompanionCommand.Execute(null);

            Assert.Equal(1, on);
            Assert.Equal(1, shown);
            Assert.Equal("", page.CompanionFix);
        });
    }

    [Fact]
    public void An_updated_sprite_says_its_version_and_a_palette_leaves_the_companion_alone()
    {
        Sta.Run(() =>
        {
            _store.SaveDesk(DeskSettings.Default with { Enabled = true, Layout = "companion:bl" });
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            var sprite = WidgetKit.Pack("good-sprite");
            string first = ZipFile("sprite.zip", sprite);
            page.PickZip = () => first;
            page.InstallFromFileCommand.Execute(null);

            var newer = new Dictionary<string, byte[]>(sprite, StringComparer.Ordinal);
            newer["pack.json"] = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(sprite["pack.json"]).Replace("\"1.0.0\"", "\"1.1.0\""));
            string second = ZipFile("sprite-1.1.zip", newer);
            page.PickZip = () => second;
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal("Fixture sprite is updated to 1.1.0 and on your desk now. It stays on this PC.", page.Status);

            string palette = ZipFile("palette.zip", WidgetKit.Pack("good-palette"));
            page.PickZip = () => palette;
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal("Fixture palette is installed from your file. It stays on this PC.", page.Status);
            Assert.Equal("good-sprite", _store.LoadCompanion().Pack);
        });
    }

    [Fact]
    public void A_broken_pack_cancel_or_missing_file_installs_nothing_and_says_why()
    {
        Sta.Run(() =>
        {
            var page = new ModsPageViewModel(() => new WidgetCatalogService(_paths), _store);
            int changed = 0;
            page.Changed = () => changed++;

            var noLicense = WidgetKit.Pack("good-palette");
            noLicense.Remove("LICENSE");
            string bad = ZipFile("bad.zip", noLicense);
            page.PickZip = () => bad;
            page.InstallFromFileCommand.Execute(null);
            Assert.StartsWith("Nothing was installed: the pack didn't pass Sanduhr's checks.", page.Status);
            Assert.Contains("LICENSE", page.Status);

            page.PickZip = () => null;
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal(SettingsNames.FileNoneChosen, page.Status);

            page.PickZip = () => Path.Combine(_root, "gone.zip");
            page.InstallFromFileCommand.Execute(null);
            Assert.Equal(SettingsNames.FileUnreadable, page.Status);

            Assert.Equal(0, changed);
            Assert.Empty(Installed.Load().Packs);
        });
    }
}
