using System.Diagnostics;
using System.Net;
using ArasPerformanceTool.Engine.Exceptions;


namespace ArasPerformanceTool.Engine.Benchmarking;

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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            config.Iterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            config.Concurrency);

        var measurements =
            new OperationMeasurement?[config.Iterations];

        HttpRequestException? authenticationFailure = null;

        var overallStart = Stopwatch.GetTimestamp();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = config.Concurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, config.Iterations),
            parallelOptions,
            async (index, token) =>
            {
                // Do not start another workload after authentication fails.
                if (Volatile.Read(ref authenticationFailure) is not null)
                {
                    return;
                }

                token.ThrowIfCancellationRequested();

                var operationStart = Stopwatch.GetTimestamp();

                try
                {
                    measurements[index] =
                        await OperationTimer.MeasureAsync(
                            () => workloadAction(index),
                            token).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                    when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Publish the failure before processing its measurement.
                    Interlocked.CompareExchange(
                        ref authenticationFailure,
                        ex,
                        null);

                    var elapsedMs = Stopwatch
                        .GetElapsedTime(operationStart)
                        .TotalMilliseconds;

                    measurements[index] = new OperationMeasurement(
                        false,
                        elapsedMs,
                        ex.Message);
                }
            }).ConfigureAwait(false);

        var totalSeconds =
            Stopwatch.GetElapsedTime(overallStart).TotalSeconds;

        // Null slots represent iterations that never started.
        var executedMeasurements = measurements
            .OfType<OperationMeasurement>()
            .ToArray();

        var stats = StatisticsCalculator.Calculate(
            executedMeasurements,
            totalSeconds);

        var report = new BenchmarkReport(
            workloadName,
            config,
            stats,
            executedMeasurements);

        if (authenticationFailure is not null)
        {
            throw new BenchmarkAuthenticationException(
                report,
                authenticationFailure);
        }

        return report;
    }
}