namespace ArasPerformanceTool.Engine.Models;

// Summary of a cleanup operation
public sealed record CleanupResult(
    int DeletedCount,
    int AlreadyMissingCount,
    IReadOnlyList<string> FailedIds);