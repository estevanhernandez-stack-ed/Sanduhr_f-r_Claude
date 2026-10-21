using System.Text.Json.Nodes;
using Sanduhr.Core;
using Sanduhr.Mcp;
using static Sanduhr.Mcp.Tests.Helpers;

namespace Sanduhr.Mcp.Tests;

/// <summary>The Store build's new vault files are virtualized into the package's
/// LocalCache and shadow the real ones per file for the widget, so get_usage_history
/// picks, per root and month, the first vault directory holding the rollup shard.</summary>
public class ToolLogicVaultOverlayTests
{
    private const string Root = ".claude-personal";

    private static (ToolLogic Logic, string Cache, string Real) Setup(TempDir tmp)
    {
        string personal = Path.Combine(tmp.Path, Root);
        Directory.CreateDirectory(personal);
        string cache = Path.Combine(tmp.Path, "cache-vault");
        string real = Path.Combine(tmp.Path, "real-vault");
        var cfg = new McpConfig
        {
            SnapshotPath = Path.Combine(tmp.Path, "snapshot.json"),
            VaultDir = real,
            VaultDirs = new[] { cache, real },
            ConsentedRoots = new[] { (Root, personal) },
            RootsFound = new[] { Root },
        };
        return (new ToolLogic(cfg, () => Now), cache, real);
    }

    private static void Shard(string vaultDir, string month, params (string Day, long Total)[] days)
    {
        var map = new Dictionary<string, VaultRollupDay>();
        foreach (var (day, total) in days)
            map[day] = new VaultRollupDay { Total = total, Input = total / 2, Output = total - total / 2 };
        new VaultStore(vaultDir).SaveRollupShard(Root, month, new VaultRollupShard { Days = map });
    }

    [Fact]
    public void A_cache_month_shadows_the_same_month_in_real()
    {
        using var tmp = new TempDir();
        var (logic, cache, real) = Setup(tmp);
        Shard(cache, "2026-07", ("2026-07-24", 100));
        Shard(real, "2026-07", ("2026-07-24", 999), ("2026-07-25", 555));

        var r = logic.BuildHistory(30);

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(100, r["total_tokens"]!.GetValue<long>());
        Assert.Equal(1, r["days_recorded"]!.GetValue<int>());
    }

    [Fact]
    public void A_month_only_in_real_is_still_read()
    {
        using var tmp = new TempDir();
        var (logic, _, real) = Setup(tmp);
        Shard(real, "2026-07", ("2026-07-24", 250));

        var r = logic.BuildHistory(30);

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(250, r["total_tokens"]!.GetValue<long>());
    }

    [Fact]
    public void Months_split_across_both_are_summed()
    {
        using var tmp = new TempDir();
        var (logic, cache, real) = Setup(tmp);
        Shard(real, "2026-06", ("2026-06-28", 7));
        Shard(cache, "2026-07", ("2026-07-24", 11));

        var r = logic.BuildHistory(30);

        Assert.Equal("ok", (string?)r["status"]);
        Assert.Equal(18, r["total_tokens"]!.GetValue<long>());
        Assert.Equal(2, r["days_recorded"]!.GetValue<int>());
        var days = (JsonArray)r["days"]!;
        Assert.Equal("2026-06-28", (string?)days[0]!["date"]);
        Assert.Equal("2026-07-24", (string?)days[1]!["date"]);
    }

    [Fact]
    public void A_cache_month_does_not_hide_a_different_month_in_real()
    {
        using var tmp = new TempDir();
        var (logic, cache, real) = Setup(tmp);
        Shard(cache, "2026-06", ("2026-06-28", 3));
        Shard(real, "2026-06", ("2026-06-28", 900));
        Shard(real, "2026-07", ("2026-07-24", 4));

        var r = logic.BuildHistory(30);

        Assert.Equal(7, r["total_tokens"]!.GetValue<long>());
    }

    [Fact]
    public void No_shard_anywhere_is_the_missing_no_data()
    {
        using var tmp = new TempDir();
        var (logic, _, _) = Setup(tmp);

        var r = logic.BuildHistory(30);

        Assert.Equal("no_data", (string?)r["status"]);
        Assert.Equal("missing", (string?)r["reason"]);
    }
}
