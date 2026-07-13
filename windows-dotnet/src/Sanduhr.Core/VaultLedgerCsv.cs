using System.Globalization;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// Session Ledger CSV builder — IO-free like <see cref="CsvExport"/>; the App
/// layer owns the save dialog and the write. One row per visible ledger row,
/// in the order given (the VM passes them pre-sorted by the active column).
/// </summary>
public static class VaultLedgerCsv
{
    public sealed record Row(
        string Uuid,
        string Root,
        string Project,
        string FirstTs,
        string LastTs,
        long TokensInScope,
        long TokensTotal,
        string Models);

    public static CsvExport.CsvBuildResult Build(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.Append("session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models\r\n");
        foreach (var r in rows)
        {
            Append(sb, r.Uuid); sb.Append(',');
            Append(sb, r.Root); sb.Append(',');
            Append(sb, r.Project); sb.Append(',');
            Append(sb, r.FirstTs); sb.Append(',');
            Append(sb, r.LastTs); sb.Append(',');
            sb.Append(r.TokensInScope.ToString(CultureInfo.InvariantCulture)); sb.Append(',');
            sb.Append(r.TokensTotal.ToString(CultureInfo.InvariantCulture)); sb.Append(',');
            Append(sb, r.Models);
            sb.Append("\r\n");
        }
        return new CsvExport.CsvBuildResult(sb.ToString(), rows.Count);
    }

    private static void Append(StringBuilder sb, string field) => sb.Append(CsvExport.Escape(field));
}
