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
    [Fact]
    public void BenchmarkReport_ShouldPreserveMetadata_AfterSerialization()
    {
        // Arrange
        const string runId = "333416a8216945edbbccefc501ade4e1";
        const string mode = "Standard";
        const string operation = "Read";

        var sampleJson = ReportExporter.ExportJson(CreateSampleReport());

        var reportNode = System.Text.Json.Nodes.JsonNode
            .Parse(sampleJson)!
            .AsObject();

        reportNode["runId"] = runId;
        reportNode["mode"] = mode;
        reportNode["operation"] = operation;

        var originalReport = JsonSerializer.Deserialize<BenchmarkReport>(
            reportNode.ToJsonString(),
            JsonOptions);

        Assert.NotNull(originalReport);
        Assert.Equal(runId, originalReport.RunId);
        Assert.Equal(mode, originalReport.Mode);
        Assert.Equal(operation, originalReport.Operation);

        // Act
        var json = ReportExporter.ExportJson(originalReport);

        var restoredReport = JsonSerializer.Deserialize<BenchmarkReport>(
            json,
            JsonOptions);

        // Assert: exported JSON contains the metadata
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(runId, root.GetProperty("runId").GetString());
        Assert.Equal(mode, root.GetProperty("mode").GetString());
        Assert.Equal(operation, root.GetProperty("operation").GetString());

        // Assert: metadata survives the round trip
        Assert.NotNull(restoredReport);
        Assert.Equal(runId, restoredReport.RunId);
        Assert.Equal(mode, restoredReport.Mode);
        Assert.Equal(operation, restoredReport.Operation);

        Assert.Equal(originalReport.WorkloadName, restoredReport.WorkloadName);
        Assert.Equal(
            originalReport.Configuration.ItemType,
            restoredReport.Configuration.ItemType);
        Assert.Equal(
            originalReport.RawMeasurements.Count,
            restoredReport.RawMeasurements.Count);
    }
    [Fact]
    public void BenchmarkReport_ShouldDeserializeLegacyJson_WithoutMetadata()
    {
        // Arrange
        const string legacyJson = """
        {
        "workloadName": "CreatePartBenchmark",
        "configuration": {
            "itemType": "Part",
            "iterations": 2,
            "concurrency": 1
        },
        "statistics": {
            "totalRequests": 2,
            "successCount": 2,
            "failureCount": 0,
            "totalDurationSeconds": 0.5,
            "throughputRequestsPerSecond": 4.0,
            "minMs": 100.5,
            "maxMs": 150.2,
            "averageMs": 125.35,
            "p50Ms": 100.5,
            "p95Ms": 150.2,
            "p99Ms": 150.2
        },
        "rawMeasurements": [
            {
            "elapsedMilliseconds": 100.5,
            "isSuccess": true,
            "errorMessage": null
            },
            {
            "elapsedMilliseconds": 150.2,
            "isSuccess": true,
            "errorMessage": null
            }
        ]
        }
        """;

        // Act
        var report = JsonSerializer.Deserialize<BenchmarkReport>(
            legacyJson,
            JsonOptions);

        // Assert: existing report data remains readable
        Assert.NotNull(report);
        Assert.Equal("CreatePartBenchmark", report.WorkloadName);

        Assert.Equal("Part", report.Configuration.ItemType);
        Assert.Equal(2, report.Configuration.Iterations);
        Assert.Equal(1, report.Configuration.Concurrency);

        Assert.Equal(2, report.Statistics.TotalRequests);
        Assert.Equal(2, report.Statistics.SuccessCount);
        Assert.Equal(0, report.Statistics.FailureCount);
        Assert.Equal(125.35, report.Statistics.AverageMs);

        Assert.Equal(2, report.RawMeasurements.Count);
        Assert.All(report.RawMeasurements, measurement =>
        {
            Assert.True(measurement.IsSuccess);
            Assert.Null(measurement.ErrorMessage);
        });

        // Assert: missing metadata is not invented
        Assert.Null(report.RunId);
        Assert.Null(report.Mode);
        Assert.Null(report.Operation);
    }
}