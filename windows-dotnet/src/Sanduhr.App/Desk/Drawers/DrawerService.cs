using System.IO;
using System.Windows.Threading;
using Sanduhr.App.Interop;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// Drawers' live side (5.0 drawers spec, section 2): every drawer's state, kept across Desk redraws so a redraw rebuilds visuals only
/// (the companion's pattern). Lists each home, watches it (debounced, as the Desk's other watchers are), hands out thumbnails, runs the
/// user's own moves through the journal, and keeps fold, hide and names in settings. A drawer mid-interaction holds the service busy
/// (<see cref="Hold"/>), and the Desk waits to redraw until the last hold is released.
/// </summary>
internal sealed class DrawerService : IDisposable
{
    public static DrawerService? Current { get; set; }

    private static readonly string[] Housekeeping = ["desktop.ini", "Thumbs.db"];
    private readonly SettingsStore _store;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, DrawerState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DrawerState> _preview = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _debounce;
    private readonly DrawerIcons _icons;
    private readonly List<object> _holds = [];
    private ILookup<string, string> _pending = Array.Empty<string>().ToLookup(s => s);
    private bool _disposed;

    public DrawerService(SettingsStore store, Paths paths, DrawerPlaces places, Dispatcher? ui = null)
    {
        _store = store;
        Places = places;
        _ui = ui ?? Dispatcher.CurrentDispatcher;
        Journal = new DrawerJournalStore(paths.DrawerJournalFile);
        Mover = new DrawerMover(places.Desktop);
        _icons = new DrawerIcons(_ui);
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => Flush();
        Reload();
    }

    public DrawerSettings Settings { get; private set; } = DrawerSettings.Default;
    public DrawerPlaces Places { get; }
    public DrawerJournalStore Journal { get; }
    public DrawerMover Mover { get; }

    public DrawerRoots Roots => new(Places.Desktop, Places.PublicDesktop, Settings.RootOr(Places.Desktop));
    public IReadOnlyList<string> Homes => [.. Settings.Drawers.Select(d => d.Home)];
    public IReadOnlyList<DrawerState> Drawers => [.. _states.Values];

    /// <summary>A drawer to draw: a saved one while drawers are on, or one a plan under review would create.</summary>
    public DrawerState? Get(string id) =>
        _preview.TryGetValue(id, out var p) ? p : Settings.Enabled && _states.TryGetValue(id, out var s) ? s : null;

    /// <summary>Drawers were added, removed, renamed, folded or hidden, or one's folder came or went: the Desk lays out again.</summary>
    public event Action? Changed;

    // ---- busy ----

    public bool Busy => _holds.Count > 0;

    /// <summary>The last hold was released: a redraw that waited can run.</summary>
    public event Action? BusyEnded;

