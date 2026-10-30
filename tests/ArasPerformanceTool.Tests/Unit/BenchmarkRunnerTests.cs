using ArasPerformanceTool.Engine.Benchmarking;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public class BenchmarkRunnerTests
{
    private readonly BenchmarkRunner _runner = new();

    [Fact]
    public async Task RunAsync_ShouldExecuteAllIterations_AndCollectValidMetrics()
    {
        // Arrange: Configure 10 iterations with concurrency of 2
        var config = new BenchmarkConfiguration("Part", Iterations: 10, Concurrency: 2);
        var executedIndices = new List<int>();

        // Act: Simulate asynchronous workload execution
        var report = await _runner.RunAsync("MockWorkload", config, async index =>
        {
            await Task.Delay(10);
            lock (executedIndices)
            {
                executedIndices.Add(index);
            }
        });

        // Assert: Verify all iterations executed exactly once without drops or duplicates
        Assert.Equal(
            Enumerable.Range(0, 10),
            executedIndices.OrderBy(x => x));

        // Assert: Verify aggregated metrics
        Assert.Equal(10, report.Statistics.TotalRequests);
        Assert.Equal(10, report.Statistics.SuccessCount);
        Assert.Equal(0, report.Statistics.FailureCount);
        Assert.True(report.Statistics.AverageMs > 0);
        Assert.Equal(10, report.RawMeasurements.Count);
    }

    [Fact]
    public async Task RunAsync_ShouldCountFailedOperations()
    {
        // Arrange: Execute 5 iterations sequentially
        var config = new BenchmarkConfiguration(
            "Part",
            Iterations: 5,
            Concurrency: 1);

        // Act: Simulate a failure at iteration 2
        var report = await _runner.RunAsync(
            "FailingWorkload",
            config,
            index =>
            {
                if (index == 2)
                {
                    throw new InvalidOperationException("Simulated AML failure");
                }

                return Task.CompletedTask;
            });

        // Assert: Verify successful and failed requests
        Assert.Equal(5, report.Statistics.TotalRequests);
        Assert.Equal(4, report.Statistics.SuccessCount);
        Assert.Equal(1, report.Statistics.FailureCount);

        Assert.Equal(5, report.RawMeasurements.Count);
        Assert.Single(report.RawMeasurements, m => !m.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_ShouldThrowOperationCanceledException_WhenCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var config = new BenchmarkConfiguration("Part", Iterations: 20, Concurrency: 1);

        // Cancel after index 2 starts
        Task WorkloadAction(int index)
        {
            if (index == 2)
            {
                cts.Cancel();
            }
            return Task.Delay(50, cts.Token);
        }

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync("CancellableWorkload", config, WorkloadAction, cts.Token));
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("Part", 0, 1)]
    [InlineData("Part", 1, 0)]
    public async Task RunAsync_ShouldValidateParameters(string itemType, int iterations, int concurrency)
    {
        // Arrange
        var config = new BenchmarkConfiguration(itemType, iterations, concurrency);

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _runner.RunAsync("ValidationWorkload", config, _ => Task.CompletedTask));
    }
}