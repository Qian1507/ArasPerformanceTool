using ArasPerformanceTool.Engine.Models;
public sealed record BenchmarkReport(
    string WorkloadName,
    BenchmarkConfiguration Configuration,
    BenchmarkStatistics Statistics,
    IReadOnlyList<OperationMeasurement> RawMeasurements);
