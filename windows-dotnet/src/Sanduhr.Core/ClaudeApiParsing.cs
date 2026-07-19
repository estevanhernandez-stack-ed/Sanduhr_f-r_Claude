using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// Pure JSON-shape parsing + HTTP-status → typed-error mapping shared by BOTH
/// claude.ai transports: the <see cref="HttpClient"/>-based
/// <see cref="ClaudeApiClient"/> and the App's WebView2-backed client
/// (<c>WebView2ApiClient</c>, which fetches through an authenticated browser to
/// clear Cloudflare). Extracting these keeps org-discovery, usage, and
/// routine-budget parsing in one unit-tested place so the two transports can
/// never drift in how they read the API.
///
/// <c>internal</c> + <c>InternalsVisibleTo</c> (Sanduhr app + Sanduhr.Tests) so
/// the App client calls in and the test project exercises each branch directly.
/// All methods are pure: a string body in, a parsed value or typed throw out — no
/// IO, no transport, no WPF.
/// </summary>
internal static class ClaudeApiParsing
{
    /// <summary>
    /// Result of parsing <c>/api/organizations</c>: the selected org id, the inert
    /// <c>_account</c> node to inject into the usage payload, and the typed plan
    /// fields surfaced to the tier badge.
    /// </summary>
    internal sealed record OrgDiscovery(string OrgId, JsonObject AccountNode, AccountPlan Account);

    /// <summary>
    /// Parse the <c>/api/organizations</c> response body, selecting the org whose
    /// <c>capabilities</c> we actually track usage for — <c>claude_max</c> preferred,
    /// then <c>chat</c>, then the first well-formed org as last resort — and
    /// capturing its plan fields. Same shape checks and exception messages as
    /// <c>ClaudeApiClient.GetOrgIdAsync</c>, which delegates to this method directly
    /// (not a parallel reimplementation), so the HttpClient path's parity tests
    /// cover this code transitively.
    /// </summary>
    internal static OrgDiscovery ParseOrganizations(string body)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch (JsonException e)
        {
            // Cloudflare can serve a challenge page WITH HTTP 200 — a
            // status-only check (ThrowForStatus/CheckAsync handle 403) never
            // sees it, and the generic non-JSON error left the UI stuck on
            // "No connection — retrying…". Only a parse FAILURE consults the
            // CF sniffer, so JSON payloads containing the word "cloudflare"
            // can never false-positive.
            if (ClaudeApiClient.LooksLikeCloudflare(body))
                throw new CloudflareBlockedException("Cloudflare challenge in 2xx body -- cf_clearance needed");
            throw new NetworkException("Org discovery returned non-JSON", e);
        }

        if (root is not JsonArray orgs)
            throw new NetworkException("Org discovery returned non-JSON");
        if (orgs.Count == 0)
            throw new NetworkException("No organizations returned for this account");

        // Accounts can carry multiple orgs (a claude_max subscription org AND
        // an API individual org, observed live 2026-07-19). orgs[0] is
        // ordering-luck; prefer the org we actually track usage for.
        JsonObject? selected = null, byChat = null, first = null;
        foreach (var node in orgs)
        {
            if (node is not JsonObject candidate) continue;
            first ??= candidate;
            if (candidate["capabilities"] is not JsonArray candidateCaps) continue;
            bool hasMax = candidateCaps.Any(c => c is JsonValue v && v.TryGetValue<string>(out var s) && s == "claude_max");
            bool hasChat = candidateCaps.Any(c => c is JsonValue v && v.TryGetValue<string>(out var s) && s == "chat");
            if (hasMax) { selected = candidate; break; }
            if (hasChat) byChat ??= candidate;
        }
        var org = selected ?? byChat ?? first;
        if (org is null)
            throw new NetworkException("Malformed organization entry");

        var uuid = (string?)org["uuid"];
        if (string.IsNullOrEmpty(uuid))
            throw new NetworkException("Organization missing uuid");

        // Capture the plan/subscription fields off the same org we track usage for
        // so the tier badge can render the subscription tier (PR #25).
        var accountNode = new JsonObject
        {
            ["rate_limit_tier"] = org["rate_limit_tier"]?.DeepClone(),
            ["billing_type"] = org["billing_type"]?.DeepClone(),
            ["capabilities"] = org["capabilities"]?.DeepClone(),
        };
        var account = new AccountPlan(
            (string?)org["rate_limit_tier"],
            (string?)org["billing_type"],
            org["capabilities"] is JsonArray caps
                ? caps.Select(e => e is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
                      .Where(s => s is not null).Select(s => s!).ToArray()
                : null);

        return new OrgDiscovery(uuid, accountNode, account);
    }

