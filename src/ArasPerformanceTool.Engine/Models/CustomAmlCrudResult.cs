using ArasPerformanceTool.Engine.Models;

// Result of the complete benchmark.
public sealed class CustomAmlCrudResult
{
    public List<CustomAmlMeasurement> Measurements { get; } = new();

    public object? CleanupSummary { get; set; }

    public string? CleanupError { get; set; }

    public int SuccessCount => Measurements.Count(x => x.IsSuccess);

    public int FailureCount => Measurements.Count(x => !x.IsSuccess);
    public List<LifecycleError> LifecycleErrors { get; } = new();

   public double GetAverageMs(string operation)
    {
        var measurements = Measurements
            .Where(x => x.Operation == operation)
            .ToList();

        return measurements.Count == 0
            ? 0
            : Math.Round(measurements.Average(x => x.ElapsedMs), 2);
    }
}