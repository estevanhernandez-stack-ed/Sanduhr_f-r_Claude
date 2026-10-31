using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What a widget under review looks like on the Desk: the pack, and for a sprite the staged copy the companion draws from.</summary>
internal sealed record WidgetPreview(WidgetProposalPack Pack, WidgetStore? Staged, string? Ink);

/// <summary>
/// What the widget review service needs from the running app (spec 3.8 section 5): the Desk drawing a pack from memory and its
/// staged copy, the floating bar, and the follow-up after an install. The App's <see cref="WidgetReviewHost"/> is the real one;
/// tests use a fake.
/// </summary>
internal interface IWidgetReviewHost
{
    /// <summary>Previews the pack on the Desk (nothing installed) and shows the bar with <paramref name="model"/>. Apply passes
    /// whether the bar's option ("Make it the companion") is checked.</summary>
    void ShowPreview(WidgetPreview preview, DeskReviewModel model, Action<bool> apply, Action dismiss);

    /// <summary>Puts the Desk back as it was and takes the bar down.</summary>
    void EndPreview();

    /// <summary>A pack was installed: the bar goes, the Desk and Settings, Mods & Widgets follow.</summary>
    void Installed(string packId);

    /// <summary>A Desk design is on the desktop for review; a widget waits until it is applied or dismissed.</summary>
    bool DeskBusy { get; }
}

/// <summary>
/// Widgets made with Claude, the app side (spec 3.8 section 5). Watches <c>%USERPROFILE%\.sanduhr</c> for <c>widget-request.json</c>
/// and hands it to Core's <see cref="WidgetHandoff"/>, which checks the pack again with the widget's own rules (the server is never
/// trusted). A good pack is staged for the preview (a sprite is installed into <c>widgets\.preview\</c>, where its pixel sheets are
/// rasterized, and drawn as the companion from there; a palette becomes the Desk ink for the review) and answered <c>previewing</c>.
/// <list type="bullet">
/// <item><description><b>Apply</b> installs it into the widgets folder as <see cref="WidgetSource.Claude"/> (local, unsigned, listed
/// "made here"), and for a sprite, with "Make it the companion" checked, makes it the companion and shows the piece. Answered
/// <c>applied</c>.</description></item>
/// <item><description><b>Dismiss</b> installs nothing and removes the staged copy. Answered <c>rejected</c>, reason <c>dismissed</c>.</description></item>
/// </list>
/// "Let Claude change the Desk directly" never covers a widget: every pack waits for Apply. A newer proposal replaces one under
/// review (answered <c>replaced</c>). Nothing here is sent anywhere.
/// </summary>
internal sealed class WidgetReviewService : IDisposable
{
    /// <summary>On from the build that ships propose_widget. A kill switch for a build that must not watch the folder.</summary>
    public const bool Enabled = true;

    /// <summary>The staging folder inside the widgets folder: Reset removes it with the widgets, and it is cleared at start.</summary>
    public const string PreviewFolder = ".preview";

    /// <summary>The app's one service; null in tests and the harness.</summary>
    public static WidgetReviewService? Current { get; set; }

    private sealed record Review(string Id, WidgetProposalPack Pack, string? Note);

    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private readonly Dispatcher _ui;
    private FileSystemWatcher? _watcher;
    private Review? _review;
    private bool _started;
    private bool _disposed;

    public WidgetReviewService(Paths paths, SettingsStore store, Dispatcher? ui = null, Func<DateTimeOffset>? clock = null)
    {
        _paths = paths;
        _store = store;
        _ui = ui ?? Dispatcher.CurrentDispatcher;
        Handoff = new WidgetHandoff(new WidgetHandoffPaths(paths.ExternalDir), clock) { Handler = OnProposal };
    }

    /// <summary>The request and result files.</summary>
    public WidgetHandoff Handoff { get; }

    /// <summary>The Desk and bar side of a review; null where there is none (tests, the harness): a pack then answers <c>not_ready</c>.</summary>
    public IWidgetReviewHost? Host { get; set; }

