using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>A GitHub that lives in memory: routes by URL, 404 for the rest, and every request recorded.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public readonly Dictionary<string, Func<HttpResponseMessage>> Routes = new(StringComparer.Ordinal);
    public readonly List<HttpRequestMessage> Seen = [];

    public void Body(string url, byte[] body) => Routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>GitHub's own shape: the release URL redirects to an asset host, which serves the body.</summary>
    public void Redirected(string url, string assetHost, byte[] body)
    {
        string asset = $"https://{assetHost}/github-production-release-asset/{Guid.NewGuid():N}";
        Routes[url] = () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri(asset);
            return r;
        };
        Body(asset, body);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Seen.Add(request);
        var url = request.RequestUri!.AbsoluteUri;
        return Task.FromResult(Routes.TryGetValue(url, out var make) ? make() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>The App-side helpers for widget tests: fixture packs, a stored zip, a throwaway key and catalog bytes.</summary>
internal static class WidgetKit
{
    public static readonly Version V380 = new(3, 8, 0, 0);

    public static Dictionary<string, byte[]> Pack(string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "widget-packs", "good", name);
        return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
    }

    public static Dictionary<string, byte[]> WithVersion(Dictionary<string, byte[]> files, string version)
    {
        var copy = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
        copy["pack.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["pack.json"]).Replace("\"1.0.0\"", $"\"{version}\""));
        return copy;
    }

    /// <summary>Stored, sorted, regular files: the shape build_catalog.py writes.</summary>
    public static byte[] Zip(IReadOnlyDictionary<string, byte[]> files)
    {
        var body = new MemoryStream();
        var central = new MemoryStream();
        foreach (var (name, data) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var n = Encoding.ASCII.GetBytes(name);
            uint crc = Crc32(data);
            uint offset = (uint)body.Position;
            var h = new byte[30];
            W32(h, 0, 0x04034b50); W16(h, 4, 20); W16(h, 12, 0x21); W32(h, 14, crc); W32(h, 18, (uint)data.Length);
            W32(h, 22, (uint)data.Length); W16(h, 26, (ushort)n.Length);
            body.Write(h); body.Write(n); body.Write(data);
            var c = new byte[46];
            W32(c, 0, 0x02014b50); W16(c, 4, (3 << 8) | 20); W16(c, 6, 20); W16(c, 14, 0x21); W32(c, 16, crc);
            W32(c, 20, (uint)data.Length); W32(c, 24, (uint)data.Length); W16(c, 28, (ushort)n.Length);
            W32(c, 38, 0x81A4u << 16); W32(c, 42, offset);
            central.Write(c); central.Write(n);
        }
        uint cdStart = (uint)body.Position;
        body.Write(central.ToArray());
        var e = new byte[22];
        W32(e, 0, 0x06054b50); W16(e, 8, (ushort)files.Count); W16(e, 10, (ushort)files.Count);
        W32(e, 12, (uint)central.Length); W32(e, 16, cdStart);
        body.Write(e);
        return body.ToArray();
    }

    public static WidgetCatalogEntry Entry(string id, string name, string kind, string version, byte[] zip) =>
        new(id, name, kind, version, "3.8.0", $"{WidgetCatalog.DownloadPrefix}v{version}/{id}-{version}.zip",
            Convert.ToHexStringLower(SHA256.HashData(zip)), zip.LongLength);

    public static byte[] Catalog(string generatedAt, params WidgetCatalogEntry[] packs)
    {
        var sb = new StringBuilder("{\n  \"generated_at\": \"" + generatedAt + "\",\n  \"packs\": [");
        for (int i = 0; i < packs.Length; i++)
        {
            var p = packs[i];
            sb.Append(i == 0 ? "\n" : ",\n").Append("    {\n")
              .Append($"      \"id\": \"{p.Id}\",\n      \"kind\": \"{p.Kind}\",\n      \"name\": \"{p.Name}\",\n")
              .Append($"      \"sanduhr_min\": \"{p.SanduhrMin}\",\n      \"sha256\": \"{p.Sha256}\",\n      \"size\": {p.Size},\n")
              .Append($"      \"url\": \"{p.Url}\",\n      \"version\": \"{p.Version}\"\n    }}");
        }
        sb.Append(packs.Length == 0 ? "],\n" : "\n  ],\n").Append("  \"schema_version\": 1\n}\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] Sig(ECDsa key, byte[] catalog) => Encoding.ASCII.GetBytes(Convert.ToBase64String(
        key.SignData(catalog, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) + "\n");

    public static string Spki(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>Publishes a catalog of <paramref name="packs"/> on <paramref name="net"/>, signed by <paramref name="key"/>,
    /// with each zip behind GitHub's redirect.</summary>
    public static void Publish(FakeGitHub net, ECDsa key, string generatedAt, params (WidgetCatalogEntry Entry, byte[] Zip)[] packs)
    {
        var catalog = Catalog(generatedAt, [.. packs.Select(p => p.Entry).OrderBy(e => e.Id, StringComparer.Ordinal)]);
        net.Redirected(WidgetCatalog.LatestBase + "catalog.json", "release-assets.githubusercontent.com", catalog);
        net.Redirected(WidgetCatalog.LatestBase + "catalog.json.sig", "release-assets.githubusercontent.com", Sig(key, catalog));
        foreach (var (entry, zip) in packs)
            net.Redirected(entry.Url, "objects.githubusercontent.com", zip);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }

    private static void W16(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v);
    private static void W32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
}

/// <summary>
/// The app's half of the widget catalog: the HTTP fetch (GitHub's redirects followed by hand, other hosts refused, size
/// caps), WPF's PNG encoding of pixel sheets, and install, update and uninstall through the Settings page's view model.
/// All over <see cref="FakeGitHub"/> and a scratch LocalAppData: no real network, no real Sanduhr folder.
/// </summary>
public sealed class WidgetCatalogServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-widget-svc-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly FakeGitHub _net = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private Paths ScratchPaths() => new(Path.Combine(_root, "roaming"), Path.Combine(_root, "home"), Path.Combine(_root, "local"));

    private WidgetCatalogService Service(Version? running = null) =>
        new(ScratchPaths(), _net, running ?? WidgetKit.V380, [WidgetKit.Spki(_key)]);

    private static (WidgetCatalogEntry, byte[]) Packed(string fixture, string name, string kind, string version = "1.0.0")
    {
        var files = version == "1.0.0" ? WidgetKit.Pack(fixture) : WidgetKit.WithVersion(WidgetKit.Pack(fixture), version);
        var zip = WidgetKit.Zip(files);
        return (WidgetKit.Entry(fixture, name, kind, version, zip), zip);
    }

    [Fact]
    public async Task A_pixel_sprite_installs_over_GitHubs_redirects_and_its_sheet_is_a_WPF_png()
    {
        var sprite = Packed("good-sprite-pixel", "Fixture pixel sprite", "sprite");
        WidgetKit.Publish(_net, _key, "2026-10-22T02:23:13Z", sprite);
        var svc = Service();

        var refresh = await svc.Client.RefreshAsync();
        Assert.Null(refresh.Error);
        var result = await svc.Client.InstallAsync(refresh.Catalog!.Packs[0]);
        Assert.True(result.Ok, result.Error);

        // The row, the files, and the rasterized sheet, decoded by WPF at the size Core rasterized it.
        var row = Assert.Single(svc.Store.Load().Packs);
        Assert.Equal(("good-sprite-pixel", WidgetSource.Catalog), (row.Id, row.Source));
        Assert.StartsWith(Path.Combine(_root, "local", "Sanduhr", "widgets"), svc.Store.PackDir("good-sprite-pixel"));
        var files = WidgetKit.Pack("good-sprite-pixel");
        foreach (var sheet in WidgetStore.PixelSheetsOf(files))
        {
            var expected = PixelSheet.Rasterize(files[sheet], out _)!;
            using var png = File.OpenRead(svc.Store.RasterPath("good-sprite-pixel", sheet));
            var frame = new PngBitmapDecoder(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal((expected.Width, expected.Height), (frame.PixelWidth, frame.PixelHeight));
            // One opaque pixel reads back with the palette's color (BGRA from WPF, RGBA from Core).
            var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var bgra = new byte[expected.Width * expected.Height * 4];
            converted.CopyPixels(bgra, expected.Width * 4, 0);
            int at = Array.FindIndex(Enumerable.Range(0, expected.Rgba.Length / 4).ToArray(), i => expected.Rgba[i * 4 + 3] == 255) * 4;
            Assert.True(at >= 0);
            Assert.Equal((expected.Rgba[at], expected.Rgba[at + 1], expected.Rgba[at + 2], (byte)255), (bgra[at + 2], bgra[at + 1], bgra[at], bgra[at + 3]));
        }

        // Every request went to GitHub's hosts over https, with no cookie and Sanduhr's plain user agent.
        Assert.All(_net.Seen, r =>
        {
            Assert.True(WidgetCatalog.IsAllowedUrl(r.RequestUri), r.RequestUri!.ToString());
            Assert.False(r.Headers.Contains("Cookie"));
        });
        Assert.Contains(_net.Seen, r => r.RequestUri!.Host == "release-assets.githubusercontent.com");
        Assert.Contains(_net.Seen, r => r.RequestUri!.Host == "objects.githubusercontent.com");
    }

    [Fact]
    public async Task A_redirect_off_GitHub_is_never_followed()
    {
        _net.Routes[WidgetCatalog.LatestBase + "catalog.json"] = () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://evil.example/catalog.json");
            return r;
        };
        var refresh = await Service().Client.RefreshAsync();
        Assert.Equal(WidgetCatalogClient.Offline, refresh.Error);
        Assert.DoesNotContain(_net.Seen, r => r.RequestUri!.Host == "evil.example");

        _net.Routes[WidgetCatalog.LatestBase + "catalog.json"] = () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("http://github.com/catalog.json");
            return r;
        };
        Assert.Equal(WidgetCatalogClient.Offline, (await Service().Client.RefreshAsync()).Error);
    }

    [Fact]
    public async Task A_body_over_its_cap_is_cut_off()
    {
        var big = new byte[WidgetCatalog.MaxCatalogBytes + 10];
        _net.Routes[WidgetCatalog.LatestBase + "catalog.json"] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(big)),   // no Content-Length up front: the read cap catches it
        };
        Assert.Equal(WidgetCatalogClient.Offline, (await Service().Client.RefreshAsync()).Error);
    }

    [Fact]
    public async Task A_bad_signature_shows_the_verification_line_on_the_page()
    {
        var palette = Packed("good-palette", "Fixture palette", "palette");
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        WidgetKit.Publish(_net, stranger, "2026-10-22T02:23:13Z", palette);
        var page = new ModsPageViewModel(() => Service());
        await page.Activate();
        Assert.Equal("The widget catalog couldn't be verified.", page.Status);
        Assert.Empty(page.Catalog);
        Assert.DoesNotContain(_net.Seen, r => r.RequestUri!.AbsoluteUri == palette.Item1.Url);
    }

    [Fact]
    public async Task Offline_shows_a_plain_line_and_installed_packs_still_list()
    {
        var svc = Service();
        var files = WidgetKit.Pack("good-palette");
        var v = PackValidator.Validate(files, "good-palette", strict: true);
        Assert.True(svc.Store.Install(files, v.Manifest!, WidgetSource.Claude).Ok);
        var page = new ModsPageViewModel(() => svc);
        await page.Activate();
        Assert.Equal(WidgetCatalogClient.Offline, page.Status);
        var row = Assert.Single(page.Installed);
        Assert.Equal(("Fixture palette", SettingsNames.WidgetMadeHere), (row.Name, row.Source));
    }

    [Fact]
    public async Task Install_update_and_uninstall_from_the_page_and_a_bad_update_leaves_the_old_version()
    {
        var v1 = Packed("good-palette", "Fixture palette", "palette");
        var effects = Packed("good-effects", "Fixture effects", "effects");
        WidgetKit.Publish(_net, _key, "2026-10-22T02:23:13Z", v1, effects);
        var svc = Service();
        var page = new ModsPageViewModel(() => svc);
        page.Confirm = (_, _) => true;
        await page.Activate();
        Assert.StartsWith("The catalog is verified: 2 widgets", page.Status);

        var paletteRow = page.Catalog.Single(r => r.Entry.Id == "good-palette");
        Assert.Equal((WidgetRowAction.Install, "Install Fixture palette"), (paletteRow.Action, paletteRow.ActionName));
        await paletteRow.ActCommand.ExecuteAsync(null);
        Assert.Equal("Fixture palette is installed.", page.Status);
        Assert.Equal("Installed.", page.Catalog.Single(r => r.Entry.Id == "good-palette").State);
        Assert.Equal(WidgetRowAction.None, page.Catalog.Single(r => r.Entry.Id == "good-palette").Action);
        Assert.Equal("Uninstall Fixture palette", Assert.Single(page.Installed).UninstallName);

        // 1.1.0 is published, but its zip on the server is not the one the catalog hashed.
        var v2 = Packed("good-palette", "Fixture palette", "palette", "1.1.0");
        WidgetKit.Publish(_net, _key, "2026-10-23T00:00:00Z", v2, effects);
        var tampered = (byte[])v2.Item2.Clone();
        tampered[^40] ^= 1;
        _net.Redirected(v2.Item1.Url, "objects.githubusercontent.com", tampered);
        await page.CheckAgainCommand.ExecuteAsync(null);
        var update = page.Catalog.Single(r => r.Entry.Id == "good-palette");
        Assert.Equal((WidgetRowAction.Update, "Update Fixture palette to 1.1.0"), (update.Action, update.ActionName));
        await update.ActCommand.ExecuteAsync(null);
        Assert.Equal(WidgetCatalogClient.DownloadMismatch, page.Status);
        Assert.Equal("1.0.0", svc.Store.Load().Find("good-palette")!.Version);
        Assert.Contains("\"1.0.0\"", File.ReadAllText(Path.Combine(svc.Store.PackDir("good-palette"), "pack.json")));

        // The real 1.1.0 zip updates it.
        _net.Redirected(v2.Item1.Url, "objects.githubusercontent.com", v2.Item2);
        await page.Catalog.Single(r => r.Entry.Id == "good-palette").ActCommand.ExecuteAsync(null);
        Assert.Equal("Fixture palette is updated to 1.1.0.", page.Status);
        Assert.Equal("1.1.0", svc.Store.Load().Find("good-palette")!.Version);

        await page.Installed.Single().UninstallCommand.ExecuteAsync(null);
        Assert.Equal("Fixture palette is uninstalled.", page.Status);
        Assert.Empty(page.Installed);
        Assert.False(Directory.Exists(svc.Store.PackDir("good-palette")));
        Assert.Equal(WidgetRowAction.Install, page.Catalog.Single(r => r.Entry.Id == "good-palette").Action);
    }

    [Fact]
    public async Task A_pack_above_the_running_version_has_no_install_button()
    {
        var palette = Packed("good-palette", "Fixture palette", "palette");
        WidgetKit.Publish(_net, _key, "2026-10-22T02:23:13Z", palette);
        var page = new ModsPageViewModel(() => Service(new Version(3, 7, 2, 0)));
        await page.Activate();
        var row = Assert.Single(page.Catalog);
        Assert.Equal((WidgetRowAction.None, "Needs Sanduhr 3.8.0 or later."), (row.Action, row.State));
    }
}
