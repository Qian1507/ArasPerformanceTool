
using ArasPerformanceTool.Engine.Benchmarking;

namespace ArasPerformanceTool.Api.DTOs;

/// <summary>
/// Represents the lifecycle status of a benchmark job.
/// </summary>
public enum BenchmarkStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>
/// Response containing benchmark execution status and performance metrics.
/// </summary>
/// <param name="JobId">Unique identifier for the benchmark run.</param>
/// <param name="Status">Current or final benchmark status.</param>
/// <param name="ItemType">Aras ItemType used in the benchmark.</param>
/// <param name="Iterations">Number of requested benchmark iterations.</param>
/// <param name="Concurrency">Maximum number of concurrent operations.</param>
/// <param name="CreatedAtUtc">UTC time when the benchmark was created.</param>
/// <param name="CompletedAtUtc">UTC time when execution finished, if available.</param>
/// <param name="Statistics">Aggregated performance metrics, if available.</param>
/// <param name="ErrorMessage">Error description if execution failed.</param>
public sealed record BenchmarkStatusResponse(
    Guid JobId,
    BenchmarkStatus Status,
    string ItemType,
    int Iterations,
    int Concurrency,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    BenchmarkStatistics? Statistics,
    string? ErrorMessage);
