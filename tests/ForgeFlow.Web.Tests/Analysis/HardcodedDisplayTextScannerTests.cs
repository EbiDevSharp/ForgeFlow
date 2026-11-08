using ForgeFlow.Web.Tests.Analysis;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class HardcodedDisplayTextScannerTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DisplayText");

    [Fact]
    public void Application_owned_Razor_CSharp_and_JavaScript_have_no_unexcepted_display_literals()
    {
        var sourceRoot = Path.Combine(AppContext.BaseDirectory, "ApplicationSources");
        var exceptions = HardcodedDisplayTextScanner.ReadExceptions(Path.Combine(AppContext.BaseDirectory, "display-text-exceptions.json"));
        var result = HardcodedDisplayTextScanner.ScanDirectory(sourceRoot, exceptions);
        var findings = string.Join(Environment.NewLine, result.Findings.Select(Format));
        var unused = string.Join(Environment.NewLine, result.UnusedExceptions.Select(entry => $"Unused exception: {entry.File}:{entry.Line} ({entry.Reason}; {entry.Reviewer})"));
        Assert.True(result.Findings.Count == 0, findings);
        Assert.True(result.UnusedExceptions.Count == 0, unused);
    }

    [Theory]
    [InlineData("<button>Save</button>", "Save")]
    [InlineData("<span>ذخیره</span>", "ذخیره")]
    [InlineData("<input aria-label=\"Search items\" />", "Search items")]
    [InlineData("<input type=\"submit\" value=\"Save\" />", "Save")]
    public void Razor_display_text_and_user_facing_attributes_are_reported(string source, string expected)
    {
        var result = HardcodedDisplayTextScanner.ScanFile("fixture.razor", source);
        Assert.Contains(result, finding => finding.Text == expected && finding.Line == 1);
    }

    [Fact]
    public void JavaScript_DOM_assignments_and_accessible_attributes_are_reported()
    {
        var source = ReadFixture("direct-dom.mjs.fixture");
        var result = HardcodedDisplayTextScanner.ScanFile("fixture.mjs", source);
        Assert.Contains(result, finding => finding.Text == "Save" && finding.Rule.Contains("textContent", StringComparison.Ordinal));
        Assert.Contains(result, finding => finding.Text == "Open settings" && finding.Rule.Contains("aria-label", StringComparison.Ordinal));
        Assert.All(result, finding => Assert.True(finding.Line > 0));
    }

    [Fact]
    public void Language_dependent_CSharp_and_JavaScript_literal_selection_is_reported()
    {
        var csharp = HardcodedDisplayTextScanner.ScanFile("selection.cs", ReadFixture("language-selection.cs.fixture"));
        var javascript = HardcodedDisplayTextScanner.ScanFile("selection.mjs", ReadFixture("language-selection.mjs.fixture"));
        Assert.Contains(csharp, finding => finding.Text == "ذخیره" && finding.Rule.Contains("language-dependent", StringComparison.Ordinal));
        Assert.Contains(csharp, finding => finding.Text == "Save" && finding.Rule.Contains("language-dependent", StringComparison.Ordinal));
        Assert.Contains(javascript, finding => finding.Text == "ذخیره" && finding.Rule.Contains("language-dependent", StringComparison.Ordinal));
        Assert.Contains(javascript, finding => finding.Text == "Save" && finding.Rule.Contains("language-dependent", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_message_literals_are_reported_from_CSharp_sinks()
    {
        const string source = "internal sealed class Example { void Validate() { messages.Add(field, \"Required.\"); } }";
        var result = HardcodedDisplayTextScanner.ScanFile("validation.cs", source);
        Assert.Contains(result, finding => finding.Text == "Required." && finding.Rule == "C# validation message");
    }

    [Fact]
    public void Resource_keys_routes_css_classes_and_diagnostic_logs_are_allowed()
    {
        foreach (var (name, extension) in new[] { ("allowed.razor.fixture", ".razor"), ("allowed.cs.fixture", ".cs"), ("allowed.mjs.fixture", ".mjs") })
        {
            var result = HardcodedDisplayTextScanner.ScanFile("allowed" + extension, ReadFixture(name));
            Assert.Empty(result);
        }
    }

    [Fact]
    public void Exception_matches_only_the_exact_file_line_and_text_occurrence()
    {
        var source = ReadFixture("exception.razor.fixture");
        var findings = HardcodedDisplayTextScanner.ScanFile("exception.razor", source);
        var exception = new HardcodedDisplayTextScanner.ExceptionEntry(
            "exception.razor", 2, 19, "Sample tool", "Razor attribute aria-label", "Fixture-only narrow exception behavior test.", "ForgeFlow UI maintainers");

        var result = HardcodedDisplayTextScanner.ApplyExceptions(findings, [exception]);

        var remaining = Assert.Single(result.Findings);
        Assert.Equal(1, remaining.Line);
        Assert.Equal("Sample tool", remaining.Text);
        Assert.Empty(result.UnusedExceptions);
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(Fixtures, fileName));
    private static string Format(HardcodedDisplayTextScanner.Finding finding)
        => $"{finding.File}:{finding.Line}:{finding.Column}: {finding.Rule}: {finding.Text}";
}
