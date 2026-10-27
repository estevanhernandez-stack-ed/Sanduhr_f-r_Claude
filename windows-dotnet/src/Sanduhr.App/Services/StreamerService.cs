using System.IO;
using System.Security.Cryptography;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Streamer mode's state (Settings v2, slice 7; spec, Streamer mode). It owns:
/// <list type="bullet">
/// <item><description>the <c>streamer_mode</c> key;</description></item>
/// <item><description>the codename book in <c>%APPDATA%\Sanduhr\codenames.json</c>, saved once per burst (decision 10);</description></item>
/// <item><description><c>~\.sanduhr\streamer.json</c>, written once streamer mode has been on, and on every change after that (decision 20);</description></item>
/// <item><description>the mask the app reads (<see cref="StreamerMode"/>).</description></item>
/// </list>
/// After Reset it writes nothing (<see cref="ResetGuard"/>). The widget owns one; Settings and App listen to <see cref="Changed"/>.
/// </summary>
public sealed class StreamerService : IDisposable
{
    private readonly Paths _paths;
    private readonly SettingsStore _store;
    private readonly Func<IReadOnlyList<CcFolder>> _folders;
    private readonly Func<string> _profileDir;
    private readonly Action<Action> _post;
    private readonly Func<bool> _resetDone;
    private CodenameBook? _book;
    private bool _dirty;
    private bool _foldersDirty;
    private bool _flushQueued;

    /// <param name="post">Runs the coalesced save later; the widget passes its dispatcher at Background priority. Tests collect them.</param>
    /// <param name="resetDone">True once Reset has run (default <see cref="ResetGuard.Done"/>); then nothing is written. A test passes its
    /// own, because <see cref="ResetGuard"/> is process-wide and tests in other collections run in parallel.</param>
    public StreamerService(Paths paths, SettingsStore store, Func<IReadOnlyList<CcFolder>> folders, Func<string> profileDir,
        Action<Action>? post = null, Func<bool>? resetDone = null)
    {
        _resetDone = resetDone ?? (() => ResetGuard.Done);
        _paths = paths;
        _store = store;
        _folders = folders;
        _profileDir = profileDir;
        _post = post ?? (a => a());
    }

    public StreamerMask Mask { get; private set; } = StreamerMask.Off;

    public bool On => Mask.On;

    /// <summary>The switch moved, the codenames were shuffled, or a name was shown or hidden again.</summary>
    public event Action? Changed;

    /// <summary>Reads the key (and the book, when on) and puts the mask in place. Raises nothing.</summary>
    public void Load() => Apply(_store.LoadStreamerMode(), announce: false);

    public void SetOn(bool on)
    {
        if (on == On)
            return;
        _store.SaveStreamerMode(on);
        Apply(on, announce: true);
    }

    /// <summary>Shuffle codenames… (spec): a new salt, an empty map; the allowlist is kept.</summary>
    public void Shuffle()
    {
        var book = EnsureBook();
        book.Shuffle(RandomNumberGenerator.GetBytes(Codenames.SaltBytes));
        _dirty = true;
        Apply(On, announce: true);
    }

    /// <summary>"Show the real name on stream" (spec, the allowlist C): ticked, the name shows as it is while streamer mode is on.</summary>
    public void SetShown(CodenameCategory category, string real, bool shown)
    {
        var book = EnsureBook();
        lock (book) book.SetShown(category, real, shown);
        _dirty = true;
        _foldersDirty |= category == CodenameCategory.Folder;
        Flush();
        Changed?.Invoke();
    }

    /// <summary>Every mapped or shown name, for the allowlist list.</summary>
    public IReadOnlyList<CodenameEntry> Entries()
    {
        var book = EnsureBook();
        lock (book) return book.Entries();
    }

    /// <summary>Saves the book (when it changed) and streamer.json (when a folder's line changed). Nothing after Reset.</summary>
    public void Flush()
    {
        if (_resetDone())
            return;
        if (_dirty && _book is not null)
        {
            lock (_book)
                if (Codenames.Save(_book, _paths.CodenamesFile))
                    _dirty = false;
        }
        if (_foldersDirty)
        {
            WriteStreamerFile();
            _foldersDirty = false;
        }
    }

    public void Dispose()
    {
        Flush();
        Mask.Assigned -= OnAssigned;
    }

    private void Apply(bool on, bool announce)
    {
        var book = on ? EnsureBook() : _book;
        var mask = new StreamerMask(on, book, _profileDir(),
            [_profileDir(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]);
        Mask.Assigned -= OnAssigned;
        mask.Assigned += OnAssigned;
        Mask = mask;
        // A load that finds it off leaves the static alone: it starts off, and a second widget (a test) must not switch off a mask in use.
        if (on || announce)
            StreamerMode.Use(mask);
        if (on)
        {
            // Every known folder gets its codename now, so streamer.json maps it before Claude Code renders a line (decision 8).
            foreach (var f in SafeFolders())
                mask.Folder(f.Path);
        }
        _foldersDirty = on || File.Exists(_paths.StreamerStateFile);
        Flush();
        if (announce)
            Changed?.Invoke();
    }

    private CodenameBook EnsureBook()
    {
        if (_book is null)
        {
            _book = Codenames.Load(_paths.CodenamesFile) ?? CodenameBook.New();
        }
        return _book;
    }

    private void OnAssigned(CodenameCategory category)
    {
        _dirty = true;
        _foldersDirty |= category == CodenameCategory.Folder;
        if (_flushQueued)
            return;
        _flushQueued = true;
        _post(() =>
        {
            _flushQueued = false;
            Flush();
        });
    }

    private void WriteStreamerFile()
    {
        var folders = new List<StreamerFolder>();
        if (On && _book is not null)
        {
            lock (_book)
            {
                foreach (var f in SafeFolders())
                {
                    var key = StreamerMask.FolderKey(f.Path);
                    if (key.Length == 0)
                        continue;
                    folders.Add(new StreamerFolder(StreamerFile.FolderRef(f.Path),
                        _book.TryGet(CodenameCategory.Folder, key, out var code) ? code : null,
                        _book.IsShown(CodenameCategory.Folder, key)));
                }
            }
        }
        StreamerFile.Write(_paths.StreamerStateFile, StreamerFile.Build(On, folders));
    }

    private IReadOnlyList<CcFolder> SafeFolders()
    {
        try { return _folders(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
