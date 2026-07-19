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

        // Per-model weekly caps ship in the top-level limits[] array
        // (kind=weekly_scoped, keyed by scope.model.display_name — model.id is
        // null in live payloads, the display name is the only stable handle).
        // The flat seven_day_* vocabulary is migrating out upstream
        // (opus/sonnet now arrive null), so synthesize a flat key per scoped
        // entry and let the whole tier pipeline pick it up. A NON-null
        // upstream key of the same name always wins; JSON-null or absent is
        // synthesized over.
        if (data["limits"] is JsonArray limits)
        {
            foreach (var node in limits)
            {
                if (node is not JsonObject entry) continue;
                if ((string?)entry["kind"] != "weekly_scoped") continue;
                string? displayName = (string?)entry["scope"]?["model"]?["display_name"];
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                string key = "seven_day_" + ScopedSlug(displayName);
                TierModel.RegisterScopedTier(key, displayName.Trim());
                if (data.ContainsKey(key) && data[key] is not null)
                    continue;
                data[key] = new JsonObject
                {
                    ["utilization"] = entry["percent"]?.DeepClone(),
                    ["resets_at"] = entry["resets_at"]?.DeepClone(),
                };
            }
        }

        // History tiers = the effective order (canonical + dynamic scoped tiers),
        // read PER CALL — dynamics register during fetch, so a static capture
        // would persist a stale list. fetcher._HISTORY_TIERS parity now spans
        // both static and synthesized keys.
        foreach (var tierKey in TierModel.EffectiveOrder)
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

    /// <summary>Community slug convention for scoped-limit keys:
    /// "Fable" → fable, "Haiku 5" → haiku_5. Lowercase, non-alphanumeric runs
    /// collapse to '_', trimmed — so a display-name tweak upstream maps to a
    /// stable key wherever possible.</summary>
    internal static string ScopedSlug(string displayName)
    {
        var sb = new System.Text.StringBuilder(displayName.Length);
        bool lastUnderscore = false;
        foreach (char c in displayName.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
                lastUnderscore = false;
            }
            else if (!lastUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }
        return sb.ToString().TrimEnd('_');
    }
}
