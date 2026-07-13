using Sanduhr.Core;

namespace Sanduhr.Tests;

public class VaultLedgerCsvTests
{
    [Fact]
    public void Header_always_emitted_and_rows_in_given_order()
    {
        var result = VaultLedgerCsv.Build(Array.Empty<VaultLedgerCsv.Row>());
        Assert.Equal("session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models\r\n",
            result.Text);
        Assert.Equal(0, result.RowCount);

        var rows = new[]
        {
            new VaultLedgerCsv.Row("u2", ".claude", "api", "2026-07-11T00:00:00+00:00",
                "2026-07-11T01:00:00+00:00", 200, 200, "claude-fable-5:200"),
            new VaultLedgerCsv.Row("u1", ".claude", "web", "2026-07-10T00:00:00+00:00",
                "2026-07-10T01:00:00+00:00", 100, 600, "claude-fable-5:400;claude-sonnet-5:200"),
        };
        var built = VaultLedgerCsv.Build(rows);
        Assert.Equal(2, built.RowCount);
        var lines = built.Text.Split("\r\n");
        Assert.StartsWith("u2,", lines[1]);                   // caller's order preserved
        Assert.StartsWith("u1,", lines[2]);
        Assert.Contains("claude-fable-5:400;claude-sonnet-5:200", lines[2]);
    }

    [Fact]
    public void Fields_with_commas_or_quotes_are_rfc4180_quoted()
    {
        var rows = new[]
        {
            new VaultLedgerCsv.Row("u1", ".claude", "odd,\"proj\"", "t", "t", 1, 1, "m"),
        };
        var built = VaultLedgerCsv.Build(rows);
        Assert.Contains("\"odd,\"\"proj\"\"\"", built.Text);
    }
}
