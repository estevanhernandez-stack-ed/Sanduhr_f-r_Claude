using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>Read-side contract: window merges, weekly buckets + no-record
/// coverage, session slice merge, scope math, and the meta projections the
/// degraded-mode gate and Trends footer consume. Fixtures write shards through
/// VaultStore directly — reader tests need no ingester.</summary>
public class VaultReaderTests
{
    private static VaultSessionRow Row(
        string projectKey, string firstTs, string lastTs, bool continuation,
        params (string Day, long Total)[] days)
    {
        var row = new VaultSessionRow
        {
            ProjectKey = projectKey,
            ProjectName = VaultReader.ProjectNameOf(projectKey),
            FirstTs = firstTs,
            LastTs = lastTs,
            UtcOffsetMin = -300,
            EventCount = continuation ? 0 : days.Length,
            Continuation = continuation,
            CacheTokens = continuation ? null : new VaultCacheTokens { Read = 1, Creation = 1 },
            ByDay = days.ToDictionary(
                d => d.Day,
                d => new VaultDayBucket
                {
                    Total = d.Total,
                    ByModel = new Dictionary<string, long> { ["claude-fable-5"] = d.Total },
                }),
        };
        VaultRowMath.RecomputeRowAggregates(row);
        return row;
    }

    private static void SaveShard(VaultStore store, string root, string month,
        params (string Uuid, VaultSessionRow Row)[] rows)
    {
        var shard = new VaultSessionShard { SchemaVersion = 1, WriterVersion = "test" };
        foreach (var (uuid, row) in rows)
            shard.Sessions[uuid] = row;
        store.SaveSessionShard(root, month, shard);
    }

    private static void SaveRollup(VaultStore store, string root, string month,
        params (string Day, long Total, string ProjectKey, string Skill)[] days)
    {
        var shard = new VaultRollupShard { SchemaVersion = 1 };
        foreach (var (day, total, projectKey, skill) in days)
        {
            if (!shard.Days.TryGetValue(day, out var d))
                shard.Days[day] = d = new VaultRollupDay();
            d.Total += total;
            d.Sessions++;
            d.ByModel["claude-fable-5"] = d.ByModel.GetValueOrDefault("claude-fable-5") + total;
            d.ByProject[projectKey] = d.ByProject.GetValueOrDefault(projectKey) + total;
            if (skill.Length > 0)
                d.BySkill[skill] = d.BySkill.GetValueOrDefault(skill) + total;
        }
        store.SaveRollupShard(root, month, shard);
    }

