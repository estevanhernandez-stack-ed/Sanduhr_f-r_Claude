using System.IO;
using System.Windows;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The bridge with the Claude Code now-playing mod (spec, a look per song):
/// <list type="bullet">
/// <item><description><c>%USERPROFILE%\.sanduhr\now-playing-look.json</c> = <c>{schema_version, key, look: {ink, style, mood?}, source: saved|seeded}</c> for the song
/// playing now (never its title or artist: the key is enough for a mod that knows its own song). Written only while the MCP integration is installed
/// and "Style what's playing" is on; removed when nothing plays, when Style goes off, when the integration goes, at Reset and when Sanduhr quits.</description></item>
/// <item><description><c>now-playing-look-offer.json</c> = <c>{key, title, artist, look}</c>, which the mod writes when its model picked a look for a song Sanduhr
/// has none saved for: taken like a Claude proposal (a card, or saved directly with "Let Claude style songs directly").</description></item>
/// <item><description>While that file is wanted, Sanduhr listens to the Windows media session through the shared hub (the same read the Desk piece uses), so the
/// file is kept even when the Now playing piece is not placed.</description></item>
/// </list>
/// </summary>
internal sealed partial class NowPlayingLookService
{
    private FileSystemWatcher? _offerWatcher;
    private System.Windows.Threading.DispatcherTimer? _offerRetry;
    private string? _seen;
    private Func<NowPlayingInfo?>? _trackSource;

    /// <summary>The track that plays now (the hub's by default; tests set it).</summary>
    internal Func<NowPlayingInfo?> TrackSource
    {
        get => _trackSource ?? (() => _hub?.Shown);
        set => _trackSource = value;
    }

    partial void StartBridge()
    {
        if (_hub is not null) _hub.Changed += OnHubChanged;
        Handoff.OfferRetryWanted += OnOfferRetry;
        try
        {
            _offerWatcher = Watch(Handoff.Paths.Support, NowPlayingLookProposal.OfferFile);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _offerWatcher?.Dispose();
            _offerWatcher = null;
        }
        // SettingsStore.AnyWritten covers a change made elsewhere (the integration installed or removed, Reset's settings).
        SettingsStore.AnyWritten += OnSettingsWritten;
        SyncBridge();
    }

    partial void StopBridge()
    {
        SettingsStore.AnyWritten -= OnSettingsWritten;
        Handoff.OfferRetryWanted -= OnOfferRetry;
        _offerRetry?.Stop();
        _offerRetry = null;
        if (_hub is not null)
        {
            _hub.Changed -= OnHubChanged;
            _hub.Want(this, false);
        }
        _offerWatcher?.Dispose();
        _offerWatcher = null;
        // The file says what plays now; with the app gone it would be stale.
        Handoff.DeleteCurrent();
    }

    /// <summary>The mod writes its offer in place, so one may be half there when the watcher fires: look again shortly (one-shot; the handoff gives up after a few tries).</summary>
    private void OnOfferRetry(TimeSpan after)
    {
        if (_disposed) return;
        _offerRetry ??= new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background);
        _offerRetry.Stop();
        _offerRetry.Interval = after;
        _offerRetry.Tick -= OnOfferRetryTick;
        _offerRetry.Tick += OnOfferRetryTick;
        _offerRetry.Start();
    }

    private void OnOfferRetryTick(object? sender, EventArgs e)
    {
        _offerRetry?.Stop();
        if (!_disposed && !ResetGuard.Done) Handoff.Check();
    }

    private void OnSettingsWritten() => Application.Current?.Dispatcher.BeginInvoke(SyncBridge);

    private static string? Signature(NowPlayingInfo? t) => t is null ? null : $"{t.State}|{t.Title}|{t.Artist}|{t.AppId}";

    /// <summary>The media session reports often (the position moves); only a different song, state or app changes the file.</summary>
    private void OnHubChanged()
    {
        if (Signature(TrackSource()) != _seen) SyncBridge();
    }

    /// <summary>
    /// Brings <c>now-playing-look.json</c> and the media-session read in line with the switches: written for the playing song while
    /// Style is on and the MCP integration is installed; removed otherwise. Cheap when nothing changed (the file is rewritten only when its bytes do).
    /// </summary>
    partial void SyncBridge()
    {
        if (_disposed) return;
        _seen = Signature(TrackSource());
        var np = _store.LoadNowPlaying();
        bool wanted = np.Style && _store.LoadMcpEnabled() && !ResetGuard.Done;
        _hub?.Want(this, wanted);
        if (wanted && Resolve(np.Prefs.Visible(TrackSource())) is { } r)
            Handoff.WriteCurrent(r.Key, r.Look, r.Source);
        else
            Handoff.DeleteCurrent();
    }
}
