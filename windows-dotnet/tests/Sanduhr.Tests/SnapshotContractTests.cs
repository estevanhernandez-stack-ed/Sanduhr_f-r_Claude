using Sanduhr.Core;

namespace Sanduhr.Tests;

public class SnapshotContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, SnapshotBand.Fresh)]
    [InlineData(449, SnapshotBand.Fresh)]
    [InlineData(450, SnapshotBand.Stale)]
    [InlineData(900, SnapshotBand.Stale)]
    [InlineData(901, SnapshotBand.Dead)]
    public void Band_boundaries_match_the_spec_table(int ageSeconds, SnapshotBand expected)
    {
        var captured = Now.AddSeconds(-ageSeconds);
        Assert.Equal(expected, SnapshotContract.Band(captured, Now));
    }

    [Fact]
    public void Negative_age_clamps_to_zero_and_reads_fresh()
    {
        var captured = Now.AddSeconds(90); // clock skew: snapshot "from the future"
        Assert.Equal(0, SnapshotContract.AgeSeconds(captured, Now));
        Assert.Equal(SnapshotBand.Fresh, SnapshotContract.Band(captured, Now));
    }

    [Fact]
    public void AccountRef_is_null_for_missing_labels()
    {
        Assert.Null(SnapshotContract.AccountRef(null));
        Assert.Null(SnapshotContract.AccountRef(""));
    }

    [Fact]
    public void AccountRef_is_a_short_stable_hash_that_never_carries_the_label()
    {
        var ref1 = SnapshotContract.AccountRef("Este");
        var ref2 = SnapshotContract.AccountRef("Este");
        var other = SnapshotContract.AccountRef("Work");

        Assert.NotNull(ref1);
        Assert.Equal(ref1, ref2);            // deterministic — switch detection depends on it
        Assert.NotEqual(ref1, other);
        Assert.Equal(8, ref1!.Length);       // 4-byte SHA-256 prefix, lowercase hex
        Assert.Matches("^[0-9a-f]{8}$", ref1);
        Assert.NotEqual("Este", ref1);
    }

    // -- script drift pins ----------------------------------------------------
    // The statusline script re-implements the contract in PowerShell. These pins
    // fail the build when someone changes a threshold or the schema guard on one
    // side only.

    [Fact]
    public void Script_embeds_the_contract_staleness_thresholds()
    {
        Assert.Contains("-gt 900", StatuslineScript.Content);   // dead boundary
        Assert.Contains("-ge 450", StatuslineScript.Content);   // stale age suffix
        Assert.Equal(900, SnapshotContract.DeadSeconds);
        Assert.Equal(450, SnapshotContract.FreshSeconds);
    }

    [Fact]
    public void Script_guards_the_schema_version_and_reads_the_contract_path()
    {
        Assert.Contains("schema_version", StatuslineScript.Content);
        Assert.Contains("update statusline", StatuslineScript.Content); // higher-major refusal
        Assert.Contains(@"Sanduhr\snapshot.json", StatuslineScript.Content);
    }

    [Fact]
    public void Script_never_renders_blank_in_the_dead_band()
    {
        // Dead = explicit "start widget" line; blank is reserved for the
        // uninstalled/missing look.
        Assert.Contains("start widget", StatuslineScript.Content);
    }

    [Fact]
    public void Script_maps_every_error_kind_the_writer_emits()
    {
        Assert.Contains(SnapshotContract.ErrorSessionExpired, StatuslineScript.Content);
        Assert.Contains(SnapshotContract.ErrorCloudflare, StatuslineScript.Content);
        // network falls through to the default 'offline' branch by design
        Assert.Contains("offline", StatuslineScript.Content);
    }
}
