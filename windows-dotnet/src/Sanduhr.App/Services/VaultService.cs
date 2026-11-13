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
        _store = new VaultStore(_paths.VaultDir, _paths.LogFile);   // streamer: notname - a file location used for IO, never drawn
        Reader = new VaultReader(_store);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var version = v is null ? "3.2.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        _ingester = new VaultIngester(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),   // streamer: notname - user folder handed to the ingester, never drawn
            _store, version, _paths.LogFile);
    }

    public VaultReader Reader { get; }

    public VaultStore Store => _store;

    public string VaultDir => _paths.VaultDir;   // streamer: notname - a file location used for IO, never drawn

    /// <summary>Basenames (".claude", ".claude-personal") of CC homes that
    /// exist on this machine right now.</summary>
    public IReadOnlyList<string> DetectedRootNames()   // streamer: key - root names for consent keys, never drawn here
        => _reader.SearchRoots().Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!).Concat(ActiveAgents().Select(a => a.Agent)).ToList();   // streamer: key - agent root names for consent keys, never drawn here

    /// <summary>The agent sources whose switch is on and whose files exist (spec 3), set by App. Read on every cycle, so a
    /// Sources switch takes effect on the next ingest without rebuilding anything.</summary>
    public Func<IReadOnlyList<IAgentBurnSource>> ActiveAgents { get; set; } = () => [];

    /// <summary>A Claude Code home needs an explicit yes in vault_roots; an active agent root is on unless purged (false is
    /// the tombstone Purge and Erase write).</summary>
    internal static bool Consented(string root, IReadOnlyDictionary<string, bool> vaultRoots, IReadOnlyCollection<string> activeAgents)
        => AgentSources.Burn.Contains(root) && root != AgentSources.Claude
            ? activeAgents.Contains(root) && vaultRoots.GetValueOrDefault(root, true)
            : vaultRoots.GetValueOrDefault(root);

    public bool IsConsented(string root)
        => Consented(root, _settings.LoadVaultRoots(), ActiveAgents().Select(a => a.Agent).ToList());   // streamer: key - root names for consent keys, never drawn here

    /// <summary>A source switched on in Sources clears its purge tombstone, so ingest resumes.</summary>
    public void SetAgentVaultOn(string agent) => SetRootConsent(agent, true);   // streamer: key - an agent name written as a consent key, never drawn

    /// <summary>Detected ∩ consented — the only roots the ingester ever touches.</summary>
    public IReadOnlyList<string> ConsentedRootNames()
    {
        var consent = _settings.LoadVaultRoots();
        var active = ActiveAgents().Select(a => a.Agent).ToList();   // streamer: key - root names for consent keys, never drawn here
        return DetectedRootNames().Where(r => Consented(r, consent, active)).ToList();   // streamer: key - root names for consent keys, never drawn here
    }

    public bool NeedsConsentPrompt
        => !_settings.LoadVaultPrompted() && _reader.SearchRoots().Count > 0;   // streamer: key - a count, never drawn

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
        if (ResetGuard.Done)
            return;
        if (Interlocked.CompareExchange(ref _ingestRunning, 1, 0) != 0)
            return;   // previous run still going -> skip this cycle

        IReadOnlyList<string> roots;
        IReadOnlyList<IAgentBurnSource> agents;
        bool fullPaths;
        try
        {
            var consented = ConsentedRootNames();
            agents = ActiveAgents().Where(a => consented.Contains(a.Agent)).ToList();   // streamer: key - root names for consent keys, never drawn here
            roots = consented.Where(r => !agents.Any(a => a.Agent == r)).ToList();   // streamer: key - root names for consent keys, never drawn here
            fullPaths = _settings.LoadVaultStoreFullPaths();
        }
        catch (Exception e)
        {
            // Prelude threw before dispatch -> latch must not jam forever.
            LogBestEffort("ingest", e);
            Interlocked.Exchange(ref _ingestRunning, 0);
            return;
        }

        if (roots.Count == 0 && agents.Count == 0)
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
                    stillConsented: IsConsented, agents: agents);
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
        foreach (var root in DetectedRootNames())   // streamer: key - root names written as consent keys, never drawn
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
            Directory.CreateDirectory(_paths.VaultDir);   // streamer: notname - a file location used for IO, never drawn
            // network: local
            Process.Start(new ProcessStartInfo("explorer.exe", _paths.VaultDir) { UseShellExecute = true });   // streamer: cant - Explorer is a window Windows opens for Sanduhr (the caption's list)
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
            File.AppendAllText(_paths.LogFile,   // streamer: notname - written to the log file, never drawn
                $"{DateTime.UtcNow:o} vault {operation} failed ({e.GetType().Name}){Environment.NewLine}");
        }
        catch
        {
        }
    }
}