    /// <summary>Encodes a rasterized pixel sheet as PNG (tests pass their own; the app uses WPF's encoder).</summary>
    internal Func<PixelSheetImage, byte[]> EncodePng { get; set; } = WidgetCatalogService.EncodePng;

    /// <summary>A pack was installed (Settings, Mods & Widgets and the companion follow).</summary>
    public event Action? Installed;

    /// <summary>A pack is on the desktop waiting for Apply or Dismiss.</summary>
    public bool Reviewing => _review is not null;

    /// <summary>The pack under review's id (tests and the harness read it).</summary>
    internal string? ReviewingId => _review?.Pack.Id;

    /// <summary>Where a pack under review is staged.</summary>
    internal string PreviewRoot => Path.Combine(_paths.WidgetsDir, PreviewFolder);   // streamer: notname - a file location used for IO, never drawn

    public void Start()
    {
        if (!Enabled || _started || _disposed) return;
        _started = true;
        DeletePreview();   // a staged copy an earlier run left behind
        var dir = Handoff.Paths.Support;
        try
        {
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, WidgetProposal.RequestFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };   // streamer: notname - NotifyFilters.FileName is a watcher flag, not a name
            _watcher.Created += (_, _) => Check();
            _watcher.Changed += (_, _) => Check();
            _watcher.Renamed += (_, _) => Check();
            _watcher.Error += (_, _) => Check();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // No watcher: a request waits (for up to its ten minutes) until the next launch picks it up.
            _watcher?.Dispose();
            _watcher = null;
        }
        Check();
    }

    private void Check()
    {
        if (_disposed) return;
        _ui.BeginInvoke(() =>
        {
            if (!_disposed && !ResetGuard.Done) Handoff.Check();
        });
    }

    // -- the review ---------------------------------------------------------------------------------------------------

    /// <summary>A pack that passed the widget's own check (the handoff's handler): staged and previewed, never installed here.
    /// The direct switch is deliberately not read: widgets always wait for Apply.</summary>
    private WidgetAnswer OnProposal(string id, WidgetProposalPack pack, string? note)
    {
        try { return OnProposalCore(id, pack, note); }
        catch (Exception e) when (IsRecoverable(e))
        {
            Log("proposal", e);
            DeletePreview();
            return WidgetAnswer.Refused([new DeskReason("widget", "could not be shown (" + e.GetType().Name + "); nothing was installed")]);
        }
    }

    private WidgetAnswer OnProposalCore(string id, WidgetProposalPack pack, string? note)
    {
        if (ResetGuard.Done || _disposed || Host is not { } host) return WidgetAnswer.NotReady;
        if (host.DeskBusy)
            return new WidgetAnswer(WidgetAnswer.Rejected, "busy",
                [new DeskReason("widget", "a Desk design or a drawers plan is waiting for the user's review; ask them to Apply or Dismiss it, then propose the widget again")],
                PackId: pack.Id);

        ReplaceReview();
        WidgetStore? staged = null;
        if (pack.Kind == "sprite")
        {
            DeletePreview();
            staged = new WidgetStore(PreviewRoot, EncodePng);
            var stagedResult = staged.Install(pack.Files, pack.Manifest, WidgetSource.Claude);
            if (!stagedResult.Ok)
            {
                DeletePreview();
                return WidgetAnswer.Refused([new DeskReason("widget", "could not be staged for the preview: " + stagedResult.Error)]);
            }
        }

        _review = new Review(id, pack, note);
        var model = WidgetReview.Model(pack, note, _store.LoadDesk().Enabled);
        try { host.ShowPreview(new WidgetPreview(pack, staged, WidgetReview.PreviewInk(pack)), model, ApplyReview, DismissReview); }
        catch (Exception e) when (IsRecoverable(e)) { Log("preview", e); }
        return new WidgetAnswer(WidgetAnswer.Previewing, PackId: pack.Id);
    }

    /// <summary>A newer proposal takes the place of the one on the desktop: the older one is answered rejected.</summary>
    private void ReplaceReview()
    {
        if (_review is not { } old) return;
        _review = null;
        HostEnd();
        DeletePreview();
        Answer(old.Id, new WidgetAnswer(WidgetAnswer.Rejected, "replaced", [new DeskReason("widget", "replaced by a newer proposal")], old.Pack.Id));
    }

    /// <summary>Dismiss (the button, Escape, or the bar closed): nothing is installed and the staged copy goes; the caller is told.</summary>
    public void DismissReview()
    {
        if (_review is not { } r) return;
        _review = null;
        HostEnd();
        DeletePreview();
        Answer(r.Id, new WidgetAnswer(WidgetAnswer.Rejected, "dismissed", [new DeskReason("widget", "dismissed by the user")], r.Pack.Id));
    }

    /// <summary>Apply (the button, or Enter): installs the pack as a widget made with Claude and, for a sprite with
    /// <paramref name="makeCompanion"/>, makes it the companion. The caller is told either way.</summary>
    public void ApplyReview(bool makeCompanion)
    {
        if (_review is not { } r) return;
        _review = null;
        WidgetAnswer answer;
        try { answer = Install(r.Pack, makeCompanion); }
        catch (Exception e) when (IsRecoverable(e))
        {
            Log("apply", e);
            answer = WidgetAnswer.Refused([new DeskReason("widget", "could not be installed (" + e.GetType().Name + ")")]);
        }
        HostEnd();
        DeletePreview();
        if (answer.Status == WidgetAnswer.Applied)
        {
            try { Host?.Installed(r.Pack.Id); }
            catch (Exception e) when (IsRecoverable(e)) { Log("refresh", e); }
            Installed?.Invoke();
        }
        Answer(r.Id, answer);
    }

    private WidgetAnswer Install(WidgetProposalPack pack, bool makeCompanion)
    {
        // The pack is checked once more against the folder it goes into, then installed atomically.
        var check = WidgetProposal.Check(pack.Files, pack.Id);
        if (check.Pack is null) return WidgetAnswer.Refused(check.Reasons);
        var store = new WidgetStore(_paths.WidgetsDir, EncodePng);   // streamer: notname - a file location used for IO, never drawn
        var result = store.Install(pack.Files, pack.Manifest, WidgetSource.Claude);
        if (!result.Ok)
            return new WidgetAnswer(WidgetAnswer.Rejected, "install_failed", [new DeskReason("widget", result.Error ?? "could not be installed")], pack.Id);

        bool companion = false;
        if (pack.Kind == "sprite" && makeCompanion)
        {
            _store.SaveCompanion(_store.LoadCompanion() with { Pack = pack.Id });
            var desk = _store.LoadDesk();
            var layout = new DeskArrangement(desk.Layout);
            if (layout.PlaceAtDefault(CompanionSettings.Widget) is not null)
                _store.SaveDesk(desk with { Layout = layout.Layout });
            companion = true;
        }
        return new WidgetAnswer(WidgetAnswer.Applied, PackId: pack.Id, Companion: pack.Kind == "sprite" ? companion : null);
    }

    private void Answer(string id, WidgetAnswer answer)
    {
        if (_disposed || ResetGuard.Done) return;
        Handoff.Answer(id, answer);
    }

    private void HostEnd()
    {
        try { Host?.EndPreview(); }
        catch (Exception e) when (IsRecoverable(e)) { Log("end preview", e); }
    }

    /// <summary>Removes the staged copy (best effort; Reset and the next start clear what is left).</summary>
    private void DeletePreview()
    {
        try
        {
            if (!Directory.Exists(PreviewRoot)) return;
            foreach (var f in Directory.EnumerateFiles(PreviewRoot, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(PreviewRoot, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsRecoverable(Exception e) => e is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    /// <summary>One line in the log: the operation and the exception type, never pack content (PRIVACY.md).</summary>
    private void Log(string operation, Exception e)
    {
        try { File.AppendAllText(_paths.LogFile, $"{DateTime.UtcNow:o} widget review {operation} failed ({e.GetType().Name}){Environment.NewLine}"); }   // streamer: notname - written to the log file, never drawn
        catch (Exception) { /* the log is a courtesy */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        if (_review is not null) HostEnd();
        _review = null;
        DeletePreview();
        (Host as IDisposable)?.Dispose();
    }
}

/// <summary>
/// The running app's side of a widget review (spec 3.8 section 5): a sprite is drawn as the Desk companion from its staged copy
/// (<see cref="CompanionService.PreviewPack"/>, the companion piece placed for the preview when it is hidden), a palette as the
/// Desk ink, effects on the Desk as it is; the floating <see cref="DeskReviewWindow"/> wears the widget's words. Nothing is saved
/// until Apply.
/// </summary>
internal sealed class WidgetReviewHost : IWidgetReviewHost, IDisposable
{
    private readonly DeskController _desk;
    private readonly SettingsStore _store;
    private readonly Action _settingsFollow;
    private DeskReviewWindow? _window;
    private bool _ownsDeskPreview;
    private Action<bool> _apply = _ => { };
    private Action _dismiss = () => { };

    public WidgetReviewHost(DeskController desk, SettingsStore store, Action settingsFollow)
    {
        _desk = desk;
        _store = store;
        _settingsFollow = settingsFollow;
    }

    /// <summary>A drawers plan is under review (one review at a time); a seam so a test never sets the process-wide static.</summary>
    internal Func<bool> DrawersReviewing { get; set; } = () => DrawerReviewService.Current?.Reviewing == true;

    public bool DeskBusy => DeskDesignService.Current?.Reviewing == true || DrawersReviewing();

    public void ShowPreview(WidgetPreview preview, DeskReviewModel model, Action<bool> apply, Action dismiss)
    {
        _apply = apply;
        _dismiss = dismiss;
        var settings = PreviewSettings(preview);
        if (preview.Staged is { } staged) _desk.Companion?.PreviewPack(staged, preview.Pack.Id);
        _desk.ShowPreview(settings);   // full: a look the user asked to see shows in full, like a design, even with desktop off
        _ownsDeskPreview = true;

        if (_window is null)
        {
            _window = new DeskReviewWindow();
            _window.ApplyRequested += () => _apply(_window?.ReviewBar.OptionChecked == true);
            _window.DismissRequested += () => _dismiss();
            _window.Closed += (s, _) =>
            {
                if (!ReferenceEquals(_window, s)) return;
                _window = null;
                _dismiss();
            };
            _window.SetModel(model);
            _window.Target = _desk.ReviewScreen(settings);
            _window.Show();
            _window.Activate();
        }
        else
        {
            _window.SetModel(model);
            _window.Target = _desk.ReviewScreen(settings);
        }
    }

    /// <summary>The saved Desk with what the pack previews: the companion piece placed for a sprite (where it would go), the
    /// first palette as Custom ink for a palette.</summary>
    internal DeskDesignSettings PreviewSettings(WidgetPreview preview)
    {
        var desk = _store.LoadDesk();
        var ink = _store.LoadDeskInk();
        if (preview.Pack.Kind == "sprite")
        {
            var layout = new DeskArrangement(desk.Layout);
            if (layout.PlaceAtDefault(CompanionSettings.Widget) is not null)
                desk = desk with { Layout = layout.Layout };
        }
        if (preview.Ink is { } stops) ink = ink with { Mode = DeskInkMode.Custom, Color = stops };
        return new DeskDesignSettings(desk, ink, _store.LoadLauncher());
    }

    public void EndPreview()
    {
        CloseWindow();
        _desk.Companion?.EndPreviewPack();
        // Only the preview this host put up; a Desk design under review keeps its own.
        if (_ownsDeskPreview && !DeskBusy) _desk.EndPreview();
        else _desk.Reload();
        _ownsDeskPreview = false;
    }

    public void Installed(string packId)
    {
        _desk.Reload();
        _settingsFollow();
    }

    private void CloseWindow()
    {
        var w = _window;
        _window = null;
        if (w is null) return;
        try { w.Close(); }
        catch (InvalidOperationException) { /* already closing */ }
    }

    public void Dispose() => CloseWindow();
}
