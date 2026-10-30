
using System.Collections.Concurrent;
using System.Xml.Linq;
using ArasPerformanceTool.Api.DTOs;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using Microsoft.AspNetCore.Mvc;

namespace ArasPerformanceTool.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BenchmarkController : ControllerBase
{
    // Store completed and failed benchmark results in memory
    private static readonly ConcurrentDictionary<Guid, BenchmarkStatusResponse> Jobs = new();

    
    private readonly IArasClient _arasClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkController> _logger;

    public BenchmarkController(
        IArasClient arasClient,
        IConfiguration configuration,
        ILogger<BenchmarkController> logger)
    {
        _arasClient = arasClient;
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
        // [ApiController] automatically validates DataAnnotations
        // before this action executes.

        // The current API supports Create for these verified ItemTypes
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
            // Use the existing Aras configuration structure
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

        

            
            var token = await _arasClient.GetAccessTokenAsync(
                username,
                password);

            // Create the benchmark runner
            var runner = new BenchmarkRunner();

            // Reuse the injected Aras client
            var dataManager = new TestDataManager(_arasClient);
            var createWorkload = new CreateWorkload(_arasClient);

            // Configure benchmark execution
            var config = new BenchmarkConfiguration(
                request.ItemType,
                request.Iterations,
                request.Concurrency);


            try
            {
                // Execute the benchmark and wait for its result
                var report = await runner.RunAsync(
                    workloadName,
                    config,
                    async index =>
                    {
                        // Generate a unique Item Number
                        var itemNumber =
                            $"API-BENCH-{Guid.NewGuid():N}"[..24];

                        var properties = new Dictionary<string, string>
                        {
                            ["item_number"] = itemNumber,
                            ["name"] = $"API Benchmark {request.ItemType} {index + 1}"
                        };

                        // Execute a real AML Create request
                        var responseXml = await createWorkload.ExecuteAsync(
                            serverUrl,
                            token,
                            request.ItemType,
                            properties);

                        // Extract the newly created Item ID
                        var id = ExtractItemId(
                            responseXml,
                            request.ItemType);

                        if (string.IsNullOrWhiteSpace(id))
                        {
                            throw new InvalidOperationException(
                                $"Unable to extract created Item ID at iteration {index}.");
                        }

                        // Track created test data for cleanup
                        dataManager.Manifest.Track(request.ItemType, id);
                    });

                statistics = report.Statistics;

                // A benchmark with failed operations is not fully successful
                if (statistics.FailureCount > 0)
                {
                    status = BenchmarkStatus.Failed;
                    errorMessage =
                        $"{statistics.FailureCount} benchmark operations failed.";
                }
            }
            finally
            {
                // Always attempt to clean up tracked test records
                var cleanupResult = await dataManager.CleanupAsync(
                    serverUrl,
                    token,
                    request.ItemType);

                // Do not silently ignore cleanup failures
                if (cleanupResult.FailedIds.Count > 0 ||
                    dataManager.Manifest.TotalCount > 0)
                {
                    throw new InvalidOperationException(
                        "Benchmark cleanup did not complete successfully.");
                }
            }
        }
        catch (Exception ex)
        {
            // Log technical details only on the server
            _logger.LogError(
                ex,
                "Benchmark failed: JobId={JobId}",
                jobId);

            status = BenchmarkStatus.Failed;
            errorMessage = "Benchmark execution or cleanup failed. Check server logs.";
        }

        // Build the final response after execution and cleanup
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

        // Save the result for future GET requests
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

    // Extract the created Item ID from the Aras AML response
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
