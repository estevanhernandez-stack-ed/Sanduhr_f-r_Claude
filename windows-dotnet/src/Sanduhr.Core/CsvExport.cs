using System.Globalization;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Pure usage-history CSV builder, ported 1:1 from <c>csv_export.export_to_csv</c>.
/// Produces a flat CSV any LLM or spreadsheet can parse; the App layer owns the
/// save-file dialog and the actual <c>File.WriteAllText</c>, so this stays IO-free
/// and unit-testable.
///
/// Columns:
/// <list type="bullet">
/// <item>All-accounts export (<c>account == null</c>): <c>timestamp, account, tier, util_pct</c></item>
/// <item>Single-account export: <c>timestamp, tier, util_pct</c></item>
/// </list>
///
/// One row per (tier, timestamp) pair from the local per-account history. Rows are
/// sorted lexicographically by the ISO-8601 timestamp — chronological because
/// <see cref="UsageHistory.Append"/> writes a fixed <c>+00:00</c> UTC offset every
/// time. The header is always emitted, even when history is empty. RFC-4180
/// quoting (QUOTE_MINIMAL, CRLF terminator) matches Python's <c>csv</c> module.
/// </summary>
public static class CsvExport
{
    /// <summary>The built CSV text plus the number of DATA rows (header excluded),
    /// mirroring the Python function's return value used in the "Wrote N rows" toast.</summary>
    public readonly record struct CsvBuildResult(string Text, int RowCount);

    /// <summary>
    /// Build the CSV for <paramref name="account"/> (a specific label) or, when
    /// <c>null</c>, ALL registered accounts with an extra <c>account</c> column.
    /// When no accounts are registered the all-accounts path falls back to the
    /// active account (which may itself be null → header only). Parity with
    /// <c>csv_export.export_to_csv</c>.
    /// </summary>
    public static CsvBuildResult Build(UsageHistory history, AccountStore accounts, string? account = null)
    {
        var rows = new List<string[]>();
        string[] fieldnames;

        if (account is null)
        {
            var labels = accounts.ListAccounts().ToList();
            if (labels.Count == 0)
            {
                var active = accounts.GetActive();
                if (active is not null)
                    labels.Add(active);
            }
            fieldnames = new[] { "timestamp", "account", "tier", "util_pct" };
            foreach (var label in labels)
            {
                foreach (var (tierKey, points) in history.LoadHistory(label))
                    foreach (var p in points)
                        rows.Add(new[] { p.T ?? "", label, tierKey, p.V.ToString(CultureInfo.InvariantCulture) });
            }
        }
        else
        {
            fieldnames = new[] { "timestamp", "tier", "util_pct" };
            foreach (var (tierKey, points) in history.LoadHistory(account))
                foreach (var p in points)
                    rows.Add(new[] { p.T ?? "", tierKey, p.V.ToString(CultureInfo.InvariantCulture) });
        }

        // Stable lexicographic sort on the timestamp column — chronological for the
        // fixed-offset ISO strings the history writer emits (parity comment in csv_export.py).
        var ordered = rows.OrderBy(r => r[0], StringComparer.Ordinal).ToList();

        var sb = new StringBuilder();
        AppendLine(sb, fieldnames);
        foreach (var r in ordered)
            AppendLine(sb, r);

        return new CsvBuildResult(sb.ToString(), ordered.Count);
    }

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Escape(fields[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>RFC-4180 minimal quoting (Python csv QUOTE_MINIMAL): quote a field
    /// only when it contains a comma, quote, CR, or LF; embedded quotes are doubled.</summary>
    internal static string Escape(string field)
    {
        if (field.IndexOfAny(QuoteTriggers) < 0)
            return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private static readonly char[] QuoteTriggers = { ',', '"', '\n', '\r' };
}
