
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Models;

namespace ArasPerformanceTool.Engine.Services;

/// <summary>
/// Result of a single-template Custom AML benchmark.
/// </summary>
public sealed record SingleAmlExecutionResult(
    BenchmarkReport Report,
    CleanupResult? CleanupSummary,
    string? CleanupError,
    string? SampleResponseXml,
    string RunId);

/// <summary>
/// Executes one Custom AML template with performance measurement.
/// </summary>
public sealed class CustomAmlBenchmarkService
{
    private readonly IArasClient _arasClient;

    public CustomAmlBenchmarkService(IArasClient arasClient)
    {
        _arasClient = arasClient;
    }

   
public async Task<SingleAmlExecutionResult> RunAsync(
    string rawAml,
    string workloadName,
    BenchmarkConfiguration config,
    string serverUrl,
    string accessToken,
    string environmentId,
    string runId,
    string? manifestDirectory = null,
    CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(rawAml);
    ArgumentNullException.ThrowIfNull(config);
    ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
    ArgumentException.ThrowIfNullOrWhiteSpace(runId);

    // Validate RunId before using it in a file path.
    if (!Guid.TryParseExact(runId, "N", out _))
    {
        throw new ArgumentException(
            "RunId must be a GUID in N format.",
            nameof(runId));
    }

    if (manifestDirectory is not null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            manifestDirectory);
    }

    var template = ParseSingleItem(rawAml);

    string action = ((string?)template.Attribute("action") ?? "")
        .Trim()
        .ToLowerInvariant();

    string itemType = ((string?)template.Attribute("type") ?? "")
        .Trim();

    if (string.IsNullOrWhiteSpace(itemType))
    {
        throw new ArgumentException(
            "Custom AML must specify an ItemType.");
    }

