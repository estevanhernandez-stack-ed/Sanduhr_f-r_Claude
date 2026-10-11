using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>One inactive account's usage request: the payload and plan name, or the error it ended in.</summary>
public sealed record InactiveReading(JsonObject? Data, string? Plan, Exception? Error);

/// <summary>
/// Reads an inactive account's usage for account follow and per-seat meters (spec 4.6). Each read
/// builds a <see cref="WebView2ApiClient"/> on that account's own profile
/// (<c>%APPDATA%\Sanduhr\webview2-&lt;account_ref&gt;</c>), asks once, and disposes the client, so no
/// extra WebView2 process outlives the check. It never touches the shared <c>webview2-fetch</c>
/// profile the active account uses. One read at a time: the follower and the seat rounds share this
/// gate. Nothing here logs labels; errors come back to the caller as values.
/// </summary>
public sealed class InactiveUsageReader
{
    private readonly Paths _paths;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _stopped;

    public InactiveUsageReader(Paths paths, Action<string>? log = null)
    {
        _paths = paths;
        _log = log;
    }

    /// <summary>Must be called on the UI thread (the client owns a WebView2).</summary>
    public async Task<InactiveReading> ReadAsync(string label, Credentials creds, CancellationToken ct = default)
    {
        var accountRef = SnapshotContract.AccountRef(label);
        if (accountRef is null || string.IsNullOrEmpty(creds.SessionKey))
            return new InactiveReading(null, null, new NetworkException("no account to read"));

        // Reset is deleting the profiles: no new read may recreate one.
        if (_stopped) throw new OperationCanceledException();
        await _gate.WaitAsync(ct).ConfigureAwait(true);
        WebView2ApiClient? client = null;
        try
        {
            if (_stopped) throw new OperationCanceledException();
            client = new WebView2ApiClient(creds.SessionKey, creds.CfClearance, _paths.WebView2ProfileDir(accountRef));
            var data = await client.GetUsageAsync(ct).ConfigureAwait(true);
            var account = client.Account;
            var plan = account is null ? null : PlanLabel.Resolve(account.RateLimitTier, account.BillingType, account.Capabilities)?.Name;
            return new InactiveReading(data, plan, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new InactiveReading(null, null, e);
        }
        finally
        {
            try { client?.Dispose(); } catch { /* best-effort */ }
            _gate.Release();
        }
    }

    /// <summary>
    /// Reset Sanduhr…: refuses every read from here on (a waiting or new <see cref="ReadAsync"/> throws
    /// <see cref="OperationCanceledException"/>), waits for the read in flight to release the gate (up to
    /// 15 s), then deletes every <c>webview2-&lt;8 hex&gt;</c> folder with retries, because WebView2's
    /// browser processes release their file locks asynchronously. Returns how many folders stayed.
    /// </summary>
    public async Task<int> DeleteAllProfilesAsync()
    {
        _stopped = true;
        bool held = await _gate.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
        try
        {
            int stayed = 0;
            foreach (var dir in ResetPlan.AccountProfileDirs(_paths))
            {
                bool gone = false;
                for (int attempt = 0; attempt < 6 && !gone; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(dir)) await Task.Run(() => Directory.Delete(dir, recursive: true)).ConfigureAwait(true);
                        gone = true;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        await Task.Delay(400).ConfigureAwait(true);
                    }
                }
                if (!gone) stayed++;
            }
            if (stayed > 0) _log?.Invoke("webview2 account profile purge failed");
            return stayed;
        }
        finally
        {
            if (held) _gate.Release();
        }
    }

    /// <summary>
    /// Deletes the account's own profile folder (a cache; it holds that account's claude.ai cookies).
    /// Waits for any read to finish first, then retries briefly because WebView2's browser processes
    /// release their file locks asynchronously. A stubborn lock is logged (operation only) and left.
    /// </summary>
    public async Task DeleteProfileAsync(string label)
    {
        var accountRef = SnapshotContract.AccountRef(label);
        if (accountRef is null) return;
        var dir = _paths.WebView2ProfileDir(accountRef);
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                }
            }
            _log?.Invoke("webview2 account profile purge failed");
        }
        finally
        {
            _gate.Release();
        }
    }
}
