namespace ArasPerformanceTool.Engine.Benchmarking;

public static class StatisticsCalculator
{
    public static BenchmarkStatistics Calculate(
        IReadOnlyCollection<OperationMeasurement> measurements,
        double wallClockDurationSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wallClockDurationSeconds);

        if (measurements == null || measurements.Count == 0)
        {
            return new BenchmarkStatistics(
                TotalRequests: 0,
                SuccessCount: 0,
                FailureCount: 0,
                TotalDurationSeconds: wallClockDurationSeconds,
                ThroughputRequestsPerSecond: 0,
                MinMs: 0,
                MaxMs: 0,
                AverageMs: 0,
                P50Ms: 0,
                P95Ms: 0,
                P99Ms: 0);
        }

        var total = measurements.Count;
        var successes = measurements.Count(m => m.IsSuccess);
        var failures = total - successes;

        var successfulLatencies = measurements
            .Where(m => m.IsSuccess)
            .Select(m => m.ElapsedMilliseconds)
            .OrderBy(ms => ms)
            .ToList();

        if (successfulLatencies.Count == 0)
        {
            return new BenchmarkStatistics(
                TotalRequests: total,
                SuccessCount: 0,
                FailureCount: failures,
                TotalDurationSeconds: wallClockDurationSeconds,
                ThroughputRequestsPerSecond: 0,
                MinMs: 0,
                MaxMs: 0,
                AverageMs: 0,
                P50Ms: 0,
                P95Ms: 0,
                P99Ms: 0);
        }

        var min = successfulLatencies[0];
        var max = successfulLatencies[^1];
        var avg = successfulLatencies.Average();
        var throughput = wallClockDurationSeconds > 0
            ? successes / wallClockDurationSeconds
            : 0;

        return new BenchmarkStatistics(
            TotalRequests: total,
            SuccessCount: successes,
            FailureCount: failures,
            TotalDurationSeconds: wallClockDurationSeconds,
            ThroughputRequestsPerSecond: throughput,
            MinMs: Math.Round(min, 2),
            MaxMs: Math.Round(max, 2),
            AverageMs: Math.Round(avg, 2),
            P50Ms: Math.Round(GetPercentile(successfulLatencies, 0.50), 2),
            P95Ms: Math.Round(GetPercentile(successfulLatencies, 0.95), 2),
            P99Ms: Math.Round(GetPercentile(successfulLatencies, 0.99), 2));
    }

    private static double GetPercentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (percentile <= 0) return sortedValues[0];
        if (percentile >= 1) return sortedValues[^1];

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}