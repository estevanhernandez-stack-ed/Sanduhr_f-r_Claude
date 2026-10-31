using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What Edit hands back: the files left out and new drawers' new names, by id.</summary>
internal sealed record DrawerPlanEdits(IReadOnlySet<string> Unticked, IReadOnlyDictionary<string, string> Renames);

/// <summary>The Desk and bar side of a drawers review; the App's is <see cref="DrawerReviewHost"/>, tests use a fake.</summary>
internal interface IDrawerReviewHost
{
    /// <summary>A Desk design or a widget is under review (spec section 1: one review at a time).</summary>
    bool OtherReviewOpen { get; }

    /// <summary>The Desk draws <paramref name="layout"/> (the plan's new drawers placed) until <see cref="EndPreview"/>.</summary>
    void Preview(string layout);
    void EndPreview();

    /// <summary>Shows or updates the bar. A null <paramref name="edit"/> hides Edit.</summary>
    void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss);
    void CloseBar();

    /// <summary>Edit: the user's changes, or null when they cancelled.</summary>
    DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds);
}

/// <summary>
/// The drawers review (5.0 drawers spec, section 1): a plan previews on the Desk, the bar reads "14 files into 3 drawers" with Apply,
/// Edit and Dismiss, nothing moves before Apply, and Apply's result keeps the bar up with Undo. Plans come from the rules here and from
/// agents in plan 3 (<see cref="Open"/>).
/// </summary>
internal sealed class DrawerReviewService
{
    public static DrawerReviewService? Current { get; set; }

    private readonly DrawerService _drawers;
    private readonly SettingsStore _store;
    private readonly IDrawerReviewHost _host;

    /// <summary>What moves the files; the mover's own Apply, and a seam for tests.</summary>
    internal Func<DrawerPlanCheck, IReadOnlyList<DrawerDef>, DrawerRun> RunApply { get; set; }
    private DrawerPlan? _plan;
    private DrawerPlanCheck? _check;
    private string? _undoId;
    private IReadOnlyList<string> _madeHomes = [];

    public DrawerReviewService(DrawerService drawers, SettingsStore store, IDrawerReviewHost host)
    {
        _drawers = drawers;
        _store = store;
        _host = host;
        RunApply = (check, drawerDefs) => _drawers.Mover.Apply(check, drawerDefs);
    }

    /// <summary>A plan is on the Desk waiting for Apply, Edit or Dismiss.</summary>
    public bool Reviewing => _plan is not null;

    /// <summary>The review's bar is up: a plan under review, or Apply's result with its Undo. The welcome tour waits while it is.</summary>
    public bool BarOpen => _barOpen;
    private bool _barOpen;

    private void ShowBar(DeskReviewModel model, Action apply, Action? edit, Action dismiss)
    {
        _barOpen = true;
        _host.Bar(model, apply, edit, dismiss);
    }

    private void CloseBar()
    {
        _barOpen = false;
        _host.CloseBar();
    }

    /// <summary>The built-in rules over the Desktop, opened for review. Null when the bar opened; otherwise the line to show instead.</summary>
    public string? SortNow()
    {
        var s = _drawers.Settings;
        var root = s.RootOr(_drawers.Places.Desktop);
        var known = s.Drawers.Concat(DrawerRules.DefaultDrawers(root).Where(d => s.Drawers.All(x => x.Id != d.Id))).ToList();
        // A rule the user added for a drawer that does not exist yet ("invoice" to Receipts) gets that drawer when it first matches.
        foreach (var id in s.Rules.Select(r => r.DrawerId).Distinct().Where(id => known.All(d => d.Id != id)).ToArray())
            known.Add(new DrawerDef(id, NameFromId(id), Path.Combine(root, NameFromId(id)), Owned: true));
        return Open(DrawerRules.Plan(_drawers.DesktopListing(), s.Rules, known, root, DateTime.UtcNow));
    }

    /// <summary>The name a drawer a rule names gets before it exists: its id in words ("older-files" is "Older files").</summary>
    internal static string NameFromId(string id) =>
        DrawerRules.DefaultDrawers("").FirstOrDefault(d => d.Id == id)?.Name
        ?? (id.Replace('-', ' ') is var words && words.Length > 0 ? char.ToUpperInvariant(words[0]) + words[1..] : id);

