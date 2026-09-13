using System.Globalization;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>How the publish token rides on the request.</summary>
public enum PublishAuthScheme
{
    /// <summary><c>Authorization: Bearer {token}</c>.</summary>
    Bearer,
    /// <summary>The token as the raw value of a header the user names.</summary>
    Header,
    /// <summary>No credential at all (a loopback collector, a pre-signed URL).</summary>
    None,
}

/// <summary>The wire shape of the POST body.</summary>
public enum PublishBodyFormat
{
    /// <summary>The snapshot object as-is: date, source, machine, homes, totals,
    /// byTier, byProject, quota, caveat.</summary>
    Sanduhr,
    /// <summary>The same fields wrapped the way the 626 Labs dashboard's
    /// <c>manage_usage</c> endpoint expects (<c>action: "record"</c>).</summary>
    Labs626,
}

/// <summary>A named destination that pre-fills URL, auth, and body format.
/// The preset is a convenience, never the name of the feature.</summary>
public enum PublishPreset
{
    Custom,
    Labs626,
}

/// <summary>The one built-in preset. Choosing it fills the endpoint and the
/// body format; the token is still the user's own.</summary>
public static class PublishPresets
{
    public const string Labs626EndpointUrl = "https://us-central1-project-626labs.cloudfunctions.net/mcp/api/manage_usage";
    public const string Labs626Hint = "Mint the key in your dashboard's Agents panel";
    public const string Labs626Display = "626 Labs dashboard";
    public const string CustomDisplay = "Custom endpoint";
}

/// <summary>The persisted "Publish usage" state. <see cref="Roots"/> is the
/// share map (absent = not shared); <see cref="TokenStored"/> mirrors
/// credential presence only — the token itself never lives in settings.</summary>
public sealed record PublishSettings(
    bool Enabled,
    IReadOnlyDictionary<string, bool> Roots,
    TimeOnly PublishTime,
    string EndpointUrl,
    PublishAuthScheme AuthScheme,
    string AuthHeaderName,
    PublishBodyFormat BodyFormat,
    PublishPreset Preset,
    bool TokenStored,
    DateTimeOffset? LastAttemptAt,
    string? LastResult,
    DateOnly? LastOkDate,
    DateTimeOffset? LastOkAt)
{
    /// <summary>Everything off, nothing shared, no destination.</summary>
    public static PublishSettings Defaults { get; } = new(
        Enabled: false,
        Roots: new Dictionary<string, bool>(StringComparer.Ordinal),
        PublishTime: PublishScheduler.DefaultPublishTime,
        EndpointUrl: "",
        AuthScheme: PublishAuthScheme.Bearer,
        AuthHeaderName: PublishSettingsJson.DefaultAuthHeaderName,
        BodyFormat: PublishBodyFormat.Sanduhr,
        Preset: PublishPreset.Custom,
        TokenStored: false,
        LastAttemptAt: null,
        LastResult: null,
        LastOkDate: null,
        LastOkAt: null);

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(EndpointUrl);

    /// <summary>The request shape derived from this state: where, and how the
    /// token rides. The token itself comes from the credential store.</summary>
    public PublishTarget Target => new(EndpointUrl, AuthScheme, AuthHeaderName);
}

/// <summary>Where a publish goes and how it authenticates. No token here.</summary>
public sealed record PublishTarget(string EndpointUrl, PublishAuthScheme AuthScheme, string AuthHeaderName);

/// <summary>
/// The settings.json <c>publish</c> group — read, write, and the one-time
/// migration from the pre-3.5 <c>publish_626</c> group. Pure JsonObject in /
/// JsonObject out so the App's SettingsStore stays a thin file wrapper and
/// every rule here is unit-testable without a disk.
///
/// <code>
/// {"publish": {"enabled": false, "roots": {".claude-personal": true}, "time": "06:45",
///   "endpoint_url": "", "auth_scheme": "bearer", "auth_header_name": "Authorization",
///   "body_format": "sanduhr", "preset": "custom", "token_stored": false,
///   "last_attempt_at": "...", "last_result": "...", "last_ok_date": "YYYY-MM-DD", "last_ok_at": "..."}}
/// </code>
///
/// The token NEVER lives here (Credential Manager, <c>PublishTokenStore</c>);
/// <c>token_stored</c> is a presence mirror so sanduhr-mcp can refuse with
/// <c>no_token</c> without touching credentials. A root absent from
/// <c>roots</c> is NOT shared (the vault_roots tombstone semantics).
/// </summary>
public static class PublishSettingsJson
{
    public const string GroupKey = "publish";
    public const string LegacyGroupKey = "publish_626";
    public const string DefaultAuthHeaderName = "Authorization";

    // -- read ---------------------------------------------------------------------

    public static PublishSettings Read(JsonObject root)
    {
        var g = root[GroupKey] as JsonObject ?? new JsonObject();
        var d = PublishSettings.Defaults;

        bool enabled = ReadBool(g["enabled"], d.Enabled);
        bool tokenStored = ReadBool(g["token_stored"], d.TokenStored);
        var roots = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (g["roots"] is JsonObject map)
        {
            foreach (var (name, node) in map)
                roots[name] = ReadBool(node, false);
        }
        TimeOnly time = PublishScheduler.ParsePublishTime(ReadString(g["time"])) ?? d.PublishTime;
        string endpoint = ReadString(g["endpoint_url"])?.Trim() ?? d.EndpointUrl;
        var scheme = ParseAuthScheme(ReadString(g["auth_scheme"])) ?? d.AuthScheme;
        string headerName = ReadString(g["auth_header_name"])?.Trim() is { Length: > 0 } h ? h : d.AuthHeaderName;
        var format = ParseBodyFormat(ReadString(g["body_format"])) ?? d.BodyFormat;
        var preset = ParsePreset(ReadString(g["preset"])) ?? d.Preset;
        string? lastResult = ReadString(g["last_result"]);
        DateTimeOffset? lastAttemptAt = ReadInstant(g["last_attempt_at"]);
        DateTimeOffset? lastOkAt = ReadInstant(g["last_ok_at"]);
        DateOnly? lastOkDate = null;
        if (DateOnly.TryParseExact(ReadString(g["last_ok_date"]), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var od))
            lastOkDate = od;

        return new PublishSettings(enabled, roots, time, endpoint, scheme, headerName, format, preset,
            tokenStored, lastAttemptAt, lastResult, lastOkDate, lastOkAt);
    }

    // -- migration ----------------------------------------------------------------

    /// <summary>Fold a pre-3.5 <c>publish_626</c> group into <c>publish</c>.
    /// Returns true when <paramref name="root"/> changed (the caller writes it
    /// back once). The old group described exactly one destination, so the
    /// migrated state is the 626 Labs preset with its URL, bearer auth, and the
    /// dashboard body format; enabled/roots/time/token presence/last_* carry
    /// over verbatim. An existing <c>publish</c> group wins field-by-field;
    /// the roots map is copied over only when the new group has none, so a
    /// share map is never lost. Idempotent: no legacy group, no change.</summary>
    public static bool Migrate(JsonObject root)
    {
        if (root[LegacyGroupKey] is not JsonObject legacy)
            return false;

        var g = Group(root);
        CopyIfMissing(legacy, g, "enabled");
        CopyIfMissing(legacy, g, "time");
        CopyIfMissing(legacy, g, "last_attempt_at");
        CopyIfMissing(legacy, g, "last_result");
        CopyIfMissing(legacy, g, "last_ok_date");
        CopyIfMissing(legacy, g, "last_ok_at");
        if (g["token_stored"] is null && legacy["key_stored"] is not null)
            g["token_stored"] = legacy["key_stored"]!.DeepClone();
        if (g["roots"] is not JsonObject && legacy["roots"] is JsonObject)
            g["roots"] = legacy["roots"]!.DeepClone();
        if (g["preset"] is null)
        {
            g["preset"] = PresetToken(PublishPreset.Labs626);
            if (g["endpoint_url"] is null) g["endpoint_url"] = PublishPresets.Labs626EndpointUrl;
            if (g["auth_scheme"] is null) g["auth_scheme"] = AuthSchemeToken(PublishAuthScheme.Bearer);
            if (g["auth_header_name"] is null) g["auth_header_name"] = DefaultAuthHeaderName;
            if (g["body_format"] is null) g["body_format"] = BodyFormatToken(PublishBodyFormat.Labs626);
        }
        root.Remove(LegacyGroupKey);
        return true;
    }

    // -- write --------------------------------------------------------------------

