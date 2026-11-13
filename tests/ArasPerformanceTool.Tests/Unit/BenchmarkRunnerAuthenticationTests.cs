using System.Collections.Concurrent;
using System.Net;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Exceptions;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public sealed class BenchmarkRunnerAuthenticationTests
{
    [Fact]
    public async Task RunAsync_WhenUnauthorized_StopsNewOperationsAndPreservesPartialReport()
    {
        // Arrange
        var runner = new BenchmarkRunner();

        var config = new BenchmarkConfiguration(
            "Part",
            5,
            1);

        var executedIndices = new List<int>();

        var authenticationFailure = new HttpRequestException(
            "Aras rejected the access token.",
            inner: null,
            statusCode: HttpStatusCode.Unauthorized);

        // Act
        var exception =
            await Assert.ThrowsAsync<BenchmarkAuthenticationException>(
                () => runner.RunAsync(
                    "AuthenticationFailureTest",
                    config,
                    index =>
                    {
                        executedIndices.Add(index);

                        if (index == 1)
                        {
                            return Task.FromException(
                                authenticationFailure);
                        }

                        return Task.CompletedTask;
                    }));

        // Assert: Only the first two operations started.
        Assert.Equal(
            new[] { 0, 1 },
            executedIndices);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            exception.StatusCode);

        Assert.Same(
            authenticationFailure,
            exception.InnerException);

        var report = exception.PartialReport;

        // Preserve the requested configuration.
        Assert.Equal(5, report.Configuration.Iterations);

        // Statistics include only operations that actually ran.
        Assert.Equal(2, report.RawMeasurements.Count);
        Assert.Equal(2, report.Statistics.TotalRequests);
        Assert.Equal(1, report.Statistics.SuccessCount);
        Assert.Equal(1, report.Statistics.FailureCount);

        Assert.True(report.RawMeasurements[0].IsSuccess);
        Assert.False(report.RawMeasurements[1].IsSuccess);

        Assert.Equal(
            authenticationFailure.Message,
            report.RawMeasurements[1].ErrorMessage);
    }

     [Fact]
    public async Task RunAsync_WhenConcurrentOperationReturnsUnauthorized_PreservesBothExecutedResults()
    {
        // Arrange
        var runner = new BenchmarkRunner();

        var config = new BenchmarkConfiguration(
            "Part",
            2,
            2);

        var unauthorizedOperationStarted =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var successfulOperationStarted =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var startedIndices = new ConcurrentQueue<int>();
        var completedIndices = new ConcurrentQueue<int>();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));

        var authenticationFailure = new HttpRequestException(
            "Aras rejected the access token.",
            inner: null,
            statusCode: HttpStatusCode.Unauthorized);

        // Act
        var exception =
            await Assert.ThrowsAsync<BenchmarkAuthenticationException>(
                () => runner.RunAsync(
                    "ConcurrentAuthenticationFailureTest",
                    config,
                    async index =>
                    {
                        startedIndices.Enqueue(index);

                        if (index == 0)
                        {
                            unauthorizedOperationStarted.TrySetResult(true);

                            // Do not fail until the other operation has started.
                            await successfulOperationStarted.Task
                                .WaitAsync(timeout.Token);

                            throw authenticationFailure;
                        }

                        successfulOperationStarted.TrySetResult(true);

                        // Ensure both operations entered the workload.
                        await unauthorizedOperationStarted.Task
                            .WaitAsync(timeout.Token);

                        completedIndices.Enqueue(index);
                    },
                    timeout.Token));

        // Assert: Both operations started exactly once.
        Assert.Equal(
            new[] { 0, 1 },
            startedIndices.OrderBy(index => index).ToArray());

        // The successful operation completed.
        Assert.Equal(
            new[] { 1 },
            completedIndices.ToArray());

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            exception.StatusCode);

        Assert.Same(
            authenticationFailure,
            exception.InnerException);

        var report = exception.PartialReport;

        Assert.Equal(2, report.Configuration.Iterations);
        Assert.Equal(2, report.Configuration.Concurrency);

        Assert.Equal(2, report.RawMeasurements.Count);
        Assert.Equal(2, report.Statistics.TotalRequests);
        Assert.Equal(1, report.Statistics.SuccessCount);
        Assert.Equal(1, report.Statistics.FailureCount);

        Assert.Equal(
            report.Statistics.TotalRequests,
            report.Statistics.SuccessCount +
            report.Statistics.FailureCount);

        // Measurements retain iteration-index order.
        Assert.False(report.RawMeasurements[0].IsSuccess);
        Assert.True(report.RawMeasurements[1].IsSuccess);

        Assert.Equal(
            authenticationFailure.Message,
            report.RawMeasurements[0].ErrorMessage);
    }
}