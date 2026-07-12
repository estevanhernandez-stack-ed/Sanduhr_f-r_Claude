using System.Globalization;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/UsageHistory.cs — ported from the Python build's
/// test_history.py, test_history_per_account.py, and test_history_retention.py.
/// The Python <c>tmp_appdata</c> + <c>_default_active_account</c> fixtures map
/// to a <see cref="Paths"/> rooted at a temp dir plus an
/// <see cref="AccountStore"/> on a <see cref="FakeCredentialManager"/>.
/// </summary>
public class UsageHistoryTests
{
    private sealed class Fixture : IDisposable
    {
        public TempDir Temp { get; }
        public Paths Paths { get; }
        public AccountStore Accounts { get; }
        public UsageHistory History { get; }

        public Fixture(params string[] accounts)
        {
            Temp = new TempDir();
            Paths = new Paths(Temp.Path);
            Accounts = new AccountStore(new FakeCredentialManager());
            foreach (var label in accounts)
                Accounts.AddAccount(label, "placeholder-" + label);
            History = new UsageHistory(Accounts, Paths);
        }

        public void Dispose() => Temp.Dispose();
    }

    private static HistoryPoint Point(DateTimeOffset t, int v)
        => new() { T = t.ToString("O", CultureInfo.InvariantCulture), V = v };

    // -- test_history.py (default 'Personal' active) --------------------------

