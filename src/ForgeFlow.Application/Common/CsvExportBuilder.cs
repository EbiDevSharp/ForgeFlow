using System.Text;
using ForgeFlow.Application.Reporting;

namespace ForgeFlow.Application.Common;

/// <summary>
/// Shared RFC-4180 + UTF-8 BOM CSV builder with spreadsheet formula-injection neutralization
/// and path-traversal filename sanitization.
/// </summary>
public static class CsvExportBuilder
{
    private static readonly byte[] Utf8Bom = Encoding.UTF8.GetPreamble();

    public static CsvExportFile Build(
        string fileName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        AppendCsvRow(sb, headers);
        foreach (var row in rows)
            AppendCsvRow(sb, row);

        var textBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var withBom = new byte[Utf8Bom.Length + textBytes.Length];
        Buffer.BlockCopy(Utf8Bom, 0, withBom, 0, Utf8Bom.Length);
        Buffer.BlockCopy(textBytes, 0, withBom, Utf8Bom.Length, textBytes.Length);

        return new CsvExportFile(SanitizeFileName(fileName), "text/csv; charset=utf-8", withBom);
    }

    public static string EscapeCell(string? raw)
    {
        var value = raw ?? string.Empty;
        if (value.Length > 0 && IsFormulaTrigger(value[0]))
            value = "'" + value;

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsFormulaTrigger(char ch) =>
        ch is '=' or '+' or '-' or '@' or '\t' or '\r';

    private static void AppendCsvRow(StringBuilder sb, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(EscapeCell(cells[i]));
        }
        sb.Append("\r\n");
    }

    private static string SanitizeFileName(string fileName)
    {
        var trimmed = fileName.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_');

        var clean = sb.ToString();
        while (clean.Contains("..", StringComparison.Ordinal))
            clean = clean.Replace("..", "_", StringComparison.Ordinal);

        if (!clean.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            clean += ".csv";
        return clean;
    }
}
