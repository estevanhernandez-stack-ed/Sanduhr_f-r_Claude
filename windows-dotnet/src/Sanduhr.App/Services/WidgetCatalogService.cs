using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The widget catalog's network half (spec 3.8, section 2): the one place the app fetches <c>catalog.json</c>, its
/// signatures and pack zips, from <c>github.com/626Labs-LLC/sanduhr-widgets/releases</c>. It runs only when Settings ▸
/// Widgets opens and when the user installs or updates a pack; nothing polls. Never in the MCP process.
///
/// No cookies, no identifiers; the user agent is just "Sanduhr". Redirects are followed by hand, at most five, and only
/// over https to GitHub and the hosts GitHub serves release assets from (<see cref="WidgetCatalog.AllowedHosts"/>).
/// Every body has a size cap (1 MB for the catalog, 1 KB for a signature, the catalog's size for a zip) enforced while
/// reading, and every request a timeout. The verification, parsing and install are Core's
/// <see cref="WidgetCatalogClient"/>; this class only moves bytes, and encodes rasterized pixel sheets as PNG.
/// </summary>
internal sealed class WidgetCatalogService
{
    /// <summary>Tests and the render harness point the service at a fake network here. Null in the app.</summary>
    internal static Func<HttpMessageHandler>? HandlerOverride { get; set; }

    public const int MaxRedirects = 5;
    public static readonly TimeSpan SmallFileTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan ZipTimeout = TimeSpan.FromSeconds(90);

    private static readonly Lazy<HttpClient> SharedHttp = new(() => NewClient(NewHandler()));

    private readonly HttpClient _http;

    public WidgetCatalogService(Paths? paths = null, HttpMessageHandler? handler = null, Version? running = null,
        IReadOnlyList<string>? pinnedKeys = null)
    {
        var h = handler ?? HandlerOverride?.Invoke();
        _http = h is null ? SharedHttp.Value : NewClient(h);
        var store = new WidgetStore((paths ?? new Paths()).WidgetsDir, EncodePng);
        store.CleanStaging();
        Client = new WidgetCatalogClient(FetchAsync, store, running ?? RunningVersion(), pinnedKeys ?? WidgetCatalog.PinnedKeys);
    }

    public WidgetCatalogClient Client { get; }

    public WidgetStore Store => Client.Store;

    /// <summary>The app's version, major.minor.build, for <c>sanduhr_min</c>.</summary>
    public static Version RunningVersion() =>
        typeof(WidgetCatalogService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    private static HttpMessageHandler NewHandler() =>
        new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None };

    private static HttpClient NewClient(HttpMessageHandler handler)
    {
        var c = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(2) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(Weather.UserAgent);
        return c;
    }

    /// <summary>One body, at most <paramref name="maxBytes"/>; null on 404. See <see cref="WidgetFetch"/>.</summary>
    internal async Task<byte[]?> FetchAsync(string url, long maxBytes, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(maxBytes > WidgetCatalog.MaxCatalogBytes ? ZipTimeout : SmallFileTimeout);
        var token = timeout.Token;
        var uri = new Uri(url);
        try
        {
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!WidgetCatalog.IsAllowedUrl(uri))
                    throw new WidgetFetchException("refused a URL outside GitHub's release hosts");
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                int code = (int)response.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location ?? throw new WidgetFetchException("a redirect without a location");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;
                if (!response.IsSuccessStatusCode)
                    throw new WidgetFetchException("HTTP " + code);
                if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
                    throw new WidgetFetchException("the file is larger than allowed");
                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var body = new MemoryStream();
                var chunk = new byte[81920];
                int n;
                while ((n = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
                {
                    if (body.Length + n > maxBytes)
                        throw new WidgetFetchException("the file is larger than allowed");
                    body.Write(chunk, 0, n);
                }
                return body.ToArray();
            }
            throw new WidgetFetchException("too many redirects");
        }
        catch (HttpRequestException e)
        {
            throw new WidgetFetchException("the request failed", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new WidgetFetchException("the request timed out", e);
        }
        catch (IOException e)
        {
            throw new WidgetFetchException("the connection dropped", e);
        }
    }

    /// <summary>A rasterized pixel sheet as PNG, through WPF's <see cref="PngBitmapEncoder"/>. The sheet is straight
    /// RGBA; <see cref="PixelFormats.Bgra32"/> is straight (not premultiplied) BGRA, so only the channels swap.</summary>
    internal static byte[] EncodePng(PixelSheetImage image)
    {
        var bgra = new byte[image.Rgba.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = image.Rgba[i + 2];
            bgra[i + 1] = image.Rgba[i + 1];
            bgra[i + 2] = image.Rgba[i];
            bgra[i + 3] = image.Rgba[i + 3];
        }
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, bgra, image.Width * 4);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
