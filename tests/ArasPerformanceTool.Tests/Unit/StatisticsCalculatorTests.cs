
using ArasPerformanceTool.Engine.Benchmarking;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public class StatisticsCalculatorTests
{
    [Fact]
    public void Calculate_ShouldReturnCorrectMetrics()
    {
        // Arrange: Three successful requests and one failed request
        var measurements = new[]
        {
            new OperationMeasurement(true, 10),
            new OperationMeasurement(true, 20),
            new OperationMeasurement(true, 30),
            new OperationMeasurement(false, 100, "Simulated failure")
        };

        const double durationSeconds = 2.0;

        // Act
        var result = StatisticsCalculator.Calculate(
            measurements,
            durationSeconds);

        // Assert: Request counts
        Assert.Equal(4, result.TotalRequests);
        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);

        // Assert: Latency metrics include only successful requests
        Assert.Equal(10, result.MinMs);
        Assert.Equal(30, result.MaxMs);
        Assert.Equal(20, result.AverageMs);

        // Assert: Successful throughput = 3 / 2 = 1.5 req/s
        Assert.Equal(1.5, result.ThroughputRequestsPerSecond);
        Assert.Equal(2.0, result.TotalDurationSeconds);
    }

    [Fact]
    public void Calculate_ShouldCalculateCorrectPercentiles()
    {
        // Arrange: Ten successful measurements from 10ms to 100ms
        var measurements = Enumerable.Range(1, 10)
            .Select(i => new OperationMeasurement(true, i * 10))
            .ToArray();

        const double durationSeconds = 1.0;

        // Act
        var result = StatisticsCalculator.Calculate(
            measurements,
            durationSeconds);

        // Assert: Nearest-rank percentile calculations
        Assert.Equal(50, result.P50Ms);
        Assert.Equal(100, result.P95Ms);
        Assert.Equal(100, result.P99Ms);

        // Assert: Additional latency metrics
        Assert.Equal(10, result.MinMs);
        Assert.Equal(100, result.MaxMs);
        Assert.Equal(55, result.AverageMs);
    }

    [Fact]
    public void Calculate_ShouldHandleAllFailuresGracefully()
    {
        // Arrange: All operations failed
        var measurements = new[]
        {
            new OperationMeasurement(false, 50, "Connection refused"),
            new OperationMeasurement(false, 80, "Timeout")
        };

        const double durationSeconds = 1.0;

        // Act
        var result = StatisticsCalculator.Calculate(
            measurements,
            durationSeconds);

        // Assert: All requests are counted as failures
        Assert.Equal(2, result.TotalRequests);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(2, result.FailureCount);

        // Assert: No successful throughput
        Assert.Equal(0, result.ThroughputRequestsPerSecond);
        Assert.Equal(1.0, result.TotalDurationSeconds);

        // Assert: No successful latency measurements
        Assert.Equal(0, result.MinMs);
        Assert.Equal(0, result.MaxMs);
        Assert.Equal(0, result.AverageMs);
        Assert.Equal(0, result.P50Ms);
        Assert.Equal(0, result.P95Ms);
        Assert.Equal(0, result.P99Ms);
    }

    [Fact]
    public void Calculate_ShouldHandleEmptyMeasurementsWithoutDivisionByZero()
    {
        // Arrange: No operations executed
        var measurements = Array.Empty<OperationMeasurement>();

        const double durationSeconds = 0.5;

        // Act
        var result = StatisticsCalculator.Calculate(
            measurements,
            durationSeconds);

        // Assert: Request counts are zero
        Assert.Equal(0, result.TotalRequests);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);

        // Assert: Duration is preserved without division by zero
        Assert.Equal(0.5, result.TotalDurationSeconds);
        Assert.Equal(0, result.ThroughputRequestsPerSecond);

        // Assert: Latency metrics are zero
        Assert.Equal(0, result.MinMs);
        Assert.Equal(0, result.MaxMs);
        Assert.Equal(0, result.AverageMs);
        Assert.Equal(0, result.P50Ms);
        Assert.Equal(0, result.P95Ms);
        Assert.Equal(0, result.P99Ms);
    }
}
