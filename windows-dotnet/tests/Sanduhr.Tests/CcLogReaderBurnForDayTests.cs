using System.Globalization;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>The publisher's date-bounded, per-root burn: one local calendar
/// day, one Claude Code home, basenames + tiers. Timestamps are built from a
/// LOCAL wall-clock so the day filter is timezone-proof on any CI box.</summary>
public class CcLogReaderBurnForDayTests
{
    private static readonly DateOnly Day = new(2026, 9, 12);

    private static string LocalIso(DateOnly day, int hour)
    {
        var dt = day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))
            .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Event(string tsIso, string? model, long inTok, long outTok, string? cwd)
    {
        var msg = new JsonObject
        {
            ["usage"] = new JsonObject { ["input_tokens"] = inTok, ["output_tokens"] = outTok },
        };
        if (model is not null) msg["model"] = model;
        var ev = new JsonObject { ["type"] = "assistant", ["timestamp"] = tsIso, ["message"] = msg };
        if (cwd is not null) ev["cwd"] = cwd;
        return ev.ToJsonString();
    }

    private static void WriteSession(string root, string project, params string[] lines)
    {
        var dir = Path.Combine(root, "projects", project);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jsonl"), string.Join("\n", lines) + "\n");
    }

    [Fact]
    public void Counts_only_the_requested_local_day_and_only_the_given_root()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        string work = Path.Combine(tmp.Path, ".claude");
        WriteSession(personal, "c--sanduhr",
            Event(LocalIso(Day, 10), "claude-fable-5", 100, 200, @"C:\Users\estev\Projects\Sanduhr"),
            Event(LocalIso(Day, 23), "claude-sonnet-5", 10, 20, @"C:\Users\estev\Projects\Sanduhr"),
            Event(LocalIso(Day.AddDays(-1), 12), "claude-fable-5", 999, 0, @"C:\Users\estev\Projects\Sanduhr"),
            Event(LocalIso(Day.AddDays(1), 1), "claude-fable-5", 777, 0, @"C:\Users\estev\Projects\Sanduhr"));
        WriteSession(work, "c--wbp",
            Event(LocalIso(Day, 12), "claude-opus-4", 500, 500, @"C:\Users\estev\Projects\Marcus\wbp"));

        var burn = new CcLogReader(tmp.Path).BurnForLocalDay(".claude-personal", personal, Day);

        Assert.Equal(".claude-personal", burn.Root);
        Assert.Equal(330, burn.Total);
        Assert.Equal(330, burn.ByProject["Sanduhr"]);   // basename, never the path
        Assert.DoesNotContain("wbp", burn.ByProject.Keys);
        Assert.Equal(300, burn.ByTier[TierModel.SevenDayFable]);
        Assert.Equal(30, burn.ByTier["seven_day_sonnet"]);
    }

    [Fact]
    public void Unknown_cwd_and_unmapped_models_stay_in_the_total()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        WriteSession(personal, "c--x",
            Event(LocalIso(Day, 9), "mystery-model", 40, 2, null),
            Event(LocalIso(Day, 9), null, 0, 0, @"C:\p\zero"));   // zero tokens: skipped

        var burn = new CcLogReader(tmp.Path).BurnForLocalDay(".claude-personal", personal, Day);

        Assert.Equal(42, burn.Total);
        Assert.Equal(42, burn.ByProject["(unknown)"]);
        Assert.Empty(burn.ByTier);
        Assert.DoesNotContain("zero", burn.ByProject.Keys);
    }

    [Fact]
    public void Missing_projects_dir_is_an_empty_burn_not_an_error()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        Directory.CreateDirectory(personal);
        var burn = new CcLogReader(tmp.Path).BurnForLocalDay(".claude-personal", personal, Day);
        Assert.Equal(0, burn.Total);
        Assert.Empty(burn.ByProject);
    }

    [Fact]
    public void Nested_subagent_transcripts_are_included()
    {
        using var tmp = new TempDir();
        string personal = Path.Combine(tmp.Path, ".claude-personal");
        var nested = Path.Combine(personal, "projects", "c--sanduhr", "parent-uuid", "sub");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "agent.jsonl"),
            Event(LocalIso(Day, 15), "claude-fable-5", 5, 5, @"C:\Users\estev\Projects\Sanduhr") + "\n");

        var burn = new CcLogReader(tmp.Path).BurnForLocalDay(".claude-personal", personal, Day);
        Assert.Equal(10, burn.Total);
    }
}
