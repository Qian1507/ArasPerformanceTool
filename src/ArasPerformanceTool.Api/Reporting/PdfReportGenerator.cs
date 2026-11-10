
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArasPerformanceTool.Api.Reporting;

public sealed class PdfReportGenerator
{
    public byte[] GenerateSummary(string json)
    {
         ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var report = document.RootElement;

        var configuration = GetObject(report, "Configuration");
        var statistics = GetObject(report, "Statistics");

        var fields = new (string Label, string Value)[]
        {
            ("Workload", GetValue(report, "WorkloadName")),
            ("Item Type", GetValue(configuration, "ItemType")),
            ("Iterations", GetValue(configuration, "Iterations")),
            ("Concurrency", GetValue(configuration, "Concurrency")),
            ("Total Requests", GetValue(statistics, "TotalRequests")),
            ("Successful", GetValue(statistics, "SuccessCount")),
            ("Failed", GetValue(statistics, "FailureCount")),
            ("Duration (s)", GetValue(statistics, "TotalDurationSeconds")),
            ("Throughput (requests/s)", GetValue(statistics, "ThroughputRequestsPerSecond")),
            ("Minimum (ms)", GetValue(statistics, "MinMs")),
            ("Maximum (ms)", GetValue(statistics, "MaxMs")),
            ("Average (ms)", GetValue(statistics, "AverageMs")),
            ("P50 (ms)", GetValue(statistics, "P50Ms")),
            ("P95 (ms)", GetValue(statistics, "P95Ms")),
            ("P99 (ms)", GetValue(statistics, "P99Ms"))
        };

        return GenerateTablePdf(
            "Benchmark Performance Summary",
            new[] { "Metric", "Value" },
            fields.Select(x => new[] { x.Label, x.Value }));
    }

    public byte[] GenerateMeasurements(string csv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csv);
        using var reader = new StringReader(csv);
        using var parser = new TextFieldParser(reader);

        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        var headers = parser.ReadFields()
            ?? throw new InvalidDataException("CSV has no header.");

        if (headers.Length == 0)
            throw new InvalidDataException("CSV has no columns.");

        var rows = new List<string[]>();

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();

            if (fields == null || fields.Length != headers.Length)
                throw new InvalidDataException("Invalid CSV row.");

            rows.Add(fields);
        }

        return GenerateTablePdf(
            "Benchmark Measurements",
            headers,
            rows);
    }

    private static byte[] GenerateTablePdf(
        string title,
        string[] headers,
        IEnumerable<string[]> rows)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header()
                    .Text(title)
                    .SemiBold()
                    .FontSize(18);

                page.Content().PaddingTop(15).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in headers)
                            columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var heading in headers)
                        {
                            header.Cell()
                                .Background(Colors.Grey.Lighten3)
                                .Padding(6)
                                .Text(heading)
                                .SemiBold();
                        }
                    });

                    foreach (var row in rows)
                    {
                        foreach (var value in row)
                        {
                            table.Cell()
                                .BorderBottom(0.5f)
                                .BorderColor(Colors.Grey.Lighten2)
                                .Padding(5)
                                .Text(value ?? "");
                        }
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                });
            });
        }).GeneratePdf();
    }

    private static JsonElement GetObject(JsonElement parent, string name)
    {
        if (TryGetProperty(parent, name, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        throw new InvalidDataException($"Missing report section: {name}");
    }

    private static string GetValue(JsonElement parent, string name)
    {
        return TryGetProperty(parent, name, out var value)
            ? value.ToString()
            : "-";
    }

    private static bool TryGetProperty(
        JsonElement parent,
        string name,
        out JsonElement value)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
