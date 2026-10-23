namespace Sanduhr.Core;

/// <summary>A fetch could not complete: offline, a timeout, an HTTP error, a redirect off the allowed hosts, or a body
/// over its size limit. The message is for the log only and never carries a URL with a query or a path from disk.</summary>
public sealed class WidgetFetchException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Fetches one URL's body, at most <c>maxBytes</c>. Returns null when the server says the file is not there (404),
/// which is how an absent <c>catalog.json.sig2</c> reads. Throws <see cref="WidgetFetchException"/> for everything else
/// that is not a body. The app's implementation is the only code that touches the network.
/// </summary>
public delegate Task<byte[]?> WidgetFetch(string url, long maxBytes, CancellationToken ct);

/// <summary>A catalog refresh: the verified catalog, or the sentence Settings shows.</summary>
public sealed record WidgetCatalogRefresh(WidgetCatalogDocument? Catalog, string? Error);

/// <summary>
/// The catalog client's steps in FORMAT 7's order, over an injected <see cref="WidgetFetch"/> (the app's HTTP, or a
/// test's dictionary) and a <see cref="WidgetStore"/>. Settings calls <see cref="RefreshAsync"/> when Settings ▸
/// Widgets opens and <see cref="InstallAsync"/> on Install or Update; nothing here runs on its own.
/// </summary>
public sealed class WidgetCatalogClient
{
    public const string Offline = "Sanduhr couldn't reach the widget catalog. Check your connection and try again.";
    public const string Unverified = "The widget catalog couldn't be verified.";
    public const string Unreadable = "The widget catalog couldn't be read.";
    public const string RolledBack = "The widget catalog is older than one Sanduhr has already seen, so it wasn't used.";
    public const string DownloadFailed = "Sanduhr couldn't download the widget. Check your connection and try again.";
    public const string DownloadMismatch = "The download didn't match the catalog, so it wasn't installed.";
    public const string NotInCatalog = "That widget isn't in the verified catalog.";

    private readonly WidgetFetch _fetch;
    private readonly WidgetStore _store;
    private readonly Version _running;
    private readonly IReadOnlyList<string> _keys;
    private readonly string _base;

    /// <param name="fetch">The network, or a test's stand-in.</param>
    /// <param name="store">The installed packs and the rollback marker.</param>
    /// <param name="running">The running Sanduhr version, for <c>sanduhr_min</c>.</param>
    /// <param name="pinnedKeys">The keys a catalog must verify against (the app passes <see cref="WidgetCatalog.PinnedKeys"/>).</param>
    /// <param name="baseUrl">Where catalog.json and its signatures are; defaults to the latest release.</param>
    public WidgetCatalogClient(WidgetFetch fetch, WidgetStore store, Version running, IReadOnlyList<string> pinnedKeys, string? baseUrl = null)
    {
        _fetch = fetch;
        _store = store;
        _running = running;
        _keys = pinnedKeys;
        _base = baseUrl ?? WidgetCatalog.LatestBase;
    }

    /// <summary>The last catalog that verified and parsed in this session, or null.</summary>
    public WidgetCatalogDocument? Current { get; private set; }

    public WidgetStore Store => _store;

    public Version Running => _running;

    /// <summary>Rules 1 and 2: fetch catalog.json and its signature files, verify before parsing, parse strictly, refuse a
    /// rollback, and remember the newest <c>generated_at</c>.</summary>
    public async Task<WidgetCatalogRefresh> RefreshAsync(CancellationToken ct = default)
    {
        byte[]? catalog, sig, sig2;
        try
        {
            catalog = await _fetch(_base + WidgetCatalog.CatalogFile, WidgetCatalog.MaxCatalogBytes, ct).ConfigureAwait(false);
            sig = await _fetch(_base + WidgetCatalog.SignatureFile, WidgetCatalog.MaxSignatureFileBytes, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WidgetFetchException or OperationCanceledException)
        {
            return new WidgetCatalogRefresh(null, Offline);
        }
        if (catalog is null)
            return new WidgetCatalogRefresh(null, Offline);
        try
        {
            // Present only during a key rotation; any failure to get it just means there is no second signature.
            sig2 = await _fetch(_base + WidgetCatalog.SecondSignatureFile, WidgetCatalog.MaxSignatureFileBytes, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WidgetFetchException or OperationCanceledException)
        {
            sig2 = null;
        }

        // Rule 1: nothing is parsed until a pinned key verifies the exact bytes.
        if (!WidgetCatalog.VerifySignature(catalog, [sig, sig2], _keys))
            return new WidgetCatalogRefresh(null, Unverified);
        var doc = WidgetCatalog.Parse(catalog, out _);
        if (doc is null)
            return new WidgetCatalogRefresh(null, Unreadable);

        // Rule 2.
        if (WidgetCatalog.IsRollback(doc.GeneratedAt, _store.NewestCatalogSeen()))
            return new WidgetCatalogRefresh(null, RolledBack);
        _store.RememberCatalog(doc.GeneratedAtText);
        Current = doc;
        return new WidgetCatalogRefresh(doc, null);
    }

    /// <summary>Rules 3 to 6 for one pack of <see cref="Current"/>: <c>sanduhr_min</c>, download, size and SHA-256
    /// before the zip is opened, safe extraction, strict local re-validation, atomic install.</summary>
    public async Task<WidgetStoreResult> InstallAsync(WidgetCatalogEntry entry, CancellationToken ct = default)
    {
        // Only a pack of the catalog that verified in this session.
        if (Current is null || entry is null || !Current.Packs.Contains(entry))
            return WidgetStoreResult.Failed(NotInCatalog);
        if (!WidgetCatalog.MeetsMinimum(entry.SanduhrMin, _running))
            return WidgetStoreResult.Failed($"{entry.Name} needs Sanduhr {entry.SanduhrMin} or later.");

        byte[]? zip;
        try
        {
            zip = await _fetch(entry.Url, entry.Size, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WidgetFetchException or OperationCanceledException)
        {
            return WidgetStoreResult.Failed(DownloadFailed);
        }
        if (zip is null)
            return WidgetStoreResult.Failed(DownloadFailed);
        if (WidgetCatalog.CheckDownload(entry, zip) is not null)
            return WidgetStoreResult.Failed(DownloadMismatch);

        var files = WidgetZip.Read(zip, out var zipError);
        if (files is null)
            return WidgetStoreResult.Failed($"{entry.Name} wasn't installed: its zip isn't safe to open ({zipError}).");

        var verdict = PackValidator.Validate(files, folderName: entry.Id, strict: true);
        if (!verdict.Ok || verdict.Manifest is not { } manifest)
            return WidgetStoreResult.Failed($"{entry.Name} wasn't installed: it didn't pass Sanduhr's checks ({verdict.Errors.FirstOrDefault()}).");
        if (manifest.Id != entry.Id || manifest.Version != entry.Version || manifest.Kind != entry.Kind)
            return WidgetStoreResult.Failed($"{entry.Name} wasn't installed: its files don't match the catalog.");
        if (!WidgetCatalog.MeetsMinimum(manifest.SanduhrMin, _running))
            return WidgetStoreResult.Failed($"{entry.Name} needs Sanduhr {manifest.SanduhrMin} or later.");

        return _store.Install(files, manifest, WidgetSource.Catalog);
    }
}
