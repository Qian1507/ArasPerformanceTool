using System.ComponentModel.DataAnnotations;

namespace ArasPerformanceTool.Api.DTOs;

public class StartBenchmarkRequest
{
    /// <summary>
    /// Raw AML XML payload to benchmark.
    /// </summary>
    [StringLength(100000)]
    public string? Aml { get; set; }

    /// <summary>
    /// Target ItemType (e.g., Part, Document). Can be parsed from AML if omitted.
    /// </summary>
    public string? ItemType { get; set; }

    /// <summary>
    /// Target CRUD action (e.g., add, get, edit, delete).
    /// </summary>
    public string? Action { get; set; }

    [Range(1, 1000)]
    public int Iterations { get; set; } = 10;

    [Range(1, 50)]
    public int Concurrency { get; set; } = 1;

    public string? WorkloadName { get; set; }
    
    
}