    [Fact]
    public void Append_creates_file()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 42);
        Assert.True(File.Exists(f.Paths.HistoryFileFor("Personal")));
    }

    [Fact]
    public void Append_adds_entry()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 42);
        Assert.Equal(new[] { 42 }, f.History.Load("five_hour"));
    }

    [Fact]
    public void Append_multiple_values()
    {
        using var f = new Fixture("Personal");
        foreach (var v in new[] { 10, 20, 30 })
            f.History.Append("five_hour", v);
        Assert.Equal(new[] { 10, 20, 30 }, f.History.Load("five_hour"));
    }

    [Fact]
    public void Append_different_tiers_isolated()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 10);
        f.History.Append("seven_day", 99);
        Assert.Equal(new[] { 10 }, f.History.Load("five_hour"));
        Assert.Equal(new[] { 99 }, f.History.Load("seven_day"));
    }

    [Fact]
    public void Load_missing_tier_returns_empty_list()
    {
        using var f = new Fixture("Personal");
        Assert.Empty(f.History.Load("nonexistent"));
    }

    [Fact]
    public void Load_missing_file_returns_empty_list()
    {
        using var f = new Fixture("Personal");
        Assert.Empty(f.History.Load("five_hour"));
    }

    [Fact]
    public void Append_rolls_over_at_max()
    {
        using var f = new Fixture("Personal");
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var seed = new Dictionary<string, List<HistoryPoint>>
        {
            ["five_hour"] = Enumerable.Range(0, UsageHistory.MaxHistory)
                .Select(i => Point(baseTime.AddMinutes(5 * i), i)).ToList(),
        };
        f.History.SaveHistory(seed);
        for (int i = UsageHistory.MaxHistory; i < UsageHistory.MaxHistory + 5; i++)
            f.History.Append("five_hour", i);
        var values = f.History.Load("five_hour");
        Assert.Equal(UsageHistory.MaxHistory, values.Count);
        Assert.Equal(UsageHistory.MaxHistory + 4, values[^1]);
    }

    [Fact]
    public void Corrupt_legacy_file_does_not_crash()
    {
        using var f = new Fixture("Personal");
        // Write garbage to the legacy history.json; first load migrates it to the
        // active account file, where the parse fails gracefully → empty.
        File.WriteAllText(f.Paths.HistoryFile, "not valid json {{{");
        Assert.Empty(f.History.Load("five_hour"));
    }

    [Fact]
    public void Corrupt_file_repaired_on_append()
    {
        using var f = new Fixture("Personal");
        File.WriteAllText(f.Paths.HistoryFileFor("Personal"), "garbage");
        f.History.Append("five_hour", 50);
        Assert.Equal(new[] { 50 }, f.History.Load("five_hour"));
    }

    // -- test_history_retention.py -------------------------------------------

    [Fact]
    public void Retention_keeps_8640_points()
        => Assert.Equal(8640, UsageHistory.MaxHistory);

    [Fact]
    public void Append_prunes_beyond_retention()
    {
        using var f = new Fixture("Personal");
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var data = new Dictionary<string, List<HistoryPoint>>
        {
            ["five_hour"] = Enumerable.Range(0, 8640)
                .Select(i => Point(baseTime.AddMinutes(5 * i), i % 100)).ToList(),
        };
        f.History.SaveHistory(data);
        var result = f.History.Append("five_hour", 99);
        Assert.Equal(8640, result.Count);
    }

    [Fact]
    public void Load_window_filters_by_days()
    {
        using var f = new Fixture("Personal");
        var now = DateTimeOffset.UtcNow;
        var data = new Dictionary<string, List<HistoryPoint>>
        {
            ["five_hour"] = Enumerable.Range(0, 240)
                .Select(i => Point(now.AddHours(-i), i % 100)).ToList(),
        };
        f.History.SaveHistory(data);
        var window = f.History.LoadWindow("five_hour", days: 7);
        Assert.InRange(window.Count, 160, 170);
    }

    [Fact]
    public void Load_window_returns_empty_for_unknown_tier()
    {
        using var f = new Fixture("Personal");
        Assert.Empty(f.History.LoadWindow("iguana_necktie", days: 7));
    }

    [Fact]
    public void Clear_all_wipes_file()
    {
        using var f = new Fixture("Personal");
        f.History.SaveHistory(new Dictionary<string, List<HistoryPoint>>
        {
            ["five_hour"] = new() { Point(new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero), 50) },
        });
        f.History.ClearAll();
        Assert.Empty(f.History.LoadHistory());
    }

    [Fact]
    public void Clear_all_preserves_file()
    {
        using var f = new Fixture("Personal");
        f.History.SaveHistory(new Dictionary<string, List<HistoryPoint>>
        {
            ["five_hour"] = new() { Point(new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero), 50) },
        });
        f.History.ClearAll();
        Assert.True(File.Exists(f.Paths.HistoryFileFor("Personal")));
    }

    // -- test_history_per_account.py -----------------------------------------

    [Fact]
    public void History_writes_to_active_account_file()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 50);
        Assert.True(File.Exists(f.Paths.HistoryFileFor("Personal")));
        Assert.Equal(new[] { 50 }, f.History.Load("five_hour"));
    }

    [Fact]
    public void History_per_account_isolated()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 30);
        f.Accounts.SetActive("Work");
        f.History.Append("five_hour", 70);
        f.Accounts.SetActive("Personal");
        Assert.Equal(new[] { 30 }, f.History.Load("five_hour"));
        f.Accounts.SetActive("Work");
        Assert.Equal(new[] { 70 }, f.History.Load("five_hour"));
    }

    [Fact]
    public void Explicit_account_override()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        f.History.AppendForAccount("five_hour", 30, account: "Work");
        f.Accounts.SetActive("Work");
        Assert.Equal(new[] { 30 }, f.History.Load("five_hour"));
        f.Accounts.SetActive("Personal");
        Assert.Empty(f.History.Load("five_hour"));
    }

    [Fact]
    public void Legacy_migration_renames_file_to_active_account()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        File.WriteAllText(f.Paths.HistoryFile,
            "{\"five_hour\":[{\"t\":\"2026-05-01T00:00:00+00:00\",\"v\":42}]}");
        // Triggers migration on first load.
        f.History.LoadHistory();
        Assert.False(File.Exists(f.Paths.HistoryFile));
        Assert.True(File.Exists(f.Paths.HistoryFileFor("Personal")));
        Assert.Equal(new[] { 42 }, f.History.Load("five_hour"));
    }

    [Fact]
    public void Legacy_migration_skipped_when_per_account_already_exists()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        File.WriteAllText(f.Paths.HistoryFileFor("Personal"),
            "{\"five_hour\":[{\"t\":\"2026-05-04T00:00:00+00:00\",\"v\":100}]}");
        File.WriteAllText(f.Paths.HistoryFile,
            "{\"five_hour\":[{\"t\":\"2026-05-01T00:00:00+00:00\",\"v\":42}]}");
        f.History.LoadHistory();
        // Per-account file is the source of truth; legacy is left alone.
        Assert.Equal(new[] { 100 }, f.History.Load("five_hour"));
    }

    [Fact]
    public void Aggregate_window_returns_all_accounts()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 30);
        f.Accounts.SetActive("Work");
        f.History.Append("five_hour", 70);
        var agg = f.History.AggregateWindow("five_hour", days: 7);
        Assert.Equal(new HashSet<string> { "Personal", "Work" }, new HashSet<string>(agg.Keys));
        Assert.Single(agg["Personal"]);
        Assert.Single(agg["Work"]);
        Assert.Equal(30, agg["Personal"][0].V);
        Assert.Equal(70, agg["Work"][0].V);
    }

    [Fact]
    public void Aggregate_window_empty_for_unrecorded_tier()
    {
        using var f = new Fixture("Personal", "Work");
        var agg = f.History.AggregateWindow("five_hour", days: 7);
        Assert.Empty(agg["Personal"]);
        Assert.Empty(agg["Work"]);
    }

    [Fact]
    public void Clear_all_only_clears_active_account()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 30);
        f.Accounts.SetActive("Work");
        f.History.Append("five_hour", 70);
        f.Accounts.SetActive("Personal");
        f.History.ClearAll();
        Assert.Empty(f.History.LoadHistory());
        Assert.Equal(new[] { 70 }, f.History.Load("five_hour", account: "Work"));
    }

    [Fact]
    public void History_when_no_active_account_is_noop()
    {
        using var f = new Fixture(); // no accounts registered
        Assert.Empty(f.History.LoadHistory());
        Assert.Empty(f.History.Load("five_hour"));
        f.History.Append("five_hour", 50); // silently no-ops
        Assert.Empty(f.History.Load("five_hour"));
    }

    // -- Delete / Rename (WS-A: complete account removal + rename) ------------

    [Fact]
    public void Delete_unlinks_the_history_file()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 42);
        Assert.True(File.Exists(f.Paths.HistoryFileFor("Personal")));
        f.History.Delete("Personal");
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Personal")));
    }

    [Fact]
    public void Delete_missing_file_is_noop()
    {
        using var f = new Fixture("Personal");
        f.History.Delete("Personal"); // no file yet — must not throw
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Personal")));
    }

    [Fact]
    public void Delete_leaves_other_accounts_untouched()
    {
        using var f = new Fixture("Personal", "Work");
        f.History.AppendForAccount("five_hour", 10, "Personal");
        f.History.AppendForAccount("five_hour", 20, "Work");
        f.History.Delete("Personal");
        Assert.Equal(new[] { 20 }, f.History.Load("five_hour", "Work"));
    }

    [Fact]
    public void Rename_moves_the_history_file()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 42);
        f.History.Rename("Personal", "Home");
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Personal")));
        Assert.Equal(new[] { 42 }, f.History.Load("five_hour", "Home"));
    }

    [Fact]
    public void Rename_without_source_file_is_noop()
    {
        using var f = new Fixture("Personal");
        f.History.Rename("Personal", "Home"); // no file yet — must not throw
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Home")));
    }

    [Fact]
    public void Rename_overwrites_a_stale_target_file()
    {
        using var f = new Fixture("Personal");
        // A stale file left under the target name (e.g. from a pre-fix rename).
        File.WriteAllText(f.Paths.HistoryFileFor("Home"), """{"five_hour":[{"t":"2026-01-01T00:00:00+00:00","v":99}]}""");
        f.History.Append("five_hour", 42);
        f.History.Rename("Personal", "Home");
        Assert.Equal(new[] { 42 }, f.History.Load("five_hour", "Home"));
    }
}
