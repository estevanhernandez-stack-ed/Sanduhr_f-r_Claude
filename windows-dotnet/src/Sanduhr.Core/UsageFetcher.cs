using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The append seam the <see cref="UsageFetcher"/> writes to. This is a STUB for
/// item 3 — the real <c>UsageHistory</c> (the
/// <c>%APPDATA%\Sanduhr\history.{account}.json</c> store, 30-day retention) is
/// item 4. Defining the interface now lets the fetcher depend on the seam and
/// stay fully unit-testable today; item 4 implements it without touching the
/// fetcher. Mirrors <c>history.append(tier_key, util, resets_at=...)</c>
/// (returns the current series, oldest-first).
/// </summary>
public interface IUsageHistory
{
    /// <summary>
    /// Record a utilization data point for <paramref name="tierKey"/> on the
    /// active account. <paramref name="resetsAt"/> is the ISO-8601 reset
    /// instant from the API (stored when present). Returns the current series.
    /// </summary>
    IReadOnlyList<int> Append(string tierKey, int utilization, string? resetsAt = null);
}

/// <summary>
/// Usage fetch orchestration, ported from <c>fetcher.py</c>. Owns an
/// <see cref="IClaudeApiClient"/>, folds the Routines budget into a synthesized
/// <c>routines</c> tier, appends each populated tier to the history seam, and
/// returns the assembled payload.
///
/// Error model: where the Python QObject converted exceptions into
/// <c>(kind, message)</c> Qt signals, the C# port lets the typed exceptions
/// (<see cref="SessionExpiredException"/> / <see cref="CloudflareBlockedException"/>
/// / <see cref="NetworkException"/>) propagate out of <see cref="FetchAsync"/>.
/// The App layer catches them and marshals targeted feedback to the UI thread
/// (spec: "raises typed errors (App marshals to UI thread)"). Anything outside
/// the typed hierarchy propagates as-is — the App's catch-all maps it to the
/// "unknown" bucket the Python fetcher used.
///
/// Pure Core: no WPF, no threading primitives beyond <c>async</c>.
/// </summary>
public sealed class UsageFetcher
{
    // The tiers we persist to history, in canonical order. fetcher._HISTORY_TIERS
    // is identical to TierModel.CanonicalOrder (five_hour … extra_usage, then
    // the synthesized routines tier last), so we reuse the single source.
    private static readonly IReadOnlyList<string> HistoryTiers = TierModel.CanonicalOrder;

    private readonly IClaudeApiClient _client;
    private readonly IUsageHistory _history;

    public UsageFetcher(IClaudeApiClient client, IUsageHistory history)
    {
        _client = client;
        _history = history;
    }

    /// <summary>
    /// Fetch usage, synthesize Routines, append history, and return the payload.
    /// Throws the typed API errors on failure (the App marshals them to the UI).
    /// </summary>
    public async Task<JsonObject> FetchAsync(CancellationToken cancellationToken = default)
    {
        // Typed errors here propagate to the caller (App) — the parity contract.
        var data = await _client.GetUsageAsync(cancellationToken).ConfigureAwait(false);

        // Routines lives on a separate endpoint (different base path, Anthropic
        // headers). Synthesize it into the main payload under the 'routines' key
        // so it flows through the same tier-rendering path. The endpoint 404s on
        // accounts without code access — that's a null budget, NOT a fetch
        // failure, so the whole call is non-fatal (parity with fetcher.py).
        RoutineBudget? budget = null;
        try
        {
            budget = await _client.GetRoutineBudgetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            budget = null; // non-fatal; the App can log at a higher layer
        }

        if (budget is not null && budget.Limit > 0)
        {
            data["routines"] = new JsonObject
            {
                ["utilization"] = budget.Utilization,
                ["resets_at"] = null, // daily quota, no specific reset time exposed
                ["used"] = budget.Used,
                ["limit"] = budget.Limit,
            };
        }

        foreach (var tierKey in HistoryTiers)
        {
            // A tier with a non-null utilization is a data point worth recording.
            // Absent / JSON-null utilization is skipped (parity with
            // `tier and tier.get("utilization") is not None`).
            if (data[tierKey] is JsonObject tier && tier["utilization"] is JsonNode utilNode)
            {
                try
                {
                    int util = (int)utilNode.GetValue<double>();
                    string? resetsAt = (string?)tier["resets_at"];
                    _history.Append(tierKey, util, resetsAt);
                }
                catch
                {
                    // History append is best-effort and never fails the fetch
                    // (parity with the per-tier try/except in fetcher.py).
                }
            }
        }

        return data;
    }
}
