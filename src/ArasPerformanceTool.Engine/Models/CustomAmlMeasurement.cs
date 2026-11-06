using ArasPerformanceTool.Engine.Models;

// Individual operation measurement.
public sealed record CustomAmlMeasurement(
    int Iteration,
    string Operation,
    double ElapsedMs,
    bool IsSuccess,
    string? ErrorMessage = null);