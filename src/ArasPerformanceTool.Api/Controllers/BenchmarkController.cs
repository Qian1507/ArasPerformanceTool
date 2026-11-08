
using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using ArasPerformanceTool.Api.DTOs;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using ArasPerformanceTool.Engine.Storage;
using Microsoft.AspNetCore.Mvc;
using ArasPerformanceTool.Engine.Services;
using ArasPerformanceTool.Engine.Models;
using Microsoft.AspNetCore.Hosting;

namespace ArasPerformanceTool.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BenchmarkController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, BenchmarkStatusResponse>
        Jobs = new();

    private readonly IArasClient _arasClient;
    private readonly CustomAmlBenchmarkService _customAmlBenchmarkService;
    private readonly FileResultStore _fileResultStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkController> _logger;

    private readonly IWebHostEnvironment _environment;

    public BenchmarkController(
        IArasClient arasClient,
        FileResultStore fileResultStore,
        IConfiguration configuration,
        ILogger<BenchmarkController> logger,
        CustomAmlBenchmarkService customAmlBenchmarkService,
        IWebHostEnvironment environment)
    {
        _arasClient = arasClient;
        _fileResultStore = fileResultStore;
        _configuration = configuration;
        _logger = logger;
        _customAmlBenchmarkService = customAmlBenchmarkService;
        _environment = environment;
    }
    /// <summary>
    /// Runs a standard CRUD or custom read-only AML benchmark.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(
        typeof(BenchmarkStatusResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(BenchmarkStatusResponse),
        StatusCodes.Status500InternalServerError)]
   
