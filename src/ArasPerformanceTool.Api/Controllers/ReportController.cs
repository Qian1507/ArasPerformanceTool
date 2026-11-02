using System.Text;
using ArasPerformanceTool.Engine.Storage;
using Microsoft.AspNetCore.Mvc;

namespace ArasPerformanceTool.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportController : ControllerBase
{
    private readonly FileResultStore _fileResultStore;
    private readonly ILogger<ReportController> _logger;

    public ReportController(
        FileResultStore fileResultStore,
        ILogger<ReportController> logger)
    {
        _fileResultStore = fileResultStore;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the benchmark report in JSON format.
    /// </summary>
    /// <param name="id">The benchmark job ID.</param>
    [HttpGet("{id:guid}/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadJsonReport(Guid id)
    {
        var content = await _fileResultStore.GetJsonAsync(id);
        if (content == null)
        {
            return NotFound(new { Message = $"Report for job '{id}' was not found." });
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        return File(bytes, "application/json", $"benchmark-report-{id:D}.json");
    }

    /// <summary>
    /// Downloads the benchmark report measurements in CSV format.
    /// </summary>
    /// <param name="id">The benchmark job ID.</param>
    [HttpGet("{id:guid}/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadCsvReport(Guid id)
    {
        var content = await _fileResultStore.GetCsvAsync(id);
        if (content == null)
        {
            return NotFound(new { Message = $"Report for job '{id}' was not found." });
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        return File(bytes, "text/csv", $"benchmark-report-{id:D}.csv");
    }
}