    if (!string.Equals(
        config.ItemType,
        itemType,
        StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException(
            "Benchmark configuration ItemType does not match AML.");
    }

    if (action is not ("get" or "add" or "edit" or "delete"))
    {
        throw new ArgumentException(
            $"Unsupported AML action: '{action}'.");
    }

    // Validate AML before sending any request to Aras.
    switch (action)
    {
        case "get":
            AmlParser.ValidateReadOnlyAml(rawAml);

            if (rawAml.Contains("${UNIQUE}") ||
                rawAml.Contains("${INDEX}"))
            {
                throw new ArgumentException(
                    "Dynamic placeholders are not supported " +
                    "in read-only benchmarks.");
            }
            break;

        case "add":
            ValidateSafeAdd(template);

            if (config.Concurrency != 1)
            {
                throw new ArgumentException(
                    "Custom add currently requires Concurrency = 1.");
            }

            // All writing operations must use persistent tracking.
            if (string.IsNullOrWhiteSpace(manifestDirectory))
            {
                throw new InvalidOperationException(
                    "Custom AML add requires a persistent manifest directory.");
            }
            break;

        case "edit":
        case "delete":
            // An explicit ID or test-looking name does not prove
            // ownership of the target item.
            throw new InvalidOperationException(
                $"Custom AML '{action}' is not enabled yet. " +
                "A benchmark-owned target must be verified " +
                "before update or delete can be executed.");
    }

    // Initialize the manifest before executing any AML.
    TestDataManifest manifest;

    if (manifestDirectory is null)
    {
        if (action == "add")
        {
            throw new InvalidOperationException(
                "Custom AML add requires a persistent manifest directory.");
        }
        // Only read-only AML may use in-memory tracking.
        manifest = new TestDataManifest(
            environmentId: environmentId,
            runId: runId);
    }
    else
    {
        // The directory must come from server-side configuration,
        // not directly from the client request.
        var directory = Path.GetFullPath(
            manifestDirectory);

        Directory.CreateDirectory(directory);

        var manifestPath = Path.Combine(
            directory,
            $"{runId}.json");

        // Prevent accidental replacement of an existing run.
        if (File.Exists(manifestPath))
        {
            throw new IOException(
                $"A manifest already exists for RunId {runId}.");
        }

        manifest = new TestDataManifest(
            environmentId: environmentId,
            filePath: manifestPath,
            runId: runId);
    }

    var dataManager = new TestDataManager(
        _arasClient,
        manifest);

    var runner = new BenchmarkRunner();

    BenchmarkReport? report = null;
    CleanupResult? cleanupSummary = null;
    string? cleanupError = null;
    string? sampleResponseXml = null;

    try
    {
        report = await runner.RunAsync(
            workloadName,
            config,
            async index =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string executableAml;

                if (action == "add")
                {
                    // Clone the original AML for every iteration.
                    var item = new XElement(template);

                    string marker =
                         $"{runId[..12]}_{index:D4}";

                    AddTestMarker(item, marker);

                    // Wrap the Item in AML.
                    executableAml = new XElement("AML", item)
                        .ToString(SaveOptions.DisableFormatting);
                }
                else
                {
                    executableAml =
                        AmlParser.PrepareExecutableAml(
                            rawAml,
                            index);
                }

                string responseXml =
                    await _arasClient.ExecuteAmlAsync(
                        serverUrl,
                        accessToken,
                        executableAml,
                        cancellationToken);

                if (action == "add")
                {
                    string? createdId =
                        ExtractCreatedId(
                            responseXml,
                            itemType);

                    if (string.IsNullOrWhiteSpace(createdId))
                    {
                        throw new InvalidOperationException(
                            "Aras response has no created Item ID. " +
                            $"RunId={runId}. " +
                            "Manual reconciliation may be needed.");
                    }

                    // Persist the successfully created item.
                    dataManager.Manifest.Track(
                        itemType,
                        createdId,
                        TestItemSource.CustomAml);
                }

                // Keep a sample response without making
                // an additional AML request.
                Interlocked.CompareExchange(
                    ref sampleResponseXml,
                    responseXml,
                    null);
            },
            cancellationToken);
    }
    finally
    {
        if (action == "add")
        {
            try
            {
                // Cleanup runs independently from benchmark
                // cancellation, with its own timeout.
                using var cleanupCts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(30));

                cleanupSummary =
                    await dataManager.CleanupAsync(
                        serverUrl,
                        accessToken,
                        itemType,
                        cleanupCts.Token,
                        expectedEnvironmentId: environmentId);
                
                if (cleanupSummary is null ||
                    cleanupSummary.FailedIds.Count > 0 ||
                    dataManager.Manifest.TotalCount > 0)
                {
                    cleanupError =
                        "Cleanup incomplete. " +
                        $"RunId={runId}. " +
                        "Some test items may remain.";
                }

            }
            catch (OperationCanceledException)
            {
                cleanupError =
                    "Cleanup timed out. " +
                    $"RunId={runId}. " +
                    "Residual test records must be checked.";
            }
            catch (Exception ex)
            {
                // Do not discard the benchmark report because
                // cleanup failed. Persisted manifest records
                // remain available for future manual cleanup.
                cleanupError =
                    $"Cleanup failed. RunId={runId}. " +
                    ex.Message;
            }
        }
    }

    if (report is null)
    {
        throw new InvalidOperationException(
            "Benchmark did not produce a report. " +
            $"RunId={runId}. " +
            (cleanupError ??
             "Check the benchmark execution log."));
    }

    return new SingleAmlExecutionResult(
        Report: report,
        CleanupSummary: cleanupSummary,
        CleanupError: cleanupError,
        SampleResponseXml: sampleResponseXml,
        RunId: runId);
}


    /// <summary>
    /// Accepts a single Item or AML containing one top-level Item.
    /// Nested get queries are preserved.
    /// </summary>
    private static XElement ParseSingleItem(string aml)
    {
        var document = ParseSafeXml(aml);

        var root = document.Root
            ?? throw new ArgumentException("AML is empty.");

        if (root.Name.LocalName == "Item")
        {
            return root;
        }

        if (root.Name.LocalName == "AML")
        {
            var elements = root.Elements().ToList();

            if (elements.Count == 1 &&
                elements[0].Name.LocalName == "Item")
            {
                return elements[0];
            }
        }

        throw new ArgumentException(
            "Custom AML must contain exactly one top-level Item.");
    }

    /// <summary>
    /// Initial safety boundary for simple test data creation.
    /// Complex relationships are intentionally not enabled here.
    /// </summary>
    private static void ValidateSafeAdd(XElement item)
    {
        string type = (string?)item.Attribute("type") ?? "";

        // Initial tested ItemTypes only.
        if (type is not ("Part" or "Document"))
        {
            throw new InvalidOperationException(
                "Custom add initially supports Part and Document. " +
                "Other ItemTypes require verified cleanup rules.");
        }

        // Do not allow explicit IDs, condition-based targeting,
        // or extra AML attributes in this initial mode.
        if (item.Attributes().Any(attribute =>
            attribute.Name.LocalName is not ("type" or "action")))
        {
            throw new InvalidOperationException(
                "Custom add must not specify ID, where, " +
                "or other Item attributes.");
        }

        // Only simple properties are supported here.
        // No relationships, nested items, or linked objects.
        if (item.Elements().Any(element =>
            element.HasElements ||
            element.HasAttributes ||
            element.Name.LocalName is not ("name" or "item_number")))
        {
            throw new InvalidOperationException(
                "Custom add currently supports only simple " +
                "name and item_number properties.");
        }

        if (!item.Elements().Any(element =>
            element.Name.LocalName == "item_number"))
        {
            throw new InvalidOperationException(
                "Custom add requires item_number for test identification.");
        }
    }

    /// <summary>
    /// Adds a unique test marker without modifying the user's template.
    /// </summary>
    
    private static void AddTestMarker(
        XElement item,
        string marker)
    {
        // Use a short unique value for Part Number.
        // Aras Part.item_number has a maximum length of 32.
        var shortMarker = marker.Length > 20
            ? marker[..20]
            : marker;

        foreach (var property in item.Elements())
        {
            if (property.Name.LocalName == "item_number")
            {
                // Keep the test identifier unique and within 32 characters.
                property.Value = $"TEST_{shortMarker}";
            }
            else if (property.Name.LocalName == "name")
            {
                // Keep the original descriptive name with a test marker.
                property.Value = $"TEST_{marker}";
            }
        }
    }


    /// <summary>
    /// Extracts the created top-level Item ID from the Aras response.
    /// </summary>
    private static string? ExtractCreatedId(
        string responseXml,
        string itemType)
    {
        if (string.IsNullOrWhiteSpace(responseXml))
        {
            return null;
        }

        var document = ParseSafeXml(responseXml);

        var item = document.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Item" &&
                string.Equals(
                    (string?)element.Attribute("type"),
                    itemType,
                    StringComparison.OrdinalIgnoreCase));

        return (string?)item?.Attribute("id")
            ?? item?.Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "id")?.Value;
    }

    /// <summary>
    /// Parses XML without enabling DTD or external entities.
    /// </summary>
    private static XDocument ParseSafeXml(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(
            new StringReader(xml),
            settings);

        return XDocument.Load(reader);
    }
}