public async Task<IActionResult> StartBenchmark(
    [FromBody] StartBenchmarkRequest request)
{
    var isRawAml = !string.IsNullOrWhiteSpace(request.Aml);

    string itemType;
    string action;

    // =====================================================
    // 1. Validate Request
    // =====================================================

    try
    {
        if (isRawAml)
        {
            // =============================================
            // Custom AML: Single AML Input
            // =============================================

            var rawAml = request.Aml!;

            // Parse the top-level ItemType and action.
            // Execution safety is validated by
            // CustomAmlBenchmarkService.
            var metadata = AmlParser.ParseMetadata(rawAml);

            itemType = metadata.ItemType;
            action = metadata.Action;

            if (string.IsNullOrWhiteSpace(itemType))
            {
                return BadRequest(new
                {
                    Message = "Custom AML must specify an ItemType."
                });
            }

            var supportedAmlActions = new[]
            {
                "add",
                "get",
                "edit",
                "delete"
            };

            if (!supportedAmlActions.Contains(
                action,
                StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    Message = "Custom AML supports add, get, edit and delete."
                });
            }

            // Validate ItemType only if explicitly supplied.
            if (!string.IsNullOrWhiteSpace(request.ItemType) &&
                !string.Equals(
                    request.ItemType,
                    itemType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    Message = "ItemType does not match the AML payload."
                });
            }

            // Accept either AML action names or CRUD labels.
            var normalizedAction = action.ToLowerInvariant() switch
            {
                "add" => "Create",
                "get" => "Read",
                "edit" => "Update",
                "delete" => "Delete",
                _ => action
            };

            if (!string.IsNullOrWhiteSpace(request.Action) &&
                !string.Equals(
                    request.Action,
                    action,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    request.Action,
                    normalizedAction,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    Message = "Action does not match the AML payload."
                });
            }

            // Do not execute AML here.
            // CustomAmlBenchmarkService handles validation,
            // macros, execution, tracking and cleanup.
        }
        else
        {
            // =============================================
            // Standard CRUD: Existing Behavior
            // =============================================

            itemType = request.ItemType ?? "Part";
            action = request.Action ?? "Create";

            if (itemType is not ("Part" or "Document"))
            {
                return BadRequest(new
                {
                    Message =
                        "Standard mode currently supports Part and Document only."
                });
            }

            var supportedActions = new[]
            {
                "Create",
                "Read",
                "Update",
                "Delete"
            };

            if (!supportedActions.Contains(
                action,
                StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    Message = "Unsupported CRUD operation."
                });
            }
        }
        }
        catch (Exception ex) when (
            ex is ArgumentException or XmlException)
        {
            return BadRequest(new
            {
                Message = ex.Message
            });
        }

   

        // =====================================================
        // 2. Initialize Benchmark
        // =====================================================

        var jobId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;

        var workloadName = string.IsNullOrWhiteSpace(
            request.WorkloadName)
            ? $"{(isRawAml ? "RawAml" : "Standard")}_{action}_{itemType}"
            : request.WorkloadName;

        BenchmarkReport? report = null;
        BenchmarkStatistics? statistics = null;
        CrudVerification? verification = null;
        AmlQueryResult? queryResult = null;

        var status = BenchmarkStatus.Completed;
        string? errorMessage = null;

        try
        {
            // =================================================
            // 3. Connect to Aras
            // =================================================

            var serverUrl = _configuration["Aras:ServerUrl"]
                ?? throw new InvalidOperationException(
                    "Aras Server URL is missing.");
            
            // Identify the Aras environment used by this benchmark.
            // Both server and database matter.
            var database = _configuration["Aras:Database"]
                ?? throw new InvalidOperationException(
                    "Aras Database is missing.");

            var environmentId =
                $"{serverUrl.TrimEnd('/')}|{database}";

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

            // =================================================
            // Manifest Storage Configuration
            // =================================================

            var dataDirectory = _configuration["Storage:DataDirectory"]
                ?? throw new InvalidOperationException(
                    "Storage data directory is not configured.");

            var dataRoot = Path.IsPathRooted(dataDirectory)
                ? Path.GetFullPath(dataDirectory)
                : Path.GetFullPath(Path.Combine(
                    _environment.ContentRootPath,
                    dataDirectory));

            var manifestDirectory = Path.Combine(
                dataRoot,
                "manifests");

            Directory.CreateDirectory(manifestDirectory);


            // =================================================
            // Benchmark Configuration
            // =================================================
            
            var runner = new BenchmarkRunner();          

            var config = new BenchmarkConfiguration(
                itemType,
                request.Iterations,
                request.Concurrency);

            // =================================================
            // 4. Custom AML Benchmark 
            // =================================================

            if (isRawAml)
            {
                
                var customResult =
                    await _customAmlBenchmarkService.RunAsync(
                        request.Aml!,
                        workloadName,
                        config,
                        serverUrl,
                        token,
                        environmentId,
                        jobId.ToString("N"),
                        manifestDirectory,
                        HttpContext.RequestAborted);

                // Reuse the existing benchmark report and statistics.
                report = customResult.Report;

                // Preserve the response for AML Query Result Details.
                if (!string.IsNullOrWhiteSpace(
                    customResult.SampleResponseXml))
                {
                    queryResult = new AmlQueryResult(
                        ResponseXml: customResult.SampleResponseXml);
                }

                // Cleanup failure must not be treated as success.
                if (!string.IsNullOrWhiteSpace(
                    customResult.CleanupError))
                {
                    status = BenchmarkStatus.Failed;
                    errorMessage = customResult.CleanupError;
                }
            }
            else
            {
                // =============================================
                // 5. Standard CRUD Benchmark
                // =============================================

               
                // Read the data directory from configuration.

                var manifestPath = Path.Combine(
                    manifestDirectory,
                    $"{jobId:N}.json");

                var manifest = new TestDataManifest(
                    environmentId: environmentId,
                    filePath: manifestPath,
                    runId: jobId.ToString("N"));

                var dataManager = new TestDataManager(
                    _arasClient,
                    manifest);


                var createWorkload =
                    new CreateWorkload(_arasClient);

                var readWorkload =
                    new ReadWorkload(_arasClient);

                var updateWorkload =
                    new UpdateWorkload(_arasClient);

                var deleteWorkload =
                    new DeleteWorkload(_arasClient);

                try
                {
                    // =========================================
                    // CREATE
                    // =========================================

                    if (string.Equals(
                        action,
                        "Create",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        var createdIds =
                            new string?[request.Iterations];

                        var createdNames =
                            new string?[request.Iterations];

                        report = await runner.RunAsync(
                            workloadName,
                            config,
                            async index =>
                            {
                                var itemNumber =
                                    $"API-BENCH-{Guid.NewGuid():N}"[..24];

                                var name =
                                    $"API Benchmark {itemType} {index + 1}";

                                var properties =
                                    new Dictionary<string, string>
                                    {
                                        ["item_number"] = itemNumber,
                                        ["name"] = name
                                    };

                                var responseXml =
                                    await createWorkload.ExecuteAsync(
                                        serverUrl,
                                        token,
                                        itemType,
                                        properties);

                                var id = ExtractItemId(
                                    responseXml,
                                    itemType);

                                if (string.IsNullOrWhiteSpace(id))
                                {
                                    throw new InvalidOperationException(
                                        $"Unable to extract created Item ID at iteration {index}.");
                                }

                                // Track immediately after getting the ID.
                                dataManager.Manifest.Track(
                                    itemType,
                                    id);

                                createdIds[index] = id;
                                createdNames[index] = name;
                            });

                        // Verify one created sample outside timing.
                        var sampleId = createdIds[0];

                        var sampleVerified = false;
                        string? retrievedName = null;

                        if (!string.IsNullOrWhiteSpace(sampleId))
                        {
                            var readXml =
                                await readWorkload.ExecuteAsync(
                                    serverUrl,
                                    token,
                                    itemType,
                                    id: sampleId,
                                    select: "id,item_number,name");

                            var document = XDocument.Parse(readXml);

                            var item = FindItem(
                                document,
                                itemType,
                                sampleId);

                            if (item is not null)
                            {
                                retrievedName = GetProperty(
                                    item,
                                    "name");

                                sampleVerified = string.Equals(
                                    retrievedName,
                                    createdNames[0],
                                    StringComparison.Ordinal);
                            }
                        }

                        verification = new CrudVerification(
                            Operation: "Create",
                            TargetId: sampleId,
                            PropertyName: "name",
                            AfterValue: retrievedName,
                            IsVerified: sampleVerified,
                            VerificationNote: sampleVerified
                                ? "Sample item was created and verified by a separate Read."
                                : "The created sample could not be verified.",
                            CleanedUp: null);
                    }

                    // =========================================
                    // READ
                    // =========================================

                    else if (string.Equals(
                        action,
                        "Read",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // Create one temporary item before timing.
                        var preparedIds =
                            await dataManager.PrepareAsync(
                                serverUrl,
                                token,
                                itemType,
                                1);

                        var readId = preparedIds[0];

                        // Each iteration keeps its own response.
                        var responses =
                            new string?[request.Iterations];

                        report = await runner.RunAsync(
                            workloadName,
                            config,
                            async index =>
                            {
                                responses[index] =
                                    await readWorkload.ExecuteAsync(
                                        serverUrl,
                                        token,
                                        itemType,
                                        id: readId,
                                        select: "id,item_number,name");
                            });

                        // Parse a representative response outside timing.
                        string? retrievedName = null;
                        string? retrievedItemNumber = null;
                        var sampleVerified = false;

                        var sampleXml = responses[0];

                        if (!string.IsNullOrWhiteSpace(sampleXml))
                        {
                            var document = XDocument.Parse(sampleXml);

                            var item = FindItem(
                                document,
                                itemType,
                                readId);

                            if (item is not null)
                            {
                                retrievedName = GetProperty(
                                    item,
                                    "name");

                                retrievedItemNumber = GetProperty(
                                    item,
                                    "item_number");

                                sampleVerified =
                                    !string.IsNullOrWhiteSpace(
                                        retrievedName) &&
                                    !string.IsNullOrWhiteSpace(
                                        retrievedItemNumber);
                            }
                        }

                        verification = new CrudVerification(
                            Operation: "Read",
                            TargetId: readId,
                            PropertyName: "name",
                            AfterValue: retrievedName,
                            IsVerified: sampleVerified,
                            VerificationNote: sampleVerified
                                ? $"Sample Item Number: {retrievedItemNumber}. " +
                                  "The returned Item ID matches the requested ID."
                                : "The sample Read result could not be verified.",
                            CleanedUp: null);
                    }

                    // =========================================
                    // UPDATE
                    // =========================================

                    else if (string.Equals(
                        action,
                        "Update",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // Prepare an independent item per iteration.
                        var preparedIds =
                            await dataManager.PrepareAsync(
                                serverUrl,
                                token,
                                itemType,
                                request.Iterations);

                        var sampleOriginalId = preparedIds[0];

                        // Capture the original sample before timing.
                        var beforeXml =
                            await readWorkload.ExecuteAsync(
                                serverUrl,
                                token,
                                itemType,
                                id: sampleOriginalId,
                                select: "id,name,generation");

                        var beforeDocument =
                            XDocument.Parse(beforeXml);

                        var beforeItem = FindItem(
                            beforeDocument,
                            itemType,
                            sampleOriginalId);

                        if (beforeItem is null)
                        {
                            throw new InvalidOperationException(
                                "Unable to retrieve the original Update sample.");
                        }

                        var beforeName = GetProperty(
                            beforeItem,
                            "name");

                        var beforeGeneration = GetProperty(
                            beforeItem,
                            "generation");

                        if (beforeName is null)
                        {
                            throw new InvalidOperationException(
                                "Original sample is missing the name property.");
                        }

                        // Keep each iteration result in its own slot.
                        var returnedIds =
                            new string?[request.Iterations];

                        report = await runner.RunAsync(
                            workloadName,
                            config,
                            async index =>
                            {
                                var targetId = preparedIds[index];

                                var properties =
                                    new Dictionary<string, string>
                                    {
                                        ["name"] =
                                            $"Updated Benchmark {index + 1}"
                                    };

                                var responseXml =
                                    await updateWorkload.ExecuteAsync(
                                        serverUrl,
                                        token,
                                        itemType,
                                        targetId,
                                        properties);

                                var returnedId = ExtractItemId(
                                    responseXml,
                                    itemType);

                                if (string.IsNullOrWhiteSpace(returnedId))
                                {
                                    throw new InvalidOperationException(
                                        $"Update did not return an Item ID at iteration {index}.");
                                }

                                returnedIds[index] = returnedId;

                                // Track any new generation ID.
                                if (!string.Equals(
                                    targetId,
                                    returnedId,
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    dataManager.Manifest.Track(
                                        itemType,
                                        returnedId);
                                }
                            });

                        // Verify the first updated sample outside timing.
                        var sampleUpdatedId = returnedIds[0];

                        string? afterName = null;
                        string? afterGeneration = null;
                        var sampleVerified = false;

                        if (!string.IsNullOrWhiteSpace(sampleUpdatedId))
                        {
                            var afterXml =
                                await readWorkload.ExecuteAsync(
                                    serverUrl,
                                    token,
                                    itemType,
                                    id: sampleUpdatedId,
                                    select: "id,name,generation");

                            var afterDocument =
                                XDocument.Parse(afterXml);

                            var afterItem = FindItem(
                                afterDocument,
                                itemType,
                                sampleUpdatedId);

                            if (afterItem is not null)
                            {
                                afterName = GetProperty(
                                    afterItem,
                                    "name");

                                afterGeneration = GetProperty(
                                    afterItem,
                                    "generation");

                                sampleVerified = string.Equals(
                                    afterName,
                                    "Updated Benchmark 1",
                                    StringComparison.Ordinal);
                            }
                        }

                        var idChanged =
                            !string.IsNullOrWhiteSpace(sampleUpdatedId) &&
                            !string.Equals(
                                sampleOriginalId,
                                sampleUpdatedId,
                                StringComparison.OrdinalIgnoreCase);

                        verification = new CrudVerification(
                            Operation: "Update",
                            TargetId: sampleOriginalId,
                            UpdatedId: sampleUpdatedId,
                            PropertyName: "name",
                            BeforeGeneration: beforeGeneration,
                            AfterGeneration: afterGeneration,
                            BeforeValue: beforeName,
                            AfterValue: afterName,
                            IsVerified: sampleVerified,
                            VerificationNote: sampleVerified
                                ? idChanged
                                    ? "Sample update verified. Aras returned a new Item ID."
                                    : "Sample update verified. Item ID remained unchanged."
                                : "The updated sample could not be verified.",
                            CleanedUp: null);
                    }

                    // =========================================
                    // DELETE
                    // =========================================

                    else if (string.Equals(
                        action,
                        "Delete",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // Prepare independent items before timing.
                        var preparedIds =
                            await dataManager.PrepareAsync(
                                serverUrl,
                                token,
                                itemType,
                                request.Iterations);

                        report = await runner.RunAsync(
                            workloadName,
                            config,
                            async index =>
                            {
                                var targetId = preparedIds[index];

                                var responseXml =
                                    await deleteWorkload.ExecuteAsync(
                                        serverUrl,
                                        token,
                                        itemType,
                                        targetId);

                                var document = XDocument.Parse(responseXml);

                                // A SOAP fault is not considered a
                                // successful deletion.
                                var faultCode = GetFaultCode(document);

                                if (faultCode is not null)
                                {
                                    throw new InvalidOperationException(
                                        $"Delete failed for Item {targetId}. " +
                                        $"Aras fault code: {faultCode}");
                                }
                            });

                        // Verify every deleted target outside timing.
                        var verifiedCount = 0;

                        foreach (var targetId in preparedIds)
                        {
                            var readXml =
                                await readWorkload.ExecuteAsync(
                                    serverUrl,
                                    token,
                                    itemType,
                                    id: targetId,
                                    select: "id");

                            var document = XDocument.Parse(readXml);

                            if (IsItemAbsent(
                                document,
                                itemType,
                                targetId))
                            {
                                verifiedCount++;
                            }
                        }

                        var allAbsent =
                            verifiedCount == preparedIds.Count;

                        var allVerified =
                            allAbsent &&
                            report.Statistics.FailureCount == 0;

                        verification = new CrudVerification(
                            Operation: "Delete",
                            TargetId: preparedIds[0],
                            IsVerified: allVerified,
                            VerifiedNotFound: allAbsent,
                            VerificationNote:
                                $"{verifiedCount}/{preparedIds.Count} test items " +
                                "were confirmed absent from standard ID queries. " +
                                "Historical versions may still exist.",
                            CleanedUp: null);
                    }
                }
                finally
                {
                    // =========================================
                    // 6. Cleanup
                    // =========================================

                    // Cleanup is excluded from benchmark timing.
                    // Failed and already-deleted records remain
                    // subject to the manager's verification rules.
                    var cleanupResult =
                        await dataManager.CleanupAsync(
                            serverUrl,
                            token,
                            itemType,
                            expectedEnvironmentId: environmentId);

                    var cleanupSucceeded =
                        cleanupResult.FailedIds.Count == 0 &&
                        dataManager.Manifest.TotalCount == 0;

                    if (verification is not null)
                    {
                        verification = verification with
                        {
                            CleanedUp = cleanupSucceeded
                        };
                    }

                    if (!cleanupSucceeded)
                    {
                        throw new InvalidOperationException(
                            "Benchmark cleanup did not complete successfully.");
                    }
                }
            }

            // =================================================
            // 7. Evaluate and Save Benchmark Report
            // =================================================

            if (report is null)
            {
                throw new InvalidOperationException(
                    "Benchmark report was not generated.");
            }

            statistics = report.Statistics;

            
            if (statistics.FailureCount > 0)
            {
                status = BenchmarkStatus.Failed;

                // Get the first meaningful error from failed operations.
                var firstError = report.RawMeasurements
                    .Where(m => !m.IsSuccess)
                    .Select(m => m.ErrorMessage)
                    .FirstOrDefault(message =>
                        !string.IsNullOrWhiteSpace(message));

                // Build the benchmark error message.
                var benchmarkError =
                    $"{statistics.FailureCount} benchmark operations failed.";

                if (!string.IsNullOrWhiteSpace(firstError))
                {
                    benchmarkError += $" Reason: {firstError}";
                }

                // Preserve any existing cleanup error.
                errorMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? benchmarkError
                    : $"{errorMessage} {benchmarkError}";
            }


            if (verification?.IsVerified == false)
            {
                status = BenchmarkStatus.Failed;
                errorMessage =
                    "CRUD verification failed. Check Operation Verification Summary.";
            }

            // Preserve the existing CSV/JSON report format.
            await _fileResultStore.SaveAsync(
                jobId,
                report);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Benchmark failed: JobId={JobId}",
                    jobId);

                status = BenchmarkStatus.Failed;

                errorMessage =
                    "Benchmark execution or cleanup failed. Check server logs.";
            }

            // =====================================================
            // 8. Build API Response
            // =====================================================

        var response = new BenchmarkStatusResponse(
            JobId: jobId,
            Status: status,
            ItemType: itemType,
            Iterations: request.Iterations,
            Concurrency: request.Concurrency,
            CreatedAtUtc: createdAtUtc,
            CompletedAtUtc: DateTime.UtcNow,
            Statistics: statistics,
            ErrorMessage: errorMessage,
            Verification: verification,
            QueryResult: queryResult);

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
    /// Retrieves a benchmark result by Job ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(BenchmarkStatusResponse),
        StatusCodes.Status200OK)]
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
    /// Retrieves benchmark runs with unresolved test data.
    /// Read-only; does not perform cleanup.
    /// </summary>
    
    [HttpGet("pending-cleanup")]
    public ActionResult GetPendingCleanupRuns()
    {
        var serverUrl = _configuration["Aras:ServerUrl"];
        var database = _configuration["Aras:Database"];

        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(database))
        {
            return StatusCode(
                500,
                new { error = "Aras environment is not configured." });
        }

        var environmentId =
            $"{serverUrl.TrimEnd('/')}|{database}";

        var dataDirectory = _configuration["Storage:DataDirectory"];

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return StatusCode(
                500,
                new { error = "Storage data directory is not configured." });
        }

        var dataRoot = Path.IsPathRooted(dataDirectory)
            ? Path.GetFullPath(dataDirectory)
            : Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                dataDirectory));

        var manifestDirectory = Path.Combine(
            dataRoot,
            "manifests");

        var store = new TestDataManifestStore(
            manifestDirectory);

        var result = store.Scan(environmentId);

        // Do not expose local ManifestPath to API clients.
        var pendingRuns = result.PendingRuns.Select(run => new
        {
            run.RunId,
            run.EnvironmentId,
            run.CreatedAtUtc,
            run.RemainingCount
        }).ToList();

        var errors = result.Errors.Select(error => new
        {
            FileName = Path.GetFileName(error.ManifestPath),
            Message = "Manifest could not be loaded."
        }).ToList();

        return Ok(new
        {
            PendingRuns = pendingRuns,
            Errors = errors
        });
    }

    
    /// <summary>
    /// Retrieves unresolved test items for a specific benchmark run.
    /// Read-only; does not execute AML or delete items.
    /// </summary>
    [HttpGet("pending-cleanup/{runId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetPendingCleanupRun(Guid runId)
    {
        var serverUrl = _configuration["Aras:ServerUrl"];
        var database = _configuration["Aras:Database"];

        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(database))
        {
            return StatusCode(500, new
            {
                Message = "Aras environment is not configured."
            });
        }

        var environmentId =
            $"{serverUrl.TrimEnd('/')}|{database}";

        var dataDirectory =
            _configuration["Storage:DataDirectory"];

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return StatusCode(500, new
            {
                Message = "Storage data directory is not configured."
            });
        }

        var dataRoot = Path.IsPathRooted(dataDirectory)
            ? Path.GetFullPath(dataDirectory)
            : Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                dataDirectory));

        var manifestDirectory = Path.Combine(
            dataRoot,
            "manifests");

        // A GUID route prevents arbitrary file path input.
        var manifestPath = Path.Combine(
            manifestDirectory,
            $"{runId:N}.json");

        if (!System.IO.File.Exists(manifestPath))
        {
            return NotFound(new
            {
                Message = "Pending cleanup run was not found."
            });
        }

        try
        {
            var manifest = TestDataManifest.Load(
                manifestPath,
                environmentId);

            // Additional consistency check:
            // file name must correspond to its stored RunId.
            if (!Guid.TryParseExact(
                    manifest.RunId, "N", out var storedRunId) ||
                storedRunId != runId)
            {
                return StatusCode(500, new
                {
                    Message = "Manifest validation failed."
                });
            }

            var items = manifest.UnresolvedItems
                .Select(item => new
                {
                    ItemType = item.ItemType,
                    ItemId = item.Id,
                    Status = item.Status.ToString()
                })
                .ToList();

            return Ok(new
            {
                RunId = manifest.RunId,
                CreatedAtUtc = manifest.CreatedAtUtc,
                RemainingCount = items.Count,
                Items = items
            });
        }
        catch (InvalidOperationException)
        {
            // Includes environment mismatch if Load()
            // uses InvalidOperationException for validation.
            return NotFound(new
            {
                Message = "Pending cleanup run was not found."
            });
        }
        catch (Exception ex) when (
            ex is IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            System.Text.Json.JsonException or
            ArgumentException)
        {
            // Do not expose file paths or internal exceptions.
            return StatusCode(500, new
            {
                Message = "Unable to load cleanup manifest."
            });
        }
    }



    /// <summary>
    /// Retrieves all benchmark results stored in memory.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(
        typeof(IEnumerable<BenchmarkStatusResponse>),
        StatusCodes.Status200OK)]
    public IActionResult GetAllBenchmarks()
    {
        var results = Jobs.Values
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();

        return Ok(results);
    }

    // =========================================================
    // 9. XML Helpers
    // =========================================================

    /// <summary>
    /// Finds an Item in an AML or SOAP response.
    /// </summary>
    private static XElement? FindItem(
        XDocument document,
        string itemType,
        string? itemId = null)
    {
        if (document.Root is null)
        {
            return null;
        }

        return document.Root.DescendantsAndSelf()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Item" &&
                string.Equals(
                    (string?)element.Attribute("type"),
                    itemType,
                    StringComparison.OrdinalIgnoreCase) &&
                (itemId is null ||
                 string.Equals(
                     GetItemId(element),
                     itemId,
                     StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets a property from an Aras Item element.
    /// </summary>
    private static string? GetProperty(
        XElement item,
        string propertyName)
    {
        return item.Elements()
            .FirstOrDefault(element =>
                string.Equals(
                    element.Name.LocalName,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    /// <summary>
    /// Gets an Item ID from its attribute or child element.
    /// </summary>
    private static string? GetItemId(XElement item)
    {
        return (string?)item.Attribute("id")
            ?? GetProperty(item, "id");
    }

    /// <summary>
    /// Extracts the returned Item ID from an AML response.
    /// </summary>
    private static string? ExtractItemId(
        string xmlPayload,
        string itemType)
    {
        var document = XDocument.Parse(xmlPayload);

        var item = FindItem(
            document,
            itemType);

        return item is null
            ? null
            : GetItemId(item);
    }

    /// <summary>
    /// Gets the SOAP fault code if one is present.
    /// </summary>
    private static string? GetFaultCode(XDocument document)
    {
        var fault = document.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Fault");

        if (fault is null)
        {
            return null;
        }

        return fault.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "faultcode")
            ?.Value ?? string.Empty;
    }

    /// <summary>
    /// Checks whether an Item is absent from a standard Read.
    /// </summary>
    private static bool IsItemAbsent(
        XDocument document,
        string itemType,
        string itemId)
    {
        var faultCode = GetFaultCode(document);

        if (faultCode is not null)
        {
            if (faultCode == "0")
            {
                return true;
            }

            throw new InvalidOperationException(
                $"Unable to verify Item {itemId}. " +
                $"Aras fault code: {faultCode}");
        }

        return FindItem(
            document,
            itemType,
            itemId) is null;
    }
}
