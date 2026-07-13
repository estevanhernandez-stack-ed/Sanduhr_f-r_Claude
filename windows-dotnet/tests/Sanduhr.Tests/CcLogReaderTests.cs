using System.Globalization;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/CcLogReader.cs — ported from the Python build's
/// test_cc_logs.py. The Python <c>_fake_home</c> fixture (monkeypatching
/// <c>Path.home()</c>) maps to injecting a temp home into
/// <see cref="CcLogReader"/>.
/// </summary>
public class CcLogReaderTests
{
    private static string Iso(DateTimeOffset dt) => dt.ToString("O", CultureInfo.InvariantCulture);

    private static string AssistantEvent(
        string tsIso, string model, long inTok, long outTok, string? cwd = null, string? skill = null)
    {
        var ev = new JsonObject
        {
            ["type"] = "assistant",
            ["timestamp"] = tsIso,
            ["message"] = new JsonObject
            {
                ["model"] = model,
                ["usage"] = new JsonObject { ["input_tokens"] = inTok, ["output_tokens"] = outTok },
            },
        };
        if (cwd is not null) ev["cwd"] = cwd;
        if (skill is not null) ev["attributionSkill"] = skill;
        return ev.ToJsonString();
    }

    private static string WriteSession(string home, string root, string project, string uuid, params string[] lines)
    {
        var dir = Path.Combine(home, root, "projects", project);
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, uuid + ".jsonl");
        File.WriteAllText(f, string.Join("\n", lines) + "\n");
        return f;
    }

    [Fact]
    public void Search_roots_finds_existing_dirs()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, ".claude"));
        // .claude-personal does NOT exist
        var reader = new CcLogReader(temp.Path);
        Assert.Equal(new[] { Path.Combine(temp.Path, ".claude") }, reader.SearchRoots());
    }

    [Fact]
    public void Search_roots_finds_both_when_present()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, ".claude"));
        Directory.CreateDirectory(Path.Combine(temp.Path, ".claude-personal"));
        var reader = new CcLogReader(temp.Path);
        var roots = reader.SearchRoots();
        Assert.Contains(Path.Combine(temp.Path, ".claude"), roots);
        Assert.Contains(Path.Combine(temp.Path, ".claude-personal"), roots);
    }

    [Fact]
    public void Discover_log_files_handles_missing_projects_dir()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, ".claude")); // no projects/ subdir
        var reader = new CcLogReader(temp.Path);
        Assert.Empty(reader.DiscoverLogFiles());
    }

    [Fact]
    public void Discover_log_files_finds_session_jsonl()
    {
        using var temp = new TempDir();
        var f = WriteSession(temp.Path, ".claude", "C--proj", "abc",
            AssistantEvent("2026-05-05T10:00:00Z", "claude-opus-4-7", 100, 200));
        var reader = new CcLogReader(temp.Path);
        Assert.Equal(new[] { f }, reader.DiscoverLogFiles());
    }

    [Fact]
    public void Discover_log_files_spans_both_roots()
    {
        using var temp = new TempDir();
        var f1 = WriteSession(temp.Path, ".claude", "P1", "s1", "");
        var f2 = WriteSession(temp.Path, ".claude-personal", "P2", "s2", "");
        var reader = new CcLogReader(temp.Path);
        Assert.Equal(new HashSet<string> { f1, f2 }, new HashSet<string>(reader.DiscoverLogFiles()));
    }

    [Fact]
    public void Iter_usage_events_yields_only_assistant()
    {
        using var temp = new TempDir();
        var f = WriteSession(temp.Path, ".claude", "P", "s",
            "{\"type\":\"user\",\"timestamp\":\"2026-05-05T10:00:00Z\",\"message\":{\"role\":\"user\"}}",
            AssistantEvent("2026-05-05T10:01:00Z", "claude-opus-4-7", 100, 200,
                cwd: "C:/proj/Sanduhr", skill: "my-skill"),
            "{\"type\":\"system\",\"timestamp\":\"2026-05-05T10:02:00Z\"}");
        var reader = new CcLogReader(temp.Path);
        var events = reader.IterUsageEvents(f).ToList();
        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("claude-opus-4-7", ev.Model);
        Assert.Equal(100, ev.Usage.InputTokens);
        Assert.Equal(200, ev.Usage.OutputTokens);
        Assert.Equal("C:/proj/Sanduhr", ev.Cwd);
        Assert.Equal("my-skill", ev.AttributionSkill);
    }

    [Fact]
    public void Iter_usage_events_skips_malformed_lines()
    {
        using var temp = new TempDir();
        var f = WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent("2026-05-05T10:00:00Z", "claude-opus-4-7", 50, 50),
            "{not valid json",
            "{\"type\":\"assistant\"}",            // no message
            "{\"type\":\"assistant\",\"message\":{}}"); // no usage
        var reader = new CcLogReader(temp.Path);
        Assert.Single(reader.IterUsageEvents(f));
    }

    [Fact]
    public void Tokens_since_filters_by_cutoff()
    {
        using var temp = new TempDir();
        var baseTime = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime.AddHours(-1)), "claude-opus-4-7", 100, 100),
            AssistantEvent(Iso(baseTime.AddMinutes(5)), "claude-opus-4-7", 50, 50),
            AssistantEvent(Iso(baseTime.AddMinutes(10)), "claude-sonnet-4-6", 30, 70));
        var reader = new CcLogReader(temp.Path);
        var totals = reader.TokensSince(baseTime);
        Assert.Equal(2, totals.Count);
        Assert.Equal(100, totals["claude-opus-4-7"]);
        Assert.Equal(100, totals["claude-sonnet-4-6"]);
    }

    [Fact]
    public void Tokens_since_aggregates_across_sessions()
    {
        using var temp = new TempDir();
        var baseTime = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        WriteSession(temp.Path, ".claude", "P", "s1",
            AssistantEvent(Iso(baseTime.AddMinutes(1)), "claude-opus-4-7", 100, 200));
        WriteSession(temp.Path, ".claude-personal", "Q", "s2",
            AssistantEvent(Iso(baseTime.AddMinutes(2)), "claude-opus-4-7", 50, 150));
        var reader = new CcLogReader(temp.Path);
        var totals = reader.TokensSince(baseTime);
        Assert.Equal(500, totals["claude-opus-4-7"]);
        Assert.Single(totals);
    }

    [Fact]
    public void Tokens_since_skips_zero_token_events()
    {
        using var temp = new TempDir();
        var baseTime = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime.AddMinutes(1)), "claude-opus-4-7", 0, 0));
        var reader = new CcLogReader(temp.Path);
        Assert.Empty(reader.TokensSince(baseTime));
    }

    [Fact]
    public void Model_to_tier_key_maps_known_prefixes()
    {
        var reader = new CcLogReader();
        Assert.Equal("seven_day_opus", reader.ModelToTierKey("claude-opus-4-7"));
        Assert.Equal("seven_day_opus", reader.ModelToTierKey("claude-opus-4-6"));
        Assert.Equal("seven_day_sonnet", reader.ModelToTierKey("claude-sonnet-4-6"));
        Assert.Equal("seven_day", reader.ModelToTierKey("claude-haiku-4-5"));
    }

    [Fact]
    public void Model_to_tier_key_returns_none_for_unknown()
    {
        var reader = new CcLogReader();
        Assert.Null(reader.ModelToTierKey("gpt-4"));
        Assert.Null(reader.ModelToTierKey(null));
        Assert.Null(reader.ModelToTierKey(""));
    }

    [Fact]
    public void Tokens_since_by_tier_groups_by_sanduhr_tier()
    {
        using var temp = new TempDir();
        var baseTime = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime.AddMinutes(1)), "claude-opus-4-7", 100, 200),
            AssistantEvent(Iso(baseTime.AddMinutes(2)), "claude-sonnet-4-6", 50, 50),
            AssistantEvent(Iso(baseTime.AddMinutes(3)), "gpt-4o", 999, 999));
        var reader = new CcLogReader(temp.Path);
        var totals = reader.TokensSinceByTier(baseTime);
        Assert.Equal(2, totals.Count);
        Assert.Equal(300, totals["seven_day_opus"]);
        Assert.Equal(100, totals["seven_day_sonnet"]);
    }

    [Fact]
    public void Tokens_by_day_groups_by_local_date()
    {
        using var temp = new TempDir();
        var now = DateTimeOffset.UtcNow;
        var d1 = now.AddDays(-2);
        var d2 = now.AddDays(-1);
        var d3 = now;
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(d1), "claude-opus-4-7", 50, 50),
            AssistantEvent(Iso(d1.AddHours(5)), "claude-opus-4-7", 100, 100),
            AssistantEvent(Iso(d2), "claude-opus-4-7", 75, 25),
            AssistantEvent(Iso(d3), "claude-opus-4-7", 200, 300));
        var reader = new CcLogReader(temp.Path);
        var byDay = reader.TokensByDay(daysBack: 30);
        Assert.Equal(900, byDay.Values.Sum());
        Assert.InRange(byDay.Count, 1, 3);
    }

    [Fact]
    public void Tokens_by_day_drops_events_outside_window()
    {
        using var temp = new TempDir();
        var baseTime = DateTimeOffset.UtcNow;
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime.AddDays(-5)), "claude-opus-4-7", 100, 100),
            AssistantEvent(Iso(baseTime.AddDays(-60)), "claude-opus-4-7", 999, 999));
        var reader = new CcLogReader(temp.Path);
        Assert.Equal(200, reader.TokensByDay(daysBack: 30).Values.Sum());
    }

    [Fact]
    public void Tokens_by_project_groups_by_cwd()
    {
        using var temp = new TempDir();
        var baseTime = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime.AddMinutes(1)), "claude-opus-4-7", 100, 100, cwd: "C:/Users/dev/Projects/Sanduhr"),
            AssistantEvent(Iso(baseTime.AddMinutes(2)), "claude-opus-4-7", 50, 50, cwd: "C:/Users/dev/Projects/Sanduhr"),
            AssistantEvent(Iso(baseTime.AddMinutes(3)), "claude-opus-4-7", 70, 30, cwd: "C:/Users/dev/Projects/Other"),
            AssistantEvent(Iso(baseTime.AddMinutes(4)), "claude-opus-4-7", 999, 999)); // no cwd — skipped
        var reader = new CcLogReader(temp.Path);
        var byProj = reader.TokensByProject(baseTime);
        Assert.Equal(2, byProj.Count);
        Assert.Equal(300, byProj["C:/Users/dev/Projects/Sanduhr"]);
        Assert.Equal(100, byProj["C:/Users/dev/Projects/Other"]);
    }

    [Fact]
    public void Tokens_by_skill_groups_by_attribution()
    {
        using var temp = new TempDir();
        var baseTime = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime.AddMinutes(1)), "claude-opus-4-7", 100, 100, skill: "superpowers:debugging"),
            AssistantEvent(Iso(baseTime.AddMinutes(2)), "claude-opus-4-7", 50, 50, skill: "superpowers:debugging"),
            AssistantEvent(Iso(baseTime.AddMinutes(3)), "claude-opus-4-7", 70, 30, skill: "vibe-doc:scan"),
            AssistantEvent(Iso(baseTime.AddMinutes(4)), "claude-opus-4-7", 999, 999)); // no skill — skipped
        var reader = new CcLogReader(temp.Path);
        var bySkill = reader.TokensBySkill(baseTime);
        Assert.Equal(2, bySkill.Count);
        Assert.Equal(300, bySkill["superpowers:debugging"]);
        Assert.Equal(100, bySkill["vibe-doc:scan"]);
    }

    [Fact]
    public void Project_display_name_extracts_basename()
    {
        Assert.Equal("Sanduhr", CcLogReader.ProjectDisplayName("C:\\Users\\estev\\Projects\\Sanduhr"));
        Assert.Equal("Sanduhr", CcLogReader.ProjectDisplayName("C:/Users/estev/Projects/Sanduhr"));
        Assert.Equal("foo", CcLogReader.ProjectDisplayName("/home/dev/work/foo"));
        Assert.Equal("foo", CcLogReader.ProjectDisplayName("/home/dev/work/foo/"));
        Assert.Equal("", CcLogReader.ProjectDisplayName(""));
    }

    [Fact]
    public void Aggregate_for_local_cc_tab_combines_three_views()
    {
        using var temp = new TempDir();
        var baseTime = DateTimeOffset.UtcNow.AddDays(-1);
        WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime), "claude-opus-4-7", 100, 200, cwd: "C:/proj/Sanduhr", skill: "superpowers:debugging"),
            AssistantEvent(Iso(baseTime.AddMinutes(5)), "claude-opus-4-7", 50, 50, cwd: "C:/proj/Sanduhr", skill: "superpowers:debugging"),
            AssistantEvent(Iso(baseTime.AddMinutes(10)), "claude-opus-4-7", 30, 70, cwd: "C:/proj/Other")); // no skill
        var reader = new CcLogReader(temp.Path);
        reader.InvalidateCache();
        var agg = reader.AggregateForLocalCcTab(daysBack: 30);
        Assert.Equal(500, agg.ByDay.Values.Sum());
        Assert.Equal(2, agg.ByProject.Count);
        Assert.Equal(400, agg.ByProject["C:/proj/Sanduhr"]);
        Assert.Equal(100, agg.ByProject["C:/proj/Other"]);
        Assert.Equal(400, agg.BySkill["superpowers:debugging"]);
        Assert.Single(agg.BySkill);
    }

    [Fact]
    public void Aggregate_for_local_cc_tab_caches_within_ttl()
    {
        using var temp = new TempDir();
        var reader = new CcLogReader(temp.Path);
        reader.InvalidateCache();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        var f = WriteSession(temp.Path, ".claude", "P", "s",
            AssistantEvent(Iso(baseTime), "claude-opus-4-7", 100, 100));
        var first = reader.AggregateForLocalCcTab(daysBack: 30);
        Assert.Equal(200, first.ByDay.Values.Sum());

        // Append a new event; the cache should still return the first result.
        File.AppendAllText(f, AssistantEvent(Iso(baseTime.AddMinutes(10)), "claude-opus-4-7", 999, 999) + "\n");
        var second = reader.AggregateForLocalCcTab(daysBack: 30);
        Assert.Equal(200, second.ByDay.Values.Sum()); // unchanged — cache hit

        // After invalidation the new event lands.
        reader.InvalidateCache();
        var third = reader.AggregateForLocalCcTab(daysBack: 30);
        Assert.Equal(200 + 1998, third.ByDay.Values.Sum());
    }

    [Fact]
    public void DiscoverLogFiles_finds_nested_subagent_transcripts()
    {
        using var home = new TempDir();
        var projectDir = Path.Combine(home.Path, ".claude", "projects", "c--x-api");
        var nested = Path.Combine(projectDir, "11111111-2222-3333-4444-555555555555", "subagents");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(projectDir, "main.jsonl"), "");
        File.WriteAllText(Path.Combine(nested, "agent-abc.jsonl"), "");

        var reader = new CcLogReader(home.Path);
        var files = reader.DiscoverLogFiles();

        Assert.Contains(files, f => f.EndsWith("main.jsonl"));
        Assert.Contains(files, f => f.EndsWith("agent-abc.jsonl"));
    }

    [Fact]
    public void AggregateTodayOnly_restricts_to_local_today()
    {
        using var home = new TempDir();
        var today = DateTimeOffset.Now;
        var yesterday = today.AddDays(-1);
        WriteSession(home.Path, ".claude", "p1", "s1",
            AssistantEvent(Iso(yesterday), "claude-fable-5", 111, 0, cwd: "C:\\p\\api"),
            AssistantEvent(Iso(today), "claude-fable-5", 42, 0, cwd: "C:\\p\\api"));

        var reader = new CcLogReader(home.Path);
        var agg = reader.AggregateTodayOnly();

        // "Today" is wall-clock-relative: if local midnight rolled over
        // between building the fixture and aggregating, the assertions are
        // undefined — bail out rather than flake (a rerun covers it).
        if (DateOnly.FromDateTime(today.LocalDateTime) != DateOnly.FromDateTime(DateTime.Now))
            return;

        var todayKey = DateOnly.FromDateTime(today.LocalDateTime);
        Assert.Equal(42, agg.ByDay.GetValueOrDefault(todayKey));
        Assert.Single(agg.ByDay);                             // yesterday excluded
        Assert.Equal(42, agg.ByProject.Values.Sum());
    }
}
