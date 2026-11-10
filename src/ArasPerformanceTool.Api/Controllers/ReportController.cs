using System.Text;
using ArasPerformanceTool.Engine.Storage;
using Microsoft.AspNetCore.Mvc;
using ArasPerformanceTool.Api.Reporting;

namespace ArasPerformanceTool.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportController : ControllerBase
{
    private readonly FileResultStore _fileResultStore;
    private readonly ILogger<ReportController> _logger;

    private readonly PdfReportGenerator _pdfReportGenerator;    

   public ReportController(
        FileResultStore fileResultStore,
        ILogger<ReportController> logger,
        PdfReportGenerator pdfReportGenerator)
    {
        _fileResultStore = fileResultStore;
        _logger = logger;
        _pdfReportGenerator = pdfReportGenerator;
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


    [HttpGet("{id:guid}/summary-pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSummaryPdf(Guid id)
    {
        var json = await _fileResultStore.GetJsonAsync(id);

        if (json == null)
        {
            return NotFound(new
            {
                Message = $"Report for job '{id}' was not found."
            });
        }

        var pdf = _pdfReportGenerator.GenerateSummary(json);

        return File(
            pdf,
            "application/pdf",
            $"benchmark-summary-{id:D}.pdf");
    }

    [HttpGet("{id:guid}/measurements-pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadMeasurementsPdf(Guid id)
    {
        var csv = await _fileResultStore.GetCsvAsync(id);

        if (csv == null)
        {
            return NotFound(new
            {
                Message = $"Report for job '{id}' was not found."
            });
        }

        var pdf = _pdfReportGenerator.GenerateMeasurements(csv);

        return File(
            pdf,
            "application/pdf",
            $"benchmark-measurements-{id:D}.pdf");
    }
}