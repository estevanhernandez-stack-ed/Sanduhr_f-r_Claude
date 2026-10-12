using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The scheduling shell around <see cref="AccountFollow.Decide"/> (port of the Mac's
/// AccountFollower.swift): with the setting on, the first check runs 60 s after starting, then every
/// 15 minutes. Each signed-in inactive account gets one usage request through
/// <see cref="InactiveUsageReader"/> (its own profile, disposed after the check), the reading is
/// recorded, and the decision runs on the readings. Kept thin; the rules live in AccountFollow.
///
/// A failed check is ignored and never shown. An auth error (session expired, Cloudflare) skips that
/// account until a different key is saved for it. Nothing here logs; labels never leave memory.
/// All members run on the UI thread.
/// </summary>
public sealed class AccountFollower
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);

    private readonly InactiveUsageReader _reader;
    private readonly Func<string, Credentials> _credentials;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Readings, the last automatic switch and the pause. The view model fills in the rest
    /// of the state (active, signed in, switching) through <see cref="Context"/>.</summary>
    public AccountFollow.State State { get; } = new();

    /// <summary>The view model's part of the state, read at decision time.</summary>
    public Func<AccountFollow.State> Context { get; set; } = () => new AccountFollow.State();
    /// <summary>The decision said switch.</summary>
    public Action<string> OnSwitch { get; set; } = _ => { };
    /// <summary>New readings: the "in use" marks may have changed.</summary>
    public Action OnReadings { get; set; } = () => { };
    /// <summary>Each check's answer for an account (per-seat meters keep its snapshot from it): no request is made for that alone.</summary>
    public Action<string, InactiveReading> OnResult { get; set; } = (_, _) => { };

    private CancellationTokenSource? _cts;
    private bool _checking;
    private bool _stopped;
    /// <summary>Accounts skipped after an auth error, with the key that failed.</summary>
    private readonly Dictionary<string, string> _authSkipped = new();
    /// <summary>Bumped when an account is renamed or forgotten, so an answer already in flight belongs to no one.</summary>
    private readonly Dictionary<string, int> _epoch = new();

    public AccountFollower(InactiveUsageReader reader, Func<string, Credentials> credentials, Func<DateTimeOffset>? now = null)
    {
        _reader = reader;
        _credentials = credentials;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // Lifecycle

    /// <summary>Starts or stops the checks to match the setting.</summary>
    public void Apply(bool enabled)
    {
        _cts?.Cancel();
        _cts = null;
        if (!enabled || _stopped) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = RunAsync(cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, ct).ConfigureAwait(true);
            while (!ct.IsCancellationRequested)
            {
                await CheckAsync(ct).ConfigureAwait(true);
                await Task.Delay(AccountFollow.CheckInterval, ct).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped or switched off.
        }
    }

    /// <summary>Reset Sanduhr: the checks stop for good.</summary>
    public void Stop()
    {
        _stopped = true;
        Apply(false);
    }

    // Inputs

    /// <summary>A good fetch of the active account (its normal 5-minute cadence).</summary>
    public void RecordActive(string? label, JsonObject usage)
    {
        if (label is null || SeatRules.SessionUtilization(usage) is not { } util) return;
        State.Record(label, util, _now());
        OnReadings();
    }

    /// <summary>A manual switch (chip, menu, Settings) pauses following.</summary>
    public void ManualSwitch(string label) => State.ManualPause = new AccountFollow.Pause(label, _now());

    public void AutomaticSwitch() => State.LastAutoSwitch = _now();

    public void Rename(string old, string newLabel)
    {
        State.Rename(old, newLabel);
        if (_authSkipped.Remove(old, out var key)) _authSkipped[newLabel] = key;
        Bump(old);
    }

    /// <summary>Signed out or removed: its readings and its skip go.</summary>
    public void Forget(string label)
    {
        State.Forget(label);
        _authSkipped.Remove(label);
        Bump(label);
        OnReadings();
    }

    private bool MayWrite(CancellationToken ct) => WritePolicy.MayWrite(_stopped, ResetGuard.Done, ct.IsCancellationRequested);

    private void Bump(string label) => _epoch[label] = _epoch.GetValueOrDefault(label) + 1;

    /// <summary>The other accounts seen in use in their latest check (empty with following off).</summary>
    public HashSet<string> OthersInUse() => AccountFollow.OthersInUse(Current(), _now());

    /// <summary>The decision state as of now: the readings here, the rest from the view model.</summary>
    public AccountFollow.State Current()
    {
        var s = Context();
        s.Readings = State.Readings;
        s.LastAutoSwitch = State.LastAutoSwitch;
        s.ManualPause = State.ManualPause;
        return s;
    }

    // The check

    /// <summary>One round: a usage request per signed-in inactive account, then the decision.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (_checking || !MayWrite(ct)) return;
        var s = Current();
        if (!s.Enabled || s.Active is not { } active || s.ActiveSignedOut || s.Switching || s.SignedIn.Count <= 1) return;
        _checking = true;
        try
        {
            foreach (var label in s.SignedIn.OrderBy(l => l, StringComparer.Ordinal))
            {
                if (label == active) continue;
                ct.ThrowIfCancellationRequested();
                if (!MayWrite(ct)) break;
                await CheckOneAsync(label, ct).ConfigureAwait(true);
            }
        }
        finally
        {
            _checking = false;
        }
        // Stopped or Reset during the round: no readings pushed, no switch.
        if (!MayWrite(ct)) return;
        OnReadings();
        if (AccountFollow.Decide(Current(), _now()) is { IsSwitch: true } d)
            OnSwitch(d.SwitchTo!);
    }

    private async Task CheckOneAsync(string label, CancellationToken ct)
    {
        var creds = _credentials(label);
        var key = creds.SessionKey;
        if (string.IsNullOrEmpty(key)) return;
        if (_authSkipped.TryGetValue(label, out var failed))
        {
            if (failed == key) return;
            _authSkipped.Remove(label);
        }
        int epoch = _epoch.GetValueOrDefault(label);
        var reading = await _reader.ReadAsync(label, creds, ct).ConfigureAwait(true);
        // Reset or stop while the request ran: write nothing.
        if (!MayWrite(ct)) return;
        // Renamed, signed out or removed while the request ran: the answer belongs to no one.
        if (_epoch.GetValueOrDefault(label) != epoch) return;
        OnResult(label, reading);
        if (reading.Error is { } e)
        {
            if (SeatRules.IsAuthKind(SeatRules.ErrorKind(e))) _authSkipped[label] = key;
            return;
        }
        if (SeatRules.SessionUtilization(reading.Data) is { } util)
            State.Record(label, util, _now());
    }
}
