using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>The move journal on disk (5.0 drawers spec, section 4): paths only, in Sanduhr's data folder, apart from the Velopack install
/// tree. Written through a temp file so a crash never leaves half a journal; nothing is written after Reset.</summary>
internal sealed class DrawerJournalStore
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _file;
    private readonly object _lock;

    public DrawerJournalStore(string file)
    {
        _file = file;
        _lock = Locks.GetOrAdd(Path.GetFullPath(file), _ => new object());
    }

    /// <summary>The forgiving read, for display: a file that cannot be read now shows as an empty journal.</summary>
    public DrawerJournal Load()
    {
        try { return ReadStrict(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new DrawerJournal(); }
    }

    /// <summary>The read that lets an IO error out: a damaged file is empty (Core decodes it so), a locked one is not.</summary>
    private DrawerJournal ReadStrict() => File.Exists(_file) ? DrawerJournal.Decode(File.ReadAllText(_file)) : new DrawerJournal();

    /// <summary>Records a run. Null when the journal could not be read or written (after three tries): the moves stand but cannot be
    /// undone, and an unreadable journal is never overwritten with a one-entry one.</summary>
    public JournalEntry? Add(DrawerPlanSource source, string? agent, IReadOnlyList<JournalMove> moves, DateTime nowUtc)
    {
        var entry = new JournalEntry(Guid.NewGuid().ToString("N"), nowUtc, source, agent, moves);
        return Change(j => j.Add(entry, nowUtc)) ? entry : null;
    }

    /// <summary>Drops one entry. False when the journal could not be read or written, so the caller can tell the entry is still there.</summary>
    public bool Remove(string id) => Change(j => j.Without(id));

    private bool Change(Func<DrawerJournal, DrawerJournal> edit)
    {
        if (ResetGuard.Done) return false;
        lock (_lock)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) Thread.Sleep(100);
                try
                {
                    Save(edit(ReadStrict()));
                    return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* locked or busy: try again, then give up without writing */ }
            }
            return false;
        }
    }

    private void Save(DrawerJournal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);   // streamer: notname - Sanduhr's own data folder, used for IO
        var tmp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, journal.Encode());
            File.Move(tmp, _file, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a stray temp file is harmless */ }
            throw;
        }
    }
}
