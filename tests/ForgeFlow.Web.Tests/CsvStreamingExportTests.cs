using System.Text;
using ForgeFlow.Application.Common;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class CsvStreamingExportTests
{
    [Fact]
    public async Task StreamingCsv_WritesUtf8Bom_AndCorrectHeaders()
    {
        var headers = new[] { "شناسه", "نام کالا", "واحد" };
        var rows = CreateAsyncRows(
        [
            ["1", "پیچ فولادی", "عدد"],
            ["2", "مهره شش‌گوش", "عدد"]
        ]);

        using var ms = new MemoryStream();
        await CsvExportBuilder.WriteStreamAsync(ms, headers, rows);

        var bytes = ms.ToArray();
        // UTF-8 BOM is 0xEF, 0xBB, 0xBF
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("شناسه,نام کالا,واحد", lines[0]);
        Assert.Equal("1,پیچ فولادی,عدد", lines[1]);
        Assert.Equal("2,مهره شش‌گوش,عدد", lines[2]);
    }

    [Fact]
    public async Task StreamingCsv_NeutralizesFormulaInjection()
    {
        var headers = new[] { "Item", "Value" };
        var rows = CreateAsyncRows(
        [
            ["Safe Item", "100"],
            ["=SUM(A1:A10)", "+cmd|' /C calc'!A0"],
            ["-DangerousMinus", "@AtFormula"]
        ]);

        using var ms = new MemoryStream();
        await CsvExportBuilder.WriteStreamAsync(ms, headers, rows);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("'=SUM(A1:A10)", text);
        Assert.Contains("'+cmd|' /C calc'!A0", text);
        Assert.Contains("'-DangerousMinus", text);
        Assert.Contains("'@AtFormula", text);
    }

    [Fact]
    public async Task StreamingCsv_Typed_StreamsItemsCorrectly()
    {
        var columns = new List<CsvColumn<SampleExportItem>>
        {
            new("ID", x => x.Id.ToString()),
            new("Title", x => x.Title),
            new("Price", x => x.Price.ToString("F2")),
            new("Category", x => x.Category)
        };

        var items = CreateAsyncItems(
        [
            new SampleExportItem(10, "محصول الف", 1500.50m, "الکترونیک"),
            new SampleExportItem(20, "محصول ب, ویژه", 2500.00m, "ابزارآلات")
        ]);

        using var ms = new MemoryStream();
        await CsvExportBuilder.WriteStreamAsync(ms, columns, items);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("ID,Title,Price,Category", text);
        Assert.Contains("10,محصول الف,1500.50,الکترونیک", text);
        Assert.Contains("20,\"محصول ب, ویژه\",2500.00,ابزارآلات", text);
    }

    [Fact]
    public async Task StreamingCsv_EscapesCommasQuotesAndNewlines()
    {
        var headers = new[] { "Text" };
        var rows = CreateAsyncRows(
        [
            ["Simple"],
            ["Has, comma"],
            ["Has \"quotes\" inside"],
            ["Line 1\r\nLine 2"]
        ]);

        using var ms = new MemoryStream();
        await CsvExportBuilder.WriteStreamAsync(ms, headers, rows);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Simple", text);
        Assert.Contains("\"Has, comma\"", text);
        Assert.Contains("\"Has \"\"quotes\"\" inside\"", text);
        Assert.Contains("\"Line 1\r\nLine 2\"", text);
    }

    [Theory]
    [InlineData("normal.csv", "normal.csv")]
    [InlineData("report", "report.csv")]
    [InlineData("../../../etc/passwd", "______etc_passwd.csv")]
    [InlineData("..\\..\\windows\\system32\\cmd.exe", "____windows_system32_cmd.exe.csv")]
    public void SanitizeFileName_NeutralizesPathTraversal(string input, string expected)
    {
        var sanitized = CsvExportBuilder.SanitizeFileName(input);
        Assert.Equal(expected, sanitized);
    }

    private static async IAsyncEnumerable<IReadOnlyList<string>> CreateAsyncRows(IEnumerable<IReadOnlyList<string>> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private static async IAsyncEnumerable<T> CreateAsyncItems<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private sealed record SampleExportItem(long Id, string Title, decimal Price, string Category);
}