    /// <summary>
    /// Parse a usage response body into a <see cref="JsonObject"/> and ride the
    /// captured plan fields through on the reserved <c>_account</c> key. The node
    /// is <see cref="JsonNode.DeepClone"/>d so every call hands back an independent
    /// tree — a JsonNode can only ever have one parent. Mirrors
    /// <c>ClaudeApiClient.GetUsageAsync</c>'s parse + inject step.
    /// </summary>
    internal static JsonObject ParseUsage(string body, JsonObject? accountNode)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch (JsonException e)
        {
            // Cloudflare can serve a challenge page WITH HTTP 200 — a
            // status-only check (ThrowForStatus/CheckAsync handle 403) never
            // sees it, and the generic non-JSON error left the UI stuck on
            // "No connection — retrying…". Only a parse FAILURE consults the
            // CF sniffer, so JSON payloads containing the word "cloudflare"
            // can never false-positive.
            if (ClaudeApiClient.LooksLikeCloudflare(body))
                throw new CloudflareBlockedException("Cloudflare challenge in 2xx body -- cf_clearance needed");
            throw new NetworkException("Usage endpoint returned non-JSON", e);
        }

        if (root is not JsonObject data)
            throw new NetworkException("Usage endpoint returned non-object JSON");

        if (accountNode is not null)
            data["_account"] = accountNode.DeepClone();

        return data;
    }

    /// <summary>
    /// Parse a Routines <c>run-budget</c> body into a <see cref="RoutineBudget"/>,
    /// or <c>null</c> for non-JSON / non-object / non-numeric shapes (parity with
    /// <c>ClaudeApiClient.GetRoutineBudgetAsync</c>'s <c>(ValueError, TypeError)</c>
    /// guard). The 404 "no code access" case is handled by the caller before this.
    /// </summary>
    internal static RoutineBudget? ParseRoutineBudget(string body)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch (JsonException) { return null; }
        if (root is not JsonObject obj) return null;

        // used/limit may arrive as JSON numbers OR numeric strings, and may be
        // absent/null on unified-billing accounts. Read leniently — never throw,
        // never return null for a valid object (limit==0 means "no routines",
        // which the fetcher already treats as nothing to show). The live tell was
        // a body {used, limit, unified_billing_enabled} that parsed to null because
        // GetValue<double> threw on a non-number value.
        int used = ReadIntLenient(obj["used"]);
        int limit = ReadIntLenient(obj["limit"]);
        return new RoutineBudget(used, limit);
    }

    /// <summary>Tolerant int read: JSON number, numeric string, or absent/null → 0. Never throws.</summary>
    private static int ReadIntLenient(JsonNode? node)
    {
        if (node is not JsonValue v) return 0;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<string>(out var s)
            && double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ds))
            return (int)ds;
        return 0;
    }

    /// <summary>
    /// Map an HTTP status (+ optional body for Cloudflare detection) to the typed
    /// error hierarchy, or return normally for 2xx. This is the WebView2 path's
    /// counterpart to <c>ClaudeApiClient.CheckAsync</c> — same status semantics,
    /// but driven by the integer status + body string the in-page <c>fetch()</c>
    /// reports rather than an <see cref="HttpResponseMessage"/>.
    ///
    /// 401 (or 403 without CF markup) → <see cref="SessionExpiredException"/>;
    /// 403 carrying a Cloudflare challenge body → <see cref="CloudflareBlockedException"/>
    /// (rare on this path — a real browser clears CF); anything else non-2xx
    /// (5xx, unexpected 4xx) → <see cref="NetworkException"/>.
    /// </summary>
    internal static void ThrowForStatus(int status, string? body)
    {
        if (status == 401)
            throw new SessionExpiredException("HTTP 401 -- session key rejected");
        if (status == 403)
        {
            if (ClaudeApiClient.LooksLikeCloudflare(body))
                throw new CloudflareBlockedException("Cloudflare challenge -- cf_clearance needed");
            throw new SessionExpiredException("HTTP 403 -- session key rejected");
        }
        if (status is < 200 or >= 300)
            throw new NetworkException($"HTTP {status}");
    }
}
