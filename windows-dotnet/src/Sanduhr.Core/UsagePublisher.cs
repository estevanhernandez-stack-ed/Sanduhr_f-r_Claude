using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>One quota tier as published: percent may be null (stale, crossed,
/// count-based) — the publisher never sends a stale number as if current.</summary>
public sealed record PublishQuotaTier(string Key, int? UtilizationPct, string? ResetsAt);

/// <summary>The quota block of the payload. Status vocabulary is closed:
/// <c>ok</c> (fresh snapshot, numbers present), <c>stale</c> (snapshot exists
/// but is too old or carries a fetch error — every percentage null),
/// <c>no_data</c> (no readable snapshot).</summary>
public sealed record PublishQuota(string Status, string? AsOf, IReadOnlyList<PublishQuotaTier> Tiers)
{
    public static PublishQuota NoData { get; } = new("no_data", null, Array.Empty<PublishQuotaTier>());
}

/// <summary>Typed outcome of one POST. <see cref="StatusCode"/> is 0 when the
/// request never produced an HTTP response (DNS, timeout, refused).</summary>
public sealed record UsagePublishResult(int StatusCode, bool Ok, string Message, DateTimeOffset At)
{
    public string Status => Ok ? "ok" : "error";
}

/// <summary>
/// Builds and posts the daily usage snapshot to an endpoint the user chose.
/// Nothing about usage comes back to 626 Labs; the user sends it to
/// themselves. The payload is a per-project token-count proxy for ONE
/// calendar day plus the widget's current quota headroom — and nothing else:
/// no paths (basenames only, capped), no session content, no account labels,
/// no tokens. Roots the user did not mark shareable never appear in
/// <c>homes</c> or <c>byProject</c>, even if burn was collected for them.
/// Two body formats: <see cref="PublishBodyFormat.Sanduhr"/> is the snapshot
/// object as-is; <see cref="PublishBodyFormat.Labs626"/> wraps it the way the
/// 626 Labs dashboard preset expects. Pure Core: the HTTP handler is
/// injectable so tests never touch the network.
/// </summary>
public static class UsagePublisher
{
    public const string Source = "sanduhr";
    public const string Caveat = "token-count proxy from local logs; lower bound";

    /// <summary>Top-N projects sent (the contract allows up to 50).</summary>
    public const int MaxProjects = 25;

    /// <summary>Project display-name cap (the contract's max 120).</summary>
    public const int MaxProjectNameLength = 120;

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Default target day: yesterday in local time (the day that has
    /// closed, so its logs are complete).</summary>
    public static DateOnly DefaultDate(DateTimeOffset nowLocal)
        => DateOnly.FromDateTime(nowLocal.LocalDateTime).AddDays(-1);

    /// <summary>Assemble the wire payload. <paramref name="shareableRoots"/> is
    /// the consent filter: burn for any other root is dropped on the floor.
    /// <paramref name="format"/> picks the envelope: the plain snapshot object,
    /// or the 626 Labs <c>action: "record"</c> wrapper around the same fields.
    /// Deterministic (no clock, no IO) so the shape is fully testable.</summary>
    public static JsonObject BuildPayload(
        DateOnly date,
        string machine,
        IReadOnlyList<RootDayBurn> burnByRoot,
        IReadOnlySet<string> shareableRoots,
        PublishQuota? quota,
        PublishBodyFormat format = PublishBodyFormat.Sanduhr)
    {
        var homes = new JsonArray();
        var byProject = new Dictionary<string, long>(StringComparer.Ordinal);
        var byTier = new Dictionary<string, long>(StringComparer.Ordinal);
        long total = 0;

        foreach (var root in burnByRoot)
        {
            if (!shareableRoots.Contains(root.Root))
                continue;
            homes.Add(root.Root);
            total += root.Total;
            foreach (var (name, tokens) in root.ByProject)
            {
                var key = ClampName(name);
                byProject[key] = byProject.GetValueOrDefault(key) + tokens;
            }
            foreach (var (tier, tokens) in root.ByTier)
                byTier[tier] = byTier.GetValueOrDefault(tier) + tokens;
        }

        var projects = new JsonArray();
        foreach (var (name, tokens) in byProject.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(MaxProjects))
            projects.Add(new JsonObject { ["name"] = name, ["tokens"] = tokens });

        var tiers = new JsonObject();
        foreach (var (tier, tokens) in byTier.OrderByDescending(p => p.Value))
            tiers[tier] = tokens;

        var payload = new JsonObject();
        if (format == PublishBodyFormat.Labs626)
            payload["action"] = "record";   // the dashboard's discriminator; the snapshot itself is identical
        payload["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        payload["source"] = Source;
        payload["machine"] = machine;
        payload["homes"] = homes;
        payload["totals"] = new JsonObject { ["tokens"] = total };
        payload["byTier"] = tiers;
        payload["byProject"] = projects;
        payload["caveat"] = Caveat;
        if (quota is not null)
            payload["quota"] = BuildQuota(quota);
        return payload;
    }

    private static JsonObject BuildQuota(PublishQuota quota)
    {
        var tiers = new JsonArray();
        foreach (var t in quota.Tiers)
        {
            tiers.Add(new JsonObject
            {
                ["key"] = t.Key,
                // A non-ok quota NEVER carries a percentage — stale numbers read as current.
                ["utilizationPct"] = quota.Status == "ok" ? t.UtilizationPct : null,
                ["resetsAt"] = t.ResetsAt,
            });
        }
        return new JsonObject
        {
            ["status"] = quota.Status,
            ["asOf"] = quota.AsOf,
            ["tiers"] = tiers,
        };
    }

    /// <summary>Read the widget's snapshot.json into a <see cref="PublishQuota"/>.
    /// Fresh band + status ok → <c>ok</c> with numbers; any other readable
    /// snapshot → <c>stale</c> with the tier keys and reset instants but null
    /// percentages; missing/malformed → <c>no_data</c>. Never throws.</summary>
    public static PublishQuota ReadQuota(string snapshotPath, DateTimeOffset now)
    {
        JsonObject snap;
        try
        {
            if (!File.Exists(snapshotPath))
                return PublishQuota.NoData;
            if (JsonNode.Parse(File.ReadAllText(snapshotPath)) is not JsonObject parsed)
                return PublishQuota.NoData;
            snap = parsed;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return PublishQuota.NoData;
        }

        string? capturedAt = (string?)snap["captured_at"];
        if (Pacing.Parse(capturedAt) is not { } captured)
            return PublishQuota.NoData;

        bool fresh = SnapshotContract.Band(captured, now) == SnapshotBand.Fresh
                     && (string?)snap["status"] != "error";
        var tiers = new List<PublishQuotaTier>();
        foreach (var node in snap["tiers"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject t || (string?)t["key"] is not { Length: > 0 } key)
                continue;
            string? resetsAt = (string?)t["resets_at"];
            bool crossed = Pacing.Parse(resetsAt) is { } r && r <= now;
            int? util = fresh && !crossed ? TryInt(t["utilization"]) : null;
            tiers.Add(new PublishQuotaTier(key, util, resetsAt));
        }
        return new PublishQuota(fresh ? "ok" : "stale", capturedAt, tiers);
    }

    /// <summary>An endpoint the publisher will post to: an absolute http(s) URL.
    /// Anything else (blank, relative, another scheme) never reaches the network.</summary>
    /// <summary>https anywhere; plain http only to a loopback host, because the
    /// publish token rides in a header and must not cross a network in clear.</summary>
    public static bool IsUsableEndpoint(string? url)
        => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

    /// <summary>POST the payload to <paramref name="target"/>. The token rides
    /// only in the header the scheme names (none at all for
    /// <see cref="PublishAuthScheme.None"/>); it is never echoed into the
    /// result message or any log line.</summary>
    public static async Task<UsagePublishResult> PublishAsync(
        JsonObject payload,
        PublishTarget target,
        string? token,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        if (!IsUsableEndpoint(target.EndpointUrl))
        {
            string why = string.IsNullOrWhiteSpace(target.EndpointUrl)
                ? "No publish endpoint configured."
                : "Publish endpoint must be an absolute https URL (plain http only for a loopback host).";
            return new UsagePublishResult(0, false, why, DateTimeOffset.Now);
        }
        bool needsToken = target.AuthScheme != PublishAuthScheme.None;
        if (needsToken && string.IsNullOrWhiteSpace(token))
            return new UsagePublishResult(0, false, "No publish token stored.", DateTimeOffset.Now);

        using var http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
        {
            Timeout = HttpTimeout,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(target.EndpointUrl.Trim(), UriKind.Absolute));
        switch (target.AuthScheme)
        {
            case PublishAuthScheme.Bearer:
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.Trim());
                break;
            case PublishAuthScheme.Header:
                string name = string.IsNullOrWhiteSpace(target.AuthHeaderName)
                    ? PublishSettingsJson.DefaultAuthHeaderName
                    : target.AuthHeaderName.Trim();
                if (!req.Headers.TryAddWithoutValidation(name, token!.Trim()))
                    return new UsagePublishResult(0, false, "Auth header name is not a valid HTTP header.", DateTimeOffset.Now);
                break;
            case PublishAuthScheme.None:
                break;
        }
        req.Content = new StringContent(
            payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new UsagePublishResult(0, false, $"Network error ({e.GetType().Name}).", DateTimeOffset.Now);
        }

        using (resp)
        {
            int code = (int)resp.StatusCode;
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
            catch { /* body unavailable — the status code still tells the story */ }

            string message = resp.StatusCode switch
            {
                HttpStatusCode.OK => "Published.",
                HttpStatusCode.Unauthorized => "Token rejected (401) - check the publish token.",
                HttpStatusCode.Forbidden => "Forbidden (403) - the endpoint refused this token.",
                HttpStatusCode.TooManyRequests => "Rate limited (429) - try again later.",
                HttpStatusCode.BadRequest => $"Rejected (400): {Snippet(body)}",
                _ => $"HTTP {code}.",
            };
            return new UsagePublishResult(code, code == 200, message, DateTimeOffset.Now);
        }
    }

    private static string ClampName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "(unknown)";
        return name.Length <= MaxProjectNameLength ? name : name[..MaxProjectNameLength];
    }

    private static string Snippet(string body)
    {
        var s = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= 160 ? s : s[..160] + "…";
    }

    private static int? TryInt(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out int i)) return i;
        if (v.TryGetValue<double>(out double d)) return (int)d;
        return null;
    }
}
