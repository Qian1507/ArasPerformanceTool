namespace ArasPerformanceTool.Api.DTOs;

public class BenchmarkStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}