using System.Diagnostics;
using System.IO;
using System.Reflection;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// App-side owner of the usage vault: consent state (settings.json), the
/// fire-and-forget ingest trigger (Interlocked single-flight — a still-running
/// cycle means this cycle skips; cross-PROCESS exclusion is the ingester's
/// named mutex), and the stewardship verbs (purge / erase / open folder).
/// Purge flips consent OFF first — consent is the tombstone; deleting the
/// folder alone is false erasure while the app runs (it re-backfills within a
/// cycle).
/// </summary>
public sealed class VaultService
{
    private readonly Paths _paths;
    private readonly SettingsStore _settings;
    private readonly CcLogReader _reader;
    private readonly VaultStore _store;
    private readonly VaultIngester _ingester;
    private int _ingestRunning;

    /// <summary>Raised after a completed ingest cycle, ON A WORKER THREAD —
    /// UI subscribers must marshal via their Dispatcher.</summary>
    public event Action? IngestCompleted;

    public VaultService(SettingsStore settings, CcLogReader reader)
    {
        _paths = new Paths();
        _settings = settings;
        _reader = reader;
        _store = new VaultStore(_paths.VaultDir, _paths.LogFile);
        Reader = new VaultReader(_store);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var version = v is null ? "3.2.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        _ingester = new VaultIngester(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _store, version, _paths.LogFile);
    }

    public VaultReader Reader { get; }

    public VaultStore Store => _store;

    public string VaultDir => _paths.VaultDir;

    /// <summary>Basenames (".claude", ".claude-personal") of CC homes that
    /// exist on this machine right now.</summary>
    public IReadOnlyList<string> DetectedRootNames()
        => _reader.SearchRoots().Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!).ToList();

    /// <summary>Detected ∩ consented — the only roots the ingester ever touches.</summary>
    public IReadOnlyList<string> ConsentedRootNames()
    {
        var consent = _settings.LoadVaultRoots();
        return DetectedRootNames().Where(r => consent.GetValueOrDefault(r)).ToList();
    }

    public bool NeedsConsentPrompt
        => !_settings.LoadVaultPrompted() && DetectedRootNames().Count > 0;

    public void SaveConsent(IReadOnlyDictionary<string, bool> roots)
    {
        _settings.SaveVaultRoots(roots);
        _settings.SaveVaultPrompted(true);
    }

    public void SetRootConsent(string root, bool on)
    {
        var map = new Dictionary<string, bool>(_settings.LoadVaultRoots(), StringComparer.Ordinal)
        {
            [root] = on,
        };
        _settings.SaveVaultRoots(map);
    }

    /// <summary>Fire-and-forget, single-flight. Never awaited anywhere in the
    /// fetch loop (the WS-B EvaluateAlerts call is synchronous-cheap — it is
    /// explicitly NOT the template here).</summary>
    public void TriggerIngest()
    {
        if (Interlocked.CompareExchange(ref _ingestRunning, 1, 0) != 0)
            return;   // previous run still going -> skip this cycle

        IReadOnlyList<string> roots;
        bool fullPaths;
        try
        {
            roots = ConsentedRootNames();
            fullPaths = _settings.LoadVaultStoreFullPaths();
        }
        catch (Exception e)
        {
            // Prelude threw before dispatch -> latch must not jam forever.
            LogBestEffort("ingest", e);
            Interlocked.Exchange(ref _ingestRunning, 0);
            return;
        }

        if (roots.Count == 0)
        {
            Interlocked.Exchange(ref _ingestRunning, 0);
            return;
        }
        _ = Task.Run(() =>
        {
            try
            {
                // stillConsented: PurgeRoot/EraseArchive flip consent BEFORE any
                // mutex wait, so re-reading settings at write time gates an
                // in-flight cycle out of a just-purged folder.
                _ingester.IngestOnce(roots, fullPaths, DateTimeOffset.UtcNow,
                    stillConsented: root => _settings.LoadVaultRoots().GetValueOrDefault(root));
                IngestCompleted?.Invoke();
            }
            catch (Exception e)
            {
                LogBestEffort("ingest", e);
            }
            finally
            {
                Interlocked.Exchange(ref _ingestRunning, 0);
            }
        });
    }

    /// <summary>Consent off (tombstone) THEN folder delete. Order matters: the
    /// reverse would let an in-flight cycle re-create the folder.</summary>
    public void PurgeRoot(string root)
    {
        SetRootConsent(root, false);
        RunUnderWriterMutex(() => _store.PurgeRoot(root));
    }

    public void EraseArchive()
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var root in DetectedRootNames())
            map[root] = false;
        _settings.SaveVaultRoots(map);
        RunUnderWriterMutex(() => _store.PurgeAll());
    }

    /// <summary>Serializes a deletion against the ingester's cross-process writer
    /// mutex so an in-flight ingest cycle (roots already snapshotted before the
    /// consent flip) can't recreate a just-purged folder after we delete it. A
    /// stuck/dead holder must not make Erase hang forever, so a timeout proceeds
    /// anyway (best-effort) rather than blocking the user's erasure request.</summary>
    private void RunUnderWriterMutex(Action action)
    {
        using var mutex = new Mutex(initiallyOwned: false, "Global\\Sanduhr.VaultWriter");
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            acquired = true;   // previous holder died; state converges by design
        }
        if (!acquired)
            LogBestEffort("purge-mutex", new TimeoutException());
        try
        {
            action();
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    public void OpenVaultFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.VaultDir);
            Process.Start(new ProcessStartInfo("explorer.exe", _paths.VaultDir) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            LogBestEffort("open-folder", e);
        }
    }

    // PRIVACY.md contract: operation + exception type only.
    private void LogBestEffort(string operation, Exception e)
    {
        try
        {
            File.AppendAllText(_paths.LogFile,
                $"{DateTime.UtcNow:o} vault {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
        }
    }
}
