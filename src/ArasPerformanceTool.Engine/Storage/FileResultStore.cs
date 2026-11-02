using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Reporting;

namespace ArasPerformanceTool.Engine.Storage;

/// <summary>
/// Provides file-based persistence for benchmark reports and exported artifacts.
/// </summary>
public class FileResultStore
{
    private readonly string _storageDirectory;

    public FileResultStore(string? storageDirectory = null)
    {
        _storageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "reports")
            : storageDirectory;
    }

    /// <summary>
    /// Gets the configured root storage directory path.
    /// </summary>
    public string StorageDirectory => _storageDirectory;

    /// <summary>
    /// Saves both JSON and CSV representations of a benchmark report to disk.
    /// </summary>
    /// <param name="jobId">The unique identifier of the benchmark job.</param>
    /// <param name="report">The benchmark report data to serialize and store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveAsync(
        Guid jobId,
        BenchmarkReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        EnsureDirectoryExists();

        var jsonContent = ReportExporter.ExportJson(report);
        var csvContent = ReportExporter.ExportCsv(report);

        var jsonPath = GetJsonFilePath(jobId);
        var csvPath = GetCsvFilePath(jobId);

        await File.WriteAllTextAsync(jsonPath, jsonContent, cancellationToken);
        await File.WriteAllTextAsync(csvPath, csvContent, cancellationToken);
    }

    /// <summary>
    /// Reads the raw exported JSON content for a specific benchmark job.
    /// </summary>
    /// <param name="jobId">The benchmark job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw JSON file content, or null if the report does not exist.</returns>
    public async Task<string?> GetJsonAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetJsonFilePath(jobId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Reads the raw exported CSV content for a specific benchmark job.
    /// </summary>
    /// <param name="jobId">The benchmark job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw CSV file content, or null if the report does not exist.</returns>
    public async Task<string?> GetCsvAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetCsvFilePath(jobId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Checks whether report files exist for the specified benchmark job.
    /// </summary>
    public bool Exists(Guid jobId)
    {
        return File.Exists(GetJsonFilePath(jobId))
            && File.Exists(GetCsvFilePath(jobId));
    }

    /// <summary>
    /// Gets the absolute path for the JSON export file.
    /// </summary>
    public string GetJsonFilePath(Guid jobId) =>
        Path.Combine(_storageDirectory, $"{jobId:D}.json");

    /// <summary>
    /// Gets the absolute path for the CSV export file.
    /// </summary>
    public string GetCsvFilePath(Guid jobId) =>
        Path.Combine(_storageDirectory, $"{jobId:D}.csv");

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }
}