using ArasPerformanceTool.Engine.Models;
public sealed record BenchmarkReport(
    string WorkloadName,
    BenchmarkConfiguration Configuration,
    BenchmarkStatistics Statistics,
    IReadOnlyList<OperationMeasurement> RawMeasurements)
{
    public string? RunId { get; init; }
    public string? Mode { get; init; }
    public string? Operation { get; init; }
}