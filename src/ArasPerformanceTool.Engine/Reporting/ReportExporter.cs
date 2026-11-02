using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArasPerformanceTool.Engine.Benchmarking;

namespace ArasPerformanceTool.Engine.Reporting;

/// <summary>
/// Exports benchmark reports to JSON and CSV formats.
/// </summary>
public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly char[] DangerousFormulaPrefixes = { '=', '+', '-', '@', '\t', '\r' };

    /// <summary>
    /// Exports the complete benchmark report as formatted JSON.
    /// </summary>
    public static string ExportJson(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    /// <summary>
    /// Exports individual operation measurements as CSV.
    /// </summary>
    public static string ExportCsv(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();

        // CSV header
        builder.AppendLine("Index,WorkloadName,ItemType,ElapsedMs,IsSuccess,ErrorMessage");

        // Export each measurement using its position as the index
        for (var i = 0; i < report.RawMeasurements.Count; i++)
        {
            var measurement = report.RawMeasurements[i];

            builder.Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(',');

            builder.Append(EscapeCsvField(report.WorkloadName))
                .Append(',');

            builder.Append(EscapeCsvField(report.Configuration.ItemType))
                .Append(',');

            builder.Append(
                measurement.ElapsedMilliseconds.ToString(
                    "F3",
                    CultureInfo.InvariantCulture))
                .Append(',');

            builder.Append(measurement.IsSuccess ? "true" : "false")
                .Append(',');

            builder.Append(
                EscapeCsvField(measurement.ErrorMessage ?? string.Empty));

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes commas, quotation marks, line breaks and mitigates CSV formula injection risks.
    /// </summary>
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }
        
        var sanitizedField = field;

        // Check the first non-whitespace character for formula prefixes
        var trimmedField = field.TrimStart();
        if (trimmedField.Length > 0 && DangerousFormulaPrefixes.Contains(trimmedField[0]))
        {
            sanitizedField = "'" + field;
        }

        var mustQuote =
            sanitizedField.Contains(',') ||
            sanitizedField.Contains('"') ||
            sanitizedField.Contains('\r') ||
            sanitizedField.Contains('\n');

        if (!mustQuote)
        {
            return sanitizedField;
        }

        // Double embedded quotes and wrap the field in quotes
        var escaped = sanitizedField.Replace("\"", "\"\"");

        return $"\"{escaped}\"";
    }
}