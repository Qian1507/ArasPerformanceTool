
using System.Diagnostics;

namespace ArasPerformanceTool.Engine.Benchmarking;

public sealed record BenchmarkConfiguration(
    string ItemType,
    int Iterations,
    int Concurrency = 1);

public sealed record BenchmarkReport(
    string WorkloadName,
    BenchmarkConfiguration Configuration,
    BenchmarkStatistics Statistics,
    IReadOnlyList<OperationMeasurement> RawMeasurements);

public class BenchmarkRunner
{
    public async Task<BenchmarkReport> RunAsync(
        string workloadName,
        BenchmarkConfiguration config,
        Func<int, Task> workloadAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(workloadAction);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadName);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ItemType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.Iterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.Concurrency);

        // Store one result per iteration, preserving index order
        var measurements = new OperationMeasurement[config.Iterations];

        // Measure the entire benchmark execution
        var overallStart = Stopwatch.GetTimestamp();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = config.Concurrency,
            CancellationToken = cancellationToken
        };

        // Execute the workload for the requested number of iterations
        await Parallel.ForEachAsync(
            Enumerable.Range(0, config.Iterations),
            parallelOptions,
            async (index, token) =>
            {
                measurements[index] = await OperationTimer.MeasureAsync(
                    () => workloadAction(index),
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        // Calculate overall execution duration and statistics
        var totalSeconds =
            Stopwatch.GetElapsedTime(overallStart).TotalSeconds;

        var stats = StatisticsCalculator.Calculate(
            measurements,
            totalSeconds);

        return new BenchmarkReport(
            workloadName,
            config,
            stats,
            measurements);
    }
}
