using System.Globalization;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// The Desk message's settings (spec 6.2), settings.json <c>desk_message</c>: the pinned line, the
/// rotation, the mix switch, how special days show and for how long each line, and the message's own
/// look (size, color, glow, font). Absent or odd members are their defaults, so an install without
/// the key reads as the Mac's defaults.
/// </summary>
public sealed record DeskMessageSettings(
    string Pin, bool Hourly, bool Mix, MessageSpecialMode Special, double Seconds,
    double Size, string Color, bool Glow, string Font)
{
    public const string Key = "desk_message";
    public const double DefaultSize = 84;
    public const double MinSize = 24;
    public const double MaxSize = 180;

    public static DeskMessageSettings Default { get; } =
        new("", false, false, MessageSpecialMode.Stack, MessageSpecialModes.DefaultSeconds, DefaultSize, "", true, "");

    /// <summary>The pin as the engine takes it: null when empty.</summary>
    public string? PinnedLine => string.IsNullOrWhiteSpace(Pin) ? null : Pin.Trim();

    public static DeskMessageSettings Decode(JsonNode? node)
    {
        var d = Default;
        if (node is not JsonObject o) return d;
        double seconds = SettingsJson.Number(o, "seconds", d.Seconds);
        double size = SettingsJson.Number(o, "size", d.Size);
        return new DeskMessageSettings(
            SettingsJson.String(o, "pin", d.Pin),
            SettingsJson.String(o, "rotate", "daily") == "hourly",
            SettingsJson.Bool(o, "mix", d.Mix),
            MessageSpecialModes.Parse(SettingsJson.String(o, "special", "stack")),
            MessageSpecialModes.Presets.Contains(seconds) ? seconds : MessageSpecialModes.DefaultSeconds,
            double.IsFinite(size) ? Math.Clamp(Math.Round(size), MinSize, MaxSize) : DefaultSize,
            SettingsJson.String(o, "color", d.Color),
            SettingsJson.Bool(o, "glow", d.Glow),
            SettingsJson.String(o, "font", d.Font));
    }

    public JsonObject Encode() => new()
    {
        ["pin"] = Pin,
        ["rotate"] = Hourly ? "hourly" : "daily",
        ["mix"] = Mix,
        ["special"] = MessageSpecialModes.Word(Special),
        ["seconds"] = Seconds,
        ["size"] = Size,
        ["color"] = Color,
        ["glow"] = Glow,
        ["font"] = Font,
    };
}

/// <summary>
/// Now playing's settings (spec 6.3), settings.json <c>now_playing</c>: the widget line (off), keep
/// showing while paused (off: a paused session hides the piece after 5 s), the excluded apps (by app id,
/// picked from apps seen this session) and the bars beside the Desk piece (on). Titles are never stored
/// here or anywhere. The retired <c>hide_paused</c> key is read by nothing: it used to make a paused
/// track hide at once, and hiding after the grace is now the default for everyone; it is dropped the
/// next time the page saves. <c>style</c> is "Style what's playing" (off): the Desk draws the song in its look (a gradient and a
/// letter style, saved or seeded from the song). <c>looks_direct</c> is "Let Claude style songs directly" (off): a look Claude
/// proposes is saved without the card. <c>bars_gradient</c> is "Gradient bars" (off, spec 3.8 G): the bars spread the ink's colors
/// across the row and fade toward their base.
/// </summary>
public sealed record NowPlayingSettings(bool Widget, bool KeepPaused, IReadOnlyList<string> Excluded, bool Bars, bool Style = false, bool LooksDirect = false,
    bool BarsGradient = false)
{
    public const string Key = "now_playing";

    public static NowPlayingSettings Default { get; } = new(false, false, [], true);

    public NowPlayingPrefs Prefs => new() { KeepWhilePaused = KeepPaused, Excluded = Excluded };

    public static NowPlayingSettings Decode(JsonNode? node)
    {
        var d = Default;
        if (node is not JsonObject o) return d;
        var excluded = new List<string>();
        if (o["excluded"] is JsonArray a)
            foreach (var n in a)
                if (n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) && !excluded.Contains(s))
                    excluded.Add(s);
        return new NowPlayingSettings(
            SettingsJson.Bool(o, "widget", d.Widget),
            SettingsJson.Bool(o, "keep_paused", d.KeepPaused),
            excluded,
            SettingsJson.Bool(o, "visualizer", d.Bars),
            SettingsJson.Bool(o, "style", d.Style),
            SettingsJson.Bool(o, "looks_direct", d.LooksDirect),
            SettingsJson.Bool(o, "bars_gradient", d.BarsGradient));
    }

    public JsonObject Encode()
    {
        var o = new JsonObject
        {
            ["widget"] = Widget,
            ["keep_paused"] = KeepPaused,
            ["excluded"] = new JsonArray([.. Excluded.Select(e => (JsonNode?)JsonValue.Create(e))]),
            ["visualizer"] = Bars,
        };
        // The looks switches are written only while on, so a settings file from before them stays as it was.
        if (Style) o["style"] = true;
        if (LooksDirect) o["looks_direct"] = true;
        if (BarsGradient) o["bars_gradient"] = true;
        return o;
    }
}

