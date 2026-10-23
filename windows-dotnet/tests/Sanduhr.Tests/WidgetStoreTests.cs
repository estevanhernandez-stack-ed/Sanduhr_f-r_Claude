using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>FORMAT 7 rules 5 and 6 and spec 3: the installed packs on disk, widgets.json, atomic install, uninstall,
/// the rollback marker, and the whole client flow over a fake fetch.</summary>
public sealed class WidgetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-widgets-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private WidgetStore Store() => new(Path.Combine(_root, "widgets"), WidgetTestKit.EncodePng,
        () => new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

    private static (Dictionary<string, byte[]> Files, PackManifest Manifest) Valid(string group, string name)
    {
        var files = WidgetPackFixtures.Load(group, name);
        var v = PackValidator.Validate(files, name, strict: true);
        Assert.True(v.Ok, string.Join(" | ", v.Errors));
        return (files, v.Manifest!);
    }

    private static Dictionary<string, byte[]> WithVersion(Dictionary<string, byte[]> files, string version)
    {
        var copy = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
        copy["pack.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["pack.json"]).Replace("\"1.0.0\"", $"\"{version}\""));
        return copy;
    }

    // ---- the store ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Install_writes_the_folder_and_the_row()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-palette");
        var r = store.Install(files, manifest, WidgetSource.Catalog);
        Assert.True(r.Ok, r.Error);

        string dir = store.PackDir("good-palette");
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).Order());
        Assert.Equal(files["palettes.json"], File.ReadAllBytes(Path.Combine(dir, "palettes.json")));

        var row = Assert.Single(store.Load().Packs);
        Assert.Equal(("good-palette", "1.0.0", WidgetSource.Catalog, true), (row.Id, row.Version, row.Source, row.Enabled));
        Assert.Equal("2026-10-22T12:00:00Z", WidgetRegistry.Stamp(row.InstalledAt));
        Assert.False(Directory.Exists(store.StagingDir) && Directory.EnumerateFileSystemEntries(store.StagingDir).Any());

        var json = File.ReadAllText(store.RegistryFile);
        Assert.Contains("\"source\": \"catalog\"", json);
        Assert.Contains("\"installed_at\": \"2026-10-22T12:00:00Z\"", json);

        var info = store.ReadInfo("good-palette")!;
        Assert.Equal(("Fixture palette", "palette"), (info.Name, info.Kind));
    }

    [Fact]
    public void Pixel_sheets_are_rasterized_to_png_at_install()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-sprite-pixel");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        var sheets = WidgetStore.PixelSheetsOf(files);
        Assert.NotEmpty(sheets);
        foreach (var sheet in sheets)
        {
            var png = File.ReadAllBytes(store.RasterPath("good-sprite-pixel", sheet));
            Assert.NotNull(PackValidator.ParsePng(png, out var why));
            Assert.Equal("", why ?? "");
        }
    }

    [Fact]
    public void Stages_mixing_a_pixel_sheet_and_a_png_rasterize_only_the_pixel_sheet()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-sprite-pixel-stages");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        Assert.Equal(["sprites/one.json"], WidgetStore.PixelSheetsOf(files));
        Assert.True(File.Exists(store.RasterPath("good-sprite-pixel-stages", "sprites/one.json")));
        Assert.True(File.Exists(Path.Combine(store.PackDir("good-sprite-pixel-stages"), "sprites", "two.png")));
        Assert.Single(Directory.GetFiles(Path.Combine(store.PackDir("good-sprite-pixel-stages"), WidgetStore.RasterName), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void An_update_from_the_same_source_replaces_the_pack()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-palette");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        File.WriteAllText(Path.Combine(store.PackDir("good-palette"), "stale.txt"), "x");

        var next = WithVersion(files, "1.1.0");
        var v = PackValidator.Validate(next, "good-palette", strict: true);
        Assert.True(store.Install(next, v.Manifest!, WidgetSource.Catalog).Ok);
        Assert.Equal("1.1.0", store.Load().Find("good-palette")!.Version);
        Assert.False(File.Exists(Path.Combine(store.PackDir("good-palette"), "stale.txt")));
    }

    [Fact]
    public void An_id_installed_from_another_source_is_refused()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-palette");
        Assert.True(store.Install(files, manifest, WidgetSource.Claude).Ok);
        var r = store.Install(files, manifest, WidgetSource.Catalog);
        Assert.False(r.Ok);
        Assert.Contains("made here", r.Error);
        Assert.Equal(WidgetSource.Claude, store.Load().Find("good-palette")!.Source);
    }

    [Fact]
    public void A_failure_before_the_swap_leaves_the_old_install_untouched()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-palette");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        var before = File.ReadAllText(store.RegistryFile);

        var next = WithVersion(files, "1.1.0");
        store.BeforeSwap = () => throw new IOException("disk full");
        var r = store.Install(next, PackValidator.Validate(next, "good-palette", strict: true).Manifest!, WidgetSource.Catalog);
        Assert.False(r.Ok);
        Assert.Equal(before, File.ReadAllText(store.RegistryFile));
        Assert.Contains("\"1.0.0\"", File.ReadAllText(Path.Combine(store.PackDir("good-palette"), "pack.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.StagingDir));
    }

    [Fact]
    public void A_failure_after_the_swap_puts_the_old_install_back()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-palette");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        var before = File.ReadAllText(store.RegistryFile);

        var next = WithVersion(files, "1.1.0");
        store.BeforeRegistryWrite = () => throw new IOException("locked");
        var r = store.Install(next, PackValidator.Validate(next, "good-palette", strict: true).Manifest!, WidgetSource.Catalog);
        Assert.False(r.Ok);
        Assert.DoesNotContain(@"\", r.Error);   // no path in the sentence
        Assert.Equal(before, File.ReadAllText(store.RegistryFile));
        Assert.Contains("\"1.0.0\"", File.ReadAllText(Path.Combine(store.PackDir("good-palette"), "pack.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.StagingDir));
    }

    [Fact]
    public void A_failed_first_install_leaves_nothing()
    {
        var store = new WidgetStore(Path.Combine(_root, "widgets"), _ => throw new NotSupportedException("no encoder"));
        var (files, manifest) = Valid("good", "good-sprite-pixel");
        var r = store.Install(files, manifest, WidgetSource.Catalog);
        Assert.False(r.Ok);
        Assert.False(Directory.Exists(store.PackDir("good-sprite-pixel")));
        Assert.Empty(store.Load().Packs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.StagingDir));
    }

    [Fact]
    public void Uninstall_removes_the_folder_and_the_row()
    {
        var store = Store();
        var (files, manifest) = Valid("good", "good-palette");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        Assert.True(store.Uninstall("good-palette").Ok);
        Assert.False(Directory.Exists(store.PackDir("good-palette")));
        Assert.Empty(store.Load().Packs);
        Assert.False(store.Uninstall("good-palette").Ok);
        Assert.False(store.Uninstall("../escape").Ok);
    }

    [Fact]
    public void The_rollback_marker_only_moves_forward_and_survives_installs()
    {
        var store = Store();
        Assert.Null(store.NewestCatalogSeen());
        store.RememberCatalog("2026-10-22T02:23:13Z");
        store.RememberCatalog("2026-10-01T00:00:00Z");
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 2, 23, 13, TimeSpan.Zero), store.NewestCatalogSeen());
        var (files, manifest) = Valid("good", "good-palette");
        Assert.True(store.Install(files, manifest, WidgetSource.Catalog).Ok);
        Assert.Equal("2026-10-22T02:23:13Z", store.Load().CatalogSeen);
    }

    [Fact]
    public void The_registry_reads_leniently()
    {
        Assert.Empty(WidgetRegistry.Parse("not json").Packs);
        var reg = WidgetRegistry.Parse("""
            {"schema_version":1,"catalog_generated_at":"2026-10-22T02:23:13Z","packs":[
              {"id":"good-one","version":"1.0.0","source":"local","installed_at":"2026-10-22T00:00:00Z","enabled":false},
              {"id":"../bad","version":"1.0.0","source":"catalog","installed_at":"2026-10-22T00:00:00Z"},
              {"id":"no-source","version":"1.0.0","installed_at":"2026-10-22T00:00:00Z"}
            ]}
            """);
        var row = Assert.Single(reg.Packs);
        Assert.Equal(("good-one", WidgetSource.Local, false, true), (row.Id, row.Source, row.Enabled, row.MadeHere));
        var back = WidgetRegistry.Parse(reg.ToJson());
        Assert.Equal(reg.CatalogSeen, back.CatalogSeen);
        Assert.Equal(reg.Packs, back.Packs);
    }

    // ---- the client, end to end over a fake fetch ------------------------------------------------------------------------------

    private sealed class FakeNet
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public readonly List<string> Asked = [];
        public bool Offline;

        public Task<byte[]?> Fetch(string url, long max, CancellationToken ct)
        {
            Asked.Add(url);
            if (Offline) throw new WidgetFetchException("offline");
            if (!Files.TryGetValue(url, out var b)) return Task.FromResult<byte[]?>(null);
            if (b.LongLength > max) throw new WidgetFetchException("too large");
            return Task.FromResult<byte[]?>(b);
        }
    }

    private (FakeNet Net, WidgetCatalogClient Client, WidgetCatalogEntry Entry, System.Security.Cryptography.ECDsa Key) Published(
        string generatedAt = "2026-10-22T02:23:13Z", Version? running = null)
    {
        var key = WidgetTestKit.NewKey();
        var zip = WidgetTestKit.ZipPack(WidgetPackFixtures.Load("good", "good-palette"));
        var entry = WidgetTestKit.EntryFor("good-palette", "Fixture palette", "palette", "1.0.0", zip);
        var catalog = WidgetTestKit.Catalog(generatedAt, entry);
        var net = new FakeNet();
        net.Files[WidgetCatalog.LatestBase + "catalog.json"] = catalog;
        net.Files[WidgetCatalog.LatestBase + "catalog.json.sig"] = WidgetTestKit.SigFile(key, catalog);
        net.Files[entry.Url] = zip;
        var client = new WidgetCatalogClient(net.Fetch, Store(), running ?? WidgetTestKit.V380, [WidgetTestKit.Spki(key)]);
        return (net, client, entry, key);
    }

    [Fact]
    public async Task Refresh_then_install_runs_every_rule_and_installs()
    {
        var (net, client, entry, key) = Published();
        using (key)
        {
            var refresh = await client.RefreshAsync();
            Assert.Null(refresh.Error);
            Assert.Equal(entry, Assert.Single(refresh.Catalog!.Packs));
            Assert.Contains(WidgetCatalog.LatestBase + "catalog.json.sig2", net.Asked);   // asked for, absent, fine

            var r = await client.InstallAsync(refresh.Catalog.Packs[0]);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(WidgetSource.Catalog, client.Store.Load().Find("good-palette")!.Source);
            Assert.Equal("2026-10-22T02:23:13Z", client.Store.Load().CatalogSeen);
        }
    }

    [Fact]
    public async Task Offline_is_a_plain_sentence()
    {
        var (net, client, _, key) = Published();
        using (key)
        {
            net.Offline = true;
            var r = await client.RefreshAsync();
            Assert.Equal(WidgetCatalogClient.Offline, r.Error);
            Assert.Null(client.Current);
        }
    }

    [Fact]
    public async Task A_bad_signature_is_the_verification_sentence_and_nothing_is_parsed_or_remembered()
    {
        var (net, client, _, key) = Published();
        using (key)
        {
            var url = WidgetCatalog.LatestBase + "catalog.json";
            var cat = net.Files[url];
            net.Files[url] = [.. cat[..^1], (byte)' ', (byte)'\n'];
            var r = await client.RefreshAsync();
            Assert.Equal("The widget catalog couldn't be verified.", r.Error);
            Assert.Null(client.Current);
            Assert.Null(client.Store.NewestCatalogSeen());

            net.Files.Remove(WidgetCatalog.LatestBase + "catalog.json.sig");
            Assert.Equal(WidgetCatalogClient.Unverified, (await client.RefreshAsync()).Error);
        }
    }

    [Fact]
    public async Task A_catalog_signed_only_in_sig2_by_a_pinned_key_verifies()
    {
        var (net, client, _, key) = Published();
        using (key)
        {
            var sig = net.Files[WidgetCatalog.LatestBase + "catalog.json.sig"];
            net.Files[WidgetCatalog.LatestBase + "catalog.json.sig"] = Encoding.ASCII.GetBytes("AAAA\n");
            net.Files[WidgetCatalog.LatestBase + "catalog.json.sig2"] = sig;
            Assert.Null((await client.RefreshAsync()).Error);
        }
    }

    [Fact]
    public async Task An_older_catalog_after_a_newer_one_is_a_rollback()
    {
        var (net, client, entry, key) = Published("2026-10-22T02:23:13Z");
        using (key)
        {
            Assert.Null((await client.RefreshAsync()).Error);
            var older = WidgetTestKit.Catalog("2026-10-21T00:00:00Z", entry);
            net.Files[WidgetCatalog.LatestBase + "catalog.json"] = older;
            net.Files[WidgetCatalog.LatestBase + "catalog.json.sig"] = WidgetTestKit.SigFile(key, older);
            Assert.Equal(WidgetCatalogClient.RolledBack, (await client.RefreshAsync()).Error);
            Assert.Equal("2026-10-22T02:23:13Z", client.Store.Load().CatalogSeen);
        }
    }

    [Fact]
    public async Task Sanduhr_min_above_the_running_version_is_refused_before_download()
    {
        var (net, client, entry, key) = Published(running: new Version(3, 7, 2, 0));
        using (key)
        {
            await client.RefreshAsync();
            var r = await client.InstallAsync(client.Current!.Packs[0]);
            Assert.False(r.Ok);
            Assert.Contains("needs Sanduhr 3.8.0", r.Error);
            Assert.DoesNotContain(entry.Url, net.Asked);
        }
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_catalog_is_refused_and_nothing_is_installed()
    {
        var (net, client, entry, key) = Published();
        using (key)
        {
            await client.RefreshAsync();
            var zip = net.Files[entry.Url];
            var other = (byte[])zip.Clone();
            other[^30] ^= 1;
            net.Files[entry.Url] = other;   // same size, different hash
            Assert.Equal(WidgetCatalogClient.DownloadMismatch, (await client.InstallAsync(client.Current!.Packs[0])).Error);
            net.Files[entry.Url] = [.. zip, 0];   // one byte more than the catalog's size
            Assert.Equal(WidgetCatalogClient.DownloadFailed, (await client.InstallAsync(client.Current!.Packs[0])).Error);
            Assert.Empty(client.Store.Load().Packs);
            Assert.False(Directory.Exists(client.Store.PackDir("good-palette")));
        }
    }

    [Fact]
    public async Task A_pack_whose_files_disagree_with_the_catalog_or_fail_validation_is_refused()
    {
        var key = WidgetTestKit.NewKey();
        using (key)
        {
            // The catalog says 1.0.1; the zip's pack.json says 1.0.0.
            var zip = WidgetTestKit.ZipPack(WidgetPackFixtures.Load("good", "good-palette"));
            var entry = WidgetTestKit.EntryFor("good-palette", "Fixture palette", "palette", "1.0.1", zip);
            // A pack that fails the validator (an unlisted file), signed and hashed correctly.
            var badFiles = WidgetPackFixtures.Load("bad", "bad-unlisted-file");
            var badZip = WidgetTestKit.ZipPack(badFiles);
            var bad = WidgetTestKit.EntryFor("bad-unlisted-file", "Bad", "palette", "1.0.0", badZip);
            var catalog = WidgetTestKit.Catalog("2026-10-22T02:23:13Z", bad, entry);
            var net = new FakeNet();
            net.Files[WidgetCatalog.LatestBase + "catalog.json"] = catalog;
            net.Files[WidgetCatalog.LatestBase + "catalog.json.sig"] = WidgetTestKit.SigFile(key, catalog);
            net.Files[entry.Url] = zip;
            net.Files[bad.Url] = badZip;
            var client = new WidgetCatalogClient(net.Fetch, Store(), WidgetTestKit.V380, [WidgetTestKit.Spki(key)]);
            Assert.Null((await client.RefreshAsync()).Error);

            Assert.Contains("don't match the catalog", (await client.InstallAsync(client.Current!.Packs.Single(p => p.Id == "good-palette"))).Error);
            Assert.Contains("didn't pass Sanduhr's checks", (await client.InstallAsync(client.Current!.Packs.Single(p => p.Id == "bad-unlisted-file"))).Error);
            Assert.Empty(client.Store.Load().Packs);
        }
    }

    [Fact]
    public async Task Only_a_pack_of_the_verified_catalog_installs()
    {
        var (net, client, entry, key) = Published();
        using (key)
        {
            Assert.Equal(WidgetCatalogClient.NotInCatalog, (await client.InstallAsync(entry)).Error);   // nothing verified yet
            await client.RefreshAsync();
            var forged = entry with { Sha256 = new string('0', 64) };
            Assert.Equal(WidgetCatalogClient.NotInCatalog, (await client.InstallAsync(forged)).Error);
            Assert.True((await client.InstallAsync(entry)).Ok);
        }
    }

    // ---- Reset ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Reset_lists_the_widgets_folder_with_the_themes()
    {
        var paths = new Paths(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"));
        Assert.DoesNotContain(ResetPlan.Inventory(paths)[ResetPart.Themes], i => i.Path == paths.WidgetsDir);
        Directory.CreateDirectory(Path.Combine(paths.WidgetsDir, "good-palette"));
        var themes = ResetPlan.Inventory(paths)[ResetPart.Themes];
        Assert.Contains(new ResetItem(paths.WidgetsDir, true), themes);
        Assert.Equal(Path.Combine(_root, "local", "Sanduhr", "widgets"), paths.WidgetsDir);
    }
}
