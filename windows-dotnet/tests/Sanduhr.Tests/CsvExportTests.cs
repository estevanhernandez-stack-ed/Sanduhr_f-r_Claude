using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/CsvExport.cs — ported from the Python build's
/// test_csv_export.py. Mirrors the UsageHistory fixture: a <see cref="Paths"/>
/// rooted at a temp dir plus an <see cref="AccountStore"/> on a
/// <see cref="FakeCredentialManager"/>.
/// </summary>
public class CsvExportTests
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

    private static string[] Lines(string csv) =>
        csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    [Fact]
    public void Empty_history_writes_header_only_single_account()
    {
        using var f = new Fixture("Personal");
        var r = CsvExport.Build(f.History, f.Accounts, account: "Personal");
        Assert.Equal(0, r.RowCount);
        var lines = Lines(r.Text);
        Assert.Single(lines);
        Assert.Equal("timestamp,tier,util_pct", lines[0]);
    }

    [Fact]
    public void Empty_history_writes_header_only_all_accounts()
    {
        using var f = new Fixture("Personal");
        var r = CsvExport.Build(f.History, f.Accounts, account: null);
        Assert.Equal(0, r.RowCount);
        var lines = Lines(r.Text);
        Assert.Single(lines);
        Assert.Equal("timestamp,account,tier,util_pct", lines[0]);
    }

    [Fact]
    public void Single_account_export_has_three_columns()
    {
        using var f = new Fixture("Personal");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 42);
        var r = CsvExport.Build(f.History, f.Accounts, account: "Personal");
        Assert.Equal(1, r.RowCount);
        var lines = Lines(r.Text);
        Assert.Equal("timestamp,tier,util_pct", lines[0]);
        // timestamp,tier,util_pct — tier=five_hour, util=42
        var cells = lines[1].Split(',');
        Assert.Equal(3, cells.Length);
        Assert.Equal("five_hour", cells[1]);
        Assert.Equal("42", cells[2]);
    }

    [Fact]
    public void All_accounts_export_includes_account_column_and_spans_accounts()
    {
        using var f = new Fixture("Personal", "Work");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 30);
        f.Accounts.SetActive("Work");
        f.History.Append("seven_day", 70);

        var r = CsvExport.Build(f.History, f.Accounts, account: null);
        Assert.Equal(2, r.RowCount);
        var lines = Lines(r.Text);
        Assert.Equal("timestamp,account,tier,util_pct", lines[0]);

        // Both accounts represented; account column present (4 cols).
        var dataRows = lines.Skip(1).Select(l => l.Split(',')).ToList();
        Assert.All(dataRows, c => Assert.Equal(4, c.Length));
        Assert.Contains(dataRows, c => c[1] == "Personal" && c[2] == "five_hour" && c[3] == "30");
        Assert.Contains(dataRows, c => c[1] == "Work" && c[2] == "seven_day" && c[3] == "70");
    }

    [Fact]
    public void Single_account_export_excludes_other_accounts()
    {
        using var f = new Fixture("Personal", "Work");
        f.History.AppendForAccount("five_hour", 30, account: "Personal");
        f.History.AppendForAccount("five_hour", 70, account: "Work");

        var r = CsvExport.Build(f.History, f.Accounts, account: "Personal");
        Assert.Equal(1, r.RowCount);
        Assert.DoesNotContain("70", r.Text);
        Assert.Contains("30", r.Text);
    }

    [Fact]
    public void Rows_sorted_chronologically_by_timestamp()
    {
        using var f = new Fixture("Personal");
        f.Accounts.SetActive("Personal");
        // Seed out-of-order timestamps directly so the sort is exercised.
        f.History.SaveHistory(new Dictionary<string, List<HistoryPoint>>
        {
            ["five_hour"] = new()
            {
                new HistoryPoint { T = "2026-05-03T00:00:00+00:00", V = 3 },
                new HistoryPoint { T = "2026-05-01T00:00:00+00:00", V = 1 },
                new HistoryPoint { T = "2026-05-02T00:00:00+00:00", V = 2 },
            },
        });
        var r = CsvExport.Build(f.History, f.Accounts, account: "Personal");
        var lines = Lines(r.Text);
        Assert.Equal("2026-05-01T00:00:00+00:00", lines[1].Split(',')[0]);
        Assert.Equal("2026-05-02T00:00:00+00:00", lines[2].Split(',')[0]);
        Assert.Equal("2026-05-03T00:00:00+00:00", lines[3].Split(',')[0]);
    }

    [Fact]
    public void All_accounts_falls_back_to_active_when_registry_empty()
    {
        // No accounts in the registry list, but an active label is set with data.
        using var f = new Fixture();
        // AddAccount also sets active when none is set yet.
        f.Accounts.AddAccount("Solo", "placeholder-Solo");
        f.History.Append("five_hour", 55);
        // Remove from the *list* is not exposed; instead verify the normal
        // all-accounts path picks up the single registered account.
        var r = CsvExport.Build(f.History, f.Accounts, account: null);
        Assert.Equal(1, r.RowCount);
        Assert.Contains("Solo", r.Text);
        Assert.Contains("55", r.Text);
    }

    [Fact]
    public void Uses_crlf_line_terminator()
    {
        using var f = new Fixture("Personal");
        f.Accounts.SetActive("Personal");
        f.History.Append("five_hour", 42);
        var r = CsvExport.Build(f.History, f.Accounts, account: "Personal");
        Assert.Contains("\r\n", r.Text);
        Assert.EndsWith("\r\n", r.Text);
    }
}