    /// <summary>Checks a plan and opens it for review. Null when the bar opened; otherwise the line to show instead.</summary>
    public string? Open(DrawerPlan plan)
    {
        if (Reviewing || _host.OtherReviewOpen) return DrawerReview.Busy;
        var check = DrawerPlanValidator.Check(plan, _drawers.Roots, _drawers.Settings.Drawers);
        if (!check.Ok) return check.Error;
        if (check.Moves.Count == 0) return DrawerReview.NothingToSort;
        Show(plan, check);
        return null;
    }

    private void Show(DrawerPlan plan, DrawerPlanCheck check)
    {
        _plan = plan;
        _check = check;
        _undoId = null;
        _drawers.SetPending(check);
        // Drawers are free panels: the layout is the Desk's own, and the drawers the plan would create are drawn from DrawerService.PreviewDrawers.
        _host.Preview(_store.LoadDesk().Layout);
        ShowBar(Model(plan, check, []), Apply, Edit, Dismiss);
    }

    private DeskReviewModel Model(DrawerPlan plan, DrawerPlanCheck check, IEnumerable<string> extraWarnings)
    {
        var s = _drawers.Settings;
        var places = _drawers.Places;
        string? note = s.Drawers.Count == 0 && string.IsNullOrWhiteSpace(s.Root) ? DrawerReview.FirstOnLine : null;
        var sync = DrawerSync.Warning(places.Desktop, s.RootOr(places.Desktop), places.Synced);
        var warnings = new List<string>();
        if (sync is not null) warnings.Add(StreamerMode.Mask.Paths(sync));   // streamer: masked - Mask.Paths hides the profile folder in the root's path while on
        warnings.AddRange(extraWarnings);
        return DrawerReview.Model(plan, check, [.. s.Drawers.Select(d => d.Id)], note, warnings,
            name => StreamerMode.Mask.Name(CodenameCategory.Folder, name));   // streamer: masked - Mask.Name gives each drawer's codename while on
    }

    /// <summary>The review is over: the plan goes, the faded items and the preview end.</summary>
    private void End()
    {
        _plan = null;
        _check = null;
        _drawers.ClearPending();
        _host.EndPreview();
    }

    private void Apply()
    {
        if (_plan is not { } plan || _check is null) return;
        // The drawers may have changed under the review (turned off, one removed, the folder moved): check the plan again against them
        // and refuse rather than run a plan that no longer fits (final review I1).
        var check = _drawers.Settings.Enabled ? DrawerPlanValidator.Check(plan, _drawers.Roots, _drawers.Settings.Drawers) : null;
        if (check is not { Ok: true } || check.Moves.Count == 0)
        {
            var why = check is null ? DrawerReview.DrawersOff
                : check.Ok ? DrawerReview.NothingToSort
                : StreamerMode.Mask.Omit(check.Error) ?? DrawerReview.PlanChanged;   // streamer: masked - a check's error can name files; Mask.Omit drops it while on
            _undoId = null;
            End();
            ShowBar(DrawerReview.Result(why, canUndo: false), Undo, null, Done);
            return;
        }
        DrawerRun run;
        try { run = RunApply(check, _drawers.Settings.Drawers); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Never leave the review stuck: the preview ends and the bar says nothing more moved, with no Undo.
            _undoId = null;
            End();
            ShowBar(DrawerReview.Result(DrawerReview.ApplyFailed, canUndo: false), Undo, null, Done);
            return;
        }
        // The journal before the settings: a save that fails after files moved still leaves the entry Undo needs (final review M1).
        var entry = run.Moved.Count > 0 ? _drawers.Journal.Add(plan.Source, plan.Agent, run.Moved, DateTime.UtcNow) : null;
        _drawers.ClearPending();
        try
        {
            var s = _drawers.Settings;
            _drawers.Save(s with
            {
                Drawers = [.. s.Drawers, .. check.Drawers],
                Folded = [.. s.Folded, .. DrawerArrival.Folded(check.Drawers)],
                AutoTidyUnlocked = s.AutoTidyUnlocked || plan.Source == DrawerPlanSource.Rules,
            });
            // No layout and no places: new drawers arrive top right at render (free panels).
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            /* the files moved and the line says so; the journal entry (and the homes below) still undo them */
        }
        _undoId = entry?.Id;
        _madeHomes = [.. check.Drawers.Select(d => d.Home)];
        End();
        var shown = (run with { UndoLost = run.Moved.Count > 0 && entry is null }).Shown;   // streamer: masked - DrawerRun.Shown cuts the file-naming line to counts while on
        ShowBar(DrawerReview.Result(shown, _undoId is not null), Undo, null, Done);
    }

