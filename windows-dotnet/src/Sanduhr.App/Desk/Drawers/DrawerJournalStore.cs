using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>The move journal on disk (5.0 drawers spec, section 4): paths only, in Sanduhr's data folder, apart from the Velopack install
/// tree. Written through a temp file so a crash never leaves half a journal; nothing is written after Reset.</summary>
internal sealed class DrawerJournalStore
{
    private readonly string _file;

    public DrawerJournalStore(string file) => _file = file;

    public DrawerJournal Load()
    {
        try { return File.Exists(_file) ? DrawerJournal.Decode(File.ReadAllText(_file)) : new DrawerJournal(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new DrawerJournal(); }
    }

    public JournalEntry Add(DrawerPlanSource source, string? agent, IReadOnlyList<JournalMove> moves, DateTime nowUtc)
    {
        var entry = new JournalEntry(Guid.NewGuid().ToString("N"), nowUtc, source, agent, moves);
        Save(Load().Add(entry, nowUtc));
        return entry;
    }

    public void Remove(string id) => Save(Load().Without(id));

    private void Save(DrawerJournal journal)
    {
        if (ResetGuard.Done) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);   // streamer: notname - Sanduhr's own data folder, used for IO
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, journal.Encode());
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the journal is best-effort; the moves stand */ }
    }
}
