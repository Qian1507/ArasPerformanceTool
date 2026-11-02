
using System.Collections.Concurrent;
using System.Xml.Linq;
using ArasPerformanceTool.Api.DTOs;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using ArasPerformanceTool.Engine.Storage;
using Microsoft.AspNetCore.Mvc;

namespace ArasPerformanceTool.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BenchmarkController : ControllerBase
{
    // Store benchmark status responses in memory
    private static readonly ConcurrentDictionary<Guid, BenchmarkStatusResponse> Jobs = new();

    private readonly IArasClient _arasClient;
    private readonly CreateWorkload _createWorkload;
    private readonly FileResultStore _fileResultStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkController> _logger;

    public BenchmarkController(
        IArasClient arasClient,
        CreateWorkload createWorkload,
        FileResultStore fileResultStore,
        IConfiguration configuration,
        ILogger<BenchmarkController> logger)
    {
        _arasClient = arasClient;
        _createWorkload = createWorkload;
        _fileResultStore = fileResultStore;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Runs a Create benchmark and returns its final result.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BenchmarkStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(BenchmarkStatusResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> StartBenchmark(
        [FromBody] StartBenchmarkRequest request)
    {
        // Currently only Part and Document Create operations are supported
        if (request.ItemType is not ("Part" or "Document"))
        {
            return BadRequest(new
            {
                Message = "M5 currently supports Part and Document only."
            });
        }

        var jobId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;

        var workloadName = string.IsNullOrWhiteSpace(request.WorkloadName)
            ? $"Create{request.ItemType}Benchmark"
            : request.WorkloadName;

        BenchmarkStatistics? statistics = null;
        var status = BenchmarkStatus.Completed;
        string? errorMessage = null;

        try
        {
            // Read the existing Aras configuration
            var serverUrl = _configuration["Aras:ServerUrl"]
                ?? throw new InvalidOperationException(
                    "Aras Server URL is missing.");

            var username = _configuration["ARAS_USERNAME"];
            var password = _configuration["ARAS_PASSWORD"];

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Aras credentials are not configured.");
            }

            // Authenticate with Aras
            var token = await _arasClient.GetAccessTokenAsync(
                username,
                password);

            // Prepare benchmark execution
            var runner = new BenchmarkRunner();
            var dataManager = new TestDataManager(_arasClient);

            var config = new BenchmarkConfiguration(
                request.ItemType,
                request.Iterations,
                request.Concurrency);

            BenchmarkReport? report = null;

            try
            {
                // Run AML Create operations
                report = await runner.RunAsync(
                    workloadName,
                    config,
                    async index =>
                    {
                        var itemNumber =
                            $"API-BENCH-{Guid.NewGuid():N}"[..24];

                        var properties = new Dictionary<string, string>
                        {
                            ["item_number"] = itemNumber,
                            ["name"] = $"API Benchmark {request.ItemType} {index + 1}"
                        };

                        var responseXml = await _createWorkload.ExecuteAsync(
                            serverUrl,
                            token,
                            request.ItemType,
                            properties);

                        var id = ExtractItemId(
                            responseXml,
                            request.ItemType);

                        if (string.IsNullOrWhiteSpace(id))
                        {
                            throw new InvalidOperationException(
                                $"Unable to extract created Item ID at iteration {index}.");
                        }

                        // Track test data for cleanup
                        dataManager.Manifest.Track(request.ItemType, id);
                    });

                statistics = report.Statistics;

                // Preserve the failure state if any operations failed
                if (statistics.FailureCount > 0)
                {
                    status = BenchmarkStatus.Failed;
                    errorMessage =
                        $"{statistics.FailureCount} benchmark operations failed.";
                }
            }
            finally
            {
                // Always attempt cleanup after benchmark execution
                var cleanupResult = await dataManager.CleanupAsync(
                    serverUrl,
                    token,
                    request.ItemType);

                if (cleanupResult.FailedIds.Count > 0 ||
                    dataManager.Manifest.TotalCount > 0)
                {
                    throw new InvalidOperationException(
                        "Benchmark cleanup did not complete successfully.");
                }
            }

            // Persist the complete benchmark report after successful cleanup
            // Also preserve measurements from partially failed benchmarks
            if (report is not null)
            {
                await _fileResultStore.SaveAsync(jobId, report);
            }
        }
        catch (Exception ex)
        {
            // Keep technical error details in server logs
            _logger.LogError(
                ex,
                "Benchmark failed: JobId={JobId}",
                jobId);

            status = BenchmarkStatus.Failed;
            errorMessage =
                "Benchmark execution or cleanup failed. Check server logs.";
        }

        // Return the final benchmark status
        var response = new BenchmarkStatusResponse(
            JobId: jobId,
            Status: status,
            ItemType: request.ItemType,
            Iterations: request.Iterations,
            Concurrency: request.Concurrency,
            CreatedAtUtc: createdAtUtc,
            CompletedAtUtc: DateTime.UtcNow,
            Statistics: statistics,
            ErrorMessage: errorMessage);

        Jobs[jobId] = response;

        _logger.LogInformation(
            "Benchmark finished: JobId={JobId}, Status={Status}",
            jobId,
            status);

        if (status == BenchmarkStatus.Failed)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a benchmark result by its Job ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BenchmarkStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBenchmarkStatus(Guid id)
    {
        if (!Jobs.TryGetValue(id, out var response))
        {
            return NotFound(new
            {
                Message = $"Benchmark job '{id}' was not found."
            });
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves all benchmark results stored in memory.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BenchmarkStatusResponse>), StatusCodes.Status200OK)]
    public IActionResult GetAllBenchmarks()
    {
        var results = Jobs.Values
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();

        return Ok(results);
    }

    /// <summary>
    /// Extracts the created Item ID from an AML response.
    /// </summary>
    private static string? ExtractItemId(
        string xmlPayload,
        string itemType)
    {
        var xml = XDocument.Parse(xmlPayload);

        var itemElement = xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                string.Equals(
                    (string?)e.Attribute("type"),
                    itemType,
                    StringComparison.OrdinalIgnoreCase));

        return (string?)itemElement?.Attribute("id");
    }
}