    private void Undo()
    {
        if (_undoId is not { } id) return;
        _undoId = null;
        // The homes this Apply made count as homes even when their settings save failed, so the files in them can come back.
        var run = _drawers.Undo(id, _madeHomes);
        ShowBar(DrawerReview.Result(run.Shown, canUndo: false), Undo, null, Done);   // streamer: masked - DrawerRun.Shown cuts the file-naming line to counts while on
    }

    private void Done()
    {
        _undoId = null;
        CloseBar();
    }

    private void Edit()
    {
        if (_plan is not { } plan) return;
        var existing = _drawers.Settings.Drawers.Select(d => d.Id).ToArray();
        if (_host.Edit(plan, existing) is not { } edits) return;
        var next = DrawerPlanEdit.Apply(plan, edits.Unticked, edits.Renames, existing, _drawers.Settings.RootOr(_drawers.Places.Desktop));
        var check = DrawerPlanValidator.Check(next, _drawers.Roots, _drawers.Settings.Drawers);
        if (!check.Ok || check.Moves.Count == 0)
        {
            // Keep the review open on the plan the user was looking at, and say why the edit did not take.
            var why = check.Ok ? DrawerReview.NothingToSort : StreamerMode.Mask.Paths(check.Error!);   // streamer: masked - Mask.Paths hides the profile folder in a refused path while on
            ShowBar(Model(plan, _check!, [why]), Apply, Edit, Dismiss);
            return;
        }
        Show(next, check);
    }

    /// <summary>Dismiss (the button, Escape, or the bar closed): nothing moves and the Desk is drawn as it was.</summary>
    public void Dismiss()
    {
        if (_plan is null) { Done(); return; }
        End();
        CloseBar();
    }
}

/// <summary>The running app's side of a drawers review: the Desk previews through <see cref="DeskController.ShowPreview"/> and the bar
/// is the shared <see cref="DeskReviewWindow"/>.</summary>
internal sealed class DrawerReviewHost : IDrawerReviewHost, IDisposable
{
    private readonly DeskController _desk;
    private readonly SettingsStore _store;
    private DeskReviewWindow? _window;
    private Action _apply = () => { }, _dismiss = () => { };
    private Action? _edit;
    private DeskDesignSettings? _preview;

    public DrawerReviewHost(DeskController desk, SettingsStore store)
    {
        _desk = desk;
        _store = store;
    }

    public bool OtherReviewOpen => DeskDesignService.Current?.Reviewing == true || WidgetReviewService.Current?.Reviewing == true;

    public void Preview(string layout)
    {
        _preview = new DeskDesignSettings(_store.LoadDesk() with { Layout = layout }, _store.LoadDeskInk(), _store.LoadLauncher());
        _desk.ShowPreview(_preview, full: false);   // drawers only while desktop is off
    }

    public void EndPreview()
    {
        _preview = null;
        _desk.EndPreview();
    }

    public void Bar(DeskReviewModel model, Action apply, Action? edit, Action dismiss)
    {
        (_apply, _edit, _dismiss) = (apply, edit, dismiss);
        if (_window is null)
        {
            _window = new DeskReviewWindow();
            _window.ApplyRequested += () => _apply();
            _window.EditRequested += () => _edit?.Invoke();
            _window.DismissRequested += () => _dismiss();
            _window.Closed += (s, _) =>
            {
                if (!ReferenceEquals(_window, s)) return;
                _window = null;
                _dismiss();
            };
            _window.SetModel(model);
            if (_preview is { } p) _window.Target = _desk.ReviewScreen(p);
            _window.Show();
            _window.Activate();
        }
        else _window.SetModel(model);
    }

    public void CloseBar()
    {
        var w = _window;
        _window = null;
        try { w?.Close(); }
        catch (InvalidOperationException) { /* already closing */ }
    }

    public DrawerPlanEdits? Edit(DrawerPlan plan, IReadOnlyCollection<string> existingIds)
    {
        var w = new DrawerPlanEditWindow(plan, existingIds) { Owner = _window };
        return w.ShowDialog() == true ? w.Result : null;
    }

    public void Dispose() => CloseBar();
}