    /// <summary>Holds the Desk's redraws off while a drawer is mid-interaction (focus, a drag, a menu, an inline rename). Dispose to release;
    /// releasing twice is harmless.</summary>
    public IDisposable Hold()
    {
        var token = new object();
        _holds.Add(token);
        return new Release(() =>
        {
            if (_holds.Remove(token) && _holds.Count == 0) BusyEnded?.Invoke();
        });
    }

    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { _release?.Invoke(); _release = null; }
    }

    // ---- state ----

    /// <summary>Reads the settings again and brings every drawer's state, listing and watcher up to date.</summary>
    public void Reload()
    {
        Settings = _store.LoadDrawers();
        var ids = Settings.Drawers.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _states.Keys.Where(k => !ids.Contains(k)).ToArray())
        {
            Unwatch(gone);
            _states.Remove(gone);
        }
        foreach (var def in Settings.Drawers)
        {
            if (!_states.TryGetValue(def.Id, out var state)) _states[def.Id] = state = new DrawerState(def);
            bool moved = !string.Equals(state.Def.Home, def.Home, StringComparison.OrdinalIgnoreCase);
            state.Def = def;
            state.Folded = Settings.Folded.Contains(def.Id);
            if (moved) state.Items.Clear();
            Refresh(state);
            if (Settings.Enabled) Watch(state); else Unwatch(def.Id);
        }
    }

    /// <summary>Saves drawers' settings and follows them.</summary>
    public void Save(DrawerSettings next)
    {
        _store.SaveDrawers(next);
        Reload();
        Changed?.Invoke();
    }

    private void Refresh(DrawerState state)
    {
        bool wasMissing = state.Missing;
        var listed = new List<DrawerItem>();
        if (Directory.Exists(state.Def.Home))
        {
            state.Missing = false;
            try
            {
                foreach (var info in new DirectoryInfo(state.Def.Home).EnumerateFileSystemInfos())
                {
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (Housekeeping.Contains(info.Name, StringComparer.OrdinalIgnoreCase)) continue;   // streamer: key - compared with fixed names
                    listed.Add(new DrawerItem(info.FullName, info is DirectoryInfo, pending: false));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* listed as far as it could be */ }
        }
        else state.Missing = !_preview.ContainsKey(state.Def.Id);
        foreach (var path in _pending[state.Def.Id]) listed.Add(new DrawerItem(path, Directory.Exists(path), pending: true));
        Sync(state, [.. listed.OrderBy(i => i.Pending).ThenBy(i => !i.IsFolder).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)]);
        if (wasMissing != state.Missing) Changed?.Invoke();
    }

    /// <summary>Brings the list to <paramref name="next"/> without rebuilding it, so a selection survives a file arriving.</summary>
    private void Sync(DrawerState state, List<DrawerItem> next)
    {
        var items = state.Items;
        var keep = next.Select(n => (n.Path, n.Pending)).ToHashSet();
        for (int i = items.Count - 1; i >= 0; i--)
            if (!keep.Contains((items[i].Path, items[i].Pending))) items.RemoveAt(i);
        for (int i = 0; i < next.Count; i++)
        {
            int at = -1;
            for (int j = i; j < items.Count; j++)
                if (items[j].Path == next[i].Path && items[j].Pending == next[i].Pending) { at = j; break; }
            if (at == i) continue;
            if (at > i) items.Move(at, i);
            else
            {
                items.Insert(i, next[i]);
                _icons.Request(next[i]);
            }
        }
        state.CountChanged();
    }

    private void Watch(DrawerState state)
    {
        Unwatch(state.Def.Id);
        if (!Directory.Exists(state.Def.Home)) return;
        try
        {
            var id = state.Def.Id;
            var w = new FileSystemWatcher(state.Def.Home)
            {
                // streamer: notname - a watcher filter flag, not a name
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            w.Created += (_, _) => MarkDirty(id);
            w.Deleted += (_, _) => MarkDirty(id);
            w.Renamed += (_, _) => MarkDirty(id);
            w.Changed += (_, _) => MarkDirty(id);
            w.Error += (_, _) => MarkDirty(id);   // the folder itself was deleted or renamed, or the buffer overflowed
            w.EnableRaisingEvents = true;
            _watchers[id] = w;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Gone between the check and the watcher: shown missing; Reload tries again.
            if (!state.Missing) { state.Missing = true; Changed?.Invoke(); }
        }
    }

    private void Unwatch(string id)
    {
        if (_watchers.Remove(id, out var w)) w.Dispose();
    }

    private void MarkDirty(string id)
    {
        if (_disposed) return;
        _ui.BeginInvoke(() =>
        {
            if (_disposed) return;
            _dirty.Add(id);
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void Flush()
    {
        _debounce.Stop();
        foreach (var id in _dirty.ToArray())
            if (_states.TryGetValue(id, out var s))
            {
                Refresh(s);
                if (s.Missing) Unwatch(id);
            }
        _dirty.Clear();
    }

    // ---- the user's own moves ----

    /// <summary>A drop the drawer takes: every item on the Desktop or in another drawer, and at least one not already in this one.</summary>
    public bool Accepts(IReadOnlyList<string> paths, string id)
    {
        if (Get(id) is not { Missing: false } state || paths.Count == 0) return false;
        var home = DrawerPaths.Normalize(state.Def.Home);
        var desktop = DrawerPaths.Normalize(Places.Desktop);
        var homes = Homes.Select(DrawerPaths.Normalize).OfType<string>().ToArray();
        bool any = false;
        foreach (var raw in paths)
        {
            if (DrawerPaths.Normalize(raw) is not { } p) return false;
            bool inside = (desktop is not null && DrawerPaths.IsChildOf(p, desktop)) || homes.Any(h => DrawerPaths.IsChildOf(p, h));
            if (!inside) return false;
            if (home is null || !DrawerPaths.IsChildOf(p, home)) any = true;
        }
        return any;
    }

    /// <summary>A drawer's "Drawers settings…" was chosen: the app opens Settings at the Drawers page.</summary>
    public event Action? SettingsRequested;

    public void RequestSettings() => SettingsRequested?.Invoke();

    /// <summary>The user's drop (spec section 1): no review, one journal entry. Items already in the drawer are left alone; a Public
    /// Desktop item is named and skipped.</summary>
    public DrawerRun MoveIn(IReadOnlyList<string> paths, string id)
    {
        if (Get(id) is not { Missing: false }) return DrawerRun.Empty;
        var plan = new DrawerPlan(DrawerPlanSource.User, null, [], [.. paths.Select(p => new DrawerMove(p, id))]);
        var check = DrawerPlanValidator.Check(plan, Roots, Settings.Drawers);
        if (!check.Ok) return new DrawerRun([], [.. paths.Select(p => new DrawerSkipped(Path.GetFileName(p), DrawerSkip.Outside))]);   // streamer: key - the leaf goes into DrawerRun.Skipped; DrawerPiece.Say shows counts only while streamer mode is on
        var run = Mover.Apply(check, Settings.Drawers);
        var shared = check.Shared.Select(p => new DrawerSkipped(Path.GetFileName(p), DrawerSkip.Shared));   // streamer: key - the leaf goes into DrawerRun.Skipped; DrawerPiece.Say shows counts only while streamer mode is on
        bool lost = run.Moved.Count > 0 && Journal.Add(DrawerPlanSource.User, null, run.Moved, DateTime.UtcNow) is null;
        return run with { Skipped = [.. shared, .. run.Skipped], UndoLost = lost };
    }

    /// <summary>Undoes one journal entry and drops it from the journal.</summary>
    public DrawerRun Undo(string entryId)
    {
        if (Journal.Load().Entries.FirstOrDefault(e => e.Id == entryId) is not { } entry) return DrawerRun.Empty;
        UndoPlan plan;
        try { plan = DrawerUndo.Plan(entry, Written, DrawerMover.Exists); }
        catch (InvalidOperationException) { return DrawerRun.Empty; }   // no free clash name for one entry: left in the journal, never thrown out of the service
        var run = Mover.Undo(plan, Homes);
        Journal.Remove(entryId);   // a false here is harmless: replaying a stale entry skips files no longer there as "was moved since"
        return run;
    }

    private static DateTime? Written(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : null;

    // ---- turning off ----

    /// <summary>The line when a clash name ran out, so nothing was attempted.</summary>
    internal const string NoFreeName = "Drawers could not be turned off: a file name on the Desktop has no free version.";

    /// <summary>A home's items for the return: everything but Windows' own housekeeping files, hidden or not (a hidden file a user put in a
    /// drawer is still theirs).</summary>
    internal static IEnumerable<DesktopItem> ChildrenOf(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            if (!Housekeeping.Contains(info.Name, StringComparer.OrdinalIgnoreCase))   // streamer: key - compared with fixed names
                yield return new DesktopItem(info.FullName, info is DirectoryInfo, info.LastWriteTimeUtc);
    }

    /// <summary>What turning off would move (the confirmation counts these). Null when a file name on the Desktop has no free version, so
    /// nothing can be planned.</summary>
    public ReturnPlan? ReturnPlan() => PlanReturn(Settings.Drawers);

    private ReturnPlan? PlanReturn(IReadOnlyList<DrawerDef> drawers)
    {
        try { return DrawerReturn.Plan(drawers, Settings.RootOr(Places.Desktop), Places.Desktop, ChildrenOf, DrawerMover.Exists); }
        catch (InvalidOperationException) { return null; }   // DrawerNames.Free ran out of clash names
    }

    /// <summary>
    /// Turning drawers off (spec section 4): every owned home empties onto the Desktop and goes, then the root; views stay as they are.
    /// A drawer whose home had to stay (a file open in another app) stays listed with its place, hidden word and fold, so turning off again
    /// finishes the job. Drawers go off, the entries of the drawers that left go, and auto-tidy goes off; the rules stay. When a clash name
    /// ran out nothing is touched and the line says why.
    /// </summary>
    public DrawerReturnRun TurnOff(Func<bool>? stop = null)
    {
        if (ReturnPlan() is not { } plan) return new DrawerReturnRun(DrawerRun.Empty, []) { Failure = NoFreeName };
        var result = Mover.Return(plan, Homes, stop);
        var kept = result.KeptHomes.Select(DrawerPaths.Normalize).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stay = Settings.Drawers.Where(d => d.Owned && DrawerPaths.Normalize(d.Home) is { } h && kept.Contains(h)).ToArray();
        Save(Without(Settings, [.. Settings.Drawers.Select(d => d.Id).Except(stay.Select(d => d.Id))]) with { Enabled = false, AutoTidy = false, Drawers = stay });
        return result with { KeptDrawers = stay.Length };
    }

    /// <summary>Removes one drawer: an owned one returns its files to the Desktop first and keeps its listing if a file could not move;
    /// a view just goes (its folder is the user's). Its place, hidden word and fold go with it.</summary>
    public DrawerReturnRun Remove(string id)
    {
        if (Settings.Drawers.FirstOrDefault(d => d.Id == id) is not { } def) return new DrawerReturnRun(DrawerRun.Empty, []);
        var result = new DrawerReturnRun(DrawerRun.Empty, []);
        if (def.Owned)
        {
            if (PlanReturn([def]) is not { } plan) return result with { Failure = NoFreeName };
            // Only this drawer's home: the root stays for the other drawers.
            var root = DrawerPaths.Normalize(Settings.RootOr(Places.Desktop));
            plan = plan with { HomesToRemove = [.. plan.HomesToRemove.Where(h => !string.Equals(DrawerPaths.Normalize(h), root, StringComparison.OrdinalIgnoreCase))] };
            result = Mover.Return(plan, Homes);
            if (result.KeptHomes.Count > 0) return result;
        }
        Save(Without(Settings, [id]) with { Drawers = [.. Settings.Drawers.Where(d => d.Id != id)] });
        return result;
    }

    /// <summary>The settings with these drawers' places, hidden words and folds cleared.</summary>
    private static DrawerSettings Without(DrawerSettings s, IReadOnlyCollection<string> ids) => s with
    {
        Places = s.Places.Where(kv => !ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        Hidden = [.. s.Hidden.Where(h => !ids.Contains(h))],
        Folded = [.. s.Folded.Where(f => !ids.Contains(f))],
    };

    // ---- fold, hide, names ----

    public void SetFolded(string id, bool folded)
    {
        var set = Settings.Folded.Where(f => f != id).ToList();
        if (folded) set.Add(id);
        Save(Settings with { Folded = set });
    }

    /// <summary>Whether a drawer is on the Desk: it is not in <see cref="DrawerSettings.Hidden"/>.</summary>
    public bool IsShown(string id) => !Settings.Hidden.Contains(id);

    /// <summary>A drawer is hidden and still exists: "Show drawers" has something to bring back.</summary>
    public bool HasHidden => Settings.Enabled && Settings.Hidden.Any(h => Settings.Drawers.Any(d => d.Id == h));

    /// <summary>Hidden: gone from the Desk, files where they are (spec section 2). Drawers are free panels, so this is a settings word and
    /// the Desk layout is not touched; Show drawers clears it.</summary>
    public void Hide(string id)
    {
        if (Settings.Hidden.Contains(id)) return;
        Save(Settings with { Hidden = [.. Settings.Hidden, id] });
    }

    /// <summary>"Show drawers": every hidden drawer comes back where it was.</summary>
    public void ShowAll()
    {
        if (Settings.Hidden.Count == 0) return;
        Save(Settings with { Hidden = [] });
    }

    /// <summary>A drawer was dragged by its title and let go with its top-left at this point, in screen pixels. The Desk knows the screens,
    /// so it turns the point into a place and calls <see cref="SetPlace"/>.</summary>
    public event Action<string, PxPoint>? PlaceRequested;

    public void RequestPlace(string id, PxPoint topLeft) => PlaceRequested?.Invoke(id, topLeft);

    /// <summary>Saves where a drawer sits after the user moved it.</summary>
    public void SetPlace(string id, DrawerPlace place) =>
        Save(Settings with { Places = new Dictionary<string, DrawerPlace>(Settings.Places, StringComparer.Ordinal) { [id] = place } });

    /// <summary>Renames a drawer; an owned drawer's folder takes the name too (its id, and so its layout word, stays). Null on success,
    /// otherwise the line to show.</summary>
    public string? RenameDrawer(string id, string name)
    {
        name = name.Trim();
        if (Settings.Drawers.FirstOrDefault(d => d.Id == id) is not { } def) return "That drawer is gone.";
        if (!DrawerNames.IsValid(name)) return "That name can't be a folder name.";
        var next = def with { Name = name };
        if (def.Owned)
        {
            var home = Path.Combine(Path.GetDirectoryName(def.Home)!, name);   // streamer: key - the renamed folder's path, used for IO
            if (!string.Equals(home, def.Home, StringComparison.OrdinalIgnoreCase) && DrawerMover.Exists(home))
                return "A folder of that name is already in the Drawers folder.";
            if (!ShellFiles.Rename(def.Home, name).Ok) return "The drawer's folder could not be renamed.";
            next = next with { Home = home };
        }
        Save(Settings with { Drawers = [.. Settings.Drawers.Select(d => d.Id == id ? next : d)] });
        return null;
    }

    /// <summary>Renames a file in a drawer through the Shell (spec section 2). Null on success, otherwise the line to show.</summary>
    public string? RenameItem(string path, string newName)
    {
        newName = newName.Trim();
        if (newName.Length is 0 or > 255 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.EndsWith('.') || newName.EndsWith(' '))
            return "That name can't be a file name.";
        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);   // streamer: key - the renamed file's path, used for IO
        if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase) && DrawerMover.Exists(target))
            return "A file of that name is already in this drawer.";
        return ShellFiles.Rename(path, newName).Ok ? null : "The file could not be renamed.";
    }

    /// <summary>Adds a drawer that shows an existing folder in place, as a view (spec section 4): its contents never move when drawers are
    /// turned off. Null on success.</summary>
    public string? AddView(string name, string folder)
    {
        name = name.Trim();
        if (!DrawerNames.IsValid(name)) return "That name can't be a folder name.";
        if (!Directory.Exists(folder)) return "That folder is gone.";
        if (Settings.Drawers.Any(d => string.Equals(DrawerPaths.Normalize(d.Home), DrawerPaths.Normalize(folder), StringComparison.OrdinalIgnoreCase)))
            return "A drawer already shows that folder.";
        var id = DrawerIds.Unique(name, [.. Settings.Drawers.Select(d => d.Id)]);
        Save(Settings with { Drawers = [.. Settings.Drawers, new DrawerDef(id, name, folder, Owned: false)] });
        ShowAll();
        return null;
    }

    // ---- the review's preview ----

    /// <summary>The drawers a plan under review would create, empty when none is pending.</summary>
    public IReadOnlyList<DrawerState> PreviewDrawers => [.. _preview.Values];

    /// <summary>Shows a checked plan's moves as faded items in their drawers, with the drawers it would create.</summary>
    public void SetPending(DrawerPlanCheck check)
    {
        ClearPending(refresh: false);
        foreach (var d in check.Drawers) _preview[d.Id] = new DrawerState(d);
        _pending = check.Moves.ToLookup(m => m.DrawerId, m => m.Source, StringComparer.Ordinal);
        foreach (var s in _states.Values.Concat(_preview.Values)) Refresh(s);
    }

    public void ClearPending() => ClearPending(refresh: true);

    private void ClearPending(bool refresh)
    {
        _preview.Clear();
        _pending = Array.Empty<string>().ToLookup(s => s);
        if (refresh) foreach (var s in _states.Values) Refresh(s);
    }

    // ---- the Desktop ----

    /// <summary>The user's Desktop as the rules see it (the Public Desktop is never listed for them: its items need an administrator).</summary>
    public IReadOnlyList<DesktopItem> DesktopListing()
    {
        var list = new List<DesktopItem>();
        try
        {
            foreach (var info in new DirectoryInfo(Places.Desktop).EnumerateFileSystemInfos())
                list.Add(new DesktopItem(info.FullName, info is DirectoryInfo, info.LastWriteTimeUtc,
                    (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* an unreadable Desktop sorts nothing */ }
        return list;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce.Stop();
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
        _icons.Dispose();
    }
}