    [Fact]
    public void ReadWindow_merges_roots_and_projects_by_display_name()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        // Same display name "api", different hashes, different roots.
        SaveRollup(store, ".claude", "2026-07",
            ("2026-07-10", 100, "api~aaaaaaaa", "code-review"),
            ("2026-07-11", 50, "api~aaaaaaaa", ""));
        SaveRollup(store, ".claude-personal", "2026-07",
            ("2026-07-10", 30, "api~bbbbbbbb", ""));

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude", ".claude-personal" },
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));

        Assert.Equal(130, w.ByDay[new DateOnly(2026, 7, 10)]);
        Assert.Equal(50, w.ByDay[new DateOnly(2026, 7, 11)]);
        Assert.Equal(180, w.ByProjectName["api"]);           // merged by display name
        Assert.Equal(100, w.BySkill["code-review"]);
    }

    [Fact]
    public void ReadWindow_excludes_toExclusive_day()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveRollup(store, ".claude", "2026-07",
            ("2026-07-11", 50, "api~aaaaaaaa", ""),
            ("2026-07-12", 999, "api~aaaaaaaa", ""));        // "today" — must not leak in

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));

        Assert.False(w.ByDay.ContainsKey(new DateOnly(2026, 7, 12)));
        Assert.Equal(50, w.ByDay.Values.Sum());
        Assert.Equal(50, w.ByProjectName["api"]);            // breakdowns respect the boundary too
    }

    [Fact]
    public void ReadWindow_spans_months()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveRollup(store, ".claude", "2026-06", ("2026-06-30", 10, "api~aaaaaaaa", ""));
        SaveRollup(store, ".claude", "2026-07", ("2026-07-01", 20, "api~aaaaaaaa", ""));

        var reader = new VaultReader(store);
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 12));
        Assert.Equal(30, w.ByDay.Values.Sum());
    }

    [Fact]
    public void Weeks_bucket_monday_start_and_flag_current()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        // 2026-07-12 is a Sunday; its week starts Monday 2026-07-06.
        SaveRollup(store, ".claude", "2026-07",
            ("2026-07-06", 100, "api~aaaaaaaa", ""),
            ("2026-07-01", 40, "api~aaaaaaaa", ""));         // previous week (Mon 2026-06-29)
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-06-01",
            Covered = new List<VaultDateRange> { new() { From = "2026-06-01", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });

        var reader = new VaultReader(store);
        var weeks = reader.ReadWeeks(new[] { ".claude" }, 4, new DateOnly(2026, 7, 12));

        Assert.Equal(4, weeks.Count);
        Assert.Equal(new DateOnly(2026, 7, 6), weeks[^1].WeekStart);
        Assert.True(weeks[^1].IsCurrent);
        Assert.False(weeks[^2].IsCurrent);
        Assert.Equal(100, weeks[^1].Total);
        Assert.Equal(40, weeks[^2].Total);
        Assert.All(weeks, w => Assert.False(w.HasNoRecordGap));   // fully covered window
    }

    [Fact]
    public void Weeks_flag_no_record_for_uncovered_and_pre_vault_days()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            // Widget-off fortnight: coverage resumes 2026-07-08.
            Covered = new List<VaultDateRange>
            {
                new() { From = "2026-06-25", To = "2026-07-01" },
                new() { From = "2026-07-08", To = "2026-07-12" },
            },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });

        var reader = new VaultReader(store);
        var weeks = reader.ReadWeeks(new[] { ".claude" }, 4, new DateOnly(2026, 7, 12));

        // Today is Sunday 2026-07-12, so the 4 week starts are 06-15, 06-22,
        // 06-29, 07-06. Coverage: [06-25..07-01] and [07-08..07-12].
        Assert.Equal(new DateOnly(2026, 6, 15), weeks[0].WeekStart);
        Assert.True(weeks[0].HasNoRecordGap);   // 06-15..06-21 fully pre-vault
        Assert.True(weeks[1].HasNoRecordGap);   // 06-22..06-24 uncovered
        Assert.True(weeks[2].HasNoRecordGap);   // 07-02..07-05 uncovered (widget-off gap)
        Assert.True(weeks[3].IsCurrent);
        Assert.True(weeks[3].HasNoRecordGap);   // 07-06/07-07 uncovered
    }

    [Fact]
    public void Coverage_is_the_intersection_of_consented_roots()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            Covered = new List<VaultDateRange> { new() { From = "2026-07-01", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });
        store.SaveMeta(".claude-personal", new VaultRootMeta
        {
            Since = "2026-07-05",
            Covered = new List<VaultDateRange> { new() { From = "2026-07-05", To = "2026-07-12" } },
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });

        var reader = new VaultReader(store);
        var roots = new[] { ".claude", ".claude-personal" };
        Assert.True(reader.IsDayCovered(roots, new DateOnly(2026, 7, 6)));
        Assert.False(reader.IsDayCovered(roots, new DateOnly(2026, 7, 3)));   // personal lacks it
        Assert.False(reader.IsDayCovered(roots, new DateOnly(2026, 6, 20)));  // nobody has it
        Assert.True(reader.IsDayCovered(new[] { ".claude" }, new DateOnly(2026, 7, 3)));
    }

    [Fact]
    public void Sessions_merge_slices_by_uuid_within_a_root()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        SaveShard(store, ".claude", "2026-07",
            ("u1", Row("api~aaaaaaaa", "2026-07-31T20:00:00+00:00", "2026-08-01T22:00:00+00:00",
                continuation: false, ("2026-07-31", 100))));
        SaveShard(store, ".claude", "2026-08",
            ("u1", Row("api~aaaaaaaa", "2026-07-31T20:00:00+00:00", "2026-08-01T22:00:00+00:00",
                continuation: true, ("2026-08-01", 200))),
            ("u2", Row("web~cccccccc", "2026-08-02T10:00:00+00:00", "2026-08-02T11:00:00+00:00",
                continuation: false, ("2026-08-02", 50))));

        var reader = new VaultReader(store);
        var sessions = reader.ReadSessions(new[] { ".claude" });

        Assert.Equal(2, sessions.Count);
        var u1 = sessions.Single(s => s.Uuid == "u1");
        Assert.Equal(300, u1.Total);                          // slices merged
        Assert.Equal(2, u1.ByDay.Count);
        Assert.Equal(".claude", u1.Root);
        Assert.NotNull(u1.Cache);                             // primary's cache survives the merge
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 20, 0, 0, TimeSpan.Zero), u1.FirstTs);
    }

    [Fact]
    public void Same_uuid_in_two_roots_stays_two_sessions()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var row = Row("api~aaaaaaaa", "2026-07-10T15:00:00+00:00", "2026-07-10T16:00:00+00:00",
            continuation: false, ("2026-07-10", 100));
        SaveShard(store, ".claude", "2026-07", ("u1", row));
        SaveShard(store, ".claude-personal", "2026-07", ("u1", row));

        var reader = new VaultReader(store);
        var sessions = reader.ReadSessions(new[] { ".claude", ".claude-personal" });
        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, sessions.Select(s => s.Root).Distinct().Count());
    }

    [Fact]
    public void TokensInScope_sums_only_days_inside_the_scope()
    {
        var info = new VaultSessionInfo(
            "u1", ".claude", "api~aaaaaaaa", "api", null,
            DateTimeOffset.Parse("2026-07-08T10:00:00+00:00"),
            DateTimeOffset.Parse("2026-07-11T10:00:00+00:00"),
            600, new Dictionary<string, long> { ["claude-fable-5"] = 600 }, null,
            new Dictionary<string, VaultDayBucket>
            {
                ["2026-07-08"] = new() { Total = 100 },
                ["2026-07-10"] = new() { Total = 200 },
                ["2026-07-11"] = new() { Total = 300 },
            },
            null, 0, 0);

        Assert.Equal(500, VaultReader.TokensInScope(info, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 11)));
        Assert.Equal(300, VaultReader.TokensInScope(info, new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 11)));
        Assert.Equal(600, VaultReader.TokensInScope(info, DateOnly.MinValue, DateOnly.MaxValue));
        Assert.Equal(0, VaultReader.TokensInScope(info, new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 12)));
    }

    [Fact]
    public void Meta_projections_birth_min_ingest_min_and_null_gaps()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var reader = new VaultReader(store);
        var roots = new[] { ".claude", ".claude-personal" };

        Assert.Null(reader.BirthDate(roots));
        Assert.Null(reader.LastSuccessfulIngestUtc(roots));

        store.SaveMeta(".claude", new VaultRootMeta
        {
            Since = "2026-07-01",
            LastIngestTs = "2026-07-12T18:00:00.000000+00:00",
        });
        // One root still missing meta: birth reports the known min, staleness
        // stays null (an unstarted root must read as degraded, not fresh).
        Assert.Equal(new DateOnly(2026, 7, 1), reader.BirthDate(roots));
        Assert.Null(reader.LastSuccessfulIngestUtc(roots));

        store.SaveMeta(".claude-personal", new VaultRootMeta
        {
            Since = "2026-06-20",
            LastIngestTs = "2026-07-12T17:00:00.000000+00:00",
        });
        Assert.Equal(new DateOnly(2026, 6, 20), reader.BirthDate(roots));
        Assert.Equal(DateTimeOffset.Parse("2026-07-12T17:00:00+00:00"),
            reader.LastSuccessfulIngestUtc(roots));           // MIN across roots
    }

    [Fact]
    public void Corrupt_shard_on_read_degrades_to_empty_and_never_mutates()
    {
        using var tmp = new TempDir();
        var store = new VaultStore(tmp.Path);
        var dir = Path.Combine(tmp.Path, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sessions-2026-07.json"), "{corrupt");
        File.WriteAllText(Path.Combine(dir, "rollups-2026-07.json"), "{corrupt");

        var reader = new VaultReader(store);
        Assert.Empty(reader.ReadSessions(new[] { ".claude" }));
        var w = reader.ReadWindow(new[] { ".claude" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 12));
        Assert.Empty(w.ByDay);
        Assert.True(File.Exists(Path.Combine(dir, "sessions-2026-07.json")));   // no quarantine from reads
        Assert.Empty(Directory.GetFiles(dir, "*.bad"));
    }

    [Fact]
    public void ProjectNameOf_strips_hash_suffix()
    {
        Assert.Equal("api", VaultReader.ProjectNameOf("api~3f2a91cc"));
        Assert.Equal("(none)", VaultReader.ProjectNameOf("(none)"));
        Assert.Equal("odd~name", VaultReader.ProjectNameOf("odd~name~12345678"));
    }
}
