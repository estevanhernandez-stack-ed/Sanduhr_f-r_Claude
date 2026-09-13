using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// The settings.json <c>publish</c> group: defaults when absent, the closed
/// string vocabularies, and the one-time fold of the pre-3.5 <c>publish_626</c>
/// group (which described exactly one destination) into the vendor-agnostic
/// shape — without ever losing the roots share map.
/// </summary>
public class PublishSettingsJsonTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    // -- defaults ---------------------------------------------------------------

    [Fact]
    public void Absent_group_reads_as_everything_off_and_no_destination()
    {
        var s = PublishSettingsJson.Read(Parse("""{"theme":"obsidian"}"""));

        Assert.False(s.Enabled);
        Assert.Empty(s.Roots);
        Assert.Equal(PublishScheduler.DefaultPublishTime, s.PublishTime);
        Assert.Equal("", s.EndpointUrl);
        Assert.False(s.HasEndpoint);
        Assert.Equal(PublishAuthScheme.Bearer, s.AuthScheme);
        Assert.Equal("Authorization", s.AuthHeaderName);
        Assert.Equal(PublishBodyFormat.Sanduhr, s.BodyFormat);
        Assert.Equal(PublishPreset.Custom, s.Preset);
        Assert.False(s.TokenStored);
        Assert.Null(s.LastAttemptAt);
        Assert.Null(s.LastResult);
        Assert.Null(s.LastOkDate);
        Assert.Null(s.LastOkAt);
    }

    [Fact]
    public void Unknown_vocabulary_values_fall_back_to_defaults_not_exceptions()
    {
        var s = PublishSettingsJson.Read(Parse("""
            {"publish": {"enabled": "yes", "auth_scheme": "magic", "body_format": "xml",
                         "preset": "acme", "auth_header_name": "  ", "time": "25:99",
                         "roots": {".claude": 1, ".claude-personal": true}}}
            """));

        Assert.False(s.Enabled);
        Assert.Equal(PublishAuthScheme.Bearer, s.AuthScheme);
        Assert.Equal(PublishBodyFormat.Sanduhr, s.BodyFormat);
        Assert.Equal(PublishPreset.Custom, s.Preset);
        Assert.Equal("Authorization", s.AuthHeaderName);
        Assert.Equal(PublishScheduler.DefaultPublishTime, s.PublishTime);
        Assert.False(s.Roots[".claude"]);
        Assert.True(s.Roots[".claude-personal"]);
    }

    [Fact]
    public void Full_group_round_trips_every_field()
    {
        var root = new JsonObject();
        PublishSettingsJson.SetEnabled(root, true);
        PublishSettingsJson.SetRoots(root, new Dictionary<string, bool> { [".claude-personal"] = true, [".claude"] = false });
        PublishSettingsJson.SetTime(root, new TimeOnly(7, 30));
        PublishSettingsJson.SetEndpointUrl(root, "  https://collector.example/usage  ");
        PublishSettingsJson.SetAuthScheme(root, PublishAuthScheme.Header);
        PublishSettingsJson.SetAuthHeaderName(root, "X-Api-Key");
        PublishSettingsJson.SetTokenStored(root, true);
        PublishSettingsJson.SetAttempt(root, new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.Zero), "Published.", new DateOnly(2026, 9, 12));

        var s = PublishSettingsJson.Read(Parse(root.ToJsonString()));

        Assert.True(s.Enabled);
        Assert.True(s.Roots[".claude-personal"]);
        Assert.False(s.Roots[".claude"]);
        Assert.Equal(new TimeOnly(7, 30), s.PublishTime);
        Assert.Equal("https://collector.example/usage", s.EndpointUrl);
        Assert.Equal(PublishAuthScheme.Header, s.AuthScheme);
        Assert.Equal("X-Api-Key", s.AuthHeaderName);
        Assert.True(s.TokenStored);
        Assert.Equal("Published.", s.LastResult);
        Assert.Equal(new DateOnly(2026, 9, 12), s.LastOkDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.Zero), s.LastAttemptAt);
        Assert.Equal(s.LastAttemptAt, s.LastOkAt);
        Assert.Equal(new PublishTarget("https://collector.example/usage", PublishAuthScheme.Header, "X-Api-Key"), s.Target);
    }

    [Fact]
    public void Blank_header_name_falls_back_to_Authorization()
    {
        var root = new JsonObject();
        PublishSettingsJson.SetAuthHeaderName(root, "   ");
        Assert.Equal("Authorization", PublishSettingsJson.Read(root).AuthHeaderName);
    }

    [Fact]
    public void Failed_attempt_records_the_attempt_but_not_a_success_day()
    {
        var root = new JsonObject();
        PublishSettingsJson.SetAttempt(root, new DateTimeOffset(2026, 9, 13, 6, 45, 0, TimeSpan.Zero), "HTTP 500.", null);
        var s = PublishSettingsJson.Read(root);
        Assert.NotNull(s.LastAttemptAt);
        Assert.Equal("HTTP 500.", s.LastResult);
        Assert.Null(s.LastOkDate);
        Assert.Null(s.LastOkAt);
    }

    // -- presets ------------------------------------------------------------------

    [Fact]
    public void Choosing_the_626_Labs_preset_fills_url_bearer_and_dashboard_format()
    {
        var root = new JsonObject();
        PublishSettingsJson.SetEndpointUrl(root, "https://elsewhere.example/x");
        PublishSettingsJson.SetAuthScheme(root, PublishAuthScheme.None);

        PublishSettingsJson.SetPreset(root, PublishPreset.Labs626);
        var s = PublishSettingsJson.Read(root);

        Assert.Equal(PublishPreset.Labs626, s.Preset);
        Assert.Equal(PublishPresets.Labs626EndpointUrl, s.EndpointUrl);
        Assert.Equal(PublishAuthScheme.Bearer, s.AuthScheme);
        Assert.Equal("Authorization", s.AuthHeaderName);
        Assert.Equal(PublishBodyFormat.Labs626, s.BodyFormat);
    }

    [Fact]
    public void Choosing_custom_keeps_the_url_and_auth_but_uses_the_plain_body_format()
    {
        var root = new JsonObject();
        PublishSettingsJson.SetPreset(root, PublishPreset.Labs626);
        PublishSettingsJson.SetPreset(root, PublishPreset.Custom);
        var s = PublishSettingsJson.Read(root);

        Assert.Equal(PublishPreset.Custom, s.Preset);
        Assert.Equal(PublishPresets.Labs626EndpointUrl, s.EndpointUrl);   // the user's field, untouched
        Assert.Equal(PublishAuthScheme.Bearer, s.AuthScheme);
        Assert.Equal(PublishBodyFormat.Sanduhr, s.BodyFormat);
    }

    [Theory]
    [InlineData(PublishAuthScheme.Bearer, "bearer")]
    [InlineData(PublishAuthScheme.Header, "header")]
    [InlineData(PublishAuthScheme.None, "none")]
    public void Auth_scheme_vocabulary_is_closed_and_round_trips(PublishAuthScheme scheme, string token)
    {
        Assert.Equal(token, PublishSettingsJson.AuthSchemeToken(scheme));
        Assert.Equal(scheme, PublishSettingsJson.ParseAuthScheme(token.ToUpperInvariant()));
    }

    [Fact]
    public void Body_format_and_preset_vocabularies_round_trip()
    {
        Assert.Equal("sanduhr", PublishSettingsJson.BodyFormatToken(PublishBodyFormat.Sanduhr));
        Assert.Equal("626labs", PublishSettingsJson.BodyFormatToken(PublishBodyFormat.Labs626));
        Assert.Equal(PublishBodyFormat.Labs626, PublishSettingsJson.ParseBodyFormat("626labs"));
        Assert.Null(PublishSettingsJson.ParseBodyFormat("record"));
        Assert.Equal("custom", PublishSettingsJson.PresetToken(PublishPreset.Custom));
        Assert.Equal("626labs", PublishSettingsJson.PresetToken(PublishPreset.Labs626));
        Assert.Equal(PublishPreset.Labs626, PublishSettingsJson.ParsePreset("626labs"));
        Assert.Null(PublishSettingsJson.ParsePreset(""));
    }

    // -- migration from publish_626 ---------------------------------------------

    private const string LegacyJson = """
        {"theme": "obsidian",
         "publish_626": {"enabled": true, "roots": {".claude-personal": true, ".claude": false},
                         "time": "07:15", "key_stored": true,
                         "last_attempt_at": "2026-09-12T06:45:00.0000000+00:00", "last_result": "Published.",
                         "last_ok_date": "2026-09-11", "last_ok_at": "2026-09-12T06:45:00.0000000+00:00"}}
        """;

    [Fact]
    public void Legacy_group_migrates_to_the_626_Labs_preset_and_is_removed()
    {
        var root = Parse(LegacyJson);

        Assert.True(PublishSettingsJson.Migrate(root));
        var s = PublishSettingsJson.Read(root);

        Assert.Null(root["publish_626"]);
        Assert.Equal("obsidian", (string?)root["theme"]);   // siblings untouched
        Assert.True(s.Enabled);
        Assert.True(s.Roots[".claude-personal"]);
        Assert.False(s.Roots[".claude"]);
        Assert.Equal(new TimeOnly(7, 15), s.PublishTime);
        Assert.True(s.TokenStored);
        Assert.Equal(PublishPreset.Labs626, s.Preset);
        Assert.Equal(PublishPresets.Labs626EndpointUrl, s.EndpointUrl);
        Assert.Equal(PublishAuthScheme.Bearer, s.AuthScheme);
        Assert.Equal("Authorization", s.AuthHeaderName);
        Assert.Equal(PublishBodyFormat.Labs626, s.BodyFormat);
        Assert.Equal("Published.", s.LastResult);
        Assert.Equal(new DateOnly(2026, 9, 11), s.LastOkDate);
        Assert.NotNull(s.LastAttemptAt);
        Assert.NotNull(s.LastOkAt);
        // The on-disk vocabulary is the new one — no key_stored leaks through.
        Assert.Null(root["publish"]!["key_stored"]);
        Assert.Equal("626labs", (string?)root["publish"]!["preset"]);
    }

    [Fact]
    public void Migration_is_a_one_time_write_then_idempotent()
    {
        var root = Parse(LegacyJson);
        Assert.True(PublishSettingsJson.Migrate(root));
        string after = root.ToJsonString();
        Assert.False(PublishSettingsJson.Migrate(root));
        Assert.Equal(after, root.ToJsonString());
    }

    [Fact]
    public void No_legacy_group_means_no_change()
    {
        var root = Parse("""{"theme":"obsidian"}""");
        Assert.False(PublishSettingsJson.Migrate(root));
        Assert.Null(root["publish"]);
        var withNew = Parse("""{"publish": {"enabled": true, "preset": "custom", "endpoint_url": "https://x.example/"}}""");
        Assert.False(PublishSettingsJson.Migrate(withNew));
    }

    [Fact]
    public void When_both_groups_exist_the_new_one_wins_but_a_missing_roots_map_is_rescued()
    {
        var root = Parse("""
            {"publish_626": {"enabled": true, "roots": {".claude-personal": true}, "key_stored": true},
             "publish": {"enabled": false, "preset": "custom", "endpoint_url": "https://mine.example/usage",
                         "auth_scheme": "header", "auth_header_name": "X-Key", "body_format": "sanduhr"}}
            """);

        Assert.True(PublishSettingsJson.Migrate(root));
        var s = PublishSettingsJson.Read(root);

        Assert.Null(root["publish_626"]);
        Assert.False(s.Enabled);                                   // new group's value
        Assert.Equal(PublishPreset.Custom, s.Preset);              // new group's preset, not forced to 626labs
        Assert.Equal("https://mine.example/usage", s.EndpointUrl);
        Assert.Equal(PublishAuthScheme.Header, s.AuthScheme);
        Assert.Equal("X-Key", s.AuthHeaderName);
        Assert.Equal(PublishBodyFormat.Sanduhr, s.BodyFormat);
        Assert.True(s.Roots[".claude-personal"]);                  // rescued from legacy
        Assert.True(s.TokenStored);                                // rescued from legacy key_stored
    }

    [Fact]
    public void Legacy_roots_map_survives_even_when_everything_else_is_missing()
    {
        var root = Parse("""{"publish_626": {"roots": {".claude": true}}}""");
        Assert.True(PublishSettingsJson.Migrate(root));
        var s = PublishSettingsJson.Read(root);
        Assert.True(s.Roots[".claude"]);
        Assert.False(s.Enabled);
        Assert.False(s.TokenStored);
        Assert.Equal(PublishPreset.Labs626, s.Preset);
    }
}
