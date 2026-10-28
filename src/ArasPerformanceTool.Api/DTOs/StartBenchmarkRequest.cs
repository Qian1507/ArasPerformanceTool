namespace ArasPerformanceTool.Api.DTOs;

public class StartBenchmarkRequest
{
    public string ItemType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public int Iterations { get; set; } = 100;
    public int Concurrency { get; set; } = 1;
    public bool AutoCleanup { get; set; } = true;
}