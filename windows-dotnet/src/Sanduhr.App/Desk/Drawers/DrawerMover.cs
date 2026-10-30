using System.IO;
using System.Text.RegularExpressions;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What one run did: the moves that landed (journal-ready) and the files that stayed, by name, with why.</summary>
internal sealed record DrawerRun(IReadOnlyList<JournalMove> Moved, IReadOnlyList<DrawerSkipped> Skipped)
{
    public static DrawerRun Empty { get; } = new([], []);

    /// <summary>"13 moved, 1 skipped: report.docx is open in Word."</summary>
    public string Line => DrawerReview.ResultLine(Moved.Count, Skipped);
}

/// <summary>Turning drawers off: the run, and the homes (and root) kept because something in them could not move.</summary>
internal sealed record DrawerReturnRun(DrawerRun Run, IReadOnlyList<string> KeptHomes);

/// <summary>
/// The only code that moves a user's file (5.0 drawers spec, section 4). One item per Shell operation; before each, the final paths
/// (junctions, links and 8.3 names resolved) must pass <see cref="DrawerPaths.MoveAllowed"/>, so a link swapped in since the plan was
/// checked is caught. A name taken since the plan gets a fresh clash name; the result is checked on disk; a failure skips that one file.
/// </summary>
internal sealed class DrawerMover
{
    private static readonly string[] Housekeeping = ["desktop.ini", "Thumbs.db"];
    private readonly string _desktop;
    private readonly Func<string, string, string, ShellResult> _move;

    public DrawerMover(string desktop) : this(desktop, ShellFiles.Move) { }

