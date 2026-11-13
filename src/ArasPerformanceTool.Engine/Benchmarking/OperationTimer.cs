using System.Diagnostics;
using ArasPerformanceTool.Engine.Models;
namespace ArasPerformanceTool.Engine.Benchmarking;




/// <summary>
/// Utility providing lightweight, high-precision latency measurement for asynchronous operations.
/// </summary>
public static class OperationTimer
{
    /// <summary>
    /// Measures the elapsed execution time of an asynchronous operation using high-resolution timestamps.
    /// </summary>
    /// <param name="operation">The asynchronous operation delegate to measure.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>An <see cref="OperationMeasurement"/> encapsulating outcome and execution duration.</returns>
    public static async Task<OperationMeasurement> MeasureAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Capture high-precision tick counter before invocation without heap allocation
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await operation().ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            return new OperationMeasurement(true, elapsedMs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Rethrow immediately when cancellation was intentionally triggered by the caller
            throw;
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Authentication failure affects the entire run,
            // not just a single benchmark measurement.
            throw;
        }

        
        catch (Exception ex)
        {
            // Capture elapsed latency up to the point of failure along with the error description
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            return new OperationMeasurement(false, elapsedMs, ex.Message);
        }
    }
}