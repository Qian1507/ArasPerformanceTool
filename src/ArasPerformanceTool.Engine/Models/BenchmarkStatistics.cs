namespace ArasPerformanceTool.Engine.Benchmarking;

/// <summary>
/// Represents aggregated performance and reliability metrics for a benchmark execution.
/// </summary>
/// <param name="TotalRequests">Total number of operations dispatched during the benchmark.</param>
/// <param name="SuccessCount">Number of operations that completed successfully without error.</param>
/// <param name="FailureCount">Number of operations that threw exceptions or returned faults.</param>
/// <param name="TotalDurationSeconds">Wall-clock duration of the benchmark run in seconds.</param>
/// <param name="ThroughputRequestsPerSecond">Throughput calculated as successful operations per wall-clock second.</param>
/// <param name="MinMs">Minimum latency among successful operations in milliseconds.</param>
/// <param name="MaxMs">Maximum latency among successful operations in milliseconds.</param>
/// <param name="AverageMs">Mean latency of all successful operations in milliseconds.</param>
/// <param name="P50Ms">50th percentile (median) latency in milliseconds.</param>
/// <param name="P95Ms">95th percentile latency in milliseconds.</param>
/// <param name="P99Ms">99th percentile latency in milliseconds, highlighting tail latency spikes.</param>
public sealed record BenchmarkStatistics(
    int TotalRequests,
    int SuccessCount,
    int FailureCount,
    double TotalDurationSeconds,
    double ThroughputRequestsPerSecond,
    double MinMs,
    double MaxMs,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms);