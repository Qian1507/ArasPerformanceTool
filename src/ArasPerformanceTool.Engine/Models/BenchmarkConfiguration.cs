using ArasPerformanceTool.Engine.Models;
public sealed record BenchmarkConfiguration(
    string ItemType,
    int Iterations,
    int Concurrency = 1);