    /// <summary>The Shell call arrives as a delegate so a test can simulate a move that lands without the Shell naming where.</summary>
    internal DrawerMover(string desktop, Func<string, string, string, ShellResult> move)
    {
        _desktop = desktop;
        _move = move;
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Applies a checked plan: makes the homes it creates, then moves each file into its drawer.</summary>
    public DrawerRun Apply(DrawerPlanCheck check, IReadOnlyList<DrawerDef> drawers)
    {
        foreach (var d in check.Drawers) Directory.CreateDirectory(d.Home);
        var homes = drawers.Concat(check.Drawers).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().Home, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<UndoStep>();
        var unnamed = new List<DrawerSkipped>();
        foreach (var m in check.Moves)
        {
            var dir = homes[m.DrawerId];
            try
            {
                var to = DrawerNames.Free(dir, Path.GetFileName(m.Source), Directory.Exists(m.Source), p => Exists(p) || claimed.Contains(p));   // streamer: key - a file's leaf names its new place, never drawn
                claimed.Add(to);
                steps.Add(new UndoStep(m.Source, to));
            }
            catch (InvalidOperationException) { unnamed.Add(new(Path.GetFileName(m.Source), DrawerSkip.CouldNotMove)); }   // streamer: key - a file's leaf for the result line, which its reader masks
        }
        var run = Run(steps, [.. homes.Values], toDesktop: false);
        return unnamed.Count == 0 ? run : run with { Skipped = [.. unnamed, .. run.Skipped] };
    }

    /// <summary>Undo for one journal entry: Core's plan, run, with the files Core already skipped added to the result.</summary>
    public DrawerRun Undo(UndoPlan plan, IReadOnlyList<string> homes)
    {
        var run = Run(plan.Steps, homes, toDesktop: true);
        var skipped = plan.Skipped.Select(s => new DrawerSkipped(Path.GetFileName(s.Path), s.Reason)).Concat(run.Skipped).ToArray();   // streamer: key - a file's leaf for the result line
        return run with { Skipped = skipped };
    }

    /// <summary>Turning drawers off: every item back on the Desktop, then each listed folder removed once it is empty.</summary>
    public DrawerReturnRun Return(ReturnPlan plan, IReadOnlyList<string> homes, Func<bool>? stop = null)
    {
        var run = Run(plan.Moves, homes, toDesktop: true, stop);
        var kept = new List<string>();
        foreach (var dir in plan.HomesToRemove)
            if (Directory.Exists(dir) && !RemoveIfEmpty(dir)) kept.Add(dir);
        return new DrawerReturnRun(run, kept);
    }

    public DrawerRun Run(IEnumerable<UndoStep> steps, IReadOnlyList<string> homes, bool toDesktop, Func<bool>? stop = null)
    {
        var moved = new List<JournalMove>();
        var skipped = new List<DrawerSkipped>();
        var finalDesktop = ShellFiles.FinalPath(_desktop) ?? _desktop;
        var finalHomes = homes.Select(h => ShellFiles.FinalPath(h) ?? h).ToArray();
        foreach (var step in steps)
        {
            var name = Path.GetFileName(step.From);   // streamer: key - a file's leaf for the result line, which its reader masks
            if (stop?.Invoke() == true) { skipped.Add(new(name, DrawerSkip.NotInTime)); continue; }
            bool isFolder = Directory.Exists(step.From);
            if (!isFolder && !File.Exists(step.From)) { skipped.Add(new(name, DrawerSkip.MovedSince)); continue; }
            var targetDir = Path.GetDirectoryName(step.To)!;   // streamer: key - the folder a file goes to, never drawn
            var finalSource = ShellFiles.FinalPath(step.From);
            var finalTarget = ShellFiles.FinalPath(targetDir);
            var refused = finalSource is null || finalTarget is null
                ? DrawerSkip.Outside
                : DrawerPaths.MoveAllowed(finalSource, finalTarget, finalDesktop, finalHomes, toDesktop);
            if (refused is not null) { skipped.Add(new(name, refused)); continue; }

            // The name chosen when the plan was made may have been taken since.
            string to;
            try { to = DrawerNames.Free(targetDir, Path.GetFileName(step.To), isFolder, Exists); }   // streamer: key - a file's leaf names its new place, never drawn
            catch (InvalidOperationException) { skipped.Add(new(name, DrawerSkip.CouldNotMove)); continue; }
            DateTime? written = isFolder ? null : File.GetLastWriteTimeUtc(step.From);
            var before = Names(targetDir);   // what was already there rules out a look-alike, even one with the moved file's write time
            var result = _move(step.From, targetDir, Path.GetFileName(to));   // streamer: key - a file's leaf names its new place, never drawn

            // The disk decides, not the Shell report: a source that is gone has moved, and an unjournaled move is one undo cannot reverse.
            if (Exists(step.From)) { skipped.Add(new(name, Reason(step.From))); continue; }
            if (Landed(result, to, targetDir, isFolder, written, before) is { } at) moved.Add(new JournalMove(step.From, at, isFolder, written));
            else skipped.Add(new(name, DrawerSkip.MovedSince));   // gone, and nowhere to be found: moved or removed by someone else
        }
        return new DrawerRun(moved, skipped);
    }

    /// <summary>Where a move that took its source away landed: the path the Shell named, else the planned one, else "name (N).ext" in the
    /// target folder. A candidate counts only when it was not in the folder before the move, and a file also only when its write time is the
    /// moved file's. Anything else could be someone else's item. Null when nothing qualifies.</summary>
    private static string? Landed(ShellResult result, string planned, string targetDir, bool isFolder, DateTime? written, HashSet<string> before)
    {
        if (result.NewPath is { } named && Exists(named)) return named;
        var leaf = Path.GetFileName(planned);   // streamer: key - a file's leaf locates it in its folder, never drawn
        var stem = Regex.Replace(isFolder ? leaf : Path.GetFileNameWithoutExtension(leaf), @" \(\d+\)$", "");   // streamer: key - a file's leaf locates it in its folder, never drawn (a clash-named plan is cut back to its base)
        var ext = isFolder ? "" : Path.GetExtension(leaf);   // streamer: key - a file's leaf locates it in its folder, never drawn
        var numbered = new Regex("^" + Regex.Escape(stem) + @" \(\d+\)" + Regex.Escape(ext) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool Ours(string p)
        {
            if (!Exists(p)) return false;
            if (before.Contains(Path.GetFileName(p))) return false;   // streamer: key - compared with the earlier listing, never drawn
            return isFolder || (written is { } w && File.GetLastWriteTimeUtc(p) == w);
        }
        if (Ours(planned)) return planned;
        try
        {
            return Directory.EnumerateFileSystemEntries(targetDir)
                .Where(p =>
                {
                    var n = Path.GetFileName(p);   // streamer: key - compared with the planned name, never drawn
                    return numbered.IsMatch(n) && Ours(p);
                })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static HashSet<string> Names(string dir)
    {
        try { return Directory.EnumerateFileSystemEntries(dir).Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase); }   // streamer: key - a listing compared by name, never drawn
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string Reason(string path) => ShellFiles.Holder(path) is { } app ? DrawerSkip.OpenIn(app) : DrawerSkip.CouldNotMove;

    /// <summary>Removes a folder that holds nothing but Windows' own housekeeping files (desktop.ini, Thumbs.db), which Windows writes into
    /// folders it shows: Windows' own folder settings files. False when anything else is in it or it cannot be removed.</summary>
    private static bool RemoveIfEmpty(string dir)
    {
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(dir).ToArray();
            if (entries.Any(e => !Housekeeping.Contains(Path.GetFileName(e), StringComparer.OrdinalIgnoreCase))) return false;   // streamer: key - compared with fixed names, never drawn
            foreach (var e in entries)
            {
                File.SetAttributes(e, FileAttributes.Normal);
                File.Delete(e);
            }
            Directory.Delete(dir, recursive: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