/// <summary>
/// The weather piece's place (spec 6.5), settings.json <c>desk_weather</c>: the place picked in
/// Settings, Weather (name, admin area, country and coordinates rounded to two decimals) and the unit.
/// Absent: no place, and the unit follows the region. <c>nickname</c> (#315, "Show it as") is the name the Desk shows instead of
/// the place's, up to <see cref="DeskDesign.WeatherNicknameMax"/> characters on one line; written only when set. It stays on this
/// PC: the place search and the forecast never see it.
/// </summary>
public sealed record DeskWeatherSettings(WeatherPlace? Place, WeatherUnit? Unit, string Nickname = "")
{
    public const string Key = "desk_weather";

    public static DeskWeatherSettings Default { get; } = new(null, null);

    /// <summary>A nickname as it is kept: control characters dropped, trimmed, and cut to <see cref="DeskDesign.WeatherNicknameMax"/>
    /// characters (whole characters, never half of a pair).</summary>
    public static string NormalizeNickname(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var rune in raw.EnumerateRunes())
            if (!System.Text.Rune.IsControl(rune) && rune.Value is not (0x2028 or 0x2029)) sb.Append(rune.ToString());
        var runes = sb.ToString().Trim().EnumerateRunes().Take(DeskDesign.WeatherNicknameMax).Select(r => r.ToString());
        return string.Concat(runes).Trim();
    }

    /// <summary>What the weather piece shows as its heading: the nickname when there is one, else the place's own name.</summary>
    public string Shown(WeatherPlace place) => Nickname.Length > 0 ? Nickname : place.Name;

    /// <summary>The unit to ask for: the saved one, else the region's.</summary>
    public WeatherUnit UnitOr(bool isMetric) => Unit ?? Weather.DefaultUnit(isMetric);

    public static DeskWeatherSettings Decode(JsonNode? node)
    {
        if (node is not JsonObject o) return Default;
        WeatherUnit? unit = SettingsJson.String(o, "unit", "") switch
        {
            "celsius" => WeatherUnit.Celsius,
            "fahrenheit" => WeatherUnit.Fahrenheit,
            _ => null,
        };
        var name = SettingsJson.String(o, "name", "").Trim();
        double lat = SettingsJson.Number(o, "lat", double.NaN), lon = SettingsJson.Number(o, "lon", double.NaN);
        WeatherPlace? place = null;
        if (name.Length > 0 && double.IsFinite(lat) && double.IsFinite(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
        {
            string? admin = SettingsJson.String(o, "admin", "");
            string? country = SettingsJson.String(o, "country", "");
            place = WeatherPlace.Create(name, admin.Length > 0 ? admin : null, country.Length > 0 ? country : null, lat, lon);
        }
        return new DeskWeatherSettings(place, unit, NormalizeNickname(SettingsJson.String(o, "nickname", "")));
    }

    public JsonObject Encode()
    {
        var o = new JsonObject();
        if (Place is { } p)
        {
            o["name"] = p.Name;
            if (p.Admin is { } a) o["admin"] = a;
            if (p.Country is { } c) o["country"] = c;
            o["lat"] = Math.Round(p.Lat, 2, MidpointRounding.AwayFromZero);
            o["lon"] = Math.Round(p.Lon, 2, MidpointRounding.AwayFromZero);
        }
        if (Unit is { } u) o["unit"] = u == WeatherUnit.Celsius ? "celsius" : "fahrenheit";
        // Written only when set, so a settings file from before nicknames stays as it was.
        if (NormalizeNickname(Nickname) is { Length: > 0 } nick) o["nickname"] = nick;
        return o;
    }
}

/// <summary>Tolerant reads of one member of a settings object: a missing or odd member is the default.</summary>
public static class SettingsJson
{
    public static bool Bool(JsonObject? o, string key, bool fallback) =>
        o?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    public static string String(JsonObject? o, string key, string fallback) =>
        o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    public static double Number(JsonObject? o, string key, double fallback)
    {
        if (o?[key] is not JsonValue v) return fallback;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        return fallback;
    }
}
