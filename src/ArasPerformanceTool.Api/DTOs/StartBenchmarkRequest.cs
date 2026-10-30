using System.ComponentModel.DataAnnotations;

namespace ArasPerformanceTool.Api.DTOs;

/// <summary>
/// Request contract for initiating a benchmark execution.
/// </summary>
public sealed class StartBenchmarkRequest
{
    /// <summary>
    /// Target Aras ItemType to benchmark (e.g., "Part", "Document").
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ItemType { get; init; } = "Part";

    /// <summary>
    /// Number of operations to execute.
    /// </summary>
    [Range(1, 10000)]
    public int Iterations { get; init; } = 5;

    /// <summary>
    /// Degree of concurrency for workload execution.
    /// </summary>
    [Range(1, 64)]
    public int Concurrency { get; init; } = 1;

    /// <summary>
    /// Optional display name for this benchmark run.
    /// The M5 API currently executes CreateWorkload only.
    /// </summary>
    public string? WorkloadName { get; init; }
}