    /// <summary>The <c>publish</c> group, created empty when absent.</summary>
    public static JsonObject Group(JsonObject root)
    {
        if (root[GroupKey] is JsonObject g)
            return g;
        var created = new JsonObject();
        root[GroupKey] = created;
        return created;
    }

    public static void SetEnabled(JsonObject root, bool on) => Group(root)["enabled"] = on;

    public static void SetRoots(JsonObject root, IReadOnlyDictionary<string, bool> roots)
    {
        var map = new JsonObject();
        foreach (var (name, on) in roots)
            map[name] = on;
        Group(root)["roots"] = map;
    }

    public static void SetTime(JsonObject root, TimeOnly time)
        => Group(root)["time"] = PublishScheduler.FormatPublishTime(time);

    public static void SetTokenStored(JsonObject root, bool stored) => Group(root)["token_stored"] = stored;

    public static void SetEndpointUrl(JsonObject root, string url) => Group(root)["endpoint_url"] = url.Trim();

    public static void SetAuthScheme(JsonObject root, PublishAuthScheme scheme)
        => Group(root)["auth_scheme"] = AuthSchemeToken(scheme);

    public static void SetAuthHeaderName(JsonObject root, string name)
        => Group(root)["auth_header_name"] = name.Trim() is { Length: > 0 } n ? n : DefaultAuthHeaderName;

    /// <summary>Record the preset choice and the body format it implies.
    /// The 626 Labs preset also fills its URL and bearer auth.</summary>
    public static void SetPreset(JsonObject root, PublishPreset preset)
    {
        var g = Group(root);
        g["preset"] = PresetToken(preset);
        if (preset == PublishPreset.Labs626)
        {
            g["endpoint_url"] = PublishPresets.Labs626EndpointUrl;
            g["auth_scheme"] = AuthSchemeToken(PublishAuthScheme.Bearer);
            g["auth_header_name"] = DefaultAuthHeaderName;
            g["body_format"] = BodyFormatToken(PublishBodyFormat.Labs626);
        }
        else
        {
            g["body_format"] = BodyFormatToken(PublishBodyFormat.Sanduhr);
        }
    }

    /// <summary>Record one attempt. <paramref name="okDate"/> is the target day
    /// on success (null on failure — the scheduler keys "already published" off
    /// last_ok_date, so a failure never suppresses the retry).</summary>
    public static void SetAttempt(JsonObject root, DateTimeOffset at, string result, DateOnly? okDate)
    {
        var g = Group(root);
        g["last_attempt_at"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        g["last_result"] = result;
        if (okDate is { } d)
        {
            g["last_ok_date"] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            g["last_ok_at"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }
    }

    // -- tokens (the closed string vocabularies on disk) ----------------------------

    public static string AuthSchemeToken(PublishAuthScheme s) => s switch
    {
        PublishAuthScheme.Header => "header",
        PublishAuthScheme.None => "none",
        _ => "bearer",
    };

    public static PublishAuthScheme? ParseAuthScheme(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "bearer" => PublishAuthScheme.Bearer,
        "header" => PublishAuthScheme.Header,
        "none" => PublishAuthScheme.None,
        _ => null,
    };

    public static string BodyFormatToken(PublishBodyFormat f)
        => f == PublishBodyFormat.Labs626 ? "626labs" : "sanduhr";

    public static PublishBodyFormat? ParseBodyFormat(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "sanduhr" => PublishBodyFormat.Sanduhr,
        "626labs" => PublishBodyFormat.Labs626,
        _ => null,
    };

    public static string PresetToken(PublishPreset p)
        => p == PublishPreset.Labs626 ? "626labs" : "custom";

    public static PublishPreset? ParsePreset(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "custom" => PublishPreset.Custom,
        "626labs" => PublishPreset.Labs626,
        _ => null,
    };

    // -- helpers ------------------------------------------------------------------

    private static void CopyIfMissing(JsonObject from, JsonObject to, string key)
    {
        if (to[key] is null && from[key] is { } v)
            to[key] = v.DeepClone();
    }

    private static bool ReadBool(JsonNode? node, bool fallback)
    {
        try { return node?.GetValue<bool>() ?? fallback; }
        catch { return fallback; }
    }

    private static string? ReadString(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch { return null; }
    }

    private static DateTimeOffset? ReadInstant(JsonNode? node)
    {
        return DateTimeOffset.TryParse(ReadString(node), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;
    }
}
