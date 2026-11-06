using ArasPerformanceTool.Engine.Models;

/// <summary>
/// Represents the result and execution latency of a single benchmark operation.
/// </summary>
/// <param name="IsSuccess">Indicates whether the measured action completed without unhandled exceptions.</param>
/// <param name="ElapsedMilliseconds">High-precision duration of the operation in milliseconds.</param>
/// <param name="ErrorMessage">Error message captured if the operation threw an unhandled exception.</param>
public sealed record OperationMeasurement(
    bool IsSuccess,
    double ElapsedMilliseconds,
    string? ErrorMessage = null);