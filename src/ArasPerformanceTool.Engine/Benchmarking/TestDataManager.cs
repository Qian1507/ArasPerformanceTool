
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using ArasPerformanceTool.Engine.Models;

namespace ArasPerformanceTool.Engine.Benchmarking;

public class TestDataManager
{
    private readonly CreateWorkload _createWorkload;
    private readonly TestDataCleanupService _cleanupService;


   

    public TestDataManifest Manifest { get; }

    public TestDataManager(
        IArasClient arasClient,
        TestDataManifest? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(arasClient);

        _createWorkload = new CreateWorkload(arasClient);
        _cleanupService = new TestDataCleanupService(arasClient);

        Manifest = manifest ?? new TestDataManifest();
    }

    /// <summary>
    /// Creates test records and tracks their Aras IDs.
    /// </summary>
    public async Task<IReadOnlyList<string>> PrepareAsync(
        string serverUrl,
        string accessToken,
        string itemType,
        int count,
        Func<int, Dictionary<string, string>>? propertyFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemType);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var createdIds = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use custom properties or default Part/Document properties
            var properties = propertyFactory?.Invoke(i)
                ?? DefaultPropertyFactory(itemType, i);

            // Create the record in Aras
            var createResult = await _createWorkload.ExecuteAsync(
                serverUrl,
                accessToken,
                itemType,
                properties,
                cancellationToken);

            // Extract the ID of the newly created record
            var xml = XDocument.Parse(createResult);

            var itemElement = xml.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Item" &&
                    string.Equals(
                        (string?)e.Attribute("type"),
                        itemType,
                        StringComparison.OrdinalIgnoreCase));

            var id = (string?)itemElement?.Attribute("id");

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"Failed to extract ID for ItemType '{itemType}' at index {i}.");
            }

            // Track immediately so later cleanup can find the record
            Manifest.Track(itemType, id);
            createdIds.Add(id);
        }

        return createdIds;
    }

    /// <summary>
    /// Deletes tracked records and retains failed items for retry.
    /// </summary>
   
    
    public async Task<CleanupResult> CleanupAsync(
        string serverUrl,
        string accessToken,
        string? targetItemType = null,
        CancellationToken cancellationToken = default,
        string? expectedEnvironmentId = null)
    {
        // Require the current connection's environment identity.
        if (string.IsNullOrWhiteSpace(expectedEnvironmentId))
        {
            throw new InvalidOperationException(
                "Cleanup requires the current Aras EnvironmentId.");
        }

        // Delegate deletion and verification to the shared service.
        var result = await _cleanupService.CleanupAsync(
            Manifest,
            expectedEnvironmentId,
            serverUrl,
            accessToken,
            targetItemType,
            cancellationToken);

        // Preserve the existing CleanupResult contract.
        var failedIds = result.Items
            .Where(item =>
                item.FinalStatus == TrackedItemStatus.CleanupFailed)
            .Select(item => item.ItemId)
            .ToList();

        return new CleanupResult(
            result.Deleted,
            result.AlreadyMissing,
            failedIds);
    }





    // Default test properties for standard Part and Document
    private Dictionary<string, string> DefaultPropertyFactory(
        string itemType,
        int index)
    {
        var uniqueSuffix =
            $"{Manifest.RunId[..8]}-{index}-{Guid.NewGuid():N}"[..24];

        return new Dictionary<string, string>
        {
            ["item_number"] = uniqueSuffix,
            ["name"] = $"Benchmark-{itemType}-{index + 1}"
        };
    }
}

