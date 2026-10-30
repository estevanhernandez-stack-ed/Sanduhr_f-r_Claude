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
    private DrawerPlan? _plan;
    private DrawerPlanCheck? _check;
    private string? _undoId;

    public DrawerReviewService(DrawerService drawers, SettingsStore store, IDrawerReviewHost host)
    {
        _drawers = drawers;
        _store = store;
        _host = host;
    }

    /// <summary>A plan is on the Desk waiting for Apply, Edit or Dismiss.</summary>
    public bool Reviewing => _plan is not null;

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
        _host.Bar(Model(plan, check, []), Apply, Edit, Dismiss);
    }

    private DeskReviewModel Model(DrawerPlan plan, DrawerPlanCheck check, IEnumerable<string> extraWarnings)
    {
        var s = _drawers.Settings;
        var places = _drawers.Places;
        string? note = s.Drawers.Count == 0 && string.IsNullOrWhiteSpace(s.Root) ? DrawerReview.FirstOnLine : null;
        var sync = DrawerSync.Warning(places.Desktop, s.RootOr(places.Desktop), places.Synced);
        var warnings = new List<string>();
        if (sync is not null) warnings.Add(sync);
        warnings.AddRange(extraWarnings);
        return DrawerReview.Model(plan, check, [.. s.Drawers.Select(d => d.Id)], note, warnings);
    }

    private void Apply()
    {
        if (_plan is not { } plan || _check is not { } check) return;
        var run = _drawers.Mover.Apply(check, _drawers.Settings.Drawers);
        _drawers.ClearPending();
        var s = _drawers.Settings;
        _drawers.Save(s with
        {
            Drawers = [.. s.Drawers, .. check.Drawers],
            Folded = [.. s.Folded, .. DrawerArrival.Folded(check.Drawers)],
            AutoTidyUnlocked = s.AutoTidyUnlocked || plan.Source == DrawerPlanSource.Rules,
        });
        // No layout and no places: new drawers arrive top right at render (free panels).
        var entry = run.Moved.Count > 0 ? _drawers.Journal.Add(plan.Source, plan.Agent, run.Moved, DateTime.UtcNow) : null;
        _undoId = entry?.Id;
        bool lost = run.Moved.Count > 0 && entry is null;
        var line = run.Line + (lost ? DrawerPieceCopy.UndoLost : "");
        _plan = null;
        _check = null;
        _host.EndPreview();
        _host.Bar(DrawerReview.Result(line, _undoId is not null), Undo, null, Done);
    }

    private void Undo()
    {
        if (_undoId is not { } id) return;
        _undoId = null;
        var run = _drawers.Undo(id);
        _host.Bar(DrawerReview.Result(run.Line, canUndo: false), Undo, null, Done);
    }

    private void Done()
    {
        _undoId = null;
        _host.CloseBar();
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
            _host.Bar(Model(plan, _check!, [check.Ok ? DrawerReview.NothingToSort : check.Error!]), Apply, Edit, Dismiss);
            return;
        }
        Show(next, check);
    }

    /// <summary>Dismiss (the button, Escape, or the bar closed): nothing moves and the Desk is drawn as it was.</summary>
    public void Dismiss()
    {
        if (_plan is null) { Done(); return; }
        _plan = null;
        _check = null;
        _drawers.ClearPending();
        _host.EndPreview();
        _host.CloseBar();
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
        _desk.ShowPreview(_preview);
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
