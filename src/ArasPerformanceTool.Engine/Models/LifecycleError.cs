using ArasPerformanceTool.Engine.Models;

// Represents a lifecycle error that occurred during a benchmark iteration.
public sealed record LifecycleError(
    int Iteration,
    string Phase,
    string ErrorMessage,
    DateTime TimestampUtc);
