
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;

namespace ArasPerformanceTool.Engine.Benchmarking;

public class TestDataManager
{
    private readonly CreateWorkload _createWorkload;
    private readonly ReadWorkload _readWorkload;
    private readonly DeleteWorkload _deleteWorkload;

    public TestDataManifest Manifest { get; }

    public TestDataManager(
        IArasClient arasClient,
        TestDataManifest? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(arasClient);

        _createWorkload = new CreateWorkload(arasClient);
        _readWorkload = new ReadWorkload(arasClient);
        _deleteWorkload = new DeleteWorkload(arasClient);

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
        CancellationToken cancellationToken = default)
    {
        // Work from a snapshot of the current manifest
        var itemsToDelete = Manifest.Items
            .Where(x =>
                targetItemType is null ||
                string.Equals(
                    x.ItemType,
                    targetItemType,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        var deletedCount = 0;
        var alreadyMissingCount = 0;
        var failedIds = new List<string>();

        foreach (var item in itemsToDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Step 1: Request deletion
                var deleteResult = await _deleteWorkload.ExecuteAsync(
                    serverUrl,
                    accessToken,
                    item.ItemType,
                    item.Id,
                    cancellationToken);

                var deleteXml = XDocument.Parse(deleteResult);

                // Check whether Aras reported that the item was missing
                var deleteFault = GetFaultCode(deleteXml);

                if (deleteFault != null && deleteFault != "0")
                {
                    failedIds.Add(item.Id);
                    continue;
                }

                // Step 2: Verify the item can no longer be retrieved
                var readResult = await _readWorkload.ExecuteAsync(
                    serverUrl,
                    accessToken,
                    item.ItemType,
                    id: item.Id,
                    cancellationToken: cancellationToken);

                var readXml = XDocument.Parse(readResult);

                if (!IsItemMissing(readXml, item))
                {
                    failedIds.Add(item.Id);
                    continue;
                }

                // Step 3: Remove only confirmed missing records
                Manifest.Remove(item.ItemType, item.Id);

                if (deleteFault == "0")
                {
                    // The item was already missing
                    alreadyMissingCount++;
                }
                else
                {
                    // Delete was acknowledged and absence verified
                    deletedCount++;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // Do not hide intentional cancellation
                throw;
            }
            catch (Exception)
            {
                // Keep failed records in the manifest for retry
                failedIds.Add(item.Id);
            }
        }

        return new CleanupResult(
            deletedCount,
            alreadyMissingCount,
            failedIds);
    }

    // Return the SOAP Fault code, if a Fault exists
    private static string? GetFaultCode(XDocument xml)
    {
        var fault = xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Fault");

        if (fault is null)
        {
            return null;
        }

        return fault.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "faultcode")?.Value
            ?? fault.Elements()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "faultcode")?.Value
            ?? string.Empty;
    }

    // Verify a tracked item is no longer returned by Aras
    private static bool IsItemMissing(
        XDocument xml,
        TrackedItem item)
    {
        var faultCode = GetFaultCode(xml);

        // Only the known "no items found" code is acceptable
        if (faultCode != null)
        {
            return faultCode == "0";
        }

        // A successful query must not return the target record
        return !xml.Descendants().Any(e =>
            e.Name.LocalName == "Item" &&
            string.Equals(
                (string?)e.Attribute("type"),
                item.ItemType,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                (string?)e.Attribute("id"),
                item.Id,
                StringComparison.OrdinalIgnoreCase));
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

// Summary of a cleanup operation
public sealed record CleanupResult(
    int DeletedCount,
    int AlreadyMissingCount,
    IReadOnlyList<string> FailedIds);
