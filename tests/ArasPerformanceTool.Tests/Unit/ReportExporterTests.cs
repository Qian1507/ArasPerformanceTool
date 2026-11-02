using System.Text.Json;
using System.Text.Json.Serialization;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Reporting;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public class ReportExporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static BenchmarkReport CreateSampleReport(string? errorMessage = null)
    {
        var configuration = new BenchmarkConfiguration("Part", Iterations: 2, Concurrency: 1);
        var statistics = new BenchmarkStatistics(
            TotalRequests: 2,
            SuccessCount: errorMessage == null ? 2 : 1,
            FailureCount: errorMessage == null ? 0 : 1,
            TotalDurationSeconds: 0.5,
            ThroughputRequestsPerSecond: 4.0,
            MinMs: 100.5,
            MaxMs: 150.2,
            AverageMs: 125.35,
            P50Ms: 100.5,
            P95Ms: 150.2,
            P99Ms: 150.2);

        var rawMeasurements = new List<OperationMeasurement>
        {
            new(ElapsedMilliseconds: 100.5, IsSuccess: true, ErrorMessage: null),
            new(ElapsedMilliseconds: 150.2, IsSuccess: errorMessage == null, ErrorMessage: errorMessage)
        };

        return new BenchmarkReport(
            "CreatePartBenchmark",
            configuration,
            statistics,
            rawMeasurements);
    }

    [Fact]
    public void ExportJson_ShouldProduceValidJson_AndRoundTripSuccessfully()
    {
        // Arrange
        var originalReport = CreateSampleReport();

        // Act
        var json = ReportExporter.ExportJson(originalReport);

        // Assert: Parse structure
        Assert.False(string.IsNullOrWhiteSpace(json));
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Assert.Equal("CreatePartBenchmark", root.GetProperty("workloadName").GetString());
            Assert.Equal("Part", root.GetProperty("configuration").GetProperty("itemType").GetString());
            Assert.Equal(2, root.GetProperty("statistics").GetProperty("totalRequests").GetInt32());
        }

        // Assert: Full round-trip deserialization back to BenchmarkReport domain object
        var deserializedReport = JsonSerializer.Deserialize<BenchmarkReport>(json, JsonOptions);
        Assert.NotNull(deserializedReport);
        Assert.Equal(originalReport.WorkloadName, deserializedReport.WorkloadName);
        Assert.Equal(originalReport.Configuration.ItemType, deserializedReport.Configuration.ItemType);
        Assert.Equal(originalReport.RawMeasurements.Count, deserializedReport.RawMeasurements.Count);
        Assert.Equal(originalReport.Statistics.AverageMs, deserializedReport.Statistics.AverageMs);
    }

    [Fact]
    public void ExportCsv_ShouldProduceCorrectHeaderAndRows()
    {
        // Arrange
        var report = CreateSampleReport();

        // Act
        var csv = ReportExporter.ExportCsv(report);

        // Assert
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // 1 header + 2 data rows

        Assert.Equal("Index,WorkloadName,ItemType,ElapsedMs,IsSuccess,ErrorMessage", lines[0]);
        Assert.Equal("0,CreatePartBenchmark,Part,100.500,true,", lines[1]);
        Assert.Equal("1,CreatePartBenchmark,Part,150.200,true,", lines[2]);
    }

    [Fact]
    public void ExportCsv_ShouldProperlyEscapeSpecialCharacters()
    {
        // Arrange: error message with commas, quotes, and newlines
        const string rawError = "Error: \"Timeout\", connection failed\nretry aborted";
        var report = CreateSampleReport(errorMessage: rawError);

        // Act
        var csv = ReportExporter.ExportCsv(report);

        // Assert: quotes doubled and wrapped in quotes
        Assert.Contains("\"Error: \"\"Timeout\"\", connection failed\nretry aborted\"", csv);
    }

    [Fact]
    public void ExportCsv_ShouldPrefixFormulaCharacters_ToPreventCsvInjection()
    {
        // Arrange: error message starting with dangerous Excel formula prefixes
        const string formulaError = "=HYPERLINK(\"http://malicious.site\", \"Click Me\")";
        var report = CreateSampleReport(errorMessage: formulaError);

        // Act
        var csv = ReportExporter.ExportCsv(report);

        // Assert: Dangerous prefix '=' must be neutralized with a leading single quote
        Assert.Contains("\"'=HYPERLINK(\"\"http://malicious.site\"\", \"\"Click Me\"\")\"", csv);
    }

    
    [Theory]
    [InlineData(
        "=HYPERLINK(\"http://malicious.site\", \"Click\")",
        "\"'=HYPERLINK(\"\"http://malicious.site\"\", \"\"Click\"\")\"")]
    [InlineData(
        "   =1+1",
        "'   =1+1")]
    [InlineData(
        "@SUM(A1:A10)",
        "'@SUM(A1:A10)")]
    [InlineData(
        "  +1+2",
        "'  +1+2")]
    public void ExportCsv_ShouldPrefixDangerousFormulaCharacters_EvenWithLeadingWhitespace(
        string rawError,
        string expectedSubstring)
    {
        // Arrange: Create a report containing a formula-like error message
        var report = CreateSampleReport(errorMessage: rawError);

        // Act: Export the report as CSV
        var csv = ReportExporter.ExportCsv(report);

        // Assert: Formula-like values are prefixed with an apostrophe
        Assert.Contains(expectedSubstring, csv);
    }

}