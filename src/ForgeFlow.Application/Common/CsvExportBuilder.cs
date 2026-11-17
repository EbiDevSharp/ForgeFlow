using System.Text;
using ForgeFlow.Application.Reporting;

namespace ForgeFlow.Application.Common;

/// <summary>
/// Column definition for typed streaming CSV generation.
/// </summary>
public sealed record CsvColumn<T>(string Header, Func<T, string?> ValueSelector);

/// <summary>
/// Shared RFC-4180 + UTF-8 BOM CSV builder and async streaming exporter with spreadsheet formula-injection neutralization
/// and path-traversal filename sanitization.
/// </summary>
public static class CsvExportBuilder
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Materializes an in-memory CSV file.
    /// </summary>
    public static CsvExportFile Build(
        string fileName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, Utf8WithBom, bufferSize: 4096, leaveOpen: true))
        {
            WriteRow(writer, headers);
            foreach (var row in rows)
            {
                WriteRow(writer, row);
            }
            writer.Flush();
        }

        return new CsvExportFile(SanitizeFileName(fileName), "text/csv; charset=utf-8", ms.ToArray());
    }

    /// <summary>
    /// Streams CSV content asynchronously to an output stream using untyped row string lists.
    /// Eliminates Large Object Heap (LOH) allocations and full dataset materialization.
    /// </summary>
    public static async Task WriteStreamAsync(
        Stream outputStream,
        IReadOnlyList<string> headers,
        IAsyncEnumerable<IReadOnlyList<string>> rows,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        await using var writer = new StreamWriter(outputStream, Utf8WithBom, bufferSize: 4096, leaveOpen: true);
        await WriteRowAsync(writer, headers, ct);

        await foreach (var row in rows.WithCancellation(ct))
        {
            await WriteRowAsync(writer, row, ct);
        }

        await writer.FlushAsync(ct);
    }

    /// <summary>
    /// Streams typed records asynchronously to an output stream using column projections.
    /// Eliminates Large Object Heap (LOH) allocations and full dataset materialization.
    /// </summary>
    public static async Task WriteStreamAsync<T>(
        Stream outputStream,
        IReadOnlyList<CsvColumn<T>> columns,
        IAsyncEnumerable<T> rows,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        await using var writer = new StreamWriter(outputStream, Utf8WithBom, bufferSize: 4096, leaveOpen: true);
        var headers = columns.Select(c => c.Header).ToArray();
        await WriteRowAsync(writer, headers, ct);

        await foreach (var item in rows.WithCancellation(ct))
        {
            var cells = columns.Select(c => c.ValueSelector(item) ?? string.Empty).ToArray();
            await WriteRowAsync(writer, cells, ct);
        }

        await writer.FlushAsync(ct);
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

    public static string SanitizeFileName(string fileName)
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

    private static bool IsFormulaTrigger(char ch) =>
        ch is '=' or '+' or '-' or '@' or '\t' or '\r';

    private static void WriteRow(TextWriter writer, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write(EscapeCell(cells[i]));
        }
        writer.Write("\r\n");
    }

    private static async Task WriteRowAsync(TextWriter writer, IReadOnlyList<string> cells, CancellationToken ct)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) await writer.WriteAsync(',');
            await writer.WriteAsync(EscapeCell(cells[i]));
        }
        await writer.WriteAsync("\r\n");
    }
}
